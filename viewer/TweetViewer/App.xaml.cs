using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using TweetViewer.Data;
using TweetViewer.Services;
using TweetViewer.ViewModels;

namespace TweetViewer;

public partial class App : Application
{
    private ReadMarkQueue? _readQueue;
    private AppLog? _log;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _log = AppLog.CreateDefault();
        _log.CleanupOldLogs();
        _log.Write($"起動 {AppInfo.Name} {typeof(App).Assembly.GetName().Version}");
        // クラッシュしてもログと既読マークを残す (OnExit は未処理例外では走らない)
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var settings = AppSettings.Load();
        // 初回起動 (または設定消失): データの置き場が決まるまで進めない。
        // fetcher (RepoDir) は無くても閲覧はできるので、ここでは必須にしない
        if (string.IsNullOrWhiteSpace(settings.RepoDir) && string.IsNullOrWhiteSpace(settings.DataDir))
        {
            // 最初に作られたウィンドウは自動的に Application.MainWindow になるため、
            // OnMainWindowClose のままだと「開始」で閉じた瞬間にシャットダウンが予約され、
            // 表示直後の本物のメインウィンドウが即座に閉じられてしまう
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var firstRun = new Views.FirstRunDialog(settings);
            if (firstRun.ShowDialog() != true)
            {
                Shutdown(1);
                return;
            }
        }
        settings.Save();

        var db = new ViewerDatabase(settings.EffectiveDataDir);
        db.EnsureCreated();

        var users = new UserRepository(db);
        var tweets = new TweetRepository(db);
        var tags = new TagRepository(db);
        var importer = new JsonlImporter(db);
        _readQueue = new ReadMarkQueue(db);
        var fetchService = new FetchProcessService(settings);
        var iconCache = new IconCache(settings.EffectiveDataDir);

        var mainVm = new MainViewModel(db, users, tweets, tags, importer, _readQueue, fetchService, iconCache,
            settings.UnreadOnly, settings.TagFilterId);
        var window = new MainWindow(settings) { DataContext = mainVm };
        MainWindow = window;
        // 初回セットアップで OnExplicitShutdown に切り替えた場合はここで通常動作に戻す
        // (本物のメインウィンドウを据えた後なら安全)
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();

        _ = mainVm.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        FlushReadQueue();
        _log?.Write("終了");
        base.OnExit(e);
    }

    /// <summary>未書込の既読マークをフラッシュする。通常終了とクラッシュの両方から呼ばれる。</summary>
    private void FlushReadQueue()
    {
        try
        {
            _readQueue?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _readQueue = null;   // 二重フラッシュ防止 (クラッシュ後に OnExit も走るため)
        }
        catch (Exception ex)
        {
            _log?.WriteException("FlushReadQueue", ex);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log?.WriteException("DispatcherUnhandledException", e.Exception);
        FlushReadQueue();
        var open = MessageBox.Show(
            "予期しないエラーが発生したため終了します。\n\n" +
            $"{e.Exception.Message}\n\n" +
            "ログフォルダを開きますか? (不具合報告の際はログファイルを添付してください)",
            AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Error);
        if (open == MessageBoxResult.Yes && _log is { } log)
        {
            try
            {
                Process.Start("explorer.exe", log.LogDirectory);
            }
            catch (Exception)
            {
                // ログフォルダが開けなくても終了処理は続ける
            }
        }
        e.Handled = true;   // WPF 既定のクラッシュダイアログとの二重表示を避ける
        Shutdown(1);
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // 非 UI スレッドの未処理例外。プロセス終了は止められないので記録だけ確実に行う
        _log?.WriteException("AppDomain.UnhandledException",
            e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
        FlushReadQueue();
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _log?.WriteException("UnobservedTaskException", e.Exception);
        e.SetObserved();
    }
}
