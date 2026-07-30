import Foundation
import SubakoCore

/// スクロール既読化のバッチ書込 (C# Services/ReadMarkQueue.cs の移植)。
/// 1 秒毎 or 100 件到達で read_state に 1 トランザクションで INSERT する。
/// 表示対象の切替時・アプリ終了時は flush() を呼ぶこと。
/// 閲覧専用モードでは何も書かない。
actor ReadMarkQueue {
    private static let flushThreshold = 100
    private static let flushInterval: Duration = .seconds(1)

    private let repo: TweetRepository
    private let readOnly: Bool
    private var pending: [String: String] = [:]   // tweet_id -> username
    private var loopTask: Task<Void, Never>?

    init(repo: TweetRepository, readOnly: Bool) {
        self.repo = repo
        self.readOnly = readOnly
    }

    func start() {
        guard loopTask == nil, !readOnly else { return }
        loopTask = Task {
            while !Task.isCancelled {
                try? await Task.sleep(for: Self.flushInterval)
                await flush()
            }
        }
    }

    func enqueue(tweetId: String, username: String) async {
        guard !readOnly else { return }
        pending[tweetId] = username
        if pending.count >= Self.flushThreshold {
            await flush()
        }
    }

    func flush() async {
        guard !pending.isEmpty else { return }
        let batch = pending
        pending = [:]
        do {
            try await repo.markRead(batch.map { (tweetId: $0.key, username: $0.value) })
        } catch {
            // 失敗分は再キュー (次のフラッシュで再試行)
            for (tweetId, username) in batch where pending[tweetId] == nil {
                pending[tweetId] = username
            }
            AppLog.error("既読書込に失敗 (再キュー): \(error)")
        }
    }

    func shutdown() async {
        loopTask?.cancel()
        loopTask = nil
        await flush()
    }
}
