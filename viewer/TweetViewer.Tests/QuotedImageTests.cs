using System.IO;
using TweetViewer.Data;
using TweetViewer.Models;
using TweetViewer.Services;
using TweetViewer.ViewModels;

namespace TweetViewer.Tests;

/// <summary>引用先画像を引用ブロックへ出し分ける VM の振り分けテスト。</summary>
public sealed class QuotedImageTests : IAsyncDisposable
{
    private readonly string _dataDir;
    private readonly ViewerDatabase _db;
    private readonly ReadMarkQueue _readQueue;

    public QuotedImageTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "TweetViewerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
        _db = new ViewerDatabase(_dataDir);
        _db.EnsureCreated();
        _readQueue = new ReadMarkQueue(_db);
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

    /// <summary>ResolveImagePaths は実ファイルが無い行を捨てるので、ダミーを置いておく。</summary>
    private void PlaceImage(string tweetId, int index, string ext = "jpg")
    {
        File.WriteAllBytes(Path.Combine(_dataDir, $"{tweetId}_{index}.{ext}"), [0]);
    }

    private TweetItemViewModel CreateItem(IReadOnlyList<TweetMediaRow> media, TweetRow row)
    {
        var list = new TweetListViewModel(
            _db, new TweetRepository(_db), _readQueue, new IconCache(_dataDir));
        // imagesDir にテンポラリ dir をそのまま使う (PlaceImage の配置先と一致させる)
        return new TweetItemViewModel(list, row, media, _dataDir, "owner", null, new IconCache(_dataDir));
    }

    private static TweetRow QuoteRow(string tweetId, string? quotedText) => new()
    {
        TweetId = tweetId, IdInt = long.Parse(tweetId), Username = "alice",
        AuthorUsername = "alice", CreatedAtUtc = "2026-07-21T20:23:54+00:00",
        SortKey = 1, Type = TweetType.Quote, FullText = "outer",
        QuotedUsername = "bob", QuotedText = quotedText,
    };

    [Fact]
    public void OwnAndRetweetedGoToBody_QuotedGoesToQuoteBlock()
    {
        PlaceImage("100", 1);
        PlaceImage("100", 2);
        PlaceImage("100", 3);
        var media = new[]
        {
            new TweetMediaRow("100", 1, "https://pbs.twimg.com/media/A.jpg", "jpg", MediaOrigin.Own),
            new TweetMediaRow("100", 2, "https://pbs.twimg.com/media/B.jpg", "jpg", MediaOrigin.Quoted),
            new TweetMediaRow("100", 3, "https://pbs.twimg.com/media/C.jpg", "jpg", MediaOrigin.Retweeted),
        };
        var vm = CreateItem(media, QuoteRow("100", "引用本文"));

        Assert.Equal(2, vm.ImagePaths.Count);          // Own + Retweeted
        Assert.EndsWith("100_1.jpg", vm.ImagePaths[0]);
        Assert.EndsWith("100_3.jpg", vm.ImagePaths[1]);
        Assert.True(vm.HasImages);

        Assert.Single(vm.QuotedImagePaths);            // Quoted のみ
        Assert.EndsWith("100_2.jpg", vm.QuotedImagePaths[0]);
        Assert.True(vm.HasQuotedImages);
    }

    [Fact]
    public void QuoteBlockShownWhenOnlyImageAndNoText()
    {
        PlaceImage("101", 1);
        var media = new[]
        {
            new TweetMediaRow("101", 1, "https://pbs.twimg.com/media/D.jpg", "jpg", MediaOrigin.Quoted),
        };
        // 引用先が画像だけで本文が空でも引用ブロックを出す
        var vm = CreateItem(media, QuoteRow("101", null));
        Assert.True(vm.HasQuotedImages);
        Assert.True(vm.HasQuote);
        Assert.Empty(vm.ImagePaths);
        Assert.False(vm.HasImages);
    }

    [Fact]
    public void NoQuotedMediaLeavesQuoteBlockTextOnly()
    {
        PlaceImage("102", 1);
        var media = new[]
        {
            new TweetMediaRow("102", 1, "https://pbs.twimg.com/media/E.jpg", "jpg", MediaOrigin.Own),
        };
        var vm = CreateItem(media, QuoteRow("102", "テキストのみ"));
        Assert.Single(vm.ImagePaths);
        Assert.Empty(vm.QuotedImagePaths);
        Assert.False(vm.HasQuotedImages);
        Assert.True(vm.HasQuote);
    }

    [Fact]
    public void MissingLocalFileIsDropped()
    {
        // ファイルを置かない → 画像なし扱い (ダウンロード失敗に耐える既存挙動)
        var media = new[]
        {
            new TweetMediaRow("103", 1, "https://pbs.twimg.com/media/F.jpg", "jpg", MediaOrigin.Quoted),
        };
        var vm = CreateItem(media, QuoteRow("103", "本文あり"));
        Assert.Empty(vm.QuotedImagePaths);
        Assert.False(vm.HasQuotedImages);
        Assert.True(vm.HasQuote);   // テキストがあるのでブロック自体は出る
    }
}
