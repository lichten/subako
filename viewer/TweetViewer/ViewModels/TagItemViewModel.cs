using CommunityToolkit.Mvvm.ComponentModel;
using TweetViewer.Models;

namespace TweetViewer.ViewModels;

public sealed partial class TagItemViewModel : ObservableObject
{
    public long TagId { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private long _userCount;

    public TagItemViewModel(TagRow row)
    {
        TagId = row.TagId;
        _name = row.Name;
        _userCount = row.UserCount;
    }

    /// <summary>差分マージ用 (RefreshTagsAsync から)。</summary>
    public void Apply(TagRow row)
    {
        Name = row.Name;
        UserCount = row.UserCount;
    }
}
