import Foundation

public struct FetchResult: Sendable, Equatable {
    public let exitCode: Int32
    public let cancelled: Bool

    public init(exitCode: Int32, cancelled: Bool) {
        self.exitCode = exitCode
        self.cancelled = cancelled
    }
}

/// 取得結果からユーザー向けメッセージと「完了後にログを自動表示すべきか」を決める
/// (C# ViewModels/FetchOutcome.cs の移植、exit code 契約は docs/fetcher-cli.md §3)。
public enum FetchOutcome {
    /// main.py が RequestBudgetExhausted で返す終了コード。
    public static let budgetExhaustedExitCode: Int32 = 10

    /// 単体取得の結果表示。hasIssues = 失敗・中断・上限到達。
    public static func describeSingle(
        _ result: FetchResult, mode: FetchMode
    ) -> (message: String, hasIssues: Bool) {
        if result.cancelled {
            return ("中断しました(途中までの取得分は保存済み)", true)
        }
        if result.exitCode == 0 {
            return ("取得完了", false)
        }
        if result.exitCode == budgetExhaustedExitCode {
            if mode.isSearch {
                return ("リクエスト上限に達したため中断しました(取得分は保存済み)。"
                    + "同じ操作をもう一度実行すると続きから再開します", true)
            }
            // フォロー一覧はカーソルを保存しないので「再開」はできない (docs/data-layer.md §1.7)
            if mode == .followings {
                return ("リクエスト上限に達したため、フォロー一覧を最後まで取得できませんでした"
                    + "(取得できた分だけ登録できます)。全件必要なら上限を増やして実行し直してください", true)
            }
            return ("リクエスト上限に達したため中断しました(取得分は保存済み)。"
                + "再度バックフィルを実行すると続きから再開します", true)
        }
        return ("エラー終了 (exit code \(result.exitCode))", true)
    }

    /// 実行ログから典型的な環境不備を検出して案内文を返す (該当なしなら nil)。
    public static func environmentHint(_ logLines: [String]) -> String? {
        if logLines.contains(where: { $0.contains("ModuleNotFoundError") }) {
            return "Python の依存パッケージが未導入のようです。fetcher のフォルダで "
                + "pip install -r requirements.txt を実行してください"
        }
        if logLines.contains(where: { $0.contains("SORSA_API_KEY") }) {
            return "Sorsa API キーが未設定のようです。fetcher のフォルダの .env に "
                + "SORSA_API_KEY を設定してください (.env.example 参照。キーは https://api.sorsa.io で取得)"
        }
        return nil
    }

    /// 一括取得のサマリ。hasIssues = 個別失敗あり、または途中停止 (中断・上限)。
    public static func describeBatch(
        succeeded: Int, total: Int, consumedTotal: Int, failed: [String], stopReason: String?
    ) -> (summary: String, hasIssues: Bool) {
        var summary = "完了 \(succeeded)/\(total) 件 / 消費 \(consumedTotal) リクエスト"
        if !failed.isEmpty {
            summary += " / 失敗 \(failed.count) 件"
        }
        if let stopReason {
            summary += " — \(stopReason)"
        }
        return (summary, !failed.isEmpty || stopReason != nil)
    }
}
