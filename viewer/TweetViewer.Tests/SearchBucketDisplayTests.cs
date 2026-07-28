using System.IO;
using System.Text;
using TweetViewer.Data;
using TweetViewer.Services;
using TweetViewer.ViewModels;

namespace TweetViewer.Tests;

/// <summary>検索バケット選択時のタイムライン表示経路 (TweetListViewModel まで) の回帰テスト。</summary>
public sealed class SearchBucketDisplayTests : IAsyncDisposable
{
    private readonly string _dataDir;
    private readonly ViewerDatabase _db;
    private readonly ReadMarkQueue _readQueue;

    public SearchBucketDisplayTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "SubakoTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
        _db = new ViewerDatabase(_dataDir);
        _db.EnsureCreated();
        _readQueue = new ReadMarkQueue(_db);
    }

    public async ValueTask DisposeAsync()
    {
        await _readQueue.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dataDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void SelectingSearchInMainViewModelLoadsTimeline() => RunOnDispatcher(async () =>
    {
        // 実際の操作フロー: 検索作成 → fetch 完了 (JSONL あり) → 取込 → サイドバーで選択
        var users = new UserRepository(_db);
        var tweets = new TweetRepository(_db);
        var tags = new TagRepository(_db);
        var importer = new JsonlImporter(_db);
        var settings = new AppSettings { RepoDir = _dataDir, DataDir = _dataDir };
        var vm = new MainViewModel(
            _db, users, tweets, tags, importer, _readQueue,
            new FetchProcessService(settings), new IconCache(_dataDir));

        var (bucketId, finalQuery) = await vm.StartApiSearchAsync("スレスパ2 OR sts2", null, 100);
        Assert.Equal("(スレスパ2 OR sts2) min_faves:100", finalQuery);
        Assert.Single(vm.Searches);

        // Python fetcher 相当: JSONL と search.json を書く
        Directory.CreateDirectory(_db.UserDir(bucketId));
        File.WriteAllText(Path.Combine(_db.UserDir(bucketId), "search.json"),
            $$"""{"query": {{System.Text.Json.JsonSerializer.Serialize(finalQuery)}}, "created_at": "2026-07-24T00:00:00+00:00"}""",
            new UTF8Encoding(false));
        File.AppendAllText(_db.JsonlPath(bucketId),
            """{"id":"1","created_at":"Tue Jul 21 20:23:54 +0000 2026","full_text":"hit","user":{"username":"someone","display_name":"Someone"},"entities":[]}""" + "\n",
            new UTF8Encoding(false));
        await vm.OnFetchCompletedAsync(bucketId);
        Assert.Equal(1, vm.Searches[0].TweetCount);
        Assert.Equal(finalQuery, vm.Searches[0].Query);

        // サイドバーで検索を選択 → タイムラインが読み込まれる
        vm.SelectedSearch = vm.Searches[0];
        await WaitForItemsAsync(vm);
        Assert.Single(vm.TweetList.Items);
        Assert.Equal("Someone", vm.TweetList.Items[0].HeaderDisplayName);
    });

    private static async Task WaitForItemsAsync(MainViewModel vm)
    {
        // OnSelectedSearchChanged の fire-and-forget を待つ
        for (var i = 0; i < 50 && vm.TweetList.Items.Count == 0; i++)
            await Task.Delay(50);
    }

    /// <summary>
    /// 回帰テスト: ユーザー選択中に検索を選ぶと、相互排他の連鎖 (SelectedUser=null) で
    /// 二重リセットが走り、実行中ロード + version ガードの競合でタイムラインが
    /// 空のままになる不具合 (実データで発生)。
    /// </summary>
    /// <summary>WPF の CollectionView (UsersView) が必要とする Dispatcher 上で非同期テスト本体を実行する。</summary>
    private static void RunOnDispatcher(Func<Task> asyncBody)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(
                new System.Windows.Threading.DispatcherSynchronizationContext(dispatcher));
            var frame = new System.Windows.Threading.DispatcherFrame();
            _ = RunAsync();
            System.Windows.Threading.Dispatcher.PushFrame(frame);

            async Task RunAsync()
            {
                try
                {
                    await asyncBody();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    frame.Continue = false;
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw new Xunit.Sdk.XunitException($"Dispatcher 上のテストが失敗: {failure}");
    }

    [Fact]
    public void SwitchingFromUserToSearchLoadsTimeline() => RunOnDispatcher(async () =>
    {
        var users = new UserRepository(_db);
        var tweets = new TweetRepository(_db);
        var importer = new JsonlImporter(_db);

        // ユーザーアーカイブ (リセット#1 のロードに時間がかかるよう多めに)
        await users.AddAsync("alice");
        var lines = new StringBuilder();
        for (var i = 1; i <= 300; i++)
            lines.Append($$"""{"id":"{{i}}","created_at":"Tue Jul 21 20:23:54 +0000 2026","full_text":"tweet {{i}}","entities":[]}""" + "\n");
        Directory.CreateDirectory(_db.UserDir("alice"));
        File.AppendAllText(_db.JsonlPath("alice"), lines.ToString(), new UTF8Encoding(false));
        await importer.ImportUserAsync("alice");

        // 検索バケット
        const string bucket = "searches/kw-deadbeef";
        await users.AddAsync(bucket);
        Directory.CreateDirectory(_db.UserDir(bucket));
        File.WriteAllText(Path.Combine(_db.UserDir(bucket), "search.json"),
            """{"query": "kw", "created_at": "2026-07-24T00:00:00+00:00"}""", new UTF8Encoding(false));
        File.AppendAllText(_db.JsonlPath(bucket),
            """{"id":"9001","created_at":"Tue Jul 21 20:23:54 +0000 2026","full_text":"hit","user":{"username":"someone","display_name":"Someone"},"entities":[]}""" + "\n",
            new UTF8Encoding(false));
        await importer.ImportUserAsync(bucket);

        var settings = new AppSettings { RepoDir = _dataDir, DataDir = _dataDir };
        var vm = new MainViewModel(
            _db, users, tweets, new TagRepository(_db), importer, _readQueue,
            new FetchProcessService(settings), new IconCache(_dataDir));
        await vm.RefreshUsersAsync();
        await vm.RefreshSearchesAsync();

        // ユーザーを選択 → ロード開始直後に検索へ切り替える (二重リセット競合の再現)
        vm.SelectedUser = vm.Users.Single(u => u.Username == "alice");
        vm.SelectedSearch = vm.Searches.Single();

        await WaitForItemsAsync(vm);
        Assert.Null(vm.SelectedUser);
        Assert.Single(vm.TweetList.Items);
        Assert.Equal("9001", vm.TweetList.Items[0].TweetId);
    });

    /// <summary>
    /// リセット中に追加ロードが割り込んでも項目が重複しないという不変条件のテスト。
    /// InfiniteScrollBehavior がリセット直後に LoadMoreCommand を発火する状況を模す。
    /// 注: _loading の横取り解除 (LoadMoreCoreAsync の finally) はこのテストでは
    /// 再現できない — AsyncRelayCommand が同一コマンドの同時実行を抑止するため、
    /// 実アプリの ScrollChanged 連打と同じ並びにはならない。ここで守れるのは
    /// 「リセットと追加ロードが混ざっても重複しない」という粗い不変条件まで。
    /// </summary>
    [Fact]
    public void LoadMoreDuringReset_DoesNotDuplicateItems() => RunOnDispatcher(async () =>
    {
        var users = new UserRepository(_db);
        var importer = new JsonlImporter(_db);
        await users.AddAsync("alice");
        var lines = new StringBuilder();
        for (var i = 1; i <= 300; i++)   // PageSize=200 を超える = HasMore が true になる
            lines.Append($$"""{"id":"{{i}}","created_at":"Tue Jul 21 20:23:54 +0000 2026","full_text":"tweet {{i}}","entities":[]}""" + "\n");
        Directory.CreateDirectory(_db.UserDir("alice"));
        File.AppendAllText(_db.JsonlPath("alice"), lines.ToString(), new UTF8Encoding(false));
        await importer.ImportUserAsync("alice");

        var settings = new AppSettings { RepoDir = _dataDir, DataDir = _dataDir };
        var vm = new MainViewModel(
            _db, users, new TweetRepository(_db), new TagRepository(_db), importer, _readQueue,
            new FetchProcessService(settings), new IconCache(_dataDir));
        await vm.RefreshUsersAsync();
        var alice = vm.Users.Single(u => u.Username == "alice");

        // 二重リセット + その最中に ScrollChanged 相当の追加ロードを何度も差し込む
        vm.SelectedUser = alice;
        vm.SelectedUser = null;
        vm.SelectedUser = alice;
        for (var i = 0; i < 10; i++)
        {
            vm.TweetList.LoadMoreCommand.Execute(null);
            await Task.Yield();
        }

        await WaitForItemsAsync(vm);
        for (var i = 0; i < 60; i++)
        {
            await Task.Delay(50);
            if (vm.TweetList.Items.Count is 200 or 300)
                break;
        }

        // 同じツイートが二重に積まれていないこと (件数はロードが何ページ進んだかで変わる)
        var ids = vm.TweetList.Items.Select(x => x.TweetId).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.InRange(ids.Count, 200, 300);
    });

    [Fact]
    public async Task ResetAsyncWithBucketIdLoadsItems()
    {
        const string bucket = "searches/(_Slay_the_Spire_2_OR_sts2)_lan-1e9993c1";
        var users = new UserRepository(_db);
        await users.AddAsync(bucket);
        Directory.CreateDirectory(_db.UserDir(bucket));
        File.AppendAllText(_db.JsonlPath(bucket),
            """{"id":"1","created_at":"Tue Jul 21 20:23:54 +0000 2026","full_text":"hit1 スレスパ2","user":{"username":"someone","display_name":"Someone"},"entities":[]}""" + "\n" +
            """{"id":"2","created_at":"Wed Jul 22 20:23:54 +0000 2026","full_text":"hit2","user":{"username":"other","display_name":"Other"},"entities":[]}""" + "\n",
            new UTF8Encoding(false));
        var importer = new JsonlImporter(_db);
        var imported = await importer.ImportUserAsync(bucket);
        Assert.Equal(2, imported.NewTweets);

        var list = new TweetListViewModel(
            _db, new TweetRepository(_db), _readQueue, new IconCache(_dataDir));
        await list.ResetAsync([new ArchiveInfo(bucket, "クエリ表示名", null)], unreadOnly: false);

        Assert.Equal(2, list.Items.Count);
        Assert.Equal("2", list.Items[0].TweetId);
        Assert.Equal("Other", list.Items[0].HeaderDisplayName);
        Assert.Equal("other", list.Items[0].HeaderUsername);
    }
}
