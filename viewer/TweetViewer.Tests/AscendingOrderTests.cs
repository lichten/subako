using System.IO;
using System.Text;
using TweetViewer.Data;
using TweetViewer.Models;

namespace TweetViewer.Tests;

/// <summary>
/// 「古い順」(ascending) 表示のリポジトリ層テスト。
/// 日付は ISO 形式 (MergedTimelineTests の "Tue Jul..." 形式は曜日不一致でパースされず
/// sort_key = 0 に落ちるため、並び順のテストには使えない)。
/// </summary>
public sealed class AscendingOrderTests : IDisposable
{
    private readonly string _dataDir;
    private readonly ViewerDatabase _db;

    public AscendingOrderTests()
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

    private static string Date(int day) => $"2026-07-{day:00}T20:00:00+00:00";

    private static long Epoch(int day) =>
        new DateTimeOffset(2026, 7, day, 20, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    [Fact]
    public async Task AscendingReturnsOldestFirst()
    {
        await ImportAsync("alice",
            TweetLine(2, Date(2)), TweetLine(1, Date(1)), TweetLine(3, Date(3)));

        var page = await new TweetRepository(_db).GetPageAsync(
            ["alice"], false, null, 10, ascending: true);

        Assert.Equal(new[] { "1", "2", "3" }, page.Rows.Select(r => r.TweetId));
    }

    [Fact]
    public async Task AscendingKeysetPagesConcatenateWithoutGapsOrDuplicates()
    {
        const string bucket = "searches/kw-x";
        // 重複ツイートがページ境界を跨ぐ構成 (MergedTimelineTests の ASC 版)
        await ImportAsync("alice",
            TweetLine(1, Date(1)), TweetLine(3, Date(3)), TweetLine(5, Date(5)));
        await ImportAsync(bucket,
            TweetLine(2, Date(2)), TweetLine(3, Date(3)), TweetLine(4, Date(4)), TweetLine(5, Date(5)));

        var repo = new TweetRepository(_db);
        var collected = new List<string>();
        (long, long)? cursor = null;
        for (var i = 0; i < 10; i++)
        {
            var page = await repo.GetPageAsync(["alice", bucket], false, cursor, 2, ascending: true);
            collected.AddRange(page.Rows.Select(r => r.TweetId));
            if (page.Rows.Count < 2)
                break;
            var last = page.Rows[^1];
            cursor = (last.SortKey, last.IdInt);
        }

        Assert.Equal(new[] { "1", "2", "3", "4", "5" }, collected);
    }

    [Fact]
    public async Task AscendingKeepsDedupRepresentative()
    {
        const string bucket = "searches/kw-x";
        var shared = TweetLine(2, Date(2));
        await ImportAsync("alice", shared);
        await ImportAsync(bucket, shared, TweetLine(1, Date(1)));

        var page = await new TweetRepository(_db).GetPageAsync(
            ["alice", bucket], false, null, 10, ascending: true);

        Assert.Equal(new[] { "1", "2" }, page.Rows.Select(r => r.TweetId));
        Assert.Equal("alice", page.Rows.Single(r => r.TweetId == "2").Username);   // 実アーカイブが代表
    }

    [Fact]
    public async Task AscendingMediaPageKeepsIdxOrderAndCursorContinues()
    {
        var two = $$"""{"id":"2","created_at":"{{Date(2)}}","full_text":"two","user":{"username":"alice"},"entities":[{"type":"photo","link":"https://pbs.twimg.com/media/A1.jpg"},{"type":"photo","link":"https://pbs.twimg.com/media/A2.jpg"}]}""";
        var one = $$"""{"id":"1","created_at":"{{Date(1)}}","full_text":"one","user":{"username":"alice"},"entities":[{"type":"photo","link":"https://pbs.twimg.com/media/B1.jpg"}]}""";
        await ImportAsync("alice", two, one);
        var repo = new TweetRepository(_db);

        var page = await repo.GetMediaPageAsync(["alice"], null, 10, ascending: true);
        // ツイートは古い順、同一ツイート内の idx は昇順のまま
        Assert.Equal(new[] { ("1", 1), ("2", 1), ("2", 2) },
            page.Select(m => (m.TweetId, m.Idx)));

        // 3 要素カーソルの継続
        var p1 = await repo.GetMediaPageAsync(["alice"], null, 2, ascending: true);
        var last = p1[^1];
        var p2 = await repo.GetMediaPageAsync(
            ["alice"], (last.SortKey, last.IdInt, last.Idx), 2, ascending: true);
        Assert.Equal(new[] { ("1", 1), ("2", 1) }, p1.Select(m => (m.TweetId, m.Idx)));
        Assert.Equal(new[] { ("2", 2) }, p2.Select(m => (m.TweetId, m.Idx)));
    }

    [Fact]
    public async Task AscendingCombinesWithRangeAndUnreadOnly()
    {
        await ImportAsync("alice",
            TweetLine(1, Date(1)), TweetLine(2, Date(2)),
            TweetLine(3, Date(3)), TweetLine(4, Date(4)));
        var repo = new TweetRepository(_db);
        await repo.SetReadAsync("3", "alice", read: true);

        // 期間 [Day2, Day5) かつ未読のみ → 2, 4 が古い順
        var page = await repo.GetPageAsync(
            ["alice"], unreadOnly: true, null, 10,
            new DateRangeFilter(Epoch(2), Epoch(5)), ascending: true);

        Assert.Equal(new[] { "2", "4" }, page.Rows.Select(r => r.TweetId));
    }
}
