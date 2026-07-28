using System.IO;
using TweetViewer.Services;

namespace TweetViewer.Tests;

/// <summary>クラッシュ調査用ファイルログの書き込みとローテーション。</summary>
public sealed class AppLogTests : IDisposable
{
    private readonly string _dir;

    public AppLogTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "TweetViewerTests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Write_はディレクトリごと作ってタイムスタンプ付きで追記する()
    {
        var log = new AppLog(_dir);

        log.Write("一行目");
        log.Write("二行目");

        var lines = File.ReadAllLines(log.CurrentLogPath);
        Assert.Equal(2, lines.Length);
        Assert.EndsWith("一行目", lines[0]);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} ", lines[0]);
    }

    [Fact]
    public void WriteException_は例外の全文を残す()
    {
        var log = new AppLog(_dir);

        log.WriteException("テスト", new InvalidOperationException("何かが壊れた"));

        var text = File.ReadAllText(log.CurrentLogPath);
        Assert.Contains("[テスト]", text);
        Assert.Contains("何かが壊れた", text);
        Assert.Contains(nameof(InvalidOperationException), text);
    }

    [Fact]
    public void CleanupOldLogs_は新しい7個だけ残す()
    {
        Directory.CreateDirectory(_dir);
        for (var day = 1; day <= 10; day++)
            File.WriteAllText(Path.Combine(_dir, $"202601{day:00}.log"), "x");
        var log = new AppLog(_dir);

        log.CleanupOldLogs();

        var remaining = Directory.GetFiles(_dir).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(7, remaining.Count);
        Assert.Equal("20260104.log", remaining[0]);   // 古い 3 つ (01-03) が消える
        Assert.Equal("20260110.log", remaining[^1]);
    }

    [Fact]
    public void CleanupOldLogs_はディレクトリが無くても失敗しない()
    {
        var log = new AppLog(Path.Combine(_dir, "not-created"));

        log.CleanupOldLogs();   // 例外にならないこと
    }
}
