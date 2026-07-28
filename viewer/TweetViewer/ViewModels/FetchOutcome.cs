using TweetViewer.Services;

namespace TweetViewer.ViewModels;

/// <summary>
/// 取得結果からユーザー向けメッセージと「完了後にログを自動表示すべきか」を決める。
/// FetchProcessService は sealed で差し替えられないため、判定を純関数に切り出して
/// テスト可能にしている (FetchBudget と同じ方針)。
/// </summary>
public static class FetchOutcome
{
    /// <summary>main.py が RequestBudgetExhausted で返す終了コード。</summary>
    public const int BudgetExhaustedExitCode = 10;

    /// <summary>単体取得の結果表示。HasIssues = 失敗・中断・上限到達。</summary>
    public static (string Message, bool HasIssues) DescribeSingle(FetchResult result, FetchMode mode) =>
        result switch
        {
            { Cancelled: true } => ("中断しました(途中までの取得分は保存済み)", true),
            { ExitCode: 0 } => ("取得完了", false),
            { ExitCode: BudgetExhaustedExitCode }
                when mode is FetchMode.Search or FetchMode.SearchUpdate or FetchMode.SearchBackfill =>
                ("リクエスト上限に達したため中断しました(取得分は保存済み)。" +
                 "同じ操作をもう一度実行すると続きから再開します", true),
            // フォロー一覧はカーソルを保存しないので「再開」はできない (docs/data-layer.md §1.7)
            { ExitCode: BudgetExhaustedExitCode } when mode is FetchMode.Followings =>
                ("リクエスト上限に達したため、フォロー一覧を最後まで取得できませんでした" +
                 "(取得できた分だけ登録できます)。全件必要なら上限を増やして実行し直してください", true),
            { ExitCode: BudgetExhaustedExitCode } =>
                ("リクエスト上限に達したため中断しました(取得分は保存済み)。" +
                 "再度バックフィルを実行すると続きから再開します", true),
            _ => ($"エラー終了 (exit code {result.ExitCode})", true),
        };

    /// <summary>一括取得のサマリ。HasIssues = 個別失敗あり、または途中停止 (中断・上限)。</summary>
    public static (string Summary, bool HasIssues) DescribeBatch(
        int succeeded, int total, int consumedTotal, IReadOnlyList<string> failed, string? stopReason)
    {
        var summary = $"完了 {succeeded}/{total} 件 / 消費 {consumedTotal} リクエスト";
        if (failed.Count > 0)
            summary += $" / 失敗 {failed.Count} 件";
        if (stopReason is not null)
            summary += $" — {stopReason}";
        return (summary, failed.Count > 0 || stopReason is not null);
    }
}
