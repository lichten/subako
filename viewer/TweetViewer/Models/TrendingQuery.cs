using System.Globalization;
using TweetViewer.Data;
using TweetViewer.Services;

namespace TweetViewer.Models;

/// <summary>対象日の種別。既定値と推奨閾値がこれで決まる。</summary>
public enum TrendingTargetDay
{
    /// <summary>
    /// 前日 (JST) の丸一日。いいね数が 24 時間以上経って安定しており、
    /// 件数も順位も再現性がある (docs/trending-jp.md §3.1)。
    /// </summary>
    Yesterday,

    /// <summary>当日 (JST) の 0 時から現在まで。速報性はあるが取得時刻で件数が大きく変わる。</summary>
    Today,
}

/// <summary>
/// 「その日、日本語圏で話題のツイート」を取るための検索クエリの生成
/// (docs/trending-jp.md §1〜§4)。
///
/// <para>
/// <b>Swift 側 (mac/Sources/SubakoCore/Text/TrendingQuery.swift) と出力文字列を
/// 1 バイトも違えないこと</b> — 同じ日・同じ閾値なら両 OS から同じクエリが出て、
/// 同じバケットを相手のカーソルを壊さずに更新できる (docs/mac-port-notes.md §2 の共有契約)。
/// </para>
/// </summary>
public static class TrendingQuery
{
    /// <summary>
    /// 固定バケットの slug。クエリに日付が入るので SearchSlug.From(query) だと
    /// 毎日別バケットになってしまう。slug は不変 ID なので固定名でよい
    /// (docs/data-layer.md §1.5、docs/trending-jp.md §7-3)。
    /// </summary>
    public const string BucketSlug = "trending-jp";

    /// <summary>バケットの既定表示名。</summary>
    public const string DefaultName = "今日の話題 (日本)";

    /// <summary>並び順。X 検索の「話題」タブ相当 (docs/trending-jp.md §1)。</summary>
    public const string Order = "popular";

    /// <summary>バケット ID (users.username に入る値)。</summary>
    public const string BucketId = TweetUrl.SearchBucketPrefix + BucketSlug;

    /// <summary>
    /// JST。夏時間が無いので固定オフセットで扱う (Swift 側とタイムゾーン ID の
    /// 命名差 "Asia/Tokyo" / "Tokyo Standard Time" を跨がないため)。
    /// </summary>
    public static readonly TimeSpan JstOffset = TimeSpan.FromHours(9);

    /// <summary>実測に基づく min_faves の推奨値 (docs/trending-jp.md §3・§3.1)。
    /// 当日は経過時間が短くいいねが伸びきっていないため 1/5 に下げる。</summary>
    public static long SuggestedMinFaves(TrendingTargetDay day) =>
        day == TrendingTargetDay.Yesterday ? 50_000 : 10_000;

    /// <summary>対象日の JST 暦日を返す (now は実行時刻)。</summary>
    public static DateTimeOffset DateFor(TrendingTargetDay day, DateTimeOffset now) =>
        day == TrendingTargetDay.Today ? now : now.AddHours(-24);

    /// <summary>
    /// since: / until: に渡す UTC 文字列 (YYYY-MM-DD_HH:MM:SS_UTC)。
    /// JST 00:00 = 前日 15:00 UTC (docs/trending-jp.md §4)。
    /// </summary>
    public static string UtcTimestamp(DateTimeOffset instant) =>
        instant.ToUniversalTime().ToString(
            "yyyy-MM-dd'_'HH':'mm':'ss'_UTC'", CultureInfo.InvariantCulture);

    /// <summary>指定 JST 暦日の 00:00 (= その日の since 境界)。</summary>
    public static DateTimeOffset JstMidnight(DateTimeOffset instant)
    {
        var jst = instant.ToOffset(JstOffset);
        return new DateTimeOffset(jst.Year, jst.Month, jst.Day, 0, 0, 0, JstOffset);
    }

    /// <summary>
    /// 検索クエリを組む。until は day == Today のとき省略する (0 時から現在まで)。
    /// <para>
    /// 出力は SearchQueryOperators.Compose の正準形 — 編集ダイアログで
    /// Split → Compose しても文字列が変わらないため、fetcher のカーソルリセットが
    /// 起きない (docs/trending-jp.md §10.3-3)。
    /// </para>
    /// <code>(lang:ja -filter:retweets since:… until:…) min_faves:50000</code>
    /// </summary>
    public static string Build(TrendingTargetDay day, long minFaves, DateTimeOffset now)
    {
        var start = JstMidnight(DateFor(day, now));
        var operators = $"lang:ja -filter:retweets since:{UtcTimestamp(start)}";
        if (day == TrendingTargetDay.Yesterday)
            operators += $" until:{UtcTimestamp(start.AddHours(24))}";
        return SearchQueryOperators.Compose(operators, minRetweets: null, minFaves: minFaves);
    }

    /// <summary>バケットの表示ラベルに添える対象日 (yyyy-MM-dd、JST 暦)。</summary>
    public static string JstDateLabel(DateTimeOffset instant) =>
        instant.ToOffset(JstOffset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
