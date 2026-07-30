import Foundation
import GRDB

/// タイムライン 1 ページ分。archivesByTweetId は複数アーカイブに存在する tweet_id のみを持つ
/// (既読化時に該当する全アーカイブの未読数を減らすため。表示中でないアーカイブも含む)。
public struct TweetPage: Sendable {
    public let rows: [TweetRow]
    public let media: [String: [TweetMediaRow]]
    public let archivesByTweetId: [String: [String]]

    public static let empty = TweetPage(rows: [], media: [:], archivesByTweetId: [:])

    public init(rows: [TweetRow], media: [String: [TweetMediaRow]], archivesByTweetId: [String: [String]]) {
        self.rows = rows
        self.media = media
        self.archivesByTweetId = archivesByTweetId
    }
}

public struct MediaPageRow: Sendable, Equatable {
    public let tweetId: String
    public let idx: Int
    public let ext: String
    public let sortKey: Int64
    public let idInt: Int64
    public let fullText: String
    public let createdAtUtc: String
    public let username: String
}

public struct TweetCursor: Sendable, Equatable {
    public let sortKey: Int64
    public let idInt: Int64

    public init(sortKey: Int64, idInt: Int64) {
        self.sortKey = sortKey
        self.idInt = idInt
    }
}

public struct MediaCursor: Sendable, Equatable {
    public let sortKey: Int64
    public let idInt: Int64
    public let idx: Int

    public init(sortKey: Int64, idInt: Int64, idx: Int) {
        self.sortKey = sortKey
        self.idInt = idInt
        self.idx = idx
    }
}

/// C# Data/TweetRepository.cs の移植。SQL 規則 (窓関数 dedup・keyset カーソル・
/// 方向反転) は AscendingOrderTests / MergedTimelineTests で固定する。
public final class TweetRepository: Sendable {
    private let db: ViewerDatabase

    public init(_ db: ViewerDatabase) {
        self.db = db
    }

    /// keyset pagination。after が nil なら先頭ページ。
    /// 複数アーカイブを混ぜる場合、同一 tweet_id はアーカイブと検索バケットの両方に
    /// 存在しうる (PK は (username, tweet_id)) ため、窓関数で LIMIT より前に重複排除する。
    /// 代表は実ユーザーアーカイブ優先 (searches/ は劣後)。
    public func getPage(
        usernames: [String], unreadOnly: Bool, after: TweetCursor?, limit: Int,
        range: DateRangeFilter? = nil, ascending: Bool = false
    ) async throws -> TweetPage {
        if usernames.isEmpty {
            return .empty
        }
        return try await db.reader.read { db in
            let placeholders = usernames.map { _ in "?" }.joined(separator: ",")
            let columns = """
                t.tweet_id, t.id_int, t.username, t.created_at_utc, t.sort_key,
                t.tweet_type, t.full_text, t.lang, t.in_reply_to_username,
                t.rt_username, t.rt_display_name, t.rt_text,
                t.quoted_username, t.quoted_display_name, t.quoted_text,
                t.like_count, t.retweet_count, t.reply_count, t.view_count,
                t.media_count, t.raw_offset, t.raw_length,
                t.rt_icon_url, t.quoted_icon_url,
                t.author_username, t.author_display_name, t.author_icon_url,
                (r.tweet_id IS NOT NULL) AS is_read
                """
            // 並び順はパラメータではなく SQL 文字列で分岐する (OR で混ぜると索引レンジに
            // 落ちず全走査に退化しうる)。2 列とも同方向にすること。
            let cmp = ascending ? ">" : "<"
            let dir = ascending ? "ASC" : "DESC"
            let filters = """
                  AND (? = 0 OR r.tweet_id IS NULL)
                  AND (? = 1 OR (t.sort_key >= ? AND t.sort_key < ?))
                  AND (? = 1 OR t.sort_key \(cmp) ?
                       OR (t.sort_key = ? AND t.id_int \(cmp) ?))
                """
            // 単一アーカイブは PK により重複しないため、窓関数を通さず
            // ix_tweets_user_sort の索引ストリームで返す (性能)
            let sql = usernames.count == 1
                ? """
                  SELECT \(columns)
                  FROM tweets t
                  LEFT JOIN read_state r ON r.tweet_id = t.tweet_id
                  WHERE t.username = ?
                  \(filters)
                  ORDER BY t.sort_key \(dir), t.id_int \(dir)
                  LIMIT ?
                  """
                : """
                  SELECT * FROM (
                      SELECT \(columns),
                             ROW_NUMBER() OVER (
                                 PARTITION BY t.tweet_id
                                 ORDER BY (t.username LIKE 'searches/%'), t.username) AS rn
                      FROM tweets t
                      LEFT JOIN read_state r ON r.tweet_id = t.tweet_id
                      WHERE t.username IN (\(placeholders))
                      \(filters)
                  )
                  WHERE rn = 1
                  ORDER BY sort_key \(dir), id_int \(dir)
                  LIMIT ?
                  """
            var args: [DatabaseValueConvertible] = usernames
            args.append(unreadOnly ? 1 : 0)
            args.append(range == nil ? 1 : 0)
            args.append(range?.fromEpoch ?? 0)
            args.append(range?.toEpochExclusive ?? 0)
            args.append(after == nil ? 1 : 0)
            args.append(after?.sortKey ?? 0)
            args.append(after?.sortKey ?? 0)
            args.append(after?.idInt ?? 0)
            args.append(limit)

            var rows: [TweetRow] = []
            let cursor = try Row.fetchCursor(db, sql: sql, arguments: StatementArguments(args)!)
            while let row = try cursor.next() {
                rows.append(TweetRow(
                    tweetId: row["tweet_id"],
                    idInt: row["id_int"],
                    username: row["username"],
                    authorUsername: row["author_username"],
                    authorDisplayName: row["author_display_name"],
                    authorIconUrl: row["author_icon_url"],
                    createdAtUtc: row["created_at_utc"],
                    sortKey: row["sort_key"],
                    type: TweetType(rawValue: row["tweet_type"]) ?? .tweet,
                    fullText: row["full_text"],
                    lang: row["lang"],
                    inReplyToUsername: row["in_reply_to_username"],
                    rtUsername: row["rt_username"],
                    rtDisplayName: row["rt_display_name"],
                    rtText: row["rt_text"],
                    rtIconUrl: row["rt_icon_url"],
                    quotedUsername: row["quoted_username"],
                    quotedDisplayName: row["quoted_display_name"],
                    quotedText: row["quoted_text"],
                    quotedIconUrl: row["quoted_icon_url"],
                    likeCount: row["like_count"],
                    retweetCount: row["retweet_count"],
                    replyCount: row["reply_count"],
                    viewCount: row["view_count"],
                    mediaCount: row["media_count"],
                    rawOffset: row["raw_offset"],
                    rawLength: row["raw_length"],
                    isRead: (row["is_read"] as Int64? ?? 0) != 0))
            }

            let media = try Self.loadMedia(
                db, tweetIds: rows.filter { $0.mediaCount > 0 }.map(\.tweetId))
            let archives = try Self.loadDuplicateArchives(db, tweetIds: rows.map(\.tweetId))
            return TweetPage(rows: rows, media: media, archivesByTweetId: archives)
        }
    }

