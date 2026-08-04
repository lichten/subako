using System.Windows;
using System.Windows.Input;
using TweetViewer.Behaviors;
using TweetViewer.Services;
using TweetViewer.ViewModels;

namespace TweetViewer.Views;

public partial class MediaViewerWindow : Window
{
    private readonly IReadOnlyList<MediaItemViewModel> _items;
    private int _index;

    public MediaViewerWindow(IReadOnlyList<MediaItemViewModel> items, int startIndex)
    {
        InitializeComponent();
        _items = items;
        _index = Math.Clamp(startIndex, 0, items.Count - 1);
        ShowItem(_index);
    }

    private void ShowItem(int index)
    {
        _index = index;
        var item = _items[_index];
        ImagePathBehavior.SetPath(MainImage, item.LocalPath);
        BodyText.Text = item.FullText;
        MetaText.Text = $"{item.CreatedAtLocal}   ({_index + 1}/{_items.Count})";
        Title = $"画像ビューア - @{item.Username}";
    }

    private void Prev_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void Next_Click(object sender, RoutedEventArgs e) => Move(+1);

    private void Move(int delta)
    {
        var next = _index + delta;
        if (next >= 0 && next < _items.Count)
            ShowItem(next);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
                Move(-1);
                e.Handled = true;
                break;
            case Key.Right:
                Move(+1);
                e.Handled = true;
                break;
        }
    }

    private void OpenInBrowser_Click(object sender, RoutedEventArgs e)
    {
        var item = _items[_index];
        Browser.OpenUrl(TweetUrl.Status(item.TweetId, item.Username, item.AuthorUsername));
    }
}
