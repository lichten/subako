using System.Windows;
using System.Windows.Controls;
using TweetViewer.Services;
using TweetViewer.ViewModels;
using TweetViewer.Views;

namespace TweetViewer;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;

    /// <summary>
    /// 実行中の取得ログウィンドウ。開始時は表示せず、ステータスバーのリンクか
    /// 失敗時の自動表示で開く。IsFetching ガードにより常に現在の実行に対応する。
    /// </summary>
    private UpdateLogWindow? _fetchLogWindow;

    public MainWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        RestoreWindowPlacement();
    }

    /// <summary>前回終了時のウィンドウ配置とサイドバー幅を復元する。画面外 (モニタ構成変更) なら既定のまま。</summary>
    private void RestoreWindowPlacement()
    {
        if (_settings is
            {
                WindowLeft: { } left, WindowTop: { } top,
                WindowWidth: { } width, WindowHeight: { } height
            }
            && width > 0 && height > 0
            && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
            && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight
            && left + width > SystemParameters.VirtualScreenLeft
            && top + height > SystemParameters.VirtualScreenTop)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }
        // 通常時の矩形を先に適用しておくことで、最大化解除時に前回サイズへ戻る
        if (_settings.WindowMaximized)
            WindowState = WindowState.Maximized;
        if (_settings.SidebarWidth is { } sidebarWidth && sidebarWidth > 0)
            SidebarColumn.Width = new GridLength(sidebarWidth);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // 最大化/最小化中は通常状態の矩形 (RestoreBounds) を保存する
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;
        if (!bounds.IsEmpty)
        {
            _settings.WindowLeft = bounds.Left;
            _settings.WindowTop = bounds.Top;
            _settings.WindowWidth = bounds.Width;
            _settings.WindowHeight = bounds.Height;
        }
        _settings.WindowMaximized = WindowState == WindowState.Maximized;
        _settings.UnreadOnly = Vm.UnreadOnly;
        _settings.TagFilterId = Vm.SelectedTagFilter?.TagId;
        if (SidebarColumn.ActualWidth > 0)
            _settings.SidebarWidth = SidebarColumn.ActualWidth;
        _settings.Save();
        // 取得中でもアプリ終了をログウィンドウの Hide 化に妨げられないようにする
        if (_fetchLogWindow is { } logWindow)
            logWindow.ForceClose = true;
        base.OnClosing(e);
    }

    private MainViewModel Vm => (MainViewModel)DataContext;

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(_settings) { Owner = this };
        dialog.ShowDialog();
    }

    private async void AddUser_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddUserDialog { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Username is { Length: > 0 } username)
        {
            var added = await Vm.AddUserAsync(username);
            if (added is not null && added.TweetCount == 0)
            {
                var run = MessageBox.Show(
                    $"@{added.Username} を追加しました。今すぐツイートを取得しますか?",
                    AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (run == MessageBoxResult.Yes)
                    StartFetch(added.Username, FetchMode.Update, maxRequests: null);
            }
        }
    }

    /// <summary>ツイート右クリック「@xxx をユーザーに追加」。表示中ユーザー/検索のタグを引き継ぐ。</summary>
    private async void MenuAddAuthor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: TweetItemViewModel tweet } ||
            tweet.AddableAuthorUsername is not { } username)
            return;
        var (added, isNew) = await Vm.AddAuthorFromTweetAsync(username);
        if (isNew && added is { TweetCount: 0 })
        {
            var run = MessageBox.Show(
                $"@{added.Username} を追加しました。今すぐツイートを取得しますか?",
                AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (run == MessageBoxResult.Yes)
                StartFetch(added.Username, FetchMode.Update, maxRequests: null);
        }
    }

    private void UpdateUser_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: UserItemViewModel user })
            StartFetch(user.Username, FetchMode.Update, maxRequests: null);
    }

    private void MenuUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: UserItemViewModel user })
            StartFetch(user.Username, FetchMode.Update, maxRequests: null);
    }

    private void ClearTagFilter_Click(object sender, RoutedEventArgs e) =>
        Vm.SelectedTagFilter = null;

    /// <summary>「タグ」サブメニューを開くたびに現在のタグ一覧から項目を作り直す (ユーザー行・検索行の両対応)。</summary>
    private void TagMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menu)
            return;
        Func<long, bool> hasTag;
        Func<TagItemViewModel, bool, Task> toggle;
        string username;
        switch (menu.DataContext)
        {
            case UserItemViewModel user:
                hasTag = user.HasTag;
                toggle = (tag, on) => Vm.ToggleTagAsync(user, tag, on);
                username = user.Username;
                break;
            case SearchItemViewModel search:
                hasTag = search.HasTag;
                toggle = (tag, on) => Vm.ToggleTagAsync(search, tag, on);
                username = search.Username;
                break;
            default:
                return;
        }
        menu.Items.Clear();
        foreach (var tag in Vm.Tags)
        {
            var item = new MenuItem
            {
                Header = tag.Name,
                IsCheckable = true,
                IsChecked = hasTag(tag.TagId),
                StaysOpenOnClick = true,   // 連続で付け外しできるように
            };
            var captured = tag;
            item.Click += async (_, _) => await toggle(captured, item.IsChecked);
            menu.Items.Add(item);
        }
        if (Vm.Tags.Count > 0)
            menu.Items.Add(new Separator());
        var add = new MenuItem { Header = "新しいタグ..." };
        add.Click += (_, _) => AddTag(username);
        menu.Items.Add(add);
        var manage = new MenuItem { Header = "タグの整理..." };
        manage.Click += (_, _) => new ManageTagsDialog(Vm) { Owner = this }.ShowDialog();
        menu.Items.Add(manage);
    }

    private async void AddTag(string username)
    {
        var dialog = new AddTagDialog { Owner = this };
        if (dialog.ShowDialog() == true && dialog.TagName is { Length: > 0 } name)
            await Vm.CreateAndAssignTagAsync(name, username);
    }

    /// <summary>保存済み JSONL から未取得画像だけ補完する (引用先・RT元の画像。API 不使用)。</summary>
    private void MenuFetchImages_Click(object sender, RoutedEventArgs e)
    {
        var username = (sender as MenuItem)?.DataContext switch
        {
            UserItemViewModel user => user.Username,
            SearchItemViewModel search => search.Username,
            _ => null,
        };
        if (username is not null)
            StartFetch(username, FetchMode.ImagesOnly, maxRequests: null);
    }

    private void MenuBackfill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: UserItemViewModel user } || Vm.IsFetching)
            return;
        var dialog = new BackfillDialog { Owner = this };
        if (dialog.ShowDialog() == true)
            StartFetch(user.Username, FetchMode.Backfill, dialog.MaxRequests);
    }

    private void StartFetch(string username, FetchMode mode, int? maxRequests,
        string? searchQuery = null, string? backfillSince = null)
    {
        if (Vm.IsFetching)
            return;
        var dialogVm = new FetchDialogViewModel(
            Vm.FetchService, username, Vm.OnFetchCompletedAsync, mode, maxRequests, searchQuery,
            backfillSince);
        LaunchFetch(dialogVm);
    }

    /// <summary>
    /// 取得を開始する。ログウィンドウは作るが表示しない — 実行中はステータスバーの
    /// 「取得中 — ログを表示」から開き、失敗・中断時だけ完了時に自動表示する。
    /// </summary>
    private void LaunchFetch(FetchDialogViewModel dialogVm)
    {
        Vm.IsFetching = true;
        var window = new UpdateLogWindow { Owner = this, DataContext = dialogVm };
        window.Closed += (_, _) =>
        {
            if (_fetchLogWindow == window)
                _fetchLogWindow = null;
        };
        _fetchLogWindow = window;
        _ = RunFetchAsync(dialogVm);
    }

    private void ShowFetchLog_Click(object sender, RoutedEventArgs e)
    {
        if (_fetchLogWindow is not { } window)
            return;
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Show();      // 非表示なら初回表示 (CenterOwner 適用)、表示済みなら no-op
        window.Activate();
    }

    /// <summary>
    /// サイドバーに表示中 (タグフィルタ適用後) のユーザーと検索を順に差分更新する。
    /// StartFetch は IsFetching ガードで単発前提のため、ここで同じ形のループを組む。
    /// </summary>
    private void UpdateAllVisible_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.IsFetching)
            return;
        // 列挙中に Refresh() が走ると例外になるため、開始時にスナップショットを取る
        var users = Vm.UsersView.Cast<UserItemViewModel>().ToList();
        var searches = Vm.SearchesView.Cast<SearchItemViewModel>().ToList();
        if (users.Count + searches.Count == 0)
        {
            Vm.StatusText = "更新できる対象が表示されていません";
            return;
        }

        var dialog = new UpdateAllDialog(users.Count, searches.Count) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var targets = users
            .Select(u => new FetchTarget(u.Username, $"@{u.Username}", FetchMode.Update))
            .Concat(searches.Select(s =>
                new FetchTarget(s.Username, $"検索「{s.Label}」", FetchMode.SearchUpdate, s.Query)))
            .ToList();

        var dialogVm = new FetchDialogViewModel(
            Vm.FetchService, targets, Vm.OnFetchCompletedAsync, dialog.MaxRequests);
        LaunchFetch(dialogVm);
    }

    /// <summary>
    /// 「フォロー中を一括登録」。指定アカウントのフォロー中を取得し (ツイートは取らない)、
    /// 全員を users に登録して選んだタグを付ける。取得完了後の処理が通常の取込
    /// (Vm.OnFetchCompletedAsync) と違うため、UpdateAllVisible_Click と同じく
    /// StartFetch を通さず FetchDialogViewModel を直接組み立てる。
    /// </summary>
    private async void ImportFollowings_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.IsFetching)
            return;
        var dialog = new ImportFollowingsDialog(Vm.Tags) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        if (MainViewModel.NormalizeUsername(dialog.SourceUsername) is not { } source)
        {
            MessageBox.Show("ユーザー名は英数字と _ のみ使用できます。",
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var tagIds = dialog.SelectedTagIds;
        var newTagNames = dialog.NewTagNames;

        // 前回取得したフォロー一覧が残っていれば API を使わず登録し直せる
        // (タグを選び直したいだけのときに再課金しないため)
        var saved = Vm.SavedFollowingsCount(source);
        if (saved > 0)
        {
            var reuse = MessageBox.Show(
                $"@{source} の取得済みフォロー一覧 ({saved:N0} 件) があります。\n" +
                "API を使わずにこれを登録しますか?\n" +
                "「いいえ」を選ぶと取得し直します。",
                AppInfo.Name, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (reuse == MessageBoxResult.Cancel)
                return;
            if (reuse == MessageBoxResult.Yes)
            {
                await ConfirmAndImportFollowingsAsync(source, tagIds, newTagNames);
                return;
            }
        }

        var dialogVm = new FetchDialogViewModel(
            Vm.FetchService, source,
            _ => ConfirmAndImportFollowingsAsync(source, tagIds, newTagNames),
            FetchMode.Followings, dialog.MaxRequests);
        dialogVm.WindowTitle = $"フォロー中を取得: @{source}";
        LaunchFetch(dialogVm);
    }

    /// <summary>
    /// 登録直前に件数を見せて確認する。数千行がいきなりサイドバーに増えるため。
    /// 中断でプロセスを Kill された実行は fetcher がファイルを残さないので、
    /// ここで 0 件 = 取得できなかった、と判定できる (古い結果を誤って取り込まない)。
    /// </summary>
    private async Task ConfirmAndImportFollowingsAsync(
        string source, IReadOnlyList<long> tagIds, IReadOnlyList<string> newTagNames)
    {
        var count = Vm.SavedFollowingsCount(source);
        if (count == 0)
        {
            Vm.StatusText = $"@{source} のフォロー一覧を取得できませんでした " +
                            "(中断・非公開アカウント・存在しないアカウントなど)";
            return;
        }
        var ok = MessageBox.Show(
            $"@{source} のフォロー {count:N0} 件をユーザーとして登録します。よろしいですか?\n\n" +
            "ツイートは取得しません。登録後にタグで絞り込んでから" +
            "「表示中をすべて更新...」を実行してください。",
            AppInfo.Name, MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (ok != MessageBoxResult.OK)
        {
            Vm.StatusText = "登録を中止しました (取得したフォロー一覧は保存済みで、" +
                            "もう一度同じ操作をすれば API なしで登録できます)";
            return;
        }
        await Vm.ImportFollowingsAsync(source, tagIds, newTagNames);
    }

    private async void AddSearch_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.IsFetching)
            return;
        var dialog = new SearchDialog { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        var (bucketId, finalQuery) = await Vm.StartApiSearchAsync(
            dialog.Query, dialog.MinRetweets, dialog.MinFaves, dialog.SearchName);
        StartFetch(bucketId, FetchMode.Search, dialog.MaxRequests, finalQuery);
    }

    private void MenuSearchUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: SearchItemViewModel item } || Vm.IsFetching)
            return;
        // リクエスト上限は差分更新でも必須 (BackfillDialog を上限入力に再利用)
        var dialog = new BackfillDialog { Owner = this, Title = "検索を更新 (差分取得)" };
        if (dialog.ShowDialog() == true)
            StartFetch(item.Username, FetchMode.SearchUpdate, dialog.MaxRequests, item.Query);
    }

    private void MenuSearchBackfill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: SearchItemViewModel item } || Vm.IsFetching)
            return;
        var dialog = new SearchBackfillDialog { Owner = this };
        if (dialog.ShowDialog() == true)
            StartFetch(item.Username, FetchMode.SearchBackfill, dialog.MaxRequests,
                item.Query, dialog.Since);
    }

    private async void MenuSearchEdit_Click(object sender, RoutedEventArgs e)
    {
        // 取得中の編集はクエリ書き換えが競合するため不可
        if (sender is not MenuItem { DataContext: SearchItemViewModel item } || Vm.IsFetching)
            return;
        var dialog = new SearchEditDialog(item.Name, item.Query) { Owner = this };
        if (dialog.ShowDialog() == true)
            await Vm.UpdateSearchAsync(item, dialog.Query, dialog.SearchName);
    }

    /// <summary>取得中の削除は Python 側がフォルダを書き戻すため不可。</summary>
    private async void MenuSearchDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: SearchItemViewModel item } || Vm.IsFetching)
            return;
        var dialog = new DeleteArchiveDialog($"検索「{item.Label}」を削除しますか?", item.TweetCount)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true)
            ReportDeleteFailure(await Vm.DeleteSearchAsync(item, dialog.DeleteFiles));
    }

    private async void MenuUserDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: UserItemViewModel item } || Vm.IsFetching)
            return;
        var dialog = new DeleteArchiveDialog($"@{item.Username} を削除しますか?", item.TweetCount)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true)
            ReportDeleteFailure(await Vm.DeleteUserAsync(item, dialog.DeleteFiles));
    }

    /// <summary>
    /// フォルダ操作が失敗するとステータスバーだけでは見落とし、
    /// 次回起動の自動登録で復活して混乱するため明示的に知らせる。
    /// </summary>
    private void ReportDeleteFailure(string? error)
    {
        if (error is not null)
            MessageBox.Show(error, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>動画リンクのサムネイルをクリックしたら動画ページをブラウザで開く。</summary>
    private void LinkThumbnail_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LinkThumbnailViewModel item })
            Browser.OpenUrl(item.PageUrl);
    }

    private void MediaCell_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MediaItemViewModel item })
            return;
        // ロード済みリストのスナップショットで前後移動できるビューアを開く
        var items = Vm.MediaGrid.FlatItems.ToList();
        var index = items.IndexOf(item);
        if (index < 0)
            return;
        var viewer = new MediaViewerWindow(items, index) { Owner = this };
        viewer.Show();
    }

    private async Task RunFetchAsync(FetchDialogViewModel dialogVm)
    {
        try
        {
            await dialogVm.StartAsync();
        }
        finally
        {
            Vm.IsFetching = false;
            // await の継続は UI スレッド (Dispatcher) 上なので Window 操作可
            if (_fetchLogWindow is { } window)
            {
                if (dialogVm.HasIssues)
                {
                    // 失敗・中断・上限到達のときだけ自動表示 (成功時はステータスバーのみ)
                    if (window.WindowState == WindowState.Minimized)
                        window.WindowState = WindowState.Normal;
                    window.Show();
                    window.Activate();
                }
                else if (!window.IsVisible)
                {
                    // 一度も見られず成功 → そのまま破棄 (Closed でフィールドが null に)
                    window.Close();
                }
                // 表示中に成功した場合は開いたまま (「取得完了」を見せる)。
                // IsRunning=false なので ✕ で普通に閉じられる
            }
        }
    }
}
