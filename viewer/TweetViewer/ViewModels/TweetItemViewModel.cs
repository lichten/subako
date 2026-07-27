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

    /// <summary>本文と RT元の画像 (本文欄に表示)。</summary>
    public IReadOnlyList<string> ImagePaths { get; }

    /// <summary>引用先ツイートの画像 (引用ブロック内に表示)。</summary>
    public IReadOnlyList<string> QuotedImagePaths { get; }

    /// <summary>本文中の動画リンクのサムネイル (本文欄に表示)。</summary>
    public IReadOnlyList<LinkThumbnailViewModel> LinkThumbnails { get; }

    /// <summary>引用先本文の動画リンクのサムネイル (引用ブロック内に表示)。</summary>
    public IReadOnlyList<LinkThumbnailViewModel> QuotedLinkThumbnails { get; }

    public bool HasLinkThumbnails => LinkThumbnails.Count > 0;
    public bool HasQuotedLinkThumbnails => QuotedLinkThumbnails.Count > 0;

    public TweetItemViewModel(
        TweetListViewModel owner, TweetRow row, IReadOnlyList<TweetMediaRow> media,
        string imagesDir, string ownerDisplayName, string? ownerIconUrl, IconCache iconCache,
        IconCache thumbnailCache)
    {
        _owner = owner;
        Row = row;
        _isRead = row.IsRead;
        OwnerDisplayName = ownerDisplayName;
        ImagePaths = ResolveImagePaths(media, imagesDir, m => m.Origin != MediaOrigin.Quoted);
        QuotedImagePaths = ResolveImagePaths(media, imagesDir, m => m.Origin == MediaOrigin.Quoted);

        var mainUrl = (IsRetweet ? Row.RtIconUrl : null) ?? Row.AuthorIconUrl ?? ownerIconUrl;
        ResolveIcon(iconCache, mainUrl, path => MainIconPath = path);
        if (IsQuote)
            ResolveIcon(iconCache, Row.QuotedIconUrl, path => QuotedIconPath = path);

        // 同じ動画を本文と引用先の両方で参照していたら本文側だけに出す (画像の先勝ちと同じ規則)
        var seenThumbnails = new HashSet<string>();
        LinkThumbnails = CreateThumbnails(DisplayText, thumbnailCache, seenThumbnails);
        QuotedLinkThumbnails = CreateThumbnails(QuotedText, thumbnailCache, seenThumbnails);
    }

    private static List<LinkThumbnailViewModel> CreateThumbnails(
        string text, IconCache cache, HashSet<string> seen)
    {
        var result = new List<LinkThumbnailViewModel>();
        foreach (var link in Linkifier.ExtractVideoLinks(text))
        {
            if (!seen.Add(link.ThumbnailUrl))
                continue;
            var vm = new LinkThumbnailViewModel(link.PageUrl);
            // ニコニコはサムネイル URL を API で引く必要がある (キャッシュミス時のみ)
            var resolver = link.NicoVideoNumber is { } number
                ? new Func<string, Task<string?>>(
                    fallback => NicoThumbnail.ResolveAsync(number, fallback))
                : null;
            ResolveIcon(cache, link.ThumbnailUrl, path => vm.ThumbnailPath = path, resolver);
            result.Add(vm);
        }
        return result;
    }

    private static async void ResolveIcon(
        IconCache cache, string? url, Action<string?> setter,
        Func<string, Task<string?>>? resolveDownloadUrl = null)
    {
        try
        {
            var path = await cache.GetLocalPathAsync(url, resolveDownloadUrl);
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

    /// <summary>行の実投稿者名。検索バケットでは行ごとに異なる (author 列がない旧データは owner)。</summary>
    private string AuthorName => Row.AuthorDisplayName ?? Row.AuthorUsername ?? OwnerDisplayName;

    /// <summary>ヘッダ太字の表示名。RT は RT元作者、それ以外は実投稿者。</summary>
    public string HeaderDisplayName => IsRetweet
        ? (string.IsNullOrEmpty(Row.RtDisplayName) ? Row.RtUsername ?? AuthorName : Row.RtDisplayName!)
        : AuthorName;

    /// <summary>ヘッダの @ユーザー名。RT は RT元作者、それ以外は実投稿者。</summary>
    public string HeaderUsername => IsRetweet && !string.IsNullOrEmpty(Row.RtUsername)
        ? Row.RtUsername!
        : Row.AuthorUsername ?? Row.Username;

    /// <summary>右クリック「ユーザーに追加」の対象作者。RT は RT元作者。特定不能 (バケット ID 等) なら null。</summary>
    public string? AddableAuthorUsername
    {
        get
        {
            var name = IsRetweet && !string.IsNullOrEmpty(Row.RtUsername)
                ? Row.RtUsername
                : Row.AuthorUsername ?? Row.Username;
            return string.IsNullOrEmpty(name) || name.Contains('/') ? null : name;
        }
    }

    public bool CanAddAuthor => AddableAuthorUsername is not null;

    /// <summary>MenuItem Header 用。_ はアクセスキー記号のため __ にエスケープ。</summary>
    public string AddAuthorMenuHeader => AddableAuthorUsername is { } u
        ? $"@{u.Replace("_", "__")} をユーザーに追加"
        : "作者をユーザーに追加";

    public string RtHeader => IsRetweet ? $"{AuthorName} さんがリツイート" : "";
    public string RtText => Row.RtText ?? "";
    public string ReplyHeader => IsReply ? $"@{Row.InReplyToUsername} への返信" : "";
    public string QuotedHeader => IsQuote
        ? $"{Row.QuotedDisplayName} @{Row.QuotedUsername}"
        : "";
    public string QuotedText => Row.QuotedText ?? "";
    // 画像だけで本文が空の引用でもブロックを出す
    public bool HasQuote => IsQuote && (!string.IsNullOrEmpty(Row.QuotedText) || HasQuotedImages);
    public bool HasQuotedImages => QuotedImagePaths.Count > 0;

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

    /// <summary>
    /// カウント行。取得できない値は項目ごと出さない (0 と偽らない)。
    /// リツイートの返信数は API が外側にも RT元にも返さないため非表示。
    /// </summary>
    public string CountsText
    {
        get
        {
            var parts = new List<string>();
            if (!IsRetweet)
                parts.Add($"返信 {Row.ReplyCount:N0}");
            parts.Add($"RT {Row.RetweetCount:N0}");
            parts.Add($"いいね {Row.LikeCount:N0}");
            if (Row.ViewCount > 0)
                parts.Add($"表示 {Row.ViewCount:N0}");
            return string.Join("  ", parts);
        }
    }

    [RelayCommand]
    private Task ToggleRead() => _owner.ToggleReadAsync(this);

    [RelayCommand]
    private void OpenInBrowser()
    {
        // RT/引用でも X 側が id で正規ツイートへリダイレクトする。
        // 検索バケット (Username = searches/<slug>) では投稿者不明時に id 直リンク形式を使う
        var user = Row.AuthorUsername ?? Row.Username;
        Browser.OpenUrl(user.Contains('/')
            ? $"https://x.com/i/web/status/{Row.TweetId}"
            : $"https://x.com/{user}/status/{Row.TweetId}");
    }

    /// <summary>既読化(スクロール検知から)。二重呼び出しは owner 側で無視。</summary>
    public void MarkReadFromScroll() => _owner.MarkSeen(this);

    private static List<string> ResolveImagePaths(
        IReadOnlyList<TweetMediaRow> media, string imagesDir, Func<TweetMediaRow, bool> filter)
    {
        return media
            .Where(filter)
            .Select(m => LocalMediaFiles.Resolve(imagesDir, m.TweetId, m.Index, m.Ext))
            .OfType<string>()
            .ToList();
    }
}
