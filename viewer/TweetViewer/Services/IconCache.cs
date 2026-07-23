using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TweetViewer.Services;

/// <summary>
/// ユーザーアイコンのダウンロードとディスクキャッシュ。
/// キャッシュ先は data/icons/&lt;sha1(url)&gt;.&lt;ext&gt; (プラットフォームフリー、
/// 消しても再取得可能な派生データ — docs/data-layer.md 参照)。
/// </summary>
public sealed partial class IconCache
{
    [GeneratedRegex(@"_normal(\.\w+)?$")]
    private static partial Regex NormalSuffixRegex();

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly string _iconsDir;
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _inflight = new();
    private readonly ConcurrentDictionary<string, byte> _failed = new();   // セッション内ネガティブキャッシュ

    public IconCache(string dataDir)
    {
        _iconsDir = Path.Combine(dataDir, "icons");
        Directory.CreateDirectory(_iconsDir);
    }

    /// <summary>ローカルキャッシュのパスを返す。未取得ならダウンロード。失敗は null。</summary>
    public Task<string?> GetLocalPathAsync(string? url)
    {
        if (string.IsNullOrEmpty(url) || _failed.ContainsKey(url))
            return Task.FromResult<string?>(null);

        var path = CachePathFor(url);
        if (File.Exists(path))
            return Task.FromResult<string?>(path);

        // 同一 URL の並行要求は1ダウンロードに束ねる
        var lazy = _inflight.GetOrAdd(url, u => new Lazy<Task<string?>>(
            () => DownloadAsync(u, CachePathFor(u)),
            LazyThreadSafetyMode.ExecutionAndPublication));
        return lazy.Value;
    }

    private async Task<string?> DownloadAsync(string url, string path)
    {
        try
        {
            // _normal (48px) を _bigger (73px) に置換して取得。404 なら元 URL で再試行
            var bigger = NormalSuffixRegex().Replace(url, "_bigger$1");
            var bytes = await TryGetBytesAsync(bigger).ConfigureAwait(false)
                        ?? (bigger != url ? await TryGetBytesAsync(url).ConfigureAwait(false) : null);
            if (bytes is null)
            {
                _failed.TryAdd(url, 0);
                return null;
            }

            var tmp = path + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true);
            return path;
        }
        catch (Exception)
        {
            _failed.TryAdd(url, 0);
            return null;
        }
        finally
        {
            _inflight.TryRemove(url, out _);
        }
    }

    private static async Task<byte[]?> TryGetBytesAsync(string url)
    {
        try
        {
            using var resp = await Http.GetAsync(url).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;
            return await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    private string CachePathFor(string url)
    {
        var hash = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(url)));
        var ext = Data.TweetJsonParser.ExtOf(url);
        return Path.Combine(_iconsDir, $"{hash}.{ext}");
    }
}
