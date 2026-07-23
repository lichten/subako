using Microsoft.Data.Sqlite;
using TweetViewer.Data;

namespace TweetViewer.Services;

/// <summary>
/// スクロール既読化のバッチ書込。1秒毎 or 100件到達で read_state に
/// 1トランザクションで INSERT する。クローズ/ユーザー切替時は FlushAsync を呼ぶこと。
/// </summary>
public sealed class ReadMarkQueue : IAsyncDisposable
{
    private const int FlushThreshold = 100;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);

    private readonly ViewerDatabase _db;
    private readonly Dictionary<string, string> _pending = new();   // tweet_id -> username
    private readonly object _lock = new();
    private readonly PeriodicTimer _timer;
    private readonly Task _loop;
    private readonly CancellationTokenSource _cts = new();

    public ReadMarkQueue(ViewerDatabase db)
    {
        _db = db;
        _timer = new PeriodicTimer(FlushInterval);
        _loop = Task.Run(FlushLoopAsync);
    }

    public void Enqueue(string tweetId, string username)
    {
        var flushNow = false;
        lock (_lock)
        {
            _pending[tweetId] = username;
            flushNow = _pending.Count >= FlushThreshold;
        }
        if (flushNow)
            _ = FlushAsync();
    }

    private async Task FlushLoopAsync()
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(_cts.Token).ConfigureAwait(false))
                await FlushAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task FlushAsync()
    {
        List<KeyValuePair<string, string>> batch;
        lock (_lock)
        {
            if (_pending.Count == 0)
                return;
            batch = _pending.ToList();
            _pending.Clear();
        }

        await _db.WriteLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                using var conn = _db.OpenConnection();
                using var tx = conn.BeginTransaction();
                var now = JsonlImporter.UtcNow();
                foreach (var (tweetId, username) in batch)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        INSERT OR IGNORE INTO read_state (tweet_id, username, read_at)
                        VALUES ($id, $u, $t)
                        """;
                    cmd.Parameters.AddWithValue("$id", tweetId);
                    cmd.Parameters.AddWithValue("$u", username);
                    cmd.Parameters.AddWithValue("$t", now);
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
            }).ConfigureAwait(false);
        }
        catch (SqliteException)
        {
            // 失敗分は再キュー(次のフラッシュで再試行)
            lock (_lock)
            {
                foreach (var (tweetId, username) in batch)
                    _pending.TryAdd(tweetId, username);
            }
        }
        finally
        {
            _db.WriteLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _timer.Dispose();
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        await FlushAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}
