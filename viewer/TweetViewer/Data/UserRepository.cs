using System.IO;
using Microsoft.Data.Sqlite;
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

    /// <summary>
    /// ユーザーをまとめて登録する (フォロー一括登録用)。AddAsync を N 回呼ぶと
    /// N 回の WriteLock 取得 + N 本の接続になるため、DeleteArchiveAsync と同じ
    /// 「1 WriteLock 内の 1 トランザクション」でまとめる。
    ///
    /// - display_name は新規行の初期値だけ (INSERT OR IGNORE なので既存行は不触)。
    ///   取込後は JsonlImporter が最新ツイート由来で上書きする。
    /// - icon_url は入れない。数千件を一度に入れると RefreshUsersAsync が
    ///   同数のアイコン DL を一斉に走らせて全部失敗させる (docs/data-layer.md §1.7)。
    /// - data/&lt;username&gt;/ は作らない。取込は JSONL 不在なら即 return し
    ///   (JsonlImporter.ImportUserAsync)、実際の取得時に Python 側が mkdir する。
    ///   数千個の空フォルダは毎起動の RegisterExistingDataDirsAsync を重くするだけ。
    /// </summary>
    /// <returns>実際に新規登録された username (入力順)。既存だったものは含まない。</returns>
    public async Task<List<string>> AddManyAsync(
        IReadOnlyList<(string Username, string? DisplayName)> users)
    {
        if (users.Count == 0)
            return [];
        await _db.WriteLock.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var added = new List<string>();
                using var conn = _db.OpenConnection();
                using var tx = conn.BeginTransaction();
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT OR IGNORE INTO users (username, display_name, added_at)
                    VALUES ($u, $d, $t)
                    """;
                var pu = cmd.Parameters.Add("$u", SqliteType.Text);
                var pd = cmd.Parameters.Add("$d", SqliteType.Text);
                cmd.Parameters.AddWithValue("$t", JsonlImporter.UtcNow());
                cmd.Prepare();
                foreach (var (username, displayName) in users)
                {
                    pu.Value = username;
                    pd.Value = (object?)displayName ?? DBNull.Value;
                    if (cmd.ExecuteNonQuery() > 0)
                        added.Add(username);
                }
                tx.Commit();
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

    /// <summary>
    /// ユーザーまたは検索バケットを DB (tweets / tweet_media / user_tags / users) から削除する。
    /// read_state は tweet_id 単位で全アーカイブ共通のため消さない (孤児行は無害)。
    /// </summary>
    public async Task DeleteArchiveAsync(string username)
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
                cmd.Parameters.AddWithValue("$u", username);
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
