using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Xaml.Behaviors;

namespace TweetViewer.Behaviors;

/// <summary>末尾まで残り2画面分を切ったら LoadMoreCommand を実行する。</summary>
public sealed class InfiniteScrollBehavior : Behavior<ListBox>
{
    public static readonly DependencyProperty LoadMoreCommandProperty =
        DependencyProperty.Register(
            nameof(LoadMoreCommand), typeof(ICommand), typeof(InfiniteScrollBehavior));

    public ICommand? LoadMoreCommand
    {
        get => (ICommand?)GetValue(LoadMoreCommandProperty);
        set => SetValue(LoadMoreCommandProperty, value);
    }

    private ScrollViewer? _scrollViewer;

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.Loaded += OnLoaded;
        AssociatedObject.Unloaded += OnUnloaded;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.Loaded -= OnLoaded;
        AssociatedObject.Unloaded -= OnUnloaded;
        Unhook();
        base.OnDetaching();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _scrollViewer = FindDescendant<ScrollViewer>(AssociatedObject);
        if (_scrollViewer is not null)
            _scrollViewer.ScrollChanged += OnScrollChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Unhook();

    private void Unhook()
    {
        if (_scrollViewer is not null)
            _scrollViewer.ScrollChanged -= OnScrollChanged;
        _scrollViewer = null;
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_scrollViewer is null)
            return;
        if (!ShouldLoadMore(_scrollViewer.ExtentHeight, _scrollViewer.VerticalOffset,
                _scrollViewer.ViewportHeight))
            return;
        if (LoadMoreCommand is { } cmd && cmd.CanExecute(null))
            cmd.Execute(null);
    }

    /// <summary>
    /// 末尾まで残り2画面を切ったら true。
    /// リストが空のとき (リセット直後の Clear と項目追加の間) は残りが負になって
    /// 必ず条件が成立してしまうため、明示的に除外する — ここで追加ロードが走ると
    /// リセット中の表示位置が乱れる。
    /// </summary>
    public static bool ShouldLoadMore(double extentHeight, double verticalOffset, double viewportHeight)
    {
        if (extentHeight <= 0 || viewportHeight <= 0)
            return false;
        return extentHeight - (verticalOffset + viewportHeight) < viewportHeight * 2;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                return match;
            if (FindDescendant<T>(child) is { } nested)
                return nested;
        }
        return null;
    }
}
