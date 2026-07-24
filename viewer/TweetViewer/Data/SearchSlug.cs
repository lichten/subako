using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TweetViewer.Data;

/// <summary>
/// 検索クエリ → ファイルシステム安全なバケット名。
/// Python 側 (sorsa_fetcher/fetcher.py の slugify_query) と同一規則を保つこと:
/// 不正文字と空白の連続を "_" に置換 → 前後の "_" を除去 → 40字に切詰め →
/// "-" + クエリ原文 UTF-8 の SHA1 先頭8hex。
/// </summary>
public static partial class SearchSlug
{
    [GeneratedRegex("""[\\/:*?"<>|\s]+""")]
    private static partial Regex UnsafeCharsRegex();

    public static string From(string query)
    {
        var baseName = UnsafeCharsRegex().Replace(query, "_").Trim('_');
        if (baseName.Length > 40)
            baseName = baseName[..40];
        if (baseName.Length == 0)
            baseName = "search";
        var hash = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(query)))[..8];
        return $"{baseName}-{hash}";
    }
}
