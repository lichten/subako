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
        var unreadOnly: Bool?
        var ascending: Bool?
        var tagFilterId: Int64?
        var hasTagFilter: Bool?
        /// Mac 版独自: 閲覧専用モード (read_state を書かない — docs/mac-port-notes.md §3)
        var readOnlyMode: Bool?
    }

    static let supportDir = FileManager.default.urls(
        for: .applicationSupportDirectory, in: .userDomainMask)[0]
        .appendingPathComponent("Subako").path

    private static var settingsPath: String { supportDir + "/settings.json" }

    var repoDir: String = ""
    var pythonPath: String = ""
    var dataDir: String = ""
    var sidebarWidth: Double = 260
    var unreadOnly: Bool = false
    var ascending: Bool = false
    /// nil = すべて、-1 = (タグなし)、その他 = tag_id
    var tagFilterId: Int64?
    var hasTagFilter: Bool = false
    var readOnlyMode: Bool = false

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
        settings.unreadOnly = stored.unreadOnly ?? false
        settings.ascending = stored.ascending ?? false
        settings.hasTagFilter = stored.hasTagFilter ?? false
        settings.tagFilterId = settings.hasTagFilter ? stored.tagFilterId : nil
        settings.readOnlyMode = stored.readOnlyMode ?? false
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
            unreadOnly: unreadOnly,
            ascending: ascending,
            tagFilterId: tagFilterId,
            hasTagFilter: tagFilterId != nil,
            readOnlyMode: readOnlyMode)
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
