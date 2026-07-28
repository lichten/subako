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

    /// <summary>
    /// 実行ログから典型的な環境不備を検出して案内文を返す (該当なしなら null)。
    /// Python のトレースバックや fetcher のエラーログは初見では原因が読み取りにくいため、
    /// 対処をひと言に翻訳する (docs/release-plan.md §2-2)。
    /// </summary>
    public static string? EnvironmentHint(IReadOnlyList<string> logLines)
    {
        if (logLines.Any(l => l.Contains("ModuleNotFoundError")))
            return "Python の依存パッケージが未導入のようです。fetcher のフォルダで " +
                   "pip install -r requirements.txt を実行してください";
        if (logLines.Any(l => l.Contains("SORSA_API_KEY")))
            return "Sorsa API キーが未設定のようです。fetcher のフォルダの .env に " +
                   "SORSA_API_KEY を設定してください (.env.example 参照。キーは https://api.sorsa.io で取得)";
        return null;
    }

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
