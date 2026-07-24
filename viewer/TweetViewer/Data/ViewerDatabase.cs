using System.IO;
using Microsoft.Data.Sqlite;

namespace TweetViewer.Data;

/// <summary>
/// data/viewer.db の接続とスキーマを管理する。
/// tweets / tweet_media は JSONL から再構築可能な派生データ、
/// users / read_state / tags / user_tags は正データ(docs/data-layer.md 参照)。
/// </summary>
public sealed class ViewerDatabase
{
    public const int SchemaVersion = 4;

    public string DataDir { get; }
    public string DbPath { get; }

    private readonly string _connectionString;

    public ViewerDatabase(string dataDir)
    {
        DataDir = dataDir;
        Directory.CreateDirectory(dataDir);
        DbPath = Path.Combine(dataDir, "viewer.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();
    }

    /// <summary>書込は呼び出し側で直列化する(JsonlImporter / ReadMarkQueue が唯一の書き手)。</summary>
    public SemaphoreSlim WriteLock { get; } = new(1, 1);

    public SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout = 5000; PRAGMA synchronous = NORMAL;";
        cmd.ExecuteNonQuery();
        return conn;
    }

    public void EnsureCreated()
    {
        using var conn = OpenConnection();
        using (var wal = conn.CreateCommand())
        {
            wal.CommandText = "PRAGMA journal_mode = WAL;";
            wal.ExecuteScalar();
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_meta (
              key   TEXT PRIMARY KEY,
              value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS users (
              username       TEXT PRIMARY KEY COLLATE NOCASE,
              display_name   TEXT,
              icon_url       TEXT,
              added_at       TEXT NOT NULL,
              last_import_at TEXT,
              jsonl_offset   INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS tweets (
              tweet_id             TEXT PRIMARY KEY,
              id_int               INTEGER NOT NULL,
              username             TEXT NOT NULL,
              created_at_utc       TEXT NOT NULL,
              sort_key             INTEGER NOT NULL,
              tweet_type           INTEGER NOT NULL,
              full_text            TEXT NOT NULL,
              lang                 TEXT,
              in_reply_to_username TEXT,
              rt_username          TEXT,
              rt_display_name      TEXT,
              rt_text              TEXT,
              rt_icon_url          TEXT,
              quoted_username      TEXT,
              quoted_display_name  TEXT,
              quoted_text          TEXT,
              quoted_icon_url      TEXT,
              like_count           INTEGER NOT NULL DEFAULT 0,
              retweet_count        INTEGER NOT NULL DEFAULT 0,
              reply_count          INTEGER NOT NULL DEFAULT 0,
              view_count           INTEGER NOT NULL DEFAULT 0,
              media_count          INTEGER NOT NULL DEFAULT 0,
              raw_offset           INTEGER NOT NULL,
              raw_length           INTEGER NOT NULL
            ) WITHOUT ROWID;

            CREATE INDEX IF NOT EXISTS ix_tweets_user_sort
              ON tweets(username, sort_key DESC, id_int DESC);

            CREATE TABLE IF NOT EXISTS tweet_media (
              tweet_id   TEXT NOT NULL,
              idx        INTEGER NOT NULL,
              source_url TEXT,
              ext        TEXT NOT NULL,
              origin     INTEGER NOT NULL DEFAULT 0,
              PRIMARY KEY (tweet_id, idx)
            ) WITHOUT ROWID;

            CREATE TABLE IF NOT EXISTS read_state (
              tweet_id TEXT PRIMARY KEY,
              username TEXT NOT NULL,
              read_at  TEXT NOT NULL
            ) WITHOUT ROWID;

            CREATE INDEX IF NOT EXISTS ix_read_state_user ON read_state(username);

            CREATE TABLE IF NOT EXISTS tags (
              tag_id INTEGER PRIMARY KEY,
              name   TEXT NOT NULL UNIQUE COLLATE NOCASE
            );

            CREATE TABLE IF NOT EXISTS user_tags (
              username TEXT NOT NULL COLLATE NOCASE,
              tag_id   INTEGER NOT NULL,
              PRIMARY KEY (username, tag_id)
            ) WITHOUT ROWID;

            CREATE INDEX IF NOT EXISTS ix_user_tags_tag ON user_tags(tag_id);

            INSERT OR IGNORE INTO schema_meta(key, value) VALUES ('schema_version', '4');
            """;
        cmd.ExecuteNonQuery();

        using var ver = conn.CreateCommand();
        ver.CommandText = "SELECT value FROM schema_meta WHERE key = 'schema_version'";
        var stored = Convert.ToInt32((string)ver.ExecuteScalar()!);
        if (stored > SchemaVersion)
        {
            throw new InvalidOperationException(
                $"viewer.db のスキーマバージョン {stored} はこのアプリ (v{SchemaVersion}) より新しいため開けません。");
        }
        // 逐次マイグレーション (派生データはリセット、users / read_state は保全)
        if (stored < 2)
            Migrate(conn, 2, """
                ALTER TABLE users  ADD COLUMN icon_url TEXT;
                ALTER TABLE tweets ADD COLUMN rt_icon_url TEXT;
                ALTER TABLE tweets ADD COLUMN quoted_icon_url TEXT;
                """);
        if (stored < 3)
            Migrate(conn, 3, """
                ALTER TABLE tweet_media ADD COLUMN origin INTEGER NOT NULL DEFAULT 0;
                """);
        if (stored < 4)
        {
            // tags / user_tags は上の CREATE TABLE IF NOT EXISTS で作成済み。
            // テーブル追加のみのため派生データのリセットは行わない。
            using var cmd4 = conn.CreateCommand();
            cmd4.CommandText = "UPDATE schema_meta SET value = '4' WHERE key = 'schema_version'";
            cmd4.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// 列追加 → 派生データ (tweets/tweet_media) をリセットして次回取込で
    /// 新列を埋める、の定型マイグレーション。
    /// </summary>
    private static void Migrate(SqliteConnection conn, int toVersion, string alterSql)
    {
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = alterSql + $"""
            DELETE FROM tweet_media;
            DELETE FROM tweets;
            UPDATE users SET jsonl_offset = 0;
            UPDATE schema_meta SET value = '{toVersion}' WHERE key = 'schema_version';
            """;
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    public string UserDir(string username) => Path.Combine(DataDir, username);
    public string JsonlPath(string username) => Path.Combine(UserDir(username), "tweets.jsonl");
    public string ImagesDir(string username) => Path.Combine(UserDir(username), "images");
}
