using TweetViewer.Data;
using TweetViewer.Models;

namespace TweetViewer.Tests;

public class TweetJsonParserTests
{
    private static ParsedTweet ParseOk(string json) =>
        TweetJsonParser.Parse(json, "testuser", 0, json.Length)
        ?? throw new InvalidOperationException("parse failed");

    [Fact]
    public void PlainTweet()
    {
        var parsed = ParseOk("""
            {"id":"100","created_at":"Wed Apr 11 08:26:14 +0000 2007",
             "full_text":"hello","is_reply":false,"entities":[]}
            """.ReplaceLineEndings(""));
        Assert.Equal("100", parsed.Row.TweetId);
        Assert.Equal(TweetType.Tweet, parsed.Row.Type);
        Assert.Equal("hello", parsed.Row.FullText);
        var expected = new DateTimeOffset(2007, 4, 11, 8, 26, 14, TimeSpan.Zero).ToUnixTimeSeconds();
        Assert.Equal(expected, parsed.Row.SortKey);
        Assert.Equal("2007-04-11T08:26:14Z", parsed.Row.CreatedAtUtc);
    }

    [Fact]
    public void IdFallbackOrder_PrefersIdStr_AndAcceptsNumeric()
    {
        Assert.Equal("7", ParseOk("""{"id_str":"7","id":"8","full_text":"x"}""").Row.TweetId);
        Assert.Equal("42", ParseOk("""{"id":42,"full_text":"x"}""").Row.TweetId);
        Assert.Equal("9", ParseOk("""{"tweet_id":"9","full_text":"x"}""").Row.TweetId);
    }

    [Fact]
    public void Retweet()
    {
        var parsed = ParseOk("""
            {"id":"1","created_at":"Tue Jul 21 20:23:54 +0000 2026","full_text":"RT @a: 略",
             "user":{"username":"owner","profile_image_url":"https://pbs.twimg.com/profile_images/1/o_normal.jpg"},
             "retweeted_status":{"id":"2","full_text":"元の全文",
               "user":{"username":"alice","display_name":"Alice",
                 "profile_image_url":"https://pbs.twimg.com/profile_images/2/a_normal.jpg"}}}
            """.ReplaceLineEndings(""));
        Assert.Equal(TweetType.Retweet, parsed.Row.Type);
        Assert.Equal("alice", parsed.Row.RtUsername);
        Assert.Equal("Alice", parsed.Row.RtDisplayName);
        Assert.Equal("元の全文", parsed.Row.RtText);
        Assert.Equal("https://pbs.twimg.com/profile_images/2/a_normal.jpg", parsed.Row.RtIconUrl);
        Assert.Equal("https://pbs.twimg.com/profile_images/1/o_normal.jpg", parsed.AuthorIconUrl);
    }

    // 以下 3 件: RT のカウントは外側と RT元に分かれて入る (実データ 1,496 件で確認)。
    // いいねは RT元にしか、表示回数は外側にしか無く、RT 数は RT元が 0 の行がある。

    [Fact]
    public void RetweetCounts_FallBackToRetweetedStatus()
    {
        var parsed = ParseOk("""
            {"id":"1","full_text":"RT @a: 略",
             "reply_count":0,"retweet_count":113,"likes_count":0,"view_count":50,
             "retweeted_status":{"id":"2","full_text":"元の全文","user":{"username":"alice"},
               "reply_count":0,"retweet_count":0,"likes_count":42,"view_count":0}}
            """.ReplaceLineEndings(""));
        Assert.Equal(42, parsed.Row.LikeCount);      // 外側 0 → RT元から
        Assert.Equal(113, parsed.Row.RetweetCount);  // RT元が 0 なので外側を維持
        Assert.Equal(50, parsed.Row.ViewCount);      // RT元は常に 0
        Assert.Equal(0, parsed.Row.ReplyCount);      // どちらにも無い (API の欠損)
    }

    [Fact]
    public void RetweetCounts_KeepOuterWhenNonZero()
    {
        // 外側に値があるときは RT元で上書きしない
        var parsed = ParseOk("""
            {"id":"1","full_text":"RT @a: 略","retweet_count":10,"likes_count":7,
             "retweeted_status":{"id":"2","full_text":"元","user":{"username":"alice"},
               "retweet_count":999,"likes_count":999}}
            """.ReplaceLineEndings(""));
        Assert.Equal(10, parsed.Row.RetweetCount);
        Assert.Equal(7, parsed.Row.LikeCount);
    }

