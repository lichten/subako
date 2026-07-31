import AppKit
import SwiftUI
import SubakoCore

/// 前回終了時のウィンドウ配置を復元し、現在の配置を覚えておく (§2.2)。
///
/// SwiftUI の `WindowGroup` 任せにできない: サイズは復元されるが、位置は
/// 起動元アプリのある画面 (`NSScreen.main`) へ移し替えられてしまう。
/// しかも移し替えた先の位置がそのまま保存されるため、マルチディスプレイでは
/// 一度ずれると二度と戻らない (実測)。そこで SwiftUI が配置した**あとに**
/// 保存済みの矩形を明示的に適用する。
///
/// 純 SwiftUI からウィンドウには触れないので NSViewRepresentable で橋渡しする。
struct WindowFrameKeeper: NSViewRepresentable {
    /// 前回終了時の矩形 (nil なら復元しない = macOS の既定配置に任せる)
    let saved: CGRect?
    /// 移動・リサイズのたびに現在の矩形を伝える (終了時に保存する)
    let onChange: (CGRect) -> Void

    func makeNSView(context: Context) -> NSView {
        KeeperView(saved: saved, onChange: onChange)
    }

    func updateNSView(_ nsView: NSView, context: Context) {}

    private final class KeeperView: NSView {
        private let saved: CGRect?
        private let onChange: (CGRect) -> Void
        private var restored = false

        init(saved: CGRect?, onChange: @escaping (CGRect) -> Void) {
            self.saved = saved
            self.onChange = onChange
            super.init(frame: .zero)
        }

        @available(*, unavailable)
        required init?(coder: NSCoder) { fatalError("not supported") }

        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            guard let window, !restored else { return }
            restored = true
            // SwiftUI が付けた AppKit の frame autosave を切る。
            // 放っておくと復元機構が二重になり、こちらが画面内へ直した矩形を
            // あとから autosave 側の値 (画面外・別ディスプレイのまま) で
            // 上書きされることがある。どちらが勝つかはタイミング次第で不安定
            window.setFrameAutosaveName("")
            // SwiftUI がウィンドウを配置し終えるのを待ってから上書きする
            DispatchQueue.main.async { [weak self, weak window] in
                guard let self, let window else { return }
                self.restore(window)
                self.observe(window)
            }
        }

        private func restore(_ window: NSWindow) {
            guard let saved else { return }
            // visibleFrame はキャッシュせず毎回取得すること (ユーザー設定で変わる)
            let screens = NSScreen.screens.map(\.visibleFrame)
            guard let fallback = NSScreen.main?.visibleFrame ?? screens.first else { return }
            let placed = WindowPlacement.onScreen(saved, screens: screens, fallback: fallback)
            window.setFrame(placed, display: true)
            // 位置がおかしいという報告のときに、保存値・補正・画面構成が一度に分かるようにする
            AppLog.info(
                "ウィンドウ復元: 保存=\(saved) 適用=\(window.frame) 画面=\(screens)")
        }

        private func observe(_ window: NSWindow) {
            let center = NotificationCenter.default
            for name in [NSWindow.didMoveNotification, NSWindow.didResizeNotification] {
                center.addObserver(
                    self, selector: #selector(windowFrameChanged(_:)),
                    name: name, object: window)
            }
            onChange(window.frame)
        }

        @objc private func windowFrameChanged(_ notification: Notification) {
            guard let window = notification.object as? NSWindow else { return }
            onChange(window.frame)
        }
    }
}
