using System.Diagnostics;
using System.IO;
using System.Text;

namespace TweetViewer.Services;

public enum FetchMode
{
    Update,
    Backfill,
    /// <summary>キーワード検索の初回取得。username は検索バケット ID (searches/&lt;slug&gt;)。</summary>
    Search,
    /// <summary>検索の最新差分 (--update)。</summary>
    SearchUpdate,
    /// <summary>検索の過去期間補完 (--backfill + --backfill-since)。</summary>
    SearchBackfill,
    /// <summary>API を使わず保存済み JSONL から未取得画像だけ補完 (--images-only)。</summary>
    ImagesOnly,
}

public sealed record FetchResult(int ExitCode, bool Cancelled);

/// <summary>
/// Python fetcher (main.py) をサブプロセス実行する。ログは stderr(logging)に
/// 出るため両ストリームを捕捉。日本語ログの文字化け対策として子プロセスを
/// UTF-8 に固定する。
/// </summary>
public sealed class FetchProcessService
{
    private readonly AppSettings _settings;

    public FetchProcessService(AppSettings settings) => _settings = settings;

    public async Task<FetchResult> RunAsync(
        string username, FetchMode mode, int? maxRequests, IProgress<string> log, CancellationToken ct,
        string? searchQuery = null, string? backfillSince = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ResolvePython(),
            WorkingDirectory = _settings.RepoDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("main.py");
        var isSearch = mode is FetchMode.Search or FetchMode.SearchUpdate or FetchMode.SearchBackfill;
        if (mode == FetchMode.ImagesOnly && username.StartsWith("searches/", StringComparison.Ordinal))
        {
            // 画像のみ取得は API を使わないのでクエリ不要。バケットは slug だけ渡す
            psi.ArgumentList.Add("--search-name");
            psi.ArgumentList.Add(username["searches/".Length..]);
        }
        else if (!isSearch)
        {
            psi.ArgumentList.Add(username);
        }
        // 共有データフォルダにも対応するため保存先を常に明示する
        psi.ArgumentList.Add("--output-dir");
        psi.ArgumentList.Add(_settings.EffectiveDataDir);
        if (isSearch)
        {
            // username は "searches/<slug>" — main.py には slug のみ渡す
            psi.ArgumentList.Add("--search");
            psi.ArgumentList.Add(searchQuery
                ?? throw new ArgumentNullException(nameof(searchQuery)));
            psi.ArgumentList.Add("--search-name");
            psi.ArgumentList.Add(username["searches/".Length..]);
        }
        switch (mode)
        {
            case FetchMode.Update:
            case FetchMode.SearchUpdate:
                psi.ArgumentList.Add("--update");
                break;
            case FetchMode.Backfill:
                psi.ArgumentList.Add("--backfill");
                break;
            case FetchMode.SearchBackfill:
                psi.ArgumentList.Add("--backfill");
                if (backfillSince is { Length: > 0 })
                {
                    psi.ArgumentList.Add("--backfill-since");
                    psi.ArgumentList.Add(backfillSince);
                }
                break;
            case FetchMode.ImagesOnly:
                psi.ArgumentList.Add("--images-only");
                break;
        }
        if (maxRequests is { } limit)
        {
            psi.ArgumentList.Add("--max-requests");
            psi.ArgumentList.Add(limit.ToString());
        }
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) log.Report(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) log.Report(e.Data); };

        log.Report($"> {psi.FileName} {string.Join(' ', psi.ArgumentList)}");
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var cancelled = false;
        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        // WaitForExitAsync は非同期読み取りの完了までは保証しないため、引数なしの
        // WaitForExit で出力の取りこぼしを防ぐ (最終行の消費リクエスト数を確実に届ける)。
        // プロセスは既に終了しているので待ちはごく短いが、UI を止めないよう別スレッドで待つ
        await Task.Run(process.WaitForExit).ConfigureAwait(false);
        return new FetchResult(process.ExitCode, cancelled);
    }

    private string ResolvePython()
    {
        if (!string.IsNullOrWhiteSpace(_settings.PythonPath) && _settings.PythonPath != "python")
            return _settings.PythonPath;
        // PATH に python が無い環境では py ランチャーへフォールバック
        var fromPath = Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator)
            .Select(dir => Path.Combine(dir.Trim(), "python.exe"))
            .FirstOrDefault(File.Exists);
        return fromPath is not null ? "python" : "py";
    }
}
