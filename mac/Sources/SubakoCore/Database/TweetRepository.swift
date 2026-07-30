import Foundation

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
        return try await db.read { conn in
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
            var args: [SQLiteBindable?] = usernames
            args.append(unreadOnly ? 1 : 0)
            args.append(range == nil ? 1 : 0)
            args.append(range?.fromEpoch ?? 0)
            args.append(range?.toEpochExclusive ?? 0)
            args.append(after == nil ? 1 : 0)
            args.append(after?.sortKey ?? 0)
            args.append(after?.sortKey ?? 0)
            args.append(after?.idInt ?? 0)
            args.append(limit)

            let rows = try conn.query(sql, args).map { row in
                TweetRow(
                    tweetId: row.string("tweet_id") ?? "",
                    idInt: row.int64("id_int") ?? 0,
                    username: row.string("username") ?? "",
                    authorUsername: row.string("author_username"),
                    authorDisplayName: row.string("author_display_name"),
                    authorIconUrl: row.string("author_icon_url"),
                    createdAtUtc: row.string("created_at_utc") ?? "",
                    sortKey: row.int64("sort_key") ?? 0,
                    type: TweetType(rawValue: row.int("tweet_type") ?? 0) ?? .tweet,
                    fullText: row.string("full_text") ?? "",
                    lang: row.string("lang"),
                    inReplyToUsername: row.string("in_reply_to_username"),
                    rtUsername: row.string("rt_username"),
                    rtDisplayName: row.string("rt_display_name"),
                    rtText: row.string("rt_text"),
                    rtIconUrl: row.string("rt_icon_url"),
                    quotedUsername: row.string("quoted_username"),
                    quotedDisplayName: row.string("quoted_display_name"),
                    quotedText: row.string("quoted_text"),
                    quotedIconUrl: row.string("quoted_icon_url"),
                    likeCount: row.int64("like_count") ?? 0,
                    retweetCount: row.int64("retweet_count") ?? 0,
                    replyCount: row.int64("reply_count") ?? 0,
                    viewCount: row.int64("view_count") ?? 0,
                    mediaCount: row.int("media_count") ?? 0,
                    rawOffset: row.int64("raw_offset") ?? 0,
                    rawLength: row.int64("raw_length") ?? 0,
                    isRead: row.bool("is_read"))
            }

            let media = try Self.loadMedia(
                conn, tweetIds: rows.filter { $0.mediaCount > 0 }.map(\.tweetId))
            let archives = try Self.loadDuplicateArchives(conn, tweetIds: rows.map(\.tweetId))
            return TweetPage(rows: rows, media: media, archivesByTweetId: archives)
        }
    }

    /// ページ内 tweet_id ごとに、それを含む全アーカイブの username を引く。
    /// read_state は tweet_id 単位で全アーカイブ共通のため、既読化は表示中でない
    /// アーカイブの未読数も実際に減らす。辞書には 2 アーカイブ以上に存在するものだけ載せる。
    private static func loadDuplicateArchives(
        _ conn: SQLiteConnection, tweetIds: [String]
    ) throws -> [String: [String]] {
        guard !tweetIds.isEmpty else { return [:] }
        let placeholders = tweetIds.map { _ in "?" }.joined(separator: ",")
        var all: [String: [String]] = [:]
        for row in try conn.query(
            "SELECT tweet_id, username FROM tweets WHERE tweet_id IN (\(placeholders)) ORDER BY tweet_id",
            tweetIds.map { $0 as SQLiteBindable? })
        {
            if let tweetId = row.string("tweet_id"), let username = row.string("username") {
                all[tweetId, default: []].append(username)
            }
        }
        return all.filter { $0.value.count > 1 }
    }

    private static func loadMedia(
        _ conn: SQLiteConnection, tweetIds: [String]
    ) throws -> [String: [TweetMediaRow]] {
        guard !tweetIds.isEmpty else { return [:] }
        let placeholders = tweetIds.map { _ in "?" }.joined(separator: ",")
        var result: [String: [TweetMediaRow]] = [:]
        for row in try conn.query(
            "SELECT tweet_id, idx, source_url, ext, origin FROM tweet_media WHERE tweet_id IN (\(placeholders)) ORDER BY tweet_id, idx",
            tweetIds.map { $0 as SQLiteBindable? })
        {
            let media = TweetMediaRow(
                tweetId: row.string("tweet_id") ?? "",
                index: row.int("idx") ?? 0,
                sourceUrl: row.string("source_url"),
                ext: row.string("ext") ?? "jpg",
                origin: MediaOrigin(rawValue: row.int("origin") ?? 0) ?? .own)
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
        return try await db.read { conn in
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
            var args: [SQLiteBindable?] = usernames
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

            return try conn.query(sql, args).map { row in
                MediaPageRow(
                    tweetId: row.string("tweet_id") ?? "",
                    idx: row.int("idx") ?? 0,
                    ext: row.string("ext") ?? "jpg",
                    sortKey: row.int64("sort_key") ?? 0,
                    idInt: row.int64("id_int") ?? 0,
                    fullText: row.string("full_text") ?? "",
                    createdAtUtc: row.string("created_at_utc") ?? "",
                    username: row.string("username") ?? "")
            }
        }
    }

    /// 表示対象全体の sort_key の最小/最大 (期間フィルタの年リスト用)。行が無ければ nil。
    public func getDateBounds(usernames: [String]) async throws -> (min: Int64, max: Int64)? {
        if usernames.isEmpty {
            return nil
        }
        return try await db.read { conn -> (min: Int64, max: Int64)? in
            let placeholders = usernames.map { _ in "?" }.joined(separator: ",")
            guard let row = try conn.queryOne(
                "SELECT MIN(sort_key) AS mn, MAX(sort_key) AS mx FROM tweets WHERE username IN (\(placeholders))",
                usernames.map { $0 as SQLiteBindable? }),
                let mn = row.int64("mn"), let mx = row.int64("mx")
            else { return nil }
            return (mn, mx)
        }
    }

    /// 手動トグル用の即時書込。
    public func setRead(tweetId: String, username: String, read: Bool) async throws {
        try await db.write { conn in
            if read {
                try conn.execute(
                    "INSERT OR IGNORE INTO read_state (tweet_id, username, read_at) VALUES (?, ?, ?)",
                    [tweetId, username, DateParsers.utcNow()])
            } else {
                try conn.execute(
                    "DELETE FROM read_state WHERE tweet_id = ?", [tweetId])
            }
        }
    }

    /// 既読マークのバッチ書込 (ReadMarkQueue 用)。1 トランザクションでまとめる。
    public func markRead(_ marks: [(tweetId: String, username: String)]) async throws {
        guard !marks.isEmpty else { return }
        let now = DateParsers.utcNow()
        try await db.write { conn in
            for mark in marks {
                try conn.execute(
                    "INSERT OR IGNORE INTO read_state (tweet_id, username, read_at) VALUES (?, ?, ?)",
                    [mark.tweetId, mark.username, now])
            }
        }
    }
}
