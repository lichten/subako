import Foundation

/// キーボードスクロールの移動量計算 (viewer-features.md §5.7)。
/// C# Behaviors/KeyboardScrollBehavior.cs と同一規則。
///
/// 目標オフセットを溜め込まない (状態を持たない) のが重要 —
/// 溜めると表示切替時の先頭復帰と綱引きになり、遅延ロードで内容高さが
/// 伸びるたびに値が陳腐化する。毎回その時点の位置から計算する。
public enum KeyboardScroll {
    /// 矢印キー 1 回の移動量 (px)。WebKit / Chromium の 1 行 40px とほぼ同等。
    public static let lineDelta: CGFloat = 48

    /// PageUp/PageDown で前の画面を数行ぶん残すためのオーバーラップ (px)。
    private static let pageOverlap: CGFloat = lineDelta

    /// 1 画面あたりの最低移動比。表示域が低い環境で固定オーバーラップが
    /// 移動量を食い潰して 0 以下になる (= 逆方向に進む) のを防ぐ。
    /// Chromium の kMinFractionToStepWhenPaging と同値。
    private static let minPageFraction: CGFloat = 0.875

    public enum Command: Sendable {
        case lineUp, lineDown
        case pageUp, pageDown
        case top, bottom
    }

    /// PageUp/PageDown の移動量。1 画面から数行ぶん差し引いて前画面の続きを残す。
    public static func pageDelta(viewportHeight: CGFloat) -> CGFloat {
        guard viewportHeight > 0 else { return 0 }   // レイアウト前
        return max(viewportHeight * minPageFraction, viewportHeight - pageOverlap)
    }

    /// スクロール可能な高さ。1 画面に満たなければ 0。
    public static func scrollableHeight(
        contentHeight: CGFloat, viewportHeight: CGFloat
    ) -> CGFloat {
        max(0, contentHeight - viewportHeight)
    }

    /// command 実行後の垂直オフセット。[0, スクロール可能高さ] に収める。
    public static func nextOffset(
        _ command: Command, offsetY: CGFloat,
        viewportHeight: CGFloat, contentHeight: CGFloat
    ) -> CGFloat {
        let scrollable = scrollableHeight(
            contentHeight: contentHeight, viewportHeight: viewportHeight)
        switch command {
        case .top:
            return 0
        case .bottom:
            return scrollable
        case .lineUp:
            return clamp(offsetY - lineDelta, scrollableHeight: scrollable)
        case .lineDown:
            return clamp(offsetY + lineDelta, scrollableHeight: scrollable)
        case .pageUp:
            return clamp(
                offsetY - pageDelta(viewportHeight: viewportHeight),
                scrollableHeight: scrollable)
        case .pageDown:
            return clamp(
                offsetY + pageDelta(viewportHeight: viewportHeight),
                scrollableHeight: scrollable)
        }
    }

    /// スクロール可能高さが 0 以下 (リセット直後・1 画面に満たない) なら 0。
    static func clamp(_ offset: CGFloat, scrollableHeight: CGFloat) -> CGFloat {
        guard scrollableHeight > 0 else { return 0 }
        return min(max(offset, 0), scrollableHeight)
    }
}
