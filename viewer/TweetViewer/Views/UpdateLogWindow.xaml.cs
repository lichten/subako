using System.Collections.Specialized;
using System.Windows;
using TweetViewer.ViewModels;

namespace TweetViewer.Views;

public partial class UpdateLogWindow : Window
{
    public UpdateLogWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
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
