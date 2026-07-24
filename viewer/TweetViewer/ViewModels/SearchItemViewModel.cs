using CommunityToolkit.Mvvm.ComponentModel;
using TweetViewer.Models;

namespace TweetViewer.ViewModels;

/// <summary>サイドバー「検索」セクションの1行 (保存済み検索バケット)。</summary>
public sealed partial class SearchItemViewModel : ObservableObject
{
    /// <summary>バケット ID (= users.username の "searches/&lt;slug&gt;")。</summary>
    public string Username { get; }

    /// <summary>検索クエリ原文 (search.json 由来。読めない場合はフォルダ名)。</summary>
    [ObservableProperty]
    private string _query;

    [ObservableProperty]
    private long _tweetCount;

    [ObservableProperty]
    private long _unreadCount;

    public SearchItemViewModel(UserRow row, string query)
    {
        Username = row.Username;
        _query = query;
        _tweetCount = row.TweetCount;
        _unreadCount = row.UnreadCount;
    }

    public void ApplyCounts(UserRow row, string query)
    {
        Query = query;
        TweetCount = row.TweetCount;
        UnreadCount = row.UnreadCount;
    }

    public bool HasUnread => UnreadCount > 0;

    partial void OnUnreadCountChanged(long value) => OnPropertyChanged(nameof(HasUnread));
}
