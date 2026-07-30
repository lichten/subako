import Foundation
import Testing
@testable import SubakoCore

/// 旧スキーマの viewer.db を開いた際の逐次マイグレーション検証
/// (C# SchemaMigrationTests.cs の移植)。
@Suite struct SchemaMigrationTests {
    /// v1 の DDL で DB を作り、users / tweets / read_state に 1 行ずつ入れる。
    private func createV1Database(_ dataDir: String) throws {
        let conn = try SQLiteConnection(
            path: (dataDir as NSString).appendingPathComponent("viewer.db"),
            readOnly: false, create: true)
        try conn.executeScript("""
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
            """)
    }

    @Test func v1DatabaseMigratesToLatestPreservingAuthoritativeData() throws {
        let dataDir = try makeTempDir()
        defer { try? FileManager.default.removeItem(atPath: dataDir) }
        try createV1Database(dataDir)

        let db = try ViewerDatabase(dataDir: dataDir)
        defer { db.checkpointAndClose() }

        let snap = try db.readSync { conn in
            (
                version: try conn.scalarString("SELECT value FROM schema_meta WHERE key='schema_version'"),
                iconUrlCount: try conn.scalarInt64("SELECT COUNT(icon_url) FROM users"),
                rtIconCount: try conn.scalarInt64("SELECT COUNT(rt_icon_url) + COUNT(quoted_icon_url) FROM tweets"),
                originCount: try conn.scalarInt64("SELECT COUNT(origin) FROM tweet_media"),
                tweetCount: try conn.scalarInt64("SELECT COUNT(*) FROM tweets"),
                mediaCount: try conn.scalarInt64("SELECT COUNT(*) FROM tweet_media"),
                offset: try conn.scalarInt64("SELECT jsonl_offset FROM users WHERE username='alice'"),
                displayName: try conn.scalarString("SELECT display_name FROM users WHERE username='alice'"),
                readStateCount: try conn.scalarInt64("SELECT COUNT(*) FROM read_state"),
                tagCount: try conn.scalarInt64("SELECT COUNT(*) FROM tags"),
                userTagCount: try conn.scalarInt64("SELECT COUNT(*) FROM user_tags"),
                authorCount: try conn.scalarInt64("SELECT COUNT(author_username) FROM tweets")
            )
        }
        // 最新バージョンまで逐次マイグレーションされ、新列 (SELECT が例外にならない) が存在する
        #expect(snap.version == String(ViewerDatabase.schemaVersion))
        #expect(snap.iconUrlCount == 0)
        #expect(snap.rtIconCount == 0)
        #expect(snap.originCount == 0)
        // 派生データはリセットされ、再取込のためオフセットも 0
        #expect(snap.tweetCount == 0)
        #expect(snap.mediaCount == 0)
        #expect(snap.offset == 0)
        // 正データ (users / read_state) は保全。タグテーブル・author 列も存在する
        #expect(snap.displayName == "Alice")
        #expect(snap.readStateCount == 1)
        #expect(snap.tagCount == 0)
        #expect(snap.userTagCount == 0)
        #expect(snap.authorCount == 0)
    }

    /// v4 スキーマ相当 (tweets は tweet_id 単独 PK・author 列なし) の DB を作る。
    private func createV4Database(_ dataDir: String) async throws {
        let setup = try ViewerDatabase(dataDir: dataDir)
        try await setup.writeWithoutTransaction { conn in
            try conn.executeScript("""
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
                """)
        }
        setup.checkpointAndClose()
    }

