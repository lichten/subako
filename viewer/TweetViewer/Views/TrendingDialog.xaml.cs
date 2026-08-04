using System.Globalization;
using System.Windows;
using TweetViewer.Models;

namespace TweetViewer.Views;

/// <summary>
/// 「今日の話題 (日本)」の取得条件を決めるダイアログ (docs/trending-jp.md)。
/// 何が投げられるか分かるよう、生成クエリをそのまま見せる。
/// </summary>
public partial class TrendingDialog : Window
{
    /// <summary>ダイアログを開いた時刻。プレビューと実際の取得で日付をずらさないため固定する。</summary>
    private readonly DateTimeOffset _now = DateTimeOffset.Now;

    public TrendingTargetDay Day { get; private set; } = TrendingTargetDay.Yesterday;
    public long MinFaves { get; private set; }
    public int MaxRequests { get; private set; }

    /// <summary>取得に使うクエリ。プレビューと同一の文字列を返す。</summary>
    public string Query { get; private set; } = "";

    public TrendingDialog()
    {
        InitializeComponent();
        MinFavesBox.Text = TrendingQuery.SuggestedMinFaves(TrendingTargetDay.Yesterday)
            .ToString(CultureInfo.InvariantCulture);
        UpdatePreview();
        Loaded += (_, _) => MinFavesBox.Focus();
    }

    private TrendingTargetDay SelectedDay =>
        TodayRadio.IsChecked == true ? TrendingTargetDay.Today : TrendingTargetDay.Yesterday;

    /// <summary>対象日を変えたら推奨閾値も入れ替える (当日は 1/5。§3.1 の実測)。</summary>
    private void TargetDay_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized)
            return;
        MinFavesBox.Text = TrendingQuery.SuggestedMinFaves(SelectedDay)
            .ToString(CultureInfo.InvariantCulture);
        UpdatePreview();
    }

    private void Input_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (IsInitialized)
            UpdatePreview();
    }

    private void UpdatePreview()
    {
        TargetDateText.Text =
            $"({TrendingQuery.JstDateLabel(TrendingQuery.DateFor(SelectedDay, _now))})";
        PreviewBox.Text = long.TryParse(MinFavesBox.Text.Trim(), out var faves) && faves > 0
            ? TrendingQuery.Build(SelectedDay, faves, _now)
            : "(いいね数の下限を入力してください)";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!long.TryParse(MinFavesBox.Text.Trim(), out var minFaves) || minFaves <= 0)
        {
            MessageBox.Show("いいね数の下限は正の整数で指定してください。",
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(MaxRequestsBox.Text.Trim(), out var maxRequests) || maxRequests <= 0)
        {
            MessageBox.Show("最大リクエスト数は正の整数で指定してください。",
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Day = SelectedDay;
        MinFaves = minFaves;
        MaxRequests = maxRequests;
        Query = TrendingQuery.Build(Day, MinFaves, _now);
        DialogResult = true;
    }
}
