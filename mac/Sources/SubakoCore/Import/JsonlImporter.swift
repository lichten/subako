import Foundation
import GRDB

public struct ImportResult: Sendable, Equatable {
    public let newTweets: Int
    public let skippedLines: Int
    public let newOffset: Int64
}

public struct ImportProgress: Sendable {
    public let bytesDone: Int64
    public let bytesTotal: Int64
    public let imported: Int
}

/// tweets.jsonl → SQLite の差分取込 (共有契約 #8、docs/data-layer.md §5)。
/// users.jsonl_offset のバイトオフセットから再開し、完結行 (\n 終端) のみ取り込む。
/// fetcher の並行追記に対して安全。
public final class JsonlImporter: Sendable {
    private static let batchSize = 500

    private let db: ViewerDatabase

    public init(_ db: ViewerDatabase) {
        self.db = db
    }

    public func importUser(
        _ username: String,
        progress: (@Sendable (ImportProgress) -> Void)? = nil
    ) async throws -> ImportResult {
        let jsonlPath = db.jsonlPath(username)
        guard FileManager.default.fileExists(atPath: jsonlPath) else {
            return ImportResult(newTweets: 0, skippedLines: 0, newOffset: 0)
        }
        let writer = try db.writer()

        var offset = try await writer.read { db in
            try Int64.fetchOne(
                db, sql: "SELECT jsonl_offset FROM users WHERE username = ?",
                arguments: [username]) ?? 0
        }

        guard let handle = FileHandle(forReadingAtPath: jsonlPath) else {
            return ImportResult(newTweets: 0, skippedLines: 0, newOffset: offset)
        }
        defer { try? handle.close() }
        let fileSize = Int64(try handle.seekToEnd())

        if offset > fileSize {
            // JSONL が作り直された (短くなった) → 派生データを破棄して最初から
            try await resetDerived(username)
            offset = 0
        }

        try handle.seek(toOffset: UInt64(offset))
        let reader = ByteLineReader(handle: handle, position: offset)

        var imported = 0
        var skipped = 0

        while true {
            var batch: [(tweet: ParsedTweet, endOffset: Int64)] = []
            var batchEndOffset = offset
            while batch.count < Self.batchSize, let line = reader.readLine() {
                batchEndOffset = line.endOffset
                guard let parsed = TweetJsonParser.parse(
                    line.text, username: username,
                    rawOffset: line.lineOffset, rawLength: line.lineLength)
                else {
                    skipped += 1
                    continue
                }
                batch.append((parsed, line.endOffset))
            }
            if batchEndOffset == offset && batch.isEmpty {
                break
            }

            let batchCopy = batch
            let endOffset = batchEndOffset
            let insertedCount = try await writer.write { db in
                var inserted = 0
                for entry in batchCopy {
                    if try Self.insertTweet(db, entry.tweet) {
                        inserted += 1
                    }
                }
                try db.execute(
                    sql: "UPDATE users SET jsonl_offset = ? WHERE username = ?",
                    arguments: [endOffset, username])
                return inserted
            }
            imported += insertedCount
            offset = batchEndOffset
            progress?(ImportProgress(bytesDone: offset, bytesTotal: fileSize, imported: imported))
        }

        // 表示名・アイコンは最新ツイートの user オブジェクトで更新 (取込があった場合のみ)。
        // 検索バケットは投稿者がバラバラなので更新しない (表示名 = クエリのまま保つ)
        var latestProfile: (displayName: String?, iconUrl: String?) = (nil, nil)
        if imported > 0, !SearchBuckets.isBucketId(username) {
            latestProfile = try await queryLatestProfile(writer, username: username)
        }

        let profile = latestProfile
        try await writer.write { db in
            try db.execute(
                sql: "UPDATE users SET last_import_at = ? WHERE username = ?",
                arguments: [DateParsers.utcNow(), username])
            if let displayName = profile.displayName {
                try db.execute(
                    sql: "UPDATE users SET display_name = ? WHERE username = ?",
                    arguments: [displayName, username])
            }
            if let iconUrl = profile.iconUrl {
                try db.execute(
                    sql: "UPDATE users SET icon_url = ? WHERE username = ?",
                    arguments: [iconUrl, username])
            }
        }

        return ImportResult(newTweets: imported, skippedLines: skipped, newOffset: offset)
    }

    /// 該当ユーザーの派生データを削除して JSONL から再取込。read_state は不触。
    public func rebuildUser(
        _ username: String,
        progress: (@Sendable (ImportProgress) -> Void)? = nil
    ) async throws -> ImportResult {
        try await resetDerived(username)
        return try await importUser(username, progress: progress)
    }

    private func resetDerived(_ username: String) async throws {
        try await db.writer().write { db in
            // tweet_media は tweet_id 単位で全バケット共有のため、
            // 行削除後に孤児になったものだけ消す
            try db.execute(
                sql: """
                    DELETE FROM tweets WHERE username = :u;
                    DELETE FROM tweet_media WHERE tweet_id NOT IN (SELECT tweet_id FROM tweets);
                    UPDATE users SET jsonl_offset = 0 WHERE username = :u;
                    """,
                arguments: ["u": username])
        }
    }

