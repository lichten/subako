using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TweetViewer.Data;

/// <summary>
/// 検索バケット直下の search.json。書き手は fetcher (初回作成・クエリ同期) と
/// ビューア (クエリ変更・名称設定)。他キーを保持する read-modify-write で扱う。
/// </summary>
public static class SearchMetadata
{
    /// <summary>読めない・壊れている場合は null (呼び出し側でフォルダ名にフォールバック)。</summary>
    public static (string Query, string? Name, string? CreatedAt)? TryRead(string bucketDir)
    {
        var path = Path.Combine(bucketDir, "search.json");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("query", out var q) &&
                q.ValueKind == JsonValueKind.String && q.GetString() is { Length: > 0 } query)
            {
                return (query, GetString(doc.RootElement, "name"), GetString(doc.RootElement, "created_at"));
            }
        }
        catch (Exception)
        {
            // ファイルなし・破損はフォールバック
        }
        return null;

        static string? GetString(JsonElement root, string key) =>
            root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String &&
            v.GetString() is { Length: > 0 } s
                ? s
                : null;
    }

    /// <summary>
    /// query / name を差し替えて保存する。既存ファイルの他キー (created_at 等) は保持。
    /// name = null はキー削除 (名称未設定 = クエリ表示)。新規・破損時は作り直す。
    /// </summary>
    public static void Write(string bucketDir, string query, string? name)
    {
        var path = Path.Combine(bucketDir, "search.json");
        JsonObject root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
        }
        catch (Exception)
        {
            root = new JsonObject();
        }
        root["query"] = query;
        if (name is { Length: > 0 })
            root["name"] = name;
        else
            root.Remove("name");
        if (root["created_at"] is null)
            root["created_at"] = DateTime.UtcNow.ToString(
                "yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        Directory.CreateDirectory(bucketDir);
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }));
    }
}
