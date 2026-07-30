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

    static func isVisible(_ frame: Frame, viewportHeight: CGFloat) -> Bool {
        let bottom = frame.top + frame.height
        let fullyVisible = frame.top >= 0 && bottom <= viewportHeight
        // 背の高いカードは完全表示になりえないので、下端が画面内へ入ったら既読扱い。
        // 上へ流れ去ったもの (bottom < 0) は対象外。
        let tallItemPassed = frame.height > viewportHeight
            && bottom <= viewportHeight && bottom >= 0
        return fullyVisible || tallItemPassed
    }
}
