using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Xaml.Behaviors;

namespace TweetViewer.Behaviors;

/// <summary>
/// 上下/PageUp/PageDown/Home/End を「選択の移動」ではなく「ピクセル単位のスクロール」に置き換える。
///
/// ListBox (Selector) の既定のキー操作は隣の ListBoxItem を選択して BringIntoView するため、
/// 1回の押下で必ずツイート1件ぶん飛ぶ。ビューポートより背の高いツイートは途中が
/// 一切表示されないまま通過してしまい、画面の狭い環境では本文を読み切れない。
/// ScrollUnit=Pixel によりホイールとスクロールバーは既に滑らかなので、
/// 残る原因はキーボードナビゲーションだけ。
///
/// PreviewKeyDown (トンネル) で捕まえて Handled にする。ListBox はキーを OnKeyDown
/// (バブル) でしか見ておらず、WPF はトンネルイベントが未処理で返ってきたときだけ
/// バブルイベントを発火させるため、ここで Handled にすると KeyDown 自体が発生せず
/// 選択は一切動かない。なおテンプレート内の ScrollViewer は TemplatedParent の
/// HandlesScrolling が true (= ListBox) のときキー処理を放棄するので、競合相手は ListBox だけ。
///
/// 目標オフセットを溜め込まない (状態を持たない) のが重要:
///  - 溜めるとユーザー切替時に ScrollToTopOnResetBehavior の先頭復帰保留と綱引きになる
///  - 仮想化パネルは項目の実体化に応じて Extent を再推定するため、溜めた値はすぐ陳腐化する
/// </summary>
public sealed class KeyboardScrollBehavior : Behavior<ListBox>
{
    /// <summary>
    /// 矢印キー1回の移動量 (px)。SystemParameters.WheelScrollLines (既定3) ×
    /// ScrollViewer の1行 16px = ホイール1ノッチと同じ量。
    /// ホイールの操作感は既に良好なのでキーボードもそこに揃える
    /// (Chromium の矢印キー既定 40px ともほぼ同等)。
    /// </summary>
    public const double LineDelta = 48.0;

    /// <summary>PageUp/PageDown で前の画面を数行ぶん残すためのオーバーラップ (px)。</summary>
    private const double PageOverlap = LineDelta;

    /// <summary>
    /// 1画面あたりの最低移動比。ビューポートが低い環境で固定オーバーラップが
    /// 移動量を食い潰して 0 以下になる (= 逆方向に進む) のを防ぐ。
    /// </summary>
    private const double MinPageFraction = 0.875;

    private ScrollViewer? _scrollViewer;

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.PreviewKeyDown += OnPreviewKeyDown;
        AssociatedObject.Unloaded += OnUnloaded;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.PreviewKeyDown -= OnPreviewKeyDown;
        AssociatedObject.Unloaded -= OnUnloaded;
        _scrollViewer = null;
        base.OnDetaching();
    }

    /// <summary>テンプレートが張り直された場合にキャッシュが古いまま残らないようにする。</summary>
    private void OnUnloaded(object sender, RoutedEventArgs e) => _scrollViewer = null;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 将来テンプレートに入力欄が入っても矢印キーを奪わないための保険
        if (e.OriginalSource is TextBoxBase)
            return;

        // Loaded ではなく遅延解決する: ScrollChanged を購読しないので読み込み時点の参照は不要で、
        // キーが届く時点ではテンプレートは必ず適用済み
        _scrollViewer ??= FindDescendant<ScrollViewer>(AssociatedObject);
        if (_scrollViewer is not { } sv)
            return;   // 見つからなければ Handled にせず従来動作に委ねる

        double target;
        switch (e.Key)
        {
            case Key.Up:
                target = NextOffset(sv.VerticalOffset, -LineDelta, sv.ScrollableHeight);
                break;
            case Key.Down:
                target = NextOffset(sv.VerticalOffset, LineDelta, sv.ScrollableHeight);
                break;
            case Key.PageUp:
                target = NextOffset(sv.VerticalOffset, -PageDelta(sv.ViewportHeight), sv.ScrollableHeight);
                break;
            case Key.PageDown:
                target = NextOffset(sv.VerticalOffset, PageDelta(sv.ViewportHeight), sv.ScrollableHeight);
                break;
            case Key.Home:
                target = 0;
                break;
            case Key.End:
                target = sv.ScrollableHeight;
                break;
            default:
                return;
        }

        sv.ScrollToVerticalOffset(target);
        // 端に達していても Handled にする。ここで素通しすると、よりによって端で
        // ListBox の選択移動が復活してしまう
        e.Handled = true;
    }

    /// <summary>
    /// delta だけスクロールした後の垂直オフセット。[0, scrollableHeight] に収める。
    /// scrollableHeight が 0 以下 (1画面に満たない/リセット直後) のときは Math.Clamp が
    /// max &lt; min で例外になるため明示的に 0 を返す。
    /// </summary>
    public static double NextOffset(double verticalOffset, double delta, double scrollableHeight)
    {
        if (scrollableHeight <= 0)
            return 0;
        return Math.Clamp(verticalOffset + delta, 0, scrollableHeight);
    }

    /// <summary>
    /// PageUp/PageDown の移動量。1画面から数行ぶん差し引いて前画面の続きを残す。
    /// ビューポートが低い環境では固定オーバーラップではなく比率が効き、移動量が正のまま保たれる。
    /// </summary>
    public static double PageDelta(double viewportHeight)
    {
        if (viewportHeight <= 0)
            return 0;   // レイアウト前
        return Math.Max(viewportHeight * MinPageFraction, viewportHeight - PageOverlap);
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
