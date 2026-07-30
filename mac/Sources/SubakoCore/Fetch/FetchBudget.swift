import Foundation

/// 一括更新でのリクエスト総量の管理 (C# Services/FetchBudget.cs の移植)。
/// 消費数は main.py の finally が必ず出す完了ログからしか取れない。
public enum FetchBudget {
    // main.py: 完了: 新規保存=%d件 / 総保存=%d件 / APIリクエスト=%d回 / 保存先=%s
    // finally ブロックなので上限到達 (exit 10) や API エラーでも出力される。
    // 唯一出ないのは中断 (プロセス kill で finally が走らない)。
    private static let consumedRequestsRegex =
        try! NSRegularExpression(pattern: "APIリクエスト=(\\d+)回")

    /// 実行ログから消費リクエスト数を読み取る。見つからなければ nil。
    /// 複数行あるときは最後の値を採る。
    public static func parseConsumedRequests(_ logLines: [String]) -> Int? {
        var last: Int?
        for line in logLines {
            let ns = line as NSString
            for m in consumedRequestsRegex.matches(
                in: line, range: NSRange(location: 0, length: ns.length))
            {
                if let n = Int(ns.substring(with: m.range(at: 1))) {
                    last = n
                }
            }
        }
        return last
    }

    /// 1 件分の実行後に残量から差し引く数。
    /// 消費数が読み取れなかった実行は「割り当てた分を全部使った」とみなす —
    /// 過少カウントで総上限を超えるより、早めに止まる方が安全なため。
    public static func consumedOrGranted(parsedConsumed: Int?, granted: Int) -> Int {
        parsedConsumed ?? granted
    }
}
