using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TweetViewer.Data;
using TweetViewer.Models;
using TweetViewer.Services;

namespace TweetViewer.ViewModels;

public sealed partial class TweetListViewModel : ObservableObject
{
    private const int PageSize = 200;

    private readonly ViewerDatabase _db;
    private readonly TweetRepository _repo;
    private readonly ReadMarkQueue _readQueue;
    private readonly IconCache _iconCache;
    /// <summary>動画リンクのサムネイル用 (data/thumbnails/。アイコンとは別ディレクトリ)。</summary>
    private readonly IconCache _thumbnailCache;

    private string? _username;
    private string _displayName = "";
    private string? _ownerIconUrl;
    private bool _unreadOnly;
    private (long SortKey, long IdInt)? _cursor;
    private bool _loading;
    private int _resetVersion;

    public ObservableCollection<TweetItemViewModel> Items { get; } = new();

    [ObservableProperty]
    private bool _hasMore;

    /// <summary>スクロール既読で未読数が1減るたびに発火(引数 = username)。</summary>
    public event Action<string, long>? UnreadDelta;

    public TweetListViewModel(
        ViewerDatabase db, TweetRepository repo, ReadMarkQueue readQueue, IconCache iconCache)
    {
        _db = db;
        _repo = repo;
        _readQueue = readQueue;
        _iconCache = iconCache;
        _thumbnailCache = new IconCache(db.DataDir, "thumbnails");
    }

    public async Task ResetAsync(string? username, string displayName, string? ownerIconUrl, bool unreadOnly)
    {
        var version = ++_resetVersion;
        await _readQueue.FlushAsync();
        _username = username;
        _displayName = displayName;
        _ownerIconUrl = ownerIconUrl;
        _unreadOnly = unreadOnly;
        _cursor = null;
        Items.Clear();
        HasMore = username is not null;
        if (username is not null)
            await LoadMoreCoreAsync(version, force: true);
    }

    [RelayCommand]
    private Task LoadMore() => LoadMoreCoreAsync(_resetVersion);

    private async Task LoadMoreCoreAsync(int version, bool force = false)
    {
        // force = リセット直後の初回ロード。旧リセットのロードが実行中でもスキップしない
        // (旧ロードの結果は version ガードで破棄されるため、ここで譲ると空のままになる)
        if ((_loading && !force) || !HasMore || _username is not { } username)
            return;
        _loading = true;
        try
        {
            var page = await _repo.GetPageAsync(username, _unreadOnly, _cursor, PageSize);
            if (version != _resetVersion)
                return;   // リセットで破棄

            var imagesDir = _db.ImagesDir(username);
            var displayName = _displayName;
            var ownerIconUrl = _ownerIconUrl;
            var vms = await Task.Run(() => page.Rows
                .Select(row => new TweetItemViewModel(
                    this, row,
                    page.Media.TryGetValue(row.TweetId, out var m) ? m : Array.Empty<TweetMediaRow>(),
                    imagesDir, displayName, ownerIconUrl, _iconCache, _thumbnailCache))
                .ToList());
            if (version != _resetVersion)
                return;

            foreach (var vm in vms)
                Items.Add(vm);
            if (page.Rows.Count > 0)
            {
                var last = page.Rows[^1];
                _cursor = (last.SortKey, last.IdInt);
            }
            HasMore = page.Rows.Count == PageSize;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>スクロール既読検知からの呼び出し。楽観更新+バッチ書込キューへ。</summary>
    public void MarkSeen(TweetItemViewModel item)
    {
        if (item.IsRead)
            return;
        item.IsRead = true;
        _readQueue.Enqueue(item.TweetId, item.Username);
        UnreadDelta?.Invoke(item.Username, -1);
    }

    /// <summary>手動トグル(即時書込)。</summary>
    public async Task ToggleReadAsync(TweetItemViewModel item)
    {
        var newValue = !item.IsRead;
        item.IsRead = newValue;
        await _repo.SetReadAsync(item.TweetId, item.Username, newValue);
        UnreadDelta?.Invoke(item.Username, newValue ? -1 : +1);
    }
}
