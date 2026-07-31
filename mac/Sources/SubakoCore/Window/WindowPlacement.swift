import Foundation

/// ウィンドウまわりの永続化にかかわる計算 (viewer-features.md §2.2)。
///
/// 位置とサイズの保存・復元、および画面外からの復帰は macOS (SwiftUI の WindowGroup +
/// AppKit の frame autosave) が面倒を見るので、Mac 版で持つのはサイドバー幅だけ。
/// 詳細は docs/mac-port-notes.md §4.6 を参照。
public enum WindowPlacement {
    /// サイドバー幅の許容範囲 (§4: 既定 260、180〜600 で可変)。
    /// MainWindow の minWidth/maxWidth と一致させること。
    public static func clampSidebarWidth(_ width: Double) -> Double {
        min(max(width, 180), 600)
    }
}
