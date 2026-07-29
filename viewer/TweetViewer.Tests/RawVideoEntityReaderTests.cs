using System.IO;
using System.Text;
using TweetViewer.Data;

namespace TweetViewer.Tests;

/// <summary>
/// X 添付動画の表示時抽出 (raw_offset/raw_length による JSONL 再読み) のテスト。
/// オフセットは合成せず JsonlImporter の実機構で得る。
/// </summary>
public sealed class RawVideoEntityReaderTests : IDisposable
{
    private readonly string _dataDir;
    private readonly ViewerDatabase _db;

    public RawVideoEntityReaderTests()
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

    private const string VideoUrl = "https://video.twimg.com/ext_tw_video/1/pu/vid/480x270/a.mp4?tag=12";
    private const string PreviewUrl = "https://pbs.twimg.com/ext_tw_video_thumb/1/pu/img/b.jpg";

    private static string PlainLine(long id) =>
        $$"""{"id":"{{id}}","created_at":"Wed Apr 11 08:26:14 +0000 2007","full_text":"日本語 {{id}}","entities":[]}""";

    private static string VideoLine(long id) =>
        $$"""{"id":"{{id}}","created_at":"Wed Apr 11 08:26:14 +0000 2007","full_text":"動画 {{VideoUrl}}","entities":[{"type":"video","link":"{{VideoUrl}}","preview":"{{PreviewUrl}}"}]}""";

    private async Task<IReadOnlyList<TweetViewer.Models.TweetRow>> ImportAsync(
        string username, string lineEnding, params string[] lines)
    {
        await new UserRepository(_db).AddAsync(username);
        Directory.CreateDirectory(_db.UserDir(username));
        File.AppendAllText(_db.JsonlPath(username),
            string.Join("", lines.Select(l => l + lineEnding)), new UTF8Encoding(false));
        await new JsonlImporter(_db).ImportUserAsync(username);
        var page = await new TweetRepository(_db).GetPageAsync([username], false, null, 100);
        return page.Rows;
    }

    [Fact]
    public async Task ReadsVideoEntitiesOnlyForVideoTweets()
    {
        var rows = await ImportAsync("alice", "\n", PlainLine(1), VideoLine(2), PlainLine(3));
        Assert.Equal(3, rows.Count);

        var videos = new RawVideoEntityReader(_db).ReadForPage(rows);

        var (tweetId, list) = Assert.Single(videos);
        Assert.Equal("2", tweetId);
        var video = Assert.Single(list);
        Assert.Equal(VideoUrl, video.PageUrl);
        Assert.Equal(PreviewUrl, video.ThumbnailUrl);
    }

    [Fact]
    public async Task CrlfLinesAreReadCorrectly()
    {
        var rows = await ImportAsync("bob", "\r\n", VideoLine(1));
        var videos = new RawVideoEntityReader(_db).ReadForPage(rows);
        Assert.Single(videos);
    }

    [Fact]
    public async Task MissingJsonlYieldsEmptyWithoutThrowing()
    {
        var rows = await ImportAsync("carol", "\n", VideoLine(1));
        File.Delete(_db.JsonlPath("carol"));

        var videos = new RawVideoEntityReader(_db).ReadForPage(rows);
        Assert.Empty(videos);
    }
}
