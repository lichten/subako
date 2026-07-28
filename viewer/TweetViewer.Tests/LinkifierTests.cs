using TweetViewer.Services;

namespace TweetViewer.Tests;

public class LinkifierTests
{
    private static string Reassemble(IEnumerable<Linkifier.Segment> segments) =>
        string.Concat(segments.Select(s => s.Text));

    [Fact]
    public void NoUrl_SingleTextSegment()
    {
        var segs = Linkifier.Split("URL のないテキスト🚀");
        Assert.Single(segs);
        Assert.False(segs[0].IsUrl);
    }

    [Fact]
    public void UrlInMiddle()
    {
        var text = "詳細は https://example.com/page を見て";
        var segs = Linkifier.Split(text);
        Assert.Equal(3, segs.Count);
        Assert.Equal("https://example.com/page", segs[1].Text);
        Assert.True(segs[1].IsUrl);
        Assert.Equal(text, Reassemble(segs));
    }

    [Fact]
    public void UrlAtStartAndEnd()
    {
        var segs = Linkifier.Split("https://a.example/x 中間 http://b.example/y");
        Assert.Equal(3, segs.Count);
        Assert.True(segs[0].IsUrl);
        Assert.True(segs[2].IsUrl);
    }

    [Fact]
    public void TrailingJapanesePunctuationExcluded()
    {
        var text = "これ https://example.com/abc 。次の文";
        var segs = Linkifier.Split(text);
        Assert.Equal("https://example.com/abc", segs.Single(s => s.IsUrl).Text);
        Assert.Equal(text, Reassemble(segs));

        var text2 = "(https://example.com/abc)";
        var segs2 = Linkifier.Split(text2);
        Assert.Equal("https://example.com/abc", segs2.Single(s => s.IsUrl).Text);
        Assert.Equal(text2, Reassemble(segs2));
    }

    [Fact]
    public void RealTweetPattern()
    {
        var text = "新刊「…」 https://ch.nicovideo.jp/examplech/blomaga/ar1234567 @example_ch #例のチャンネル";
        var segs = Linkifier.Split(text);
        Assert.Equal("https://ch.nicovideo.jp/examplech/blomaga/ar1234567", segs.Single(s => s.IsUrl).Text);
        Assert.Equal(text, Reassemble(segs));
    }

    [Fact]
    public void TcoUrlIsLinkified()
    {
        var segs = Linkifier.Split("see https://t.co/u6dBbg4q07");
        Assert.Equal("https://t.co/u6dBbg4q07", segs.Single(s => s.IsUrl).Text);
    }

    [Fact]
    public void BareSchemeFragmentNotLinkified()
    {
        var segs = Linkifier.Split("切れた https://");
        Assert.DoesNotContain(segs, s => s.IsUrl);
        Assert.Equal("切れた https://", Reassemble(segs));
    }

    [Fact]
    public void MultipleUrlsPreserveAllText()
    {
        var text = "a https://x.example/1、b https://x.example/2!c";
        var segs = Linkifier.Split(text);
        Assert.Equal(2, segs.Count(s => s.IsUrl));
        Assert.Equal(text, Reassemble(segs));
    }

    [Fact]
    public void EmptyText_NoSegments()
    {
        Assert.Empty(Linkifier.Split(""));
    }

    private const string YtThumb = "https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg";

