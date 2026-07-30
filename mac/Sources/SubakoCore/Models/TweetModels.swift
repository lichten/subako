import Foundation

/// tweets.tweet_type の値 (docs/data-layer.md §2 の種別判定)。
public enum TweetType: Int, Sendable, Equatable {
    case tweet = 0
    case retweet = 1
    case reply = 2
    case quote = 3
}

/// tweet_media.origin の値 (0=本文 / 1=引用先 / 2=RT元)。
public enum MediaOrigin: Int, Sendable, Equatable {
    case own = 0
    case quoted = 1
    case retweeted = 2
}

/// tweets テーブルの 1 行 (docs/data-layer.md §4.2)。
public struct TweetRow: Sendable, Equatable {
    public var tweetId: String
    public var idInt: Int64
    public var username: String
    public var authorUsername: String?
    public var authorDisplayName: String?
    public var authorIconUrl: String?
    public var createdAtUtc: String
    public var sortKey: Int64
    public var type: TweetType
    public var fullText: String
    public var lang: String?
    public var inReplyToUsername: String?
    public var rtUsername: String?
    public var rtDisplayName: String?
    public var rtText: String?
    public var rtIconUrl: String?
    public var quotedUsername: String?
    public var quotedDisplayName: String?
    public var quotedText: String?
    public var quotedIconUrl: String?
    public var likeCount: Int64
    public var retweetCount: Int64
    public var replyCount: Int64
    public var viewCount: Int64
    public var mediaCount: Int
    public var rawOffset: Int64
    public var rawLength: Int64
    /// ページ取得時の LEFT JOIN read_state 由来 (取込時は未使用)。
    public var isRead: Bool

    public init(
        tweetId: String,
        idInt: Int64,
        username: String,
        authorUsername: String? = nil,
        authorDisplayName: String? = nil,
        authorIconUrl: String? = nil,
        createdAtUtc: String = "",
        sortKey: Int64 = 0,
        type: TweetType = .tweet,
        fullText: String = "",
        lang: String? = nil,
        inReplyToUsername: String? = nil,
        rtUsername: String? = nil,
        rtDisplayName: String? = nil,
        rtText: String? = nil,
        rtIconUrl: String? = nil,
        quotedUsername: String? = nil,
        quotedDisplayName: String? = nil,
        quotedText: String? = nil,
        quotedIconUrl: String? = nil,
        likeCount: Int64 = 0,
        retweetCount: Int64 = 0,
        replyCount: Int64 = 0,
        viewCount: Int64 = 0,
        mediaCount: Int = 0,
        rawOffset: Int64 = 0,
        rawLength: Int64 = 0,
        isRead: Bool = false
    ) {
        self.tweetId = tweetId
        self.idInt = idInt
        self.username = username
        self.authorUsername = authorUsername
        self.authorDisplayName = authorDisplayName
        self.authorIconUrl = authorIconUrl
        self.createdAtUtc = createdAtUtc
        self.sortKey = sortKey
        self.type = type
        self.fullText = fullText
        self.lang = lang
        self.inReplyToUsername = inReplyToUsername
        self.rtUsername = rtUsername
        self.rtDisplayName = rtDisplayName
        self.rtText = rtText
        self.rtIconUrl = rtIconUrl
        self.quotedUsername = quotedUsername
        self.quotedDisplayName = quotedDisplayName
        self.quotedText = quotedText
        self.quotedIconUrl = quotedIconUrl
        self.likeCount = likeCount
        self.retweetCount = retweetCount
        self.replyCount = replyCount
        self.viewCount = viewCount
        self.mediaCount = mediaCount
        self.rawOffset = rawOffset
        self.rawLength = rawLength
        self.isRead = isRead
    }
}

/// tweet_media テーブルの 1 行。idx は 1 始まり (docs/data-layer.md §3)。
public struct TweetMediaRow: Sendable, Equatable {
    public let tweetId: String
    public let index: Int
    public let sourceUrl: String?
    public let ext: String
    public let origin: MediaOrigin

    public init(tweetId: String, index: Int, sourceUrl: String?, ext: String, origin: MediaOrigin) {
        self.tweetId = tweetId
        self.index = index
        self.sourceUrl = sourceUrl
        self.ext = ext
        self.origin = origin
    }
}

/// TweetJsonParser.parse の結果。
public struct ParsedTweet: Sendable {
    public let row: TweetRow
    public let media: [TweetMediaRow]
    public let authorIconUrl: String?

    public init(row: TweetRow, media: [TweetMediaRow], authorIconUrl: String?) {
        self.row = row
        self.media = media
        self.authorIconUrl = authorIconUrl
    }
}

/// X 添付動画。pageUrl = video.twimg.com の mp4、thumbnailUrl = preview (pbs.twimg.com)。
public struct VideoEntity: Sendable, Equatable {
    public let pageUrl: String
    public let thumbnailUrl: String

    public init(pageUrl: String, thumbnailUrl: String) {
        self.pageUrl = pageUrl
        self.thumbnailUrl = thumbnailUrl
    }
}
