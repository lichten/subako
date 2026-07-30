import Foundation
import Testing
@testable import SubakoCore

/// スクロール自動既読の可視判定 (§8.1)。規則は C# Behaviors/ScrollReadBehavior.cs と同一。
@Suite struct ScrollReadRuleTests {
    private static let viewport: CGFloat = 800

    private func ids(_ frames: [ScrollReadRule.Frame]) -> [String] {
        ScrollReadRule.visibleIds(viewportHeight: Self.viewport, frames: frames)
    }

    @Test func fullyVisibleCardIsMarked() {
        #expect(ids([.init(id: "a", top: 100, height: 300)]) == ["a"])
    }

    @Test func cardTouchingBothEdgesExactlyIsMarked() {
        #expect(ids([.init(id: "a", top: 0, height: 800)]) == ["a"])
    }

    @Test func cardHangingBelowTheFoldIsNotMarked() {
        #expect(ids([.init(id: "a", top: 600, height: 300)]).isEmpty)
    }

    @Test func cardClippedAtTheTopIsNotMarked() {
        #expect(ids([.init(id: "a", top: -10, height: 300)]).isEmpty)
    }

    @Test func cardScrolledAwayAboveIsNotMarked() {
        #expect(ids([.init(id: "a", top: -900, height: 300)]).isEmpty)
    }

    @Test func tallCardIsMarkedOnceItsBottomEntersTheViewport() {
        // ビューポート (800) より背が高いカードは完全表示になりえない
        #expect(ids([.init(id: "a", top: -500, height: 1200)]) == ["a"])
    }

    @Test func tallCardStillBelowTheFoldIsNotMarked() {
        #expect(ids([.init(id: "a", top: -100, height: 1200)]).isEmpty)
    }

    @Test func tallCardScrolledCompletelyAwayIsNotMarked() {
        // bottom < 0 — 下端が画面上端より上。C# の bottom >= 0 に相当
        #expect(ids([.init(id: "a", top: -1300, height: 1200)]).isEmpty)
    }

    @Test func emptyViewportMarksNothing() {
        let frames = [ScrollReadRule.Frame(id: "a", top: 0, height: 100)]
        #expect(ScrollReadRule.visibleIds(viewportHeight: 0, frames: frames).isEmpty)
    }

    /// 回帰: 判定はビューポート相対の矩形だけで決まり、スクロール量に依存しない。
    /// (contentOffset を混ぜていた頃は、先頭を離れると一切既読が付かなくなっていた)
    @Test func resultDoesNotDependOnScrollOffset() {
        let frames = [
            ScrollReadRule.Frame(id: "above", top: -400, height: 300),
            ScrollReadRule.Frame(id: "visible", top: 50, height: 400),
            ScrollReadRule.Frame(id: "below", top: 700, height: 400),
        ]
        // 同じ矩形群なら、リストの先頭にいようが 1 万 px スクロール済みだろうが同じ結果
        #expect(ids(frames) == ["visible"])
    }

    @Test func mixedFramesKeepInputOrder() {
        let frames = [
            ScrollReadRule.Frame(id: "a", top: 0, height: 100),
            ScrollReadRule.Frame(id: "b", top: 900, height: 100),
            ScrollReadRule.Frame(id: "c", top: 200, height: 100),
        ]
        #expect(ids(frames) == ["a", "c"])
    }
}
