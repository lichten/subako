using System.Text.RegularExpressions;

namespace TweetViewer.Data;

/// <summary>
/// 検索クエリと min_retweets: / min_faves: 演算子の合成・分解。
/// 合成時は OR の結合順が壊れないよう元クエリを (...) で包み、
/// 分解時はその括弧を1枚だけ外して往復可能にする。
/// </summary>
public static partial class SearchQueryOperators
{
    [GeneratedRegex(@"\bmin_(retweets|faves):(\d+)")]
    private static partial Regex OperatorRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    /// <summary>
    /// since: / until: (および since_time: / until_time:) を含むか。
    /// Python の sorsa_fetcher.fetcher.has_period_operator / Swift の
    /// SearchQueryOperators.hasPeriodOperator と同一規則にすること — fetcher は
    /// この条件でバックフィルを exit 1 拒否するので、ビューアは同じ判定で導線を
    /// 出さないようにする (docs/trending-jp.md §10.3-2)。
    /// </summary>
    [GeneratedRegex(@"\b(?:since|until)(?:_time)?:", RegexOptions.IgnoreCase)]
    private static partial Regex PeriodOperatorRegex();

    public static bool HasPeriodOperator(string query) =>
        PeriodOperatorRegex().IsMatch(query);

    public static string Compose(string baseQuery, long? minRetweets, long? minFaves)
    {
        var query = baseQuery.Trim();
        if (minRetweets is null && minFaves is null)
            return query;
        return $"({query})"
            + (minRetweets is { } r ? $" min_retweets:{r}" : "")
            + (minFaves is { } f ? $" min_faves:{f}" : "");
    }

    public static (string BaseQuery, long? MinRetweets, long? MinFaves) Split(string fullQuery)
    {
        long? minRetweets = null;
        long? minFaves = null;
        var stripped = OperatorRegex().Replace(fullQuery, m =>
        {
            // 重複時は最後の出現を採用
            var value = long.Parse(m.Groups[2].Value);
            if (m.Groups[1].Value == "retweets")
                minRetweets = value;
            else
                minFaves = value;
            return "";
        });
        var baseQuery = WhitespaceRegex().Replace(stripped, " ").Trim();
        // Compose が付けた外側括弧の逆変換。演算子がなかった場合はユーザーが
        // 意図して書いた括弧の可能性があるため外さない (不要な差分 = 状態リセットを防ぐ)
        if ((minRetweets is not null || minFaves is not null) && IsFullyParenthesized(baseQuery))
            baseQuery = baseQuery[1..^1].Trim();
        return (baseQuery, minRetweets, minFaves);
    }

    /// <summary>先頭 '(' の対応括弧が末尾にある (= 全体が1組の括弧で包まれている) か。</summary>
    private static bool IsFullyParenthesized(string query)
    {
        if (query.Length < 2 || query[0] != '(' || query[^1] != ')')
            return false;
        var depth = 0;
        for (var i = 0; i < query.Length; i++)
        {
            if (query[i] == '(')
                depth++;
            else if (query[i] == ')')
            {
                depth--;
                if (depth == 0)
                    return i == query.Length - 1;
            }
        }
        return false;
    }
}
