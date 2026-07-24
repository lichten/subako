using System.Windows;
using TweetViewer.ViewModels;

namespace TweetViewer.Views;

public partial class ManageTagsDialog : Window
{
    private readonly MainViewModel _vm;

    public ManageTagsDialog(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (TagListBox.SelectedItem is not TagItemViewModel tag)
            return;
        if (tag.UserCount > 0)
        {
            var result = MessageBox.Show(
                $"「{tag.Name}」は {tag.UserCount} 人に付いています。削除しますか?",
                "TweetViewer", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
                return;
        }
        await _vm.DeleteTagAsync(tag);
    }
}
