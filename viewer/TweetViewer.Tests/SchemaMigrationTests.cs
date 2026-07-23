using System.IO;
using Microsoft.Data.Sqlite;
using TweetViewer.Data;

namespace TweetViewer.Tests;

/// <summary>v1 スキーマの viewer.db を開いた際の v2 マイグレーション検証。</summary>
public sealed class SchemaMigrationTests : IDisposable
{
    private readonly string _dataDir;

    public SchemaMigrationTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "TweetViewerTests", Guid.NewGuid().ToString("N"));
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
    public void V1DatabaseMigratesToV2PreservingAuthoritativeData()
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

        // バージョンが 2 に更新されている
        Assert.Equal("2", (string)Scalar("SELECT value FROM schema_meta WHERE key='schema_version'")!);
        // 新列が存在する (SELECT が例外にならない)
        Assert.Equal(0L, Scalar("SELECT COUNT(icon_url) FROM users"));
        Assert.Equal(0L, Scalar("SELECT COUNT(rt_icon_url) + COUNT(quoted_icon_url) FROM tweets"));
        // 派生データはリセットされ、再取込のためオフセットも 0
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM tweets"));
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM tweet_media"));
        Assert.Equal(0L, Scalar("SELECT jsonl_offset FROM users WHERE username='alice'"));
        // 正データ (users / read_state) は保全
        Assert.Equal("Alice", (string)Scalar("SELECT display_name FROM users WHERE username='alice'")!);
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM read_state"));
    }
}
