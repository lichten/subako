using TweetViewer.Data;
using TweetViewer.Models;

namespace TweetViewer.Tests;

/// <summary>
/// 「その日の話題」クエリの生成規則 (docs/trending-jp.md §1〜§4)。
///
/// <para>
/// <b>期待値は Swift 実装 (mac/Tests/SubakoCoreTests/TrendingQueryTests.swift) との
/// 共有契約</b>。同じ日・同じ閾値なら両 OS が同一文字列を出すこと — 違うと同じ
/// バケットを更新するたびにクエリ変更とみなされてカーソルがリセットされる
/// (docs/mac-port-notes.md §2)。
/// </para>
/// </summary>
public class TrendingQueryTests
{
    /// <summary>JST 2026-08-04 10:40 (= UTC 2026-08-04 01:40)</summary>
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_785_807_600);

    [Fact]
    public void JST0時はUTCで前日15時()
    {
        var jstDay = TrendingQuery.JstMidnight(Now);
        Assert.Equal("2026-08-03_15:00:00_UTC", TrendingQuery.UtcTimestamp(jstDay));
    }

    [Fact]
    public void 前日は丸一日をsinceとuntilで囲う()
    {
        var query = TrendingQuery.Build(TrendingTargetDay.Yesterday, 50_000, Now);
        Assert.Equal("(lang:ja -filter:retweets" +
            " since:2026-08-02_15:00:00_UTC until:2026-08-03_15:00:00_UTC) min_faves:50000", query);
    }

    [Fact]
    public void 当日はuntilを付けない()
    {
        var query = TrendingQuery.Build(TrendingTargetDay.Today, 10_000, Now);
        Assert.Equal(
            "(lang:ja -filter:retweets since:2026-08-03_15:00:00_UTC) min_faves:10000", query);
    }

    /// <summary>
    /// 編集ダイアログは Split → Compose するので、正準形でないと保存しただけで
    /// 文字列が変わり fetcher のカーソルがリセットされる (§10.3-3)。
    /// </summary>
    [Theory]
    [InlineData(TrendingTargetDay.Yesterday)]
    [InlineData(TrendingTargetDay.Today)]
    public void 編集ダイアログの往復で文字列が変わらない(TrendingTargetDay day)
    {
        var suggested = TrendingQuery.SuggestedMinFaves(day);
        var query = TrendingQuery.Build(day, suggested, Now);

        var (baseQuery, minRt, minFav) = SearchQueryOperators.Split(query);

        Assert.Null(minRt);
        Assert.Equal(suggested, minFav);
        Assert.Equal(query, SearchQueryOperators.Compose(baseQuery, minRt, minFav));
    }

    /// <summary>
    /// 生成したクエリは必ず fetcher のバックフィル拒否に引っかかる形になる
    /// (期間演算子を含む = 1 回の取得で完結する)。
    /// </summary>
    [Fact]
    public void 期間演算子を必ず含む()
    {
        var query = TrendingQuery.Build(TrendingTargetDay.Yesterday, 50_000, Now);
        Assert.Contains("since:", query);
        Assert.Contains("until:", query);
        Assert.True(SearchQueryOperators.HasPeriodOperator(query));
    }

    [Fact]
    public void 月跨ぎと年跨ぎ()
    {
        // JST 2026-03-01 08:00 (= UTC 2026-02-28 23:00) の前日 = JST 2026-02-28
        var march1 = DateTimeOffset.FromUnixTimeSeconds(1_772_319_600);
        Assert.Equal("(lang:ja -filter:retweets" +
            " since:2026-02-27_15:00:00_UTC until:2026-02-28_15:00:00_UTC) min_faves:1",
            TrendingQuery.Build(TrendingTargetDay.Yesterday, 1, march1));

        // JST 2026-01-01 00:30 (= UTC 2025-12-31 15:30) の前日 = JST 2025-12-31。
        // UTC 暦では前年なので、JST 暦で切っていないと 1 日ずれる
        var newYear = DateTimeOffset.FromUnixTimeSeconds(1_767_195_000);
        Assert.Equal("(lang:ja -filter:retweets" +
            " since:2025-12-30_15:00:00_UTC until:2025-12-31_15:00:00_UTC) min_faves:1",
            TrendingQuery.Build(TrendingTargetDay.Yesterday, 1, newYear));
    }

    [Fact]
    public void 推奨閾値は当日だけ下げる()
    {
        // 当日は経過時間が短くいいねが伸びきっていない (§3.1 の実測)
        Assert.Equal(50_000, TrendingQuery.SuggestedMinFaves(TrendingTargetDay.Yesterday));
        Assert.Equal(10_000, TrendingQuery.SuggestedMinFaves(TrendingTargetDay.Today));
    }

    [Fact]
    public void 日付ラベルはJST暦()
    {
        Assert.Equal("2026-08-04", TrendingQuery.JstDateLabel(Now));
        Assert.Equal("2026-08-03",
            TrendingQuery.JstDateLabel(TrendingQuery.DateFor(TrendingTargetDay.Yesterday, Now)));
    }

    /// <summary>slug は日替わりのクエリから導出せず固定 (docs/trending-jp.md §7-3)。</summary>
    [Fact]
    public void バケットIDは固定() =>
        Assert.Equal("searches/trending-jp", TrendingQuery.BucketId);
}
