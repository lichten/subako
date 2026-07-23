using System.Windows;

namespace TweetViewer.Views;

public partial class BackfillDialog : Window
{
    public int MaxRequests { get; private set; }

    public BackfillDialog()
    {
        InitializeComponent();
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
