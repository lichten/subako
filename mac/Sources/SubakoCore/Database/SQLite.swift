import Foundation
import SwiftToolchainCSQLite

// 同梱 SQLite (swift-toolchain-sqlite) の薄いラッパー。
// macOS 標準の libsqlite3 は「開いているファイルへのハードリンク作成」を検知して
// 接続を無効化するガード (vnode guard) を持ち、Google Drive 同期フォルダ上の
// viewer.db-wal と衝突するため使わない (Windows 版も自前 SQLite を同梱している)。

public struct SQLiteError: Error, CustomStringConvertible, LocalizedError {
    public let code: Int32
    public let message: String
    public let sql: String?

    public var description: String {
        "SQLite error \(code): \(message)" + (sql.map { " - in \"\($0)\"" } ?? "")
    }

    public var errorDescription: String? { description }

    static func from(_ handle: OpaquePointer?, code: Int32, sql: String? = nil) -> SQLiteError {
        let message = handle.flatMap { sqlite3_errmsg($0) }.map { String(cString: $0) }
            ?? "code \(code)"
        return SQLiteError(code: code, message: message, sql: sql)
    }
}

/// バインド可能な値。
public protocol SQLiteBindable: Sendable {}
extension String: SQLiteBindable {}
extension Int: SQLiteBindable {}
extension Int64: SQLiteBindable {}
extension Double: SQLiteBindable {}
extension Bool: SQLiteBindable {}
extension Data: SQLiteBindable {}

/// クエリ結果の 1 行。列名アクセスのみ提供する
/// (実 DB はマイグレーション由来で列順が異なるため序数アクセスは提供しない —
/// docs/mac-port-notes.md §3)。
public struct SQLiteRow: Sendable {
    private let values: [String: Value]

    enum Value: Sendable {
        case null
        case integer(Int64)
        case real(Double)
        case text(String)
        case blob(Data)
    }

    init(statement: OpaquePointer) {
        var values: [String: Value] = [:]
        let count = sqlite3_column_count(statement)
        for i in 0..<count {
            let name = String(cString: sqlite3_column_name(statement, i))
            switch sqlite3_column_type(statement, i) {
            case SQLITE_INTEGER:
                values[name] = .integer(sqlite3_column_int64(statement, i))
            case SQLITE_FLOAT:
                values[name] = .real(sqlite3_column_double(statement, i))
            case SQLITE_TEXT:
                values[name] = .text(String(cString: sqlite3_column_text(statement, i)))
            case SQLITE_BLOB:
                if let bytes = sqlite3_column_blob(statement, i) {
                    values[name] = .blob(Data(
                        bytes: bytes, count: Int(sqlite3_column_bytes(statement, i))))
                } else {
                    values[name] = .blob(Data())
                }
            default:
                values[name] = .null
            }
        }
        self.values = values
    }

    public func string(_ column: String) -> String? {
        switch values[column] {
        case .text(let s): return s
        case .integer(let n): return String(n)
        case .real(let d): return String(d)
        default: return nil
        }
    }

    public func int64(_ column: String) -> Int64? {
        switch values[column] {
        case .integer(let n): return n
        case .real(let d): return Int64(d)
        case .text(let s): return Int64(s)
        default: return nil
        }
    }

    public func int(_ column: String) -> Int? {
        int64(column).map(Int.init)
    }

    public func bool(_ column: String) -> Bool {
        (int64(column) ?? 0) != 0
    }
}

/// 1 本の SQLite 接続。スレッドセーフではない — 呼び出し側 (ViewerDatabase) が
/// 専用キューで直列化するため @unchecked Sendable。
public final class SQLiteConnection: @unchecked Sendable {
    let handle: OpaquePointer

    /// - Parameter path: 通常パスまたは `file:` URI (mode=ro&immutable=1 等)。
    public init(path: String, readOnly: Bool, create: Bool = false) throws {
        var flags: Int32 = readOnly
            ? SQLITE_OPEN_READONLY
            : (create ? SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE : SQLITE_OPEN_READWRITE)
        flags |= SQLITE_OPEN_URI | SQLITE_OPEN_NOMUTEX
        var h: OpaquePointer?
        let rc = sqlite3_open_v2(path, &h, flags, nil)
        guard rc == SQLITE_OK, let h else {
            let error = SQLiteError.from(h, code: rc, sql: nil)
            if let h { sqlite3_close_v2(h) }
            throw error
        }
        handle = h
        sqlite3_busy_timeout(handle, 5000)
    }

    deinit {
        sqlite3_close_v2(handle)
    }

    public var changesCount: Int {
        Int(sqlite3_changes64(handle))
    }

    /// 複数ステートメント可の実行 (引数なし)。DDL・マイグレーション用。
    public func executeScript(_ sql: String) throws {
        var rc: Int32 = SQLITE_OK
        var errorMessage: UnsafeMutablePointer<CChar>?
        rc = sqlite3_exec(handle, sql, nil, nil, &errorMessage)
        if rc != SQLITE_OK {
            let message = errorMessage.map { String(cString: $0) } ?? "code \(rc)"
            sqlite3_free(errorMessage)
            throw SQLiteError(code: rc, message: message, sql: sql)
        }
    }

    /// 単一ステートメントの実行 (位置引数 `?`)。
    public func execute(_ sql: String, _ arguments: [SQLiteBindable?] = []) throws {
        let statement = try prepare(sql)
        defer { sqlite3_finalize(statement) }
        try bind(statement, positional: arguments, sql: sql)
        try stepToDone(statement, sql: sql)
    }

