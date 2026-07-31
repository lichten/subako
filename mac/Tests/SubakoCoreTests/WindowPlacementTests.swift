import Foundation
import Testing
@testable import SubakoCore

/// サイドバー幅の永続化 (§2.2 の SidebarWidth、§4 の 180〜600)。
@Suite struct WindowPlacementTests {
    @Test(arguments: [
        (260.0, 260.0), (180.0, 180.0), (600.0, 600.0),
        (100.0, 180.0), (900.0, 600.0), (0.0, 180.0),
    ])
    func sidebarWidthIsClamped(input: Double, expected: Double) {
        #expect(WindowPlacement.clampSidebarWidth(input) == expected)
    }
}
