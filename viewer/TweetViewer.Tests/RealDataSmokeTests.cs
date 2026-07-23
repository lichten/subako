using System.IO;
using TweetViewer.Data;

namespace TweetViewer.Tests;

/// <summary>
/// 実データ (data/) に対する取込スモークテスト。
/// 環境変数 TWEETVIEWER_REAL_DATA_DIR が設定されている場合のみ実行される。
/// 実運用と同じ data/viewer.db を作成・更新する点に注意。
/// </summary>
public class RealDataSmokeTests
{
    private static string? RealDataDir =>
        Environment.GetEnvironmentVariable("TWEETVIEWER_REAL_DATA_DIR") is { Length: > 0 } dir &&
        Directory.Exists(dir)
            ? dir
            : null;

    [Fact]
    public async Task ImportAllUsers_CountsMatchJsonlLines()
    {
        if (RealDataDir is not { } dataDir)
            return;

        var db = new ViewerDatabase(dataDir);
        db.EnsureCreated();
        var users = new UserRepository(db);
        var importer = new JsonlImporter(db);

        await users.RegisterExistingDataDirsAsync();
        foreach (var user in await users.GetAllAsync())
        {
            var result = await importer.ImportUserAsync(user.Username);
            var jsonlLines = File.ReadLines(db.JsonlPath(user.Username)).Count(l => l.Length > 0);

            using var conn = db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM tweets WHERE username = $u";
            cmd.Parameters.AddWithValue("$u", user.Username);
            var dbCount = (long)cmd.ExecuteScalar()!;

            Assert.Equal(jsonlLines, dbCount + result.SkippedLines);
            Assert.Equal(0, result.SkippedLines);

            // 再実行は差分ゼロ
            var again = await importer.ImportUserAsync(user.Username);
            Assert.Equal(0, again.NewTweets);
        }
    }
}
