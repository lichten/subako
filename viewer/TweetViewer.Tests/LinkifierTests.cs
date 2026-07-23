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
        var text = "小飼「…」 https://ch.nicovideo.jp/dankogai/blomaga/ar2212530 @dankogai #小飼弾の論弾";
        var segs = Linkifier.Split(text);
        Assert.Equal("https://ch.nicovideo.jp/dankogai/blomaga/ar2212530", segs.Single(s => s.IsUrl).Text);
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
}
