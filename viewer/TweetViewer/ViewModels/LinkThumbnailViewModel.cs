using CommunityToolkit.Mvvm.ComponentModel;

namespace TweetViewer.ViewModels;

/// <summary>本文中の動画リンク 1 件のサムネイル表示。</summary>
public sealed partial class LinkThumbnailViewModel : ObservableObject
{
    /// <summary>クリックでブラウザに開く動画ページの URL。</summary>
    public string PageUrl { get; }

    public LinkThumbnailViewModel(string pageUrl) => PageUrl = pageUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    private string? _thumbnailPath;

    /// <summary>取得できるまで / 取得に失敗したときは枠自体を出さない。</summary>
    public bool HasImage => ThumbnailPath is not null;
}
