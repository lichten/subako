using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TweetViewer.Services;

namespace TweetViewer.ViewModels;

/// <summary>UpdateLogWindow 用: 取得サブプロセスのライブログ表示と中断。</summary>
public sealed partial class FetchDialogViewModel : ObservableObject
{
    private readonly FetchProcessService _service;
    private readonly Func<string, Task> _onCompleted;
    private readonly CancellationTokenSource _cts = new();

    public string Username { get; }

    public ObservableCollection<string> LogLines { get; } = new();

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _resultText = "";

    public FetchDialogViewModel(
        FetchProcessService service, string username, Func<string, Task> onCompleted)
    {
        _service = service;
        Username = username;
        _onCompleted = onCompleted;
    }

    public async Task StartAsync()
    {
        IsRunning = true;
        var progress = new Progress<string>(line =>
        {
            LogLines.Add(line);
            if (LogLines.Count > 2000)
                LogLines.RemoveAt(0);
        });
        try
        {
            var result = await _service.RunAsync(Username, FetchMode.Update, progress, _cts.Token);
            ResultText = result switch
            {
                { Cancelled: true } => "中断しました(途中までの取得分は保存済み)",
                { ExitCode: 0 } => "取得完了",
                _ => $"エラー終了 (exit code {result.ExitCode})",
            };
            // 失敗・中断でもページ毎に保存済みのため取込は実行する
            await _onCompleted(Username);
        }
        catch (Exception ex)
        {
            ResultText = $"実行に失敗しました: {ex.Message}";
            LogLines.Add(ResultText);
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts.Cancel();
}
