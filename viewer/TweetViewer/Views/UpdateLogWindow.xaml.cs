using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using TweetViewer.ViewModels;

namespace TweetViewer.Views;

public partial class UpdateLogWindow : Window
{
    /// <summary>
    /// アプリ終了時など、実行中でも本当に閉じたいときに立てる。
    /// これが無いと OnClosing のキャンセルがオーナー (MainWindow) の Close も巻き込んで
    /// 取得中にアプリを終了できなくなる。
    /// </summary>
    public bool ForceClose { get; set; }

    public UpdateLogWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // 実行中の ✕ は「隠す」に読み替える: 中断ボタン (唯一のキャンセル UI) への
        // 導線とウィンドウ位置を保つ。再表示はステータスバーのリンクから
        if (!ForceClose && DataContext is FetchDialogViewModel { IsRunning: true })
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is FetchDialogViewModel vm)
        {
            ((INotifyCollectionChanged)vm.LogLines).CollectionChanged += (_, args) =>
            {
                if (args.Action != NotifyCollectionChangedAction.Add)
                    return;
                // CollectionChanged 内で同期的に ScrollIntoView するとアイテム
                // ジェネレーターが不整合を起こす (高頻度ログでクラッシュ) ため遅延実行
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
                {
                    if (LogList.Items.Count > 0)
                        LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
                });
            };
        }
    }
}