    [Fact]
    public void QuotedStatusCountsAreNotUsed()
    {
        // 引用ツイートは自分自身の投稿なので外側の値が正しい (0 も正当)
        var parsed = ParseOk("""
            {"id":"1","full_text":"引用します","is_quote_status":true,
             "reply_count":0,"retweet_count":0,"likes_count":0,"view_count":0,
             "quoted_status":{"id":"2","full_text":"元","user":{"username":"bob"},
               "reply_count":88,"retweet_count":77,"likes_count":999,"view_count":66}}
            """.ReplaceLineEndings(""));
        Assert.Equal(TweetType.Quote, parsed.Row.Type);
        Assert.Equal(0, parsed.Row.LikeCount);
        Assert.Equal(0, parsed.Row.RetweetCount);
        Assert.Equal(0, parsed.Row.ReplyCount);
        Assert.Equal(0, parsed.Row.ViewCount);
    }

    [Fact]
    public void PlainTweetCounts_ReadFromTopLevel()
    {
        var parsed = ParseOk("""
            {"id":"1","full_text":"x","reply_count":3,"retweet_count":10,
             "likes_count":25,"view_count":1200}
            """.ReplaceLineEndings(""));
        Assert.Equal(3, parsed.Row.ReplyCount);
        Assert.Equal(10, parsed.Row.RetweetCount);
        Assert.Equal(25, parsed.Row.LikeCount);
        Assert.Equal(1200, parsed.Row.ViewCount);
    }

    [Fact]
    public void AuthorFields_FilledFromUserObject()
    {
        var parsed = ParseOk("""
            {"id":"1","full_text":"hello",
             "user":{"username":"alice","display_name":"Alice",
               "profile_image_url":"https://pbs.twimg.com/profile_images/1/a_normal.jpg"}}
            """.ReplaceLineEndings(""));
        Assert.Equal("alice", parsed.Row.AuthorUsername);
        Assert.Equal("Alice", parsed.Row.AuthorDisplayName);
        Assert.Equal("https://pbs.twimg.com/profile_images/1/a_normal.jpg", parsed.Row.AuthorIconUrl);
    }

    [Fact]
    public void AuthorFields_NullWhenUserMissing()
    {
        var parsed = ParseOk("""{"id":"1","full_text":"hello"}""");
        Assert.Null(parsed.Row.AuthorUsername);
        Assert.Null(parsed.Row.AuthorDisplayName);
        Assert.Null(parsed.Row.AuthorIconUrl);
    }

    [Fact]
    public void Reply()
    {
        var parsed = ParseOk("""
            {"id":"1","full_text":"reply","is_reply":true,"in_reply_to_username":"bob"}
            """);
        Assert.Equal(TweetType.Reply, parsed.Row.Type);
        Assert.Equal("bob", parsed.Row.InReplyToUsername);
    }

    [Fact]
    public void Quote()
    {
        var parsed = ParseOk("""
            {"id":"1","full_text":"comment","is_quote_status":true,
             "quoted_status":{"id":"2","full_text":"quoted text",
               "user":{"username":"carol","display_name":"Carol",
                 "profile_image_url":"https://pbs.twimg.com/profile_images/3/c_normal.png"}}}
            """.ReplaceLineEndings(""));
        Assert.Equal(TweetType.Quote, parsed.Row.Type);
        Assert.Equal("carol", parsed.Row.QuotedUsername);
        Assert.Equal("quoted text", parsed.Row.QuotedText);
        Assert.Equal("https://pbs.twimg.com/profile_images/3/c_normal.png", parsed.Row.QuotedIconUrl);
        Assert.Null(parsed.AuthorIconUrl);
    }

    [Fact]
    public void RetweetWinsOverReplyAndQuote()
    {
        var parsed = ParseOk("""
            {"id":"1","full_text":"x","is_reply":true,"is_quote_status":true,
             "retweeted_status":{"id":"2","full_text":"y","user":{"username":"a"}}}
            """.ReplaceLineEndings(""));
        Assert.Equal(TweetType.Retweet, parsed.Row.Type);
    }

