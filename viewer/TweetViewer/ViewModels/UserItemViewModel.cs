using CommunityToolkit.Mvvm.ComponentModel;
using TweetViewer.Models;

namespace TweetViewer.ViewModels;

public sealed partial class UserItemViewModel : ObservableObject
{
    public string Username { get; }

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private long _unreadCount;

    [ObservableProperty]
    private long _tweetCount;

    public UserItemViewModel(UserRow row)
    {
        Username = row.Username;
        _displayName = row.DisplayName ?? row.Username;
        _unreadCount = row.UnreadCount;
        _tweetCount = row.TweetCount;
    }

    public void ApplyCounts(UserRow row)
    {
        DisplayName = row.DisplayName ?? row.Username;
        UnreadCount = row.UnreadCount;
        TweetCount = row.TweetCount;
    }

    public bool HasUnread => UnreadCount > 0;

    partial void OnUnreadCountChanged(long value) => OnPropertyChanged(nameof(HasUnread));
}
