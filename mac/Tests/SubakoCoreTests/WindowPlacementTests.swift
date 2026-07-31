import CoreGraphics
import Foundation
import Testing
@testable import SubakoCore

/// ウィンドウ配置の復元 (§2.2「復元時に画面外なら既定値へ」) とサイドバー幅 (§4)。
@Suite struct WindowPlacementTests {
    /// 内蔵ディスプレイ相当 (メニューバーぶんを引いた visibleFrame)
    private let builtIn = CGRect(x: 0, y: 0, width: 1710, height: 1041)
    /// 右側の外部ディスプレイ相当
    private let external = CGRect(x: 1710, y: 510, width: 1920, height: 1050)

    @Test func frameOnBuiltInIsKept() {
        let frame = CGRect(x: 120, y: 287, width: 1000, height: 700)
        #expect(
            WindowPlacement.onScreen(frame, screens: [builtIn, external], fallback: builtIn)
                == frame)
    }

    @Test func frameOnExternalIsKeptWhileConnected() {
        let frame = CGRect(x: 1865, y: 779, width: 1000, height: 700)
        #expect(
            WindowPlacement.onScreen(frame, screens: [builtIn, external], fallback: builtIn)
                == frame)
    }

    @Test func frameOnDisconnectedExternalComesBack() {
        // 外部ディスプレイに置いたまま、そのディスプレイを外して起動した状況
        let frame = CGRect(x: 1865, y: 779, width: 1000, height: 700)
        let placed = WindowPlacement.onScreen(frame, screens: [builtIn], fallback: builtIn)
        #expect(placed != frame)
        #expect(builtIn.contains(placed))
        #expect(placed.size == frame.size)
    }

    @Test func farOffScreenFrameComesBack() {
        let frame = CGRect(x: -5000, y: -5000, width: 900, height: 700)
        #expect(builtIn.contains(
            WindowPlacement.onScreen(frame, screens: [builtIn], fallback: builtIn)))
    }

    @Test func barelyOverlappingCornerComesBack() {
        let frame = CGRect(x: 1690, y: 1021, width: 1000, height: 700)
        let placed = WindowPlacement.onScreen(frame, screens: [builtIn], fallback: builtIn)
        #expect(placed != frame)
    }

    @Test func oversizedFrameIsShrunkToFit() {
        let frame = CGRect(x: 9000, y: 9000, width: 4000, height: 3000)
        let placed = WindowPlacement.onScreen(frame, screens: [builtIn], fallback: builtIn)
        #expect(placed.width == builtIn.width)
        #expect(placed.height == builtIn.height)
    }

    @Test func zeroSizedFrameFallsBack() {
        let placed = WindowPlacement.onScreen(.zero, screens: [builtIn], fallback: builtIn)
        #expect(placed.width > 0 && placed.height > 0)
    }

    @Test func noScreensFallsBack() {
        let frame = CGRect(x: 100, y: 100, width: 1000, height: 700)
        #expect(builtIn.contains(
            WindowPlacement.onScreen(frame, screens: [], fallback: builtIn)))
    }

    @Test(arguments: [
        (260.0, 260.0), (180.0, 180.0), (600.0, 600.0),
        (100.0, 180.0), (900.0, 600.0), (0.0, 180.0),
    ])
    func sidebarWidthIsClamped(input: Double, expected: Double) {
        #expect(WindowPlacement.clampSidebarWidth(input) == expected)
    }
}
