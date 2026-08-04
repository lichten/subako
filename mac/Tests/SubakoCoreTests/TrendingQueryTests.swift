import Foundation
import Testing
@testable import SubakoCore

/// 「その日の話題」クエリの生成規則 (docs/trending-jp.md §1〜§4)。
///
/// **期待値は C# 実装 (viewer/TweetViewer/Models/TrendingQuery.cs) との共有契約**。
/// 同じ日・同じ閾値なら両 OS が同一文字列を出すこと — 違うと同じバケットを
/// 更新するたびにクエリ変更とみなされてカーソルがリセットされる
/// (docs/mac-port-notes.md §2)。
@Suite struct TrendingQueryTests {
    /// JST 2026-08-04 10:40 (= UTC 2026-08-04 01:40)
    private static let now = Date(timeIntervalSince1970: 1_785_807_600)

    @Test func JST0時はUTCで前日15時() {
        let jstDay = TrendingQuery.jstMidnight(of: Self.now)
        #expect(TrendingQuery.utcTimestamp(jstDay) == "2026-08-03_15:00:00_UTC")
    }

    @Test func 前日は丸一日をsinceとuntilで囲う() {
        let query = TrendingQuery.build(day: .yesterday, minFaves: 50_000, now: Self.now)
        #expect(query == "(lang:ja -filter:retweets"
            + " since:2026-08-02_15:00:00_UTC until:2026-08-03_15:00:00_UTC) min_faves:50000")
    }

    @Test func 当日はuntilを付けない() {
        let query = TrendingQuery.build(day: .today, minFaves: 10_000, now: Self.now)
        #expect(query
            == "(lang:ja -filter:retweets since:2026-08-03_15:00:00_UTC) min_faves:10000")
    }

    /// Windows の編集ダイアログは Split → Compose するので、正準形でないと
    /// 保存しただけで文字列が変わり fetcher のカーソルがリセットされる (§10.3-3)。
    @Test func 編集ダイアログの往復で文字列が変わらない() {
        for day in [TrendingQuery.TargetDay.yesterday, .today] {
            let query = TrendingQuery.build(
                day: day, minFaves: day.suggestedMinFaves, now: Self.now)
            let (base, rt, fav) = SearchQueryOperators.split(query)
            #expect(rt == nil)
            #expect(fav == day.suggestedMinFaves)
            #expect(SearchQueryOperators.compose(
                baseQuery: base, minRetweets: rt, minFaves: fav) == query)
        }
    }

    /// 生成したクエリは必ず fetcher のバックフィル拒否に引っかかる形になる
    /// (期間演算子を含む = 1 回の取得で完結する)。
    @Test func 期間演算子を必ず含む() {
        let query = TrendingQuery.build(day: .yesterday, minFaves: 50_000, now: Self.now)
        #expect(query.contains("since:"))
        #expect(query.contains("until:"))
    }

    @Test func 月跨ぎと年跨ぎ() {
        // JST 2026-03-01 08:00 (= UTC 2026-02-28 23:00) の前日 = JST 2026-02-28
        let march1 = Date(timeIntervalSince1970: 1_772_319_600)
        #expect(TrendingQuery.build(day: .yesterday, minFaves: 1, now: march1)
            == "(lang:ja -filter:retweets"
            + " since:2026-02-27_15:00:00_UTC until:2026-02-28_15:00:00_UTC) min_faves:1")

        // JST 2026-01-01 00:30 (= UTC 2025-12-31 15:30) の前日 = JST 2025-12-31。
        // UTC 暦では前年なので、JST 暦で切っていないと 1 日ずれる
        let newYear = Date(timeIntervalSince1970: 1_767_195_000)
        #expect(TrendingQuery.build(day: .yesterday, minFaves: 1, now: newYear)
            == "(lang:ja -filter:retweets"
            + " since:2025-12-30_15:00:00_UTC until:2025-12-31_15:00:00_UTC) min_faves:1")
    }

    @Test func 推奨閾値は当日だけ下げる() {
        // 当日は経過時間が短くいいねが伸びきっていない (§3.1 の実測)
        #expect(TrendingQuery.TargetDay.yesterday.suggestedMinFaves == 50_000)
        #expect(TrendingQuery.TargetDay.today.suggestedMinFaves == 10_000)
    }

    @Test func 日付ラベルはJST暦() {
        #expect(TrendingQuery.jstDateLabel(Self.now) == "2026-08-04")
        #expect(TrendingQuery.jstDateLabel(
            TrendingQuery.date(for: .yesterday, now: Self.now)) == "2026-08-03")
    }

    /// slug は日替わりのクエリから導出せず固定 (docs/trending-jp.md §7-3)。
    @Test func バケットIDは固定() {
        #expect(SearchBuckets.bucketId(slug: TrendingQuery.bucketSlug)
            == "searches/trending-jp")
    }
}
