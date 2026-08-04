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
    public const int SchemaVersion = 7;

    /// <summary>
    /// tweets テーブルの DDL。EnsureCreated と v5 マイグレーション (DROP→CREATE) で共用。
    /// username はアーカイブ所有ユーザーまたは検索バケット ID (searches/&lt;slug&gt;)、
    /// author_* は実投稿者 (JSONL の user オブジェクト由来)。
    /// 同一ツイートがアーカイブと検索バケットの両方に存在し得るため複合主キー。
    /// </summary>
    private const string TweetsDdl = """
        CREATE TABLE IF NOT EXISTS tweets (
          tweet_id             TEXT NOT NULL,
          id_int               INTEGER NOT NULL,
          username             TEXT NOT NULL,
          author_username      TEXT,
          author_display_name  TEXT,
          author_icon_url      TEXT,
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
          raw_length           INTEGER NOT NULL,
          PRIMARY KEY (username, tweet_id)
        ) WITHOUT ROWID;

        CREATE INDEX IF NOT EXISTS ix_tweets_user_sort
          ON tweets(username, sort_key DESC, id_int DESC);
        """;

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
        // 版チェックは DDL より前に行う。後にすると、将来 tweets の列を削除・改名した
        // 版の DB に対して CREATE/ALTER が先に走り、分かりやすいメッセージではなく
        // 生の "no such column" になってしまう (docs/trending-jp.md §10.2)。
        // Mac 版は同一トランザクション内で判定してロールバックする。
        var existing = TryReadSchemaVersion(conn);
        if (existing > SchemaVersion)
            throw new SchemaTooNewException(existing.Value, SchemaVersion);

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

            """ + TweetsDdl + """

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

            INSERT OR IGNORE INTO schema_meta(key, value) VALUES ('schema_version', '7');
            """;
        cmd.ExecuteNonQuery();

        var stored = existing ?? SchemaVersion;
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
        if (stored < 5)
        {
            // author 3列の追加 + 主キーを (username, tweet_id) に変更するためテーブルを作り直す。
            // 派生データはリセットし次回取込で再構築 (ローカル処理のみで API 消費なし)。
            using var tx = conn.BeginTransaction();
            using var cmd5 = conn.CreateCommand();
            cmd5.Transaction = tx;
            cmd5.CommandText = "DROP TABLE tweets;\n" + TweetsDdl + """

                DELETE FROM tweet_media;
                UPDATE users SET jsonl_offset = 0;
                UPDATE schema_meta SET value = '5' WHERE key = 'schema_version';
                """;
            cmd5.ExecuteNonQuery();
            tx.Commit();
        }
        if (stored < 6)
        {
            // DDL 変更なし。引用先・RT元の画像を full_text から拾えるようパーサを
            // 変えたので、派生データを捨てて次回取込で作り直す (API 消費なし)。
            Migrate(conn, 6, "");
        }
        if (stored < 7)
        {
            // DDL 変更なし。RT のカウント (いいね等) を RT元から拾うようパーサを
            // 変えたので、派生データを捨てて次回取込で作り直す (API 消費なし)。
            Migrate(conn, 7, "");
        }
    }

    /// <summary>
    /// WAL の内容を viewer.db 本体へ書き戻して -wal を切り詰める。終了時に呼ぶ。
    /// クラウド同期 (Google Drive 等) は viewer.db / -wal / -shm をファイル単位で
    /// 別々に同期するため、WAL を残したまま終了すると相手側が古いスナップショットを
    /// 読む・不整合な組で同期される (docs/mac-port-notes.md §3)。
    /// 他プロセスが読んでいる等で切り詰められなくても終了は妨げない。
    /// </summary>
    public void Checkpoint()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        cmd.ExecuteScalar();
    }

    /// <summary>
    /// 既存 DB の schema_version。DB が新規 (schema_meta が無い / 行が無い) なら null。
    /// DDL を一切実行しないこと — 版が新しすぎる DB を触らずに判定するのが目的。
    /// </summary>
    private static int? TryReadSchemaVersion(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM schema_meta WHERE key = 'schema_version'";
        try
        {
            return cmd.ExecuteScalar() is string s && int.TryParse(s, out var v) ? v : null;
        }
        catch (SqliteException)
        {
            // schema_meta が無い DB (新規作成) では no such table になる
            return null;
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
