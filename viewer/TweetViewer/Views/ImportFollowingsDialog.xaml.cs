using System.Collections.ObjectModel;
using System.Windows;
using TweetViewer.ViewModels;

namespace TweetViewer.Views;

/// <summary>
/// ダイアログ内だけのタグのチェック状態。MainViewModel.Tags の TagItemViewModel は
/// サイドバーのチップとフィルタ ComboBox と参照を共有しているので、そこに
/// IsChecked を足さずにこの器で包む。UI → プロパティの一方向なので INPC は不要。
/// </summary>
public sealed class TagCheckItem(long tagId, string name)
{
    public long TagId { get; } = tagId;
    public string Name { get; } = name;
    public bool IsChecked { get; set; }
}

public partial class ImportFollowingsDialog : Window
{
    private static readonly char[] TagSeparators = [',', '，', '、', '\n', '\r'];

    private readonly ObservableCollection<TagCheckItem> _items = [];

    public string SourceUsername { get; private set; } = "";
    public IReadOnlyList<long> SelectedTagIds { get; private set; } = [];
    public IReadOnlyList<string> NewTagNames { get; private set; } = [];
    public int MaxRequests { get; private set; }

    /// <summary>
    /// tags には MainViewModel.Tags (実タグのみ) を渡すこと。
    /// TagFilterOptions は疑似タグ「(タグなし)」を含むので渡してはいけない。
    /// </summary>
    public ImportFollowingsDialog(IEnumerable<TagItemViewModel> tags)
    {
        InitializeComponent();
        foreach (var tag in tags)
            _items.Add(new TagCheckItem(tag.TagId, tag.Name));
        TagListBox.ItemsSource = _items;
        Loaded += (_, _) => SourceBox.Focus();
    }

    /// <summary>
    /// カンマ (半角/全角/読点) と改行で分割 → trim → 空除去 → 大文字小文字無視で
    /// 重複除去。tags.name が UNIQUE COLLATE NOCASE のため。
    /// </summary>
    public static IReadOnlyList<string> ParseTagNames(string text)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var part in text.Split(TagSeparators, StringSplitOptions.TrimEntries |
                                                       StringSplitOptions.RemoveEmptyEntries))
        {
            if (seen.Add(part))
                result.Add(part);
        }
        return result;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var source = SourceBox.Text.Trim().TrimStart('@');
        if (source.Length == 0)
        {
            MessageBox.Show("フォロー元アカウントを入力してください。",
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var selected = _items.Where(i => i.IsChecked).Select(i => i.TagId).ToList();
        var newNames = ParseTagNames(NewTagsBox.Text);
        if (selected.Count == 0 && newNames.Count == 0)
        {
            // タグ無しで数千件登録すると、後からその集合を選び直す手段が無くなる
            MessageBox.Show("タグを 1 つ以上選ぶか、新しいタグ名を入力してください。",
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(MaxRequestsBox.Text.Trim(), out var value) || value <= 0)
        {
            MessageBox.Show("最大リクエスト数は正の整数で指定してください。",
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        SourceUsername = source;
        SelectedTagIds = selected;
        NewTagNames = newNames;
        MaxRequests = value;
        DialogResult = true;
    }
}
