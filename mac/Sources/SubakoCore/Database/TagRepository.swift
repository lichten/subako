import Foundation
import GRDB

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
        try await db.reader.read { db in
            var rows: [TagRow] = []
            let cursor = try Row.fetchCursor(db, sql: """
                SELECT t.tag_id, t.name,
                       (SELECT COUNT(*) FROM user_tags ut WHERE ut.tag_id = t.tag_id) AS user_count
                FROM tags t
                ORDER BY t.name COLLATE NOCASE
                """)
            while let row = try cursor.next() {
                rows.append(TagRow(
                    tagId: row["tag_id"], name: row["name"], userCount: row["user_count"]))
            }
            return rows
        }
    }

    /// username → 付与タグ ID リスト (サイドバー表示用に全件を 1 クエリで取得)。
    /// キーは小文字化して照合する (users.username は COLLATE NOCASE)。
    public func getAssignments() async throws -> [String: [Int64]] {
        try await db.reader.read { db in
            var map: [String: [Int64]] = [:]
            let cursor = try Row.fetchCursor(db, sql: "SELECT username, tag_id FROM user_tags")
            while let row = try cursor.next() {
                let username: String = row["username"]
                map[username.lowercased(), default: []].append(row["tag_id"])
            }
            return map
        }
    }

    /// タグ作成。同名 (大文字小文字無視) が既存ならその tag_id を返す。
    public func add(name: String) async throws -> Int64 {
        try await db.writer().write { db in
            try db.execute(
                sql: "INSERT INTO tags (name) VALUES (?) ON CONFLICT(name) DO NOTHING",
                arguments: [name])
            return try Int64.fetchOne(
                db, sql: "SELECT tag_id FROM tags WHERE name = ?", arguments: [name])!
        }
    }

    /// タグ付与。既に付いていれば何もしない。
    public func assign(username: String, tagId: Int64) async throws {
        try await db.writer().write { db in
            try db.execute(
                sql: "INSERT OR IGNORE INTO user_tags (username, tag_id) VALUES (?, ?)",
                arguments: [username, tagId])
        }
    }

    /// 複数ユーザー × 複数タグをまとめて付与する (フォロー一括登録用)。冪等。
    public func assignMany(usernames: [String], tagIds: [Int64]) async throws {
        guard !usernames.isEmpty, !tagIds.isEmpty else { return }
        try await db.writer().write { db in
            for username in usernames {
                for tagId in tagIds {
                    try db.execute(
                        sql: "INSERT OR IGNORE INTO user_tags (username, tag_id) VALUES (?, ?)",
                        arguments: [username, tagId])
                }
            }
        }
    }

    /// タグ解除。
    public func unassign(username: String, tagId: Int64) async throws {
        try await db.writer().write { db in
            try db.execute(
                sql: "DELETE FROM user_tags WHERE username = ? AND tag_id = ?",
                arguments: [username, tagId])
        }
    }

    /// タグ削除 (全ユーザーからの付与も同一トランザクションで削除)。
    public func delete(tagId: Int64) async throws {
        try await db.writer().write { db in
            try db.execute(
                sql: """
                    DELETE FROM user_tags WHERE tag_id = :t;
                    DELETE FROM tags WHERE tag_id = :t;
                    """,
                arguments: ["t": tagId])
        }
    }
}
