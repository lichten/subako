using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TweetViewer.Models;

namespace TweetViewer.Data;

public sealed record ParsedTweet(TweetRow Row, IReadOnlyList<TweetMediaRow> Media);

/// <summary>
/// tweets.jsonl の1行を TweetRow + tweet_media 行に変換する純関数。
/// ID 抽出順・日時形式・画像抽出順は Python 側 (storage.tweet_id_of /
/// fetcher.parse_created_at / media.extract_photo_urls) と一致させること。
/// </summary>
public static partial class TweetJsonParser
{
    [GeneratedRegex(@"\.(\w{3,4})$")]
    private static partial Regex PathExtRegex();

    private static readonly string[] CreatedAtFormats =
    {
        "ddd MMM dd HH:mm:ss zzz yyyy",   // Wed Oct 10 20:19:24 +0000 2018
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFzzz",
        "yyyy-MM-dd HH:mm:sszzz",
    };

    private static readonly string[] PhotoTypes = { "photo", "image" };
    private static readonly string[] UrlKeys =
        { "preview", "media_url_https", "media_url", "url", "link", "expanded_url" };
    private static readonly string[] NestedKeys =
        { "quoted_status", "retweeted_status", "quoted_tweet", "retweeted_tweet" };

    /// <summary>パース不能行は null(呼び出し側でスキップ集計)。</summary>
    public static ParsedTweet? Parse(string line, string username, long rawOffset, long rawLength)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var tweetId = TweetIdOf(root);
            if (tweetId is null)
                return null;

            long.TryParse(tweetId, NumberStyles.None, CultureInfo.InvariantCulture, out var idInt);

