import Foundation
import Testing
@testable import SubakoCore

/// 閲覧専用 ⇄ 書込モードの切替 (設定変更で同一プロセス内から開き直すケース)。
/// immutable=1 の接続 (ロックなし) と通常接続が同一プロセスで同居すると
/// SQLite の inode ロック状態が壊れ disk I/O error になるため、
/// 開き直す前に必ず古い接続を閉じること (AppModel.openAndInitialize)。
@Suite struct ModeSwitchTests {
    @Test func 閲覧専用から書込へ切り替えても読み書きできる() async throws {
        let dataDir = try makeTempDir()
        defer { try? FileManager.default.removeItem(atPath: dataDir) }

        // 準備: 書込モードで DB を作りツイートを 1 件入れて閉じる
        do {
            let setup = try TestDataDir(existingDir: dataDir)
            try await setup.importUser("alice", [tweetLine(1)])
            setup.db.checkpointAndClose()
        }

        // 閲覧専用で開く (アプリ起動直後の状態)
        let ro = try ViewerDatabase(dataDir: dataDir, readOnly: true)
        let roPage = try await TweetRepository(ro).getPage(
            usernames: ["alice"], unreadOnly: false, after: nil, limit: 10)
        #expect(roPage.rows.count == 1)

        // 設定変更 → 古い接続を閉じてから書込モードで開き直す
        ro.checkpointAndClose()
        let rw = try ViewerDatabase(dataDir: dataDir, readOnly: false)
        defer { rw.checkpointAndClose() }

        // 書込モードの読取・取込・書込が全て通ること
        let page = try await TweetRepository(rw).getPage(
            usernames: ["alice"], unreadOnly: false, after: nil, limit: 10)
        #expect(page.rows.count == 1)

        let t = TestDataDir(dataDir: dataDir, db: rw)
        try t.writeJsonl("alice", [tweetLine(2)])
        let result = try await JsonlImporter(rw).importUser("alice")
        #expect(result.newTweets == 1)
        try await TweetRepository(rw).setRead(tweetId: "1", username: "alice", read: true)
    }
}