    /// 生 JSONL から最新ツイートの user オブジェクトをシーク読みする。
    private func queryLatestProfile(
        _ reader: any DatabaseReader, username: String
    ) async throws -> (displayName: String?, iconUrl: String?) {
        let location = try await reader.read { db in
            try Row.fetchOne(db, sql: """
                SELECT raw_offset, raw_length FROM tweets
                WHERE username = ? ORDER BY sort_key DESC, id_int DESC LIMIT 1
                """, arguments: [username])
                .map { (offset: $0["raw_offset"] as Int64, length: $0["raw_length"] as Int64) }
        }
        guard let location,
              let handle = FileHandle(forReadingAtPath: db.jsonlPath(username))
        else { return (nil, nil) }
        defer { try? handle.close() }
        do {
            try handle.seek(toOffset: UInt64(location.offset))
            guard let data = try handle.read(upToCount: Int(location.length)),
                  data.count == Int(location.length),
                  let root = JSONValue.parseLine(String(decoding: data, as: UTF8.self)),
                  let user = root.object("user")
            else { return (nil, nil) }
            return (user.string("display_name"), user.string("profile_image_url"))
        } catch {
            return (nil, nil)
        }
    }

    static func insertTweet(_ db: Database, _ parsed: ParsedTweet) throws -> Bool {
        let r = parsed.row
        try db.execute(
            sql: """
                INSERT OR IGNORE INTO tweets (
                  tweet_id, id_int, username,
                  author_username, author_display_name, author_icon_url,
                  created_at_utc, sort_key, tweet_type,
                  full_text, lang, in_reply_to_username,
                  rt_username, rt_display_name, rt_text, rt_icon_url,
                  quoted_username, quoted_display_name, quoted_text, quoted_icon_url,
                  like_count, retweet_count, reply_count, view_count,
                  media_count, raw_offset, raw_length
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
            arguments: [
                r.tweetId, r.idInt, r.username,
                r.authorUsername, r.authorDisplayName, r.authorIconUrl,
                r.createdAtUtc, r.sortKey, r.type.rawValue,
                r.fullText, r.lang, r.inReplyToUsername,
                r.rtUsername, r.rtDisplayName, r.rtText, r.rtIconUrl,
                r.quotedUsername, r.quotedDisplayName, r.quotedText, r.quotedIconUrl,
                r.likeCount, r.retweetCount, r.replyCount, r.viewCount,
                r.mediaCount, r.rawOffset, r.rawLength,
            ])
        let inserted = db.changesCount > 0
        if inserted {
            for m in parsed.media {
                try db.execute(
                    sql: """
                        INSERT OR IGNORE INTO tweet_media (tweet_id, idx, source_url, ext, origin)
                        VALUES (?, ?, ?, ?, ?)
                        """,
                    arguments: [m.tweetId, m.index, m.sourceUrl, m.ext, m.origin.rawValue])
            }
        }
        return inserted
    }
}

/// バイト位置を追跡しながら UTF-8 の \n 終端行を読む。末尾の不完全行 (\n なし) は
/// 読まずに終了する — fetcher が並行追記中でも完結行だけを取り込むため。
final class ByteLineReader {
    private static let chunkSize = 64 * 1024

    private let handle: FileHandle
    private var buffer = Data()
    private var bufPos = 0
    private var position: Int64
    private var pending = Data()

    init(handle: FileHandle, position: Int64) {
        self.handle = handle
        self.position = position
    }

    /// 1 行読む。text は改行を除いた文字列、lineOffset/lineLength は改行を含まない
    /// バイト範囲、endOffset は改行の次のバイト位置 (= 次回再開オフセット)。
    func readLine() -> (text: String, lineOffset: Int64, lineLength: Int64, endOffset: Int64)? {
        let lineStart = position - Int64(pending.count)
        while true {
            if bufPos >= buffer.count {
                guard let chunk = try? handle.read(upToCount: Self.chunkSize),
                      !chunk.isEmpty
                else {
                    // 末尾の不完全行: 取り込まない (pending は保持し次回に備える)
                    return nil
                }
                buffer = chunk
                bufPos = 0
            }

            if let nlIndex = buffer[(buffer.startIndex + bufPos)...].firstIndex(of: UInt8(ascii: "\n")) {
                let nl = nlIndex - buffer.startIndex
                pending.append(buffer[(buffer.startIndex + bufPos)..<nlIndex])
                position += Int64(nl - bufPos + 1)   // +1 = \n
                bufPos = nl + 1

                let bytes = pending
                pending = Data()

                let lineLength = Int64(bytes.count)
                // CR を除去 (CRLF 保険)
                var text = String(decoding: bytes, as: UTF8.self)
                while text.hasSuffix("\r") { text.removeLast() }
                return (text, lineStart, lineLength, position)
            }

            pending.append(buffer[(buffer.startIndex + bufPos)...])
            position += Int64(buffer.count - bufPos)
            bufPos = buffer.count
        }
    }
}
