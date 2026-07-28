using System.IO;
using System.Text;
using TweetViewer.Data;
using TweetViewer.Services;
using TweetViewer.ViewModels;

namespace TweetViewer.Tests;

/// <summary>ユーザー / 検索バケットの削除 (_trash 退避と完全削除) のテスト。</summary>
public sealed class DeleteArchiveTests : IAsyncDisposable
{
    private readonly string _dataDir;
    private readonly ViewerDatabase _db;
    private readonly ReadMarkQueue _readQueue;

    public DeleteArchiveTests()
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

    private static string TweetLine(long id, string? imageUrl = null)
    {
        var entities = imageUrl is null
            ? "[]"
            : $$"""[{"type":"photo","link":"{{imageUrl}}"}]""";
        return $$"""
            {"id":"{{id}}","created_at":"Wed Jul 22 21:28:37 +0000 2026","full_text":"t{{id}}","entities":{{entities}}}
            """.ReplaceLineEndings("");
    }

    private void WriteJsonl(string username, params string[] lines)
    {
        Directory.CreateDirectory(_db.UserDir(username));
        File.AppendAllText(_db.JsonlPath(username),
            string.Join("", lines.Select(l => l + "\n")), new UTF8Encoding(false));
    }

    private long Count(string sql, string? param = null)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (param is not null)
            cmd.Parameters.AddWithValue("$u", param);
        return (long)cmd.ExecuteScalar()!;
    }

    private MainViewModel CreateViewModel()
    {
        var settings = new AppSettings { RepoDir = _dataDir, DataDir = _dataDir };
        return new MainViewModel(
            _db, new UserRepository(_db), new TweetRepository(_db), new TagRepository(_db),
            new JsonlImporter(_db), _readQueue, new FetchProcessService(settings),
            new IconCache(_dataDir));
    }

    [Fact]
    public void DeleteUserKeepingFiles_MovesToTrashAndKeepsSharedRows() => RunOnDispatcher(async () =>
    {
        // alice と検索バケットが同じツイート (画像付き) を共有する状態を作る
        var users = new UserRepository(_db);
        var tags = new TagRepository(_db);
        var importer = new JsonlImporter(_db);
        const string bucket = "searches/kw-shared";
        await users.AddAsync("alice");
        await users.AddAsync(bucket);
        var line = TweetLine(1, "https://pbs.twimg.com/media/SHARED.jpg");
        WriteJsonl("alice", line);
        WriteJsonl(bucket, line);
        await importer.ImportUserAsync("alice");
        await importer.ImportUserAsync(bucket);
        var tagId = await tags.AddAsync("A");
        await tags.AssignAsync("alice", tagId);
        await tags.AssignAsync(bucket, tagId);
        // 既読を付ける (read_state は tweet_id 単位で全アーカイブ共通)
        await new TweetRepository(_db).SetReadAsync("1", "alice", read: true);

        var vm = CreateViewModel();
        await vm.RefreshUsersAsync();
        await vm.RefreshSearchesAsync();
        await vm.RefreshTagsAsync();
        var alice = vm.Users.Single(u => u.Username == "alice");
        vm.SelectedUser = alice;

        var error = await vm.DeleteUserAsync(alice, deleteFiles: false);

        Assert.Null(error);
        Assert.Equal(0, Count("SELECT COUNT(*) FROM users WHERE username = $u", "alice"));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM tweets WHERE username = $u", "alice"));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM user_tags WHERE username = $u", "alice"));
        // バケット側が同じ tweet_id を持つので tweet_media 行は残る
        Assert.Equal(1, Count("SELECT COUNT(*) FROM tweet_media WHERE tweet_id = '1'"));
        Assert.Equal(1, Count("SELECT COUNT(*) FROM tweets WHERE username = $u", bucket));
        // read_state は消さない (他アーカイブの既読を壊さないため)
        Assert.Equal(1, Count("SELECT COUNT(*) FROM read_state WHERE tweet_id = '1'"));
        // 一覧から消え、選択も外れている
        Assert.DoesNotContain(vm.Users, u => u.Username == "alice");
        Assert.NotEqual(alice, vm.SelectedUser);
        // フォルダは _trash へ退避 (元の場所には無い)
        Assert.False(Directory.Exists(Path.Combine(_dataDir, "alice")));
        Assert.True(File.Exists(Path.Combine(
            ArchiveTrash.TrashDir(_dataDir), "alice", "tweets.jsonl")));
        Assert.Contains(ArchiveTrash.TrashDirName, vm.StatusText);
    });

    [Fact]
    public void DeleteUserWithFiles_RemovesDirectory() => RunOnDispatcher(async () =>
    {
        var users = new UserRepository(_db);
        await users.AddAsync("bob");
        WriteJsonl("bob", TweetLine(2));
        await new JsonlImporter(_db).ImportUserAsync("bob");

        var vm = CreateViewModel();
        await vm.RefreshUsersAsync();
        var bob = vm.Users.Single(u => u.Username == "bob");

        var error = await vm.DeleteUserAsync(bob, deleteFiles: true);

        Assert.Null(error);
        Assert.False(Directory.Exists(Path.Combine(_dataDir, "bob")));
        Assert.False(Directory.Exists(ArchiveTrash.TrashDir(_dataDir)));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM tweets WHERE username = $u", "bob"));
        Assert.Empty(vm.Users);
    });

    [Fact]
    public void DeleteSearchKeepingFiles_MovesBucketUnderTrash() => RunOnDispatcher(async () =>
    {
        const string bucket = "searches/kw-del";
        var users = new UserRepository(_db);
        await users.AddAsync(bucket);
        Directory.CreateDirectory(_db.UserDir(bucket));
        File.WriteAllText(Path.Combine(_db.UserDir(bucket), "search.json"),
            """{"query": "kw", "created_at": "2026-07-24T00:00:00+00:00"}""");
        WriteJsonl(bucket, TweetLine(3));
        await new JsonlImporter(_db).ImportUserAsync(bucket);

        var vm = CreateViewModel();
        await vm.RefreshSearchesAsync();
        var search = vm.Searches.Single();
        vm.SelectedSearch = search;

        var error = await vm.DeleteSearchAsync(search, deleteFiles: false);

        Assert.Null(error);
        Assert.Empty(vm.Searches);
        Assert.Null(vm.SelectedSearch);
        // searches/ の階層ごと _trash 配下へ移る (自動登録の走査対象外)
        Assert.True(File.Exists(Path.Combine(
            ArchiveTrash.TrashDir(_dataDir), "searches", "kw-del", "tweets.jsonl")));
        Assert.False(Directory.Exists(Path.Combine(_dataDir, "searches", "kw-del")));
    });

    [Fact]
    public void MoveToTrashTwice_AddsTimestampSuffix()
    {
        Directory.CreateDirectory(Path.Combine(_dataDir, "carol"));
        File.WriteAllText(Path.Combine(_dataDir, "carol", "tweets.jsonl"), "one");
        var first = ArchiveTrash.MoveToTrash(_dataDir, "carol");
        Assert.Equal(Path.Combine(ArchiveTrash.TrashDir(_dataDir), "carol"), first);

        // 同名を再度削除しても上書きせず別名で残す
        Directory.CreateDirectory(Path.Combine(_dataDir, "carol"));
        File.WriteAllText(Path.Combine(_dataDir, "carol", "tweets.jsonl"), "two");
        var second = ArchiveTrash.MoveToTrash(_dataDir, "carol");
        Assert.NotEqual(first, second);
        Assert.StartsWith(Path.Combine(ArchiveTrash.TrashDir(_dataDir), "carol_"), second);
        Assert.Equal("one", File.ReadAllText(Path.Combine(first!, "tweets.jsonl")));
        Assert.Equal("two", File.ReadAllText(Path.Combine(second!, "tweets.jsonl")));
    }

    [Fact]
    public void MoveToTrashReturnsNullWhenMissing() =>
        Assert.Null(ArchiveTrash.MoveToTrash(_dataDir, "nope"));

    [Fact]
    public void RefreshUsersAsyncDropsRowsDeletedInDb() => RunOnDispatcher(async () =>
    {
        var users = new UserRepository(_db);
        await users.AddAsync("dave");
        var vm = CreateViewModel();
        await vm.RefreshUsersAsync();
        vm.SelectedUser = vm.Users.Single(u => u.Username == "dave");

        await users.DeleteArchiveAsync("dave");
        await vm.RefreshUsersAsync();

        Assert.Empty(vm.Users);
        Assert.Null(vm.SelectedUser);
    });

    /// <summary>
    /// ダイアログの XAML 解析と文言切り替えを GUI 無しで検証する
    /// (XAML のエラーは実行時にしか出ないため、生成できること自体に意味がある)。
    /// </summary>
    [Fact]
    public void DeleteArchiveDialog_TogglesNoteAndDefaultsToKeepingFiles()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dialog = new Views.DeleteArchiveDialog("@alice を削除しますか?", 1234);
                Assert.False(dialog.DeleteFiles);   // 既定はファイルを残す
                var keepNote = FindNote(dialog);
                Assert.Contains(ArchiveTrash.TrashDirName, keepNote);

                FindCheckBox(dialog).IsChecked = true;
                var deleteNote = FindNote(dialog);
                Assert.Contains("復元できません", deleteNote);
                Assert.DoesNotContain(ArchiveTrash.TrashDirName, deleteNote);
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
            throw new Xunit.Sdk.XunitException($"ダイアログのテストが失敗: {failure}");

        static System.Windows.Controls.CheckBox FindCheckBox(System.Windows.Window w) =>
            (System.Windows.Controls.CheckBox)w.FindName("DeleteFilesBox")!;
        static string FindNote(System.Windows.Window w) =>
            ((System.Windows.Controls.TextBlock)w.FindName("NoteText")!).Text;
    }

    /// <summary>
    /// 回帰テスト: Grid.Row を省いた要素が行 0 に集まり文言が重なって描画された不具合。
    /// 実際にレイアウトを走らせて各要素が縦に重なっていないことを確認する。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeleteArchiveDialog_ElementsDoNotOverlap(bool deleteFilesChecked)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                // 見出しが折り返す長めのラベルでも重ならないことを見たいので長い名前を使う
                var dialog = new Views.DeleteArchiveDialog(
                    "検索「(_Slay_the_Spire_2_OR_sts2_OR_スレスパ2)_lan」を削除しますか?", 123456);
                ((System.Windows.Controls.CheckBox)dialog.FindName("DeleteFilesBox")!).IsChecked =
                    deleteFilesChecked;

                // 未表示の Window は視覚ツリーの根にならないので、内容の Grid を基準に測る
                var root = (System.Windows.Controls.Grid)dialog.Content;
                root.Measure(new System.Windows.Size(dialog.Width, double.PositiveInfinity));
                root.Arrange(new System.Windows.Rect(
                    new System.Windows.Point(0, 0), root.DesiredSize));
                root.UpdateLayout();

                var names = new[] { "HeadlineText", "CountText", "DeleteFilesBox", "NoteText" };
                var previousBottom = double.NegativeInfinity;
                var previousName = "(先頭)";
                foreach (var name in names)
                {
                    var element = (System.Windows.FrameworkElement)dialog.FindName(name)!;
                    var top = element.TransformToAncestor(root)
                        .Transform(new System.Windows.Point(0, 0)).Y;
                    var height = element.RenderSize.Height;
                    Assert.True(height > 0, $"{name} の高さが 0 です");
                    Assert.True(top >= previousBottom,
                        $"{name} (top={top}) が {previousName} (bottom={previousBottom}) と重なっています");
                    previousBottom = top + height;
                    previousName = name;
                }
                // ボタンは最下段
                var buttons = root.Children.OfType<System.Windows.Controls.StackPanel>().Single();
                var buttonsTop = buttons.TransformToAncestor(root)
                    .Transform(new System.Windows.Point(0, 0)).Y;
                Assert.True(buttonsTop >= previousBottom,
                    $"ボタン (top={buttonsTop}) が NoteText (bottom={previousBottom}) と重なっています");
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
            throw new Xunit.Sdk.XunitException($"レイアウトのテストが失敗: {failure}");
    }

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
