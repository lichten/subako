using System.IO;
using System.Text;
using TweetViewer.Data;
using TweetViewer.Services;
using TweetViewer.ViewModels;

namespace TweetViewer.Tests;

/// <summary>フォロー中の一括登録 (MainViewModel.ImportFollowingsAsync) のテスト。</summary>
public sealed class ImportFollowingsTests : IAsyncDisposable
{
    private readonly string _dataDir;
    private readonly ViewerDatabase _db;
    private readonly ReadMarkQueue _readQueue;

    public ImportFollowingsTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "TweetViewerTests", Guid.NewGuid().ToString("N"));
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

    private MainViewModel CreateViewModel(out TagRepository tags)
    {
        var users = new UserRepository(_db);
        var tweets = new TweetRepository(_db);
        tags = new TagRepository(_db);
        var importer = new JsonlImporter(_db);
        var settings = new AppSettings { RepoDir = _dataDir, DataDir = _dataDir };
        return new MainViewModel(
            _db, users, tweets, tags, importer, _readQueue,
            new FetchProcessService(settings), new IconCache(_dataDir));
    }

    private void WriteFollowings(string source, params string[] usernames)
    {
        var path = FollowingsFile.PathFor(_dataDir, source);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            string.Join("", usernames.Select(u =>
                $$"""{"id":"1","username":"{{u}}","display_name":"{{u}} さん"}""" + "\n")),
            new UTF8Encoding(false));
    }

    [Fact]
    public void 全員を登録してタグを付ける() => RunOnDispatcher(async () =>
    {
        var vm = CreateViewModel(out var tags);
        var tagA = await tags.AddAsync("ゲーム");
        // 既に登録済みのユーザーもフォロー一覧に含める
        await new UserRepository(_db).AddAsync("bob");
        await vm.RefreshUsersAsync();
        await vm.RefreshTagsAsync();
        WriteFollowings("src", "alice", "bob", "src");   // src 自身は除外される

        var result = await vm.ImportFollowingsAsync("src", [tagA], ["フォロー"]);

        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.Added);      // 新規は alice だけ
        Assert.Equal(2, result.TagCount);   // 既存タグ + 新規タグ
        Assert.Equal(new[] { "alice", "bob" }, vm.Users.Select(u => u.Username).Order());
        // 登録済みだった bob にもタグが付く (後続の絞り込み更新で取りこぼさないため)
        foreach (var user in vm.Users)
            Assert.Equal(2, user.Tags.Count);
        Assert.Contains("フォロー", vm.Tags.Select(t => t.Name));
        Assert.DoesNotContain(vm.Users, u => u.Username == "src");
    });

    [Fact]
    public void 既存タグ名を新規欄に入れても重複しない() => RunOnDispatcher(async () =>
    {
        var vm = CreateViewModel(out var tags);
        var tagA = await tags.AddAsync("ゲーム");
        await vm.RefreshTagsAsync();
        WriteFollowings("src", "alice");

        // 大文字小文字違いの同名 (tags.name は UNIQUE COLLATE NOCASE)
        var result = await vm.ImportFollowingsAsync("src", [], ["ゲーム"]);

        Assert.Equal(1, result.TagCount);
        Assert.Single(vm.Tags);
        Assert.Equal(tagA, vm.Users.Single().Tags.Single().TagId);
    });

    [Fact]
    public void ファイルが無ければ何も登録しない() => RunOnDispatcher(async () =>
    {
        var vm = CreateViewModel(out var tags);
        var tagA = await tags.AddAsync("ゲーム");
        await vm.RefreshTagsAsync();

        var result = await vm.ImportFollowingsAsync("src", [tagA], ["未使用"]);

        Assert.Equal(0, result.Total);
        Assert.Empty(vm.Users);
        // 取得に失敗した実行で空タグだけが増えないこと
        Assert.Single(vm.Tags);
        Assert.Contains("読み込めませんでした", vm.StatusText);
    });

    [Fact]
    public void 不正なハンドルは落とす() => RunOnDispatcher(async () =>
    {
        var vm = CreateViewModel(out _);
        WriteFollowings("src", "bad name", "searches/x", "ok_user");

        var result = await vm.ImportFollowingsAsync("src", [], ["フォロー"]);

        Assert.Equal(1, result.Total);
        Assert.Equal(new[] { "ok_user" }, vm.Users.Select(u => u.Username));
    });

    [Fact]
    public void 再実行しても冪等() => RunOnDispatcher(async () =>
    {
        var vm = CreateViewModel(out _);
        WriteFollowings("src", "alice", "bob");

        await vm.ImportFollowingsAsync("src", [], ["フォロー"]);
        var second = await vm.ImportFollowingsAsync("src", [], ["フォロー"]);

        Assert.Equal(2, second.Total);
        Assert.Equal(0, second.Added);
        Assert.Equal(2, vm.Users.Count);
        Assert.Single(vm.Tags);
        Assert.Equal(2, vm.Tags.Single().UserCount);
    });

    [Fact]
    public void タグフィルタの選択は参照ごと保たれる() => RunOnDispatcher(async () =>
    {
        // RefreshTagsAsync の差分マージを迂回すると ComboBox の選択が飛ぶ
        var vm = CreateViewModel(out var tags);
        await tags.AddAsync("ゲーム");
        await vm.RefreshTagsAsync();
        var selected = vm.Tags.Single();
        vm.SelectedTagFilter = selected;
        WriteFollowings("src", "alice");

        await vm.ImportFollowingsAsync("src", [], ["フォロー"]);

        Assert.Same(selected, vm.SelectedTagFilter);
    });

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
}