    [Fact]
    public void MediaFromEntitiesList_SorsaFormat()
    {
        var parsed = ParseOk("""
            {"id":"55","full_text":"pic",
             "entities":[{"type":"photo","link":"https://pbs.twimg.com/media/ABC123.jpg","preview":""},
                         {"type":"photo","link":"https://pbs.twimg.com/media/DEF456.png","preview":""}]}
            """.ReplaceLineEndings(""));
        Assert.Equal(2, parsed.Media.Count);
        Assert.Equal(new TweetMediaRow("55", 1, "https://pbs.twimg.com/media/ABC123.jpg", "jpg", MediaOrigin.Own), parsed.Media[0]);
        Assert.Equal(new TweetMediaRow("55", 2, "https://pbs.twimg.com/media/DEF456.png", "png", MediaOrigin.Own), parsed.Media[1]);
        Assert.Equal(2, parsed.Row.MediaCount);
    }

    [Fact]
    public void MediaExtFromFormatQuery()
    {
        Assert.Equal("png", TweetJsonParser.ExtOf("https://pbs.twimg.com/media/ABC?format=png&name=large"));
        Assert.Equal("jpg", TweetJsonParser.ExtOf("https://pbs.twimg.com/media/ABC"));
        Assert.Equal("gif", TweetJsonParser.ExtOf("https://example.com/x.gif"));
        Assert.Equal("jpg", TweetJsonParser.ExtOf("https://example.com/noext"));
    }

    [Fact]
    public void MediaFromQuotedStatus_DedupedAcrossTargets()
    {
        var parsed = ParseOk("""
            {"id":"77","full_text":"q",
             "entities":[{"type":"photo","link":"https://pbs.twimg.com/media/SAME.jpg"}],
             "quoted_status":{"id":"78","full_text":"inner",
               "entities":[{"type":"photo","link":"https://pbs.twimg.com/media/SAME.jpg"},
                           {"type":"photo","link":"https://pbs.twimg.com/media/OTHER.jpg"}]}}
            """.ReplaceLineEndings(""));
        Assert.Equal(2, parsed.Media.Count);
        Assert.Equal(1, parsed.Media[0].Index);
        Assert.Equal("https://pbs.twimg.com/media/SAME.jpg", parsed.Media[0].SourceUrl);
        Assert.Equal(MediaOrigin.Own, parsed.Media[0].Origin);       // 本文優先 (先勝ち)
        Assert.Equal("https://pbs.twimg.com/media/OTHER.jpg", parsed.Media[1].SourceUrl);
        Assert.Equal(MediaOrigin.Quoted, parsed.Media[1].Origin);
    }

    [Fact]
    public void MediaOriginFromRetweetedStatus()
    {
        var parsed = ParseOk("""
            {"id":"88","full_text":"RT @a: x",
             "retweeted_status":{"id":"89","full_text":"x","user":{"username":"a"},
               "entities":[{"type":"photo","link":"https://pbs.twimg.com/media/RTIMG.jpg"}]}}
            """.ReplaceLineEndings(""));
        Assert.Single(parsed.Media);
        Assert.Equal(MediaOrigin.Retweeted, parsed.Media[0].Origin);
    }

    // 以下 4 件: 入れ子ツイート (quoted_status / retweeted_status) の entities は
    // Sorsa API 実挙動として常に空。展開済みの本文 URL からのフォールバック抽出を検証する。

    [Fact]
    public void MediaFromQuotedFullText_WhenEntitiesEmpty()
    {
        var parsed = ParseOk("""
            {"id":"90","full_text":"見て","entities":[],
             "quoted_status":{"id":"91","full_text":"猫 https://pbs.twimg.com/media/QUOTED1.jpg",
               "entities":[]}}
            """.ReplaceLineEndings(""));
        Assert.Single(parsed.Media);
        Assert.Equal(
            new TweetMediaRow("90", 1, "https://pbs.twimg.com/media/QUOTED1.jpg", "jpg", MediaOrigin.Quoted),
            parsed.Media[0]);
        Assert.Equal(1, parsed.Row.MediaCount);
    }

