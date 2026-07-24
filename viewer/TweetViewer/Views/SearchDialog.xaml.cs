using System.Windows;

namespace TweetViewer.Views;

public partial class SearchDialog : Window
{
    public string Query { get; private set; } = "";
    public long? MinRetweets { get; private set; }
    public long? MinFaves { get; private set; }
    public int MaxRequests { get; private set; }

    public SearchDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => QueryBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var query = QueryBox.Text.Trim();
        if (query.Length == 0)
        {
            MessageBox.Show("検索クエリを入力してください。",
                "TweetViewer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!TryParseMin(MinRetweetsBox.Text, out var minRt) ||
            !TryParseMin(MinFavesBox.Text, out var minFav))
        {
            MessageBox.Show("RT数・いいね数の下限は 0 以上の整数か空欄で指定してください。",
                "TweetViewer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(MaxRequestsBox.Text.Trim(), out var maxRequests) || maxRequests <= 0)
        {
            MessageBox.Show("最大リクエスト数は正の整数で指定してください。",
                "TweetViewer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Query = query;
        MinRetweets = minRt;
        MinFaves = minFav;
        MaxRequests = maxRequests;
        DialogResult = true;
    }

    private static bool TryParseMin(string text, out long? value)
    {
        value = null;
        text = text.Trim();
        if (text.Length == 0)
            return true;
        if (long.TryParse(text, out var n) && n >= 0)
        {
            value = n;
            return true;
        }
        return false;
    }
}
