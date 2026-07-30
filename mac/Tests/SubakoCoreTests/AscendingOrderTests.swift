import Foundation
import Testing
@testable import SubakoCore

/// 「古い順」(ascending) 表示のリポジトリ層テスト (C# AscendingOrderTests.cs の移植)。
@Suite struct AscendingOrderTests {
    private let bucket = "searches/kw-x"

    @Test func ascendingReturnsOldestFirst() async throws {
        let t = try TestDataDir()
        defer { t.cleanup() }
        try await t.importUser("alice", [
            tweetLine(2, date: isoDate(2)), tweetLine(1, date: isoDate(1)), tweetLine(3, date: isoDate(3)),
        ])

        let page = try await TweetRepository(t.db).getPage(
            usernames: ["alice"], unreadOnly: false, after: nil, limit: 10, ascending: true)

        #expect(page.rows.map(\.tweetId) == ["1", "2", "3"])
    }

    @Test func ascendingKeysetPagesConcatenateWithoutGapsOrDuplicates() async throws {
        let t = try TestDataDir()
        defer { t.cleanup() }
        // 重複ツイートがページ境界を跨ぐ構成 (MergedTimelineTests の ASC 版)
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
                usernames: ["alice", bucket], unreadOnly: false, after: cursor, limit: 2,
                ascending: true)
            collected.append(contentsOf: page.rows.map(\.tweetId))
            if page.rows.count < 2 { break }
            let last = page.rows.last!
            cursor = TweetCursor(sortKey: last.sortKey, idInt: last.idInt)
        }

        #expect(collected == ["1", "2", "3", "4", "5"])
    }

    @Test func ascendingKeepsDedupRepresentative() async throws {
        let t = try TestDataDir()
        defer { t.cleanup() }
        let shared = tweetLine(2, date: isoDate(2))
        try await t.importUser("alice", [shared])
        try await t.importUser(bucket, [shared, tweetLine(1, date: isoDate(1))])

        let page = try await TweetRepository(t.db).getPage(
            usernames: ["alice", bucket], unreadOnly: false, after: nil, limit: 10,
            ascending: true)

        #expect(page.rows.map(\.tweetId) == ["1", "2"])
        #expect(page.rows.first { $0.tweetId == "2" }?.username == "alice")   // 実アーカイブが代表
    }

    @Test func ascendingMediaPageKeepsIdxOrderAndCursorContinues() async throws {
        let t = try TestDataDir()
        defer { t.cleanup() }
        let two = #"{"id":"2","created_at":"\#(isoDate(2))","full_text":"two","user":{"username":"alice"},"entities":[{"type":"photo","link":"https://pbs.twimg.com/media/A1.jpg"},{"type":"photo","link":"https://pbs.twimg.com/media/A2.jpg"}]}"#
        let one = #"{"id":"1","created_at":"\#(isoDate(1))","full_text":"one","user":{"username":"alice"},"entities":[{"type":"photo","link":"https://pbs.twimg.com/media/B1.jpg"}]}"#
        try await t.importUser("alice", [two, one])
        let repo = TweetRepository(t.db)

        let page = try await repo.getMediaPage(
            usernames: ["alice"], after: nil, limit: 10, ascending: true)
        // ツイートは古い順、同一ツイート内の idx は昇順のまま
        #expect(page.map { "\($0.tweetId):\($0.idx)" } == ["1:1", "2:1", "2:2"])

        // 3 要素カーソルの継続
        let p1 = try await repo.getMediaPage(
            usernames: ["alice"], after: nil, limit: 2, ascending: true)
        let last = p1.last!
        let p2 = try await repo.getMediaPage(
            usernames: ["alice"],
            after: MediaCursor(sortKey: last.sortKey, idInt: last.idInt, idx: last.idx),
            limit: 2, ascending: true)
        #expect(p1.map { "\($0.tweetId):\($0.idx)" } == ["1:1", "2:1"])
        #expect(p2.map { "\($0.tweetId):\($0.idx)" } == ["2:2"])
    }

    @Test func ascendingCombinesWithRangeAndUnreadOnly() async throws {
        let t = try TestDataDir()
        defer { t.cleanup() }
        try await t.importUser("alice", [
            tweetLine(1, date: isoDate(1)), tweetLine(2, date: isoDate(2)),
            tweetLine(3, date: isoDate(3)), tweetLine(4, date: isoDate(4)),
        ])
        let repo = TweetRepository(t.db)
        try await repo.setRead(tweetId: "3", username: "alice", read: true)

        // 期間 [Day2, Day5) かつ未読のみ → 2, 4 が古い順
        let page = try await repo.getPage(
            usernames: ["alice"], unreadOnly: true, after: nil, limit: 10,
            range: DateRangeFilter(fromEpoch: isoEpoch(2), toEpochExclusive: isoEpoch(5)),
            ascending: true)

        #expect(page.rows.map(\.tweetId) == ["2", "4"])
    }
}

