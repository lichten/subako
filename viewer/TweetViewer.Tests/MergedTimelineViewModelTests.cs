using System.IO;
using System.Text;
using TweetViewer.Data;
using TweetViewer.Services;
using TweetViewer.ViewModels;

namespace TweetViewer.Tests;

/// <summary>統合タイムライン (IsAllTimeline) の VM 層テスト。</summary>
public sealed class MergedTimelineViewModelTests : IAsyncDisposable
{
    private readonly string _dataDir;
    private readonly ViewerDatabase _db;
    private readonly ReadMarkQueue _readQueue;

    public MergedTimelineViewModelTests()
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

    private static string TweetLine(long id, string date) =>
        $$"""{"id":"{{id}}","created_at":"{{date}}","full_text":"tweet {{id}}","user":{"username":"author{{id}}","display_name":"Author {{id}}"},"entities":[]}""";

    private static string Date(int day) => $"Tue Jul {day:00} 20:00:00 +0000 2026";

    private async Task ImportAsync(string username, params string[] lines)
    {
        await new UserRepository(_db).AddAsync(username);
        Directory.CreateDirectory(_db.UserDir(username));
        if (username.StartsWith("searches/", StringComparison.Ordinal))
            File.WriteAllText(Path.Combine(_db.UserDir(username), "search.json"),
                """{"query": "kw", "created_at": "2026-07-24T00:00:00+00:00"}""", new UTF8Encoding(false));
        File.AppendAllText(_db.JsonlPath(username),
            string.Join("", lines.Select(l => l + "\n")), new UTF8Encoding(false));
        await new JsonlImporter(_db).ImportUserAsync(username);
    }

