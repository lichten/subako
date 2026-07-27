using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Xaml.Behaviors;

namespace TweetViewer.Behaviors;

/// <summary>
/// リスト内容のリセット (ObservableCollection.Clear = Reset 通知) でスクロールを先頭へ戻す。
/// WPF の VirtualizingStackPanel は ItemsSource の中身を入れ替えてもオフセットを 0 に
/// 戻さないため、ユーザー切替時に前のスクロール位置が残るのを防ぐ。
///
/// Clear の時点でしか戻さないと不十分: ViewModel は Clear のあと await を挟んで
/// 別のディスパッチャターンで項目を追加するため、「空のリストを先頭にする」だけになり、
/// 中身が入ったときの位置は仮想化パネルの再計算任せになってしまう (まれに最下段になる)。
/// そこで中身が入った状態で先頭を確定できるまで要求を保留する。
/// </summary>
public sealed class ScrollToTopOnResetBehavior : Behavior<ListBox>
{
    private ScrollViewer? _scrollViewer;
    private INotifyCollectionChanged? _items;
    /// <summary>リセット後、中身が入った状態で先頭を確定できるまで true。</summary>
    private bool _pending;
    /// <summary>BeginInvoke の重複投入防止 (1ページ分の Add が連続で来るため)。</summary>
    private bool _scheduled;

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
            _pending = true;
        if (_pending)
            ScheduleScrollToTop();
    }

    /// <summary>
    /// Background 優先度 = このターンのレイアウトが終わってから走るので、
    /// 追加された項目の分まで反映された状態で先頭に戻せる。
    /// </summary>
    private void ScheduleScrollToTop()
    {
        if (_scheduled)
            return;
        _scheduled = true;
        AssociatedObject.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _scheduled = false;
            _scrollViewer?.ScrollToHome();
            if (AssociatedObject.Items.Count > 0)
                _pending = false;   // 中身がある状態で先頭を確定できた
        });
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
