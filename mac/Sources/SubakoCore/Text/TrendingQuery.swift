import Foundation

/// 「その日、日本語圏で話題のツイート」を取るための検索クエリの生成
/// (docs/trending-jp.md §1〜§4)。
///
/// **C# 側 (viewer/TweetViewer/Models/TrendingQuery.cs) と出力文字列を 1 バイトも
/// 違えないこと** — 同じ日・同じ閾値なら両 OS から同じクエリが出て、同じバケットを
/// 相手のカーソルを壊さずに更新できる (docs/mac-port-notes.md §2 の共有契約)。
public enum TrendingQuery {
    /// 固定バケットの slug。クエリに日付が入るので `SearchSlug.from(query)` だと
    /// 毎日別バケットになってしまう。slug は不変 ID なので固定名でよい
    /// (docs/data-layer.md §1.5、docs/trending-jp.md §7-3)。
    public static let bucketSlug = "trending-jp"

    /// バケットの既定表示名。
    public static let defaultName = "今日の話題 (日本)"

    /// 並び順。X 検索の「話題」タブ相当 (docs/trending-jp.md §1)。
    public static let order = "popular"

    /// JST。夏時間が無いので固定オフセットで扱う (C# 側とタイムゾーン ID の
    /// 命名差 "Asia/Tokyo" / "Tokyo Standard Time" を跨がないため)。
    public static let jst = TimeZone(secondsFromGMT: 9 * 3600) ?? .gmt

    /// 対象日の種別。既定値と推奨閾値がこれで決まる。
    public enum TargetDay: Sendable, Equatable {
        /// 前日 (JST) の丸一日。いいね数が 24 時間以上経って安定しており、
        /// 件数も順位も再現性がある (docs/trending-jp.md §3.1)。
        case yesterday
        /// 当日 (JST) の 0 時から現在まで。速報性はあるが取得時刻で件数が大きく変わる。
        case today

        /// 実測に基づく min_faves の推奨値 (docs/trending-jp.md §3・§3.1)。
        /// 当日は経過時間が短くいいねが伸びきっていないため 1/5 に下げる。
        public var suggestedMinFaves: Int64 {
            switch self {
            case .yesterday: return 50_000
            case .today: return 10_000
            }
        }
    }

    /// 対象日の JST 暦日を返す (`now` は実行時刻)。
    public static func date(for day: TargetDay, now: Date) -> Date {
        switch day {
        case .today: return now
        case .yesterday: return now.addingTimeInterval(-24 * 3600)
        }
    }

    /// `since:` / `until:` に渡す UTC 文字列 (`YYYY-MM-DD_HH:MM:SS_UTC`)。
    /// JST 00:00 = 前日 15:00 UTC (docs/trending-jp.md §4)。
    public static func utcTimestamp(_ date: Date) -> String {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = .gmt
        let c = calendar.dateComponents([.year, .month, .day, .hour, .minute, .second], from: date)
        return String(
            format: "%04d-%02d-%02d_%02d:%02d:%02d_UTC",
            c.year ?? 0, c.month ?? 1, c.day ?? 1, c.hour ?? 0, c.minute ?? 0, c.second ?? 0)
    }

    /// 指定 JST 暦日の 00:00 (= その日の since 境界)。
    public static func jstMidnight(of date: Date) -> Date {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = jst
        return calendar.startOfDay(for: date)
    }

    /// 検索クエリを組む。
    ///
    /// `until` は `day == .today` のとき省略する (0 時から現在まで)。
    /// 出力は `SearchQueryOperators.compose` の正準形 — Windows の編集ダイアログで
    /// `split` → `compose` しても文字列が変わらないため、fetcher のカーソルリセットが
    /// 起きない (docs/trending-jp.md §10.3-3)。
    ///
    ///     (lang:ja -filter:retweets since:… until:…) min_faves:50000
    public static func build(day: TargetDay, minFaves: Int64, now: Date) -> String {
        let start = jstMidnight(of: date(for: day, now: now))
        var operators = ["lang:ja", "-filter:retweets", "since:\(utcTimestamp(start))"]
        if day == .yesterday {
            let end = start.addingTimeInterval(24 * 3600)
            operators.append("until:\(utcTimestamp(end))")
        }
        return SearchQueryOperators.compose(
            baseQuery: operators.joined(separator: " "),
            minRetweets: nil, minFaves: minFaves)
    }

    /// バケットの表示ラベルに添える対象日 (`YYYY-MM-DD`、JST 暦)。
    public static func jstDateLabel(_ date: Date) -> String {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = jst
        let c = calendar.dateComponents([.year, .month, .day], from: date)
        return String(format: "%04d-%02d-%02d", c.year ?? 0, c.month ?? 1, c.day ?? 1)
    }
}
