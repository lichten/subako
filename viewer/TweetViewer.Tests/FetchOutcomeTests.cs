using TweetViewer.Services;
using TweetViewer.ViewModels;

namespace TweetViewer.Tests;

/// <summary>
/// 取得結果の表示文言と「完了後にログを自動表示するか」(HasIssues) の判定。
/// RunFetchAsync の自動表示はこのフラグに従うため、ここで固定する。
/// </summary>
public class FetchOutcomeTests
{
    [Fact]
    public void 中断はexit_codeに関係なくissue扱い()
    {
        var (message, hasIssues) = FetchOutcome.DescribeSingle(
            new FetchResult(ExitCode: 0, Cancelled: true), FetchMode.Update);

        Assert.Contains("中断しました", message);
        Assert.True(hasIssues);
    }

    [Fact]
    public void 正常終了だけがissueなし()
    {
        var (message, hasIssues) = FetchOutcome.DescribeSingle(
            new FetchResult(ExitCode: 0, Cancelled: false), FetchMode.Update);

        Assert.Equal("取得完了", message);
        Assert.False(hasIssues);
    }

    [Theory]
    [InlineData(FetchMode.Search)]
    [InlineData(FetchMode.SearchUpdate)]
    [InlineData(FetchMode.SearchBackfill)]
    public void 検索系の上限到達は再開の案内つきでissue(FetchMode mode)
    {
        var (message, hasIssues) = FetchOutcome.DescribeSingle(
            new FetchResult(FetchOutcome.BudgetExhaustedExitCode, Cancelled: false), mode);

        Assert.Contains("続きから再開します", message);
        Assert.True(hasIssues);
    }

    [Fact]
    public void フォロー取得の上限到達は再開できない案内つきでissue()
    {
        var (message, hasIssues) = FetchOutcome.DescribeSingle(
            new FetchResult(FetchOutcome.BudgetExhaustedExitCode, Cancelled: false),
            FetchMode.Followings);

        Assert.Contains("実行し直してください", message);
        Assert.DoesNotContain("続きから再開します", message);
        Assert.True(hasIssues);
    }

    [Theory]
    [InlineData(FetchMode.Update)]
    [InlineData(FetchMode.Backfill)]
    public void タイムライン系の上限到達はバックフィル再開の案内つきでissue(FetchMode mode)
    {
        var (message, hasIssues) = FetchOutcome.DescribeSingle(
            new FetchResult(FetchOutcome.BudgetExhaustedExitCode, Cancelled: false), mode);

        Assert.Contains("バックフィル", message);
        Assert.True(hasIssues);
    }

    [Fact]
    public void エラー終了はissue()
    {
        var (message, hasIssues) = FetchOutcome.DescribeSingle(
            new FetchResult(ExitCode: 1, Cancelled: false), FetchMode.Update);

        Assert.Equal("エラー終了 (exit code 1)", message);
        Assert.True(hasIssues);
    }

    [Fact]
    public void 一括_全件成功はissueなし()
    {
        var (summary, hasIssues) = FetchOutcome.DescribeBatch(
            succeeded: 3, total: 3, consumedTotal: 12, failed: [], stopReason: null);

        Assert.Equal("完了 3/3 件 / 消費 12 リクエスト", summary);
        Assert.False(hasIssues);
    }

    [Fact]
    public void 一括_個別失敗ありはissue()
    {
        var (summary, hasIssues) = FetchOutcome.DescribeBatch(
            succeeded: 2, total: 3, consumedTotal: 12,
            failed: ["@alice (exit 1)"], stopReason: null);

        Assert.Contains("失敗 1 件", summary);
        Assert.True(hasIssues);
    }

    [Fact]
    public void 一括_全件成功でも途中停止はissue()
    {
        // 上限で停止した一括更新: ここまで全部成功でも「中断」なので表示する
        var (summary, hasIssues) = FetchOutcome.DescribeBatch(
            succeeded: 2, total: 5, consumedTotal: 50,
            failed: [], stopReason: "リクエスト上限に達したため中断しました");

        Assert.EndsWith("— リクエスト上限に達したため中断しました", summary);
        Assert.True(hasIssues);
    }
}