    @Test func v4DatabaseMigratesToV5ResettingDerivedDataOnly() async throws {
        let dataDir = try makeTempDir()
        defer { try? FileManager.default.removeItem(atPath: dataDir) }
        try await createV4Database(dataDir)

        let db = try ViewerDatabase(dataDir: dataDir)
        defer { db.checkpointAndClose() }

        let snap = try db.readSync { conn in
            (
                version: try conn.scalarString("SELECT value FROM schema_meta WHERE key='schema_version'"),
                tweetCount: try conn.scalarInt64("SELECT COUNT(*) FROM tweets"),
                mediaCount: try conn.scalarInt64("SELECT COUNT(*) FROM tweet_media"),
                offset: try conn.scalarInt64("SELECT jsonl_offset FROM users WHERE username='alice'"),
                authorCount: try conn.scalarInt64("SELECT COUNT(author_username) FROM tweets"),
                displayName: try conn.scalarString("SELECT display_name FROM users WHERE username='alice'"),
                readStateCount: try conn.scalarInt64("SELECT COUNT(*) FROM read_state"),
                tagCount: try conn.scalarInt64("SELECT COUNT(*) FROM tags"),
                userTagCount: try conn.scalarInt64("SELECT COUNT(*) FROM user_tags")
            )
        }
        #expect(snap.version == String(ViewerDatabase.schemaVersion))
        // 派生データはリセットされ再取込のためオフセットも 0。author 列が存在する
        #expect(snap.tweetCount == 0)
        #expect(snap.mediaCount == 0)
        #expect(snap.offset == 0)
        #expect(snap.authorCount == 0)
        // 正データ (users / read_state / tags) は保全
        #expect(snap.displayName == "Alice")
        #expect(snap.readStateCount == 1)
        #expect(snap.tagCount == 1)
        #expect(snap.userTagCount == 1)
    }

    @Test func compositePrimaryKeyAllowsSameTweetInArchiveAndSearchBucket() async throws {
        let dataDir = try makeTempDir()
        defer { try? FileManager.default.removeItem(atPath: dataDir) }
        let db = try ViewerDatabase(dataDir: dataDir)
        defer { db.checkpointAndClose() }

        try await db.write { conn in
            // 同一 tweet_id をアーカイブと検索バケットの両方に格納できる (v5 複合 PK)
            try conn.executeScript("""
                INSERT INTO tweets (tweet_id, id_int, username, created_at_utc, sort_key,
                                    tweet_type, full_text, raw_offset, raw_length)
                  VALUES ('1', 1, 'alice', '2026-01-01T00:00:00Z', 100, 0, 'hello', 0, 10);
                INSERT INTO tweets (tweet_id, id_int, username, created_at_utc, sort_key,
                                    tweet_type, full_text, raw_offset, raw_length)
                  VALUES ('1', 1, 'searches/kw-12345678', '2026-01-01T00:00:00Z', 100, 0, 'hello', 0, 10);
                """)
        }
        let count = try db.readSync { conn in
            try conn.scalarInt64("SELECT COUNT(*) FROM tweets WHERE tweet_id = '1'")
        }
        #expect(count == 2)
    }

    @Test func newerSchemaVersionIsRejected() async throws {
        let dataDir = try makeTempDir()
        defer { try? FileManager.default.removeItem(atPath: dataDir) }
        do {
            let setup = try ViewerDatabase(dataDir: dataDir)
            try await setup.write { conn in
                try conn.execute(
                    "UPDATE schema_meta SET value = '99' WHERE key = 'schema_version'")
            }
            setup.checkpointAndClose()
        }
        #expect(throws: ViewerDatabaseError.schemaTooNew(stored: 99)) {
            _ = try ViewerDatabase(dataDir: dataDir)
        }
        #expect(throws: ViewerDatabaseError.schemaTooNew(stored: 99)) {
            _ = try ViewerDatabase(dataDir: dataDir, readOnly: true)
        }
    }

    @Test func readOnlyModeOpensCurrentSchemaAndRejectsWrites() async throws {
        let dataDir = try makeTempDir()
        defer { try? FileManager.default.removeItem(atPath: dataDir) }
        // 書込モードで作成してデータを入れ、WAL を畳んでから閉じる
        do {
            let setup = try TestDataDir(existingDir: dataDir)
            try await setup.importUser("alice", [tweetLine(1)])
            setup.db.checkpointAndClose()
        }

        let ro = try ViewerDatabase(dataDir: dataDir, readOnly: true)
        defer { ro.checkpointAndClose() }
        #expect(ro.isReadOnly)
        let page = try await TweetRepository(ro).getPage(
            usernames: ["alice"], unreadOnly: false, after: nil, limit: 10)
        #expect(page.rows.count == 1)
        // 書込 API は拒否される
        await #expect(throws: ViewerDatabaseError.readOnlyMode) {
            try await TweetRepository(ro).setRead(tweetId: "1", username: "alice", read: true)
        }
    }
}

extension TestDataDir {
    /// 既存ディレクトリを使う初期化 (readOnly テスト用)。
    init(existingDir: String) throws {
        self.init(dataDir: existingDir, db: try ViewerDatabase(dataDir: existingDir))
    }
}
