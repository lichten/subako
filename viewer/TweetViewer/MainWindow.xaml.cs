using System.Windows;
using System.Windows.Controls;
using TweetViewer.Services;
using TweetViewer.ViewModels;
using TweetViewer.Views;

namespace TweetViewer;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel Vm => (MainViewModel)DataContext;

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

    private void MenuBackfill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: UserItemViewModel user } || Vm.IsFetching)
            return;
        var dialog = new BackfillDialog { Owner = this };
        if (dialog.ShowDialog() == true)
            StartFetch(user.Username, FetchMode.Backfill, dialog.MaxRequests);
    }

    private void StartFetch(string username, FetchMode mode, int? maxRequests)
    {
        if (Vm.IsFetching)
            return;
        Vm.IsFetching = true;
        var dialogVm = new FetchDialogViewModel(
            Vm.FetchService, username, Vm.OnFetchCompletedAsync, mode, maxRequests);
        var window = new UpdateLogWindow { Owner = this, DataContext = dialogVm };
        window.Show();
        _ = RunFetchAsync(dialogVm);
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
