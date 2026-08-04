namespace TweetViewer.Services;

/// <summary>
/// 「ブラウザで開く」の URL 規則 (docs/viewer-features.md §5.2・§6.2)。
/// タイムラインと画像ビューアの両方がこの 1 実装を使うこと — 画像ビューアが
/// 独自にアーカイブ名から URL を組んでいたため、検索バケット由来の画像では
/// https://x.com/searches/&lt;slug&gt;/status/... という壊れた URL を開いていた
/// (docs/mac-port-notes.md §5)。Mac 版は SubakoCore/Text/TweetURL.swift。
/// </summary>
public static class TweetUrl
{
    /// <summary>検索バケット ID (users.username に入る "searches/&lt;slug&gt;") の接頭辞。</summary>
    public const string SearchBucketPrefix = "searches/";

    public static bool IsSearchBucket(string username) =>
        username.StartsWith(SearchBucketPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 行の情報から投稿者を決めて status URL を組む。
    /// author_username → (アーカイブ名がバケット ID でなければ) アーカイブ名 → 不明。
    /// 投稿者が不明なら X 側が id からリダイレクトする /i/web/status/ 形式にする。
    /// RT/引用でも X 側が id で正規ツイートへリダイレクトする。
    /// </summary>
    public static string Status(string tweetId, string username, string? authorUsername)
    {
        var author = authorUsername ?? (IsSearchBucket(username) ? null : username);
        return author is { Length: > 0 }
            ? $"https://x.com/{author}/status/{tweetId}"
            : $"https://x.com/i/web/status/{tweetId}";
    }
}
