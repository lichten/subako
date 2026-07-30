import Foundation

/// 取得モード (docs/fetcher-cli.md §2 の 7 モード)。
public enum FetchMode: Sendable, Equatable {
    case update
    case backfill
    /// キーワード検索の初回取得。username は検索バケット ID (searches/<slug>)。
    case search
    /// 検索の最新差分 (--update)。
    case searchUpdate
    /// 検索の過去期間補完 (--backfill + --backfill-since)。
    case searchBackfill
    /// API を使わず保存済み JSONL から未取得画像だけ補完 (--images-only)。
    case imagesOnly
    /// フォロー中一覧の取得 (--followings)。ツイートは取得しない。
    case followings

    public var isSearch: Bool {
        switch self {
        case .search, .searchUpdate, .searchBackfill: return true
        default: return false
        }
    }
}

/// main.py へ渡す引数列の生成 (C# FetchProcessService の引数構築部の移植)。
/// 純関数として切り出しテスト可能にする。
public enum FetchArguments {
    public static func build(
        username: String, mode: FetchMode, dataDir: String,
        maxRequests: Int?, searchQuery: String? = nil, backfillSince: String? = nil
    ) -> [String] {
        var args = ["main.py"]
        if mode == .imagesOnly, SearchBuckets.isBucketId(username) {
            // 画像のみ取得は API を使わないのでクエリ不要。バケットは slug だけ渡す
            args.append("--search-name")
            args.append(String(username.dropFirst(SearchBuckets.prefix.count)))
        } else if !mode.isSearch {
            args.append(username)
        }
        // 共有データフォルダにも対応するため保存先を常に明示する (fetcher-cli.md §2)
        args.append("--output-dir")
        args.append(dataDir)
        if mode.isSearch {
            // username は "searches/<slug>" — main.py には slug のみ渡す
            args.append("--search")
            args.append(searchQuery ?? "")
            args.append("--search-name")
            args.append(String(username.dropFirst(SearchBuckets.prefix.count)))
        }
        switch mode {
        case .update, .searchUpdate:
            args.append("--update")
        case .backfill:
            args.append("--backfill")
        case .searchBackfill:
            args.append("--backfill")
            if let backfillSince, !backfillSince.isEmpty {
                args.append("--backfill-since")
                args.append(backfillSince)
            }
        case .imagesOnly:
            args.append("--images-only")
        case .followings:
            args.append("--followings")
        case .search:
            break
        }
        if let maxRequests {
            args.append("--max-requests")
            args.append(String(maxRequests))
        }
        return args
    }
}
