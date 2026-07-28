using CommunityToolkit.Mvvm.ComponentModel;
using TweetViewer.Models;

namespace TweetViewer.ViewModels;

public sealed partial class TagItemViewModel : ObservableObject
{
    /// <summary>「(タグなし)」疑似タグの ID。実タグの tag_id は 1 以上なので衝突しない。</summary>
    public const long UntaggedTagId = -1;

    public long TagId { get; }

    /// <summary>フィルタ ComboBox 専用の疑似タグか (DB には存在しない)。</summary>
    public bool IsUntagged => TagId == UntaggedTagId;

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

    /// <summary>
    /// タグフィルタ用の疑似項目「(タグなし)」を作る。TagFilterOptions 専用で、
    /// 付け外し・削除の対象になる Tags には決して入れないこと。
    /// </summary>
    public static TagItemViewModel CreateUntagged() =>
        new(new TagRow { TagId = UntaggedTagId, Name = "(タグなし)" });

    /// <summary>差分マージ用 (RefreshTagsAsync から)。</summary>
    public void Apply(TagRow row)
    {
        Name = row.Name;
        UserCount = row.UserCount;
    }
}
