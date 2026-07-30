import Foundation
import GRDB

public enum ViewerDatabaseError: Error, LocalizedError, Equatable {
    /// schema_version が自分の対応バージョンより新しい (開いてはならない)。
    case schemaTooNew(stored: Int)
    /// 読み取り専用モードで旧バージョンの DB を開いた (マイグレーションできない)。
    case readOnlyNeedsMigration(stored: Int)
    /// 読み取り専用モードで書込 API を呼んだ。
    case readOnlyMode

    public var errorDescription: String? {
        switch self {
        case .schemaTooNew(let stored):
            return "viewer.db のスキーマバージョン \(stored) はこのアプリ (v\(ViewerDatabase.schemaVersion)) より新しいため開けません。"
        case .readOnlyNeedsMigration(let stored):
            return "viewer.db のスキーマバージョン \(stored) は古い形式です。閲覧専用モードでは更新できないため、書込可能モードまたは Windows 版で一度開いてください。"
        case .readOnlyMode:
            return "閲覧専用モードのため書き込みできません。"
        }
    }
}

/// data/viewer.db の接続とスキーマを管理する (docs/data-layer.md §4)。
/// tweets / tweet_media は JSONL から再構築可能な派生データ、
/// users / read_state / tags / user_tags は正データ。
///
/// - 書込モード: DatabasePool (WAL / busy_timeout=5000 / synchronous=NORMAL)。
///   書込は GRDB が直列化する (書き手はプロセス内で 1 つ、の契約)。
/// - 閲覧専用モード: `file:...?mode=ro&immutable=1` で開き、
///   一切の書込 API を拒否する (Google Drive 同期中の WAL に巻き込まれない)。
public final class ViewerDatabase: Sendable {
    public static let schemaVersion = 7

    public let dataDir: String
    public let dbPath: String
    public let isReadOnly: Bool

    private let pool: DatabasePool?
    private let roQueue: DatabaseQueue?

    /// tweets テーブルの DDL。新規作成と v5 マイグレーション (DROP → CREATE) で共用。
    private static let tweetsDdl = """
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
        """

    public init(dataDir: String, readOnly: Bool = false) throws {
        self.dataDir = dataDir
        self.dbPath = (dataDir as NSString).appendingPathComponent("viewer.db")
        self.isReadOnly = readOnly

        if readOnly {
            var config = Configuration()
            config.readonly = true
            // 同期中の WAL に巻き込まれないよう immutable で開く (docs/mac-port-notes.md §3)
            let queue = try DatabaseQueue(
                path: "file:\(dbPath)?mode=ro&immutable=1", configuration: config)
            self.pool = nil
            self.roQueue = queue
            let stored = try Self.readSchemaVersion(queue)
            if stored > Self.schemaVersion {
                throw ViewerDatabaseError.schemaTooNew(stored: stored)
            }
            if stored < Self.schemaVersion {
                throw ViewerDatabaseError.readOnlyNeedsMigration(stored: stored)
            }
        } else {
            try FileManager.default.createDirectory(
                atPath: dataDir, withIntermediateDirectories: true)
            var config = Configuration()
            config.busyMode = .timeout(5.0)
            config.prepareDatabase { db in
                try db.execute(sql: "PRAGMA synchronous = NORMAL")
            }
            let pool = try DatabasePool(path: dbPath, configuration: config)
            self.pool = pool
            self.roQueue = nil
            try Self.ensureCreated(pool)
        }
    }

    /// 読み取り接続 (両モード共通)。列は必ず名前でアクセスすること
    /// (実 DB はマイグレーション由来で列順が異なる — SELECT * + 序数アクセス禁止)。
    public var reader: any DatabaseReader {
        pool ?? roQueue!
    }

    /// 書込接続。閲覧専用モードでは throw。
    public func writer() throws -> any DatabaseWriter {
        guard let pool else { throw ViewerDatabaseError.readOnlyMode }
        return pool
    }