    private MainViewModel CreateViewModel(long? tagFilterId = null)
    {
        var settings = new AppSettings { RepoDir = _dataDir, DataDir = _dataDir };
        return new MainViewModel(
            _db, new UserRepository(_db), new TweetRepository(_db), new TagRepository(_db),
            new JsonlImporter(_db), _readQueue, new FetchProcessService(settings),
            new IconCache(_dataDir), unreadOnly: false, tagFilterId);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 60 && !condition(); i++)
            await Task.Delay(50);
        Assert.True(condition(), "条件が満たされませんでした");
    }

    [Fact]
    public void AllTimelineMergesArchivesAndDeduplicates() => RunOnDispatcher(async () =>
    {
        const string bucket = "searches/kw-x";
        await ImportAsync("alice", TweetLine(1, Date(1)), TweetLine(3, Date(3)));
        await ImportAsync(bucket, TweetLine(2, Date(2)), TweetLine(3, Date(3)));   // 3 は重複

        var vm = CreateViewModel();
        await vm.RefreshUsersAsync();
        await vm.RefreshSearchesAsync();

        vm.IsAllTimeline = true;
        await WaitForAsync(() => vm.TweetList.Items.Count == 3);

        Assert.Equal(new[] { "3", "2", "1" }, vm.TweetList.Items.Select(i => i.TweetId));
        Assert.Null(vm.SelectedUser);
        Assert.Null(vm.SelectedSearch);
        // 重複ツイートの代表は実ユーザーアーカイブ
        Assert.Equal("alice", vm.TweetList.Items[0].Username);
        // ヘッダは行の author (別アーカイブの owner 名が混入しない)
        Assert.Equal("Author 2", vm.TweetList.Items[1].HeaderDisplayName);
    });

    [Fact]
    public void SelectingUserExitsAllTimeline() => RunOnDispatcher(async () =>
    {
        await ImportAsync("alice", TweetLine(1, Date(1)));
        var vm = CreateViewModel();
        await vm.RefreshUsersAsync();

        vm.IsAllTimeline = true;
        await WaitForAsync(() => vm.TweetList.Items.Count == 1);

        vm.SelectedUser = vm.Users.Single();
        Assert.False(vm.IsAllTimeline);
        await WaitForAsync(() => vm.TweetList.Items.Count == 1);

        // 逆方向: 統合 ON で選択が外れる
        vm.IsAllTimeline = true;
        Assert.Null(vm.SelectedUser);
    });

    [Fact]
    public void TagFilterNarrowsAllTimeline() => RunOnDispatcher(async () =>
    {
        await ImportAsync("alice", TweetLine(1, Date(1)));
        await ImportAsync("bob", TweetLine(2, Date(2)));

        var vm = CreateViewModel();
        var tags = new TagRepository(_db);
        var tagId = await tags.AddAsync("A");
        await tags.AssignAsync("alice", tagId);
        await vm.RefreshUsersAsync();
        await vm.RefreshTagsAsync();

        vm.IsAllTimeline = true;
        await WaitForAsync(() => vm.TweetList.Items.Count == 2);

        // タグで絞ると統合タイムラインも追随して作り直される
        vm.SelectedTagFilter = vm.Tags.Single(t => t.TagId == tagId);
        await WaitForAsync(() => vm.TweetList.Items.Count == 1);
        Assert.Equal("1", vm.TweetList.Items[0].TweetId);
        Assert.True(vm.IsAllTimeline);   // 統合状態は維持

        vm.SelectedTagFilter = null;
        await WaitForAsync(() => vm.TweetList.Items.Count == 2);
    });

    [Fact]
    public void UntaggedFilterNarrowsToRowsWithoutTags() => RunOnDispatcher(async () =>
    {
        await ImportAsync("alice", TweetLine(1, Date(1)));
        await ImportAsync("bob", TweetLine(2, Date(2)));

        var vm = CreateViewModel();
        var tags = new TagRepository(_db);
        var tagId = await tags.AddAsync("A");
        await tags.AssignAsync("alice", tagId);
        await vm.RefreshUsersAsync();
        await vm.RefreshTagsAsync();

        // 疑似タグはフィルタ用の一覧の先頭だけに現れ、実タグ一覧には入らない
        Assert.True(vm.TagFilterOptions[0].IsUntagged);
        Assert.DoesNotContain(vm.Tags, t => t.IsUntagged);
        Assert.Equal(vm.Tags.Count + 1, vm.TagFilterOptions.Count);

        vm.IsAllTimeline = true;
        await WaitForAsync(() => vm.TweetList.Items.Count == 2);

        // 「(タグなし)」= タグ 0 件の bob だけが残る
        vm.SelectedTagFilter = vm.TagFilterOptions.Single(t => t.IsUntagged);
        await WaitForAsync(() => vm.TweetList.Items.Count == 1);
        Assert.Equal("2", vm.TweetList.Items[0].TweetId);
        Assert.Equal(new[] { "bob" },
            vm.UsersView.Cast<UserItemViewModel>().Select(u => u.Username));
        Assert.True(vm.IsAllTimeline);   // 統合状態は維持

        // タグを付けると「(タグなし)」の対象から外れる
        await vm.ToggleTagAsync(vm.Users.Single(u => u.Username == "bob"),
            vm.Tags.Single(t => t.TagId == tagId), assign: true);
        Assert.Empty(vm.UsersView.Cast<UserItemViewModel>());

        vm.SelectedTagFilter = null;
        await WaitForAsync(() => vm.TweetList.Items.Count == 2);
    });

    [Fact]
    public void UntaggedOptionSurvivesTagAddAndDelete() => RunOnDispatcher(async () =>
    {
        await ImportAsync("alice", TweetLine(1, Date(1)));

        var vm = CreateViewModel();
        await vm.RefreshUsersAsync();
        await vm.RefreshTagsAsync();
        Assert.Single(vm.TagFilterOptions);   // 疑似タグのみ

        await vm.CreateAndAssignTagAsync("A", "alice");
        Assert.Equal(new[] { "(タグなし)", "A" }, vm.TagFilterOptions.Select(t => t.Name));

        // 疑似タグを選んだまま実タグを消しても、先頭と選択は残る
        vm.SelectedTagFilter = vm.TagFilterOptions[0];
        await vm.DeleteTagAsync(vm.Tags.Single(t => t.Name == "A"));
        Assert.Empty(vm.Tags);
        Assert.Single(vm.TagFilterOptions);
        Assert.True(vm.SelectedTagFilter?.IsUntagged);
        Assert.Single(vm.UsersView.Cast<UserItemViewModel>());   // タグ 0 件に戻って再表示
    });

    [Fact]
    public void RestoresTagFilterFromSettings() => RunOnDispatcher(async () =>
    {
        await ImportAsync("alice", TweetLine(1, Date(1)));
        await ImportAsync("bob", TweetLine(2, Date(2)));
        var tags = new TagRepository(_db);
        var tagId = await tags.AddAsync("A");
        await tags.AssignAsync("alice", tagId);

        // -1 = 「(タグなし)」。表示中の先頭が選択ユーザーになる
        var untagged = CreateViewModel(TagItemViewModel.UntaggedTagId);
        await untagged.InitializeAsync();
        Assert.True(untagged.SelectedTagFilter?.IsUntagged);
        Assert.Equal(new[] { "bob" },
            untagged.UsersView.Cast<UserItemViewModel>().Select(u => u.Username));
        Assert.Equal("bob", untagged.SelectedUser?.Username);

        var tagged = CreateViewModel(tagId);
        await tagged.InitializeAsync();
        Assert.Equal(tagId, tagged.SelectedTagFilter?.TagId);

        // 消えたタグ ID は「すべて表示」に戻す
        var stale = CreateViewModel(tagId + 999);
        await stale.InitializeAsync();
        Assert.Null(stale.SelectedTagFilter);
    });

    [Fact]
    public void MarkSeenDecrementsAllArchivesContainingTweet() => RunOnDispatcher(async () =>
    {
        const string bucket = "searches/kw-x";
        await ImportAsync("alice", TweetLine(1, Date(1)), TweetLine(2, Date(2)));
        await ImportAsync(bucket, TweetLine(1, Date(1)));

        var vm = CreateViewModel();
        await vm.RefreshUsersAsync();
        await vm.RefreshSearchesAsync();
        var alice = vm.Users.Single(u => u.Username == "alice");
        var search = vm.Searches.Single();
        Assert.Equal(2, alice.UnreadCount);
        Assert.Equal(1, search.UnreadCount);

        vm.IsAllTimeline = true;
        await WaitForAsync(() => vm.TweetList.Items.Count == 2);

        // 重複ツイート "1" を既読化 → 両アーカイブの未読数が減る
        var dup = vm.TweetList.Items.Single(i => i.TweetId == "1");
        vm.TweetList.MarkSeen(dup);
        Assert.Equal(1, alice.UnreadCount);
        Assert.Equal(0, search.UnreadCount);

        // 手動トグルで未読に戻すと両方 +1
        await vm.TweetList.ToggleReadAsync(dup);   // 既読 → 未読
        Assert.Equal(2, alice.UnreadCount);
        Assert.Equal(1, search.UnreadCount);
    });

    [Fact]
    public void DeleteUserDuringAllTimelineKeepsUnifiedView() => RunOnDispatcher(async () =>
    {
        await ImportAsync("alice", TweetLine(1, Date(1)));
        await ImportAsync("bob", TweetLine(2, Date(2)));

        var vm = CreateViewModel();
        await vm.RefreshUsersAsync();
        vm.IsAllTimeline = true;
        await WaitForAsync(() => vm.TweetList.Items.Count == 2);

        var error = await vm.DeleteUserAsync(
            vm.Users.Single(u => u.Username == "alice"), deleteFiles: false);

        Assert.Null(error);
        Assert.True(vm.IsAllTimeline);     // 統合状態を維持
        Assert.Null(vm.SelectedUser);      // 単一表示へ落ちない
        await WaitForAsync(() => vm.TweetList.Items.Count == 1);
        Assert.Equal("2", vm.TweetList.Items[0].TweetId);
    });

    /// <summary>WPF の CollectionView (UsersView) が必要とする Dispatcher 上で実行する。</summary>
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
