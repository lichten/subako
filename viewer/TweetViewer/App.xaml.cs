using System.Windows;
using TweetViewer.Data;
using TweetViewer.Services;
using TweetViewer.ViewModels;

namespace TweetViewer;

public partial class App : Application
{
    private ReadMarkQueue? _readQueue;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settings = AppSettings.Load();
        if (string.IsNullOrWhiteSpace(settings.RepoDir))
        {
            MessageBox.Show(
                "リポジトリ(main.py のあるフォルダ)を検出できませんでした。\n" +
                "%APPDATA%\\TweetViewer\\settings.json の RepoDir を設定してください。",
                "TweetViewer", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
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

        var mainVm = new MainViewModel(db, users, tweets, tags, importer, _readQueue, fetchService, iconCache);
        var window = new MainWindow(settings) { DataContext = mainVm };
        MainWindow = window;
        window.Show();

        _ = mainVm.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 未書込の既読マークをフラッシュしてから終了
        _readQueue?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }
}
