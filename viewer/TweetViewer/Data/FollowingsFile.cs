using System.IO;
using System.Text.Json;

namespace TweetViewer.Data;

/// <summary>
/// data/_followings/&lt;source&gt;.jsonl — フォロー中アカウントの一覧。
/// 1 行 = /follows が返した User オブジェクトの生データで、fetcher が
/// 実行ごとに全書き換えする (docs/data-layer.md §1.7)。
/// アーカイブではないため users には登録されず、消しても構わない中間ファイル。
/// </summary>
public static class FollowingsFile
{
    public const string DirName = "_followings";

    public sealed record Entry(string Username, string? DisplayName);

    public static string PathFor(string dataDir, string sourceUsername) =>
        Path.Combine(dataDir, DirName, sourceUsername + ".jsonl");

    /// <summary>
    /// 読めた行だけを <b>ファイル内の順序のまま</b> 返す。username を持たない行・
    /// 壊れた行はスキップし (JsonlImporter と同流儀)、username は大文字小文字
    /// 無視で重複排除する。ファイルが無ければ空リスト。
    /// </summary>
    public static List<Entry> Read(string dataDir, string sourceUsername)
    {
        var list = new List<Entry>();
        var path = PathFor(dataDir, sourceUsername);
        if (!File.Exists(path))
            return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0)
                continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (GetString(doc.RootElement, "username") is not { } raw)
                    continue;
                var name = raw.TrimStart('@').Trim();
                if (name.Length == 0 || !seen.Add(name))
                    continue;
                list.Add(new Entry(name, GetString(doc.RootElement, "display_name")));
            }
            catch (JsonException)
            {
                // 壊れ行はスキップして続行
            }
        }
        return list;

        static string? GetString(JsonElement root, string key) =>
            root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String &&
            v.GetString() is { Length: > 0 } s ? s : null;
    }

    /// <summary>取得済み件数 (未取得なら 0)。確認ダイアログの件数表示用。</summary>
    public static int Count(string dataDir, string sourceUsername) =>
        Read(dataDir, sourceUsername).Count;
}
