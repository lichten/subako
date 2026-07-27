using System.Globalization;
using System.Text.RegularExpressions;

namespace TweetViewer.Services;

/// <summary>
/// 一括更新でのリクエスト総量の管理。
/// 消費数は main.py の finally が必ず出す完了ログからしか取れないため、その解析もここに置く。
/// </summary>
public static partial class FetchBudget
{
    // main.py: logger.info("完了: 新規保存=%d件 / 総保存=%d件 / APIリクエスト=%d回 / 保存先=%s", ...)
    // finally ブロックなので上限到達 (exit 10) や API エラーでも出力される。
    // 唯一出ないのは中断ボタン (プロセスを Kill するため finally が走らない)。
    [GeneratedRegex(@"APIリクエスト=(\d+)回")]
    private static partial Regex ConsumedRequestsRegex();

    /// <summary>
    /// 実行ログから消費リクエスト数を読み取る。見つからなければ null。
    /// 複数行あるときは最後の値を採る。
    /// </summary>
    public static int? ParseConsumedRequests(IEnumerable<string> logLines)
    {
        int? last = null;
        foreach (var line in logLines)
        {
            if (line is null)
                continue;
            if (ConsumedRequestsRegex().Match(line) is { Success: true } m &&
                int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                last = n;
            }
        }
        return last;
    }

    /// <summary>
    /// 1 件分の実行後に残量から差し引く数。
    /// **消費数が読み取れなかった実行は「割り当てた分を全部使った」とみなす** —
    /// 過少カウントで総上限を超えるより、早めに止まる方が安全なため。
    /// 実消費が割当を上回ることもある (リトライは 1 回ずつカウントされる) ので、
    /// 読み取れた値は丸めずそのまま使う。
    /// </summary>
    public static int ConsumedOrGranted(int? parsedConsumed, int granted) =>
        parsedConsumed ?? granted;
}
