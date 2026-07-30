import Foundation

/// 日次ファイルログ (docs/viewer-features.md §11.4)。
/// ~/Library/Logs/Subako/yyyyMMdd.log、直近 7 個を保持。マシンローカル。
enum AppLog {
    nonisolated(unsafe) private static let queue = DispatchQueue(label: "subako.applog")

    private static let logsDir = FileManager.default.urls(
        for: .libraryDirectory, in: .userDomainMask)[0]
        .appendingPathComponent("Logs/Subako").path

    static func info(_ message: String) { write("INFO", message) }
    static func error(_ message: String) { write("ERROR", message) }

    private static func write(_ level: String, _ message: String) {
        let now = Date()
        queue.async {
            let day = DateFormatter()
            day.locale = Locale(identifier: "en_US_POSIX")
            day.dateFormat = "yyyyMMdd"
            let time = DateFormatter()
            time.locale = Locale(identifier: "en_US_POSIX")
            time.dateFormat = "HH:mm:ss.SSS"
            let path = logsDir + "/\(day.string(from: now)).log"
            let line = "\(time.string(from: now)) [\(level)] \(message)\n"
            do {
                try FileManager.default.createDirectory(
                    atPath: logsDir, withIntermediateDirectories: true)
                if let handle = FileHandle(forWritingAtPath: path) {
                    defer { try? handle.close() }
                    try handle.seekToEnd()
                    try handle.write(contentsOf: Data(line.utf8))
                } else {
                    try Data(line.utf8).write(to: URL(fileURLWithPath: path))
                    pruneOldLogs()
                }
            } catch {
                // ログ書込失敗は握りつぶす (アプリ動作を止めない)
            }
        }
    }

    /// 日次ローテーション: 直近 7 ファイルより古いものを削除。
    private static func pruneOldLogs() {
        guard let entries = try? FileManager.default.contentsOfDirectory(atPath: logsDir) else {
            return
        }
        let logs = entries.filter { $0.hasSuffix(".log") }.sorted()
        for old in logs.dropLast(7) {
            try? FileManager.default.removeItem(atPath: logsDir + "/" + old)
        }
    }

    static func logsDirectory() -> String { logsDir }
}
