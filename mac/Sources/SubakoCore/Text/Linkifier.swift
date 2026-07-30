import Foundation

/// 本文テキストを URL / 非 URL のセグメントに分割する純ロジック
/// (C# Services/Linkifier.cs の移植)。
public enum Linkifier {
    public struct Segment: Sendable, Equatable {
        public let text: String
        public let isUrl: Bool

        public init(text: String, isUrl: Bool) {
            self.text = text
            self.isUrl = isUrl
        }
    }

    private static let urlRegex =
        try! NSRegularExpression(pattern: #"https?://\S+"#)

    // URL 末尾から取り除く約物 (日本語文中の「〜だ https://... 。」対策)
    private static let trailingPunctuation = Set(".,;:!?)]}>»」』】〉》。、!?…")

    public static func split(_ text: String) -> [Segment] {
        var segments: [Segment] = []
        if text.isEmpty { return segments }

        let ns = text as NSString
        var pos = 0
        for m in urlRegex.matches(in: text, range: NSRange(location: 0, length: ns.length)) {
            let url = trimTrailingPunctuation(ns.substring(with: m.range))
            if url.utf16.count <= "https://".utf16.count {
                continue   // スキーム断片のみはリンク化しない
            }
            if m.range.location > pos {
                segments.append(Segment(
                    text: ns.substring(with: NSRange(location: pos, length: m.range.location - pos)),
                    isUrl: false))
            }
            segments.append(Segment(text: url, isUrl: true))
            pos = m.range.location + url.utf16.count
        }
        if pos < ns.length {
            segments.append(Segment(
                text: ns.substring(with: NSRange(location: pos, length: ns.length - pos)),
                isUrl: false))
        }
        return segments
    }

    private static func trimTrailingPunctuation(_ url: String) -> String {
        var result = url
        while let last = result.last, trailingPunctuation.contains(last) {
            result.removeLast()
        }
        return result
    }

    /// 本文中の動画リンクと、そのサムネイル URL (docs/data-layer.md §3.6)。
    /// ニコニコは番号からサムネイル URL を決定できないため、thumbnailUrl は
    /// キャッシュのキー兼フォールバックで、実 URL は nicoVideoNumber を使って
    /// 取得時に解決する (NicoThumbnail)。
    public struct VideoLink: Sendable, Equatable {
        public let pageUrl: String
        public let thumbnailUrl: String
        public let nicoVideoNumber: String?

        public init(pageUrl: String, thumbnailUrl: String, nicoVideoNumber: String? = nil) {
            self.pageUrl = pageUrl
            self.thumbnailUrl = thumbnailUrl
            self.nicoVideoNumber = nicoVideoNumber
        }
    }

    // 動画 ID の後ろに続けて拾う文字 (クエリやフラグメント)。日本語が混じらないよう
    // ASCII のみを明示する
    private static let urlTail = #"[A-Za-z0-9_\-=&%.:/?#!$'()*+,;@~\[\]]*"#

    // 11 文字の動画 ID を持つ YouTube の各種 URL 形式
    private static let youTubeLongRegex = try! NSRegularExpression(
        pattern: #"^https?://(?:www\.|m\.)?youtube\.com/(?:watch\?(?:[^&\s]*&)*v=|shorts/|live/|embed/)([A-Za-z0-9_\-]{11})(?![A-Za-z0-9_\-])"# + urlTail)

    private static let youTubeShortRegex = try! NSRegularExpression(
        pattern: #"^https?://youtu\.be/([A-Za-z0-9_\-]{11})(?![A-Za-z0-9_\-])"# + urlTail)

    // ニコニコ動画 (nico.ms は短縮 URL)
    private static let nicoWatchRegex = try! NSRegularExpression(
        pattern: #"^https?://(?:www\.)?nicovideo\.jp/watch/(?:sm|nm|so)(\d+)"# + urlTail)

    private static let nicoShortRegex = try! NSRegularExpression(
        pattern: #"^https?://nico\.ms/(?:sm|nm|so)(\d+)"# + urlTail)

    /// 本文から動画リンクを出現順に抽出する。同一サムネイルは先勝ちで 1 件。
    public static func extractVideoLinks(_ text: String?) -> [VideoLink] {
        guard let text, !text.isEmpty else { return [] }

        var result: [VideoLink] = []
        var seen = Set<String>()
        for segment in split(text) where segment.isUrl {
            guard let link = matchVideoLink(segment.text) else { continue }
            if seen.insert(link.thumbnailUrl).inserted {
                result.append(link)
            }
        }
        return result
    }

    /// URL セグメントが動画リンクなら VideoLink を返す。
    /// Split のセグメントは「https://youtu.be/xxx。おすすめ」のように後続の日本語を
    /// 含みうる (約物除去は末尾が空白のときだけ効く) ため、ページ URL は
    /// 正規表現がマッチした範囲から作る。
    private static func matchVideoLink(_ url: String) -> VideoLink? {
        let ns = url as NSString
        let range = NSRange(location: 0, length: ns.length)
        if let m = youTubeLongRegex.firstMatch(in: url, range: range) {
            return youTubeLink(ns, m)
        }
        if let m = youTubeShortRegex.firstMatch(in: url, range: range) {
            return youTubeLink(ns, m)
        }
        // ニコニコは数字部だけを使う (sm/nm/so の別はサムネイル URL に現れない)
        if let m = nicoWatchRegex.firstMatch(in: url, range: range) {
            return nicoLink(ns, m)
        }
        if let m = nicoShortRegex.firstMatch(in: url, range: range) {
            return nicoLink(ns, m)
        }
        return nil
    }

    private static func youTubeLink(_ ns: NSString, _ m: NSTextCheckingResult) -> VideoLink {
        let id = ns.substring(with: m.range(at: 1))
        return VideoLink(
            pageUrl: trimTrailingPunctuation(ns.substring(with: m.range)),
            thumbnailUrl: "https://i.ytimg.com/vi/\(id)/hqdefault.jpg")
    }

    private static func nicoLink(_ ns: NSString, _ m: NSTextCheckingResult) -> VideoLink {
        let number = ns.substring(with: m.range(at: 1))
        return VideoLink(
            pageUrl: trimTrailingPunctuation(ns.substring(with: m.range)),
            thumbnailUrl: "https://nicovideo.cdn.nimg.jp/thumbnails/\(number)/\(number)",
            nicoVideoNumber: number)
    }
}
