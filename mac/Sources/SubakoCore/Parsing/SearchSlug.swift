import CryptoKit
import Foundation

/// 検索クエリ → ファイルシステム安全なバケット名 (共有契約 #6)。
/// Python 側 (sorsa_fetcher/fetcher.py の slugify_query) と同一規則を保つこと:
/// 不正文字と空白の連続を "_" に置換 → 前後の "_" を除去 → 40 字に切詰め →
/// "-" + クエリ原文 UTF-8 の SHA1 先頭 8 hex。
public enum SearchSlug {
    private static let unsafeCharsRegex =
        try! NSRegularExpression(pattern: #"[\\/:*?"<>|\s]+"#)

    public static func from(_ query: String) -> String {
        let ns = query as NSString
        var baseName = unsafeCharsRegex.stringByReplacingMatches(
            in: query, range: NSRange(location: 0, length: ns.length), withTemplate: "_")
        while baseName.hasPrefix("_") { baseName.removeFirst() }
        while baseName.hasSuffix("_") { baseName.removeLast() }
        // 切詰めは Python の文字列スライスに合わせてコードポイント単位
        if baseName.unicodeScalars.count > 40 {
            baseName = String(String.UnicodeScalarView(baseName.unicodeScalars.prefix(40)))
        }
        if baseName.isEmpty {
            baseName = "search"
        }
        let digest = Insecure.SHA1.hash(data: Data(query.utf8))
        let hash = digest.map { String(format: "%02x", $0) }.joined().prefix(8)
        return "\(baseName)-\(hash)"
    }
}
