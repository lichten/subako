using System.IO;
using TweetViewer.Services;
using TweetViewer.ViewModels;
using TweetViewer.Views;

namespace TweetViewer.Tests;

/// <summary>
/// 取得ログウィンドウの ✕ の挙動: 実行中は隠すだけ (中断ボタンへの導線を保つ)、
/// 非実行時と ForceClose (アプリ終了経路) は本当に閉じる。
/// </summary>
public class UpdateLogWindowTests
{
    private static FetchDialogViewModel CreateVm()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SubakoTests", Guid.NewGuid().ToString("N"));
        var settings = new AppSettings { RepoDir = dir, DataDir = dir };
        return new FetchDialogViewModel(
            new FetchProcessService(settings), "alice", _ => Task.CompletedTask);
    }

    private static void RunSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw new Xunit.Sdk.XunitException($"STA スレッドのテストが失敗: {failure}");
    }

    [Fact]
    public void 実行中のCloseは隠すだけ() => RunSta(() =>
    {
        var vm = CreateVm();
        vm.IsRunning = true;
        var window = new UpdateLogWindow { DataContext = vm };
        var closed = false;
        window.Closed += (_, _) => closed = true;

        window.Close();

        Assert.False(closed);
        Assert.False(window.IsVisible);
        // 後始末: 実行中フラグを下ろせば本当に閉じられる
        vm.IsRunning = false;
        window.Close();
        Assert.True(closed);
    });

    [Fact]
    public void 非実行時のCloseは本当に閉じる() => RunSta(() =>
    {
        var vm = CreateVm();
        var window = new UpdateLogWindow { DataContext = vm };
        var closed = false;
        window.Closed += (_, _) => closed = true;

        window.Close();

        Assert.True(closed);
    });

    [Fact]
    public void ForceCloseなら実行中でも閉じる() => RunSta(() =>
    {
        // アプリ終了経路 (MainWindow.OnClosing) のガード。これが無いと
        // 取得中に MainWindow を閉じられなくなる
        var vm = CreateVm();
        vm.IsRunning = true;
        var window = new UpdateLogWindow { DataContext = vm, ForceClose = true };
        var closed = false;
        window.Closed += (_, _) => closed = true;

        window.Close();

        Assert.True(closed);
    });
}
