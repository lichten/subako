using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TweetViewer.Data;
using TweetViewer.Services;

namespace TweetViewer.ViewModels;

/// <summary>フォロー一括登録の結果 (対象件数 / 新規登録数 / 付与したタグ数)。</summary>
public sealed record FollowingsImportResult(int Total, int Added, int TagCount);

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ViewerDatabase _db;
    private readonly UserRepository _users;
    private readonly TagRepository _tags;
    private readonly JsonlImporter _importer;

    public TweetListViewModel TweetList { get; }
    public MediaGridViewModel MediaGrid { get; }
    public FetchProcessService FetchService { get; }
    public JsonlImporter Importer => _importer;

    /// <summary>true = メディア欄、false = タイムライン。</summary>
    [ObservableProperty]
    private bool _isMediaView;

    public ObservableCollection<UserItemViewModel> Users { get; } = new();

    /// <summary>サイドバー表示用のタグフィルタ済みビュー。</summary>
    public ICollectionView UsersView { get; }

    /// <summary>
    /// 実在するタグのみ (ContextMenu のタグ付け外し / タグの整理ダイアログ用)。
    /// 疑似タグを混ぜてはいけない。追加・削除は RefreshTagsAsync 内で
    /// TagFilterOptions と必ず対で行うこと。
    /// </summary>
    public ObservableCollection<TagItemViewModel> Tags { get; } = new();

    /// <summary>
    /// タグフィルタ ComboBox 用。先頭が疑似タグ「(タグなし)」で、以降は Tags と同じ並び。
    /// SelectedTagFilter の参照同一性と ComboBox の選択を保つため Clear() は禁止
    /// (Reset 通知で Selector が選択を捨てる)。Tags と対で差分更新する。
    /// </summary>
    public ObservableCollection<TagItemViewModel> TagFilterOptions { get; } =
        new() { TagItemViewModel.CreateUntagged() };

    /// <summary>null = 全件表示。IsUntagged = タグ未設定の項目のみ表示。</summary>
    [ObservableProperty]
    private TagItemViewModel? _selectedTagFilter;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RebuildCommand))]
    private UserItemViewModel? _selectedUser;

    /// <summary>保存済み検索 (サイドバー「検索」セクション)。</summary>
    public ObservableCollection<SearchItemViewModel> Searches { get; } = new();

    /// <summary>検索セクション表示用のタグフィルタ済みビュー。</summary>
    public ICollectionView SearchesView { get; }

    /// <summary>選択中の検索バケット (SelectedUser と相互排他)。</summary>
    [ObservableProperty]
    private SearchItemViewModel? _selectedSearch;

    /// <summary>
    /// 統合タイムライン (表示中のユーザー・検索をすべて混ぜた時系列)。
    /// SelectedUser / SelectedSearch と相互排他の第 3 の選択状態。
    /// </summary>
    [ObservableProperty]
    private bool _isAllTimeline;

    [ObservableProperty]
    private bool _unreadOnly;

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>取得サブプロセスの多重起動防止(全「更新」ボタンを disable)。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RebuildCommand))]
    private bool _isFetching;

    private readonly IconCache _iconCache;

    /// <summary>前回終了時のタグフィルタ (InitializeAsync で Tags 読込後に適用)。</summary>
    private readonly long? _initialTagFilterId;

    public MainViewModel(
        ViewerDatabase db, UserRepository users, TweetRepository tweets, TagRepository tags,
        JsonlImporter importer, ReadMarkQueue readQueue, FetchProcessService fetchService,
        IconCache iconCache, bool unreadOnly = false, long? tagFilterId = null)
    {
        // プロパティ経由だと SelectedUser 確定前に ResetListAsync が走るためフィールドを直接初期化
        _unreadOnly = unreadOnly;
        _initialTagFilterId = tagFilterId;
        _db = db;
        _users = users;
        _tags = tags;
        _importer = importer;
        _iconCache = iconCache;
        FetchService = fetchService;
        TweetList = new TweetListViewModel(db, tweets, readQueue, iconCache);
        TweetList.UnreadDelta += OnUnreadDelta;
        MediaGrid = new MediaGridViewModel(db, tweets);
        UsersView = CollectionViewSource.GetDefaultView(Users);
        UsersView.Filter = FilterUser;
        SearchesView = CollectionViewSource.GetDefaultView(Searches);
        SearchesView.Filter = FilterSearch;
    }

    /// <summary>起動時: data/ 直下の既存アーカイブと検索バケットを登録 → 差分取込 → 一覧表示。</summary>
    public async Task InitializeAsync()
    {
        StatusText = "既存データを確認しています…";
        await _users.RegisterExistingDataDirsAsync();
        await _users.RegisterExistingSearchDirsAsync();
        await RefreshUsersAsync();
        await RefreshSearchesAsync();
        await RefreshTagsAsync();   // Users / Searches が埋まってからタグを反映
        RestoreTagFilter();

        var targets = Users.Select(u => u.Username)
            .Concat(Searches.Select(s => s.Username))
            .ToList();
        foreach (var username in targets)
        {
            var progress = new Progress<ImportProgress>(p =>
                StatusText = $"{username} を取込中… {p.BytesDone * 100 / Math.Max(1, p.BytesTotal)}% ({p.Imported:N0}件)");
            var result = await _importer.ImportUserAsync(username, progress);
            if (result.NewTweets > 0 || result.SkippedLines > 0)
                StatusText = $"{username}: 新規 {result.NewTweets:N0}件を取込" +
                             (result.SkippedLines > 0 ? $" (壊れ行 {result.SkippedLines} をスキップ)" : "");
        }
        await RefreshUsersAsync();
        await RefreshSearchesAsync();
        StatusText = "準備完了";

        // タグフィルタ復元で Users[0] が非表示になりうるため、表示中の先頭を選ぶ
        if (SelectedUser is null && SelectedSearch is null && !IsAllTimeline)
            SelectedUser = UsersView.Cast<UserItemViewModel>().FirstOrDefault();
    }

    /// <summary>前回終了時のタグフィルタを復元する (タグが消えていれば「すべて表示」のまま)。</summary>
    private void RestoreTagFilter()
    {
        if (_initialTagFilterId is not { } id)
            return;
        SelectedTagFilter = id == TagItemViewModel.UntaggedTagId
            ? TagFilterOptions.FirstOrDefault(t => t.IsUntagged)
            : Tags.FirstOrDefault(t => t.TagId == id);
    }

    public async Task RefreshUsersAsync()
    {
        var rows = await _users.GetAllAsync();
        var byName = Users.ToDictionary(u => u.Username, StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (byName.Remove(row.Username, out var existing))
                existing.ApplyCounts(row);
            else
                Users.Add(new UserItemViewModel(row));
        }
        // DB から消えた行 (削除されたユーザー) を一覧から除く
        foreach (var removed in byName.Values)
        {
            Users.Remove(removed);
            if (SelectedUser == removed)
                SelectedUser = null;
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

    private bool FilterUser(object obj) => MatchesTagFilter(((UserItemViewModel)obj).Tags);

    private bool FilterSearch(object obj) => MatchesTagFilter(((SearchItemViewModel)obj).Tags);

    /// <summary>
    /// タグフィルタ判定。未選択 = 全件、「(タグなし)」= タグ 0 件の行のみ、
    /// それ以外 = そのタグが付いた行のみ。
    /// </summary>
    private bool MatchesTagFilter(IReadOnlyCollection<TagItemViewModel> tags)
    {
        if (SelectedTagFilter is not { } filter)
            return true;
        return filter.IsUntagged
            ? tags.Count == 0
            : tags.Any(t => t.TagId == filter.TagId);
    }

    partial void OnSelectedTagFilterChanged(TagItemViewModel? value)
    {
        UsersView.Refresh();
        SearchesView.Refresh();
        // 統合表示は「表示中の集合」がフィルタで変わるため作り直す
        if (IsAllTimeline)
        {
            _ = ResetListAsync();
            return;
        }
        // 選択中の項目がフィルタで消えたら表示中の先頭ユーザーを選択 (空ペイン回避)
        if (SelectedUser is not null && !FilterUser(SelectedUser))
            SelectedUser = UsersView.Cast<UserItemViewModel>().FirstOrDefault();
        if (SelectedSearch is not null && !FilterSearch(SelectedSearch))
        {
            SelectedSearch = null;
            SelectedUser ??= UsersView.Cast<UserItemViewModel>().FirstOrDefault();
        }
    }

    /// <summary>tags / user_tags を再読込して Tags と各ユーザーの Tags を更新。</summary>
    public async Task RefreshTagsAsync()
    {
        var rows = await _tags.GetAllAsync();
        var byId = Tags.ToDictionary(t => t.TagId);
        foreach (var row in rows)
        {
            if (byId.Remove(row.TagId, out var existing))
                existing.Apply(row);
            else
            {
                var added = new TagItemViewModel(row);
                Tags.Add(added);
                TagFilterOptions.Add(added);   // 先頭の「(タグなし)」は動かさず末尾へ
            }
        }
        // 消えたタグを除去 (SelectedTagFilter の参照同一性を保つため作り直さない)
        foreach (var removed in byId.Values)
        {
            // ComboBox 側の自動 null 化より先に VM 主導で解除する
            // (CollectionChanged の最中に OnSelectedTagFilterChanged が走るのを避ける)
            if (SelectedTagFilter == removed)
                SelectedTagFilter = null;
            Tags.Remove(removed);
            TagFilterOptions.Remove(removed);
        }

        var assignments = await _tags.GetAssignmentsAsync();
        var tagById = Tags.ToDictionary(t => t.TagId);
        foreach (var user in Users)
            ApplyAssignedTags(user.Username, user.ApplyTags);
        foreach (var search in Searches)
            ApplyAssignedTags(search.Username, search.ApplyTags);
        UsersView.Refresh();
        SearchesView.Refresh();

        void ApplyAssignedTags(string username, Action<IEnumerable<TagItemViewModel>> apply)
        {
            var ids = assignments.TryGetValue(username, out var list) ? list : (IReadOnlyList<long>)[];
            apply(ids.Where(tagById.ContainsKey).Select(id => tagById[id]));
        }
    }

    /// <summary>タグの付け外し (ContextMenu のチェック項目から)。</summary>
    public Task ToggleTagAsync(UserItemViewModel user, TagItemViewModel tag, bool assign) =>
        ToggleTagCoreAsync(user.Username, user.Tags, tag, assign);

    /// <summary>検索バケット版のタグ付け外し。</summary>
    public Task ToggleTagAsync(SearchItemViewModel search, TagItemViewModel tag, bool assign) =>
        ToggleTagCoreAsync(search.Username, search.Tags, tag, assign);

    private async Task ToggleTagCoreAsync(
        string username, ObservableCollection<TagItemViewModel> tags, TagItemViewModel tag, bool assign)
    {
        if (assign)
        {
            if (tags.Any(t => t.TagId == tag.TagId))
                return;
            await _tags.AssignAsync(username, tag.TagId);
            tags.Add(tag);
            tag.UserCount++;
        }
        else
        {
            var existing = tags.FirstOrDefault(t => t.TagId == tag.TagId);
            if (existing is null)
                return;
            await _tags.UnassignAsync(username, tag.TagId);
            tags.Remove(existing);
            tag.UserCount = Math.Max(0, tag.UserCount - 1);
        }
        UsersView.Refresh();
        SearchesView.Refresh();
    }

    /// <summary>新規タグ作成 + 対象 (ユーザーまたは検索バケット) へ付与 (AddTagDialog から)。</summary>
    public async Task CreateAndAssignTagAsync(string name, string username)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            StatusText = "タグ名を入力してください";
            return;
        }
        var tagId = await _tags.AddAsync(name);
        await _tags.AssignAsync(username, tagId);
        await RefreshTagsAsync();
    }

    /// <summary>タグ削除 (ManageTagsDialog から)。</summary>
    public async Task DeleteTagAsync(TagItemViewModel tag)
    {
        await _tags.DeleteAsync(tag.TagId);
        await RefreshTagsAsync();
    }

    /// <summary>相互排他の連鎖 null 代入で二重リセットしないためのフラグ。</summary>
    private bool _switchingSelection;

    partial void OnSelectedUserChanged(UserItemViewModel? value)
    {
        // ユーザーと検索は相互排他 (null 代入の連鎖ループを避けるため非 null 遷移時のみ相手を消す)
        if (value is not null && (SelectedSearch is not null || IsAllTimeline))
        {
            _switchingSelection = true;
            SelectedSearch = null;
            IsAllTimeline = false;
            _switchingSelection = false;
        }
        if (!_switchingSelection)
            _ = ResetListAsync();
    }

    partial void OnSelectedSearchChanged(SearchItemViewModel? value)
    {
        if (value is not null && (SelectedUser is not null || IsAllTimeline))
        {
            _switchingSelection = true;
            SelectedUser = null;
            IsAllTimeline = false;
            _switchingSelection = false;
        }
        if (!_switchingSelection)
            _ = ResetListAsync();
    }

    partial void OnIsAllTimelineChanged(bool value)
    {
        if (value)
        {
            // 統合表示に入る: 個別選択を外す (サイドバーのハイライトも消える)
            _switchingSelection = true;
            SelectedUser = null;
            SelectedSearch = null;
            _switchingSelection = false;
            _ = ResetListAsync();
        }
        else if (!_switchingSelection)
        {
            // トグルを手動でオフ → 空ペイン回避で表示中の先頭ユーザーへ (既存の流儀)
            SelectedUser = UsersView.Cast<UserItemViewModel>().FirstOrDefault();
            if (SelectedUser is null)
                _ = ResetListAsync();
        }
    }

    partial void OnUnreadOnlyChanged(bool value) => _ = ResetListAsync();

    partial void OnIsMediaViewChanged(bool value) => _ = ResetListAsync();

    private Task ResetListAsync()
    {
        if (IsAllTimeline)
        {
            // 表示中 (タグフィルタ適用後) のスナップショット。列挙中の Refresh は例外になる
            var archives = VisibleArchives();
            return IsMediaView
                ? MediaGrid.ResetAsync(archives.Select(a => a.Username).ToList())
                : TweetList.ResetAsync(archives, UnreadOnly);
        }
        if (SelectedSearch is { } search)
            return IsMediaView
                ? MediaGrid.ResetAsync([search.Username])
                : TweetList.ResetAsync([new ArchiveInfo(search.Username, search.Label, null)], UnreadOnly);
        if (SelectedUser is { } user)
            return IsMediaView
                ? MediaGrid.ResetAsync([user.Username])
                : TweetList.ResetAsync(
                    [new ArchiveInfo(user.Username, user.DisplayName, user.IconUrl)], UnreadOnly);
        return IsMediaView
            ? MediaGrid.ResetAsync([])
            : TweetList.ResetAsync([], UnreadOnly);
    }

    /// <summary>統合タイムラインの対象 = サイドバーに表示中のユーザーと検索 (表示順)。</summary>
    private List<ArchiveInfo> VisibleArchives() =>
        UsersView.Cast<UserItemViewModel>()
            .Select(u => new ArchiveInfo(u.Username, u.DisplayName, u.IconUrl))
            .Concat(SearchesView.Cast<SearchItemViewModel>()
                .Select(s => new ArchiveInfo(s.Username, s.Label, null)))
            .ToList();

    private void OnUnreadDelta(string username, long delta)
    {
        var user = Users.FirstOrDefault(
            u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
        if (user is not null)
        {
            user.UnreadCount = Math.Max(0, user.UnreadCount + delta);
            return;
        }
        var search = Searches.FirstOrDefault(
            s => string.Equals(s.Username, username, StringComparison.OrdinalIgnoreCase));
        if (search is not null)
            search.UnreadCount = Math.Max(0, search.UnreadCount + delta);
    }

    /// <summary>@ を外して英数字と _ のみ検証。不正なら null。</summary>
    public static string? NormalizeUsername(string username)
    {
        username = username.TrimStart('@').Trim();
        return username.Length == 0 || username.Any(c => !char.IsLetterOrDigit(c) && c != '_')
            ? null : username;
    }

    /// <summary>ユーザー追加(AddUserDialog から)。登録後に一覧を更新して選択。</summary>
    public async Task<UserItemViewModel?> AddUserAsync(string username)
    {
        if (NormalizeUsername(username) is not { } name)
        {
            StatusText = "ユーザー名は英数字と _ のみ使用できます";
            return null;
        }
        await _users.AddAsync(name);
        await RefreshUsersAsync();
        var added = Users.FirstOrDefault(
            u => string.Equals(u.Username, name, StringComparison.OrdinalIgnoreCase));
        if (added is not null)
            SelectedUser = added;
        return added;
    }

    /// <summary>ツイート右クリックから作者を追加。表示は切り替えず、表示中ユーザー/検索のタグを引き継ぐ。</summary>
    public async Task<(UserItemViewModel? User, bool IsNew)> AddAuthorFromTweetAsync(string username)
    {
        if (NormalizeUsername(username) is not { } name)
        {
            StatusText = "ユーザー名は英数字と _ のみ使用できます";
            return (null, false);
        }
        // 自分自身を追加した場合に added.Tags と同一参照でループが壊れないようスナップショット
        var sourceTags = (SelectedSearch?.Tags ?? SelectedUser?.Tags)?.ToList() ?? [];
        var isNew = await _users.AddAsync(name);
        await RefreshUsersAsync();
        var added = Users.FirstOrDefault(
            u => string.Equals(u.Username, name, StringComparison.OrdinalIgnoreCase));
        if (added is null)
            return (null, false);
        foreach (var tag in sourceTags)
            await ToggleTagAsync(added, tag, assign: true);
        StatusText = isNew
            ? $"@{added.Username} を追加しました" +
              (sourceTags.Count > 0 ? $" (タグ {sourceTags.Count}件を引き継ぎ)" : "")
            : sourceTags.Count > 0
                ? $"@{added.Username} は登録済みのためタグのみ引き継ぎました"
                : $"@{added.Username} は登録済みです";
        return (added, isNew);
    }

    /// <summary>取得済みフォロー一覧の件数 (未取得なら 0)。確認ダイアログ用。</summary>
    public int SavedFollowingsCount(string sourceUsername) =>
        FollowingsFile.Count(_db.DataDir, sourceUsername);

    /// <summary>
    /// data/_followings/&lt;source&gt;.jsonl を読み、載っているアカウントを users へ
    /// 一括登録して指定タグを付ける (ImportFollowingsDialog → 取得完了後)。
    /// ツイートは取得しない — 後から「表示中をすべて更新...」をタグで絞って実行する想定。
    ///
    /// 一括処理なので RefreshUsersAsync / RefreshTagsAsync は最後に 1 回だけ呼ぶ。
    /// ToggleTagAsync は 1 件ごとに UsersView.Refresh() と tag.UserCount の手動加算を
    /// するため、ここでは絶対に使わないこと (数千回 Refresh すると固まる)。
    ///
    /// 既に登録済みのユーザーにも同じタグを付ける。「このタグ = @source のフォロー」
    /// という前提が崩れると、後続の一括更新をタグで絞ったときに取りこぼす。
    /// </summary>
    public async Task<FollowingsImportResult> ImportFollowingsAsync(
        string sourceUsername, IReadOnlyList<long> existingTagIds,
        IReadOnlyList<string> newTagNames)
    {
        var entries = await Task.Run(() => FollowingsFile.Read(_db.DataDir, sourceUsername));
        // API 由来の handle も検証する。不正文字はフォルダ名や
        // 検索バケット ID (searches/<slug>) と衝突しうる
        var targets = entries
            .Select(e => (Username: NormalizeUsername(e.Username), e.DisplayName))
            .Where(e => e.Username is not null &&
                        !string.Equals(e.Username, sourceUsername,
                                       StringComparison.OrdinalIgnoreCase))
            .Select(e => (Username: e.Username!, e.DisplayName))
            .ToList();
        if (targets.Count == 0)
        {
            StatusText = $"@{sourceUsername} のフォロー一覧を読み込めませんでした";
            return new FollowingsImportResult(0, 0, 0);
        }

        StatusText = $"{targets.Count:N0} 件を登録中…";
        // タグ作成は「登録する対象が確定してから」。取得に失敗した実行で
        // 空タグだけが増えるのを避ける。AddAsync は名前の upsert なので、
        // 既存タグ名を打ち込んでも重複せず既存 ID に合流する
        var tagIds = new List<long>(existingTagIds.Distinct());
        foreach (var name in newTagNames)
        {
            var id = await _tags.AddAsync(name);
            if (!tagIds.Contains(id))
                tagIds.Add(id);
        }

        var added = await _users.AddManyAsync(targets);
        await _tags.AssignManyAsync(targets.Select(t => t.Username).ToList(), tagIds);

        await RefreshUsersAsync();
        await RefreshTagsAsync();
        StatusText =
            $"@{sourceUsername} のフォロー {targets.Count:N0} 件のうち " +
            $"{added.Count:N0} 件を新規登録しました" +
            (tagIds.Count > 0 ? $" (タグ {tagIds.Count} 件を付与)" : "") +
            " — ツイートは未取得です";
        return new FollowingsImportResult(targets.Count, added.Count, tagIds.Count);
    }

    /// <summary>取得完了後の差分取込+画面反映(UpdateLogWindow から)。username はバケット ID の場合もある。</summary>
    public async Task OnFetchCompletedAsync(string username)
    {
        var progress = new Progress<ImportProgress>(p =>
            StatusText = $"{username} を取込中… ({p.Imported:N0}件)");
        var result = await _importer.ImportUserAsync(username, progress);
        StatusText = $"{username}: 新規 {result.NewTweets:N0}件を取込";
        await RefreshUsersAsync();
        await RefreshSearchesAsync();
        var affectsCurrent =
            string.Equals(SelectedUser?.Username, username, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(SelectedSearch?.Username, username, StringComparison.OrdinalIgnoreCase) ||
            (IsAllTimeline && VisibleArchives().Any(a =>
                string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase)));
        if (affectsCurrent)
            await ResetListAsync();
    }

    /// <summary>検索バケット一覧を再読込 (ラベルは search.json の name → query → フォルダ名)。</summary>
    public async Task RefreshSearchesAsync()
    {
        var rows = await _users.GetSearchBucketsAsync();
        var byId = Searches.ToDictionary(s => s.Username, StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var meta = SearchMetadata.TryRead(_db.UserDir(row.Username));
            var query = meta?.Query ?? row.Username["searches/".Length..];
            if (byId.Remove(row.Username, out var existing))
                existing.ApplyCounts(row, query, meta?.Name);
            else
                Searches.Add(new SearchItemViewModel(row, query, meta?.Name));
        }
        foreach (var removed in byId.Values)
        {
            Searches.Remove(removed);
            if (SelectedSearch == removed)
                SelectedSearch = null;
        }
    }

    /// <summary>検索の名称・クエリを変更する (SearchEditDialog から)。取得済みツイートは保持。</summary>
    public async Task UpdateSearchAsync(SearchItemViewModel item, string newQuery, string? newName)
    {
        var queryChanged = item.Query != newQuery;
        SearchMetadata.Write(_db.UserDir(item.Username), newQuery, newName);
        await RefreshSearchesAsync();
        if (SelectedSearch == item)
            await ResetListAsync();
        StatusText = queryChanged
            ? "クエリを変更しました。次回の更新/バックフィルから新しい条件で取得します"
            : $"検索「{item.Label}」を更新しました";
    }

    /// <summary>
    /// 新規 API 検索の準備 (SearchDialog から)。RT数・いいね数の下限はサーバー側で
    /// 絞るため min_retweets: / min_faves: 演算子としてクエリに付与する。
    /// OR の結合順が壊れないよう元クエリを (...) で括る。
    /// </summary>
    public async Task<(string BucketId, string FinalQuery)> StartApiSearchAsync(
        string query, long? minRetweets, long? minFaves, string? name = null)
    {
        var finalQuery = SearchQueryOperators.Compose(query, minRetweets, minFaves);
        var bucketId = "searches/" + SearchSlug.From(finalQuery);
        await _users.AddAsync(bucketId);
        // 名称が最初から表示されるよう search.json を先に作る (Python 側は既存一致で素通り)
        SearchMetadata.Write(_db.UserDir(bucketId), finalQuery, name);
        await RefreshSearchesAsync();
        return (bucketId, finalQuery);
    }

    /// <summary>
    /// アーカイブを DB から削除し、フォルダを削除 (deleteFiles) または _trash へ退避する。
    /// 戻り値は失敗理由 (成功時 null)。フォルダが残ると次回起動の自動登録で復活するため、
    /// 呼び出し側は失敗を必ずユーザーに伝えること。
    /// </summary>
    private async Task<string?> RemoveArchiveAsync(string username, bool deleteFiles)
    {
        await _users.DeleteArchiveAsync(username);
        try
        {
            if (deleteFiles)
            {
                var dir = _db.UserDir(username);
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            else
            {
                ArchiveTrash.MoveToTrash(_db.DataDir, username);
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // async void ハンドラ経由なので捕まえないとアプリが落ちる
            return deleteFiles
                ? $"フォルダの削除に失敗しました: {ex.Message}"
                : $"フォルダの退避に失敗しました: {ex.Message}";
        }
    }

    /// <summary>ユーザーの削除 (DB + フォルダ)。戻り値は失敗理由 (成功時 null)。</summary>
    public async Task<string?> DeleteUserAsync(UserItemViewModel item, bool deleteFiles)
    {
        if (SelectedUser == item)
            SelectedUser = null;
        var error = await RemoveArchiveAsync(item.Username, deleteFiles);
        await RefreshUsersAsync();
        await RefreshTagsAsync();
        if (IsAllTimeline)
            await ResetListAsync();   // 統合表示は維持したまま削除分を反映
        else
            SelectedUser ??= UsersView.Cast<UserItemViewModel>().FirstOrDefault();
        StatusText = error ?? $"@{item.Username} を削除しました" +
            (deleteFiles ? "" : $" (データは {ArchiveTrash.TrashDirName} に退避)");
        return error;
    }

    /// <summary>検索バケットの削除 (DB + フォルダ)。戻り値は失敗理由 (成功時 null)。</summary>
    public async Task<string?> DeleteSearchAsync(SearchItemViewModel item, bool deleteFiles)
    {
        if (SelectedSearch == item)
            SelectedSearch = null;
        var error = await RemoveArchiveAsync(item.Username, deleteFiles);
        await RefreshSearchesAsync();
        await RefreshTagsAsync();
        if (IsAllTimeline)
            await ResetListAsync();   // 統合表示は維持したまま削除分を反映
        else
            SelectedUser ??= UsersView.Cast<UserItemViewModel>().FirstOrDefault();
        StatusText = error ?? $"検索「{item.Label}」を削除しました" +
            (deleteFiles ? "" : $" (データは {ArchiveTrash.TrashDirName} に退避)");
        return error;
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
