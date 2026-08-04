using System.IO;
using Microsoft.Data.Sqlite;
using TweetViewer.Data;

namespace TweetViewer.Tests;

/// <summary>旧スキーマの viewer.db を開いた際の逐次マイグレーション検証。</summary>
public sealed class SchemaMigrationTests : IDisposable
{
    private readonly string _dataDir;

    public SchemaMigrationTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "SubakoTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dataDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string DbPath => Path.Combine(_dataDir, "viewer.db");

    /// <summary>v1 の DDL で DB を作り、users / tweets / read_state に1行ずつ入れる。</summary>
    private void CreateV1Database()
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE schema_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE users (
              username TEXT PRIMARY KEY COLLATE NOCASE, display_name TEXT,
              added_at TEXT NOT NULL, last_import_at TEXT,
              jsonl_offset INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE tweets (
              tweet_id TEXT PRIMARY KEY, id_int INTEGER NOT NULL, username TEXT NOT NULL,
              created_at_utc TEXT NOT NULL, sort_key INTEGER NOT NULL, tweet_type INTEGER NOT NULL,
              full_text TEXT NOT NULL, lang TEXT, in_reply_to_username TEXT,
              rt_username TEXT, rt_display_name TEXT, rt_text TEXT,
              quoted_username TEXT, quoted_display_name TEXT, quoted_text TEXT,
              like_count INTEGER NOT NULL DEFAULT 0, retweet_count INTEGER NOT NULL DEFAULT 0,
              reply_count INTEGER NOT NULL DEFAULT 0, view_count INTEGER NOT NULL DEFAULT 0,
              media_count INTEGER NOT NULL DEFAULT 0,
              raw_offset INTEGER NOT NULL, raw_length INTEGER NOT NULL
            ) WITHOUT ROWID;
            CREATE TABLE tweet_media (
              tweet_id TEXT NOT NULL, idx INTEGER NOT NULL, source_url TEXT, ext TEXT NOT NULL,
              PRIMARY KEY (tweet_id, idx)
            ) WITHOUT ROWID;
            CREATE TABLE read_state (
              tweet_id TEXT PRIMARY KEY, username TEXT NOT NULL, read_at TEXT NOT NULL
            ) WITHOUT ROWID;
            INSERT INTO schema_meta VALUES ('schema_version', '1');
            INSERT INTO users (username, display_name, added_at, jsonl_offset)
              VALUES ('alice', 'Alice', '2026-01-01T00:00:00Z', 12345);
            INSERT INTO tweets (tweet_id, id_int, username, created_at_utc, sort_key,
                                tweet_type, full_text, raw_offset, raw_length)
              VALUES ('1', 1, 'alice', '2026-01-01T00:00:00Z', 100, 0, 'hello', 0, 10);
            INSERT INTO tweet_media VALUES ('1', 1, 'http://x/img.jpg', 'jpg');
            INSERT INTO read_state VALUES ('1', 'alice', '2026-01-02T00:00:00Z');
            """;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void V1DatabaseMigratesToLatestPreservingAuthoritativeData()
    {
        CreateV1Database();
        SqliteConnection.ClearAllPools();

        var db = new ViewerDatabase(_dataDir);
        db.EnsureCreated();

        using var conn = db.OpenConnection();
        object? Scalar(string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return cmd.ExecuteScalar();
        }

        // 最新バージョンまで逐次マイグレーションされている
        Assert.Equal(
            ViewerDatabase.SchemaVersion.ToString(),
            (string)Scalar("SELECT value FROM schema_meta WHERE key='schema_version'")!);
        // 新列が存在する (SELECT が例外にならない)
        Assert.Equal(0L, Scalar("SELECT COUNT(icon_url) FROM users"));
        Assert.Equal(0L, Scalar("SELECT COUNT(rt_icon_url) + COUNT(quoted_icon_url) FROM tweets"));
        Assert.Equal(0L, Scalar("SELECT COUNT(origin) FROM tweet_media"));
        // 派生データはリセットされ、再取込のためオフセットも 0
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM tweets"));
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM tweet_media"));
        Assert.Equal(0L, Scalar("SELECT jsonl_offset FROM users WHERE username='alice'"));
        // 正データ (users / read_state) は保全
        Assert.Equal("Alice", (string)Scalar("SELECT display_name FROM users WHERE username='alice'")!);
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM read_state"));
        // v4 で追加されたタグテーブルが存在する (SELECT が例外にならない)
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM tags"));
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM user_tags"));
        // v5 の author 列が存在する
        Assert.Equal(0L, Scalar("SELECT COUNT(author_username) FROM tweets"));
    }

    /// <summary>v4 スキーマ相当 (tweets は tweet_id 単独 PK・author 列なし) の DB を作る。</summary>
    private void CreateV4Database()
    {
        var setup = new ViewerDatabase(_dataDir);
        setup.EnsureCreated();
        using var conn = setup.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DROP TABLE tweets;
            CREATE TABLE tweets (
              tweet_id TEXT PRIMARY KEY, id_int INTEGER NOT NULL, username TEXT NOT NULL,
              created_at_utc TEXT NOT NULL, sort_key INTEGER NOT NULL, tweet_type INTEGER NOT NULL,
              full_text TEXT NOT NULL, lang TEXT, in_reply_to_username TEXT,
              rt_username TEXT, rt_display_name TEXT, rt_text TEXT, rt_icon_url TEXT,
              quoted_username TEXT, quoted_display_name TEXT, quoted_text TEXT, quoted_icon_url TEXT,
              like_count INTEGER NOT NULL DEFAULT 0, retweet_count INTEGER NOT NULL DEFAULT 0,
              reply_count INTEGER NOT NULL DEFAULT 0, view_count INTEGER NOT NULL DEFAULT 0,
              media_count INTEGER NOT NULL DEFAULT 0,
              raw_offset INTEGER NOT NULL, raw_length INTEGER NOT NULL
            ) WITHOUT ROWID;
            UPDATE schema_meta SET value = '4' WHERE key = 'schema_version';
            INSERT INTO users (username, display_name, added_at, jsonl_offset)
              VALUES ('alice', 'Alice', '2026-01-01T00:00:00Z', 12345);
            INSERT INTO tweets (tweet_id, id_int, username, created_at_utc, sort_key,
                                tweet_type, full_text, raw_offset, raw_length)
              VALUES ('1', 1, 'alice', '2026-01-01T00:00:00Z', 100, 0, 'hello', 0, 10);
            INSERT INTO tweet_media VALUES ('1', 1, 'http://x/img.jpg', 'jpg', 0);
            INSERT INTO read_state VALUES ('1', 'alice', '2026-01-02T00:00:00Z');
            INSERT INTO tags (name) VALUES ('絵師');
            INSERT INTO user_tags VALUES ('alice', 1);
            """;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void V4DatabaseMigratesToV5ResettingDerivedDataOnly()
    {
        CreateV4Database();
        SqliteConnection.ClearAllPools();

        var db = new ViewerDatabase(_dataDir);
        db.EnsureCreated();

        using var conn = db.OpenConnection();
        object? Scalar(string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return cmd.ExecuteScalar();
        }

        Assert.Equal(
            ViewerDatabase.SchemaVersion.ToString(),
            (string)Scalar("SELECT value FROM schema_meta WHERE key='schema_version'")!);
        // 派生データはリセットされ再取込のためオフセットも 0
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM tweets"));
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM tweet_media"));
        Assert.Equal(0L, Scalar("SELECT jsonl_offset FROM users WHERE username='alice'"));
        // author 列が存在する
        Assert.Equal(0L, Scalar("SELECT COUNT(author_username) FROM tweets"));
        // 正データ (users / read_state / tags) は保全
        Assert.Equal("Alice", (string)Scalar("SELECT display_name FROM users WHERE username='alice'")!);
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM read_state"));
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM tags"));
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM user_tags"));
    }

    [Fact]
    public void CompositePrimaryKeyAllowsSameTweetInArchiveAndSearchBucket()
    {
        var db = new ViewerDatabase(_dataDir);
        db.EnsureCreated();

        using var conn = db.OpenConnection();
        using var cmd = conn.CreateCommand();
        // 同一 tweet_id をアーカイブと検索バケットの両方に格納できる (v5 複合 PK)
        cmd.CommandText = """
            INSERT INTO tweets (tweet_id, id_int, username, created_at_utc, sort_key,
                                tweet_type, full_text, raw_offset, raw_length)
              VALUES ('1', 1, 'alice', '2026-01-01T00:00:00Z', 100, 0, 'hello', 0, 10);
            INSERT INTO tweets (tweet_id, id_int, username, created_at_utc, sort_key,
                                tweet_type, full_text, raw_offset, raw_length)
              VALUES ('1', 1, 'searches/kw-12345678', '2026-01-01T00:00:00Z', 100, 0, 'hello', 0, 10);
            """;
        cmd.ExecuteNonQuery();
        using var count = conn.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM tweets WHERE tweet_id = '1'";
        Assert.Equal(2L, count.ExecuteScalar());
    }

    /// <summary>
    /// 対応版より新しい DB は専用例外で拒否する (Mac 版と同じ扱い。
    /// docs/trending-jp.md §10.2)。汎用例外だとアプリがクラッシュ扱いになる。
    /// </summary>
    [Fact]
    public void SchemaNewerThanSupportedIsRejected()
    {
        CreateFutureDatabase(ViewerDatabase.SchemaVersion + 1);

        var db = new ViewerDatabase(_dataDir);
        var ex = Assert.Throws<SchemaTooNewException>(() => db.EnsureCreated());

        Assert.Equal(ViewerDatabase.SchemaVersion + 1, ex.StoredVersion);
        Assert.Equal(ViewerDatabase.SchemaVersion, ex.SupportedVersion);
    }

    /// <summary>
    /// 版チェックは DDL より前。後だと、将来 tweets の列を削った版の DB に対して
    /// CREATE/ALTER が先に走り "no such column" になる (docs/trending-jp.md §10.2)。
    /// ここでは「拒否した DB に何も書き足していない」ことで順序を確かめる。
    /// </summary>
    [Fact]
    public void SchemaTooNewIsDetectedBeforeAnyDdlRuns()
    {
        CreateFutureDatabase(ViewerDatabase.SchemaVersion + 1);

        var db = new ViewerDatabase(_dataDir);
        Assert.Throws<SchemaTooNewException>(() => db.EnsureCreated());
        SqliteConnection.ClearAllPools();

        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        // 未来版にしか無い列は残り、このアプリ版の DDL が作るテーブルは増えていない
        cmd.CommandText = """
            SELECT COUNT(*) FROM sqlite_master
             WHERE type = 'table' AND name IN ('users', 'tags', 'user_tags', 'tweets')
            """;
        Assert.Equal(1L, cmd.ExecuteScalar());
    }

    /// <summary>schema_meta だけを持つ「未来版」の DB。tweets 等は意図的に作らない。</summary>
    private void CreateFutureDatabase(int version)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            CREATE TABLE schema_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            INSERT INTO schema_meta(key, value) VALUES ('schema_version', '{version}');
            CREATE TABLE users (
              username TEXT PRIMARY KEY COLLATE NOCASE, display_name TEXT,
              added_at TEXT NOT NULL, last_import_at TEXT,
              jsonl_offset INTEGER NOT NULL DEFAULT 0
            );
            """;
        cmd.ExecuteNonQuery();
        SqliteConnection.ClearAllPools();
    }
}
