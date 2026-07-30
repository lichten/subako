import AppKit
import SwiftUI
import SubakoCore

struct MediaViewerEntry {
    let localPath: String
    let text: String
    let dateText: String
    let statusURL: URL
}

/// 画像ビューア (§6.2): 非モーダルの別ウィンドウ (暗背景・原寸デコード)。
/// ライフサイクル制御のため NSWindow を自前管理する (SwiftUI Window では
/// 「◀▶ / Esc / 開いた時点の一覧固定」の制御が難しい)。
@MainActor
final class MediaViewerController {
    private var window: NSWindow?
    private var state = ViewerState()

    func show(entries: [MediaViewerEntry], index: Int) {
        state.entries = entries
        state.index = index
        if window == nil {
            let win = NSWindow(
                contentRect: NSRect(x: 0, y: 0, width: 900, height: 700),
                styleMask: [.titled, .closable, .resizable, .miniaturizable],
                backing: .buffered, defer: false)
            win.title = "画像ビューア"
            win.backgroundColor = .black
            win.isReleasedWhenClosed = false
            win.contentView = NSHostingView(
                rootView: MediaViewerView(state: state) { [weak self] in
                    self?.window?.close()
                })
            win.center()
            window = win
        }
        window?.makeKeyAndOrderFront(nil)
    }
}

@MainActor
@Observable
final class ViewerState {
    var entries: [MediaViewerEntry] = []
    var index = 0
}

struct MediaViewerView: View {
    @Bindable var state: ViewerState
    let onClose: () -> Void

    @Environment(\.openURL) private var openURL
    @State private var image: NSImage?

    private var entry: MediaViewerEntry? {
        state.entries.indices.contains(state.index) ? state.entries[state.index] : nil
    }

    var body: some View {
        VStack(spacing: 0) {
            ZStack {
                Color.black
                if let image {
                    Image(nsImage: image)
                        .resizable()
                        .aspectRatio(contentMode: .fit)
                }
                HStack {
                    navButton("chevron.backward", enabled: state.index > 0) {
                        state.index -= 1
                    }
                    Spacer()
                    navButton("chevron.forward", enabled: state.index < state.entries.count - 1) {
                        state.index += 1
                    }
                }
                .padding(12)
            }
            footer
        }
        .task(id: state.index) {
            image = nil
            guard let entry else { return }
            let path = entry.localPath
            // 原寸デコード (§6.2)
            image = await Task.detached(priority: .userInitiated) {
                ImageBox(NSImage(contentsOfFile: path))
            }.value.image
        }
        .onKeyPress(.leftArrow) {
            if state.index > 0 { state.index -= 1 }
            return .handled
        }
        .onKeyPress(.rightArrow) {
            if state.index < state.entries.count - 1 { state.index += 1 }
            return .handled
        }
        .onKeyPress(.escape) {
            onClose()
            return .handled
        }
        .focusable()
    }

    private func navButton(_ systemName: String, enabled: Bool, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: systemName)
                .font(.system(size: 22, weight: .bold))
                .foregroundStyle(.white.opacity(enabled ? 0.9 : 0.25))
                .padding(10)
                .background(.black.opacity(0.35))
                .clipShape(Circle())
        }
        .buttonStyle(.plain)
        .disabled(!enabled)
    }

    private var footer: some View {
        VStack(alignment: .leading, spacing: 6) {
            if let entry {
                Text(entry.text)
                    .font(.system(size: 12))
                    .foregroundStyle(.white.opacity(0.9))
                    .lineLimit(3)
                    .truncationMode(.tail)
                HStack {
                    Text("\(entry.dateText)   (\(state.index + 1)/\(state.entries.count))")
                        .font(.system(size: 11))
                        .foregroundStyle(.white.opacity(0.6))
                    Spacer()
                    // タイムライン側と同じ URL 規則 (§6.2 の既知課題を回避)
                    Button("ブラウザで開く") {
                        openURL(entry.statusURL)
                    }
                    Button("閉じる") {
                        onClose()
                    }
                }
            }
        }
        .padding(10)
        .background(Color.black.opacity(0.9))
    }
}
