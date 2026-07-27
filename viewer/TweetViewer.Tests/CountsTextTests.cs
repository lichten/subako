using System.IO;
using TweetViewer.Data;
using TweetViewer.Models;
using TweetViewer.Services;
using TweetViewer.ViewModels;

namespace TweetViewer.Tests;

/// <summary>カウント行の表示テスト (取得できない値は項目ごと出さない)。</summary>
public sealed class CountsTextTests : IAsyncDisposable
{
    private readonly string _dataDir;
    private readonly ViewerDatabase _db;
    private readonly ReadMarkQueue _readQueue;

    public CountsTextTests()
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

    private string CountsTextFor(TweetType type, long reply, long rt, long like, long view)
    {
        var row = new TweetRow
        {
            TweetId = "1",
            IdInt = 1,
            Username = "alice",
            AuthorUsername = "alice",
            CreatedAtUtc = "2026-07-27T00:00:00+00:00",
            SortKey = 1,
            Type = type,
            FullText = "x",
            RtText = type == TweetType.Retweet ? "元の全文" : null,
            RtUsername = type == TweetType.Retweet ? "bob" : null,
            ReplyCount = reply,
            RetweetCount = rt,
            LikeCount = like,
            ViewCount = view,
        };
        var list = new TweetListViewModel(
            _db, new TweetRepository(_db), _readQueue, new IconCache(_dataDir));
        var vm = new TweetItemViewModel(list, row, [], _dataDir, "owner", null,
            new IconCache(_dataDir), new IconCache(_dataDir, "thumbnails"));
        return vm.CountsText;
    }

    [Fact]
    public void Retweet_OmitsReplyCount()
    {
        // RT の返信数は API が外側にも RT元にも返さないので 0 と偽らず項目ごと消す
        var text = CountsTextFor(TweetType.Retweet, reply: 0, rt: 10, like: 25, view: 1200);
        Assert.DoesNotContain("返信", text);
        Assert.Equal("RT 10  いいね 25  表示 1,200", text);
    }

    [Fact]
    public void PlainTweet_ShowsReplyCount()
    {
        var text = CountsTextFor(TweetType.Tweet, reply: 3, rt: 10, like: 25, view: 1200);
        Assert.Equal("返信 3  RT 10  いいね 25  表示 1,200", text);
    }

    [Fact]
    public void PlainTweet_ShowsZeroReplyCount()
    {
        // 通常ツイートの 0 は実際に 0 なので表示してよい
        Assert.StartsWith("返信 0", CountsTextFor(TweetType.Tweet, 0, 0, 0, 0));
    }

    [Fact]
    public void ViewCountOmittedWhenZero()
    {
        Assert.Equal("返信 1  RT 2  いいね 3", CountsTextFor(TweetType.Tweet, 1, 2, 3, view: 0));
        Assert.Equal("RT 2  いいね 3", CountsTextFor(TweetType.Retweet, 0, 2, 3, view: 0));
    }

    [Fact]
    public void QuoteTweet_ShowsReplyCount()
    {
        // 引用は自分自身の投稿なので 4 項目とも外側の値が正しい
        var text = CountsTextFor(TweetType.Quote, reply: 5, rt: 6, like: 7, view: 8);
        Assert.Equal("返信 5  RT 6  いいね 7  表示 8", text);
    }
}
