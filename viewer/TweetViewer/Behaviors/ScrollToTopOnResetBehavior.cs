using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Xaml.Behaviors;

namespace TweetViewer.Behaviors;

/// <summary>
/// リスト内容のリセット (ObservableCollection.Clear = Reset 通知) でスクロールを先頭へ戻す。
/// WPF の VirtualizingStackPanel は ItemsSource の中身を入れ替えてもオフセットを 0 に
/// 戻さないため、ユーザー切替時に前のスクロール位置が残るのを防ぐ。
/// </summary>
public sealed class ScrollToTopOnResetBehavior : Behavior<ListBox>
{
    private ScrollViewer? _scrollViewer;
    private INotifyCollectionChanged? _items;

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.Loaded += OnLoaded;
        AssociatedObject.Unloaded += OnUnloaded;
        _items = AssociatedObject.Items;
        _items.CollectionChanged += OnCollectionChanged;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.Loaded -= OnLoaded;
        AssociatedObject.Unloaded -= OnUnloaded;
        if (_items is not null)
            _items.CollectionChanged -= OnCollectionChanged;
        _items = null;
        _scrollViewer = null;
        base.OnDetaching();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) =>
        _scrollViewer = FindDescendant<ScrollViewer>(AssociatedObject);

    private void OnUnloaded(object sender, RoutedEventArgs e) => _scrollViewer = null;

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
            _scrollViewer?.ScrollToHome();
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
