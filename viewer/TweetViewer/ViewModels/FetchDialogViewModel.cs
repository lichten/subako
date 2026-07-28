using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TweetViewer.Services;

namespace TweetViewer.ViewModels;

/// <summary>取得対象 1 件分。検索は Query が必須 (Python 側はクエリ省略に対応していない)。</summary>
public sealed record FetchTarget(
    string Username, string Label, FetchMode Mode, string? Query = null, string? BackfillSince = null);

/// <summary>UpdateLogWindow 用: 取得サブプロセスのライブログ表示と中断。</summary>
public sealed partial class FetchDialogViewModel : ObservableObject
{
    private readonly FetchProcessService _service;
    private readonly Func<string, Task> _onCompleted;
    private readonly IReadOnlyList<FetchTarget> _targets;
    /// <summary>複数対象のときの総リクエスト上限 (null = 無制限)。</summary>
    private readonly int? _maxRequests;
    private readonly CancellationTokenSource _cts = new();

    public ObservableCollection<string> LogLines { get; } = new();

    [ObservableProperty]
    private string _windowTitle = "";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _resultText = "";

    /// <summary>
    /// 失敗・中断・上限到達で終わったか。完了後にログを自動表示するかの判定に使う。
    /// StartAsync 完了後に UI スレッドで 1 回読むだけなので INPC は不要。
    /// </summary>
    public bool HasIssues { get; private set; }

    /// <summary>単体取得 (従来の呼び出し)。</summary>
    public FetchDialogViewModel(
        FetchProcessService service, string username, Func<string, Task> onCompleted,
        FetchMode mode = FetchMode.Update, int? maxRequests = null, string? searchQuery = null,
        string? backfillSince = null)
        : this(service,
               [new FetchTarget(
                   username,
                   username.StartsWith("searches/", StringComparison.Ordinal)
                       ? $"検索「{username["searches/".Length..]}」"
                       : $"@{username}",
                   mode, searchQuery, backfillSince)],
               onCompleted, maxRequests)
    {
    }

    /// <summary>複数対象の一括取得。maxRequests は全対象の合計に対する上限。</summary>
    public FetchDialogViewModel(
        FetchProcessService service, IReadOnlyList<FetchTarget> targets,
        Func<string, Task> onCompleted, int? maxRequests)
    {
        _service = service;
        _targets = targets;
        _onCompleted = onCompleted;
        _maxRequests = maxRequests;
        WindowTitle = targets.Count == 1
            ? $"更新: {targets[0].Label}"
            : $"一括更新 ({targets.Count} 件)";
    }

    public async Task StartAsync()
    {
        IsRunning = true;
        try
        {
            if (_targets.Count == 1)
                await RunSingleAsync();
            else
                await RunBatchAsync();
        }
        catch (Exception ex)
        {
            HasIssues = true;
            ResultText = $"実行に失敗しました: {ex.Message}";
            LogLines.Add(ResultText);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task RunSingleAsync()
    {
        var target = _targets[0];
        var (result, _) = await RunTargetAsync(target, _maxRequests);
        (ResultText, HasIssues) = FetchOutcome.DescribeSingle(result, target.Mode);
        // 失敗・中断でもページ毎に保存済みのため取込は実行する
        await _onCompleted(target.Username);
    }

    /// <summary>
    /// 対象を順に更新する。総リクエスト上限に達したらそこで停止し、
    /// 個別の失敗は記録して次へ進む。
    /// </summary>
    private async Task RunBatchAsync()
    {
        var remaining = _maxRequests;
        var consumedTotal = 0;
        var succeeded = 0;
        var failed = new List<string>();
        string? stopReason = null;

        for (var i = 0; i < _targets.Count; i++)
        {
            if (remaining is <= 0)
            {
                stopReason = "リクエスト上限に達したため、残りは実行していません";
                break;
            }
            var target = _targets[i];
            WindowTitle = $"一括更新 ({i + 1}/{_targets.Count}) {target.Label}";
            LogLines.Add("");
            LogLines.Add($"===== [{i + 1}/{_targets.Count}] {target.Label} を更新中" +
                         (remaining is { } r ? $" (残りリクエスト {r})" : "") + " =====");

            var (result, consumed) = await RunTargetAsync(target, remaining);
            consumedTotal += consumed;
            if (remaining is { } before)
                remaining = before - consumed;
            LogLines.Add($"----- {target.Label}: 消費 {consumed} リクエスト " +
                         $"(累計 {consumedTotal}" + (_maxRequests is { } max ? $" / {max}" : "") + ")");

            // 失敗・中断でもページ毎に保存済みのため取込は実行する
            await _onCompleted(target.Username);

            if (result.Cancelled)
            {
                stopReason = "中断しました(ここまでの取得分は保存済み)";
                break;
            }
            if (result.ExitCode == FetchOutcome.BudgetExhaustedExitCode)
            {
                succeeded++;
                stopReason = "リクエスト上限に達したため中断しました。" +
                             "もう一度実行すると続きから再開します";
                break;
            }
            if (result.ExitCode != 0)
            {
                failed.Add($"{target.Label} (exit {result.ExitCode})");
                continue;
            }
            succeeded++;
        }

        (var summary, var hasIssues) = FetchOutcome.DescribeBatch(
            succeeded, _targets.Count, consumedTotal, failed, stopReason);
        HasIssues = hasIssues;
        if (failed.Count > 0)
        {
            LogLines.Add("");
            LogLines.Add($"===== 失敗した対象: {string.Join(", ", failed)}");
        }
        ResultText = summary;
        LogLines.Add($"===== {summary}");
    }

    /// <summary>1 対象を実行し、結果と消費リクエスト数を返す。</summary>
    private async Task<(FetchResult Result, int Consumed)> RunTargetAsync(FetchTarget target, int? maxRequests)
    {
        var runLog = new List<string>();
        var uiProgress = new Progress<string>(line =>
        {
            LogLines.Add(line);
            if (LogLines.Count > 2000)
                LogLines.RemoveAt(0);
        });
        // Progress<T> の配送は非同期なので、消費リクエスト数の解析用には同期的に溜める
        var progress = new CapturingProgress(runLog, uiProgress);
        var result = await _service.RunAsync(
            target.Username, target.Mode, maxRequests, progress, _cts.Token,
            target.Query, target.BackfillSince);
        // 上限なしの実行は残量計算に使わないので 0 でよい
        var consumed = maxRequests is { } granted
            ? FetchBudget.ConsumedOrGranted(FetchBudget.ParseConsumedRequests(runLog), granted)
            : FetchBudget.ParseConsumedRequests(runLog) ?? 0;
        return (result, consumed);
    }

    [RelayCommand]
    private void Cancel() => _cts.Cancel();

    /// <summary>
    /// 受け取った行を同期的に控えつつ UI 側へも流す。
    /// Progress&lt;T&gt; だけだと配送が非同期で、実行直後に最終行 (消費リクエスト数) を
    /// 取りこぼすことがあるため。
    /// </summary>
    private sealed class CapturingProgress(List<string> sink, IProgress<string> ui) : IProgress<string>
    {
        public void Report(string value)
        {
            lock (sink)
                sink.Add(value);
            ui.Report(value);
        }
    }
}
