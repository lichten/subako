import Foundation

/// 検索クエリと min_retweets: / min_faves: 演算子の合成・分解
/// (C# Data/SearchQueryOperators.cs の移植)。
/// 合成時は OR の結合順が壊れないよう元クエリを (...) で包み、
/// 分解時はその括弧を 1 枚だけ外して往復可能にする。
public enum SearchQueryOperators {
    private static let operatorRegex =
        try! NSRegularExpression(pattern: #"\bmin_(retweets|faves):(\d+)"#)

    private static let whitespaceRegex =
        try! NSRegularExpression(pattern: #"\s+"#)

    public static func compose(baseQuery: String, minRetweets: Int64?, minFaves: Int64?) -> String {
        let query = baseQuery.trimmingCharacters(in: .whitespaces)
        if minRetweets == nil && minFaves == nil {
            return query
        }
        var result = "(\(query))"
        if let r = minRetweets { result += " min_retweets:\(r)" }
        if let f = minFaves { result += " min_faves:\(f)" }
        return result
    }

    public static func split(_ fullQuery: String)
        -> (baseQuery: String, minRetweets: Int64?, minFaves: Int64?)
    {
        var minRetweets: Int64?
        var minFaves: Int64?
        let ns = fullQuery as NSString
        var stripped = ""
        var pos = 0
        for m in operatorRegex.matches(in: fullQuery, range: NSRange(location: 0, length: ns.length)) {
            // 重複時は最後の出現を採用
            let value = Int64(ns.substring(with: m.range(at: 2))) ?? 0
            if ns.substring(with: m.range(at: 1)) == "retweets" {
                minRetweets = value
            } else {
                minFaves = value
            }
            stripped += ns.substring(with: NSRange(location: pos, length: m.range.location - pos))
            pos = m.range.location + m.range.length
        }
        stripped += ns.substring(from: pos)

        var baseQuery = whitespaceRegex.stringByReplacingMatches(
            in: stripped, range: NSRange(location: 0, length: (stripped as NSString).length),
            withTemplate: " ")
            .trimmingCharacters(in: .whitespaces)
        // Compose が付けた外側括弧の逆変換。演算子がなかった場合はユーザーが
        // 意図して書いた括弧の可能性があるため外さない (不要な差分 = 状態リセットを防ぐ)
        if (minRetweets != nil || minFaves != nil), isFullyParenthesized(baseQuery) {
            baseQuery = String(baseQuery.dropFirst().dropLast())
                .trimmingCharacters(in: .whitespaces)
        }
        return (baseQuery, minRetweets, minFaves)
    }

    /// 先頭 '(' の対応括弧が末尾にある (= 全体が 1 組の括弧で包まれている) か。
    private static func isFullyParenthesized(_ query: String) -> Bool {
        guard query.count >= 2, query.first == "(", query.last == ")" else { return false }
        var depth = 0
        for (offset, ch) in query.enumerated() {
            if ch == "(" {
                depth += 1
            } else if ch == ")" {
                depth -= 1
                if depth == 0 {
                    return offset == query.count - 1
                }
            }
        }
        return false
    }
}
