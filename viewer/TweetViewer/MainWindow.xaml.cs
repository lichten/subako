using System.Windows;
using System.Windows.Controls;
using TweetViewer.Services;
using TweetViewer.ViewModels;
using TweetViewer.Views;

namespace TweetViewer;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;

    public MainWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        RestoreWindowPlacement();
    }

    /// <summary>前回終了時のウィンドウ配置とサイドバー幅を復元する。画面外 (モニタ構成変更) なら既定のまま。</summary>
    private void RestoreWindowPlacement()
    {
        if (_settings is { WindowLeft: { } left, WindowTop: { } top,
                           WindowWidth: { } width, WindowHeight: { } height }
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
        if (SidebarColumn.ActualWidth > 0)
            _settings.SidebarWidth = SidebarColumn.ActualWidth;
        _settings.Save();
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
                    "TweetViewer", MessageBoxButton.YesNo, MessageBoxImage.Question);
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
                "TweetViewer", MessageBoxButton.YesNo, MessageBoxImage.Question);
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
        Vm.IsFetching = true;
        var dialogVm = new FetchDialogViewModel(
            Vm.FetchService, username, Vm.OnFetchCompletedAsync, mode, maxRequests, searchQuery,
            backfillSince);
        var window = new UpdateLogWindow { Owner = this, DataContext = dialogVm };
        window.Show();
        _ = RunFetchAsync(dialogVm);
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

    private async void MenuSearchDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: SearchItemViewModel item })
            return;
        var result = MessageBox.Show(
            $"検索「{item.Query}」を削除しますか?\n保存済みの {item.TweetCount:N0}件と画像も削除されます。",
            "TweetViewer", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
            await Vm.DeleteSearchAsync(item);
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
        }
    }
}