    /// ページ内 tweet_id ごとに、それを含む全アーカイブの username を引く。
    /// read_state は tweet_id 単位で全アーカイブ共通のため、既読化は表示中でない
    /// アーカイブの未読数も実際に減らす。そのため表示中集合では絞らない。
    /// 辞書には 2 アーカイブ以上に存在するものだけ載せる。
    private static func loadDuplicateArchives(
        _ db: Database, tweetIds: [String]
    ) throws -> [String: [String]] {
        guard !tweetIds.isEmpty else { return [:] }
        let placeholders = tweetIds.map { _ in "?" }.joined(separator: ",")
        var all: [String: [String]] = [:]
        let cursor = try Row.fetchCursor(
            db,
            sql: "SELECT tweet_id, username FROM tweets WHERE tweet_id IN (\(placeholders)) ORDER BY tweet_id",
            arguments: StatementArguments(tweetIds)!)
        while let row = try cursor.next() {
            all[row["tweet_id"], default: []].append(row["username"])
        }
        return all.filter { $0.value.count > 1 }
    }

    private static func loadMedia(
        _ db: Database, tweetIds: [String]
    ) throws -> [String: [TweetMediaRow]] {
        guard !tweetIds.isEmpty else { return [:] }
        let placeholders = tweetIds.map { _ in "?" }.joined(separator: ",")
        var result: [String: [TweetMediaRow]] = [:]
        let cursor = try Row.fetchCursor(
            db,
            sql: "SELECT tweet_id, idx, source_url, ext, origin FROM tweet_media WHERE tweet_id IN (\(placeholders)) ORDER BY tweet_id, idx",
            arguments: StatementArguments(tweetIds)!)
        while let row = try cursor.next() {
            let media = TweetMediaRow(
                tweetId: row["tweet_id"],
                index: row["idx"],
                sourceUrl: row["source_url"],
                ext: row["ext"],
                origin: MediaOrigin(rawValue: row["origin"]) ?? .own)
            result[media.tweetId, default: []].append(media)
        }
        return result
    }

