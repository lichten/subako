using System.Globalization;
using System.Windows;

namespace TweetViewer.Views;

public partial class SearchBackfillDialog : Window
{
    /// <summary>遡る開始日 (YYYY-MM-DD)。</summary>
    public string Since { get; private set; } = "";
    public int MaxRequests { get; private set; }

    public SearchBackfillDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => { MaxRequestsBox.Focus(); MaxRequestsBox.SelectAll(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var since = SinceBox.Text.Trim();
        if (!DateOnly.TryParseExact(since, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _))
        {
            MessageBox.Show("遡る開始日は YYYY-MM-DD 形式で指定してください。",
                "TweetViewer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(MaxRequestsBox.Text.Trim(), out var value) || value <= 0)
        {
            MessageBox.Show("最大リクエスト数は正の整数で指定してください。",
                "TweetViewer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Since = since;
        MaxRequests = value;
        DialogResult = true;
    }
}
