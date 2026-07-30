import Foundation

/// 検索バケット直下の search.json (docs/data-layer.md §1.5)。書き手は fetcher
/// (初回作成・クエリ同期) とビューア (クエリ変更・名称設定)。
/// 他キーを保持する read-modify-write で扱う。
public enum SearchMetadata {
    public struct Info: Sendable, Equatable {
        public let query: String
        public let name: String?
        public let createdAt: String?
    }

    private static func path(_ bucketDir: String) -> String {
        (bucketDir as NSString).appendingPathComponent("search.json")
    }

    /// 読めない・壊れている場合は nil (呼び出し側でフォルダ名にフォールバック)。
    public static func tryRead(bucketDir: String) -> Info? {
        guard let data = FileManager.default.contents(atPath: path(bucketDir)),
              let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let query = nonEmptyString(root["query"])
        else { return nil }
        return Info(
            query: query,
            name: nonEmptyString(root["name"]),
            createdAt: nonEmptyString(root["created_at"]))
    }

    /// query / name を差し替えて保存する。既存ファイルの他キー (created_at 等) は保持。
    /// name = nil はキー削除 (名称未設定 = クエリ表示)。新規・破損時は作り直す。
    public static func write(bucketDir: String, query: String, name: String?) throws {
        var root: [String: Any] = [:]
        if let data = FileManager.default.contents(atPath: path(bucketDir)),
           let existing = try? JSONSerialization.jsonObject(with: data) as? [String: Any] {
            root = existing
        }
        root["query"] = query
        if let name, !name.isEmpty {
            root["name"] = name
        } else {
            root.removeValue(forKey: "name")
        }
        if root["created_at"] == nil {
            root["created_at"] = DateParsers.utcNow()
        }
        try FileManager.default.createDirectory(
            atPath: bucketDir, withIntermediateDirectories: true)
        let data = try JSONSerialization.data(
            withJSONObject: root,
            options: [.prettyPrinted, .withoutEscapingSlashes, .sortedKeys])
        try data.write(to: URL(fileURLWithPath: path(bucketDir)))
    }

    private static func nonEmptyString(_ value: Any?) -> String? {
        guard let s = value as? String, !s.isEmpty else { return nil }
        return s
    }
}
