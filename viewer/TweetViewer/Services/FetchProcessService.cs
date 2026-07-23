using System.Diagnostics;
using System.IO;
using System.Text;

namespace TweetViewer.Services;

public enum FetchMode
{
    Update,
    Backfill,
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
        string username, FetchMode mode, int? maxRequests, IProgress<string> log, CancellationToken ct)
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
        psi.ArgumentList.Add(username);
        // 共有データフォルダにも対応するため保存先を常に明示する
        psi.ArgumentList.Add("--output-dir");
        psi.ArgumentList.Add(_settings.EffectiveDataDir);
        switch (mode)
        {
            case FetchMode.Update:
                psi.ArgumentList.Add("--update");
                break;
            case FetchMode.Backfill:
                psi.ArgumentList.Add("--backfill");
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
