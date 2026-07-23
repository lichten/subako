using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TweetViewer.Data;
using TweetViewer.Services;

namespace TweetViewer.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ViewerDatabase _db;
    private readonly UserRepository _users;
    private readonly JsonlImporter _importer;

    public TweetListViewModel TweetList { get; }
    public MediaGridViewModel MediaGrid { get; }
    public FetchProcessService FetchService { get; }
    public JsonlImporter Importer => _importer;

    /// <summary>true = メディア欄、false = タイムライン。</summary>
    [ObservableProperty]
    private bool _isMediaView;

    public ObservableCollection<UserItemViewModel> Users { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RebuildCommand))]
    private UserItemViewModel? _selectedUser;

    [ObservableProperty]
    private bool _unreadOnly;

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>取得サブプロセスの多重起動防止(全「更新」ボタンを disable)。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RebuildCommand))]
    private bool _isFetching;

    private readonly IconCache _iconCache;

    public MainViewModel(
        ViewerDatabase db, UserRepository users, TweetRepository tweets,
        JsonlImporter importer, ReadMarkQueue readQueue, FetchProcessService fetchService,
        IconCache iconCache)
    {
        _db = db;
        _users = users;
        _importer = importer;
        _iconCache = iconCache;
        FetchService = fetchService;
        TweetList = new TweetListViewModel(db, tweets, readQueue, iconCache);
        TweetList.UnreadDelta += OnUnreadDelta;
        MediaGrid = new MediaGridViewModel(db, tweets);
    }

    /// <summary>起動時: data/ 直下の既存アーカイブを登録 → 全ユーザー差分取込 → 一覧表示。</summary>
    public async Task InitializeAsync()
    {
        StatusText = "既存データを確認しています…";
        await _users.RegisterExistingDataDirsAsync();
        await RefreshUsersAsync();

        foreach (var user in Users.ToList())
        {
            var progress = new Progress<ImportProgress>(p =>
                StatusText = $"{user.Username} を取込中… {p.BytesDone * 100 / Math.Max(1, p.BytesTotal)}% ({p.Imported:N0}件)");
            var result = await _importer.ImportUserAsync(user.Username, progress);
            if (result.NewTweets > 0 || result.SkippedLines > 0)
                StatusText = $"{user.Username}: 新規 {result.NewTweets:N0}件を取込" +
                             (result.SkippedLines > 0 ? $" (壊れ行 {result.SkippedLines} をスキップ)" : "");
        }
        await RefreshUsersAsync();
        StatusText = "準備完了";

        if (SelectedUser is null && Users.Count > 0)
            SelectedUser = Users[0];
    }

    public async Task RefreshUsersAsync()
    {
        var rows = await _users.GetAllAsync();
        var byName = Users.ToDictionary(u => u.Username, StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (byName.TryGetValue(row.Username, out var existing))
                existing.ApplyCounts(row);
            else
                Users.Add(new UserItemViewModel(row));
        }
        foreach (var user in Users)
            ResolveUserIcon(user);
    }

    private async void ResolveUserIcon(UserItemViewModel user)
    {
        try
        {
            if (user.IconUrl is { Length: > 0 } url)
                user.IconPath = await _iconCache.GetLocalPathAsync(url) ?? user.IconPath;
        }
        catch (Exception)
        {
            // アイコンなし (プレースホルダのまま)
        }
    }

    partial void OnSelectedUserChanged(UserItemViewModel? value) => _ = ResetListAsync();

    partial void OnUnreadOnlyChanged(bool value) => _ = ResetListAsync();

    partial void OnIsMediaViewChanged(bool value) => _ = ResetListAsync();

    private Task ResetListAsync() =>
        IsMediaView
            ? MediaGrid.ResetAsync(SelectedUser?.Username)
            : TweetList.ResetAsync(
                SelectedUser?.Username, SelectedUser?.DisplayName ?? "",
                SelectedUser?.IconUrl, UnreadOnly);

    private void OnUnreadDelta(string username, long delta)
    {
        var user = Users.FirstOrDefault(
            u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
        if (user is not null)
            user.UnreadCount = Math.Max(0, user.UnreadCount + delta);
    }

    /// <summary>ユーザー追加(AddUserDialog から)。登録後に一覧を更新して選択。</summary>
    public async Task<UserItemViewModel?> AddUserAsync(string username)
    {
        username = username.TrimStart('@').Trim();
        if (username.Length == 0 || username.Any(c => !char.IsLetterOrDigit(c) && c != '_'))
        {
            StatusText = "ユーザー名は英数字と _ のみ使用できます";
            return null;
        }
        await _users.AddAsync(username);
        await RefreshUsersAsync();
        var added = Users.FirstOrDefault(
            u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
        if (added is not null)
            SelectedUser = added;
        return added;
    }

    /// <summary>取得完了後の差分取込+画面反映(UpdateLogWindow から)。</summary>
    public async Task OnFetchCompletedAsync(string username)
    {
        var progress = new Progress<ImportProgress>(p =>
            StatusText = $"{username} を取込中… ({p.Imported:N0}件)");
        var result = await _importer.ImportUserAsync(username, progress);
        StatusText = $"{username}: 新規 {result.NewTweets:N0}件を取込";
        await RefreshUsersAsync();
        if (string.Equals(SelectedUser?.Username, username, StringComparison.OrdinalIgnoreCase))
            await ResetListAsync();
    }

    private bool CanRebuild() => SelectedUser is not null && !IsFetching;

    [RelayCommand(CanExecute = nameof(CanRebuild))]
    private async Task Rebuild()
    {
        if (SelectedUser is not { } user)
            return;
        StatusText = $"{user.Username} のインデックスを再構築中…";
        var progress = new Progress<ImportProgress>(p =>
            StatusText = $"{user.Username} を再取込中… {p.BytesDone * 100 / Math.Max(1, p.BytesTotal)}% ({p.Imported:N0}件)");
        var result = await _importer.RebuildUserAsync(user.Username, progress);
        StatusText = $"{user.Username}: 再構築完了 ({result.NewTweets:N0}件)";
        await RefreshUsersAsync();
        await ResetListAsync();
    }
}
