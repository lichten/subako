import CoreGraphics
import Foundation

/// 設定の永続化 (docs/viewer-features.md §2.2)。データフォルダの外 (マシンローカル) に置く。
/// 保存先: ~/Library/Application Support/Subako/settings.json
@MainActor
@Observable
final class AppSettings {
    struct Stored: Codable {
        var repoDir: String?
        var pythonPath: String?
        var dataDir: String?
        var sidebarWidth: Double?
        /// 前回終了時のウィンドウ配置 (§2.2)。Windows 版と同じ意味のキー
        var windowLeft: Double?
        var windowTop: Double?
        var windowWidth: Double?
        var windowHeight: Double?
        var unreadOnly: Bool?
        var ascending: Bool?
        var tagFilterId: Int64?
        var hasTagFilter: Bool?
        /// Mac 版独自: 閲覧専用モード (read_state を書かない — docs/mac-port-notes.md §3)
        var readOnlyMode: Bool?
        /// Mac 版独自: タイムライン画像の既定表示倍率 (ImageScale.rawValue)。
        /// Windows 版は常に等倍・非永続だが、設定はマシンローカルのため保存して良い
        /// (docs/mac-port-notes.md「非永続の状態」)
        var defaultImageScale: Double?
    }

    static let supportDir = FileManager.default.urls(
        for: .applicationSupportDirectory, in: .userDomainMask)[0]
        .appendingPathComponent("Subako").path

    private static var settingsPath: String { supportDir + "/settings.json" }

    var repoDir: String = ""
    var pythonPath: String = ""
    var dataDir: String = ""
    var sidebarWidth: Double = 260
    /// 前回終了時のウィンドウ矩形 (画面座標)。nil = 未保存で macOS 任せ
    var windowFrame: CGRect?
    var unreadOnly: Bool = false
    var ascending: Bool = false
    /// nil = すべて、-1 = (タグなし)、その他 = tag_id
    var tagFilterId: Int64?
    var hasTagFilter: Bool = false
    var readOnlyMode: Bool = false
    /// タイムライン画像の既定表示倍率 (1.0 = 等倍)
    var defaultImageScale: Double = 1.0

    /// DataDir が空なら RepoDir/data (Windows 版と同じ規則)。
    var effectiveDataDir: String {
        if !dataDir.isEmpty { return dataDir }
        if !repoDir.isEmpty { return repoDir + "/data" }
        return ""
    }

    var isConfigured: Bool { !effectiveDataDir.isEmpty }

    static let defaultDataDir = FileManager.default.urls(
        for: .documentDirectory, in: .userDomainMask)[0]
        .appendingPathComponent("Subako").path

    static func load() -> AppSettings {
        let settings = AppSettings()
        guard let data = FileManager.default.contents(atPath: settingsPath),
              let stored = try? JSONDecoder().decode(Stored.self, from: data)
        else {
            settings.detectRepoDir()
            return settings
        }
        settings.repoDir = stored.repoDir ?? ""
        settings.pythonPath = stored.pythonPath ?? ""
        settings.dataDir = stored.dataDir ?? ""
        settings.sidebarWidth = stored.sidebarWidth ?? 260
        if let left = stored.windowLeft, let top = stored.windowTop,
           let width = stored.windowWidth, let height = stored.windowHeight,
           width > 0, height > 0 {
            settings.windowFrame = CGRect(x: left, y: top, width: width, height: height)
        }
        settings.unreadOnly = stored.unreadOnly ?? false
        settings.ascending = stored.ascending ?? false
        settings.hasTagFilter = stored.hasTagFilter ?? false
        settings.tagFilterId = settings.hasTagFilter ? stored.tagFilterId : nil
        settings.readOnlyMode = stored.readOnlyMode ?? false
        settings.defaultImageScale = stored.defaultImageScale ?? 1.0
        if settings.repoDir.isEmpty {
            settings.detectRepoDir()
        }
        return settings
    }

    func save() {
        let stored = Stored(
            repoDir: repoDir.isEmpty ? nil : repoDir,
            pythonPath: pythonPath.isEmpty ? nil : pythonPath,
            dataDir: dataDir.isEmpty ? nil : dataDir,
            sidebarWidth: sidebarWidth,
            windowLeft: windowFrame.map { Double($0.origin.x) },
            windowTop: windowFrame.map { Double($0.origin.y) },
            windowWidth: windowFrame.map { Double($0.width) },
            windowHeight: windowFrame.map { Double($0.height) },
            unreadOnly: unreadOnly,
            ascending: ascending,
            tagFilterId: tagFilterId,
            hasTagFilter: tagFilterId != nil,
            readOnlyMode: readOnlyMode,
            defaultImageScale: defaultImageScale)
        do {
            try FileManager.default.createDirectory(
                atPath: Self.supportDir, withIntermediateDirectories: true)
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            try encoder.encode(stored).write(
                to: URL(fileURLWithPath: Self.settingsPath))
        } catch {
            AppLog.error("設定の保存に失敗: \(error)")
        }
    }

    /// リポジトリ clone 内で実行された場合の fetcher 自動検出
    /// (実行ファイルの祖先から main.py + sorsa_fetcher/ を探す)。
    private func detectRepoDir() {
        var dir = Bundle.main.bundlePath
        for _ in 0..<8 {
            if FileManager.default.fileExists(atPath: dir + "/main.py"),
               FileManager.default.fileExists(atPath: dir + "/sorsa_fetcher") {
                repoDir = dir
                return
            }
            let parent = (dir as NSString).deletingLastPathComponent
            if parent == dir { return }
            dir = parent
        }
    }
}
