using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TweetViewer.Data;
using TweetViewer.Models;

namespace TweetViewer.ViewModels;

/// <summary>サイドバー「検索」セクションの1行 (保存済み検索バケット)。</summary>
public sealed partial class SearchItemViewModel : ObservableObject
{
    /// <summary>バケット ID (= users.username の "searches/&lt;slug&gt;")。</summary>
    public string Username { get; }

    /// <summary>この検索に付与されたタグ (チップ表示・フィルタ判定用)。</summary>
    public ObservableCollection<TagItemViewModel> Tags { get; } = new();

    /// <summary>検索クエリ原文 (search.json 由来。読めない場合はフォルダ名)。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private string _query;

    /// <summary>任意の表示名 (search.json の name。null = 未設定でクエリを表示)。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private string? _name;

    /// <summary>
    /// search.json の order ("popular" 等。null = 未設定で fetcher 既定の latest)。
    /// 表示にも取得引数にも使わないが、編集経路で落とさないよう保持する
    /// (docs/trending-jp.md §10.3-1)。
    /// </summary>
    [ObservableProperty]
    private string? _order;

    [ObservableProperty]
    private long _tweetCount;

    [ObservableProperty]
    private long _unreadCount;

    /// <summary>サイドバー・タイムラインヘッダの表示ラベル。</summary>
    public string Label => Name ?? Query;

    /// <summary>
    /// 期間を絞ったクエリのバケットか。バックフィルは 30 日窓を後付けするため
    /// `(q since:X until:Y) since:A until:B` になって必ず 0 件になり、しかも
    /// 「完了」と記録されて再開状態が壊れる。fetcher も exit 1 で拒否するので、
    /// ビューアは同じ判定でメニューを無効化する (docs/trending-jp.md §10.3-2)。
    /// </summary>
    public bool IsPeriodScopedSearch => SearchQueryOperators.HasPeriodOperator(Query);

    partial void OnQueryChanged(string value) => OnPropertyChanged(nameof(IsPeriodScopedSearch));

    public SearchItemViewModel(UserRow row, string query, string? name, string? order = null)
    {
        Username = row.Username;
        _query = query;
        _name = name;
        _order = order;
        _tweetCount = row.TweetCount;
        _unreadCount = row.UnreadCount;
    }

    public void ApplyCounts(UserRow row, string query, string? name, string? order = null)
    {
        Query = query;
        Name = name;
        Order = order;
        TweetCount = row.TweetCount;
        UnreadCount = row.UnreadCount;
    }

    public bool HasUnread => UnreadCount > 0;

    partial void OnUnreadCountChanged(long value) => OnPropertyChanged(nameof(HasUnread));

    public bool HasTag(long tagId) => Tags.Any(t => t.TagId == tagId);

    /// <summary>タグ割当を差し替える (RefreshTagsAsync から)。</summary>
    public void ApplyTags(IEnumerable<TagItemViewModel> tags)
    {
        Tags.Clear();
        foreach (var tag in tags)
            Tags.Add(tag);
    }
}
