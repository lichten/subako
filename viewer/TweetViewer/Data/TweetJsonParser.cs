using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TweetViewer.Models;

namespace TweetViewer.Data;

public sealed record ParsedTweet(TweetRow Row, IReadOnlyList<TweetMediaRow> Media, string? AuthorIconUrl);

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

            var author = GetObject(root, "user");
            var row = new TweetRow
            {
                TweetId = tweetId,
                IdInt = idInt,
                Username = username,
                AuthorUsername = GetString(author, "username"),
                AuthorDisplayName = GetString(author, "display_name"),
                AuthorIconUrl = GetString(author, "profile_image_url"),
                CreatedAtUtc = createdAtUtc,
                SortKey = sortKey,
                Type = type,
                FullText = GetString(root, "full_text") ?? GetString(root, "text") ?? "",
                Lang = GetString(root, "lang"),
                InReplyToUsername = GetString(root, "in_reply_to_username"),
                RtUsername = rt is null ? null : GetString(GetObject(rt.Value, "user"), "username"),
                RtDisplayName = rt is null ? null : GetString(GetObject(rt.Value, "user"), "display_name"),
                RtText = rt is null ? null : GetString(rt.Value, "full_text") ?? GetString(rt.Value, "text"),
                RtIconUrl = rt is null ? null : GetString(GetObject(rt.Value, "user"), "profile_image_url"),
                QuotedUsername = quoted is null ? null : GetString(GetObject(quoted.Value, "user"), "username"),
                QuotedDisplayName = quoted is null ? null : GetString(GetObject(quoted.Value, "user"), "display_name"),
                QuotedText = quoted is null ? null : GetString(quoted.Value, "full_text") ?? GetString(quoted.Value, "text"),
                QuotedIconUrl = quoted is null ? null : GetString(GetObject(quoted.Value, "user"), "profile_image_url"),
                LikeCount = Count(root, rt, "likes_count"),
                RetweetCount = Count(root, rt, "retweet_count"),
                ReplyCount = Count(root, rt, "reply_count"),
                ViewCount = Count(root, rt, "view_count"),
                MediaCount = media.Count,
                RawOffset = rawOffset,
                RawLength = rawLength,
            };
            var authorIcon = GetString(GetObject(root, "user"), "profile_image_url");
            return new ParsedTweet(row, media, authorIcon);
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

        // 列挙順は media.py と同一 (本文 → 引用 → RT)。URL 重複は先勝ち=本文優先
        var targets = new List<(JsonElement Element, MediaOrigin Origin)> { (root, MediaOrigin.Own) };
        foreach (var key in NestedKeys)
        {
            if (GetObject(root, key) is { } nested)
                targets.Add((nested, key.StartsWith("quoted") ? MediaOrigin.Quoted : MediaOrigin.Retweeted));
        }

        foreach (var (target, origin) in targets)
        {
            var found = IterMediaEntries(target).Select(PickImageUrl).OfType<string>().ToList();
            if (found.Count == 0)
                found = MediaUrlsFromText(target);
            foreach (var url in found)
            {
                if (!seen.Add(url))
                    continue;
                result.Add(new TweetMediaRow(tweetId, result.Count + 1, url, ExtOf(url), origin));
            }
        }
        return result;
    }

    /// <summary>
    /// entities が空のときのフォールバック(media._media_urls_from_text と同一規則)。
    /// 入れ子の quoted_status / retweeted_status は API 実挙動として entities が常に空で、
    /// Sorsa が本文の t.co を展開済み URL にするためここから 1 枚目だけ拾える。
    /// </summary>
    private static List<string> MediaUrlsFromText(JsonElement tweet)
    {
        if (GetString(tweet, "full_text") is not { Length: > 0 } text)
            return [];
        return TextMediaRegex().Matches(text).Select(m => m.Value).ToList();
    }

    // 末尾の句読点を巻き込まないよう media キー・拡張子・クエリまでを厳密に区切る
    [GeneratedRegex(@"https?://pbs\.twimg\.com/media/[A-Za-z0-9_\-]+" +
                    @"(?:\.(?:jpg|jpeg|png|webp|gif))?(?:\?[A-Za-z0-9_=&%.\-]*)?")]
    private static partial Regex TextMediaRegex();

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

    /// <summary>
    /// カウントの取得。RT のときはトップレベルが 0 なら RT元にフォールバックする。
    /// 実データ (リツイート 1,496 件) の実測:
    /// いいね/ブックマーク/引用数は RT ラッパー側が常に 0 で RT元にしか無い。
    /// 逆に表示回数は RT元が常に 0 で外側にしか無く、RT 数は RT元が 0 の行がある。
    /// この 1 規則で 4 項目とも正しい方が採れる。返信数はどちらにも無い (API の欠損)。
    /// 引用 (quoted_status) にはフォールバックしない — 引用ツイートは自分の投稿なので
    /// 外側の値が正しく、0 も正当な値。
    /// </summary>
    private static long Count(JsonElement root, JsonElement? rt, string key)
    {
        var value = GetLong(root, key);
        if (value != 0 || rt is not { } source)
            return value;
        return GetLong(source, key);
    }

    private static long GetLong(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number &&
        v.TryGetInt64(out var n) ? n : 0;

    private static JsonElement? GetObject(JsonElement obj, string key) =>
        obj.ValueKind == JsonValueKind.Object &&
        obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Object
            ? v
            : null;
}
