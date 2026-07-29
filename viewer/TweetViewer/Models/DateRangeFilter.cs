namespace TweetViewer.Models;

/// <summary>sort_key (epoch 秒) に対する半開区間 [FromEpoch, ToEpochExclusive)。</summary>
public readonly record struct DateRangeFilter(long FromEpoch, long ToEpochExclusive)
{
    /// <summary>
    /// ローカル暦の (年 / 年+月 / 年+月+日) を epoch 秒の半開区間へ変換する。
    /// 保存は UTC・表示はローカルのため、境界をローカル 0 時で切らないと
    /// 月初 0〜9 時 (JST) のツイートが前の期間に漏れる。
    /// tz はテストで固定注入するための引数 (実行時は TimeZoneInfo.Local)。
    /// </summary>
    public static DateRangeFilter FromLocalParts(int year, int? month, int? day, TimeZoneInfo tz)
    {
        var from = new DateTime(year, month ?? 1, day ?? 1, 0, 0, 0, DateTimeKind.Unspecified);
        var to = day.HasValue ? from.AddDays(1)
            : month.HasValue ? from.AddMonths(1)
            : from.AddYears(1);
        return new DateRangeFilter(ToEpoch(from, tz), ToEpoch(to, tz));
    }

    /// <summary>MIN/MAX sort_key がローカル暦で被覆する年の昇順リスト (年 ComboBox 用)。</summary>
    public static IReadOnlyList<int> YearsCovered(long minEpoch, long maxEpoch, TimeZoneInfo tz)
    {
        var minYear = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(minEpoch), tz).Year;
        var maxYear = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(maxEpoch), tz).Year;
        return maxYear < minYear
            ? []
            : Enumerable.Range(minYear, maxYear - minYear + 1).ToList();
    }

    private static long ToEpoch(DateTime local, TimeZoneInfo tz) =>
        new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, tz), TimeSpan.Zero)
            .ToUnixTimeSeconds();
}
