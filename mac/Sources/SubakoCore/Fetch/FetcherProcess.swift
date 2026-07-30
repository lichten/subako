import Foundation

/// data/_trash/ への退避 (C# Services/ArchiveTrash.cs の移植、docs/data-layer.md §1.6)。
public enum ArchiveTrash {
    public static let trashDirName = "_trash"

    public static func trashDir(dataDir: String) -> String {
        (dataDir as NSString).appendingPathComponent(trashDirName)
    }

    /// data/<username>/ を data/_trash/<username>/ へ移動し、移動先パスを返す。
    /// 元フォルダが無い場合は nil。戻せるよう通常はサフィックスを付けず、
    /// 同名が既にある場合だけ日時を付ける。
    @discardableResult
    public static func moveToTrash(dataDir: String, username: String) throws -> String? {
        let fm = FileManager.default
        let source = (dataDir as NSString).appendingPathComponent(username)
        guard fm.fileExists(atPath: source) else { return nil }
        var dest = (trashDir(dataDir: dataDir) as NSString).appendingPathComponent(username)
        if fm.fileExists(atPath: dest) {
            let formatter = DateFormatter()
            formatter.locale = Locale(identifier: "en_US_POSIX")
            formatter.dateFormat = "yyyyMMdd_HHmmss"
            dest = (trashDir(dataDir: dataDir) as NSString)
                .appendingPathComponent("\(username)_\(formatter.string(from: Date()))")
        }
        try fm.createDirectory(
            atPath: (dest as NSString).deletingLastPathComponent,
            withIntermediateDirectories: true)
        try fm.moveItem(atPath: source, toPath: dest)
        return dest
    }
}

public enum FetcherProcessError: Error, LocalizedError {
    case pythonNotFound(String)

    public var errorDescription: String? {
        switch self {
        case .pythonNotFound(let path):
            return "Python を起動できませんでした (\(path))。\n"
                + "取得機能には Python 3 が必要です (macOS には python3 が同梱されています)。"
                + "起動しない場合は「設定」で Python パスを指定してください。"
        }
    }
}

/// Python fetcher (main.py) をサブプロセス実行する
/// (C# Services/FetchProcessService.cs の移植、契約は docs/fetcher-cli.md)。
/// ログは stderr (logging) に出るため両ストリームを捕捉する。
public final class FetcherProcess: @unchecked Sendable {
    private let repoDir: String
    private let pythonPath: String?
    private let lock = NSLock()
    private var current: Process?

    public init(repoDir: String, pythonPath: String?) {
        self.repoDir = repoDir
        self.pythonPath = pythonPath
    }

    private func setCurrent(_ process: Process?) {
        lock.withLock { current = process }
    }

    /// 実行中プロセスをプロセスグループごと停止する (fetcher-cli.md §1 — kill で安全)。
    public func cancel() {
        let process = lock.withLock { current }
        guard let process, process.isRunning else { return }
        let pid = process.processIdentifier
        // 子プロセスグループ宛に SIGTERM → 猶予後 SIGKILL
        killpg(getpgid(pid), SIGTERM)
        DispatchQueue.global().asyncAfter(deadline: .now() + 2) {
            if process.isRunning {
                killpg(getpgid(pid), SIGKILL)
            }
        }
    }

    public func run(
        username: String, mode: FetchMode, dataDir: String, maxRequests: Int?,
        searchQuery: String? = nil, backfillSince: String? = nil,
        log: @escaping @Sendable (String) -> Void
    ) async throws -> FetchResult {
        let python = resolvePython()
        let args = FetchArguments.build(
            username: username, mode: mode, dataDir: dataDir,
            maxRequests: maxRequests, searchQuery: searchQuery, backfillSince: backfillSince)

        let process = Process()
        process.executableURL = URL(fileURLWithPath: python)
        process.arguments = args
        // 作業ディレクトリは fetcher のフォルダ (.env の解決のため — fetcher-cli.md §1)
        process.currentDirectoryURL = URL(fileURLWithPath: repoDir)
        var env = ProcessInfo.processInfo.environment
        env["PYTHONIOENCODING"] = "utf-8"
        env["PYTHONUTF8"] = "1"
        process.environment = env

        let stdout = Pipe()
        let stderr = Pipe()
        process.standardOutput = stdout
        process.standardError = stderr

        let lineBuffers = LineBuffers(log: log)
        stdout.fileHandleForReading.readabilityHandler = { handle in
            lineBuffers.feed(stream: 0, data: handle.availableData)
        }
        stderr.fileHandleForReading.readabilityHandler = { handle in
            lineBuffers.feed(stream: 1, data: handle.availableData)
        }

        log("> \(python) \(args.joined(separator: " "))")
        do {
            try process.run()
        } catch {
            throw FetcherProcessError.pythonNotFound(python)
        }
        // kill 時に子孫まで止められるよう、fetcher を自プロセスグループのリーダーにする
        setpgid(process.processIdentifier, process.processIdentifier)

        setCurrent(process)
        defer { setCurrent(nil) }

        let cancelled = await withCheckedContinuation { (cont: CheckedContinuation<Bool, Never>) in
            process.terminationHandler = { p in
                // SIGTERM / SIGKILL による終了は中断扱い
                let killed = p.terminationReason == .uncaughtSignal
                cont.resume(returning: killed)
            }
        }
        // 残りの出力を flush
        stdout.fileHandleForReading.readabilityHandler = nil
        stderr.fileHandleForReading.readabilityHandler = nil
        lineBuffers.feed(stream: 0, data: (try? stdout.fileHandleForReading.readToEnd()) ?? Data())
        lineBuffers.feed(stream: 1, data: (try? stderr.fileHandleForReading.readToEnd()) ?? Data())
        lineBuffers.flushRemainder()

        return FetchResult(exitCode: process.terminationStatus, cancelled: cancelled)
    }

    private func resolvePython() -> String {
        if let pythonPath, !pythonPath.trimmingCharacters(in: .whitespaces).isEmpty {
            return pythonPath
        }
        // PATH の python3 を探す。無ければ macOS 同梱の /usr/bin/python3
        let path = ProcessInfo.processInfo.environment["PATH"] ?? ""
        for dir in path.split(separator: ":") {
            let candidate = String(dir) + "/python3"
            if FileManager.default.isExecutableFile(atPath: candidate) {
                return candidate
            }
        }
        return "/usr/bin/python3"
    }

    /// stdout/stderr の断片を行単位に組み立てる (行の途中で切れた断片を保持)。
    private final class LineBuffers: @unchecked Sendable {
        private let log: @Sendable (String) -> Void
        private var buffers = [Data(), Data()]
        private let lock = NSLock()

        init(log: @escaping @Sendable (String) -> Void) {
            self.log = log
        }

        func feed(stream: Int, data: Data) {
            guard !data.isEmpty else { return }
            lock.lock()
            buffers[stream].append(data)
            var lines: [String] = []
            while let nl = buffers[stream].firstIndex(of: UInt8(ascii: "\n")) {
                let lineData = buffers[stream][buffers[stream].startIndex..<nl]
                buffers[stream] = Data(buffers[stream][(nl + 1)...])
                var line = String(decoding: lineData, as: UTF8.self)
                while line.hasSuffix("\r") { line.removeLast() }
                lines.append(line)
            }
            lock.unlock()
            for line in lines {
                log(line)
            }
        }

        func flushRemainder() {
            lock.lock()
            let rest = buffers.map { String(decoding: $0, as: UTF8.self) }
            buffers = [Data(), Data()]
            lock.unlock()
            for line in rest where !line.isEmpty {
                log(line)
            }
        }
    }
}
