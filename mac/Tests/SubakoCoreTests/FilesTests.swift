import Foundation
import Testing
@testable import SubakoCore

/// テスト用の一時ディレクトリ。
func makeTempDir() throws -> String {
    let dir = NSTemporaryDirectory() + "SubakoTests/" + UUID().uuidString
    try FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)
    return dir
}

/// data/_followings/<source>.jsonl の読み取り (C# FollowingsFileTests.cs の移植)。
@Suite struct FollowingsFileTests {
    private func writeFollowings(_ dataDir: String, _ source: String, _ lines: [String]) throws {
        let path = FollowingsFile.path(dataDir: dataDir, sourceUsername: source)
        try FileManager.default.createDirectory(
            atPath: (path as NSString).deletingLastPathComponent,
            withIntermediateDirectories: true)
        try lines.map { $0 + "\n" }.joined()
            .write(toFile: path, atomically: true, encoding: .utf8)
    }

    private func userLine(_ username: String, _ displayName: String? = nil) -> String {
        displayName == nil
            ? #"{"id":"1","username":"\#(username)"}"#
            : #"{"id":"1","username":"\#(username)","display_name":"\#(displayName!)"}"#
    }

    @Test func ファイル順を保ちつつ壊れ行と重複を落とす() throws {
        let dataDir = try makeTempDir()
        defer { try? FileManager.default.removeItem(atPath: dataDir) }
        try writeFollowings(dataDir, "src", [
            userLine("alice", "Alice"),
            userLine("@bob"),                          // @ 付きは剥がす
            "{壊れた JSON",                             // スキップ
            #"{"id":"9","display_name":"名前だけ"}"#,    // username 無しはスキップ
            "",                                         // 空行はスキップ
            userLine("ALICE"),                          // 大文字小文字無視で重複
            userLine("carol", "Carol"),
        ])

        let entries = FollowingsFile.read(dataDir: dataDir, sourceUsername: "src")

        #expect(entries.map(\.username) == ["alice", "bob", "carol"])
        #expect(entries[0].displayName == "Alice")
        #expect(entries[1].displayName == nil)
        #expect(FollowingsFile.count(dataDir: dataDir, sourceUsername: "src") == 3)
    }

    @Test func ファイルが無ければ空リスト() throws {
        let dataDir = try makeTempDir()
        defer { try? FileManager.default.removeItem(atPath: dataDir) }
        #expect(FollowingsFile.read(dataDir: dataDir, sourceUsername: "unknown").isEmpty)
        #expect(FollowingsFile.count(dataDir: dataDir, sourceUsername: "unknown") == 0)
    }

    @Test func 空文字のusernameは落とす() throws {
        let dataDir = try makeTempDir()
        defer { try? FileManager.default.removeItem(atPath: dataDir) }
        try writeFollowings(dataDir, "src", [userLine(""), userLine("@"), userLine("dave")])
        #expect(FollowingsFile.read(dataDir: dataDir, sourceUsername: "src")
            .map(\.username) == ["dave"])
    }

    @Test func pathは_followings配下を指す() throws {
        let dataDir = try makeTempDir()
        defer { try? FileManager.default.removeItem(atPath: dataDir) }
        #expect(FollowingsFile.path(dataDir: dataDir, sourceUsername: "alice")
            == dataDir + "/" + FollowingsFile.dirName + "/alice.jsonl")
    }
}

/// search.json の read-modify-write (docs/data-layer.md §1.5)。
@Suite struct SearchMetadataTests {
    @Test func 読み書きと他キー保持() throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(atPath: dir) }

        // fetcher が作った体の search.json (未知キー extra 付き)
        try #"{"query":"old query","created_at":"2026-01-01T00:00:00Z","extra":123}"#
            .write(toFile: dir + "/search.json", atomically: true, encoding: .utf8)

        let info = try #require(SearchMetadata.tryRead(bucketDir: dir))
        #expect(info.query == "old query")
        #expect(info.name == nil)
        #expect(info.createdAt == "2026-01-01T00:00:00Z")

        try SearchMetadata.write(bucketDir: dir, query: "new query", name: "表示名")
        let updated = try #require(SearchMetadata.tryRead(bucketDir: dir))
        #expect(updated.query == "new query")
        #expect(updated.name == "表示名")
        #expect(updated.createdAt == "2026-01-01T00:00:00Z")   // 既存キー保持

        // 未知キーも保持される (read-modify-write の契約)
        let data = try #require(FileManager.default.contents(atPath: dir + "/search.json"))
        let root = try #require(try JSONSerialization.jsonObject(with: data) as? [String: Any])
        #expect(root["extra"] as? Int == 123)

        // name = nil はキー削除
        try SearchMetadata.write(bucketDir: dir, query: "new query", name: nil)
        #expect(SearchMetadata.tryRead(bucketDir: dir)?.name == nil)
    }

    @Test func 破損や欠落はnil() throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(atPath: dir) }
        #expect(SearchMetadata.tryRead(bucketDir: dir) == nil)   // ファイルなし

        try "{broken".write(toFile: dir + "/search.json", atomically: true, encoding: .utf8)
        #expect(SearchMetadata.tryRead(bucketDir: dir) == nil)   // 破損

        try #"{"name":"x"}"#.write(toFile: dir + "/search.json", atomically: true, encoding: .utf8)
        #expect(SearchMetadata.tryRead(bucketDir: dir) == nil)   // query 無し
    }

    @Test func 新規作成はcreated_atを付与() throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(atPath: dir) }
        try SearchMetadata.write(bucketDir: dir, query: "q", name: nil)
        let info = try #require(SearchMetadata.tryRead(bucketDir: dir))
        #expect(info.query == "q")
        #expect(info.createdAt != nil)
    }
}

