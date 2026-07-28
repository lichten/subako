using TweetViewer.Behaviors;

namespace TweetViewer.Tests;

/// <summary>キーボードスクロールの移動量計算 (ScrollViewer 非依存の純ロジック)。</summary>
public class KeyboardScrollBehaviorTests
{
    [Fact]
    public void Down_MovesByOneLineStep()
    {
        Assert.Equal(48, KeyboardScrollBehavior.NextOffset(
            verticalOffset: 0, delta: KeyboardScrollBehavior.LineDelta, scrollableHeight: 5000));
    }

    [Fact]
    public void Up_AtTop_ClampsToZero()
    {
        Assert.Equal(0, KeyboardScrollBehavior.NextOffset(
            verticalOffset: 0, delta: -KeyboardScrollBehavior.LineDelta, scrollableHeight: 5000));
    }

    [Fact]
    public void Up_NearTop_ClampsInsteadOfOvershooting()
    {
        Assert.Equal(0, KeyboardScrollBehavior.NextOffset(
            verticalOffset: 20, delta: -KeyboardScrollBehavior.LineDelta, scrollableHeight: 5000));
    }

    [Fact]
    public void Down_AtBottom_ClampsToScrollableHeight()
    {
        Assert.Equal(4200, KeyboardScrollBehavior.NextOffset(
            verticalOffset: 4200, delta: KeyboardScrollBehavior.LineDelta, scrollableHeight: 4200));
    }

    [Fact]
    public void ContentShorterThanViewport_StaysAtZero()
    {
        // 回帰ガード: Math.Clamp は max < min で例外になる。
        // リセット直後や 1 画面に満たないリストでは ScrollableHeight が 0 になる
        Assert.Equal(0, KeyboardScrollBehavior.NextOffset(
            verticalOffset: 0, delta: KeyboardScrollBehavior.LineDelta, scrollableHeight: 0));
    }

    [Fact]
    public void NegativeScrollableHeight_StaysAtZero()
    {
        Assert.Equal(0, KeyboardScrollBehavior.NextOffset(
            verticalOffset: 0, delta: KeyboardScrollBehavior.LineDelta, scrollableHeight: -1));
    }

    [Fact]
    public void NormalViewport_LeavesOverlap()
    {
        // 800 - 48 = 752 (前の画面の下端が 48px ぶん残る)
        Assert.Equal(752, KeyboardScrollBehavior.PageDelta(800));
    }

    [Fact]
    public void ShortViewport_FallsBackToFraction()
    {
        // 固定オーバーラップだと 252 だが、比率の下限 (0.875) が勝ってオーバーラップ側が縮む
        Assert.Equal(262.5, KeyboardScrollBehavior.PageDelta(300));
    }

    [Fact]
    public void VeryShortViewport_StaysPositive()
    {
        // 下限が無いと 40 - 48 = -8 となり PageDown で逆走する
        Assert.True(KeyboardScrollBehavior.PageDelta(40) > 0);
    }

    [Fact]
    public void ZeroViewport_IsZero()
    {
        // レイアウト前
        Assert.Equal(0, KeyboardScrollBehavior.PageDelta(0));
    }

    [Fact]
    public void PageDownThenPageUp_ReturnsToStart()
    {
        var delta = KeyboardScrollBehavior.PageDelta(800);
        var down = KeyboardScrollBehavior.NextOffset(0, delta, scrollableHeight: 5000);
        var back = KeyboardScrollBehavior.NextOffset(down, -delta, scrollableHeight: 5000);

        Assert.Equal(0, back);
    }
}
