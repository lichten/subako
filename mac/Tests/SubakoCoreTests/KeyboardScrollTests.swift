import Foundation
import Testing
@testable import SubakoCore

/// キーボードスクロールの移動量計算 (§5.7)。ScrollView 非依存の純ロジック。
/// C# KeyboardScrollBehaviorTests.cs からの移植。
@Suite struct KeyboardScrollTests {
    /// scrollableHeight = contentHeight - viewportHeight になるよう内容高さを逆算する
    private func offset(
        _ command: KeyboardScroll.Command, from offsetY: CGFloat,
        viewportHeight: CGFloat = 800, scrollableHeight: CGFloat
    ) -> CGFloat {
        KeyboardScroll.nextOffset(
            command, offsetY: offsetY, viewportHeight: viewportHeight,
            contentHeight: viewportHeight + scrollableHeight)
    }

    @Test func downMovesByOneLineStep() {
        #expect(offset(.lineDown, from: 0, scrollableHeight: 5000) == 48)
    }

    @Test func upAtTopClampsToZero() {
        #expect(offset(.lineUp, from: 0, scrollableHeight: 5000) == 0)
    }

    @Test func upNearTopClampsInsteadOfOvershooting() {
        #expect(offset(.lineUp, from: 20, scrollableHeight: 5000) == 0)
    }

    @Test func downAtBottomClampsToScrollableHeight() {
        #expect(offset(.lineDown, from: 4200, scrollableHeight: 4200) == 4200)
    }

    @Test func contentShorterThanViewportStaysAtZero() {
        // リセット直後や 1 画面に満たないリストではスクロール可能高さが 0
        #expect(offset(.lineDown, from: 0, scrollableHeight: 0) == 0)
    }

    @Test func negativeScrollableHeightStaysAtZero() {
        #expect(offset(.lineDown, from: 0, scrollableHeight: -1) == 0)
    }

    @Test func normalViewportLeavesOverlap() {
        // 800 - 48 = 752 (前の画面の下端が 48px ぶん残る)
        #expect(KeyboardScroll.pageDelta(viewportHeight: 800) == 752)
    }

    @Test func shortViewportFallsBackToFraction() {
        // 固定オーバーラップだと 252 だが、比率の下限 (0.875) が勝つ
        #expect(KeyboardScroll.pageDelta(viewportHeight: 300) == 262.5)
    }

    @Test func veryShortViewportStaysPositive() {
        // 下限が無いと 40 - 48 = -8 となり PageDown で逆走する
        #expect(KeyboardScroll.pageDelta(viewportHeight: 40) > 0)
    }

    @Test func zeroViewportIsZero() {
        // レイアウト前
        #expect(KeyboardScroll.pageDelta(viewportHeight: 0) == 0)
    }

    @Test func pageDownThenPageUpReturnsToStart() {
        let down = offset(.pageDown, from: 0, scrollableHeight: 5000)
        #expect(down == 752)
        let back = offset(.pageUp, from: down, scrollableHeight: 5000)
        #expect(back == 0)
    }

    // MARK: - top / bottom (Home / End / Command+↑ / Command+↓)

    @Test func topGoesToZeroFromAnywhere() {
        #expect(offset(.top, from: 3000, scrollableHeight: 5000) == 0)
    }

    @Test func bottomGoesToScrollableHeight() {
        #expect(offset(.bottom, from: 0, scrollableHeight: 5000) == 5000)
    }

    @Test func bottomOnShortContentStaysAtZero() {
        #expect(offset(.bottom, from: 0, scrollableHeight: 0) == 0)
    }

    @Test func scrollableHeightIsContentMinusViewport() {
        #expect(KeyboardScroll.scrollableHeight(contentHeight: 5800, viewportHeight: 800) == 5000)
        // 1 画面に満たないときは負にならない
        #expect(KeyboardScroll.scrollableHeight(contentHeight: 200, viewportHeight: 800) == 0)
    }
}
