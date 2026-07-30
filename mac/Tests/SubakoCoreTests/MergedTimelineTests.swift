import Foundation
import Testing
@testable import SubakoCore

/// 統合タイムライン用の複数アーカイブページング (C# MergedTimelineTests.cs の移植)。
@Suite struct MergedTimelineTests {
    private let bucket = "searches/kw-x"

    @Test func duplicateTweetAppearsOnceWithUserArchiveAsRepresentative() async throws {
        let t = try TestDataDir()
        defer { t.cleanup() }
        let shared = tweetLine(1, date: isoDate(1))
        try await t.importUser("alice", [shared])
        try await t.importUser(bucket, [shared])

        let page = try await TweetRepository(t.db).getPage(
            usernames: ["alice", bucket], unreadOnly: false, after: nil, limit: 10)

        #expect(page.rows.count == 1)
        #expect(page.rows.first?.tweetId == "1")
        #expect(page.rows.first?.username == "alice")   // 実ユーザーアーカイブが代表
    }

    @Test func bucketOnlyTweetKeepsBucketUsername() async throws {
        let t = try TestDataDir()
        defer { t.cleanup() }
        try await t.importUser("alice", [tweetLine(1, date: isoDate(10))])
        try await t.importUser(bucket, [tweetLine(2, date: isoDate(11))])

        let page = try await TweetRepository(t.db).getPage(
            usernames: ["alice", bucket], unreadOnly: false, after: nil, limit: 10)

        #expect(page.rows.count == 2)
        let bucketRow = try #require(page.rows.first { $0.tweetId == "2" })
        #expect(bucketRow.username == bucket)
        // author 列で表示が成立する (統合時のヘッダ用)
        #expect(bucketRow.authorUsername == "author2")
    }

    @Test func archivesInterleaveInDescendingTimeOrder() async throws {
        let t = try TestDataDir()
        defer { t.cleanup() }
        try await t.importUser("alice", [tweetLine(1, date: isoDate(1)), tweetLine(3, date: isoDate(3))])
        try await t.importUser("bob", [tweetLine(2, date: isoDate(2)), tweetLine(4, date: isoDate(4))])

        let page = try await TweetRepository(t.db).getPage(
            usernames: ["alice", "bob"], unreadOnly: false, after: nil, limit: 10)

        #expect(page.rows.map(\.tweetId) == ["4", "3", "2", "1"])
    }

    @Test func keysetPagesConcatenateWithoutGapsOrDuplicates() async throws {
        let t = try TestDataDir()
        defer { t.cleanup() }
        // 重複ツイートがページ境界を跨ぐ構成
        try await t.importUser("alice", [
            tweetLine(1, date: isoDate(1)), tweetLine(3, date: isoDate(3)), tweetLine(5, date: isoDate(5)),
        ])
        try await t.importUser(bucket, [
            tweetLine(2, date: isoDate(2)), tweetLine(3, date: isoDate(3)),
            tweetLine(4, date: isoDate(4)), tweetLine(5, date: isoDate(5)),
        ])

        let repo = TweetRepository(t.db)
        var collected: [String] = []
        var cursor: TweetCursor?
        for _ in 0..<10 {
            let page = try await repo.getPage(
                usernames: ["alice", bucket], unreadOnly: false, after: cursor, limit: 2)
            collected.append(contentsOf: page.rows.map(\.tweetId))
            if page.rows.count < 2 { break }
            let last = page.rows.last!
            cursor = TweetCursor(sortKey: last.sortKey, idInt: last.idInt)
        }

        #expect(collected == ["5", "4", "3", "2", "1"])
    }

    @Test func limitAppliesAfterDeduplication() async throws {
        let t = try TestDataDir()
        defer { t.cleanup() }
        let lines = (1...4).map { tweetLine(Int64($0), date: isoDate($0)) }
        try await t.importUser("alice", lines)
        try await t.importUser(bucket, lines)   // 物理 8 行

        let page = try await TweetRepository(t.db).getPage(
            usernames: ["alice", bucket], unreadOnly: false, after: nil, limit: 4)

        // dedup が LIMIT より前なら 4 件全部返る (後だと 2 件になる)
        #expect(page.rows.count == 4)
        #expect(Set(page.rows.map(\.tweetId)).count == 4)
    }

