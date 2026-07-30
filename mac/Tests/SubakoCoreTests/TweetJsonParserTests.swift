import Foundation
import Testing
@testable import SubakoCore

// C# TweetJsonParserTests.cs の移植 (共有契約 #1–#5 の検証)。

private func parseOk(
    _ json: String,
    sourceLocation: SourceLocation = #_sourceLocation
) throws -> ParsedTweet {
    try #require(
        TweetJsonParser.parse(json, username: "testuser", rawOffset: 0, rawLength: Int64(json.utf8.count)),
        sourceLocation: sourceLocation)
}

@Suite struct TweetJsonParserTests {
    @Test func plainTweet() throws {
        let parsed = try parseOk(
            #"{"id":"100","created_at":"Wed Apr 11 08:26:14 +0000 2007","full_text":"hello","is_reply":false,"entities":[]}"#)
        #expect(parsed.row.tweetId == "100")
        #expect(parsed.row.type == .tweet)
        #expect(parsed.row.fullText == "hello")
        var comps = DateComponents()
        (comps.year, comps.month, comps.day) = (2007, 4, 11)
        (comps.hour, comps.minute, comps.second) = (8, 26, 14)
        comps.timeZone = TimeZone(identifier: "UTC")
        let expected = Calendar(identifier: .gregorian).date(from: comps)!
        #expect(parsed.row.sortKey == Int64(expected.timeIntervalSince1970))
        #expect(parsed.row.createdAtUtc == "2007-04-11T08:26:14Z")
    }

    @Test func idFallbackOrder_PrefersIdStr_AndAcceptsNumeric() throws {
        #expect(try parseOk(#"{"id_str":"7","id":"8","full_text":"x"}"#).row.tweetId == "7")
        #expect(try parseOk(#"{"id":42,"full_text":"x"}"#).row.tweetId == "42")
        #expect(try parseOk(#"{"tweet_id":"9","full_text":"x"}"#).row.tweetId == "9")
    }

    @Test func retweet() throws {
        let parsed = try parseOk(#"""
            {"id":"1","created_at":"Tue Jul 21 20:23:54 +0000 2026","full_text":"RT @a: 略",
             "user":{"username":"owner","profile_image_url":"https://pbs.twimg.com/profile_images/1/o_normal.jpg"},
             "retweeted_status":{"id":"2","full_text":"元の全文",
               "user":{"username":"alice","display_name":"Alice",
                 "profile_image_url":"https://pbs.twimg.com/profile_images/2/a_normal.jpg"}}}
            """#.replacingOccurrences(of: "\n", with: ""))
        #expect(parsed.row.type == .retweet)
        #expect(parsed.row.rtUsername == "alice")
        #expect(parsed.row.rtDisplayName == "Alice")
        #expect(parsed.row.rtText == "元の全文")
        #expect(parsed.row.rtIconUrl == "https://pbs.twimg.com/profile_images/2/a_normal.jpg")
        #expect(parsed.authorIconUrl == "https://pbs.twimg.com/profile_images/1/o_normal.jpg")
    }

    // 以下 3 件: RT のカウントは外側と RT元に分かれて入る (実データ 1,496 件で確認)。

    @Test func retweetCounts_FallBackToRetweetedStatus() throws {
        let parsed = try parseOk(#"""
            {"id":"1","full_text":"RT @a: 略",
             "reply_count":0,"retweet_count":113,"likes_count":0,"view_count":50,
             "retweeted_status":{"id":"2","full_text":"元の全文","user":{"username":"alice"},
               "reply_count":0,"retweet_count":0,"likes_count":42,"view_count":0}}
            """#.replacingOccurrences(of: "\n", with: ""))
        #expect(parsed.row.likeCount == 42)      // 外側 0 → RT元から
        #expect(parsed.row.retweetCount == 113)  // RT元が 0 なので外側を維持
        #expect(parsed.row.viewCount == 50)      // RT元は常に 0
        #expect(parsed.row.replyCount == 0)      // どちらにも無い (API の欠損)
    }

    @Test func retweetCounts_KeepOuterWhenNonZero() throws {
        let parsed = try parseOk(#"""
            {"id":"1","full_text":"RT @a: 略","retweet_count":10,"likes_count":7,
             "retweeted_status":{"id":"2","full_text":"元","user":{"username":"alice"},
               "retweet_count":999,"likes_count":999}}
            """#.replacingOccurrences(of: "\n", with: ""))
        #expect(parsed.row.retweetCount == 10)
        #expect(parsed.row.likeCount == 7)
    }

    @Test func quotedStatusCountsAreNotUsed() throws {
        // 引用ツイートは自分自身の投稿なので外側の値が正しい (0 も正当)
        let parsed = try parseOk(#"""
            {"id":"1","full_text":"引用します","is_quote_status":true,
             "reply_count":0,"retweet_count":0,"likes_count":0,"view_count":0,
             "quoted_status":{"id":"2","full_text":"元","user":{"username":"bob"},
               "reply_count":88,"retweet_count":77,"likes_count":999,"view_count":66}}
            """#.replacingOccurrences(of: "\n", with: ""))
        #expect(parsed.row.type == .quote)
        #expect(parsed.row.likeCount == 0)
        #expect(parsed.row.retweetCount == 0)
        #expect(parsed.row.replyCount == 0)
        #expect(parsed.row.viewCount == 0)
    }

    @Test func plainTweetCounts_ReadFromTopLevel() throws {
        let parsed = try parseOk(
            #"{"id":"1","full_text":"x","reply_count":3,"retweet_count":10,"likes_count":25,"view_count":1200}"#)
        #expect(parsed.row.replyCount == 3)
        #expect(parsed.row.retweetCount == 10)
        #expect(parsed.row.likeCount == 25)
        #expect(parsed.row.viewCount == 1200)
    }

    @Test func authorFields_FilledFromUserObject() throws {
        let parsed = try parseOk(#"""
            {"id":"1","full_text":"hello",
             "user":{"username":"alice","display_name":"Alice",
               "profile_image_url":"https://pbs.twimg.com/profile_images/1/a_normal.jpg"}}
            """#.replacingOccurrences(of: "\n", with: ""))
        #expect(parsed.row.authorUsername == "alice")
        #expect(parsed.row.authorDisplayName == "Alice")
        #expect(parsed.row.authorIconUrl == "https://pbs.twimg.com/profile_images/1/a_normal.jpg")
    }

    @Test func authorFields_NilWhenUserMissing() throws {
        let parsed = try parseOk(#"{"id":"1","full_text":"hello"}"#)
        #expect(parsed.row.authorUsername == nil)
        #expect(parsed.row.authorDisplayName == nil)
        #expect(parsed.row.authorIconUrl == nil)
    }

    @Test func reply() throws {
        let parsed = try parseOk(
            #"{"id":"1","full_text":"reply","is_reply":true,"in_reply_to_username":"bob"}"#)
        #expect(parsed.row.type == .reply)
        #expect(parsed.row.inReplyToUsername == "bob")
    }

    @Test func replyByStringInReplyToId_NumericIdIsNotReply() throws {
        // in_reply_to_tweet_id は JSON 文字列のときのみ Reply (docs/data-layer.md §2)
        #expect(try parseOk(#"{"id":"1","full_text":"x","in_reply_to_tweet_id":"5"}"#).row.type == .reply)
        #expect(try parseOk(#"{"id":"1","full_text":"x","in_reply_to_tweet_id":5}"#).row.type == .tweet)
    }

    @Test func quote() throws {
        let parsed = try parseOk(#"""
            {"id":"1","full_text":"comment","is_quote_status":true,
             "quoted_status":{"id":"2","full_text":"quoted text",
               "user":{"username":"carol","display_name":"Carol",
                 "profile_image_url":"https://pbs.twimg.com/profile_images/3/c_normal.png"}}}
            """#.replacingOccurrences(of: "\n", with: ""))
        #expect(parsed.row.type == .quote)
        #expect(parsed.row.quotedUsername == "carol")
        #expect(parsed.row.quotedText == "quoted text")
        #expect(parsed.row.quotedIconUrl == "https://pbs.twimg.com/profile_images/3/c_normal.png")
        #expect(parsed.authorIconUrl == nil)
    }

    @Test func retweetWinsOverReplyAndQuote() throws {
        let parsed = try parseOk(#"""
            {"id":"1","full_text":"x","is_reply":true,"is_quote_status":true,
             "retweeted_status":{"id":"2","full_text":"y","user":{"username":"a"}}}
            """#.replacingOccurrences(of: "\n", with: ""))
        #expect(parsed.row.type == .retweet)
    }

    @Test func mediaFromEntitiesList_SorsaFormat() throws {
        let parsed = try parseOk(#"""
            {"id":"55","full_text":"pic",
             "entities":[{"type":"photo","link":"https://pbs.twimg.com/media/ABC123.jpg","preview":""},
                         {"type":"photo","link":"https://pbs.twimg.com/media/DEF456.png","preview":""}]}
            """#.replacingOccurrences(of: "\n", with: ""))
        #expect(parsed.media.count == 2)
        #expect(parsed.media[0] == TweetMediaRow(
            tweetId: "55", index: 1, sourceUrl: "https://pbs.twimg.com/media/ABC123.jpg",
            ext: "jpg", origin: .own))
        #expect(parsed.media[1] == TweetMediaRow(
            tweetId: "55", index: 2, sourceUrl: "https://pbs.twimg.com/media/DEF456.png",
            ext: "png", origin: .own))
        #expect(parsed.row.mediaCount == 2)
    }

    @Test func mediaExtFromFormatQuery() {
        #expect(TweetJsonParser.extOf("https://pbs.twimg.com/media/ABC?format=png&name=large") == "png")
        #expect(TweetJsonParser.extOf("https://pbs.twimg.com/media/ABC") == "jpg")
        #expect(TweetJsonParser.extOf("https://example.com/x.gif") == "gif")
        #expect(TweetJsonParser.extOf("https://example.com/noext") == "jpg")
    }

    @Test func mediaFromQuotedStatus_DedupedAcrossTargets() throws {
        let parsed = try parseOk(#"""
            {"id":"77","full_text":"q",
             "entities":[{"type":"photo","link":"https://pbs.twimg.com/media/SAME.jpg"}],
             "quoted_status":{"id":"78","full_text":"inner",
               "entities":[{"type":"photo","link":"https://pbs.twimg.com/media/SAME.jpg"},
                           {"type":"photo","link":"https://pbs.twimg.com/media/OTHER.jpg"}]}}
            """#.replacingOccurrences(of: "\n", with: ""))
        #expect(parsed.media.count == 2)
        #expect(parsed.media[0].index == 1)
        #expect(parsed.media[0].sourceUrl == "https://pbs.twimg.com/media/SAME.jpg")
        #expect(parsed.media[0].origin == .own)       // 本文優先 (先勝ち)
        #expect(parsed.media[1].sourceUrl == "https://pbs.twimg.com/media/OTHER.jpg")
        #expect(parsed.media[1].origin == .quoted)
    }

    @Test func mediaOriginFromRetweetedStatus() throws {
        let parsed = try parseOk(#"""
            {"id":"88","full_text":"RT @a: x",
             "retweeted_status":{"id":"89","full_text":"x","user":{"username":"a"},
               "entities":[{"type":"photo","link":"https://pbs.twimg.com/media/RTIMG.jpg"}]}}
            """#.replacingOccurrences(of: "\n", with: ""))
        #expect(parsed.media.count == 1)
        #expect(parsed.media[0].origin == .retweeted)
    }

    // 以下: 入れ子ツイートの entities は Sorsa API 実挙動として常に空。
    // 展開済みの本文 URL からのフォールバック抽出を検証する。

    @Test func mediaFromQuotedFullText_WhenEntitiesEmpty() throws {
        let parsed = try parseOk(#"""
            {"id":"90","full_text":"見て","entities":[],
             "quoted_status":{"id":"91","full_text":"猫 https://pbs.twimg.com/media/QUOTED1.jpg",
               "entities":[]}}
            """#.replacingOccurrences(of: "\n", with: ""))
        #expect(parsed.media == [TweetMediaRow(
            tweetId: "90", index: 1, sourceUrl: "https://pbs.twimg.com/media/QUOTED1.jpg",
            ext: "jpg", origin: .quoted)])
        #expect(parsed.row.mediaCount == 1)
    }

    @Test func mediaFromQuotedFullText_AfterOwnEntities() throws {
        let parsed = try parseOk(#"""
            {"id":"92","full_text":"本文 https://pbs.twimg.com/media/IGNORED.jpg",
             "entities":[{"type":"photo","link":"https://pbs.twimg.com/media/OWN.jpg"}],
             "quoted_status":{"id":"93","full_text":"引用 https://pbs.twimg.com/media/QUOTED2.jpg",
               "entities":[]}}
            """#.replacingOccurrences(of: "\n", with: ""))
        // 本文は entities があるので full_text は見ない (IGNORED.jpg は拾わない)
        #expect(parsed.media.count == 2)
        #expect(parsed.media[0].sourceUrl == "https://pbs.twimg.com/media/OWN.jpg")
        #expect(parsed.media[0].origin == .own)
        #expect(parsed.media[1].index == 2)
        #expect(parsed.media[1].sourceUrl == "https://pbs.twimg.com/media/QUOTED2.jpg")
        #expect(parsed.media[1].origin == .quoted)
    }

    @Test func mediaFromFullText_IgnoresNonMediaUrlsAndTrailingPunctuation() throws {
        let parsed = try parseOk(#"""
            {"id":"94","full_text":"x","entities":[],
             "quoted_status":{"id":"95","entities":[],
               "full_text":"記事 https://qiita.com/a/b と引用 https://x.com/u/status/1 と画像https://pbs.twimg.com/media/TRIM.jpg。続く"}}
            """#.replacingOccurrences(of: "\n", with: ""))
        // pbs.twimg.com/media 以外は拾わない。末尾の句読点も巻き込まない
        #expect(parsed.media.count == 1)
        #expect(parsed.media[0].sourceUrl == "https://pbs.twimg.com/media/TRIM.jpg")
    }

    @Test func mediaFromRetweetedFullText_WhenEntitiesEmpty() throws {
        let parsed = try parseOk(#"""
            {"id":"96","full_text":"RT @a: x","entities":[],
             "retweeted_status":{"id":"97","full_text":"x https://pbs.twimg.com/media/RTTEXT.jpg",
               "user":{"username":"a"},"entities":[]}}
            """#.replacingOccurrences(of: "\n", with: ""))
        #expect(parsed.media.count == 1)
        #expect(parsed.media[0].sourceUrl == "https://pbs.twimg.com/media/RTTEXT.jpg")
        #expect(parsed.media[0].origin == .retweeted)
    }

    @Test func mediaFromQuotedFullText_DedupedAgainstOwnEntities() throws {
        let parsed = try parseOk(#"""
            {"id":"98","full_text":"x",
             "entities":[{"type":"photo","link":"https://pbs.twimg.com/media/DUP.jpg"}],
             "quoted_status":{"id":"99","full_text":"y https://pbs.twimg.com/media/DUP.jpg","entities":[]}}
            """#.replacingOccurrences(of: "\n", with: ""))
        // 同一 URL は本文優先で 1 件のみ
        #expect(parsed.media.count == 1)
        #expect(parsed.media[0].origin == .own)
    }

    @Test func isoCreatedAtVariants() throws {
        #expect(try parseOk(
            #"{"id":"1","created_at":"2018-10-10T20:19:24+00:00","full_text":"x"}"#
        ).row.createdAtUtc == "2018-10-10T20:19:24Z")
        #expect(try parseOk(
            #"{"id":"1","created_at":"2018-10-10T20:19:24Z","full_text":"x"}"#
        ).row.createdAtUtc == "2018-10-10T20:19:24Z")
    }

    @Test func unparsableCreatedAt_SinksToSortKeyZero() throws {
        let parsed = try parseOk(#"{"id":"1","created_at":"not a date","full_text":"x"}"#)
        #expect(parsed.row.sortKey == 0)
        #expect(parsed.row.createdAtUtc == "")
    }

    @Test func brokenLinesReturnNil() {
        #expect(TweetJsonParser.parse("{broken json", username: "u", rawOffset: 0, rawLength: 12) == nil)
        #expect(TweetJsonParser.parse("[1,2,3]", username: "u", rawOffset: 0, rawLength: 7) == nil)
        #expect(TweetJsonParser.parse(#"{"full_text":"no id"}"#, username: "u", rawOffset: 0, rawLength: 21) == nil)
    }

    @Test func nonNumericId_IdIntFallsToZero() throws {
        // 非数値 ID / 符号付きは id_int = 0 に落とす (共有契約: 全実装で同じ値)
        #expect(try parseOk(#"{"id":"abc123","full_text":"x"}"#).row.idInt == 0)
        #expect(try parseOk(#"{"id":"-5","full_text":"x"}"#).row.idInt == 0)
        #expect(try parseOk(#"{"id":"100","full_text":"x"}"#).row.idInt == 100)
    }
}

// X 添付動画 (video エンティティ) の抽出。tweet_media (idx は Python と共有) には
// 載せず extractVideoEntities で表示時にのみ拾う。
@Suite struct VideoEntityTests {
    private func extractVideos(
        _ json: String,
        sourceLocation: SourceLocation = #_sourceLocation
    ) throws -> [VideoEntity] {
        let root = try #require(JSONValue.parseLine(json), sourceLocation: sourceLocation)
        return TweetJsonParser.extractVideoEntities(root)
    }

    @Test func extractedFromEntitiesList_SorsaFormat() throws {
        let videos = try extractVideos(#"""
            {"id":"200","full_text":"v",
             "entities":[{"type":"video","link":"https://video.twimg.com/ext_tw_video/1/pu/vid/480x270/a.mp4?tag=12",
                          "preview":"https://pbs.twimg.com/ext_tw_video_thumb/1/pu/img/b.jpg"},
                         {"type":"photo","link":"https://pbs.twimg.com/media/ABC.jpg"}]}
            """#.replacingOccurrences(of: "\n", with: ""))
        #expect(videos == [VideoEntity(
            pageUrl: "https://video.twimg.com/ext_tw_video/1/pu/vid/480x270/a.mp4?tag=12",
            thumbnailUrl: "https://pbs.twimg.com/ext_tw_video_thumb/1/pu/img/b.jpg")])
    }

    @Test func animatedGifIncluded() throws {
        let videos = try extractVideos(#"""
            {"id":"201","entities":[{"type":"animated_gif",
              "link":"https://video.twimg.com/tweet_video/x.mp4",
              "preview":"https://pbs.twimg.com/tweet_video_thumb/x.jpg"}]}
            """#.replacingOccurrences(of: "\n", with: ""))
        #expect(videos.count == 1)
    }

    @Test func missingPreviewOrLinkSkipped_AndDeduped() throws {
        let videos = try extractVideos(#"""
            {"id":"202","entities":[
              {"type":"video","link":"https://video.twimg.com/a.mp4"},
              {"type":"video","preview":"https://pbs.twimg.com/b.jpg"},
              {"type":"video","link":"https://video.twimg.com/c.mp4","preview":"https://pbs.twimg.com/c.jpg"},
              {"type":"video","link":"https://video.twimg.com/c.mp4","preview":"https://pbs.twimg.com/c2.jpg"}]}
            """#.replacingOccurrences(of: "\n", with: ""))
        #expect(videos == [VideoEntity(
            pageUrl: "https://video.twimg.com/c.mp4",
            thumbnailUrl: "https://pbs.twimg.com/c.jpg")])
    }

    @Test func nestedTweetsNotScanned() throws {
        // 入れ子は entities が常に空という前提だが、仮に入っていても root のみ対象
        let videos = try extractVideos(#"""
            {"id":"203","entities":[],
             "quoted_status":{"id":"204","entities":[{"type":"video",
               "link":"https://video.twimg.com/q.mp4","preview":"https://pbs.twimg.com/q.jpg"}]}}
            """#.replacingOccurrences(of: "\n", with: ""))
        #expect(videos.isEmpty)
    }

    @Test func videosDoNotConsumePhotoIndex() throws {
        // 回帰ガード: photo の idx は Python (media.extract_photo_urls) と共有の契約。
        // video が混ざっても photo の idx が従来通り 1,2 で採番されること
        let parsed = try parseOk(#"""
            {"id":"205","full_text":"mix",
             "entities":[{"type":"photo","link":"https://pbs.twimg.com/media/P1.jpg"},
                         {"type":"video","link":"https://video.twimg.com/v.mp4","preview":"https://pbs.twimg.com/v.jpg"},
                         {"type":"photo","link":"https://pbs.twimg.com/media/P2.jpg"}]}
            """#.replacingOccurrences(of: "\n", with: ""))
        #expect(parsed.media.count == 2)
        #expect(parsed.media[0] == TweetMediaRow(
            tweetId: "205", index: 1, sourceUrl: "https://pbs.twimg.com/media/P1.jpg",
            ext: "jpg", origin: .own))
        #expect(parsed.media[1] == TweetMediaRow(
            tweetId: "205", index: 2, sourceUrl: "https://pbs.twimg.com/media/P2.jpg",
            ext: "jpg", origin: .own))
        #expect(parsed.row.mediaCount == 2)
    }
}
