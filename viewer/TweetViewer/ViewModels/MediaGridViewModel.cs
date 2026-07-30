using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TweetViewer.Data;
using TweetViewer.Models;
using TweetViewer.Services;

namespace TweetViewer.ViewModels;

public sealed class MediaItemViewModel
{
    public required string TweetId { get; init; }
    public required string Username { get; init; }
    public string? LocalPath { get; init; }
    public required string FullText { get; init; }
    public required string CreatedAtUtc { get; init; }

    public string CreatedAtLocal =>
        DateTimeOffset.TryParse(CreatedAtUtc, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var dto)
            ? dto.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : CreatedAtUtc;
}

/// <summary>1行 = 最大3セルの正方形サムネイル (行単位で仮想化するため)。</summary>
public sealed class MediaRowViewModel
{
    public required IReadOnlyList<MediaItemViewModel> Cells { get; init; }
}

public sealed partial class MediaGridViewModel : ObservableObject
{
    private const int Columns = 3;
    private const int PageSize = 120;   // 40行分

    private readonly ViewerDatabase _db;
    private readonly TweetRepository _repo;

    private IReadOnlyList<string> _usernames = [];
    private bool _ascending;
    private DateRangeFilter? _dateRange;
    private (long SortKey, long IdInt, int Idx)? _cursor;
    private bool _loading;
    private int _resetVersion;

    public ObservableCollection<MediaRowViewModel> Rows { get; } = new();

    /// <summary>ビューアの ←/→ 用フラットリスト (ロード済み分)。</summary>
    public List<MediaItemViewModel> FlatItems { get; } = new();

    [ObservableProperty]
    private bool _hasMore;

    /// <summary>期間フィルタで 0 件になったときの案内表示 (ロード完了後にのみ立てる)。</summary>
    [ObservableProperty]
    private bool _showEmptyNotice;

    public MediaGridViewModel(ViewerDatabase db, TweetRepository repo)
    {
        _db = db;
        _repo = repo;
    }

    /// <summary>表示対象を差し替える。usernames が複数なら統合表示 (時系列にマージ)。</summary>
    public async Task ResetAsync(
        IReadOnlyList<string> usernames, DateRangeFilter? dateRange, bool ascending = false)
    {
        var version = ++_resetVersion;
        _usernames = usernames;
        _ascending = ascending;
        _dateRange = dateRange;
        _cursor = null;
        ShowEmptyNotice = false;
        Rows.Clear();
        FlatItems.Clear();
        HasMore = usernames.Count > 0;
        if (usernames.Count > 0)
            await LoadMoreCoreAsync(version);
    }

    [RelayCommand]
    private Task LoadMore() => LoadMoreCoreAsync(_resetVersion);

    private async Task LoadMoreCoreAsync(int version)
    {
        if (_loading || !HasMore || _usernames.Count == 0)
            return;
        var usernames = _usernames;
        _loading = true;
        try
        {
            var page = await _repo.GetMediaPageAsync(
                usernames, _cursor, PageSize, _dateRange, _ascending);
            if (version != _resetVersion)
                return;

            // 画像ファイルはアーカイブごとの images/ に入っているため行の username で解決する
            var imagesDirs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var items = await Task.Run(() => page
                .Select(r =>
                {
                    if (!imagesDirs.TryGetValue(r.Username, out var imagesDir))
                        imagesDirs[r.Username] = imagesDir = _db.ImagesDir(r.Username);
                    return new MediaItemViewModel
                    {
                        TweetId = r.TweetId,
                        Username = r.Username,
                        LocalPath = LocalMediaFiles.Resolve(imagesDir, r.TweetId, r.Idx, r.Ext),
                        FullText = r.FullText,
                        CreatedAtUtc = r.CreatedAtUtc,
                    };
                })
                .ToList());
            if (version != _resetVersion)
                return;

            FlatItems.AddRange(items);
            // 末尾の不完全行があれば作り直して詰める
            if (Rows.Count > 0 && Rows[^1].Cells.Count < Columns)
                Rows.RemoveAt(Rows.Count - 1);
            var offset = Rows.Count * Columns;
            for (var i = offset; i < FlatItems.Count; i += Columns)
            {
                Rows.Add(new MediaRowViewModel
                {
                    Cells = FlatItems.Skip(i).Take(Columns).ToList(),
                });
            }

            if (page.Count > 0)
            {
                var last = page[^1];
                _cursor = (last.SortKey, last.IdInt, last.Idx);
            }
            HasMore = page.Count == PageSize;
            ShowEmptyNotice = _dateRange is not null && FlatItems.Count == 0;
        }
        finally
        {
            _loading = false;
        }
    }
}
