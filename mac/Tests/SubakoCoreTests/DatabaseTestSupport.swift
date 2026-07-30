import Foundation
import Testing
@testable import SubakoCore

/// DB 系テストの共通土台: 一時データフォルダ + ViewerDatabase。
struct TestDataDir {
    let dataDir: String
    let db: ViewerDatabase

    init() throws {
        dataDir = try makeTempDir()
        db = try ViewerDatabase(dataDir: dataDir)
    }

    init(dataDir: String, db: ViewerDatabase) {
        self.dataDir = dataDir
        self.db = db
    }

    func cleanup() {
        db.checkpointAndClose()
        try? FileManager.default.removeItem(atPath: dataDir)
    }

    /// tweets.jsonl へ追記 (UTF-8、\n 終端)。
    func writeJsonl(_ username: String, _ lines: [String]) throws {
        try FileManager.default.createDirectory(
            atPath: db.userDir(username), withIntermediateDirectories: true)
        let path = db.jsonlPath(username)
        let data = Data(lines.map { $0 + "\n" }.joined().utf8)
        if let handle = FileHandle(forWritingAtPath: path) {
            defer { try? handle.close() }
            try handle.seekToEnd()
            try handle.write(contentsOf: data)
        } else {
            try data.write(to: URL(fileURLWithPath: path))
        }
    }

    /// 改行なしで追記 (不完全行のテスト用)。
    func appendRaw(_ username: String, _ text: String) throws {
        let handle = FileHandle(forWritingAtPath: db.jsonlPath(username))!
        defer { try? handle.close() }
        try handle.seekToEnd()
        try handle.write(contentsOf: Data(text.utf8))
    }

    func importUser(_ username: String, _ lines: [String]) async throws {
        try await UserRepository(db).add(username)
        try writeJsonl(username, lines)
        _ = try await JsonlImporter(db).importUser(username)
    }

    func countTweets(_ username: String) throws -> Int64 {
        try db.reader.read { db in
            try Int64.fetchOne(
                db, sql: "SELECT COUNT(*) FROM tweets WHERE username = ?",
                arguments: [username]) ?? 0
        }
    }

    func scalar<T: DatabaseValueConvertible & StatementColumnConvertible & Sendable>(
        _ sql: String, as type: T.Type = T.self
    ) throws -> T? {
        try db.reader.read { db in
            try T.fetchOne(db, sql: sql)
        }
    }
}

import GRDB

func tweetLine(_ id: Int64, date: String = "Wed Apr 11 08:26:14 +0000 2007") -> String {
    #"{"id":"\#(id)","created_at":"\#(date)","full_text":"tweet \#(id) 日本語","user":{"username":"author\#(id)","display_name":"Author \#(id)"},"entities":[]}"#
}

/// ISO 形式の日付 (2026-07-DD)。並び順テスト用に sort_key へ確実に差をつける。
func isoDate(_ day: Int) -> String {
    String(format: "2026-07-%02dT20:00:00+00:00", day)
}

func isoEpoch(_ day: Int) -> Int64 {
    var comps = DateComponents()
    (comps.year, comps.month, comps.day, comps.hour) = (2026, 7, day, 20)
    comps.timeZone = TimeZone(identifier: "UTC")
    return Int64(Calendar(identifier: .gregorian).date(from: comps)!.timeIntervalSince1970)
}
