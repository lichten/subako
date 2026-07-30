import Foundation
import SubakoCore

/// 画像の表示倍率 (docs/viewer-features.md §5.3)。セッション限り。
enum ImageScale: CGFloat, CaseIterable, Identifiable {
    case half = 0.5
    case normal = 1
    case double = 2
    case quad = 4

    var id: CGFloat { rawValue }
    var label: String {
        switch self {
        case .half: return "小さく (×0.5)"
        case .normal: return "等倍に戻す"
        case .double: return "大きく (×2)"
        case .quad: return "もっと大きく (×4)"
        }
    }
}

/// カード上の 1 画像 (ローカルファイル解決済み)。
struct ResolvedImage: Identifiable, Equatable {
    let id: String          // "<tweetId>_<idx>"
    let idx: Int
    let localPath: String
    /// 基準サイズ: 本文 400×280 / 引用先 260×180 (§5.3)
    let baseWidth: CGFloat
    let baseHeight: CGFloat
}

/// タイムラインカード 1 枚分の表示モデル (C# TweetItemViewModel の移植)。
/// ファイル存在チェックと生 JSONL 再読みを伴うため、構築はバックグラウンドで行う。
struct TweetItem: Identifiable, Equatable, Sendable {
    var row: TweetRow
    /// 本文へ出す画像 (origin 0 = 本文 + origin 2 = RT元 — §5.1)
    let bodyImages: [ResolvedImage]
    /// 引用ブロックへ出す画像 (origin 1)
    let quotedImages: [ResolvedImage]
    /// 本文リンク由来の動画 (YouTube / ニコニコ)
    let videoLinks: [Linkifier.VideoLink]
    let quotedVideoLinks: [Linkifier.VideoLink]
    /// X 添付動画 (raw JSONL 再読みで抽出)
    let xVideos: [VideoEntity]
    /// この tweet_id を含む全アーカイブ (2 つ以上のときのみ、既読ファンアウト用)
    let archives: [String]

    var id: String { row.username + "\u{1}" + row.tweetId }

    var isRetweet: Bool { row.type == .retweet }

    /// カードの主表示テキスト。RT は "RT @x: …" の切り詰め文ではなく RT元の全文 (§5.1)
    var displayText: String {
        isRetweet ? (row.rtText ?? row.fullText) : row.fullText
    }

    /// ヘッダの表示名・@name・アイコン (RT は RT元作者)
    var headerDisplayName: String {
        if isRetweet {
            return row.rtDisplayName ?? row.rtUsername ?? ""
        }
        return row.authorDisplayName ?? row.authorUsername ?? row.username
    }

    var headerUsername: String? {
        isRetweet ? row.rtUsername : (row.authorUsername ?? archiveFallbackName)
    }

    var headerIconUrl: String? {
        isRetweet ? row.rtIconUrl : row.authorIconUrl
    }

    /// `○○ さんがリツイート` の ○○ (RT のみ)
    var retweetedByName: String? {
        guard isRetweet else { return nil }
        return row.authorDisplayName ?? row.authorUsername ?? archiveFallbackName
    }

    private var archiveFallbackName: String? {
        SearchBuckets.isBucketId(row.username) ? nil : row.username
    }

    var replyHeader: String? {
        guard row.type == .reply, let name = row.inReplyToUsername else { return nil }
        return "@\(name) への返信"
    }

    var hasQuote: Bool {
        row.quotedUsername != nil || row.quotedText != nil || !quotedImages.isEmpty
    }

    /// ローカル時刻 yyyy-MM-dd HH:mm (§11.3)
    var localDateText: String {
        Self.displayDate(fromUtc: row.createdAtUtc)
    }

    static func displayDate(fromUtc utc: String) -> String {
        guard let date = DateParsers.parseCreatedAt(utc) else { return "" }
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.dateFormat = "yyyy-MM-dd HH:mm"
        return formatter.string(from: date)
    }

    /// カウント行: RT では返信数を出さない。表示回数は 0 なら省略 (§5.1)
    var countsText: String {
        var parts: [String] = []
        if !isRetweet {
            parts.append("返信 \(row.replyCount)")
        }
        parts.append("RT \(row.retweetCount)")
        parts.append("いいね \(row.likeCount)")
        if row.viewCount > 0 {
            parts.append("表示 \(row.viewCount)")
        }
        return parts.joined(separator: "  ")
    }

    /// ブラウザで開く URL (§5.2。RT はカードの作者 = RT元作者を優先)
    var statusURL: URL {
        if isRetweet, let rtUser = row.rtUsername {
            return TweetURL.status(author: rtUser, tweetId: row.tweetId)
        }
        return TweetURL.status(row: row)
    }

    /// ユーザー追加メニューの対象 (RT なら RT元作者 — §5.2)
    var addableAuthor: String? {
        isRetweet ? row.rtUsername : row.authorUsername
    }

    /// ページの行からカードモデルを構築する (バックグラウンド実行前提)。
    static func build(
        row: TweetRow, media: [TweetMediaRow], archives: [String],
        dataDir: String, jsonlPath: String
    ) -> TweetItem {
        let imagesDir = ((dataDir as NSString).appendingPathComponent(row.username) as NSString)
            .appendingPathComponent("images")
        var body: [ResolvedImage] = []
        var quoted: [ResolvedImage] = []
        for m in media {
            // 実ファイルが無い画像は表示から落とす (§11.2 — 壊れ枠を見せない)
            guard let path = LocalMediaFiles.resolve(
                imagesDir: imagesDir, tweetId: m.tweetId, index: m.index, ext: m.ext)
            else { continue }
            if m.origin == .quoted {
                quoted.append(ResolvedImage(
                    id: "\(m.tweetId)_\(m.index)", idx: m.index, localPath: path,
                    baseWidth: 260, baseHeight: 180))
            } else {
                body.append(ResolvedImage(
                    id: "\(m.tweetId)_\(m.index)", idx: m.index, localPath: path,
                    baseWidth: 400, baseHeight: 280))
            }
        }

        let isRT = row.type == .retweet
        let displayText = isRT ? (row.rtText ?? row.fullText) : row.fullText
        let videoLinks = Linkifier.extractVideoLinks(displayText)
        let quotedVideoLinks = Linkifier.extractVideoLinks(row.quotedText)
        let xVideos = RawVideoEntityReader.read(
            jsonlPath: jsonlPath, rawOffset: row.rawOffset, rawLength: row.rawLength)

        return TweetItem(
            row: row, bodyImages: body, quotedImages: quoted,
            videoLinks: videoLinks, quotedVideoLinks: quotedVideoLinks,
            xVideos: xVideos, archives: archives)
    }
}

/// メディアグリッドのセル。
struct MediaItem: Identifiable, Equatable, Sendable {
    let media: MediaPageRow
    let localPath: String
    var id: String { "\(media.username)\u{1}\(media.tweetId)_\(media.idx)" }
}
