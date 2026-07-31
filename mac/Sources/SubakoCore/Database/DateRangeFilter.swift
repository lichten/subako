import Foundation

// 固定入力 (gregorian + 妥当な year/month/day) に対する Calendar API は失敗しないため、
// 本ファイルでは force unwrap を許容する。
// swiftlint:disable force_unwrapping

/// sort_key (epoch 秒) に対する半開区間 [fromEpoch, toEpochExclusive)
/// (C# Models/DateRangeFilter.cs の移植)。
public struct DateRangeFilter: Sendable, Equatable {
    public let fromEpoch: Int64
    public let toEpochExclusive: Int64

    public init(fromEpoch: Int64, toEpochExclusive: Int64) {
        self.fromEpoch = fromEpoch
        self.toEpochExclusive = toEpochExclusive
    }

    /// ローカル暦の (年 / 年+月 / 年+月+日) を epoch 秒の半開区間へ変換する。
    /// 保存は UTC・表示はローカルのため、境界をローカル 0 時で切らないと
    /// 月初 0〜9 時 (JST) のツイートが前の期間に漏れる。
    /// timeZone はテストで固定注入するための引数 (実行時は TimeZone.current)。
    public static func fromLocalParts(
        year: Int, month: Int?, day: Int?, timeZone: TimeZone
    ) -> DateRangeFilter {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = timeZone

        var comps = DateComponents()
        comps.year = year
        comps.month = month ?? 1
        comps.day = day ?? 1
        let from = calendar.date(from: comps)!

        let to: Date
        if day != nil {
            to = calendar.date(byAdding: .day, value: 1, to: from)!
        } else if month != nil {
            to = calendar.date(byAdding: .month, value: 1, to: from)!
        } else {
            to = calendar.date(byAdding: .year, value: 1, to: from)!
        }
        return DateRangeFilter(
            fromEpoch: Int64(from.timeIntervalSince1970),
            toEpochExclusive: Int64(to.timeIntervalSince1970))
    }

    /// MIN/MAX sort_key がローカル暦で被覆する年の昇順リスト (年選択 UI 用)。
    public static func yearsCovered(
        minEpoch: Int64, maxEpoch: Int64, timeZone: TimeZone
    ) -> [Int] {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = timeZone
        let minYear = calendar.component(
            .year, from: Date(timeIntervalSince1970: TimeInterval(minEpoch)))
        let maxYear = calendar.component(
            .year, from: Date(timeIntervalSince1970: TimeInterval(maxEpoch)))
        return maxYear < minYear ? [] : Array(minYear...maxYear)
    }

    /// 指定年月の日数 (日選択 UI 用)。
    public static func daysIn(year: Int, month: Int, timeZone: TimeZone) -> Int {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = timeZone
        var comps = DateComponents()
        comps.year = year
        comps.month = month
        comps.day = 1
        let date = calendar.date(from: comps)!
        return calendar.range(of: .day, in: .month, for: date)!.count
    }
}

// swiftlint:enable force_unwrapping
