import Foundation

/// tweets.jsonl の 1 行を TweetRow + tweet_media 行に変換する純関数。
/// ID 抽出順・日時形式・画像抽出順は C# (TweetViewer.Data.TweetJsonParser) /
/// Python (storage.tweet_id_of / fetcher.parse_created_at / media.extract_photo_urls)
/// と一致させること (docs/mac-port-notes.md §2 の共有契約 #1–#5)。
public enum TweetJsonParser {
    private static let photoTypes: Set<String> = ["photo", "image"]
    private static let videoTypes: Set<String> = ["video", "animated_gif"]
    private static let urlKeys = ["preview", "media_url_https", "media_url", "url", "link", "expanded_url"]
    private static let nestedKeys = ["quoted_status", "retweeted_status", "quoted_tweet", "retweeted_tweet"]

    private static let pathExtRegex =
        try! NSRegularExpression(pattern: #"\.(\w{3,4})$"#)

    // 末尾の句読点を巻き込まないよう media キー・拡張子・クエリまでを厳密に区切る
    private static let textMediaRegex = try! NSRegularExpression(
        pattern: #"https?://pbs\.twimg\.com/media/[A-Za-z0-9_\-]+"#
            + #"(?:\.(?:jpg|jpeg|png|webp|gif))?(?:\?[A-Za-z0-9_=&%.\-]*)?"#)

    /// パース不能行は nil (呼び出し側でスキップ集計)。
    public static func parse(
        _ line: String, username: String, rawOffset: Int64, rawLength: Int64
    ) -> ParsedTweet? {
        guard let root = JSONValue.parseLine(line), root.isObject else { return nil }
        guard let tweetId = tweetIdOf(root) else { return nil }

        // C# は NumberStyles.None (符号・空白なしの十進数字のみ)。失敗は 0 (共有契約)
        let idInt: Int64 =
            tweetId.allSatisfy({ $0.isASCII && $0.isNumber }) ? (Int64(tweetId) ?? 0) : 0

        let createdAt = DateParsers.parseCreatedAt(
            root.string("created_at") ?? root.string("createdAt"))
        let sortKey = createdAt.map(DateParsers.unixSeconds) ?? 0
        let createdAtUtc = createdAt.map(DateParsers.utcString) ?? ""

        let rt = root.object("retweeted_status") ?? root.object("retweeted_tweet")
        let quoted = root.object("quoted_status") ?? root.object("quoted_tweet")
        // in_reply_to_tweet_id は JSON 文字列のときのみ Reply (数値は Reply 扱いしない)
        let isReply = root.boolIsTrue("is_reply") || root.string("in_reply_to_tweet_id") != nil
        let isQuote = root.boolIsTrue("is_quote_status") || quoted != nil

        let type: TweetType = rt != nil ? .retweet
            : isReply ? .reply
            : isQuote ? .quote
            : .tweet

        let media = extractMedia(root, tweetId: tweetId)
        let author = root.object("user")

        let row = TweetRow(
            tweetId: tweetId,
            idInt: idInt,
            username: username,
            authorUsername: author?.string("username"),
            authorDisplayName: author?.string("display_name"),
            authorIconUrl: author?.string("profile_image_url"),
            createdAtUtc: createdAtUtc,
            sortKey: sortKey,
            type: type,
            fullText: root.string("full_text") ?? root.string("text") ?? "",
            lang: root.string("lang"),
            inReplyToUsername: root.string("in_reply_to_username"),
            rtUsername: rt?.object("user")?.string("username"),
            rtDisplayName: rt?.object("user")?.string("display_name"),
            rtText: rt.flatMap { $0.string("full_text") ?? $0.string("text") },
            rtIconUrl: rt?.object("user")?.string("profile_image_url"),
            quotedUsername: quoted?.object("user")?.string("username"),
            quotedDisplayName: quoted?.object("user")?.string("display_name"),
            quotedText: quoted.flatMap { $0.string("full_text") ?? $0.string("text") },
            quotedIconUrl: quoted?.object("user")?.string("profile_image_url"),
            likeCount: count(root, rt, "likes_count"),
            retweetCount: count(root, rt, "retweet_count"),
            replyCount: count(root, rt, "reply_count"),
            viewCount: count(root, rt, "view_count"),
            mediaCount: media.count,
            rawOffset: rawOffset,
            rawLength: rawLength
        )
        return ParsedTweet(row: row, media: media, authorIconUrl: author?.string("profile_image_url"))
    }

    /// storage.tweet_id_of と同順: id_str → id → tweet_id。数値でも文字列化。
    public static func tweetIdOf(_ root: JSONValue) -> String? {
        for key in ["id_str", "id", "tweet_id"] {
            switch root.member(key) {
            case .string(let s) where !s.isEmpty:
                return s
            case .integer(let n):
                return String(n)
            case .number(let d):
                // Int64 に収まる整数値のみ正確に文字列化できる (実データの ID は
                // id_str が常にあるため、この経路は保険)
                if let i = Int64(exactly: d) {
                    return String(i)
                }
                return String(d)
            default:
                continue
            }
        }
        return nil
    }

    /// media.extract_photo_urls + to_original_size の再現。idx は 1 始まり。
    static func extractMedia(_ root: JSONValue, tweetId: String) -> [TweetMediaRow] {
        var result: [TweetMediaRow] = []
        var seen = Set<String>()

        // 列挙順は media.py と同一 (本文 → 引用 → RT)。URL 重複は先勝ち = 本文優先
        var targets: [(JSONValue, MediaOrigin)] = [(root, .own)]
        for key in nestedKeys {
            if let nested = root.object(key) {
                targets.append((nested, key.hasPrefix("quoted") ? .quoted : .retweeted))
            }
        }

        for (target, origin) in targets {
            var found = iterEntityEntries(target)
                .filter { isEntry($0, ofTypes: photoTypes) }
                .compactMap(pickImageUrl)
            if found.isEmpty {
                found = mediaUrlsFromText(target)
            }
            for url in found {
                guard seen.insert(url).inserted else { continue }
                result.append(TweetMediaRow(
                    tweetId: tweetId, index: result.count + 1,
                    sourceUrl: url, ext: extOf(url), origin: origin))
            }
        }
        return result
    }

    /// entities が空のときのフォールバック (media._media_urls_from_text と同一規則)。
    /// 入れ子の quoted_status / retweeted_status は API 実挙動として entities が常に空で、
    /// Sorsa が本文の t.co を展開済み URL にするためここから 1 枚目だけ拾える。
    static func mediaUrlsFromText(_ tweet: JSONValue) -> [String] {
        guard let text = tweet.string("full_text"), !text.isEmpty else { return [] }
        let ns = text as NSString
        return textMediaRegex
            .matches(in: text, range: NSRange(location: 0, length: ns.length))
            .map { ns.substring(with: $0.range) }
    }

    private static func iterEntityEntries(_ tweet: JSONValue) -> [JSONValue] {
        var entries: [JSONValue] = []
        if let entities = tweet.member("entities") {
            if case .array(let items) = entities {
                entries.append(contentsOf: items)
            } else if case .array(let items) = entities.member("media") {
                entries.append(contentsOf: items)
            }
        }
        for key in ["extended_entities", "extendedEntities"] {
            if let ext = tweet.object(key), case .array(let items) = ext.member("media") {
                entries.append(contentsOf: items)
            }
        }
        return entries
    }

    private static func isEntry(_ entry: JSONValue, ofTypes types: Set<String>) -> Bool {
        guard let t = entry.string("type") else { return false }
        return types.contains(t)
    }

    /// X 添付動画 (type: video / animated_gif) の抽出。tweet_media (idx は Python と
    /// 共有の契約) には載せず、表示時にのみ使う。preview の無い要素はサムネイルを
    /// 出せないためスキップ。入れ子 (quoted_status 等) は entities が常に空なので
    /// root のみ走査する。
    public static func extractVideoEntities(_ root: JSONValue) -> [VideoEntity] {
        guard root.isObject else { return [] }
        var result: [VideoEntity] = []
        var seen = Set<String>()
        for entry in iterEntityEntries(root) where isEntry(entry, ofTypes: videoTypes) {
            guard let pageUrl = entry.string("link"), !pageUrl.isEmpty,
                  let thumbnailUrl = entry.string("preview"), !thumbnailUrl.isEmpty
            else { continue }
            if seen.insert(pageUrl).inserted {
                result.append(VideoEntity(pageUrl: pageUrl, thumbnailUrl: thumbnailUrl))
            }
        }
        return result
    }

    private static func pickImageUrl(_ entry: JSONValue) -> String? {
        var candidates: [String] = []
        for key in urlKeys {
            if let s = entry.string(key), !s.isEmpty {
                candidates.append(s)
            }
        }
        if let pbs = candidates.first(where: { $0.contains("pbs.twimg.com") }) {
            return pbs
        }
        return candidates.first
    }

    /// media.to_original_size の拡張子規則 (ローカルファイル名再現用)。共有契約 #5。
    public static func extOf(_ url: String) -> String {
        let components = URLComponents(string: url)
        let isAbsolute = components?.scheme != nil && components?.host != nil
        let path = isAbsolute ? (components?.percentEncodedPath ?? url) : url
        let ext = firstGroup(pathExtRegex, in: path)
        if isAbsolute, components?.host?.contains("pbs.twimg.com") == true {
            if let ext { return ext }
            let format = components?.queryItems?.first(where: { $0.name == "format" })?.value
            return (format?.isEmpty == false) ? format! : "jpg"
        }
        return ext ?? "jpg"
    }

    private static func firstGroup(_ regex: NSRegularExpression, in text: String) -> String? {
        let ns = text as NSString
        guard let m = regex.firstMatch(in: text, range: NSRange(location: 0, length: ns.length)),
              m.range(at: 1).location != NSNotFound
        else { return nil }
        return ns.substring(with: m.range(at: 1))
    }

    /// カウントの取得。RT のときはトップレベルが 0 なら RT元にフォールバックする。
    /// (詳細は C# 実装のコメント参照: いいね等は RT元にしか無く、表示回数は外側にしか無い。
    /// 引用にはフォールバックしない — 外側の 0 も正当な値のため。)
    private static func count(_ root: JSONValue, _ rt: JSONValue?, _ key: String) -> Int64 {
        let value = root.int64(key)
        if value != 0 { return value }
        guard let rt else { return value }
        return rt.int64(key)
    }
}
