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
}
