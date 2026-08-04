using TweetViewer.Services;

namespace TweetViewer.Tests;

/// <summary>
/// 「ブラウザで開く」の URL 規則 (docs/viewer-features.md §5.2・§6.2)。
/// タイムラインと画像ビューアが同じ規則を使うことの裏付け。
/// </summary>
public class TweetUrlTests
{
    [Fact]
    public void UsesAuthorWhenKnown() =>
        Assert.Equal("https://x.com/bob/status/123",
            TweetUrl.Status("123", "alice", "bob"));

    [Fact]
    public void FallsBackToArchiveNameWhenAuthorUnknown() =>
        Assert.Equal("https://x.com/alice/status/123",
            TweetUrl.Status("123", "alice", null));

    /// <summary>
    /// 検索バケット由来で投稿者が不明なら id 直リンク。バケット ID をそのまま
    /// 埋めると https://x.com/searches/&lt;slug&gt;/status/... という壊れた URL になる
    /// (画像ビューアにあった既知の不具合。docs/mac-port-notes.md §5)。
    /// </summary>
    [Fact]
    public void SearchBucketWithoutAuthorUsesIdLink() =>
        Assert.Equal("https://x.com/i/web/status/123",
            TweetUrl.Status("123", "searches/kw-12345678", null));

    [Fact]
    public void SearchBucketWithAuthorUsesAuthor() =>
        Assert.Equal("https://x.com/bob/status/123",
            TweetUrl.Status("123", "searches/kw-12345678", "bob"));

    [Theory]
    [InlineData("searches/kw-12345678", true)]
    [InlineData("Searches/kw-12345678", true)]
    [InlineData("alice", false)]
    [InlineData("searcher", false)]
    public void DetectsSearchBucketId(string username, bool expected) =>
        Assert.Equal(expected, TweetUrl.IsSearchBucket(username));
}
