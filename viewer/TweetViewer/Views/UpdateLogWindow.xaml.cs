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
                // 追記時に自動スクロール
                if (args.Action == NotifyCollectionChangedAction.Add && LogList.Items.Count > 0)
                    LogList.ScrollIntoView(LogList.Items[^1]);
            };
        }
    }
}
