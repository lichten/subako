using TweetViewer.Behaviors;

namespace TweetViewer.Tests;

/// <summary>追加ロードの発火判定 (ScrollViewer 非依存の純ロジック)。</summary>
public class InfiniteScrollBehaviorTests
{
    [Fact]
    public void EmptyList_DoesNotLoadMore()
    {
        // 回帰テスト: リセット直後 (Clear と項目追加の間) は Extent が 0 で
        // 残り = 0 - (0 + viewport) が必ず閾値を下回るため、以前は必ず発火していた。
        // ここで発火するとリセット中に追加ロードが割り込み、表示位置が乱れる
        Assert.False(InfiniteScrollBehavior.ShouldLoadMore(
            extentHeight: 0, verticalOffset: 0, viewportHeight: 800));
    }

    [Fact]
    public void ZeroViewport_DoesNotLoadMore()
    {
        // レイアウト前 (ViewportHeight が未確定)
        Assert.False(InfiniteScrollBehavior.ShouldLoadMore(
            extentHeight: 5000, verticalOffset: 0, viewportHeight: 0));
    }

    [Fact]
    public void TopOfLongList_DoesNotLoadMore()
    {
        // 残り 4200 > 800*2
        Assert.False(InfiniteScrollBehavior.ShouldLoadMore(
            extentHeight: 5000, verticalOffset: 0, viewportHeight: 800));
    }

    [Fact]
    public void NearBottom_LoadsMore()
    {
        // 残り 1400 < 800*2
        Assert.True(InfiniteScrollBehavior.ShouldLoadMore(
            extentHeight: 5000, verticalOffset: 2800, viewportHeight: 800));
    }

    [Fact]
    public void AtBottom_LoadsMore()
    {
        Assert.True(InfiniteScrollBehavior.ShouldLoadMore(
            extentHeight: 5000, verticalOffset: 4200, viewportHeight: 800));
    }

    [Fact]
    public void ShortListThatDoesNotFillViewport_StillLoadsMore()
    {
        // 1画面に満たないが中身はある場合は、画面を埋めるために追加ロードしたい
        Assert.True(InfiniteScrollBehavior.ShouldLoadMore(
            extentHeight: 300, verticalOffset: 0, viewportHeight: 800));
    }
}
