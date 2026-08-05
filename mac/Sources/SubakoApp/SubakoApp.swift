import AppKit
import SwiftUI
import SubakoCore

/// Subako macOS 版のエントリポイント。
@main
struct SubakoApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate

    @State private var settings: AppSettings
    @State private var app: AppModel

    init() {
        let settings = AppSettings.load()
        let model = AppModel(settings: settings)
        _settings = State(initialValue: settings)
        _app = State(initialValue: model)
        AppDelegate.sharedModel = model
    }

    var body: some Scene {
        WindowGroup("Subako") {
            MainWindow(app: app)
        }
        .windowResizability(.contentMinSize)
        // 保存値が無い初回だけ効く既定サイズ (Windows 版に合わせる)。
        // 前回終了時の位置・サイズの復元は SwiftUI/AppKit の frame autosave が
        // やってくれる (docs/mac-port-notes.md §4.6)
        .defaultSize(width: 1100, height: 800)
        .commands {
            // 標準の View メニュー (Show Tab Bar / Enter Full Screen) の
            // toolbar セクション直後に差し込む。CommandMenu で自前の「表示」を
            // 作ると View と二重になるため使わない
            CommandGroup(after: .toolbar) {
                Button("文字を大きく") { app.changeTimelineFontScale(.larger) }
                    .keyboardShortcut("+", modifiers: .command)
                    .disabled(TimelineFontScale.isLargest(CGFloat(app.settings.timelineFontScale)))
                Button("文字を小さく") { app.changeTimelineFontScale(.smaller) }
                    .keyboardShortcut("-", modifiers: .command)
                    .disabled(TimelineFontScale.isSmallest(CGFloat(app.settings.timelineFontScale)))
                Button("標準サイズ") { app.changeTimelineFontScale(.reset) }
                    .keyboardShortcut("0", modifiers: .command)
                    .disabled(CGFloat(app.settings.timelineFontScale) == TimelineFontScale.normal)
                Divider()
            }
        }
    }
}

/// 終了時の後始末: 既読キューのフラッシュ + WAL チェックポイント (§11.4)。
final class AppDelegate: NSObject, NSApplicationDelegate {
    @MainActor static var sharedModel: AppModel?

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        true
    }

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        guard let model = AppDelegate.sharedModel else { return .terminateNow }
        Task { @MainActor in
            await model.shutdown()
            NSApplication.shared.reply(toApplicationShouldTerminate: true)
        }
        return .terminateLater
    }
}
