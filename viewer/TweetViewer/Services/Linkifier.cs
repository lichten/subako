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

    /// <summary>
    /// 本文中の動画リンクと、そのサムネイル URL。
    /// ニコニコは番号からサムネイル URL を決定できないため (NicoThumbnail 参照)、
    /// ThumbnailUrl はキャッシュのキー兼フォールバックで、実 URL は
    /// <paramref name="NicoVideoNumber"/> を使って取得時に解決する。
    /// </summary>
    public sealed record VideoLink(string PageUrl, string ThumbnailUrl, string? NicoVideoNumber = null);

    // 動画 ID の後ろに続けて拾う文字 (クエリやフラグメント)。日本語が混じらないよう
    // ASCII のみを明示する (\w は .NET だと日本語にもマッチしてしまう)
    private const string UrlTail = @"[A-Za-z0-9_\-=&%.:/?#!$'()*+,;@~\[\]]*";

    // 11 文字の動画 ID を持つ YouTube の各種 URL 形式
    [GeneratedRegex(@"^https?://(?:www\.|m\.)?youtube\.com/(?:watch\?(?:[^&\s]*&)*v=|shorts/|live/|embed/)([A-Za-z0-9_\-]{11})(?![A-Za-z0-9_\-])" + UrlTail)]
    private static partial Regex YouTubeLongRegex();

    [GeneratedRegex(@"^https?://youtu\.be/([A-Za-z0-9_\-]{11})(?![A-Za-z0-9_\-])" + UrlTail)]
    private static partial Regex YouTubeShortRegex();

    // ニコニコ動画 (nico.ms は短縮 URL)
    [GeneratedRegex(@"^https?://(?:www\.)?nicovideo\.jp/watch/(?:sm|nm|so)(\d+)" + UrlTail)]
    private static partial Regex NicoWatchRegex();

    [GeneratedRegex(@"^https?://nico\.ms/(?:sm|nm|so)(\d+)" + UrlTail)]
    private static partial Regex NicoShortRegex();

    /// <summary>
    /// 本文から動画リンクを出現順に抽出する。サムネイル URL は API を使わず
    /// 規則で組み立てる (docs/data-layer.md §3.6)。同一サムネイルは先勝ちで 1 件。
    /// </summary>
    public static IReadOnlyList<VideoLink> ExtractVideoLinks(string? text)
    {
        var result = new List<VideoLink>();
        if (string.IsNullOrEmpty(text))
            return result;

        var seen = new HashSet<string>();
        foreach (var segment in Split(text))
        {
            if (!segment.IsUrl || MatchVideoLink(segment.Text) is not { } link)
                continue;
            if (seen.Add(link.ThumbnailUrl))
                result.Add(link);
        }
        return result;
    }

    /// <summary>
    /// URL セグメントが動画リンクなら VideoLink を返す。
    /// Split のセグメントは「https://youtu.be/xxx。おすすめ」のように後続の日本語を
    /// 含みうる (約物除去は末尾が空白のときだけ効く) ため、ページ URL は
    /// 正規表現がマッチした範囲から作る。
    /// </summary>
    private static VideoLink? MatchVideoLink(string url)
    {
        if (YouTubeLongRegex().Match(url) is { Success: true } y1)
            return YouTubeLink(y1);
        if (YouTubeShortRegex().Match(url) is { Success: true } y2)
            return YouTubeLink(y2);
        // ニコニコは数字部だけを使う (sm/nm/so の別はサムネイル URL に現れない)
        if (NicoWatchRegex().Match(url) is { Success: true } n1)
            return NicoLink(n1);
        if (NicoShortRegex().Match(url) is { Success: true } n2)
            return NicoLink(n2);
        return null;
    }

    private static VideoLink YouTubeLink(Match m) => new(
        TrimTrailingPunctuation(m.Value),
        $"https://i.ytimg.com/vi/{m.Groups[1].Value}/hqdefault.jpg");

    private static VideoLink NicoLink(Match m) => new(
        TrimTrailingPunctuation(m.Value),
        $"https://nicovideo.cdn.nimg.jp/thumbnails/{m.Groups[1].Value}/{m.Groups[1].Value}",
        m.Groups[1].Value);
}
