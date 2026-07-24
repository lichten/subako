using System.IO;
using System.Text.Json;

namespace TweetViewer.Data;

/// <summary>
/// 検索バケット直下の search.json (書き手は Python fetcher、ビューアは読取のみ)。
/// </summary>
public static class SearchMetadata
{
    /// <summary>読めない・壊れている場合は null (呼び出し側でフォルダ名にフォールバック)。</summary>
    public static (string Query, string? CreatedAt)? TryRead(string bucketDir)
    {
        var path = Path.Combine(bucketDir, "search.json");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("query", out var q) &&
                q.ValueKind == JsonValueKind.String && q.GetString() is { Length: > 0 } query)
            {
                var createdAt = doc.RootElement.TryGetProperty("created_at", out var c) &&
                                c.ValueKind == JsonValueKind.String
                    ? c.GetString()
                    : null;
                return (query, createdAt);
            }
        }
        catch (Exception)
        {
            // ファイルなし・破損はフォールバック
        }
        return null;
    }
}
