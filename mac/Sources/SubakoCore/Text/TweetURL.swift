import Foundation

/// 「ブラウザで開く」の URL 規則 (docs/viewer-features.md §5.2)。
/// タイムラインと画像ビューアの両方がこの 1 実装を使うこと
/// (Windows 版は画像ビューアが独自に URL を組んでいて検索バケットで壊れる既知の課題があった)。
public enum TweetURL {
    /// author を特定できない場合 (author が無くアーカイブ名がバケット ID の場合) は
    /// /i/web/status/ フォールバック。
    public static func status(author: String?, tweetId: String) -> URL {
        // ホスト固定 + パス連結の URL 文字列は構造上妥当なため force unwrap を許容
        if let author, !author.isEmpty {
            // swiftlint:disable:next force_unwrapping
            return URL(string: "https://x.com/\(author)/status/\(tweetId)")!
        }
        // swiftlint:disable:next force_unwrapping
        return URL(string: "https://x.com/i/web/status/\(tweetId)")!
    }

    /// 行の情報から投稿者を決める: author_username → (アーカイブ名がバケット ID でなければ)
    /// アーカイブ名 → nil。RT の場合の作者差し替えは呼び出し側 (表示側) の責務。
    public static func status(row: TweetRow) -> URL {
        let author = row.authorUsername
            ?? (SearchBuckets.isBucketId(row.username) ? nil : row.username)
        return status(author: author, tweetId: row.tweetId)
    }
}

/// 検索バケット ID (`searches/<slug>`) の判定 (docs/mac-port-notes.md §4)。
public enum SearchBuckets {
    public static let prefix = "searches/"

    public static func isBucketId(_ username: String) -> Bool {
        username.lowercased().hasPrefix(prefix)
    }

    public static func bucketId(slug: String) -> String {
        prefix + slug
    }
}