    @Test func unreadOnlyDropsBothCopiesOfReadTweet() async throws {
        let t = try TestDataDir()
        defer { t.cleanup() }
        try await t.importUser("alice", [tweetLine(1, date: isoDate(1)), tweetLine(2, date: isoDate(2))])
        try await t.importUser(bucket, [tweetLine(1, date: isoDate(1))])

        let repo = TweetRepository(t.db)
        try await repo.setRead(tweetId: "1", username: "alice", read: true)

        let page = try await repo.getPage(
            usernames: ["alice", bucket], unreadOnly: true, after: nil, limit: 10)
        #expect(page.rows.map(\.tweetId) == ["2"])
    }

    @Test func archivesByTweetIdListsOnlyDuplicatesIncludingArchivesOutsideScope() async throws {
        let t = try TestDataDir()
        defer { t.cleanup() }
        try await t.importUser("alice", [tweetLine(1, date: isoDate(1)), tweetLine(2, date: isoDate(2))])
        try await t.importUser(bucket, [tweetLine(1, date: isoDate(1))])
        // スコープ外のアーカイブにも同じツイートがある (既読化で未読数が実際に減るのはここも)
        try await t.importUser("carol", [tweetLine(1, date: isoDate(1))])

        let page = try await TweetRepository(t.db).getPage(
            usernames: ["alice", bucket], unreadOnly: false, after: nil, limit: 10)

        #expect(page.archivesByTweetId.count == 1)   // 重複している "1" だけ載る
        #expect(page.archivesByTweetId["1"]?.sorted() == ["alice", "carol", bucket].sorted())
    }

    @Test func mediaPageDeduplicatesPerImageAndCarriesRowUsername() async throws {
        let t = try TestDataDir()
        defer { t.cleanup() }
        // 2 枚画像のツイートを両アーカイブに、1 枚画像をバケットのみに
        let two = #"{"id":"2","created_at":"\#(isoDate(2))","full_text":"two","user":{"username":"alice"},"entities":[{"type":"photo","link":"https://pbs.twimg.com/media/A1.jpg"},{"type":"photo","link":"https://pbs.twimg.com/media/A2.jpg"}]}"#
        let one = #"{"id":"1","created_at":"\#(isoDate(1))","full_text":"one","user":{"username":"someone"},"entities":[{"type":"photo","link":"https://pbs.twimg.com/media/B1.jpg"}]}"#
        try await t.importUser("alice", [two])
        try await t.importUser(bucket, [two, one])

        let repo = TweetRepository(t.db)
        let page = try await repo.getMediaPage(
            usernames: ["alice", bucket], after: nil, limit: 10)

        // (tweet_id, idx) 単位で dedup、代表は実ユーザーアーカイブ
        #expect(page.map { "\($0.tweetId):\($0.idx):\($0.username)" }
            == ["2:1:alice", "2:2:alice", "1:1:\(bucket)"])

        // 3 要素カーソルの継続
        let p1 = try await repo.getMediaPage(usernames: ["alice", bucket], after: nil, limit: 2)
        let last = p1.last!
        let p2 = try await repo.getMediaPage(
            usernames: ["alice", bucket],
            after: MediaCursor(sortKey: last.sortKey, idInt: last.idInt, idx: last.idx), limit: 2)
        #expect(p1.map { "\($0.tweetId):\($0.idx)" } == ["2:1", "2:2"])
        #expect(p2.map { "\($0.tweetId):\($0.idx)" } == ["1:1"])
    }

    @Test func emptyUsernamesReturnsEmptyPage() async throws {
        let t = try TestDataDir()
        defer { t.cleanup() }
        try await t.importUser("alice", [tweetLine(1)])
        let repo = TweetRepository(t.db)
        let page = try await repo.getPage(usernames: [], unreadOnly: false, after: nil, limit: 10)
        #expect(page.rows.isEmpty)
        #expect(try await repo.getMediaPage(usernames: [], after: nil, limit: 10).isEmpty)
    }
}
