import Foundation
import SubakoCore

// 実データフォルダとの疎通確認 CLI (docs/mac-port-notes.md §6.3)。
// 使い方: subako-smoke <データフォルダ> [--import]
//   既定: viewer.db を読み取り専用 (mode=ro&immutable=1) で開き、ユーザー別件数を表示する。
//   Windows 側ビューアは閉じた状態で実行すること。
//   --import: 書込モードで開き、全アーカイブの JSONL 差分取込まで実行する
//   (コピーしたデータフォルダでの検証用)。

@main
struct Smoke {
    static func fail(_ message: String) -> Never {
        FileHandle.standardError.write(Data((message + "\n").utf8))
        exit(1)
    }

    static func main() async {
        let args = CommandLine.arguments
        guard args.count >= 2 else {
            fail("usage: subako-smoke <data-dir> [--import]")
        }
        let dataDir = args[1]
        let doImport = args.contains("--import")

        do {
            let db = try ViewerDatabase(dataDir: dataDir, readOnly: !doImport)
            print("mode: \(doImport ? "read-write" : "read-only (mode=ro&immutable=1)")")

            let users = UserRepository(db)
            if doImport {
                let dirs = try await users.registerExistingDataDirs()
                let searches = try await users.registerExistingSearchDirs()
                print("自動登録: ユーザー \(dirs) 件 / 検索 \(searches) 件")
                let importer = JsonlImporter(db)
                for user in try await users.getAll() + users.getSearchBuckets() {
                    let result = try await importer.importUser(user.username)
                    if result.newTweets > 0 || result.skippedLines > 0 {
                        print("取込 \(user.username): 新規 \(result.newTweets) 件 / スキップ \(result.skippedLines) 行")
                    }
                }
            }

            let all = try await users.getAll()
            let buckets = try await users.getSearchBuckets()
            print("--- ユーザー (\(all.count)) ---")
            for u in all {
                print("\(u.username)\t\(u.tweetCount) 件 (未読 \(u.unreadCount))")
            }
            print("--- 検索バケット (\(buckets.count)) ---")
            for b in buckets {
                let label = SearchMetadata.tryRead(
                    bucketDir: db.userDir(b.username)).map { $0.name ?? $0.query }
                print("\(b.username)\t\(b.tweetCount) 件 (未読 \(b.unreadCount))\t\(label ?? "")")
            }

            let repo = TweetRepository(db)
            let usernames = (all + buckets).map(\.username)
            if let bounds = try await repo.getDateBounds(usernames: usernames) {
                print("sort_key 範囲: \(bounds.min) 〜 \(bounds.max)")
                let page = try await repo.getPage(
                    usernames: usernames, unreadOnly: false, after: nil, limit: 3)
                print("--- 最新 3 件 ---")
                for row in page.rows {
                    let text = row.fullText.replacingOccurrences(of: "\n", with: " ").prefix(60)
                    print("[\(row.createdAtUtc)] @\(row.authorUsername ?? row.username): \(text)")
                }
            }
            db.checkpointAndClose()
        } catch {
            fail("エラー: \(error.localizedDescription)")
        }
    }
}