            var createdAt = ParseCreatedAt(GetString(root, "created_at") ?? GetString(root, "createdAt"));
            var sortKey = createdAt?.ToUnixTimeSeconds() ?? 0;
            var createdAtUtc = createdAt?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) ?? "";

            var rt = GetObject(root, "retweeted_status") ?? GetObject(root, "retweeted_tweet");
            var quoted = GetObject(root, "quoted_status") ?? GetObject(root, "quoted_tweet");
            var isReply = GetBool(root, "is_reply") || GetString(root, "in_reply_to_tweet_id") is not null;
            var isQuote = GetBool(root, "is_quote_status") || quoted is not null;

            var type = rt is not null ? TweetType.Retweet
                     : isReply ? TweetType.Reply
                     : isQuote ? TweetType.Quote
                     : TweetType.Tweet;

            var media = ExtractMedia(root, tweetId);

            var row = new TweetRow
            {
                TweetId = tweetId,
                IdInt = idInt,
                Username = username,
                CreatedAtUtc = createdAtUtc,
                SortKey = sortKey,
                Type = type,
                FullText = GetString(root, "full_text") ?? GetString(root, "text") ?? "",
                Lang = GetString(root, "lang"),
                InReplyToUsername = GetString(root, "in_reply_to_username"),
                RtUsername = rt is null ? null : GetString(GetObject(rt.Value, "user"), "username"),
                RtDisplayName = rt is null ? null : GetString(GetObject(rt.Value, "user"), "display_name"),
                RtText = rt is null ? null : GetString(rt.Value, "full_text") ?? GetString(rt.Value, "text"),
                QuotedUsername = quoted is null ? null : GetString(GetObject(quoted.Value, "user"), "username"),
                QuotedDisplayName = quoted is null ? null : GetString(GetObject(quoted.Value, "user"), "display_name"),
                QuotedText = quoted is null ? null : GetString(quoted.Value, "full_text") ?? GetString(quoted.Value, "text"),
                LikeCount = GetLong(root, "likes_count"),
                RetweetCount = GetLong(root, "retweet_count"),
                ReplyCount = GetLong(root, "reply_count"),
                ViewCount = GetLong(root, "view_count"),
                MediaCount = media.Count,
                RawOffset = rawOffset,
                RawLength = rawLength,
            };
            return new ParsedTweet(row, media);
        }
    }

    /// <summary>storage.tweet_id_of と同順: id_str → id → tweet_id。数値でも文字列化。</summary>
    public static string? TweetIdOf(JsonElement root)
    {
        foreach (var key in new[] { "id_str", "id", "tweet_id" })
        {
            if (root.TryGetProperty(key, out var v))
            {
                var s = v.ValueKind switch
                {
                    JsonValueKind.String => v.GetString(),
                    JsonValueKind.Number => v.GetRawText(),
                    _ => null,
                };
                if (!string.IsNullOrEmpty(s))
                    return s;
            }
        }
        return null;
    }

    /// <summary>fetcher.parse_created_at 相当(4形式 + ISO フォールバック)。</summary>
    public static DateTimeOffset? ParseCreatedAt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var text = value.Trim().Replace("Z", "+0000");
        // "+0000" 形式は .NET の zzz が受けないことがあるため ":" 挿入版も試す
        var normalized = NoColonOffsetRegex().Replace(text, "$1:$2");
        foreach (var candidate in new[] { text, normalized })
        {
            foreach (var fmt in CreatedAtFormats)
            {
                if (DateTimeOffset.TryParseExact(candidate, fmt, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var dto))
                    return dto;
            }
        }
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var fallback))
            return fallback;
        return null;
    }

    [GeneratedRegex(@"([+-]\d{2})(\d{2})(?=\s|$)")]
    private static partial Regex NoColonOffsetRegex();

    /// <summary>media.extract_photo_urls + to_original_size の再現。idx は1始まり。</summary>
    private static List<TweetMediaRow> ExtractMedia(JsonElement root, string tweetId)
    {
        var result = new List<TweetMediaRow>();
        var seen = new HashSet<string>();

        var targets = new List<JsonElement> { root };
        foreach (var key in NestedKeys)
        {
            if (GetObject(root, key) is { } nested)
                targets.Add(nested);
        }

        foreach (var target in targets)
        {
            foreach (var entry in IterMediaEntries(target))
            {
                var url = PickImageUrl(entry);
                if (url is null || !seen.Add(url))
                    continue;
                result.Add(new TweetMediaRow(tweetId, result.Count + 1, url, ExtOf(url)));
            }
        }
        return result;
    }

    private static IEnumerable<JsonElement> IterMediaEntries(JsonElement tweet)
    {
        if (tweet.TryGetProperty("entities", out var entities))
        {
            if (entities.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in entities.EnumerateArray())
                    if (IsPhotoEntry(entry))
                        yield return entry;
            }
            else if (entities.ValueKind == JsonValueKind.Object &&
                     entities.TryGetProperty("media", out var media) &&
                     media.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in media.EnumerateArray())
                    if (IsPhotoEntry(entry))
                        yield return entry;
            }
        }
        foreach (var key in new[] { "extended_entities", "extendedEntities" })
        {
            if (tweet.TryGetProperty(key, out var ext) &&
                ext.ValueKind == JsonValueKind.Object &&
                ext.TryGetProperty("media", out var media) &&
                media.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in media.EnumerateArray())
                    if (IsPhotoEntry(entry))
                        yield return entry;
            }
        }
    }

    private static bool IsPhotoEntry(JsonElement entry) =>
        entry.ValueKind == JsonValueKind.Object &&
        entry.TryGetProperty("type", out var t) &&
        t.ValueKind == JsonValueKind.String &&
        PhotoTypes.Contains(t.GetString());

    private static string? PickImageUrl(JsonElement entry)
    {
        var candidates = new List<string>();
        foreach (var key in UrlKeys)
        {
            if (entry.TryGetProperty(key, out var v) &&
                v.ValueKind == JsonValueKind.String &&
                v.GetString() is { Length: > 0 } s)
                candidates.Add(s);
        }
        foreach (var url in candidates)
            if (url.Contains("pbs.twimg.com"))
                return url;
        return candidates.Count > 0 ? candidates[0] : null;
    }

    /// <summary>media.to_original_size の拡張子規則(ローカルファイル名再現用)。</summary>
    public static string ExtOf(string url)
    {
        Uri? uri = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u : null;
        var path = uri?.AbsolutePath ?? url;
        var m = PathExtRegex().Match(path);
        if (uri is not null && uri.Host.Contains("pbs.twimg.com"))
        {
            if (m.Success)
                return m.Groups[1].Value;
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            return query.Get("format") is { Length: > 0 } f ? f : "jpg";
        }
        return m.Success ? m.Groups[1].Value : "jpg";
    }

    private static string? GetString(JsonElement? obj, string key) =>
        obj is { } o ? GetString(o, key) : null;

    private static string? GetString(JsonElement obj, string key) =>
        obj.ValueKind == JsonValueKind.Object &&
        obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static bool GetBool(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

    private static long GetLong(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number &&
        v.TryGetInt64(out var n) ? n : 0;

    private static JsonElement? GetObject(JsonElement obj, string key) =>
        obj.ValueKind == JsonValueKind.Object &&
        obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Object
            ? v
            : null;
}
