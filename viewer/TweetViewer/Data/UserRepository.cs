using System.IO;
using TweetViewer.Models;

namespace TweetViewer.Data;

public sealed class UserRepository
{
    private readonly ViewerDatabase _db;

    public UserRepository(ViewerDatabase db) => _db = db;

    /// <summary>ユーザーアーカイブのみ (検索バケット searches/ は除外)。</summary>
    public Task<List<UserRow>> GetAllAsync(CancellationToken ct = default) =>
        QueryAsync(includeSearches: false, ct);

    /// <summary>検索バケット (username = searches/&lt;slug&gt;) のみ。</summary>
    public Task<List<UserRow>> GetSearchBucketsAsync(CancellationToken ct = default) =>
        QueryAsync(includeSearches: true, ct);

    private Task<List<UserRow>> QueryAsync(bool includeSearches, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            var cond = includeSearches ? "LIKE" : "NOT LIKE";
            cmd.CommandText = $"""
                SELECT u.username, u.display_name, u.icon_url, u.added_at, u.last_import_at, u.jsonl_offset,
                       (SELECT COUNT(*) FROM tweets t WHERE t.username = u.username) AS tweet_count,
                       (SELECT COUNT(*) FROM tweets t
                        LEFT JOIN read_state r ON r.tweet_id = t.tweet_id
                        WHERE t.username = u.username AND r.tweet_id IS NULL) AS unread_count
                FROM users u
                WHERE u.username {cond} 'searches/%'
                ORDER BY u.username COLLATE NOCASE
                """;
            var rows = new List<UserRow>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new UserRow
                {
                    Username = reader.GetString(0),
                    DisplayName = reader.IsDBNull(1) ? null : reader.GetString(1),
                    IconUrl = reader.IsDBNull(2) ? null : reader.GetString(2),
                    AddedAt = reader.GetString(3),
                    LastImportAt = reader.IsDBNull(4) ? null : reader.GetString(4),
                    JsonlOffset = reader.GetInt64(5),
                    TweetCount = reader.GetInt64(6),
                    UnreadCount = reader.GetInt64(7),
                });
            }
            return rows;
        }, ct);
    }

    /// <summary>ユーザー登録 + data/&lt;username&gt;/ の作成。既存なら何もしない。</summary>
    public async Task<bool> AddAsync(string username)
    {
        await _db.WriteLock.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                using var conn = _db.OpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT OR IGNORE INTO users (username, added_at) VALUES ($u, $t)
                    """;
                cmd.Parameters.AddWithValue("$u", username);
                cmd.Parameters.AddWithValue("$t", JsonlImporter.UtcNow());
                var added = cmd.ExecuteNonQuery() > 0;
                Directory.CreateDirectory(_db.UserDir(username));
                return added;
            }).ConfigureAwait(false);
        }
        finally
        {
            _db.WriteLock.Release();
        }
    }

    /// <summary>data/ 直下の tweets.jsonl を持つディレクトリを users に自動登録する。</summary>
    public async Task<int> RegisterExistingDataDirsAsync()
    {
        if (!Directory.Exists(_db.DataDir))
            return 0;
        var found = Directory.EnumerateDirectories(_db.DataDir)
            .Where(dir => File.Exists(Path.Combine(dir, "tweets.jsonl")))
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToList();
        var added = 0;
        foreach (var username in found)
        {
            if (await AddAsync(username).ConfigureAwait(false))
                added++;
        }
        return added;
    }

    /// <summary>data/searches/ 直下の検索バケットを users に自動登録する (CLI で作った検索も GUI に出す)。</summary>
    public async Task<int> RegisterExistingSearchDirsAsync()
    {
        var searchesDir = Path.Combine(_db.DataDir, "searches");
        if (!Directory.Exists(searchesDir))
            return 0;
        var found = Directory.EnumerateDirectories(searchesDir)
            .Where(dir => File.Exists(Path.Combine(dir, "tweets.jsonl")))
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToList();
        var added = 0;
        foreach (var name in found)
        {
            if (await AddAsync("searches/" + name).ConfigureAwait(false))
                added++;
        }
        return added;
    }

    /// <summary>検索バケットを DB (tweets / tweet_media / users) から削除する。read_state の孤児行は無害なので残す。</summary>
    public async Task DeleteBucketAsync(string bucketId)
    {
        await _db.WriteLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                using var conn = _db.OpenConnection();
                using var tx = conn.BeginTransaction();
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                // tweet_media は tweet_id 単位で全バケット共有のため、
                // 行削除後にどこからも参照されなくなったものだけ消す
                cmd.CommandText = """
                    DELETE FROM tweets WHERE username = $u;
                    DELETE FROM tweet_media WHERE tweet_id NOT IN (SELECT tweet_id FROM tweets);
                    DELETE FROM user_tags WHERE username = $u;
                    DELETE FROM users WHERE username = $u;
                    """;
                cmd.Parameters.AddWithValue("$u", bucketId);
                cmd.ExecuteNonQuery();
                tx.Commit();
            }).ConfigureAwait(false);
        }
        finally
        {
            _db.WriteLock.Release();
        }
    }
}