    /// 終了時に呼ぶ。WAL を本体へ畳んでクラウド同期の安全性を上げる
    /// (docs/mac-port-notes.md §3 — Windows 版は未実施の改善点)。
    public func checkpointAndClose() {
        if let pool {
            try? pool.writeWithoutTransaction { db in
                try db.checkpoint(.truncate)
            }
            try? pool.close()
        } else {
            try? roQueue?.close()
        }
    }

    private static func readSchemaVersion(_ reader: any DatabaseReader) throws -> Int {
        try reader.read { db in
            let value = try String.fetchOne(
                db, sql: "SELECT value FROM schema_meta WHERE key = 'schema_version'")
            return value.flatMap(Int.init) ?? 0
        }
    }

    // MARK: - スキーマ作成・マイグレーション

    private static func ensureCreated(_ pool: DatabasePool) throws {
        try pool.write { db in
            try db.execute(sql: """
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

                \(tweetsDdl)

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
                """)

            let stored = try String.fetchOne(
                db, sql: "SELECT value FROM schema_meta WHERE key = 'schema_version'")
                .flatMap(Int.init) ?? 0
            if stored > schemaVersion {
                throw ViewerDatabaseError.schemaTooNew(stored: stored)
            }
            // 逐次マイグレーション (派生データはリセット、正データは保全)
            if stored < 2 {
                try migrate(db, to: 2, alterSql: """
                    ALTER TABLE users  ADD COLUMN icon_url TEXT;
                    ALTER TABLE tweets ADD COLUMN rt_icon_url TEXT;
                    ALTER TABLE tweets ADD COLUMN quoted_icon_url TEXT;
                    """)
            }
            if stored < 3 {
                try migrate(db, to: 3, alterSql: """
                    ALTER TABLE tweet_media ADD COLUMN origin INTEGER NOT NULL DEFAULT 0;
                    """)
            }
            if stored < 4 {
                // tags / user_tags は上の CREATE TABLE IF NOT EXISTS で作成済み。
                // テーブル追加のみのため派生データのリセットは行わない。
                try db.execute(sql: "UPDATE schema_meta SET value = '4' WHERE key = 'schema_version'")
            }
            if stored < 5 {
                // author 3 列の追加 + 主キーを (username, tweet_id) に変更するため作り直す。
                try db.execute(sql: """
                    DROP TABLE tweets;
                    \(tweetsDdl)
                    DELETE FROM tweet_media;
                    UPDATE users SET jsonl_offset = 0;
                    UPDATE schema_meta SET value = '5' WHERE key = 'schema_version';
                    """)
            }
            if stored < 6 {
                // DDL 変更なし。パーサ変更 (引用先・RT元の本文由来画像) の反映のため派生リセット
                try migrate(db, to: 6, alterSql: "")
            }
            if stored < 7 {
                // DDL 変更なし。パーサ変更 (RT カウントの RT元フォールバック) の反映のため派生リセット
                try migrate(db, to: 7, alterSql: "")
            }
        }
    }

    /// 列追加 → 派生データ (tweets/tweet_media) をリセットして次回取込で
    /// 新列を埋める、の定型マイグレーション。
    private static func migrate(_ db: Database, to version: Int, alterSql: String) throws {
        try db.execute(sql: alterSql + """
            DELETE FROM tweet_media;
            DELETE FROM tweets;
            UPDATE users SET jsonl_offset = 0;
            UPDATE schema_meta SET value = '\(version)' WHERE key = 'schema_version';
            """)
    }

    // MARK: - パス

    public func userDir(_ username: String) -> String {
        (dataDir as NSString).appendingPathComponent(username)
    }

    public func jsonlPath(_ username: String) -> String {
        (userDir(username) as NSString).appendingPathComponent("tweets.jsonl")
    }

    public func imagesDir(_ username: String) -> String {
        (userDir(username) as NSString).appendingPathComponent("images")
    }
}
