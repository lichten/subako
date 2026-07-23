using System.IO;
using TweetViewer.Models;

namespace TweetViewer.Data;

public sealed class UserRepository
{
    private readonly ViewerDatabase _db;

    public UserRepository(ViewerDatabase db) => _db = db;

    public Task<List<UserRow>> GetAllAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT u.username, u.display_name, u.added_at, u.last_import_at, u.jsonl_offset,
                       (SELECT COUNT(*) FROM tweets t WHERE t.username = u.username) AS tweet_count,
                       (SELECT COUNT(*) FROM tweets t
                        LEFT JOIN read_state r ON r.tweet_id = t.tweet_id
                        WHERE t.username = u.username AND r.tweet_id IS NULL) AS unread_count
                FROM users u
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
                    AddedAt = reader.GetString(2),
                    LastImportAt = reader.IsDBNull(3) ? null : reader.GetString(3),
                    JsonlOffset = reader.GetInt64(4),
                    TweetCount = reader.GetInt64(5),
                    UnreadCount = reader.GetInt64(6),
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
}