/// images/<tweet_id>_<idx>.<ext> の解決 (docs/viewer-features.md §11.2)。
@Suite struct LocalMediaFilesTests {
    @Test func 期待パス優先_なければ拡張子フォールバック探索() throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(atPath: dir) }
        FileManager.default.createFile(atPath: dir + "/100_1.png", contents: Data([0]))

        // 期待 ext は jpg だが実ファイルは png → フォールバックで見つける
        #expect(LocalMediaFiles.resolve(imagesDir: dir, tweetId: "100", index: 1, ext: "jpg")
            == dir + "/100_1.png")
        // 実ファイルが無ければ nil (壊れ枠を見せない)
        #expect(LocalMediaFiles.resolve(imagesDir: dir, tweetId: "100", index: 2, ext: "jpg") == nil)
    }
}

/// アイコンキャッシュのファイル名 (共有契約 #7)。
@Suite struct IconCacheKeyTests {
    @Test func sha1小文字hexと拡張子規則() {
        // sha1("https://pbs.twimg.com/profile_images/1/a_normal.jpg")
        let name = IconCacheKey.fileName(for: "https://pbs.twimg.com/profile_images/1/a_normal.jpg")
        #expect(name.hasSuffix(".jpg"))
        let hash = String(name.dropLast(4))
        #expect(hash.count == 40)
        #expect(hash == hash.lowercased())
        #expect(hash.allSatisfy { $0.isHexDigit })
        // 同一 URL は常に同一ファイル名 (決定的)
        #expect(name == IconCacheKey.fileName(for: "https://pbs.twimg.com/profile_images/1/a_normal.jpg"))
    }

    @Test func biggerVariantは_normalを置換() {
        #expect(IconCacheKey.biggerVariant(of: "https://pbs.twimg.com/profile_images/1/a_normal.jpg")
            == "https://pbs.twimg.com/profile_images/1/a_bigger.jpg")
        #expect(IconCacheKey.biggerVariant(of: "https://pbs.twimg.com/profile_images/1/a_normal")
            == "https://pbs.twimg.com/profile_images/1/a_bigger")
        // _normal を含まない URL はそのまま
        #expect(IconCacheKey.biggerVariant(of: "https://example.com/icon.png")
            == "https://example.com/icon.png")
    }
}

/// 「ブラウザで開く」URL 規則 (docs/viewer-features.md §5.2 / §6.2 の既知課題回避)。
@Suite struct TweetURLTests {
    @Test func author指定ありは通常URL() {
        #expect(TweetURL.status(author: "alice", tweetId: "100").absoluteString
            == "https://x.com/alice/status/100")
    }

    @Test func author不明はiWebStatusフォールバック() {
        #expect(TweetURL.status(author: nil, tweetId: "100").absoluteString
            == "https://x.com/i/web/status/100")
        #expect(TweetURL.status(author: "", tweetId: "100").absoluteString
            == "https://x.com/i/web/status/100")
    }

    @Test func 検索バケット行はアーカイブ名を作者にしない() {
        // Windows 版の画像ビューアのバグ (searches/<slug> を作者名として URL に埋める) の回避
        let bucketRow = TweetRow(tweetId: "1", idInt: 1, username: "searches/cat-12345678")
        #expect(TweetURL.status(row: bucketRow).absoluteString == "https://x.com/i/web/status/1")

        let bucketRowWithAuthor = TweetRow(
            tweetId: "1", idInt: 1, username: "searches/cat-12345678", authorUsername: "alice")
        #expect(TweetURL.status(row: bucketRowWithAuthor).absoluteString
            == "https://x.com/alice/status/1")

        let userRow = TweetRow(tweetId: "2", idInt: 2, username: "bob")
        #expect(TweetURL.status(row: userRow).absoluteString == "https://x.com/bob/status/2")
    }
}

/// ユーザー名検証 (ASCII 限定 — Python 側の規則)。
@Suite struct UsernameRulesTests {
    @Test func ASCII英数とアンダースコアのみ許可() {
        #expect(UsernameRules.isValid("alice_123"))
        #expect(!UsernameRules.isValid(""))
        #expect(!UsernameRules.isValid("日本語"))       // Windows 版は通してしまう実装差
        #expect(!UsernameRules.isValid("Ａｌｉｃｅ"))   // 全角英数も拒否
        #expect(!UsernameRules.isValid("a b"))
        #expect(!UsernameRules.isValid("a-b"))
    }

    @Test func normalizeは空白と先頭アットを除去() {
        #expect(UsernameRules.normalize(" @alice ") == "alice")
        #expect(UsernameRules.normalize("@@alice") == "alice")
        #expect(UsernameRules.normalize("alice") == "alice")
    }
}
