using System.IO;
using System.Text;
using TweetViewer.Data;
using TweetViewer.Models;

namespace TweetViewer.Tests;

/// <summary>
/// 期間フィルタ (sort_key 範囲) のリポジトリ層テスト。
/// 範囲は epoch 直指定で TZ 非依存 (ローカル暦→epoch の変換は DateRangeFilterTests に隔離)。
/// </summary>
public sealed class DateRangeQueryTests : IDisposable
{
    private readonly string _dataDir;
    private readonly ViewerDatabase _db;

    public DateRangeQueryTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "SubakoTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
        _db = new ViewerDatabase(_dataDir);
        _db.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dataDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string TweetLine(long id, string date) =>
        $$"""{"id":"{{id}}","created_at":"{{date}}","full_text":"tweet {{id}}","user":{"username":"author{{id}}","display_name":"Author {{id}}"},"entities":[]}""";

    private async Task ImportAsync(string username, params string[] lines)
    {
        await new UserRepository(_db).AddAsync(username);
        Directory.CreateDirectory(_db.UserDir(username));
        File.AppendAllText(_db.JsonlPath(username),
            string.Join("", lines.Select(l => l + "\n")), new UTF8Encoding(false));
        await new JsonlImporter(_db).ImportUserAsync(username);
    }

    // MergedTimelineTests の "Tue Jul ..." 形式は曜日不一致でパースされず sort_key = 0 に
    // 落ちる (あちらは id 順で並びが成立している)。ここは sort_key が本題なので ISO 形式を使う
    private static string Date(int day) => $"2026-07-{day:00}T20:00:00+00:00";

    /// <summary>Date(day) と同じ時刻の epoch 秒。</summary>
    private static long Epoch(int day) =>
        new DateTimeOffset(2026, 7, day, 20, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    [Fact]
    public async Task RangeFiltersPageWithHalfOpenBounds()
    {
        await ImportAsync("alice",
            TweetLine(1, Date(1)), TweetLine(2, Date(2)), TweetLine(3, Date(3)));

        // [Day2, Day3): From ちょうどは含む、ToExclusive ちょうどは含まない
        var page = await new TweetRepository(_db).GetPageAsync(
            ["alice"], false, null, 10, new DateRangeFilter(Epoch(2), Epoch(3)));

        var row = Assert.Single(page.Rows);
        Assert.Equal("2", row.TweetId);
    }

    [Fact]
    public async Task RangeCombinesWithUnreadOnly()
    {
        await ImportAsync("alice",
            TweetLine(1, Date(1)), TweetLine(2, Date(2)), TweetLine(3, Date(3)));
        var repo = new TweetRepository(_db);
        await repo.SetReadAsync("2", "alice", read: true);

        var page = await repo.GetPageAsync(
            ["alice"], unreadOnly: true, null, 10, new DateRangeFilter(Epoch(2), Epoch(4)));

        // 期間内は 2 と 3、2 は既読なので 3 だけ
        var row = Assert.Single(page.Rows);
        Assert.Equal("3", row.TweetId);
    }

    [Fact]
    public async Task RangeAppliesToMergedTimelineWithDedup()
    {
        const string bucket = "searches/kw-x";
        await ImportAsync("alice", TweetLine(1, Date(1)), TweetLine(2, Date(2)));
        await ImportAsync(bucket, TweetLine(2, Date(2)), TweetLine(3, Date(3)));

        var page = await new TweetRepository(_db).GetPageAsync(
            ["alice", bucket], false, null, 10, new DateRangeFilter(Epoch(2), Epoch(4)));

        // 範囲外の 1 は落ち、重複の 2 は代表 (実ユーザーアーカイブ) の 1 件のみ
        Assert.Equal(new[] { "3", "2" }, page.Rows.Select(r => r.TweetId));
        Assert.Equal("alice", page.Rows.Single(r => r.TweetId == "2").Username);
    }

    [Fact]
    public async Task MediaPageRespectsRange()
    {
        var m1 = $$"""{"id":"1","created_at":"{{Date(1)}}","full_text":"one","user":{"username":"alice"},"entities":[{"type":"photo","link":"https://pbs.twimg.com/media/A1.jpg"}]}""";
        var m2 = $$"""{"id":"2","created_at":"{{Date(2)}}","full_text":"two","user":{"username":"alice"},"entities":[{"type":"photo","link":"https://pbs.twimg.com/media/B1.jpg"}]}""";
        await ImportAsync("alice", m1, m2);

        var page = await new TweetRepository(_db).GetMediaPageAsync(
            ["alice"], null, 10, new DateRangeFilter(Epoch(2), Epoch(3)));

        var row = Assert.Single(page);
        Assert.Equal("2", row.TweetId);
    }

    [Fact]
    public async Task DateBoundsReturnMinAndMax()
    {
        await ImportAsync("alice", TweetLine(1, Date(1)), TweetLine(3, Date(3)));
        await ImportAsync("bob", TweetLine(5, Date(5)));
        var repo = new TweetRepository(_db);

        Assert.Equal((Epoch(1), Epoch(3)), await repo.GetDateBoundsAsync(["alice"]));
        Assert.Equal((Epoch(1), Epoch(5)), await repo.GetDateBoundsAsync(["alice", "bob"]));
        Assert.Null(await repo.GetDateBoundsAsync([]));
        Assert.Null(await repo.GetDateBoundsAsync(["nobody"]));
    }
}
