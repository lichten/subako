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

    /// <summary>「タグ」サブメニューを開くたびに現在のタグ一覧から項目を作り直す。</summary>
    private void TagMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: UserItemViewModel user } menu)
            return;
        menu.Items.Clear();
        foreach (var tag in Vm.Tags)
        {
            var item = new MenuItem
            {
                Header = tag.Name,
                IsCheckable = true,
                IsChecked = user.HasTag(tag.TagId),
                StaysOpenOnClick = true,   // 連続で付け外しできるように
            };
            var captured = tag;
            item.Click += async (_, _) => await Vm.ToggleTagAsync(user, captured, item.IsChecked);
            menu.Items.Add(item);
        }
        if (Vm.Tags.Count > 0)
            menu.Items.Add(new Separator());
        var add = new MenuItem { Header = "新しいタグ..." };
        add.Click += (_, _) => AddTag(user);
        menu.Items.Add(add);
        var manage = new MenuItem { Header = "タグの整理..." };
        manage.Click += (_, _) => new ManageTagsDialog(Vm) { Owner = this }.ShowDialog();
        menu.Items.Add(manage);
    }

    private async void AddTag(UserItemViewModel user)
    {
        var dialog = new AddTagDialog { Owner = this };
        if (dialog.ShowDialog() == true && dialog.TagName is { Length: > 0 } name)
            await Vm.CreateAndAssignTagAsync(name, user);
    }

    private void MenuBackfill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: UserItemViewModel user } || Vm.IsFetching)
            return;
        var dialog = new BackfillDialog { Owner = this };
        if (dialog.ShowDialog() == true)
            StartFetch(user.Username, FetchMode.Backfill, dialog.MaxRequests);
    }

    private void StartFetch(string username, FetchMode mode, int? maxRequests, string? searchQuery = null)
    {
        if (Vm.IsFetching)
            return;
        Vm.IsFetching = true;
        var dialogVm = new FetchDialogViewModel(
            Vm.FetchService, username, Vm.OnFetchCompletedAsync, mode, maxRequests, searchQuery);
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
            dialog.Query, dialog.MinRetweets, dialog.MinFaves);
        StartFetch(bucketId, FetchMode.Search, dialog.MaxRequests, finalQuery);
    }

    private void MenuSearchUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: SearchItemViewModel item } || Vm.IsFetching)
            return;
        // リクエスト上限は差分更新でも必須 (BackfillDialog を上限入力に再利用)
        var dialog = new BackfillDialog { Owner = this, Title = "検索を差分更新" };
        if (dialog.ShowDialog() == true)
            StartFetch(item.Username, FetchMode.Search, dialog.MaxRequests, item.Query);
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
