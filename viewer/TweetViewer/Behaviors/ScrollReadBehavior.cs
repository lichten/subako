using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Xaml.Behaviors;
using TweetViewer.ViewModels;

namespace TweetViewer.Behaviors;

/// <summary>
/// スクロールが 300ms 静止したら、ビューポート内に完全表示されている
/// (またはビューポートより背が高く下端を通過した)ツイートを既読にする。
/// 実体化済みコンテナのみ走査するため 33k 行でも軽い。
/// </summary>
public sealed class ScrollReadBehavior : Behavior<ListBox>
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    private ScrollViewer? _scrollViewer;
    private DispatcherTimer? _timer;

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
        Detach();
        base.OnDetaching();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _scrollViewer = FindDescendant<ScrollViewer>(AssociatedObject);
        if (_scrollViewer is null)
            return;
        _scrollViewer.ScrollChanged += OnScrollChanged;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Debounce };
        _timer.Tick += OnSettled;
        // 初期表示分も既読対象にする
        _timer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Detach();

    private void Detach()
    {
        if (_scrollViewer is not null)
            _scrollViewer.ScrollChanged -= OnScrollChanged;
        _scrollViewer = null;
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnSettled;
            _timer = null;
        }
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // スクロール中はタイマーを巻き戻す(静止した時点のビューポートだけ数える)
        _timer?.Stop();
        _timer?.Start();
    }

    private void OnSettled(object? sender, EventArgs e)
    {
        _timer?.Stop();
        if (_scrollViewer is null)
            return;
        if (AssociatedObject.DataContext is not TweetListViewModel listVm)
            return;

        var panel = FindDescendant<VirtualizingStackPanel>(AssociatedObject);
        if (panel is null)
            return;

        var viewportHeight = _scrollViewer.ViewportHeight;
        // ScrollUnit=Pixel なので ViewportHeight はピクセル
        foreach (var child in panel.Children)
        {
            if (child is not ListBoxItem container)
                continue;
            if (container.DataContext is not TweetItemViewModel item || item.IsRead)
                continue;

            double top;
            try
            {
                top = container.TransformToAncestor(_scrollViewer).Transform(new Point(0, 0)).Y;
            }
            catch (InvalidOperationException)
            {
                continue;   // リサイクル直後などツリー未接続
            }
            var bottom = top + container.ActualHeight;

            var fullyVisible = top >= 0 && bottom <= viewportHeight;
            var tallItemPassed = container.ActualHeight > viewportHeight && bottom <= viewportHeight && bottom >= 0;
            if (fullyVisible || tallItemPassed)
                listVm.MarkSeen(item);
        }
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
