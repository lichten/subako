using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
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

    [ObservableProperty]
    private long _tweetCount;

    [ObservableProperty]
    private long _unreadCount;

    /// <summary>サイドバー・タイムラインヘッダの表示ラベル。</summary>
    public string Label => Name ?? Query;

    public SearchItemViewModel(UserRow row, string query, string? name)
    {
        Username = row.Username;
        _query = query;
        _name = name;
        _tweetCount = row.TweetCount;
        _unreadCount = row.UnreadCount;
    }

    public void ApplyCounts(UserRow row, string query, string? name)
    {
        Query = query;
        Name = name;
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
