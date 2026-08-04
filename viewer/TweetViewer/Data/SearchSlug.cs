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
        var baseName = TruncateToRunes(UnsafeCharsRegex().Replace(query, "_").Trim('_'), 40);
        if (baseName.Length == 0)
            baseName = "search";
        var hash = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(query)))[..8];
        return $"{baseName}-{hash}";
    }

    /// <summary>
    /// 先頭 maxRunes 個のコードポイントまで切り詰める。
    /// Python (str のスライス) / Swift (unicodeScalars) は**コードポイント単位**なので、
    /// C# の string.Length (UTF-16 コードユニット) で切ると絵文字などの非 BMP 文字を
    /// 含むクエリで別のフォルダ名が生成されてしまう。
    /// </summary>
    private static string TruncateToRunes(string value, int maxRunes)
    {
        var utf16Length = 0;
        var taken = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (taken == maxRunes)
                return value[..utf16Length];   // サロゲートペアを割らない境界
            utf16Length += rune.Utf16SequenceLength;
            taken++;
        }
        return value;
    }
}
