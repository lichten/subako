using System.IO;
using System.Text;
using TweetViewer.Data;

namespace TweetViewer.Tests;

public sealed class JsonlImporterTests : IDisposable
{
    private readonly string _dataDir;
    private readonly ViewerDatabase _db;

    public JsonlImporterTests()
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

    private static string TweetLine(long id, string date = "Wed Apr 11 08:26:14 +0000 2007") =>
        $$"""{"id":"{{id}}","created_at":"{{date}}","full_text":"tweet {{id}} 日本語","entities":[]}""";

    private void WriteJsonl(string username, params string[] lines)
    {
        Directory.CreateDirectory(_db.UserDir(username));
        File.AppendAllText(_db.JsonlPath(username),
            string.Join("", lines.Select(l => l + "\n")), new UTF8Encoding(false));
    }

    private long CountTweets(string username)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM tweets WHERE username = $u";
        cmd.Parameters.AddWithValue("$u", username);
        return (long)cmd.ExecuteScalar()!;
    }

    [Fact]
    public async Task ImportsAndResumesDifferentially()
    {
        var users = new UserRepository(_db);
        await users.AddAsync("alice");
        WriteJsonl("alice", TweetLine(1), TweetLine(2), TweetLine(3));

        var importer = new JsonlImporter(_db);
        var first = await importer.ImportUserAsync("alice");
        Assert.Equal(3, first.NewTweets);
        Assert.Equal(0, first.SkippedLines);
        Assert.Equal(3, CountTweets("alice"));

        // 再実行は 0 件
        var second = await importer.ImportUserAsync("alice");
        Assert.Equal(0, second.NewTweets);

        // 追記分だけ差分取込
        WriteJsonl("alice", TweetLine(4), TweetLine(5));
        var third = await importer.ImportUserAsync("alice");
        Assert.Equal(2, third.NewTweets);
        Assert.Equal(5, CountTweets("alice"));
    }

    [Fact]
    public async Task SkipsBrokenAndIncompleteTrailingLines()
    {
        var users = new UserRepository(_db);
        await users.AddAsync("bob");
        WriteJsonl("bob", TweetLine(1), "{broken");
        // \n なしの不完全行(取込対象外、オフセットも進まない)
        File.AppendAllText(_db.JsonlPath("bob"), """{"id":"99","full_text":"partial""");

        var importer = new JsonlImporter(_db);
        var result = await importer.ImportUserAsync("bob");
        Assert.Equal(1, result.NewTweets);
        Assert.Equal(1, result.SkippedLines);

        // 不完全行が完結したら次の取込で入る
        File.AppendAllText(_db.JsonlPath("bob"), "\"}\n");
        var next = await importer.ImportUserAsync("bob");
        Assert.Equal(1, next.NewTweets);
        Assert.Equal(2, CountTweets("bob"));
    }

    [Fact]
    public async Task RebuildPreservesReadState()
    {
        var users = new UserRepository(_db);
        await users.AddAsync("carol");
        WriteJsonl("carol", TweetLine(10), TweetLine(11));

        var importer = new JsonlImporter(_db);
        await importer.ImportUserAsync("carol");

        var tweets = new TweetRepository(_db);
        await tweets.SetReadAsync("10", "carol", read: true);

        var page = await tweets.GetPageAsync(["carol"], unreadOnly: false, after: null, limit: 10);
        Assert.Equal(2, page.Rows.Count);
        Assert.Contains(page.Rows, r => r.TweetId == "10" && r.IsRead);

        var rebuilt = await importer.RebuildUserAsync("carol");
        Assert.Equal(2, rebuilt.NewTweets);

        var after = await tweets.GetPageAsync(["carol"], unreadOnly: false, after: null, limit: 10);
        Assert.Contains(after.Rows, r => r.TweetId == "10" && r.IsRead);
        Assert.Contains(after.Rows, r => r.TweetId == "11" && !r.IsRead);

        var unreadOnly = await tweets.GetPageAsync(["carol"], unreadOnly: true, after: null, limit: 10);
        Assert.Single(unreadOnly.Rows);
        Assert.Equal("11", unreadOnly.Rows[0].TweetId);
    }

    [Fact]
    public async Task TruncatedJsonlTriggersRebuild()
    {
        var users = new UserRepository(_db);
        await users.AddAsync("dave");
        WriteJsonl("dave", TweetLine(1), TweetLine(2), TweetLine(3));
        var importer = new JsonlImporter(_db);
        await importer.ImportUserAsync("dave");
        Assert.Equal(3, CountTweets("dave"));

        // JSONL 作り直し(短くなる)→ offset > length → 自動 rebuild
        File.Delete(_db.JsonlPath("dave"));
        WriteJsonl("dave", TweetLine(7));
        var result = await importer.ImportUserAsync("dave");
        Assert.Equal(1, result.NewTweets);
        Assert.Equal(1, CountTweets("dave"));
    }

    [Fact]
    public async Task KeysetPaginationOrdersBySortKeyDesc()
    {
        var users = new UserRepository(_db);
        await users.AddAsync("erin");
        WriteJsonl("erin",
            TweetLine(1, "Wed Apr 11 08:26:14 +0000 2007"),
            TweetLine(2, "Wed Oct 10 20:19:24 +0000 2018"),
            TweetLine(3, "Tue Jul 21 20:23:54 +0000 2026"));
        var importer = new JsonlImporter(_db);
        await importer.ImportUserAsync("erin");

        var tweets = new TweetRepository(_db);
        var page1 = await tweets.GetPageAsync(["erin"], false, null, 2);
        Assert.Equal(new[] { "3", "2" }, page1.Rows.Select(r => r.TweetId));

        var last = page1.Rows[^1];
        var page2 = await tweets.GetPageAsync(["erin"], false, (last.SortKey, last.IdInt), 2);
        Assert.Equal(new[] { "1" }, page2.Rows.Select(r => r.TweetId));
    }

    [Fact]
    public async Task MediaPageReturnsOwnMediaOnlyExcludingRetweets()
    {
        var users = new UserRepository(_db);
        await users.AddAsync("grace");
        WriteJsonl("grace",
            // 本文画像2枚 (最新)
            """{"id":"4","created_at":"Tue Jul 21 20:23:54 +0000 2026","full_text":"own2","entities":[{"type":"photo","link":"https://pbs.twimg.com/media/A1.jpg"},{"type":"photo","link":"https://pbs.twimg.com/media/A2.jpg"}]}""",
            // 引用: 本文画像1枚 + 引用先画像1枚
            """{"id":"3","created_at":"Wed Oct 10 20:19:24 +0000 2018","full_text":"quote","entities":[{"type":"photo","link":"https://pbs.twimg.com/media/B1.jpg"}],"quoted_status":{"id":"30","full_text":"q","entities":[{"type":"photo","link":"https://pbs.twimg.com/media/QB.jpg"}]}}""",
            // RT: RT元画像のみ → メディア欄に出ない
            """{"id":"2","created_at":"Wed Oct 10 10:00:00 +0000 2018","full_text":"RT @a: x","retweeted_status":{"id":"20","full_text":"x","entities":[{"type":"photo","link":"https://pbs.twimg.com/media/RT1.jpg"}]}}""",
            // 画像なし
            """{"id":"1","created_at":"Wed Apr 11 08:26:14 +0000 2007","full_text":"plain","entities":[]}""");
        var importer = new JsonlImporter(_db);
        await importer.ImportUserAsync("grace");

        var tweets = new TweetRepository(_db);
        var page = await tweets.GetMediaPageAsync(["grace"], after: null, limit: 10);
        // 本文画像のみ・新しい順・同一ツイート内は idx 昇順
        Assert.Equal(new[] { ("4", 1), ("4", 2), ("3", 1) },
            page.Select(m => (m.TweetId, m.Idx)));

        // keyset ページング (limit 2 → 続き)
        var p1 = await tweets.GetMediaPageAsync(["grace"], null, 2);
        var last = p1[^1];
        var p2 = await tweets.GetMediaPageAsync(["grace"], (last.SortKey, last.IdInt, last.Idx), 2);
        Assert.Equal(new[] { ("4", 1), ("4", 2) }, p1.Select(m => (m.TweetId, m.Idx)));
        Assert.Equal(new[] { ("3", 1) }, p2.Select(m => (m.TweetId, m.Idx)));
    }

    [Fact]
    public async Task SearchBucketImportDoesNotOverwriteProfile()
    {
        var users = new UserRepository(_db);
        await users.AddAsync("searches/kw-12345678");
        WriteJsonl("searches/kw-12345678",
            """{"id":"1","full_text":"hit","user":{"username":"someone","display_name":"Someone Else","profile_image_url":"https://pbs.twimg.com/profile_images/9/x_normal.jpg"}}""");

        var importer = new JsonlImporter(_db);
        var result = await importer.ImportUserAsync("searches/kw-12345678");
        Assert.Equal(1, result.NewTweets);

        // バケットの表示名は他人の user オブジェクトで上書きされない
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT display_name FROM users WHERE username = 'searches/kw-12345678'";
        Assert.Equal(DBNull.Value, cmd.ExecuteScalar());

        // author 列には実投稿者が入る
        var tweets = new TweetRepository(_db);
        var page = await tweets.GetPageAsync(["searches/kw-12345678"], false, null, 10);
        Assert.Equal("someone", page.Rows.Single().AuthorUsername);
        Assert.Equal("Someone Else", page.Rows.Single().AuthorDisplayName);
    }

    [Fact]
    public async Task SameTweetImportsIntoArchiveAndSearchBucket()
    {
        var users = new UserRepository(_db);
        await users.AddAsync("alice");
        await users.AddAsync("searches/kw-12345678");
        var line = TweetLine(1);
        WriteJsonl("alice", line);
        WriteJsonl("searches/kw-12345678", line);

        var importer = new JsonlImporter(_db);
        Assert.Equal(1, (await importer.ImportUserAsync("alice")).NewTweets);
        Assert.Equal(1, (await importer.ImportUserAsync("searches/kw-12345678")).NewTweets);
        Assert.Equal(1, CountTweets("alice"));
        Assert.Equal(1, CountTweets("searches/kw-12345678"));

        // タグを両方に付与 → バケット削除でバケットの割当だけ消える
        var tags = new TagRepository(_db);
        var tagId = await tags.AddAsync("A");
        await tags.AssignAsync("alice", tagId);
        await tags.AssignAsync("searches/kw-12345678", tagId);

        // バケット削除ではアーカイブ側の行は残る
        await users.DeleteArchiveAsync("searches/kw-12345678");
        Assert.Equal(0, CountTweets("searches/kw-12345678"));
        Assert.Equal(1, CountTweets("alice"));
        var assignments = await tags.GetAssignmentsAsync();
        Assert.False(assignments.ContainsKey("searches/kw-12345678"));
        Assert.Equal(new[] { tagId }, assignments["alice"]);
    }

    [Fact]
    public async Task GetAllAsyncExcludesSearchBuckets()
    {
        var users = new UserRepository(_db);
        await users.AddAsync("alice");
        await users.AddAsync("searches/kw-12345678");

        Assert.Equal(new[] { "alice" },
            (await users.GetAllAsync()).Select(u => u.Username));
        Assert.Equal(new[] { "searches/kw-12345678" },
            (await users.GetSearchBucketsAsync()).Select(u => u.Username));
    }

    [Fact]
    public async Task ByteOffsetsSurviveMultibyteText()
    {
        var users = new UserRepository(_db);
        await users.AddAsync("frank");
        // 絵文字・日本語混在でバイトオフセットずれがないこと
        var line1 = """{"id":"1","full_text":"🇯🇵 日本語テキスト 🚀","entities":[]}""";
        var line2 = """{"id":"2","full_text":"second","entities":[]}""";
        WriteJsonl("frank", line1, line2);
        var importer = new JsonlImporter(_db);
        var result = await importer.ImportUserAsync("frank");
        Assert.Equal(2, result.NewTweets);

        // raw_offset/raw_length で元の行を正確に切り出せること
        var tweets = new TweetRepository(_db);
        var page = await tweets.GetPageAsync(["frank"], false, null, 10);
        var row1 = page.Rows.Single(r => r.TweetId == "1");
        using var stream = File.OpenRead(_db.JsonlPath("frank"));
        stream.Seek(row1.RawOffset, SeekOrigin.Begin);
        var buf = new byte[row1.RawLength];
        stream.ReadExactly(buf);
        Assert.Equal(line1, Encoding.UTF8.GetString(buf));
    }
}
