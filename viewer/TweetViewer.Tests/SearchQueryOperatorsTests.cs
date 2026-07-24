using TweetViewer.Data;

namespace TweetViewer.Tests;

/// <summary>min_retweets: / min_faves: の合成・分解の検証。</summary>
public class SearchQueryOperatorsTests
{
    [Fact]
    public void Compose_下限なしはそのまま()
    {
        Assert.Equal("sts2 OR スレスパ2", SearchQueryOperators.Compose("sts2 OR スレスパ2", null, null));
    }

    [Fact]
    public void Compose_下限ありは括弧で包んで演算子を付与()
    {
        Assert.Equal("(q) min_retweets:5", SearchQueryOperators.Compose("q", 5, null));
        Assert.Equal("(q) min_faves:10", SearchQueryOperators.Compose("q", null, 10));
        Assert.Equal("(q) min_retweets:5 min_faves:10", SearchQueryOperators.Compose("q", 5, 10));
    }

    [Theory]
    [InlineData("(sts2 OR \"slay the spire 2\") min_faves:10", "sts2 OR \"slay the spire 2\"", null, 10L)]
    [InlineData("(a OR b) min_retweets:3 min_faves:7", "a OR b", 3L, 7L)]
    [InlineData("plain query", "plain query", null, null)]
    public void Split_基本形(string full, string expectedBase, long? expectedRt, long? expectedFav)
    {
        var (baseQuery, minRt, minFav) = SearchQueryOperators.Split(full);
        Assert.Equal(expectedBase, baseQuery);
        Assert.Equal(expectedRt, minRt);
        Assert.Equal(expectedFav, minFav);
    }

    [Fact]
    public void SplitとComposeで往復できる()
    {
        var original = SearchQueryOperators.Compose("""sts2 OR "slay the spire 2" OR スレスパ2""", null, 10);
        var (baseQuery, minRt, minFav) = SearchQueryOperators.Split(original);
        Assert.Equal(original, SearchQueryOperators.Compose(baseQuery, minRt, minFav));
    }

    [Fact]
    public void Split_並列括弧は外さない()
    {
        var (baseQuery, _, minFav) = SearchQueryOperators.Split("((a OR b) (c OR d)) min_faves:1");
        Assert.Equal("(a OR b) (c OR d)", baseQuery);
        Assert.Equal(1, minFav);
    }

    [Fact]
    public void Split_演算子なしの括弧クエリは括弧を維持()
    {
        // 意図して書かれた括弧を外すと保存時に文字列が変わり不要な状態リセットになる
        var (baseQuery, minRt, minFav) = SearchQueryOperators.Split("(a OR b)");
        Assert.Equal("(a OR b)", baseQuery);
        Assert.Null(minRt);
        Assert.Null(minFav);
    }

    [Fact]
    public void Split_手書きの中間演算子も拾う()
    {
        var (baseQuery, _, minFav) = SearchQueryOperators.Split("foo min_faves:10 bar");
        Assert.Equal("foo bar", baseQuery);
        Assert.Equal(10, minFav);
    }

    [Fact]
    public void Split_重複トークンは最後の値を採用()
    {
        var (_, _, minFav) = SearchQueryOperators.Split("q min_faves:5 min_faves:20");
        Assert.Equal(20, minFav);
    }
}
