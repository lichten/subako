using System.Net.Http;
using System.Text.RegularExpressions;

namespace TweetViewer.Services;

/// <summary>
/// ニコニコ動画のサムネイル URL 解決。CDN の URL は
/// `thumbnails/&lt;番号&gt;/&lt;番号&gt;` の形と `.../&lt;番号&gt;.&lt;別番号&gt;` の形があり
/// **番号だけからは決定できない** (新しい動画はサフィックス付き)。
/// そのため鍵不要の公開 API getthumbinfo で実 URL を引く。
/// 失敗時はサフィックスなしの URL を返して古い動画のケースを救う。
/// </summary>
public static partial class NicoThumbnail
{
    // getthumbinfo は User-Agent が無いと XML ではなく HTML ページを 200 で返すため必須
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.UserAgent);
        return client;
    }

    [GeneratedRegex(@"<thumbnail_url>([^<]+)</thumbnail_url>")]
    private static partial Regex ThumbnailUrlRegex();

    /// <summary>キャッシュミス時にだけ呼ばれる (ヒット時は API を叩かない)。</summary>
    public static async Task<string?> ResolveAsync(string videoNumber, string fallbackUrl)
    {
        try
        {
            using var resp = await Http
                .GetAsync($"https://ext.nicovideo.jp/api/getthumbinfo/sm{videoNumber}")
                .ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                var xml = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (ThumbnailUrlRegex().Match(xml) is { Success: true } m)
                    return m.Groups[1].Value;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // ネットワーク不調時はフォールバックに任せる
        }
        return fallbackUrl;
    }
}
