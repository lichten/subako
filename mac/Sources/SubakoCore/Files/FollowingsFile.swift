import Foundation

/// data/_followings/<source>.jsonl — フォロー中アカウントの一覧
/// (docs/data-layer.md §1.7)。fetcher が実行ごとに全書き換えする中間ファイル。
public enum FollowingsFile {
    public static let dirName = "_followings"

    public struct Entry: Sendable, Equatable {
        public let username: String
        public let displayName: String?
    }

    public static func path(dataDir: String, sourceUsername: String) -> String {
        ((dataDir as NSString).appendingPathComponent(dirName) as NSString)
            .appendingPathComponent(sourceUsername + ".jsonl")
    }

    /// 読めた行だけをファイル内の順序のまま返す。username を持たない行・壊れた行は
    /// スキップし、username は大文字小文字無視で重複排除する。ファイルが無ければ空。
    public static func read(dataDir: String, sourceUsername: String) -> [Entry] {
        guard let content = try? String(
            contentsOfFile: path(dataDir: dataDir, sourceUsername: sourceUsername),
            encoding: .utf8)
        else { return [] }

        var entries: [Entry] = []
        var seen = Set<String>()
        for line in content.split(separator: "\n", omittingEmptySubsequences: true) {
            guard let root = JSONValue.parseLine(String(line)),
                  let raw = root.string("username"), !raw.isEmpty
            else { continue }
            var name = raw
            while name.hasPrefix("@") { name.removeFirst() }
            name = name.trimmingCharacters(in: .whitespaces)
            guard !name.isEmpty, seen.insert(name.lowercased()).inserted else { continue }
            let displayName = root.string("display_name")
            entries.append(Entry(
                username: name,
                displayName: (displayName?.isEmpty == false) ? displayName : nil))
        }
        return entries
    }

    /// 取得済み件数 (未取得なら 0)。確認ダイアログの件数表示用。
    public static func count(dataDir: String, sourceUsername: String) -> Int {
        read(dataDir: dataDir, sourceUsername: sourceUsername).count
    }
}
