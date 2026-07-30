import CryptoKit
import Foundation

/// アイコン / 動画サムネイルキャッシュのファイル名規則 (共有契約 #7、docs/data-layer.md §3.5)。
/// `<sha1(元URL) の小文字hex>.<ext>`。ext は拡張子規則 (契約 #5) を元 URL に適用。
public enum IconCacheKey {
    private static let normalSuffixRegex =
        try! NSRegularExpression(pattern: #"_normal(\.\w+)?$"#)

    public static func fileName(for url: String) -> String {
        let digest = Insecure.SHA1.hash(data: Data(url.utf8))
        let hash = digest.map { String(format: "%02x", $0) }.joined()
        return "\(hash).\(TweetJsonParser.extOf(url))"
    }

    /// `_normal` (48px) を `_bigger` (73px) に置換した取得用 URL。
    /// キャッシュのキーは元 URL のまま (キー ≠ 取得先、docs/viewer-features.md §11.1)。
    public static func biggerVariant(of url: String) -> String {
        let ns = url as NSString
        return normalSuffixRegex.stringByReplacingMatches(
            in: url, range: NSRange(location: 0, length: ns.length),
            withTemplate: "_bigger$1")
    }
}