    /// 単一ステートメントの実行 (名前付き引数 `:name`)。
    public func execute(_ sql: String, named arguments: [String: SQLiteBindable?]) throws {
        let statement = try prepare(sql)
        defer { sqlite3_finalize(statement) }
        try bind(statement, named: arguments, sql: sql)
        try stepToDone(statement, sql: sql)
    }

    /// クエリ (位置引数)。全行を配列で返す。
    public func query(_ sql: String, _ arguments: [SQLiteBindable?] = []) throws -> [SQLiteRow] {
        let statement = try prepare(sql)
        defer { sqlite3_finalize(statement) }
        try bind(statement, positional: arguments, sql: sql)
        var rows: [SQLiteRow] = []
        while true {
            let rc = sqlite3_step(statement)
            if rc == SQLITE_ROW {
                rows.append(SQLiteRow(statement: statement))
            } else if rc == SQLITE_DONE {
                break
            } else {
                throw SQLiteError.from(handle, code: rc, sql: sql)
            }
        }
        return rows
    }

    public func queryOne(_ sql: String, _ arguments: [SQLiteBindable?] = []) throws -> SQLiteRow? {
        try query(sql, arguments).first
    }

    /// 先頭行・先頭列の文字列 (スカラ取得)。行なし・NULL は nil。
    public func scalarString(_ sql: String, _ arguments: [SQLiteBindable?] = []) throws -> String? {
        try scalar(sql, arguments) { statement in
            sqlite3_column_type(statement, 0) == SQLITE_NULL
                ? nil : String(cString: sqlite3_column_text(statement, 0))
        }
    }

    /// 先頭行・先頭列の整数 (スカラ取得)。行なし・NULL は nil。
    public func scalarInt64(_ sql: String, _ arguments: [SQLiteBindable?] = []) throws -> Int64? {
        try scalar(sql, arguments) { statement in
            sqlite3_column_type(statement, 0) == SQLITE_NULL
                ? nil : sqlite3_column_int64(statement, 0)
        }
    }

    private func scalar<T>(
        _ sql: String, _ arguments: [SQLiteBindable?],
        _ read: (OpaquePointer) -> T?
    ) throws -> T? {
        let statement = try prepare(sql)
        defer { sqlite3_finalize(statement) }
        try bind(statement, positional: arguments, sql: sql)
        let rc = sqlite3_step(statement)
        if rc == SQLITE_ROW {
            return read(statement)
        }
        if rc == SQLITE_DONE {
            return nil
        }
        throw SQLiteError.from(handle, code: rc, sql: sql)
    }

    /// 明示的なトランザクション。
    public func transaction<T>(_ body: () throws -> T) throws -> T {
        try executeScript("BEGIN IMMEDIATE")
        do {
            let result = try body()
            try executeScript("COMMIT")
            return result
        } catch {
            try? executeScript("ROLLBACK")
            throw error
        }
    }

    public func checkpointTruncate() {
        _ = try? executeScript("PRAGMA wal_checkpoint(TRUNCATE)")
    }

    // MARK: - 内部

    private func prepare(_ sql: String) throws -> OpaquePointer {
        var statement: OpaquePointer?
        let rc = sqlite3_prepare_v2(handle, sql, -1, &statement, nil)
        guard rc == SQLITE_OK, let statement else {
            throw SQLiteError.from(handle, code: rc, sql: sql)
        }
        return statement
    }

    private func stepToDone(_ statement: OpaquePointer, sql: String) throws {
        let rc = sqlite3_step(statement)
        guard rc == SQLITE_DONE || rc == SQLITE_ROW else {
            throw SQLiteError.from(handle, code: rc, sql: sql)
        }
    }

    private func bind(
        _ statement: OpaquePointer, positional arguments: [SQLiteBindable?], sql: String
    ) throws {
        for (offset, value) in arguments.enumerated() {
            try bindValue(statement, index: Int32(offset + 1), value: value, sql: sql)
        }
    }

    private func bind(
        _ statement: OpaquePointer, named arguments: [String: SQLiteBindable?], sql: String
    ) throws {
        for (name, value) in arguments {
            let index = sqlite3_bind_parameter_index(statement, ":" + name)
            guard index > 0 else {
                throw SQLiteError(code: SQLITE_MISUSE, message: "unknown parameter :\(name)", sql: sql)
            }
            try bindValue(statement, index: index, value: value, sql: sql)
        }
    }

    private func bindValue(
        _ statement: OpaquePointer, index: Int32, value: SQLiteBindable?, sql: String
    ) throws {
        let transient = unsafeBitCast(-1, to: sqlite3_destructor_type.self)
        let rc: Int32
        switch value {
        case nil:
            rc = sqlite3_bind_null(statement, index)
        case let v as String:
            rc = sqlite3_bind_text(statement, index, v, -1, transient)
        case let v as Int64:
            rc = sqlite3_bind_int64(statement, index, v)
        case let v as Int:
            rc = sqlite3_bind_int64(statement, index, Int64(v))
        case let v as Bool:
            rc = sqlite3_bind_int64(statement, index, v ? 1 : 0)
        case let v as Double:
            rc = sqlite3_bind_double(statement, index, v)
        case let v as Data:
            rc = v.withUnsafeBytes {
                sqlite3_bind_blob(statement, index, $0.baseAddress, Int32(v.count), transient)
            }
        default:
            throw SQLiteError(code: SQLITE_MISUSE, message: "unsupported bind type", sql: sql)
        }
        guard rc == SQLITE_OK else {
            throw SQLiteError.from(handle, code: rc, sql: sql)
        }
    }
}