/// 期間フィルタの境界規則 (docs/viewer-features.md §7.3 — ローカル 0 時の半開区間)。
/// タイムゾーン依存は JST 固定で検証する。
@Suite struct DateRangeFilterTests {
    private let jst = TimeZone(identifier: "Asia/Tokyo")!

    @Test func 月境界はローカル0時の半開区間() {
        // JST 2026-07 の月フィルタ: [2026-06-30T15:00Z, 2026-07-31T15:00Z)
        let range = DateRangeFilter.fromLocalParts(year: 2026, month: 7, day: nil, timeZone: jst)
        let junE30_15utc = Int64(1_782_831_600)   // 2026-06-30T15:00:00Z = JST 7/1 0:00
        #expect(range.fromEpoch == junE30_15utc)
        #expect(range.toEpochExclusive == junE30_15utc + 31 * 86_400)
    }

    @Test func 日フィルタは1日幅() {
        let range = DateRangeFilter.fromLocalParts(year: 2026, month: 7, day: 15, timeZone: jst)
        #expect(range.toEpochExclusive - range.fromEpoch == 86_400)
    }

    @Test func 年フィルタは1年幅でうるう年も正しい() {
        let range = DateRangeFilter.fromLocalParts(year: 2024, month: nil, day: nil, timeZone: jst)
        #expect(range.toEpochExclusive - range.fromEpoch == 366 * 86_400)   // 2024 はうるう年
    }

    @Test func yearsCoveredはローカル暦の年() {
        // 2018-12-31T20:00Z は JST では 2019-01-01T05:00
        var comps = DateComponents()
        (comps.year, comps.month, comps.day, comps.hour) = (2018, 12, 31, 20)
        comps.timeZone = TimeZone(identifier: "UTC")
        let epoch = Int64(Calendar(identifier: .gregorian).date(from: comps)!.timeIntervalSince1970)
        #expect(DateRangeFilter.yearsCovered(minEpoch: epoch, maxEpoch: epoch, timeZone: jst) == [2019])
        #expect(DateRangeFilter.yearsCovered(
            minEpoch: epoch - 86_400 * 400, maxEpoch: epoch, timeZone: jst) == [2017, 2018, 2019])
    }

    @Test func daysInは月の実日数() {
        #expect(DateRangeFilter.daysIn(year: 2026, month: 2, timeZone: jst) == 28)
        #expect(DateRangeFilter.daysIn(year: 2024, month: 2, timeZone: jst) == 29)
        #expect(DateRangeFilter.daysIn(year: 2026, month: 7, timeZone: jst) == 31)
    }

    @Test func 月初のJST早朝ツイートが前月に漏れない() async throws {
        // 2026-07-01T05:00 JST = 2026-06-30T20:00Z。7 月フィルタに含まれ、6 月には含まれない
        let t = try TestDataDir()
        defer { t.cleanup() }
        try await t.importUser("alice", [
            tweetLine(1, date: "2026-06-30T20:00:00+00:00"),
        ])
        let repo = TweetRepository(t.db)
        let july = DateRangeFilter.fromLocalParts(year: 2026, month: 7, day: nil, timeZone: jst)
        let june = DateRangeFilter.fromLocalParts(year: 2026, month: 6, day: nil, timeZone: jst)
        let inJuly = try await repo.getPage(
            usernames: ["alice"], unreadOnly: false, after: nil, limit: 10, range: july)
        let inJune = try await repo.getPage(
            usernames: ["alice"], unreadOnly: false, after: nil, limit: 10, range: june)
        #expect(inJuly.rows.count == 1)
        #expect(inJune.rows.isEmpty)
    }
}
