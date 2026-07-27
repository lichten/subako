using System.IO;
using TweetViewer.Data;
using TweetViewer.Models;
using TweetViewer.Services;
using TweetViewer.ViewModels;

namespace TweetViewer.Tests;

/// <summary>
/// 動画リンクのサムネイルの振り分けテスト。
/// キャッシュファイルを先に置いて IconCache の File.Exists 経路に乗せることで
/// ネットワークを一切使わずに検証する。
/// </summary>
public sealed class LinkThumbnailTests : IAsyncDisposable
{
    private const string YtUrl = "https://youtu.be/dQw4w9WgXcQ";
    private const string YtThumb = "https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg";
    private const string NicoUrl = "https://www.nicovideo.jp/watch/sm9";
    private const string NicoThumb = "https://nicovideo.cdn.nimg.jp/thumbnails/9/9";

    private readonly string _dataDir;
    private readonly ViewerDatabase _db;
    private readonly ReadMarkQueue _readQueue;
    private readonly IconCache _thumbnailCache;

    public LinkThumbnailTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "TweetViewerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
        _db = new ViewerDatabase(_dataDir);
        _db.EnsureCreated();
        _readQueue = new ReadMarkQueue(_db);
        _thumbnailCache = new IconCache(_dataDir, "thumbnails");
    }

    public async ValueTask DisposeAsync()
    {
        await _readQueue.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dataDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>キャッシュ済みに見せかける (これによりダウンロードは走らない)。</summary>
    private void PlaceCached(string thumbnailUrl) =>
        File.WriteAllBytes(_thumbnailCache.CachePathFor(thumbnailUrl), [0]);

    private TweetItemViewModel CreateItem(TweetRow row)
    {
        var list = new TweetListViewModel(
            _db, new TweetRepository(_db), _readQueue, new IconCache(_dataDir));
        return new TweetItemViewModel(
            list, row, [], _dataDir, "owner", null, new IconCache(_dataDir), _thumbnailCache);
    }

    private static TweetRow Row(
        string tweetId, string fullText, string? quotedText = null, TweetType type = TweetType.Tweet,
        string? rtText = null) => new()
        {
            TweetId = tweetId,
            IdInt = long.Parse(tweetId),
            Username = "alice",
            AuthorUsername = "alice",
            CreatedAtUtc = "2026-07-27T00:00:00+00:00",
            SortKey = 1,
            Type = type,
            FullText = fullText,
            RtText = rtText,
            QuotedUsername = quotedText is null ? null : "bob",
            QuotedText = quotedText,
        };

    [Fact]
    public async Task BodyVideoLinkGetsThumbnail()
    {
        PlaceCached(YtThumb);
        var vm = CreateItem(Row("1", $"この動画 {YtUrl} を見て"));

        var item = Assert.Single(vm.LinkThumbnails);
        Assert.Equal(YtUrl, item.PageUrl);
        Assert.True(vm.HasLinkThumbnails);
        Assert.Empty(vm.QuotedLinkThumbnails);
        Assert.False(vm.HasQuotedLinkThumbnails);
        // 解決は fire-and-forget なので待つ
        await WaitForAsync(() => item.HasImage);
        Assert.EndsWith(".jpg", item.ThumbnailPath);
    }

    [Fact]
    public async Task QuotedVideoLinkGoesToQuoteBlock()
    {
        PlaceCached(NicoThumb);
        var vm = CreateItem(Row("2", "引用します", quotedText: $"元ツイート {NicoUrl}", type: TweetType.Quote));

        Assert.Empty(vm.LinkThumbnails);
        var item = Assert.Single(vm.QuotedLinkThumbnails);
        Assert.Equal(NicoUrl, item.PageUrl);
        Assert.True(vm.HasQuotedLinkThumbnails);
        await WaitForAsync(() => item.HasImage);
    }

    [Fact]
    public void SameVideoInBodyAndQuote_ShownOnlyInBody()
    {
        PlaceCached(YtThumb);
        var vm = CreateItem(Row("3", $"同じ動画 {YtUrl}",
            quotedText: $"こっちも {YtUrl}", type: TweetType.Quote));

        Assert.Single(vm.LinkThumbnails);
        Assert.Empty(vm.QuotedLinkThumbnails);
    }

    [Fact]
    public void RetweetUsesRetweetedTextForThumbnails()
    {
        PlaceCached(YtThumb);
        // RT の本文表示は RtText なのでそちらから拾う
        var vm = CreateItem(Row("4", "RT @bob: ...", type: TweetType.Retweet, rtText: $"元の本文 {YtUrl}"));
        Assert.Single(vm.LinkThumbnails);
    }

    [Fact]
    public void NoVideoLink_NoThumbnails()
    {
        var vm = CreateItem(Row("5", "https://example.com/article だけ"));
        Assert.Empty(vm.LinkThumbnails);
        Assert.False(vm.HasLinkThumbnails);
    }

    [Fact]
    public void ThumbnailIsHiddenUntilPathResolved()
    {
        // 取得できるまで / 失敗したままなら枠を出さない (灰色の空箱を残さないため)。
        // ここで TweetItemViewModel を使うと実 HTTP が走るので VM 単体で検証する
        var item = new LinkThumbnailViewModel(YtUrl);
        Assert.False(item.HasImage);

        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        item.ThumbnailPath = Path.Combine(_dataDir, "dummy.jpg");

        Assert.True(item.HasImage);
        Assert.Contains(nameof(LinkThumbnailViewModel.HasImage), changed);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 50 && !condition(); i++)
            await Task.Delay(20);
        Assert.True(condition(), "条件が満たされませんでした");
    }
}
