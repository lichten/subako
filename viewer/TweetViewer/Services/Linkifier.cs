using System.Text.RegularExpressions;

namespace TweetViewer.Services;

/// <summary>本文テキストを URL / 非 URL のセグメントに分割する純ロジック。</summary>
public static partial class Linkifier
{
    public sealed record Segment(string Text, bool IsUrl);

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex UrlRegex();

    // URL 末尾から取り除く約物 (日本語文中の「〜だ https://... 。」対策)
    private const string TrailingPunctuation = ".,;:!?)]}>»」』】〉》。、！？…";

    public static IReadOnlyList<Segment> Split(string text)
    {
        var segments = new List<Segment>();
        if (string.IsNullOrEmpty(text))
            return segments;

        var pos = 0;
        foreach (Match m in UrlRegex().Matches(text))
        {
            var url = TrimTrailingPunctuation(m.Value);
            if (url.Length <= "https://".Length)
                continue;   // スキーム断片のみはリンク化しない

            if (m.Index > pos)
                segments.Add(new Segment(text[pos..m.Index], false));
            segments.Add(new Segment(url, true));
            pos = m.Index + url.Length;
        }
        if (pos < text.Length)
            segments.Add(new Segment(text[pos..], false));
        return segments;
    }

    private static string TrimTrailingPunctuation(string url)
    {
        var end = url.Length;
        while (end > 0 && TrailingPunctuation.Contains(url[end - 1]))
            end--;
        return url[..end];
    }
}
