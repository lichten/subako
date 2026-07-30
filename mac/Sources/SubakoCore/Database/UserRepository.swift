import Foundation
import GRDB

/// users テーブルの 1 行 + 集計 (件数・未読数)。
public struct UserRow: Sendable, Equatable {
    public let username: String
    public let displayName: String?
    public let iconUrl: String?
    public let addedAt: String
    public let lastImportAt: String?
    public let jsonlOffset: Int64
    public let tweetCount: Int64
    public let unreadCount: Int64
}

/// C# Data/UserRepository.cs の移植。
public final class UserRepository: Sendable {
    private let db: ViewerDatabase

    public init(_ db: ViewerDatabase) {
        self.db = db
    }

    /// ユーザーアーカイブのみ (検索バケット searches/ は除外)。username 昇順 (NOCASE)。
    public func getAll() async throws -> [UserRow] {
        try await query(includeSearches: false)
    }

    /// 検索バケット (username = searches/<slug>) のみ。
    public func getSearchBuckets() async throws -> [UserRow] {
        try await query(includeSearches: true)
    }

    private func query(includeSearches: Bool) async throws -> [UserRow] {
        try await db.reader.read { db in
            let cond = includeSearches ? "LIKE" : "NOT LIKE"
            let sql = """
                SELECT u.username, u.display_name, u.icon_url, u.added_at, u.last_import_at, u.jsonl_offset,
                       (SELECT COUNT(*) FROM tweets t WHERE t.username = u.username) AS tweet_count,
                       (SELECT COUNT(*) FROM tweets t
                        LEFT JOIN read_state r ON r.tweet_id = t.tweet_id
                        WHERE t.username = u.username AND r.tweet_id IS NULL) AS unread_count
                FROM users u
                WHERE u.username \(cond) 'searches/%'
                ORDER BY u.username COLLATE NOCASE
                """
            var rows: [UserRow] = []
            let cursor = try Row.fetchCursor(db, sql: sql)
            while let row = try cursor.next() {
                rows.append(UserRow(
                    username: row["username"],
                    displayName: row["display_name"],
                    iconUrl: row["icon_url"],
                    addedAt: row["added_at"],
                    lastImportAt: row["last_import_at"],
                    jsonlOffset: row["jsonl_offset"],
                    tweetCount: row["tweet_count"],
                    unreadCount: row["unread_count"]))
            }
            return rows
        }
    }

    /// ユーザー登録 + data/<username>/ の作成。既存なら何もしない。
    @discardableResult
    public func add(_ username: String) async throws -> Bool {
        let added = try await db.writer().write { db in
            try db.execute(
                sql: "INSERT OR IGNORE INTO users (username, added_at) VALUES (?, ?)",
                arguments: [username, DateParsers.utcNow()])
            return db.changesCount > 0
        }
        try FileManager.default.createDirectory(
            atPath: db.userDir(username), withIntermediateDirectories: true)
        return added
    }

    /// ユーザーをまとめて登録する (フォロー一括登録用)。1 トランザクションでまとめる。
    /// - display_name は新規行の初期値だけ (INSERT OR IGNORE なので既存行は不触)。
    /// - icon_url は入れない (数千件の一斉アイコン DL を避ける — docs/data-layer.md §1.7)。
    /// - data/<username>/ は作らない (実際の取得時に fetcher 側が作る)。
    /// - 戻り値: 実際に新規登録された username (入力順)。
    public func addMany(_ users: [(username: String, displayName: String?)]) async throws -> [String] {
        guard !users.isEmpty else { return [] }
        let now = DateParsers.utcNow()
        return try await db.writer().write { db in
            var added: [String] = []
            for user in users {
                try db.execute(
                    sql: "INSERT OR IGNORE INTO users (username, display_name, added_at) VALUES (?, ?, ?)",
                    arguments: [user.username, user.displayName, now])
                if db.changesCount > 0 {
                    added.append(user.username)
                }
            }
            return added
        }
    }

    /// data/ 直下の tweets.jsonl を持つディレクトリを users に自動登録する。
    /// 条件はこれだけ (`_trash/` / `_followings/` は直下に tweets.jsonl が無いため拾われない)。
    @discardableResult
    public func registerExistingDataDirs() async throws -> Int {
        try await register(baseDir: db.dataDir, prefix: "")
    }

    /// data/searches/ 直下の検索バケットを users に自動登録する (CLI で作った検索も GUI に出す)。
    @discardableResult
    public func registerExistingSearchDirs() async throws -> Int {
        let searchesDir = (db.dataDir as NSString).appendingPathComponent("searches")
        return try await register(baseDir: searchesDir, prefix: "searches/")
    }

    private func register(baseDir: String, prefix: String) async throws -> Int {
        let fm = FileManager.default
        guard let entries = try? fm.contentsOfDirectory(atPath: baseDir) else { return 0 }
        var added = 0
        for entry in entries.sorted() {
            let dir = (baseDir as NSString).appendingPathComponent(entry)
            var isDir: ObjCBool = false
            guard fm.fileExists(atPath: dir, isDirectory: &isDir), isDir.boolValue,
                  fm.fileExists(atPath: (dir as NSString).appendingPathComponent("tweets.jsonl"))
            else { continue }
            if try await add(prefix + entry) {
                added += 1
            }
        }
        return added
    }

    /// ユーザーまたは検索バケットを DB (tweets / tweet_media / user_tags / users) から削除する。
    /// read_state は tweet_id 単位で全アーカイブ共通のため消さない (孤児行は無害)。
    public func deleteArchive(_ username: String) async throws {
        try await db.writer().write { db in
            // tweet_media は tweet_id 単位で全バケット共有のため、
            // 行削除後にどこからも参照されなくなったものだけ消す
            try db.execute(
                sql: """
                    DELETE FROM tweets WHERE username = :u;
                    DELETE FROM tweet_media WHERE tweet_id NOT IN (SELECT tweet_id FROM tweets);
                    DELETE FROM user_tags WHERE username = :u;
                    DELETE FROM users WHERE username = :u;
                    """,
                arguments: ["u": username])
        }
    }
}
