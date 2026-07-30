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
