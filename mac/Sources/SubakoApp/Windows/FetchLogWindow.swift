import AppKit
import SwiftUI

/// 取得ログウィンドウ (§9.5): 非モーダル・等幅フォント・自動末尾追従。
/// 実行中に閉じる操作は「隠す」に読み替える。
@MainActor
final class FetchLogWindowController: NSObject, NSWindowDelegate {
    private var window: NSWindow?
    private weak var app: AppModel?

    func show(app: AppModel) {
        self.app = app
        if window == nil {
            let win = NSWindow(
                contentRect: NSRect(x: 0, y: 0, width: 720, height: 460),
                styleMask: [.titled, .closable, .resizable, .miniaturizable],
                backing: .buffered, defer: false)
            win.title = "取得ログ"
            win.isReleasedWhenClosed = false
            win.delegate = self
            win.contentView = NSHostingView(rootView: FetchLogView(app: app))
            win.center()
            window = win
        }
        window?.makeKeyAndOrderFront(nil)
    }

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        // 実行中は「隠す」(中断への導線と位置を保つ — §9.5)
        if app?.isFetching == true {
            sender.orderOut(nil)
            return false
        }
        return true
    }
}

struct FetchLogView: View {
    @Bindable var app: AppModel

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 8) {
                if app.isFetching {
                    ProgressView().controlSize(.small)
                    Text(app.fetchTargetLabel)
                        .font(.system(size: 12))
                } else {
                    Text("完了: \(app.fetchTargetLabel)")
                        .font(.system(size: 12))
                }
                Spacer()
                Button("中断") {
                    app.cancelFetch()
                }
                .disabled(!app.isFetching)
            }
            .padding(8)
            Divider()
            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 0) {
                        ForEach(Array(app.fetchLogLines.enumerated()), id: \.offset) { pair in
                            Text(pair.element)
                                .font(.system(size: 11, design: .monospaced))
                                .frame(maxWidth: .infinity, alignment: .leading)
                                .textSelection(.enabled)
                        }
                        Color.clear.frame(height: 1).id("bottom")
                    }
                    .padding(6)
                }
                .onChange(of: app.fetchLogLines.count) {
                    proxy.scrollTo("bottom", anchor: .bottom)
                }
            }
        }
    }
}
