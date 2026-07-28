using System.IO;
using TweetViewer.Data;
using TweetViewer.Models;
using TweetViewer.Services;
using TweetViewer.ViewModels;

namespace TweetViewer.Tests;

/// <summary>ツイート右クリック「作者をユーザーに追加」(タグ引き継ぎ) のテスト。</summary>
public sealed class AddAuthorFromTweetTests : IAsyncDisposable
{
    private readonly string _dataDir;
    private readonly ViewerDatabase _db;
    private readonly ReadMarkQueue _readQueue;

    public AddAuthorFromTweetTests()
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

    [Fact]
    public void NewAuthorInheritsTagsAndSelectionStays() => RunOnDispatcher(async () =>
    {
        var vm = CreateViewModel(out var tags);
        await new UserRepository(_db).AddAsync("viewer");
        var tagA = await tags.AddAsync("ゲーム");
        var tagB = await tags.AddAsync("イラスト");
        await tags.AssignAsync("viewer", tagA);
        await tags.AssignAsync("viewer", tagB);
        await vm.RefreshUsersAsync();
        await vm.RefreshTagsAsync();
        vm.SelectedUser = vm.Users.Single(u => u.Username == "viewer");

        var (added, isNew) = await vm.AddAuthorFromTweetAsync("author1");

        Assert.True(isNew);
        Assert.NotNull(added);
        Assert.Equal("author1", added!.Username);
        Assert.Equal(2, added.Tags.Count);
        Assert.Equal("viewer", vm.SelectedUser?.Username); // 表示は切り替わらない
        var assignments = await tags.GetAssignmentsAsync();
        Assert.Equal(2, assignments["author1"].Count); // DB 側にも保存されている
        Assert.Contains("author1 を追加しました", vm.StatusText);
        Assert.Contains("タグ 2件を引き継ぎ", vm.StatusText);
    });

    [Fact]
    public void ExistingAuthorMergesTagsOnly() => RunOnDispatcher(async () =>
    {
        var vm = CreateViewModel(out var tags);
        var users = new UserRepository(_db);
        await users.AddAsync("viewer");
        await users.AddAsync("author1"); // 登録済み
        var tagA = await tags.AddAsync("ゲーム");
        var tagB = await tags.AddAsync("イラスト");
        await tags.AssignAsync("viewer", tagA);
        await tags.AssignAsync("viewer", tagB);
        await tags.AssignAsync("author1", tagA); // 片方は重複
        await vm.RefreshUsersAsync();
        await vm.RefreshTagsAsync();
        vm.SelectedUser = vm.Users.Single(u => u.Username == "viewer");
        var tagAVm = vm.Tags.Single(t => t.TagId == tagA);
        var countBefore = tagAVm.UserCount;

        var (added, isNew) = await vm.AddAuthorFromTweetAsync("author1");

        Assert.False(isNew);
        Assert.NotNull(added);
        Assert.Equal(2, added!.Tags.Count); // 重複なしで 2 件
        Assert.Equal(countBefore, tagAVm.UserCount); // 重複タグは二重加算されない
        Assert.Contains("登録済み", vm.StatusText);
    });

    [Fact]
    public void SearchBucketTagsAreInherited() => RunOnDispatcher(async () =>
    {
        var vm = CreateViewModel(out var tags);
        const string bucket = "searches/kw-deadbeef";
        var users = new UserRepository(_db);
        await users.AddAsync(bucket);
        Directory.CreateDirectory(_db.UserDir(bucket));
        File.WriteAllText(Path.Combine(_db.UserDir(bucket), "search.json"),
            """{"query": "kw", "created_at": "2026-07-24T00:00:00+00:00"}""");
        var tagA = await tags.AddAsync("ウォッチ");
        await tags.AssignAsync(bucket, tagA);
        await vm.RefreshSearchesAsync();
        await vm.RefreshTagsAsync();
        vm.SelectedSearch = vm.Searches.Single();

        var (added, isNew) = await vm.AddAuthorFromTweetAsync("someone");

        Assert.True(isNew);
        Assert.Single(added!.Tags);
        Assert.Equal("ウォッチ", added.Tags[0].Name);
        Assert.NotNull(vm.SelectedSearch); // 検索表示のまま
    });

    [Fact]
    public void InvalidUsernameIsRejected() => RunOnDispatcher(async () =>
    {
        var vm = CreateViewModel(out _);
        var (added, isNew) = await vm.AddAuthorFromTweetAsync("searches/kw-x");
        Assert.Null(added);
        Assert.False(isNew);
        Assert.Contains("英数字", vm.StatusText);
    });

    [Fact]
    public void AddableAuthorUsernameResolution()
    {
        var list = new TweetListViewModel(
            _db, new TweetRepository(_db), _readQueue, new IconCache(_dataDir));

        // 通常ツイート → 実投稿者
        var normal = CreateItem(list, new TweetRow
        {
            TweetId = "1",
            IdInt = 1,
            Username = "searches/kw-x",
            AuthorUsername = "someone",
            CreatedAtUtc = "2026-07-21T20:23:54+00:00",
            SortKey = 1,
            Type = TweetType.Tweet,
            FullText = "t",
        });
        Assert.Equal("someone", normal.AddableAuthorUsername);
        Assert.True(normal.CanAddAuthor);

        // RT → RT元の作者
        var rt = CreateItem(list, new TweetRow
        {
            TweetId = "2",
            IdInt = 2,
            Username = "alice",
            AuthorUsername = "alice",
            RtUsername = "original",
            CreatedAtUtc = "2026-07-21T20:23:54+00:00",
            SortKey = 2,
            Type = TweetType.Retweet,
            FullText = "RT @original: t",
        });
        Assert.Equal("original", rt.AddableAuthorUsername);

        // author 列がない旧バケットデータ → 追加不可 (Username はバケット ID)
        var legacy = CreateItem(list, new TweetRow
        {
            TweetId = "3",
            IdInt = 3,
            Username = "searches/kw-x",
            CreatedAtUtc = "2026-07-21T20:23:54+00:00",
            SortKey = 3,
            Type = TweetType.Tweet,
            FullText = "t",
        });
        Assert.Null(legacy.AddableAuthorUsername);
        Assert.False(legacy.CanAddAuthor);

        // MenuItem Header の _ エスケープ
        var underscore = CreateItem(list, new TweetRow
        {
            TweetId = "4",
            IdInt = 4,
            Username = "alice",
            AuthorUsername = "some_user",
            CreatedAtUtc = "2026-07-21T20:23:54+00:00",
            SortKey = 4,
            Type = TweetType.Tweet,
            FullText = "t",
        });
        Assert.Equal("@some__user をユーザーに追加", underscore.AddAuthorMenuHeader);
    }

    private TweetItemViewModel CreateItem(TweetListViewModel list, TweetRow row) =>
        new(list, row, [], _dataDir, "owner", null,
            new IconCache(_dataDir), new IconCache(_dataDir, "thumbnails"));

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
