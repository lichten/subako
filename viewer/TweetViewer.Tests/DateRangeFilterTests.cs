using TweetViewer.Models;

namespace TweetViewer.Tests;

/// <summary>期間フィルタのローカル暦 → epoch 変換。タイムゾーン依存はここに隔離 (JST を固定注入)。</summary>
public sealed class DateRangeFilterTests
{
    private static readonly TimeZoneInfo Jst =
        TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");

    private static long Utc(int y, int mo, int d, int h = 0, int mi = 0) =>
        new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    [Fact]
    public void YearRangeStartsAtPreviousDay15UtcForJst()
    {
        var r = DateRangeFilter.FromLocalParts(2024, null, null, Jst);
        Assert.Equal(Utc(2023, 12, 31, 15), r.FromEpoch);
        Assert.Equal(Utc(2024, 12, 31, 15), r.ToEpochExclusive);
    }

    [Fact]
    public void MonthRangeHandlesLeapYear()
    {
        var leap = DateRangeFilter.FromLocalParts(2024, 2, null, Jst);
        Assert.Equal(29 * 86400L, leap.ToEpochExclusive - leap.FromEpoch);
        var normal = DateRangeFilter.FromLocalParts(2023, 2, null, Jst);
        Assert.Equal(28 * 86400L, normal.ToEpochExclusive - normal.FromEpoch);
    }

    [Fact]
    public void DayRangeIsOneDayFromLocalMidnight()
    {
        var r = DateRangeFilter.FromLocalParts(2024, 1, 1, Jst);
        Assert.Equal(Utc(2023, 12, 31, 15), r.FromEpoch);
        Assert.Equal(86400L, r.ToEpochExclusive - r.FromEpoch);
    }

    [Fact]
    public void ConsecutivePeriodsShareBoundary()
    {
        // 半開区間なので隣接期間の境界 epoch は一致し、隙間も重なりもない
        Assert.Equal(
            DateRangeFilter.FromLocalParts(2024, 1, 2, Jst).FromEpoch,
            DateRangeFilter.FromLocalParts(2024, 1, 1, Jst).ToEpochExclusive);
        Assert.Equal(
            DateRangeFilter.FromLocalParts(2024, 1, null, Jst).FromEpoch,
            DateRangeFilter.FromLocalParts(2023, 12, null, Jst).ToEpochExclusive);
    }

    [Fact]
    public void YearsCoveredUsesLocalCalendarYear()
    {
        // UTC では 2023-12-31 だが JST では 2024-01-01
        var epoch = Utc(2023, 12, 31, 15, 30);
        Assert.Equal(new[] { 2024 }, DateRangeFilter.YearsCovered(epoch, epoch, Jst));
    }

    [Fact]
    public void YearsCoveredEnumeratesContinuousRange()
    {
        Assert.Equal(new[] { 2023, 2024, 2025 },
            DateRangeFilter.YearsCovered(Utc(2023, 6, 1), Utc(2025, 6, 1), Jst));
    }
}
