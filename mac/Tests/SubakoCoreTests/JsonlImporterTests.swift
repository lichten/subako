import Foundation
import Testing
@testable import SubakoCore

/// C# JsonlImporterTests.cs の移植 (共有契約 #8)。
@Suite struct JsonlImporterTests {
    @Test func importsAndResumesDifferentially() async throws {
        let t = try TestDataDir()
        defer { t.cleanup() }
        try await UserRepository(t.db).add("alice")
        try t.writeJsonl("alice", [tweetLine(1), tweetLine(2), tweetLine(3)])

        let importer = JsonlImporter(t.db)
        let first = try await importer.importUser("alice")
        #expect(first.newTweets == 3)
        #expect(first.skippedLines == 0)
        #expect(try t.countTweets("alice") == 3)

        // 再実行は 0 件
        let second = try await importer.importUser("alice")
        #expect(second.newTweets == 0)

        // 追記分だけ差分取込
        try t.writeJsonl("alice", [tweetLine(4), tweetLine(5)])
        let third = try await importer.importUser("alice")
        #expect(third.newTweets == 2)
        #expect(try t.countTweets("alice") == 5)
    }

    @Test func skipsBrokenAndIncompleteTrailingLines() async throws {
        let t = try TestDataDir()
        defer { t.cleanup() }
        try await UserRepository(t.db).add("bob")
        try t.writeJsonl("bob", [tweetLine(1), "{broken"])
        // \n なしの不完全行 (取込対象外、オフセットも進まない)
        try t.appendRaw("bob", #"{"id":"99","full_text":"partial"#)

        let importer = JsonlImporter(t.db)
        let result = try await importer.importUser("bob")
        #expect(result.newTweets == 1)
        #expect(result.skippedLines == 1)

        // 不完全行が完結したら次の取込で入る
        try t.appendRaw("bob", "\"}\n")
        let next = try await importer.importUser("bob")
        #expect(next.newTweets == 1)
        #expect(try t.countTweets("bob") == 2)
    }

    @Test func rebuildPreservesReadState() async throws {
        let t = try TestDataDir()
        defer { t.cleanup() }
        try await UserRepository(t.db).add("carol")
        try t.writeJsonl("carol", [tweetLine(10), tweetLine(11)])

        let importer = JsonlImporter(t.db)
        _ = try await importer.importUser("carol")

        let tweets = TweetRepository(t.db)
        try await tweets.setRead(tweetId: "10", username: "carol", read: true)

        let page = try await tweets.getPage(
            usernames: ["carol"], unreadOnly: false, after: nil, limit: 10)
        #expect(page.rows.count == 2)
        #expect(page.rows.contains { $0.tweetId == "10" && $0.isRead })

        let rebuilt = try await importer.rebuildUser("carol")
        #expect(rebuilt.newTweets == 2)

        let after = try await tweets.getPage(
            usernames: ["carol"], unreadOnly: false, after: nil, limit: 10)
        #expect(after.rows.contains { $0.tweetId == "10" && $0.isRead })
        #expect(after.rows.contains { $0.tweetId == "11" && !$0.isRead })

        let unreadOnly = try await tweets.getPage(
            usernames: ["carol"], unreadOnly: true, after: nil, limit: 10)
        #expect(unreadOnly.rows.map(\.tweetId) == ["11"])
    }

    @Test func truncatedJsonlTriggersRebuild() async throws {
        let t = try TestDataDir()
        defer { t.cleanup() }
        try await UserRepository(t.db).add("dave")
        try t.writeJsonl("dave", [tweetLine(1), tweetLine(2), tweetLine(3)])
        let importer = JsonlImporter(t.db)
        _ = try await importer.importUser("dave")
        #expect(try t.countTweets("dave") == 3)

        // JSONL 作り直し (短くなる) → offset > length → 自動 rebuild
        try FileManager.default.removeItem(atPath: t.db.jsonlPath("dave"))
        try t.writeJsonl("dave", [tweetLine(7)])
        let result = try await importer.importUser("dave")
        #expect(result.newTweets == 1)
        #expect(try t.countTweets("dave") == 1)
    }

    @Test func keysetPaginationOrdersBySortKeyDesc() async throws {
        let t = try TestDataDir()
        defer { t.cleanup() }
        try await t.importUser("erin", [
            tweetLine(1, date: "Wed Apr 11 08:26:14 +0000 2007"),
            tweetLine(2, date: "Wed Oct 10 20:19:24 +0000 2018"),
            tweetLine(3, date: "Tue Jul 21 20:23:54 +0000 2026"),
        ])

        let tweets = TweetRepository(t.db)
        let page1 = try await tweets.getPage(
            usernames: ["erin"], unreadOnly: false, after: nil, limit: 2)
        #expect(page1.rows.map(\.tweetId) == ["3", "2"])

        let last = page1.rows.last!
        let page2 = try await tweets.getPage(
            usernames: ["erin"], unreadOnly: false,
            after: TweetCursor(sortKey: last.sortKey, idInt: last.idInt), limit: 2)
        #expect(page2.rows.map(\.tweetId) == ["1"])
    }

    @Test func mediaPageReturnsOwnMediaOnlyExcludingRetweets() async throws {
        let t = try TestDataDir()
        defer { t.cleanup() }
        try await t.importUser("grace", [
            // 本文画像 2 枚 (最新)
            #"{"id":"4","created_at":"Tue Jul 21 20:23:54 +0000 2026","full_text":"own2","entities":[{"type":"photo","link":"https://pbs.twimg.com/media/A1.jpg"},{"type":"photo","link":"https://pbs.twimg.com/media/A2.jpg"}]}"#,
            // 引用: 本文画像 1 枚 + 引用先画像 1 枚
            #"{"id":"3","created_at":"Wed Oct 10 20:19:24 +0000 2018","full_text":"quote","entities":[{"type":"photo","link":"https://pbs.twimg.com/media/B1.jpg"}],"quoted_status":{"id":"30","full_text":"q","entities":[{"type":"photo","link":"https://pbs.twimg.com/media/QB.jpg"}]}}"#,
            // RT: RT元画像のみ → メディア欄に出ない
            #"{"id":"2","created_at":"Wed Oct 10 10:00:00 +0000 2018","full_text":"RT @a: x","retweeted_status":{"id":"20","full_text":"x","entities":[{"type":"photo","link":"https://pbs.twimg.com/media/RT1.jpg"}]}}"#,
            // 画像なし
            #"{"id":"1","created_at":"Wed Apr 11 08:26:14 +0000 2007","full_text":"plain","entities":[]}"#,
        ])

        let tweets = TweetRepository(t.db)
        let page = try await tweets.getMediaPage(usernames: ["grace"], after: nil, limit: 10)
        // 本文画像のみ・新しい順・同一ツイート内は idx 昇順
        #expect(page.map { "\($0.tweetId):\($0.idx)" } == ["4:1", "4:2", "3:1"])

        // keyset ページング (limit 2 → 続き)
        let p1 = try await tweets.getMediaPage(usernames: ["grace"], after: nil, limit: 2)
        let last = p1.last!
        let p2 = try await tweets.getMediaPage(
            usernames: ["grace"],
            after: MediaCursor(sortKey: last.sortKey, idInt: last.idInt, idx: last.idx), limit: 2)
        #expect(p1.map { "\($0.tweetId):\($0.idx)" } == ["4:1", "4:2"])
        #expect(p2.map { "\($0.tweetId):\($0.idx)" } == ["3:1"])
    }

    @Test func searchBucketImportDoesNotOverwriteProfile() async throws {
        let t = try TestDataDir()
        defer { t.cleanup() }
        try await UserRepository(t.db).add("searches/kw-12345678")
        try t.writeJsonl("searches/kw-12345678", [
            #"{"id":"1","full_text":"hit","user":{"username":"someone","display_name":"Someone Else","profile_image_url":"https://pbs.twimg.com/profile_images/9/x_normal.jpg"}}"#,
        ])

        let importer = JsonlImporter(t.db)
        let result = try await importer.importUser("searches/kw-12345678")
        #expect(result.newTweets == 1)

        // バケットの表示名は他人の user オブジェクトで上書きされない
        let displayName = try t.scalarString(
            "SELECT display_name FROM users WHERE username = 'searches/kw-12345678'")
        #expect(displayName == nil)

        // author 列には実投稿者が入る
        let page = try await TweetRepository(t.db).getPage(
            usernames: ["searches/kw-12345678"], unreadOnly: false, after: nil, limit: 10)
        #expect(page.rows.first?.authorUsername == "someone")
        #expect(page.rows.first?.authorDisplayName == "Someone Else")
    }

    @Test func sameTweetImportsIntoArchiveAndSearchBucket() async throws {
        let t = try TestDataDir()
        defer { t.cleanup() }
        let users = UserRepository(t.db)
        let line = tweetLine(1)
        try await t.importUser("alice", [line])
        try await t.importUser("searches/kw-12345678", [line])
        #expect(try t.countTweets("alice") == 1)
        #expect(try t.countTweets("searches/kw-12345678") == 1)

        // タグを両方に付与 → バケット削除でバケットの割当だけ消える
        let tags = TagRepository(t.db)
        let tagId = try await tags.add(name: "A")
        try await tags.assign(username: "alice", tagId: tagId)
        try await tags.assign(username: "searches/kw-12345678", tagId: tagId)

        // バケット削除ではアーカイブ側の行は残る
        try await users.deleteArchive("searches/kw-12345678")
        #expect(try t.countTweets("searches/kw-12345678") == 0)
        #expect(try t.countTweets("alice") == 1)
        let assignments = try await tags.getAssignments()
        #expect(assignments["searches/kw-12345678"] == nil)
        #expect(assignments["alice"] == [tagId])
    }

    @Test func getAllExcludesSearchBuckets() async throws {
        let t = try TestDataDir()
        defer { t.cleanup() }
        let users = UserRepository(t.db)
        try await users.add("alice")
        try await users.add("searches/kw-12345678")

        #expect(try await users.getAll().map(\.username) == ["alice"])
        #expect(try await users.getSearchBuckets().map(\.username) == ["searches/kw-12345678"])
    }

    @Test func byteOffsetsSurviveMultibyteText() async throws {
        let t = try TestDataDir()
        defer { t.cleanup() }
        // 絵文字・日本語混在でバイトオフセットずれがないこと
        let line1 = #"{"id":"1","full_text":"🇯🇵 日本語テキスト 🚀","entities":[]}"#
        let line2 = #"{"id":"2","full_text":"second","entities":[]}"#
        try await t.importUser("frank", [line1, line2])

        // raw_offset/raw_length で元の行を正確に切り出せること
        let page = try await TweetRepository(t.db).getPage(
            usernames: ["frank"], unreadOnly: false, after: nil, limit: 10)
        let row1 = try #require(page.rows.first { $0.tweetId == "1" })
        let handle = FileHandle(forReadingAtPath: t.db.jsonlPath("frank"))!
        defer { try? handle.close() }
        try handle.seek(toOffset: UInt64(row1.rawOffset))
        let data = try #require(try handle.read(upToCount: Int(row1.rawLength)))
        #expect(String(decoding: data, as: UTF8.self) == line1)
    }
}
