using System.IO;
using System.Text;
using TweetViewer.Data;

namespace TweetViewer.Tests;

/// <summary>data/_followings/&lt;source&gt;.jsonl の読み取り (docs/data-layer.md §1.7)。</summary>
public sealed class FollowingsFileTests : IDisposable
{
    private readonly string _dataDir;

    public FollowingsFileTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "SubakoTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dataDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private void WriteFollowings(string source, params string[] lines)
    {
        var path = FollowingsFile.PathFor(_dataDir, source);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join("", lines.Select(l => l + "\n")), new UTF8Encoding(false));
    }

    private static string UserLine(string username, string? displayName = null) =>
        displayName is null
            ? $$"""{"id":"1","username":"{{username}}"}"""
            : $$"""{"id":"1","username":"{{username}}","display_name":"{{displayName}}"}""";

    [Fact]
    public void ファイル順を保ちつつ壊れ行と重複を落とす()
    {
        WriteFollowings("src",
            UserLine("alice", "Alice"),
            UserLine("@bob"),                     // @ 付きは剥がす
            "{壊れた JSON",                        // スキップ
            """{"id":"9","display_name":"名前だけ"}""",   // username 無しはスキップ
            "",                                    // 空行はスキップ
            UserLine("ALICE"),                     // 大文字小文字無視で重複
            UserLine("carol", "Carol"));

        var entries = FollowingsFile.Read(_dataDir, "src");

        Assert.Equal(new[] { "alice", "bob", "carol" }, entries.Select(e => e.Username));
        Assert.Equal("Alice", entries[0].DisplayName);
        Assert.Null(entries[1].DisplayName);
        Assert.Equal(3, FollowingsFile.Count(_dataDir, "src"));
    }

    [Fact]
    public void ファイルが無ければ空リスト()
    {
        Assert.Empty(FollowingsFile.Read(_dataDir, "unknown"));
        Assert.Equal(0, FollowingsFile.Count(_dataDir, "unknown"));
    }

    [Fact]
    public void 空文字のusernameは落とす()
    {
        WriteFollowings("src", UserLine(""), UserLine("@"), UserLine("dave"));

        Assert.Equal(new[] { "dave" }, FollowingsFile.Read(_dataDir, "src").Select(e => e.Username));
    }

    [Fact]
    public void PathForは_followings配下を指す()
    {
        var path = FollowingsFile.PathFor(_dataDir, "alice");

        Assert.Equal(Path.Combine(_dataDir, FollowingsFile.DirName, "alice.jsonl"), path);
    }
}