    [Fact]
    public void MediaFromQuotedFullText_AfterOwnEntities()
    {
        var parsed = ParseOk("""
            {"id":"92","full_text":"本文 https://pbs.twimg.com/media/IGNORED.jpg",
             "entities":[{"type":"photo","link":"https://pbs.twimg.com/media/OWN.jpg"}],
             "quoted_status":{"id":"93","full_text":"引用 https://pbs.twimg.com/media/QUOTED2.jpg",
               "entities":[]}}
            """.ReplaceLineEndings(""));
        // 本文は entities があるので full_text は見ない (IGNORED.jpg は拾わない)
        Assert.Equal(2, parsed.Media.Count);
        Assert.Equal("https://pbs.twimg.com/media/OWN.jpg", parsed.Media[0].SourceUrl);
        Assert.Equal(MediaOrigin.Own, parsed.Media[0].Origin);
        Assert.Equal(2, parsed.Media[1].Index);
        Assert.Equal("https://pbs.twimg.com/media/QUOTED2.jpg", parsed.Media[1].SourceUrl);
        Assert.Equal(MediaOrigin.Quoted, parsed.Media[1].Origin);
    }

    [Fact]
    public void MediaFromFullText_IgnoresNonMediaUrlsAndTrailingPunctuation()
    {
        var parsed = ParseOk("""
            {"id":"94","full_text":"x","entities":[],
             "quoted_status":{"id":"95","entities":[],
               "full_text":"記事 https://qiita.com/a/b と引用 https://x.com/u/status/1 と画像https://pbs.twimg.com/media/TRIM.jpg。続く"}}
            """.ReplaceLineEndings(""));
        // pbs.twimg.com/media 以外は拾わない。末尾の句読点も巻き込まない
        Assert.Single(parsed.Media);
        Assert.Equal("https://pbs.twimg.com/media/TRIM.jpg", parsed.Media[0].SourceUrl);
    }

    [Fact]
    public void MediaFromRetweetedFullText_WhenEntitiesEmpty()
    {
        var parsed = ParseOk("""
            {"id":"96","full_text":"RT @a: x","entities":[],
             "retweeted_status":{"id":"97","full_text":"x https://pbs.twimg.com/media/RTTEXT.jpg",
               "user":{"username":"a"},"entities":[]}}
            """.ReplaceLineEndings(""));
        Assert.Single(parsed.Media);
        Assert.Equal("https://pbs.twimg.com/media/RTTEXT.jpg", parsed.Media[0].SourceUrl);
        Assert.Equal(MediaOrigin.Retweeted, parsed.Media[0].Origin);
    }

    [Fact]
    public void MediaFromQuotedFullText_DedupedAgainstOwnEntities()
    {
        var parsed = ParseOk("""
            {"id":"98","full_text":"x",
             "entities":[{"type":"photo","link":"https://pbs.twimg.com/media/DUP.jpg"}],
             "quoted_status":{"id":"99","full_text":"y https://pbs.twimg.com/media/DUP.jpg","entities":[]}}
            """.ReplaceLineEndings(""));
        // 同一 URL は本文優先で 1 件のみ
        Assert.Single(parsed.Media);
        Assert.Equal(MediaOrigin.Own, parsed.Media[0].Origin);
    }

    [Fact]
    public void IsoCreatedAtVariants()
    {
        Assert.Equal("2018-10-10T20:19:24Z",
            ParseOk("""{"id":"1","created_at":"2018-10-10T20:19:24+00:00","full_text":"x"}""").Row.CreatedAtUtc);
        Assert.Equal("2018-10-10T20:19:24Z",
            ParseOk("""{"id":"1","created_at":"2018-10-10T20:19:24Z","full_text":"x"}""").Row.CreatedAtUtc);
    }

    [Fact]
    public void UnparsableCreatedAt_SinksToSortKeyZero()
    {
        var parsed = ParseOk("""{"id":"1","created_at":"not a date","full_text":"x"}""");
        Assert.Equal(0, parsed.Row.SortKey);
        Assert.Equal("", parsed.Row.CreatedAtUtc);
    }

    [Fact]
    public void BrokenLinesReturnNull()
    {
        Assert.Null(TweetJsonParser.Parse("{broken json", "u", 0, 12));
        Assert.Null(TweetJsonParser.Parse("[1,2,3]", "u", 0, 7));
        Assert.Null(TweetJsonParser.Parse("""{"full_text":"no id"}""", "u", 0, 21));
    }

