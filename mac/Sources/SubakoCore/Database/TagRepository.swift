import Foundation

public struct TagRow: Sendable, Equatable {
    public let tagId: Int64
    public let name: String
    public let userCount: Int64
}

/// ユーザー / 検索バケットへの独自タグ (tags / user_tags) の読み書き
/// (C# Data/TagRepository.cs の移植)。
public final class TagRepository: Sendable {
    private let db: ViewerDatabase

    public init(_ db: ViewerDatabase) {
        self.db = db
    }

    /// 全タグ (name 昇順 NOCASE)。付与人数付き。
    public func getAll() async throws -> [TagRow] {
        try await db.read { conn in
            try conn.query("""
                SELECT t.tag_id, t.name,
                       (SELECT COUNT(*) FROM user_tags ut WHERE ut.tag_id = t.tag_id) AS user_count
                FROM tags t
                ORDER BY t.name COLLATE NOCASE
                """).map { row in
                TagRow(
                    tagId: row.int64("tag_id") ?? 0,
                    name: row.string("name") ?? "",
                    userCount: row.int64("user_count") ?? 0)
            }
        }
    }

    /// username → 付与タグ ID リスト (サイドバー表示用に全件を 1 クエリで取得)。
    /// キーは小文字化して照合する (users.username は COLLATE NOCASE)。
    public func getAssignments() async throws -> [String: [Int64]] {
        try await db.read { conn in
            var map: [String: [Int64]] = [:]
            for row in try conn.query("SELECT username, tag_id FROM user_tags") {
                if let username = row.string("username"), let tagId = row.int64("tag_id") {
                    map[username.lowercased(), default: []].append(tagId)
                }
            }
            return map
        }
    }

    /// タグ作成。同名 (大文字小文字無視) が既存ならその tag_id を返す。
    public func add(name: String) async throws -> Int64 {
        try await db.write { conn in
            try conn.execute(
                "INSERT INTO tags (name) VALUES (?) ON CONFLICT(name) DO NOTHING", [name])
            return try conn.scalarInt64(
                "SELECT tag_id FROM tags WHERE name = ?", [name]) ?? 0
        }
    }

    /// タグ付与。既に付いていれば何もしない。
    public func assign(username: String, tagId: Int64) async throws {
        try await db.write { conn in
            try conn.execute(
                "INSERT OR IGNORE INTO user_tags (username, tag_id) VALUES (?, ?)",
                [username, tagId])
        }
    }

    /// 複数ユーザー × 複数タグをまとめて付与する (フォロー一括登録用)。冪等。
    public func assignMany(usernames: [String], tagIds: [Int64]) async throws {
        guard !usernames.isEmpty, !tagIds.isEmpty else { return }
        try await db.write { conn in
            for username in usernames {
                for tagId in tagIds {
                    try conn.execute(
                        "INSERT OR IGNORE INTO user_tags (username, tag_id) VALUES (?, ?)",
                        [username, tagId])
                }
            }
        }
    }

    /// タグ解除。
    public func unassign(username: String, tagId: Int64) async throws {
        try await db.write { conn in
            try conn.execute(
                "DELETE FROM user_tags WHERE username = ? AND tag_id = ?",
                [username, tagId])
        }
    }

    /// タグ削除 (全ユーザーからの付与も同一トランザクションで削除)。
    public func delete(tagId: Int64) async throws {
        try await db.write { conn in
            try conn.execute("DELETE FROM user_tags WHERE tag_id = ?", [tagId])
            try conn.execute("DELETE FROM tags WHERE tag_id = ?", [tagId])
        }
    }
}
