using System.IO;
using System.Text;
using TweetViewer.Data;

namespace TweetViewer.Tests;

/// <summary>統合タイムライン用の複数アーカイブページング (リポジトリ層) のテスト。</summary>
public sealed class MergedTimelineTests : IDisposable
{
    private readonly string _dataDir;
    private readonly ViewerDatabase _db;

    public MergedTimelineTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "TweetViewerTests", Guid.NewGuid().ToString("N"));
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

    private static string TweetLine(long id, string date = "Tue Jul 21 20:23:54 +0000 2026") =>
        $$"""{"id":"{{id}}","created_at":"{{date}}","full_text":"tweet {{id}}","user":{"username":"author{{id}}","display_name":"Author {{id}}"},"entities":[]}""";

    private async Task ImportAsync(string username, params string[] lines)
    {
        await new UserRepository(_db).AddAsync(username);
        Directory.CreateDirectory(_db.UserDir(username));
        File.AppendAllText(_db.JsonlPath(username),
            string.Join("", lines.Select(l => l + "\n")), new UTF8Encoding(false));
        await new JsonlImporter(_db).ImportUserAsync(username);
    }

    // 日時を変えて sort_key に差をつける (新しい順に id が大きい構成にはしない — 並びの根拠が日時であることを見るため)
    private static string Date(int day) => $"Tue Jul {day:00} 20:00:00 +0000 2026";

    [Fact]
    public async Task DuplicateTweetAppearsOnceWithUserArchiveAsRepresentative()
    {
        const string bucket = "searches/kw-x";
        var shared = TweetLine(1);
        await ImportAsync("alice", shared);
        await ImportAsync(bucket, shared);

        var page = await new TweetRepository(_db).GetPageAsync(["alice", bucket], false, null, 10);

        var row = Assert.Single(page.Rows);
        Assert.Equal("1", row.TweetId);
        Assert.Equal("alice", row.Username);   // 実ユーザーアーカイブが代表
    }

    [Fact]
    public async Task BucketOnlyTweetKeepsBucketUsername()
    {
        const string bucket = "searches/kw-x";
        await ImportAsync("alice", TweetLine(1, Date(10)));
        await ImportAsync(bucket, TweetLine(2, Date(11)));

        var page = await new TweetRepository(_db).GetPageAsync(["alice", bucket], false, null, 10);

        Assert.Equal(2, page.Rows.Count);
        Assert.Equal(bucket, page.Rows.Single(r => r.TweetId == "2").Username);
        // author 列で表示が成立する (統合時のヘッダ用)
        Assert.Equal("author2", page.Rows.Single(r => r.TweetId == "2").AuthorUsername);
    }

    [Fact]
    public async Task ArchivesInterleaveInDescendingTimeOrder()
    {
        await ImportAsync("alice", TweetLine(1, Date(1)), TweetLine(3, Date(3)));
        await ImportAsync("bob", TweetLine(2, Date(2)), TweetLine(4, Date(4)));

        var page = await new TweetRepository(_db).GetPageAsync(["alice", "bob"], false, null, 10);

        Assert.Equal(new[] { "4", "3", "2", "1" }, page.Rows.Select(r => r.TweetId));
    }

    [Fact]
    public async Task KeysetPagesConcatenateWithoutGapsOrDuplicates()
    {
        const string bucket = "searches/kw-x";
        // 重複ツイートがページ境界を跨ぐ構成
        await ImportAsync("alice",
            TweetLine(1, Date(1)), TweetLine(3, Date(3)), TweetLine(5, Date(5)));
        await ImportAsync(bucket,
            TweetLine(2, Date(2)), TweetLine(3, Date(3)), TweetLine(4, Date(4)), TweetLine(5, Date(5)));

        var repo = new TweetRepository(_db);
        var collected = new List<string>();
        (long, long)? cursor = null;
        for (var i = 0; i < 10; i++)
        {
            var page = await repo.GetPageAsync(["alice", bucket], false, cursor, 2);
            collected.AddRange(page.Rows.Select(r => r.TweetId));
            if (page.Rows.Count < 2)
                break;
            var last = page.Rows[^1];
            cursor = (last.SortKey, last.IdInt);
        }

        Assert.Equal(new[] { "5", "4", "3", "2", "1" }, collected);
    }

    [Fact]
    public async Task LimitAppliesAfterDeduplication()
    {
        const string bucket = "searches/kw-x";
        var lines = Enumerable.Range(1, 4).Select(i => TweetLine(i, Date(i))).ToArray();
        await ImportAsync("alice", lines);
        await ImportAsync(bucket, lines);   // 物理 8 行

        var page = await new TweetRepository(_db).GetPageAsync(["alice", bucket], false, null, 4);

        // dedup が LIMIT より前なら 4 件全部返る (後だと 2 件になる)
        Assert.Equal(4, page.Rows.Count);
        Assert.Equal(4, page.Rows.Select(r => r.TweetId).Distinct().Count());
    }

    [Fact]
    public async Task UnreadOnlyDropsBothCopiesOfReadTweet()
    {
        const string bucket = "searches/kw-x";
        await ImportAsync("alice", TweetLine(1, Date(1)), TweetLine(2, Date(2)));
        await ImportAsync(bucket, TweetLine(1, Date(1)));

        var repo = new TweetRepository(_db);
        await repo.SetReadAsync("1", "alice", read: true);

        var page = await repo.GetPageAsync(["alice", bucket], unreadOnly: true, null, 10);
        var row = Assert.Single(page.Rows);
        Assert.Equal("2", row.TweetId);
    }

    [Fact]
    public async Task ArchivesByTweetIdListsOnlyDuplicatesIncludingArchivesOutsideScope()
    {
        const string bucket = "searches/kw-x";
        await ImportAsync("alice", TweetLine(1, Date(1)), TweetLine(2, Date(2)));
        await ImportAsync(bucket, TweetLine(1, Date(1)));
        // スコープ外のアーカイブにも同じツイートがある (既読化で未読数が実際に減るのはここも)
        await ImportAsync("carol", TweetLine(1, Date(1)));

        var page = await new TweetRepository(_db).GetPageAsync(["alice", bucket], false, null, 10);

        var archives = Assert.Single(page.ArchivesByTweetId);   // 重複している "1" だけ載る
        Assert.Equal("1", archives.Key);
        Assert.Equal(new[] { "alice", "carol", bucket }, archives.Value.OrderBy(x => x));
    }

    [Fact]
    public async Task MediaPageDeduplicatesPerImageAndCarriesRowUsername()
    {
        const string bucket = "searches/kw-x";
        // 2 枚画像のツイートを両アーカイブに、1 枚画像をバケットのみに
        var two = $$"""{"id":"2","created_at":"{{Date(2)}}","full_text":"two","user":{"username":"alice"},"entities":[{"type":"photo","link":"https://pbs.twimg.com/media/A1.jpg"},{"type":"photo","link":"https://pbs.twimg.com/media/A2.jpg"}]}""";
        var one = $$"""{"id":"1","created_at":"{{Date(1)}}","full_text":"one","user":{"username":"someone"},"entities":[{"type":"photo","link":"https://pbs.twimg.com/media/B1.jpg"}]}""";
        await ImportAsync("alice", two);
        await ImportAsync(bucket, two, one);

        var repo = new TweetRepository(_db);
        var page = await repo.GetMediaPageAsync(["alice", bucket], null, 10);

        // (tweet_id, idx) 単位で dedup、代表は実ユーザーアーカイブ
        Assert.Equal(new[] { ("2", 1, "alice"), ("2", 2, "alice"), ("1", 1, bucket) },
            page.Select(m => (m.TweetId, m.Idx, m.Username)));

        // 3 要素カーソルの継続
        var p1 = await repo.GetMediaPageAsync(["alice", bucket], null, 2);
        var last = p1[^1];
        var p2 = await repo.GetMediaPageAsync(["alice", bucket], (last.SortKey, last.IdInt, last.Idx), 2);
        Assert.Equal(new[] { ("2", 1), ("2", 2) }, p1.Select(m => (m.TweetId, m.Idx)));
        Assert.Equal(new[] { ("1", 1) }, p2.Select(m => (m.TweetId, m.Idx)));
    }

    [Fact]
    public async Task EmptyUsernamesReturnsEmptyPage()
    {
        await ImportAsync("alice", TweetLine(1));
        var repo = new TweetRepository(_db);
        var page = await repo.GetPageAsync([], false, null, 10);
        Assert.Empty(page.Rows);
        Assert.Empty(await repo.GetMediaPageAsync([], null, 10));
    }
}