    // 以下: X 添付動画 (video エンティティ) の抽出。tweet_media (idx は Python と共有) には
    // 載せず ExtractVideoEntities で表示時にのみ拾う。

    private static IReadOnlyList<VideoEntity> ExtractVideos(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return TweetJsonParser.ExtractVideoEntities(doc.RootElement);
    }

    [Fact]
    public void VideoEntities_ExtractedFromEntitiesList_SorsaFormat()
    {
        var videos = ExtractVideos("""
            {"id":"200","full_text":"v",
             "entities":[{"type":"video","link":"https://video.twimg.com/ext_tw_video/1/pu/vid/480x270/a.mp4?tag=12",
                          "preview":"https://pbs.twimg.com/ext_tw_video_thumb/1/pu/img/b.jpg"},
                         {"type":"photo","link":"https://pbs.twimg.com/media/ABC.jpg"}]}
            """.ReplaceLineEndings(""));
        var video = Assert.Single(videos);
        Assert.Equal("https://video.twimg.com/ext_tw_video/1/pu/vid/480x270/a.mp4?tag=12", video.PageUrl);
        Assert.Equal("https://pbs.twimg.com/ext_tw_video_thumb/1/pu/img/b.jpg", video.ThumbnailUrl);
    }

    [Fact]
    public void VideoEntities_AnimatedGifIncluded()
    {
        var videos = ExtractVideos("""
            {"id":"201","entities":[{"type":"animated_gif",
              "link":"https://video.twimg.com/tweet_video/x.mp4",
              "preview":"https://pbs.twimg.com/tweet_video_thumb/x.jpg"}]}
            """.ReplaceLineEndings(""));
        Assert.Single(videos);
    }

    [Fact]
    public void VideoEntities_MissingPreviewOrLinkSkipped_AndDeduped()
    {
        var videos = ExtractVideos("""
            {"id":"202","entities":[
              {"type":"video","link":"https://video.twimg.com/a.mp4"},
              {"type":"video","preview":"https://pbs.twimg.com/b.jpg"},
              {"type":"video","link":"https://video.twimg.com/c.mp4","preview":"https://pbs.twimg.com/c.jpg"},
              {"type":"video","link":"https://video.twimg.com/c.mp4","preview":"https://pbs.twimg.com/c2.jpg"}]}
            """.ReplaceLineEndings(""));
        var video = Assert.Single(videos);
        Assert.Equal("https://video.twimg.com/c.mp4", video.PageUrl);
        Assert.Equal("https://pbs.twimg.com/c.jpg", video.ThumbnailUrl);
    }

    [Fact]
    public void VideoEntities_NestedTweetsNotScanned()
    {
        // 入れ子は entities が常に空という前提だが、仮に入っていても root のみ対象
        var videos = ExtractVideos("""
            {"id":"203","entities":[],
             "quoted_status":{"id":"204","entities":[{"type":"video",
               "link":"https://video.twimg.com/q.mp4","preview":"https://pbs.twimg.com/q.jpg"}]}}
            """.ReplaceLineEndings(""));
        Assert.Empty(videos);
    }

    [Fact]
    public void VideoEntities_DoNotConsumePhotoIndex()
    {
        // 回帰ガード: photo の idx は Python (media.extract_photo_urls) と共有の契約。
        // video が混ざっても photo の idx が従来通り 1,2 で採番されること
        var parsed = ParseOk("""
            {"id":"205","full_text":"mix",
             "entities":[{"type":"photo","link":"https://pbs.twimg.com/media/P1.jpg"},
                         {"type":"video","link":"https://video.twimg.com/v.mp4","preview":"https://pbs.twimg.com/v.jpg"},
                         {"type":"photo","link":"https://pbs.twimg.com/media/P2.jpg"}]}
            """.ReplaceLineEndings(""));
        Assert.Equal(2, parsed.Media.Count);
        Assert.Equal(new TweetMediaRow("205", 1, "https://pbs.twimg.com/media/P1.jpg", "jpg", MediaOrigin.Own), parsed.Media[0]);
        Assert.Equal(new TweetMediaRow("205", 2, "https://pbs.twimg.com/media/P2.jpg", "jpg", MediaOrigin.Own), parsed.Media[1]);
        Assert.Equal(2, parsed.Row.MediaCount);
    }
}
