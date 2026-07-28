using TweetViewer.Services;

namespace TweetViewer.Tests;

/// <summary>一括更新の総リクエスト上限まわり (プロセス起動を伴わない純ロジック)。</summary>
public class FetchBudgetTests
{
    // main.py の finally が必ず出す完了ログ (上限到達・API エラーでも出る)
    private const string CompletionLine =
        "2026-07-27 11:14:23,456 INFO 完了: 新規保存=12件 / 総保存=3456件 / APIリクエスト=7回 / 保存先=data\\elonmusk";

    [Fact]
    public void ParsesConsumedRequestsFromCompletionLine()
    {
        Assert.Equal(7, FetchBudget.ParseConsumedRequests([CompletionLine]));
    }

    [Fact]
    public void ParsesFromRealisticLogSequence()
    {
        var log = new[]
        {
            "> python main.py alice --output-dir data --update --max-requests 100",
            "2026-07-27 11:14:20,000 INFO 既存ツイート 1,234 件を読み込みました",
            "2026-07-27 11:14:21,000 INFO [update] page=1 取得=20 新規=3 累計新規=3",
            "2026-07-27 11:14:22,000 INFO 既知ツイートのみのページに到達したため差分取得を終了します",
            CompletionLine,
        };
        Assert.Equal(7, FetchBudget.ParseConsumedRequests(log));
    }

    [Fact]
    public void ParsesFollowingsCompletionLine()
    {
        // --followings は TweetFetcher を通らず独自に完了ログを出すため、
        // 書式が崩れていないことをここで固定する (Python↔C# の請求契約)
        var log = new[]
        {
            "> python main.py alice --output-dir data --followings --max-requests 50",
            "2026-07-28 10:02:56,681 INFO [follows] page=1 取得=200 累計=200",
            "2026-07-28 10:02:56,686 INFO 完了: 新規保存=3749件 / 総保存=3749件 / " +
            "APIリクエスト=19回 / 保存先=data\\_followings\\alice.jsonl",
        };
        Assert.Equal(19, FetchBudget.ParseConsumedRequests(log));
    }

    [Fact]
    public void ReturnsNullWhenNoCompletionLine()
    {
        // 中断ボタンでプロセスを Kill したときは finally が走らず完了ログが出ない
        var log = new[] { "> python main.py alice", "2026-07-27 INFO [update] page=1 取得=20" };
        Assert.Null(FetchBudget.ParseConsumedRequests(log));
    }

    [Fact]
    public void EmptyLog_ReturnsNull()
    {
        Assert.Null(FetchBudget.ParseConsumedRequests([]));
    }

    [Fact]
    public void TakesLastValueWhenMultipleMatches()
    {
        var log = new[]
        {
            "完了: 新規保存=0件 / 総保存=1件 / APIリクエスト=2回 / 保存先=a",
            "完了: 新規保存=0件 / 総保存=1件 / APIリクエスト=9回 / 保存先=b",
        };
        Assert.Equal(9, FetchBudget.ParseConsumedRequests(log));
    }

    [Fact]
    public void UnparsedRunCountsAsFullGrant()
    {
        // 消費数が読めなかった実行は「割り当てを全部使った」扱い。
        // 過少カウントで総上限を超えるより、早めに止まる方が安全なため
        Assert.Equal(50, FetchBudget.ConsumedOrGranted(null, granted: 50));
    }

    [Fact]
    public void ParsedValueIsUsedAsIs()
    {
        Assert.Equal(3, FetchBudget.ConsumedOrGranted(3, granted: 50));
    }

    [Fact]
    public void OvershootIsNotClamped()
    {
        // リトライは 1 回ずつカウントされるので実消費が割当を上回ることがある。
        // 丸めると総量を超えてしまうため、そのまま差し引く
        Assert.Equal(55, FetchBudget.ConsumedOrGranted(55, granted: 50));
    }
}
