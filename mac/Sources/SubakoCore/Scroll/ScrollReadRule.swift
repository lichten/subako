import Foundation

/// スクロール自動既読の可視判定 (viewer-features.md §8.1)。
/// C# Behaviors/ScrollReadBehavior.cs と同一規則 —
/// ビューポート内に完全表示されているカード、およびビューポートより背が高く
/// 下端が通過したカードを既読にする。
///
/// 座標はすべて**ビューポート相対** (可視領域の上端が 0、上へ流れると負)。
/// SwiftUI の `proxy.frame(in: .scrollView)` と WPF の
/// `TransformToAncestor(scrollViewer)` がこの座標系にあたる。
/// スクロール量 (contentOffset) を混ぜてはいけない。
///
/// 規則 (§8.1) は C# と同一だが、Mac 側だけ境界比較に ±1pt の許容誤差を持つ。
/// SwiftUI は Retina (2x) で 0.5pt 刻みの端数座標を生むため、厳密比較だと
/// 最下端ぴったりのカードを取りこぼす (WPF はレイアウト丸めで整数座標のため不要)。
public enum ScrollReadRule {
    /// 判定対象のカード 1 枚。
    public struct Frame: Sendable, Equatable {
        public let id: String
        /// カード上端の位置 (ビューポート上端が 0)。
        public let top: CGFloat
        public let height: CGFloat

        public init(id: String, top: CGFloat, height: CGFloat) {
            self.id = id
            self.top = top
            self.height = height
        }
    }

    /// 既読にすべきカードの id。順序は入力順。
    public static func visibleIds(viewportHeight: CGFloat, frames: [Frame]) -> [String] {
        guard viewportHeight > 0 else { return [] }
        return frames.filter { isVisible($0, viewportHeight: viewportHeight) }.map(\.id)
    }

    /// 境界比較の許容誤差 (pt)。端数の吸収には十分大きく、「一部が隠れている」と
    /// 視認できる量よりは十分小さい。高さの分類 (tall かどうか) には適用しない
    static let tolerance: CGFloat = 1

    static func isVisible(_ frame: Frame, viewportHeight: CGFloat) -> Bool {
        let bottom = frame.top + frame.height
        let fullyVisible = frame.top >= -tolerance && bottom <= viewportHeight + tolerance
        // 背の高いカードは完全表示になりえないので、下端が画面内へ入ったら既読扱い。
        // 上へ流れ去ったもの (bottom < -tolerance) は対象外。
        let tallItemPassed = frame.height > viewportHeight
            && bottom <= viewportHeight + tolerance && bottom >= -tolerance
        return fullyVisible || tallItemPassed
    }
}