    [Theory]
    // YouTube の各種 URL 形式 (Sorsa は t.co を展開済みで本文に入れる)
    [InlineData("https://youtu.be/dQw4w9WgXcQ", YtThumb)]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", YtThumb)]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ", YtThumb)]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ", YtThumb)]
    [InlineData("http://www.youtube.com/watch?v=dQw4w9WgXcQ", YtThumb)]
    // v= の前後に他のクエリが付く形
    [InlineData("https://www.youtube.com/watch?feature=share&v=dQw4w9WgXcQ", YtThumb)]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=42s", YtThumb)]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ", YtThumb)]
    [InlineData("https://www.youtube.com/live/dQw4w9WgXcQ", YtThumb)]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ", YtThumb)]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?t=30", YtThumb)]
    // ニコニコ動画 (sm/nm/so と短縮 URL)。サムネイルは数字部のみを使う
    [InlineData("https://www.nicovideo.jp/watch/sm2455666",
        "https://nicovideo.cdn.nimg.jp/thumbnails/2455666/2455666")]
    [InlineData("https://nicovideo.jp/watch/nm10171192",
        "https://nicovideo.cdn.nimg.jp/thumbnails/10171192/10171192")]
    [InlineData("https://nico.ms/so12345",
        "https://nicovideo.cdn.nimg.jp/thumbnails/12345/12345")]
    [InlineData("https://www.nicovideo.jp/watch/sm9?ref=x",
        "https://nicovideo.cdn.nimg.jp/thumbnails/9/9")]
    public void ExtractVideoLinks_BuildsThumbnailUrl(string url, string expectedThumbnail)
    {
        var links = Linkifier.ExtractVideoLinks($"動画です {url} どうぞ");
        var link = Assert.Single(links);
        Assert.Equal(url, link.PageUrl);
        Assert.Equal(expectedThumbnail, link.ThumbnailUrl);
    }

    [Theory]
    [InlineData("https://www.youtube.com/channel/UCabcdefghijklmnop")]      // 動画ではない
    [InlineData("https://www.youtube.com/@somechannel")]
    [InlineData("https://www.youtube.com/watch?v=short")]                   // ID が 11 文字未満
    [InlineData("https://youtu.be/tooLongVideoId123")]                      // ID が 11 文字超
    [InlineData("https://www.nicovideo.jp/user/12345")]                     // 動画ではない
    [InlineData("https://live.nicovideo.jp/watch/lv12345")]                 // 生放送は対象外
    [InlineData("https://example.com/watch?v=dQw4w9WgXcQ")]                 // 別ホスト
    [InlineData("https://notyoutube.com/watch?v=dQw4w9WgXcQ")]
    public void ExtractVideoLinks_IgnoresNonVideoUrls(string url)
    {
        Assert.Empty(Linkifier.ExtractVideoLinks($"見て {url} ね"));
    }

    [Theory]
    // URL の直後に空白なしで日本語が続くケース (Split の約物除去は末尾が空白のときだけ効くため、
    // ページ URL は正規表現のマッチ範囲から作る必要がある)
    [InlineData("これ https://youtu.be/dQw4w9WgXcQ。おすすめ", "https://youtu.be/dQw4w9WgXcQ")]
    [InlineData("これ https://youtu.be/dQw4w9WgXcQ.です", "https://youtu.be/dQw4w9WgXcQ")]
    [InlineData("これ https://youtu.be/dQw4w9WgXcQ、と", "https://youtu.be/dQw4w9WgXcQ")]
    [InlineData("末尾 https://youtu.be/dQw4w9WgXcQ。", "https://youtu.be/dQw4w9WgXcQ")]
    // クエリ付きは保持する (再生位置を失わない)
    [InlineData("時間指定 https://youtu.be/dQw4w9WgXcQ?t=30 から",
        "https://youtu.be/dQw4w9WgXcQ?t=30")]
    public void ExtractVideoLinks_PageUrlExcludesSurroundingText(string text, string expectedPageUrl)
    {
        var link = Assert.Single(Linkifier.ExtractVideoLinks(text));
        Assert.Equal(expectedPageUrl, link.PageUrl);
        Assert.Equal(YtThumb, link.ThumbnailUrl);
    }

    [Fact]
    public void ExtractVideoLinks_DedupesSameVideoAndKeepsOrder()
    {
        var links = Linkifier.ExtractVideoLinks(
            "https://youtu.be/dQw4w9WgXcQ と https://www.youtube.com/watch?v=dQw4w9WgXcQ と " +
            "https://www.nicovideo.jp/watch/sm9");
        Assert.Equal(2, links.Count);
        Assert.Equal(YtThumb, links[0].ThumbnailUrl);
        Assert.Equal("https://nicovideo.cdn.nimg.jp/thumbnails/9/9", links[1].ThumbnailUrl);
    }

    [Fact]
    public void ExtractVideoLinks_NicoCarriesVideoNumberForApiResolution()
    {
        // ニコニコの CDN URL は番号だけでは決まらない (新しい動画はサフィックス付き) ので、
        // 取得時に getthumbinfo で解決できるよう番号を持ち回る
        var nico = Assert.Single(Linkifier.ExtractVideoLinks("https://www.nicovideo.jp/watch/sm36810714"));
        Assert.Equal("36810714", nico.NicoVideoNumber);

        var yt = Assert.Single(Linkifier.ExtractVideoLinks("https://youtu.be/dQw4w9WgXcQ"));
        Assert.Null(yt.NicoVideoNumber);
    }

    [Fact]
    public void ExtractVideoLinks_EmptyOrNullText()
    {
        Assert.Empty(Linkifier.ExtractVideoLinks(null));
        Assert.Empty(Linkifier.ExtractVideoLinks(""));
        Assert.Empty(Linkifier.ExtractVideoLinks("リンクのない本文"));
    }
}