    /// メディア欄用: 本人の投稿画像のみ (origin=0、RT 除外)。
    /// keyset カーソルは (sort_key, id_int, idx) の 3 要素。
    /// 重複排除は getPage と同じ規則で、パーティションは (tweet_id, idx)。
    public func getMediaPage(
        usernames: [String], after: MediaCursor?, limit: Int,
        range: DateRangeFilter? = nil, ascending: Bool = false
    ) async throws -> [MediaPageRow] {
        if usernames.isEmpty {
            return []
        }
        return try await db.reader.read { db in
            let placeholders = usernames.map { _ in "?" }.joined(separator: ",")
            // 第 3 要素 idx はツイート内の並びで常に昇順のため、比較 (> ?) も
            // ORDER BY (idx ASC) も方向によらず不変
            let cmp = ascending ? ">" : "<"
            let dir = ascending ? "ASC" : "DESC"
            let filters = """
                  AND m.origin = 0
                  AND t.tweet_type != 1
                  AND (? = 1 OR (t.sort_key >= ? AND t.sort_key < ?))
                  AND (? = 1
                       OR t.sort_key \(cmp) ?
                       OR (t.sort_key = ? AND t.id_int \(cmp) ?)
                       OR (t.sort_key = ? AND t.id_int = ? AND m.idx > ?))
                """
            let sql = usernames.count == 1
                ? """
                  SELECT m.tweet_id, m.idx, m.ext, t.sort_key, t.id_int,
                         t.full_text, t.created_at_utc, t.username
                  FROM tweet_media m
                  JOIN tweets t ON t.tweet_id = m.tweet_id
                  WHERE t.username = ?
                  \(filters)
                  ORDER BY t.sort_key \(dir), t.id_int \(dir), m.idx ASC
                  LIMIT ?
                  """
                : """
                  SELECT * FROM (
                      SELECT m.tweet_id, m.idx, m.ext, t.sort_key, t.id_int,
                             t.full_text, t.created_at_utc, t.username,
                             ROW_NUMBER() OVER (
                                 PARTITION BY m.tweet_id, m.idx
                                 ORDER BY (t.username LIKE 'searches/%'), t.username) AS rn
                      FROM tweet_media m
                      JOIN tweets t ON t.tweet_id = m.tweet_id
                      WHERE t.username IN (\(placeholders))
                      \(filters)
                  )
                  WHERE rn = 1
                  ORDER BY sort_key \(dir), id_int \(dir), idx ASC
                  LIMIT ?
                  """
            var args: [DatabaseValueConvertible] = usernames
            args.append(range == nil ? 1 : 0)
            args.append(range?.fromEpoch ?? 0)
            args.append(range?.toEpochExclusive ?? 0)
            args.append(after == nil ? 1 : 0)
            args.append(after?.sortKey ?? 0)
            args.append(after?.sortKey ?? 0)
            args.append(after?.idInt ?? 0)
            args.append(after?.sortKey ?? 0)
            args.append(after?.idInt ?? 0)
            args.append(after?.idx ?? 0)
            args.append(limit)

            var rows: [MediaPageRow] = []
            let cursor = try Row.fetchCursor(db, sql: sql, arguments: StatementArguments(args)!)
            while let row = try cursor.next() {
                rows.append(MediaPageRow(
                    tweetId: row["tweet_id"],
                    idx: row["idx"],
                    ext: row["ext"],
                    sortKey: row["sort_key"],
                    idInt: row["id_int"],
                    fullText: row["full_text"],
                    createdAtUtc: row["created_at_utc"],
                    username: row["username"]))
            }
            return rows
        }
    }

    /// 表示対象全体の sort_key の最小/最大 (期間フィルタの年リスト用)。行が無ければ nil。
    public func getDateBounds(usernames: [String]) async throws -> (min: Int64, max: Int64)? {
        if usernames.isEmpty {
            return nil
        }
        return try await db.reader.read { db in
            let placeholders = usernames.map { _ in "?" }.joined(separator: ",")
            let row = try Row.fetchOne(
                db,
                sql: "SELECT MIN(sort_key) AS mn, MAX(sort_key) AS mx FROM tweets WHERE username IN (\(placeholders))",
                arguments: StatementArguments(usernames)!)
            guard let row, let mn = row["mn"] as Int64?, let mx = row["mx"] as Int64? else {
                return nil
            }
            return (mn, mx)
        }
    }

    /// 手動トグル用の即時書込。
    public func setRead(tweetId: String, username: String, read: Bool) async throws {
        try await db.writer().write { db in
            if read {
                try db.execute(
                    sql: "INSERT OR IGNORE INTO read_state (tweet_id, username, read_at) VALUES (?, ?, ?)",
                    arguments: [tweetId, username, DateParsers.utcNow()])
            } else {
                try db.execute(
                    sql: "DELETE FROM read_state WHERE tweet_id = ?",
                    arguments: [tweetId])
            }
        }
    }

    /// 既読マークのバッチ書込 (ReadMarkQueue 用)。1 トランザクションでまとめる。
    public func markRead(_ marks: [(tweetId: String, username: String)]) async throws {
        guard !marks.isEmpty else { return }
        let now = DateParsers.utcNow()
        try await db.writer().write { db in
            for mark in marks {
                try db.execute(
                    sql: "INSERT OR IGNORE INTO read_state (tweet_id, username, read_at) VALUES (?, ?, ?)",
                    arguments: [mark.tweetId, mark.username, now])
            }
        }
    }
}
