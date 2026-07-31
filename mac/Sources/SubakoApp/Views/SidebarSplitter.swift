import AppKit
import SwiftUI
import SubakoCore

/// サイドバーと本文のあいだの分割線 (§3)。ドラッグで 180〜600 に可変 (§4)。
///
/// `HSplitView` を使わないのは、ウィンドウをリサイズするとサイドバーまで比例して
/// 広がってしまい、Windows 版 (幅は固定で本文側が伸縮) と挙動が変わるため。
/// 分割位置を指定する API も無く、ペインの `idealWidth` も尊重されない。
struct SidebarSplitter: View {
    @Binding var width: Double
    /// ドラッグが終わったとき (この値を終了時に保存する)
    let onCommit: () -> Void

    /// ドラッグ開始時の幅。ドラッグ中の累積で誤差が出ないよう起点を覚えておく
    @State private var dragStart: Double?

    var body: some View {
        Rectangle()
            .fill(Theme.border)
            .frame(width: 1)
            .overlay {
                // 罫線は 1px だが、掴める幅はもう少し広く取る
                Rectangle()
                    .fill(Color.clear)
                    .frame(width: 9)
                    .contentShape(Rectangle())
                    .onHover { inside in
                        if inside {
                            NSCursor.resizeLeftRight.push()
                        } else {
                            NSCursor.pop()
                        }
                    }
                    .gesture(
                        DragGesture(minimumDistance: 1)
                            .onChanged { value in
                                let start = dragStart ?? width
                                dragStart = start
                                width = WindowPlacement.clampSidebarWidth(
                                    start + value.translation.width)
                            }
                            .onEnded { _ in
                                dragStart = nil
                                onCommit()
                            })
            }
    }
}
