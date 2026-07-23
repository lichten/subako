using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TweetViewer.Models;
using TweetViewer.Services;

namespace TweetViewer.ViewModels;

public sealed partial class TweetItemViewModel : ObservableObject
{
    private readonly TweetListViewModel _owner;

    public TweetRow Row { get; }

    [ObservableProperty]
    private bool _isRead;

    /// <summary>メインアイコン (RT は RT元作者、それ以外はアーカイブユーザー)。</summary>
    [ObservableProperty]
    private string? _mainIconPath;

    /// <summary>引用ブロックの引用先ユーザーアイコン。</summary>
    [ObservableProperty]
    private string? _quotedIconPath;

    public IReadOnlyList<string> ImagePaths { get; }

    public TweetItemViewModel(
        TweetListViewModel owner, TweetRow row, IReadOnlyList<TweetMediaRow> media,
        string imagesDir, string ownerDisplayName, string? ownerIconUrl, IconCache iconCache)
    {
        _owner = owner;
        Row = row;
        _isRead = row.IsRead;
        OwnerDisplayName = ownerDisplayName;
        ImagePaths = ResolveImagePaths(media, imagesDir);

        var mainUrl = (IsRetweet ? Row.RtIconUrl : null) ?? ownerIconUrl;
        ResolveIcon(iconCache, mainUrl, path => MainIconPath = path);
        if (IsQuote)
            ResolveIcon(iconCache, Row.QuotedIconUrl, path => QuotedIconPath = path);
    }

    private static async void ResolveIcon(IconCache cache, string? url, Action<string?> setter)
    {
        try
        {
            var path = await cache.GetLocalPathAsync(url);
            if (path is not null)
                setter(path);
        }
        catch (Exception)
        {
            // アイコンなし (プレースホルダのまま)
        }
    }

    public string OwnerDisplayName { get; }

    public string TweetId => Row.TweetId;
    public string Username => Row.Username;
    public string FullText => Row.FullText;
    public bool IsRetweet => Row.Type == TweetType.Retweet;
    public bool IsReply => Row.Type == TweetType.Reply;
    public bool IsQuote => Row.Type == TweetType.Quote;
    public bool HasImages => ImagePaths.Count > 0;

    public string RtHeader => IsRetweet
        ? $"RT @{Row.RtUsername}" + (string.IsNullOrEmpty(Row.RtDisplayName) ? "" : $" ({Row.RtDisplayName})")
        : "";
    public string RtText => Row.RtText ?? "";
    public string ReplyHeader => IsReply ? $"@{Row.InReplyToUsername} への返信" : "";
    public string QuotedHeader => IsQuote
        ? $"{Row.QuotedDisplayName} @{Row.QuotedUsername}"
        : "";
    public string QuotedText => Row.QuotedText ?? "";
    public bool HasQuote => IsQuote && !string.IsNullOrEmpty(Row.QuotedText);

    /// <summary>本文表示。RT は truncate された "RT @x: …" ではなく元ツイート全文を出す。</summary>
    public string DisplayText =>
        IsRetweet && !string.IsNullOrEmpty(Row.RtText) ? Row.RtText! : Row.FullText;

    public string CreatedAtLocal
    {
        get
        {
            if (DateTimeOffset.TryParse(Row.CreatedAtUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var dto))
                return dto.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            return Row.CreatedAtUtc;
        }
    }

    public string CountsText =>
        $"返信 {Row.ReplyCount:N0}  RT {Row.RetweetCount:N0}  いいね {Row.LikeCount:N0}" +
        (Row.ViewCount > 0 ? $"  表示 {Row.ViewCount:N0}" : "");

    [RelayCommand]
    private Task ToggleRead() => _owner.ToggleReadAsync(this);

    [RelayCommand]
    private void OpenInBrowser() =>
        // RT/引用でも X 側が id で正規ツイートへリダイレクトする
        Browser.OpenUrl($"https://x.com/{Row.Username}/status/{Row.TweetId}");

    /// <summary>既読化(スクロール検知から)。二重呼び出しは owner 側で無視。</summary>
    public void MarkReadFromScroll() => _owner.MarkSeen(this);

    private static List<string> ResolveImagePaths(IReadOnlyList<TweetMediaRow> media, string imagesDir)
    {
        return media
            .Select(m => LocalMediaFiles.Resolve(imagesDir, m.TweetId, m.Index, m.Ext))
            .OfType<string>()
            .ToList();
    }
}
