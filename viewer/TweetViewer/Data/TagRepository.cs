using TweetViewer.Models;

namespace TweetViewer.Data;

/// <summary>ユーザーへの独自タグ (tags / user_tags) の読み書き。</summary>
public sealed class TagRepository
{
    private readonly ViewerDatabase _db;

    public TagRepository(ViewerDatabase db) => _db = db;

    /// <summary>全タグ (name 昇順)。付与人数付き。</summary>
    public Task<List<TagRow>> GetAllAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT t.tag_id, t.name,
                       (SELECT COUNT(*) FROM user_tags ut WHERE ut.tag_id = t.tag_id) AS user_count
                FROM tags t
                ORDER BY t.name COLLATE NOCASE
                """;
            var rows = new List<TagRow>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new TagRow
                {
                    TagId = reader.GetInt64(0),
                    Name = reader.GetString(1),
                    UserCount = reader.GetInt64(2),
                });
            }
            return rows;
        }, ct);
    }

    /// <summary>username → 付与タグ ID リスト(サイドバー表示用に全件を1クエリで取得)。</summary>
    public Task<Dictionary<string, List<long>>> GetAssignmentsAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT username, tag_id FROM user_tags";
            var map = new Dictionary<string, List<long>>(StringComparer.OrdinalIgnoreCase);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var username = reader.GetString(0);
                if (!map.TryGetValue(username, out var list))
                    map[username] = list = new List<long>();
                list.Add(reader.GetInt64(1));
            }
            return map;
        }, ct);
    }

    /// <summary>タグ作成。同名 (大文字小文字無視) が既存ならその tag_id を返す。</summary>
    public async Task<long> AddAsync(string name)
    {
        await _db.WriteLock.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                using var conn = _db.OpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO tags (name) VALUES ($n) ON CONFLICT(name) DO NOTHING;
                    SELECT tag_id FROM tags WHERE name = $n;
                    """;
                cmd.Parameters.AddWithValue("$n", name);
                return (long)cmd.ExecuteScalar()!;
            }).ConfigureAwait(false);
        }
        finally
        {
            _db.WriteLock.Release();
        }
    }

    /// <summary>タグ付与。既に付いていれば何もしない。</summary>
    public Task AssignAsync(string username, long tagId) =>
        ExecuteWriteAsync("INSERT OR IGNORE INTO user_tags (username, tag_id) VALUES ($u, $t)",
            username, tagId);

    /// <summary>タグ解除。</summary>
    public Task UnassignAsync(string username, long tagId) =>
        ExecuteWriteAsync("DELETE FROM user_tags WHERE username = $u AND tag_id = $t",
            username, tagId);

    /// <summary>タグ削除 (全ユーザーからの付与も同一トランザクションで削除)。</summary>
    public async Task DeleteAsync(long tagId)
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
                cmd.CommandText = """
                    DELETE FROM user_tags WHERE tag_id = $t;
                    DELETE FROM tags WHERE tag_id = $t;
                    """;
                cmd.Parameters.AddWithValue("$t", tagId);
                cmd.ExecuteNonQuery();
                tx.Commit();
            }).ConfigureAwait(false);
        }
        finally
        {
            _db.WriteLock.Release();
        }
    }

    private async Task ExecuteWriteAsync(string sql, string username, long tagId)
    {
        await _db.WriteLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                using var conn = _db.OpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("$u", username);
                cmd.Parameters.AddWithValue("$t", tagId);
                cmd.ExecuteNonQuery();
            }).ConfigureAwait(false);
        }
        finally
        {
            _db.WriteLock.Release();
        }
    }
}
