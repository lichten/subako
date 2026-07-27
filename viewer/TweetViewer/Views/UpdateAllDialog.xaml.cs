using System.Windows;

namespace TweetViewer.Views;

public partial class UpdateAllDialog : Window
{
    /// <summary>全対象の合計に対するリクエスト上限。</summary>
    public int MaxRequests { get; private set; }

    public UpdateAllDialog(int userCount, int searchCount)
    {
        InitializeComponent();
        TargetText.Text =
            $"サイドバーに表示中のユーザー {userCount} 件と検索 {searchCount} 件を順に更新します。";
        Loaded += (_, _) => { MaxRequestsBox.Focus(); MaxRequestsBox.SelectAll(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(MaxRequestsBox.Text.Trim(), out var value) || value <= 0)
        {
            MessageBox.Show("最大リクエスト数は正の整数で指定してください。",
                "TweetViewer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        MaxRequests = value;
        DialogResult = true;
    }
}
