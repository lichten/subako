using System.Windows;
using TweetViewer.Data;

namespace TweetViewer.Views;

public partial class SearchEditDialog : Window
{
    /// <summary>任意の表示名 (空欄 = null でクエリを表示)。</summary>
    public string? SearchName { get; private set; }

    /// <summary>下限演算子を再合成した最終クエリ。</summary>
    public string Query { get; private set; } = "";

    public SearchEditDialog(string? currentName, string currentQuery)
    {
        InitializeComponent();
        var (baseQuery, minRetweets, minFaves) = SearchQueryOperators.Split(currentQuery);
        NameBox.Text = currentName ?? "";
        QueryBox.Text = baseQuery;
        MinRetweetsBox.Text = minRetweets?.ToString() ?? "";
        MinFavesBox.Text = minFaves?.ToString() ?? "";
        Loaded += (_, _) => NameBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var baseQuery = QueryBox.Text.Trim();
        if (baseQuery.Length == 0)
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
        Query = SearchQueryOperators.Compose(baseQuery, minRt, minFav);
        SearchName = NameBox.Text.Trim() is { Length: > 0 } name ? name : null;
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
