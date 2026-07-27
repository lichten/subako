using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TweetViewer.Data;
using TweetViewer.Models;
using TweetViewer.Services;

namespace TweetViewer.ViewModels;

/// <summary>タイムラインに混ぜる 1 アーカイブ分の表示情報 (author 列が無い旧データ行のフォールバック用)。</summary>
public sealed record ArchiveInfo(string Username, string DisplayName, string? IconUrl);

public sealed partial class TweetListViewModel : ObservableObject
{
    private const int PageSize = 200;

    private readonly ViewerDatabase _db;
    private readonly TweetRepository _repo;
    private readonly ReadMarkQueue _readQueue;
    private readonly IconCache _iconCache;
    /// <summary>動画リンクのサムネイル用 (data/thumbnails/。アイコンとは別ディレクトリ)。</summary>
    private readonly IconCache _thumbnailCache;

    private IReadOnlyList<string> _usernames = [];
    /// <summary>行の Username → 表示情報。統合表示では行ごとに引く (ページ共通のスカラーだと誤表示になる)。</summary>
    private readonly Dictionary<string, (string ImagesDir, string DisplayName, string? IconUrl)> _archiveInfo =
        new(StringComparer.OrdinalIgnoreCase);
    /// <summary>複数アーカイブに存在する tweet_id → 含まれる全アーカイブ (未読数のファンアウト用)。</summary>
    private readonly Dictionary<string, IReadOnlyList<string>> _duplicateArchives =
        new(StringComparer.Ordinal);
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

    /// <summary>表示対象を差し替える。archives が複数なら統合タイムライン (時系列にマージ)。</summary>
    public async Task ResetAsync(IReadOnlyList<ArchiveInfo> archives, bool unreadOnly)
    {
        var version = ++_resetVersion;
        await _readQueue.FlushAsync();
        _usernames = archives.Select(a => a.Username).ToList();
        _archiveInfo.Clear();
        foreach (var archive in archives)
            _archiveInfo[archive.Username] =
                (_db.ImagesDir(archive.Username), archive.DisplayName, archive.IconUrl);
        _duplicateArchives.Clear();
        _unreadOnly = unreadOnly;
        _cursor = null;
        Items.Clear();
        HasMore = archives.Count > 0;
        if (archives.Count > 0)
            await LoadMoreCoreAsync(version, force: true);
    }

    [RelayCommand]
    private Task LoadMore() => LoadMoreCoreAsync(_resetVersion);

    private async Task LoadMoreCoreAsync(int version, bool force = false)
    {
        // force = リセット直後の初回ロード。旧リセットのロードが実行中でもスキップしない
        // (旧ロードの結果は version ガードで破棄されるため、ここで譲ると空のままになる)
        if ((_loading && !force) || !HasMore || _usernames.Count == 0)
            return;
        var usernames = _usernames;
        _loading = true;
        try
        {
            var page = await _repo.GetPageAsync(usernames, _unreadOnly, _cursor, PageSize);
            if (version != _resetVersion)
                return;   // リセットで破棄

            var archiveInfo = _archiveInfo;
            var vms = await Task.Run(() => page.Rows
                .Select(row =>
                {
                    // 行のアーカイブに応じた表示情報 (統合表示ではページ内に複数アーカイブが混ざる)
                    var (imagesDir, displayName, iconUrl) =
                        archiveInfo.TryGetValue(row.Username, out var info)
                            ? info
                            : (_db.ImagesDir(row.Username), row.Username, null);
                    return new TweetItemViewModel(
                        this, row,
                        page.Media.TryGetValue(row.TweetId, out var m) ? m : Array.Empty<TweetMediaRow>(),
                        imagesDir, displayName, iconUrl, _iconCache, _thumbnailCache);
                })
                .ToList());
            if (version != _resetVersion)
                return;

            foreach (var pair in page.ArchivesByTweetId)
                _duplicateArchives[pair.Key] = pair.Value;
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
            // 自分より新しいリセットが始まっていたら、_loading は後発 (force:true で
            // 入ってきた側) の所有物なので触らない。横取り解除すると、リセット中に
            // 余計な追加ロードが割り込んで表示位置が乱れる
            if (version == _resetVersion)
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
        NotifyUnreadDelta(item, -1);
    }

    /// <summary>手動トグル(即時書込)。</summary>
    public async Task ToggleReadAsync(TweetItemViewModel item)
    {
        var newValue = !item.IsRead;
        item.IsRead = newValue;
        await _repo.SetReadAsync(item.TweetId, item.Username, newValue);
        NotifyUnreadDelta(item, newValue ? -1 : +1);
    }

    /// <summary>
    /// 未読数の楽観更新。read_state は tweet_id 単位で全アーカイブ共通のため、
    /// 同じツイートを含む全アーカイブ (表示中でないものも含む) の未読数が実際に増減する。
    /// DB 書込は 1 回でよいが、サイドバーへの通知は該当アーカイブすべてに送る。
    /// </summary>
    private void NotifyUnreadDelta(TweetItemViewModel item, long delta)
    {
        if (_duplicateArchives.TryGetValue(item.TweetId, out var archives))
        {
            foreach (var username in archives)
                UnreadDelta?.Invoke(username, delta);
        }
        else
        {
            UnreadDelta?.Invoke(item.Username, delta);
        }
    }
}
