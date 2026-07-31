import Foundation
import SubakoCore

// AppModel は Windows 版 MainViewModel の移植で意図的に 1 ファイルに集約している。
// 分割リファクタは別課題 (.swiftlint.yml 参照)。
// swiftlint:disable file_length type_body_length

enum ArchiveSelection: Equatable, Hashable {
    case user(String)
    case search(String)
    /// 統合タイムライン (表示中のユーザー + 検索をすべて混ぜる — §4.5)。非永続。
    case all

    var username: String? {
        switch self {
        case .user(let u), .search(let u): return u
        case .all: return nil
        }
    }
}

enum ViewMode: Equatable {
    case timeline
    case media
}

/// タグフィルタ (§4.3)。nil 相当は AppModel.tagFilter = .all。
enum TagFilter: Equatable, Hashable {
    case all
    case untagged      // (タグなし)
    case tag(Int64)
}

/// サイドバー 1 行 (ユーザーまたは検索バケット)。
struct ArchiveItem: Identifiable, Equatable {
    var row: UserRow
    var tagIds: [Int64]
    var isSearch: Bool
    /// 検索: search.json の name → query → フォルダ名 (§4.2)
    var searchLabel: String?
    var searchQuery: String?
    /// 楽観更新される未読数
    var unread: Int64

    var id: String { row.username }

    var displayLabel: String {
        if isSearch {
            return searchLabel ?? slugName
        }
        return row.displayName ?? row.username
    }

    var slugName: String {
        String(row.username.dropFirst(SearchBuckets.prefix.count))
    }
}

/// 期間フィルタの UI 状態 (§7.3)。非永続。
struct DateFilterState: Equatable {
    var years: [Int] = []
    var year: Int?
    var month: Int?
    var day: Int?

    var isActive: Bool { year != nil }

    func range(timeZone: TimeZone = .current) -> DateRangeFilter? {
        guard let year else { return nil }
        return DateRangeFilter.fromLocalParts(year: year, month: month, day: day, timeZone: timeZone)
    }
}

@MainActor
@Observable
final class AppModel {
    // MARK: - 依存
    let settings: AppSettings
    private(set) var db: ViewerDatabase?
    private(set) var tweetRepo: TweetRepository?
    private(set) var userRepo: UserRepository?
    private(set) var tagRepo: TagRepository?
    private(set) var importer: JsonlImporter?
    private(set) var readQueue: ReadMarkQueue?
    /// 既読投入 (enqueue) の直列チェーン。フラッシュ前にこれを待てば取りこぼさない
    private var readEnqueueTask: Task<Void, Never>?
    private(set) var iconCache: IconCache?
    private(set) var thumbnailCache: IconCache?

    // MARK: - 状態
    var openError: String?
    var needsFirstRun = false
    var statusMessage = "準備完了"

    private(set) var users: [ArchiveItem] = []
    private(set) var searches: [ArchiveItem] = []
    private(set) var tags: [TagRow] = []

    var tagFilter: TagFilter = .all {
        didSet { onTagFilterChanged() }
    }

    var selection: ArchiveSelection?
    var viewMode: ViewMode = .timeline
    var unreadOnly = false
    var ascending = false
    var dateFilter = DateFilterState()

    // タイムライン / メディアのページング状態
    private(set) var timelineItems: [TweetItem] = []
    private(set) var timelineHasMore = true
    private(set) var timelineLoading = false
    private(set) var timelineLoadedOnce = false
    /// 直近のページ読込が失敗したときの表示用メッセージ (成功・リセットで消える)
    private(set) var timelineLoadError: String?
    private var timelineCursor: TweetCursor?
    private(set) var mediaItems: [MediaItem] = []
    private(set) var mediaHasMore = true
    private(set) var mediaLoading = false
    private(set) var mediaLoadedOnce = false
    private(set) var mediaLoadError: String?
    private var mediaCursor: MediaCursor?
    /// 表示対象の切替ごとに増える世代。古いロード結果の反映と自動既読の混線を防ぐ
    private(set) var generation = 0

    static let timelinePageSize = 200
    static let mediaPageSize = 120

    // MARK: - 取得 (フェーズ⑤)
    private(set) var isFetching = false
    private(set) var fetchLogLines: [String] = []
    private(set) var fetchTargetLabel = ""
    var showFetchLogRequested = false
    private var fetcherProcess: FetcherProcess?

    init(settings: AppSettings) {
        self.settings = settings
        unreadOnly = settings.unreadOnly
        ascending = settings.ascending
    }

    var isReadOnly: Bool { db?.isReadOnly ?? true }

    // MARK: - 起動

    func startup() async {
        guard settings.isConfigured else {
            needsFirstRun = true
            return
        }
        await openAndInitialize()
    }

    func openAndInitialize() async {
        needsFirstRun = false
        openError = nil
        // 開き直し (設定変更) の際は、古いキューの未書込を古い DB へ書き切ってから捨てる。
        // 新しい接続を開く前に済ませること
        await flushReadMarks()
        await readQueue?.shutdown()
        readQueue = nil
        readEnqueueTask = nil
        do {
            let dataDir = settings.effectiveDataDir
            let db = try ViewerDatabase(dataDir: dataDir, readOnly: settings.readOnlyMode)
            self.db = db
            tweetRepo = TweetRepository(db)
            userRepo = UserRepository(db)
            tagRepo = TagRepository(db)
            importer = JsonlImporter(db)
            iconCache = IconCache(dataDir: dataDir)
            thumbnailCache = IconCache(dataDir: dataDir, subDirectory: "thumbnails")
            // 直前で代入した直後の unwrap (起動シーケンスの不変条件)
            // swiftlint:disable:next force_unwrapping
            let queue = ReadMarkQueue(repo: tweetRepo!, readOnly: db.isReadOnly)
            readQueue = queue
            await queue.start()

            if !db.isReadOnly {
                // 既存データの自動登録 (§1.2 手順 3)
                // swiftlint:disable:next force_unwrapping
                _ = try await userRepo!.registerExistingDataDirs()
                // swiftlint:disable:next force_unwrapping
                _ = try await userRepo!.registerExistingSearchDirs()
            }
            try await reloadLists()
            restoreTagFilter()
            if !db.isReadOnly {
                await importAllArchives()
            }
            selectFirstVisible()
            AppLog.info("起動完了 dataDir=\(dataDir) readOnly=\(db.isReadOnly)")
        } catch {
            openError = (error as? (any LocalizedError))?.errorDescription ?? "\(error)"
            AppLog.error("DB オープン失敗: \(error)")
        }
    }

    /// 全アーカイブの JSONL 差分取込 (§1.2 手順 5)。
    func importAllArchives() async {
        guard let importer, let userRepo else { return }
        do {
            let all = try await userRepo.getAll() + userRepo.getSearchBuckets()
            for archive in all {
                let name = archive.username
                statusMessage = "\(name) を取込中…"
                let result = try await importer.importUser(name) { [weak self] progress in
                    let percent = progress.bytesTotal > 0
                        ? Int(progress.bytesDone * 100 / progress.bytesTotal) : 100
                    Task { @MainActor in
                        self?.statusMessage = "\(name) を取込中… \(percent)% (\(progress.imported)件)"
                    }
                }
                if result.newTweets > 0 {
                    AppLog.info("取込 \(name): 新規 \(result.newTweets) 件")
                }
            }
            try await reloadLists()
            statusMessage = "準備完了"
        } catch {
            statusMessage = "取込エラー: \(error.localizedDescription)"
            AppLog.error("取込エラー: \(error)")
        }
    }

    // MARK: - 一覧

    func reloadLists() async throws {
        guard let userRepo, let tagRepo, let db else { return }
        // 未読数は read_state を読み直して楽観値を上書きするので、先に書き切る (§8.2)
        await flushReadMarks()
        let userRows = try await userRepo.getAll()
        let searchRows = try await userRepo.getSearchBuckets()
        tags = try await tagRepo.getAll()
        let assignments = try await tagRepo.getAssignments()

        users = userRows.map { row in
            ArchiveItem(
                row: row, tagIds: assignments[row.username.lowercased()] ?? [],
                isSearch: false, searchLabel: nil, searchQuery: nil, unread: row.unreadCount)
        }
        searches = searchRows.map { row in
            let info = SearchMetadata.tryRead(bucketDir: db.userDir(row.username))
            return ArchiveItem(
                row: row, tagIds: assignments[row.username.lowercased()] ?? [],
                isSearch: true,
                searchLabel: info.map { $0.name ?? $0.query },
                searchQuery: info?.query, unread: row.unreadCount)
        }
    }

    // MARK: - タグフィルタ (§4.3)

    private func restoreTagFilter() {
        if settings.hasTagFilter {
            if let id = settings.tagFilterId {
                tagFilter = id == -1 ? .untagged : .tag(id)
            }
        }
    }

    func matchesTagFilter(_ item: ArchiveItem) -> Bool {
        switch tagFilter {
        case .all: return true
        case .untagged: return item.tagIds.isEmpty
        case .tag(let id): return item.tagIds.contains(id)
        }
    }

    var visibleUsers: [ArchiveItem] { users.filter(matchesTagFilter) }
    var visibleSearches: [ArchiveItem] { searches.filter(matchesTagFilter) }

    private func onTagFilterChanged() {
        switch tagFilter {
        case .all: settings.tagFilterId = nil
        case .untagged: settings.tagFilterId = -1
        case .tag(let id): settings.tagFilterId = id
        }
        settings.save()
        // 選択中がフィルタで隠れたら先頭へ (§4.3)。統合中は対象集合を組み直す
        switch selection {
        case .all:
            reloadCurrentView()
        case .user(let name):
            if !visibleUsers.contains(where: { $0.id == name }) {
                selectFirstVisible()
            }
        case .search(let name):
            if !visibleSearches.contains(where: { $0.id == name }) {
                selectFirstVisible()
            }
        case nil:
            selectFirstVisible()
        }
    }

    func selectFirstVisible() {
        if let first = visibleUsers.first {
            select(.user(first.id))
        } else if let first = visibleSearches.first {
            select(.search(first.id))
        } else {
            selection = nil
            timelineItems = []
            mediaItems = []
        }
    }

    // MARK: - 選択・表示対象

    func select(_ newSelection: ArchiveSelection) {
        selection = newSelection
        reloadCurrentView()
    }

    /// 表示対象のアーカイブ集合 (§4.5: 統合はタグフィルタ適用後の全 visible)。
    var currentUsernames: [String] {
        switch selection {
        case .user(let name): return [name]
        case .search(let name): return [name]
        case .all: return (visibleUsers + visibleSearches).map(\.id)
        case nil: return []
        }
    }

    func reloadCurrentView() {
        generation += 1
        timelineItems = []
        timelineCursor = nil
        timelineHasMore = true
        timelineLoadedOnce = false
        timelineLoadError = nil
        mediaItems = []
        mediaCursor = nil
        mediaHasMore = true
        mediaLoadedOnce = false
        mediaLoadError = nil
        Task {
            // 再クエリが read_state の古い状態を読まないよう、先に書き切る (§8.2)
            await flushReadMarks()
            await refreshDateBounds()
            // リセットの初回ページは旧ロードが実行中でも必ず走らせる
            await loadNextTimelinePage(force: true)
            await loadNextMediaPage(force: true)
        }
    }

    // MARK: - 期間フィルタ (§7.3)

    private func refreshDateBounds() async {
        guard let tweetRepo else { return }
        let usernames = currentUsernames
        let bounds = try? await tweetRepo.getDateBounds(usernames: usernames)
        if let bounds {
            dateFilter.years = DateRangeFilter.yearsCovered(
                minEpoch: bounds.min, maxEpoch: bounds.max, timeZone: .current)
        } else {
            dateFilter.years = []
        }
        // 表示対象の切替で選択年が範囲外になったら全期間へ自動リセット
        if let year = dateFilter.year, !dateFilter.years.contains(year) {
            dateFilter.year = nil
            dateFilter.month = nil
            dateFilter.day = nil
        }
    }

    func setDateFilter(year: Int?, month: Int?, day: Int?) {
        dateFilter.year = year
        dateFilter.month = year == nil ? nil : month
        dateFilter.day = (year == nil || month == nil) ? nil : day
        reloadPages()
    }

    /// ◀▶: 選択中の最も細かい粒度で前後へ移動。実データ範囲の外へは出ない。
    func canStepDateFilter(_ direction: Int) -> Bool {
        guard let year = dateFilter.year, let first = dateFilter.years.first,
              let last = dateFilter.years.last else { return false }
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = .current
        if let month = dateFilter.month, let day = dateFilter.day {
            var comps = DateComponents()
            (comps.year, comps.month, comps.day) = (year, month, day)
            guard let date = calendar.date(from: comps),
                  let next = calendar.date(byAdding: .day, value: direction, to: date)
            else { return false }
            let y = calendar.component(.year, from: next)
            return y >= first && y <= last
        }
        if let month = dateFilter.month {
            let total = year * 12 + (month - 1) + direction
            let y = total / 12
            return y >= first && y <= last
        }
        let y = year + direction
        return y >= first && y <= last
    }

    func stepDateFilter(_ direction: Int) {
        guard let year = dateFilter.year, canStepDateFilter(direction) else { return }
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = .current
        if let month = dateFilter.month, let day = dateFilter.day {
            var comps = DateComponents()
            (comps.year, comps.month, comps.day) = (year, month, day)
            if let date = calendar.date(from: comps),
               let next = calendar.date(byAdding: .day, value: direction, to: date) {
                let c = calendar.dateComponents([.year, .month, .day], from: next)
                setDateFilter(year: c.year, month: c.month, day: c.day)
            }
        } else if let month = dateFilter.month {
            let total = year * 12 + (month - 1) + direction
            setDateFilter(year: total / 12, month: total % 12 + 1, day: nil)
        } else {
            setDateFilter(year: year + direction, month: nil, day: nil)
        }
    }

    /// フィルタ変更 (未読のみ / 古い順 / 期間) 時のリロード。対象集合は変えない。
    func reloadPages() {
        generation += 1
        timelineItems = []
        timelineCursor = nil
        timelineHasMore = true
        timelineLoadedOnce = false
        timelineLoadError = nil
        mediaItems = []
        mediaCursor = nil
        mediaHasMore = true
        mediaLoadedOnce = false
        mediaLoadError = nil
        Task {
            // 再クエリが read_state の古い状態を読まないよう、先に書き切る (§8.2)
            await flushReadMarks()
            // リセットの初回ページは旧ロードが実行中でも必ず走らせる
            await loadNextTimelinePage(force: true)
            await loadNextMediaPage(force: true)
        }
    }

    func setUnreadOnly(_ value: Bool) {
        unreadOnly = value
        settings.unreadOnly = value
        settings.save()
        reloadPages()
    }

    func setAscending(_ value: Bool) {
        ascending = value
        settings.ascending = value
        settings.save()
        reloadPages()
    }

    // MARK: - ページング (§5.6)

    /// force = リセット直後の初回ロード。旧リセットのロードが実行中でもスキップしない
    /// (旧ロードの結果は世代ガードで破棄されるため、ここで譲ると空のままになる)
    func loadNextTimelinePage(force: Bool = false) async {
        guard let tweetRepo, let db, force || !timelineLoading, timelineHasMore else { return }
        let usernames = currentUsernames
        guard !usernames.isEmpty else {
            timelineLoadedOnce = true
            timelineHasMore = false
            return
        }
        timelineLoading = true
        let gen = generation
        // 自分より新しいリセットが始まっていたら、timelineLoading は後発 (force:true で
        // 入ってきた側) の所有物なので触らない。横取り解除すると、リセット中に
        // 余計な追加ロードが割り込んで表示位置が乱れる
        defer { if gen == generation { timelineLoading = false } }
        let cursor = timelineCursor
        let range = dateFilter.range()
        let dataDir = db.dataDir
        do {
            let page = try await tweetRepo.getPage(
                usernames: usernames, unreadOnly: unreadOnly, after: cursor,
                limit: Self.timelinePageSize, range: range, ascending: ascending)
            // カード構築 (ファイル存在チェック・raw 再読み) はバックグラウンドで
            let items = await Task.detached(priority: .userInitiated) { () -> [TweetItem] in
                page.rows.map { row in
                    TweetItem.build(
                        row: row,
                        media: page.media[row.tweetId] ?? [],
                        archives: page.archivesByTweetId[row.tweetId] ?? [],
                        dataDir: dataDir,
                        jsonlPath: ((dataDir as NSString)
                            .appendingPathComponent(row.username) as NSString)
                            .appendingPathComponent("tweets.jsonl"))
                }
            }.value
            guard gen == generation else { return }
            timelineItems.append(contentsOf: items)
            if let last = page.rows.last {
                timelineCursor = TweetCursor(sortKey: last.sortKey, idInt: last.idInt)
            }
            timelineHasMore = page.rows.count == Self.timelinePageSize
            timelineLoadedOnce = true
            timelineLoadError = nil
        } catch {
            AppLog.error("タイムライン読込エラー: \(error)")
            // ページングは止めない。timelineHasMore と timelineCursor をそのまま残すので、
            // 「再試行」か次のスクロールで同じページをやり直せる
            // (Windows 版 TweetListViewModel も失敗時に HasMore を触らない)
            let message = Self.userMessage(for: error)
            timelineLoadError = message
            timelineLoadedOnce = true
            statusMessage = "タイムライン読込エラー: \(message)"
        }
    }

    /// force の意味は loadNextTimelinePage と同じ。
    func loadNextMediaPage(force: Bool = false) async {
        guard let tweetRepo, let db, force || !mediaLoading, mediaHasMore else { return }
        let usernames = currentUsernames
        guard !usernames.isEmpty else {
            mediaLoadedOnce = true
            mediaHasMore = false
            return
        }
        mediaLoading = true
        let gen = generation
        defer { if gen == generation { mediaLoading = false } }
        let cursor = mediaCursor
        let range = dateFilter.range()
        let dataDir = db.dataDir
        do {
            let rows = try await tweetRepo.getMediaPage(
                usernames: usernames, after: cursor,
                limit: Self.mediaPageSize, range: range, ascending: ascending)
            let items = await Task.detached(priority: .userInitiated) { () -> [MediaItem] in
                rows.compactMap { row in
                    // 統合では images/ の場所を行ごとの username から解決する (§11.2)
                    let imagesDir = ((dataDir as NSString)
                        .appendingPathComponent(row.username) as NSString)
                        .appendingPathComponent("images")
                    guard let path = LocalMediaFiles.resolve(
                        imagesDir: imagesDir, tweetId: row.tweetId,
                        index: row.idx, ext: row.ext)
                    else { return nil }
                    return MediaItem(media: row, localPath: path)
                }
            }.value
            guard gen == generation else { return }
            mediaItems.append(contentsOf: items)
            if let last = rows.last {
                mediaCursor = MediaCursor(sortKey: last.sortKey, idInt: last.idInt, idx: last.idx)
            }
            mediaHasMore = rows.count == Self.mediaPageSize
            mediaLoadedOnce = true
            mediaLoadError = nil
        } catch {
            AppLog.error("メディア読込エラー: \(error)")
            // タイムライン側と同じ — ページングは止めず、再試行できる状態で残す
            let message = Self.userMessage(for: error)
            mediaLoadError = message
            mediaLoadedOnce = true
            statusMessage = "メディア読込エラー: \(message)"
        }
    }

    /// 読込失敗行の「再試行」。失敗したページをそのまま引き直す
    func retryTimelineLoad() async {
        timelineLoadError = nil
        await loadNextTimelinePage()
    }

    func retryMediaLoad() async {
        mediaLoadError = nil
        await loadNextMediaPage()
    }

    /// SQLiteError の description は SQL 文まで含むので、表示は sqlite のメッセージだけにする
    /// (完全な内容はログに残る)
    private static func userMessage(for error: any Error) -> String {
        (error as? SQLiteError)?.message ?? error.localizedDescription
    }

    // MARK: - 既読 (§8)

    /// スクロール自動既読 (バッチ)。楽観反映 + 未読数ファンアウト (§8.3)。
    func markRead(itemIds: [String]) {
        guard !isReadOnly, let readQueue else { return }
        var marked: [(String, String)] = []
        for id in itemIds {
            guard let index = timelineItems.firstIndex(where: { $0.id == id }),
                  !timelineItems[index].row.isRead
            else { continue }
            timelineItems[index].row.isRead = true
            let row = timelineItems[index].row
            marked.append((row.tweetId, row.username))
            applyUnreadDelta(
                tweetId: row.tweetId, primary: row.username,
                archives: timelineItems[index].archives, delta: -1)
        }
        guard !marked.isEmpty else { return }
        // 投入は直列につなぐ。flushReadMarks がこれを待てば、投入待ちを取りこぼさない
        readEnqueueTask = Task { [previous = readEnqueueTask] in
            await previous?.value
            for (tweetId, username) in marked {
                await readQueue.enqueue(tweetId: tweetId, username: username)
            }
        }
    }

    /// 表示切替・再クエリの前に、投入待ちを含めて既読を DB へ書き切る (§8.2)。
    /// これをしないと再クエリが read_state の古い状態を読み、既読が未読に戻って見える
    func flushReadMarks() async {
        await readEnqueueTask?.value
        await readQueue?.flush()
    }

    /// 手動トグル (§5.2)。即時 DB 書込。
    func toggleRead(itemId: String) {
        guard !isReadOnly, let tweetRepo else { return }
        guard let index = timelineItems.firstIndex(where: { $0.id == itemId }) else { return }
        let newValue = !timelineItems[index].row.isRead
        timelineItems[index].row.isRead = newValue
        let row = timelineItems[index].row
        let archives = timelineItems[index].archives
        applyUnreadDelta(
            tweetId: row.tweetId, primary: row.username,
            archives: archives, delta: newValue ? -1 : 1)
        Task {
            try? await tweetRepo.setRead(
                tweetId: row.tweetId, username: row.username, read: newValue)
        }
    }

    /// 同じツイートを含む全アーカイブ (タグフィルタで隠れているものも含む) の未読数を更新。
    private func applyUnreadDelta(tweetId: String, primary: String, archives: [String], delta: Int64) {
        let targets = archives.isEmpty ? [primary] : archives
        for name in targets {
            if let i = users.firstIndex(where: { $0.id.caseInsensitiveCompare(name) == .orderedSame }) {
                users[i].unread = max(0, users[i].unread + delta)
            }
            if let i = searches.firstIndex(where: { $0.id.caseInsensitiveCompare(name) == .orderedSame }) {
                searches[i].unread = max(0, searches[i].unread + delta)
            }
        }
    }

    // MARK: - アーカイブ操作

    /// ユーザー追加 (§9.2)。戻り値 = 新規登録されたか。
    func addUser(_ input: String) async throws -> Bool {
        guard let userRepo else { return false }
        let name = UsernameRules.normalize(input)
        guard UsernameRules.isValid(name) else {
            throw AppError("ユーザー名は英数字と _ のみ使用できます")
        }
        let added = try await userRepo.add(name)
        try await reloadLists()
        select(.user(name))
        return added
    }

    /// ツイートの作者をユーザーに追加 (§5.2)。表示中のタグを引き継ぐ。表示は切り替えない。
    func addAuthorFromTweet(_ item: TweetItem) async throws -> (added: Bool, username: String) {
        guard let userRepo, let tagRepo else { throw AppError("DB 未接続") }
        guard let author = item.addableAuthor, UsernameRules.isValid(author) else {
            throw AppError("このツイートの作者を特定できません")
        }
        let added = try await userRepo.add(author)
        // 表示中のユーザー/検索に付いているタグを引き継ぐ
        if let current = selection?.username {
            let currentItem = (users + searches).first { $0.id == current }
            if let tagIds = currentItem?.tagIds, !tagIds.isEmpty {
                try await tagRepo.assignMany(usernames: [author], tagIds: tagIds)
            }
        }
        try await reloadLists()
        return (added, author)
    }

    /// アーカイブ削除 (§10.1)。deleteData = true で完全削除、false で _trash/ へ退避。
    func deleteArchive(_ username: String, deleteData: Bool) async throws {
        guard let userRepo, let db else { return }
        guard !isFetching else { throw AppError("取得実行中は削除できません") }
        if deleteData {
            let dir = db.userDir(username)
            if FileManager.default.fileExists(atPath: dir) {
                try FileManager.default.removeItem(atPath: dir)
            }
        } else {
            // フォルダが残ると次回起動の自動登録で復活するため、移動失敗はエラーにする
            _ = try ArchiveTrash.moveToTrash(dataDir: db.dataDir, username: username)
        }
        try await userRepo.deleteArchive(username)
        try await reloadLists()
        if selection?.username == username {
            selectFirstVisible()
        }
        statusMessage = "\(username) を削除しました"
    }

    /// インデックス再構築 (§10.2)。ユーザー選択中のみ。既読は保持。
    func rebuildIndex() async {
        guard case .user(let username)? = selection, let importer, !isFetching else { return }
        do {
            statusMessage = "\(username) を再構築中…"
            let result = try await importer.rebuildUser(username) { [weak self] progress in
                let percent = progress.bytesTotal > 0
                    ? Int(progress.bytesDone * 100 / progress.bytesTotal) : 100
                Task { @MainActor in
                    self?.statusMessage = "\(username) を再構築中… \(percent)%"
                }
            }
            try await reloadLists()
            reloadCurrentView()
            statusMessage = "再構築完了 (\(result.newTweets)件)"
        } catch {
            statusMessage = "再構築エラー: \(error.localizedDescription)"
        }
    }

    // MARK: - タグ (§4.4)

    func createTag(name: String) async throws -> Int64 {
        guard let tagRepo else { throw AppError("DB 未接続") }
        let id = try await tagRepo.add(name: name)
        try await reloadLists()
        return id
    }

    func setTag(_ tagId: Int64, on username: String, assigned: Bool) async {
        guard let tagRepo else { return }
        do {
            if assigned {
                try await tagRepo.assign(username: username, tagId: tagId)
            } else {
                try await tagRepo.unassign(username: username, tagId: tagId)
            }
            try await reloadLists()
        } catch {
            statusMessage = "タグ操作エラー: \(error.localizedDescription)"
        }
    }

    func deleteTag(_ tagId: Int64) async {
        guard let tagRepo else { return }
        do {
            try await tagRepo.delete(tagId: tagId)
            if tagFilter == .tag(tagId) {
                tagFilter = .all
            }
            try await reloadLists()
        } catch {
            statusMessage = "タグ削除エラー: \(error.localizedDescription)"
        }
    }

    // MARK: - 検索バケット (§9.3 / §9.4)

    /// 新規検索の保存。search.json を取得開始前に書き、サイドバーに即表示する。
    func createSearch(
        name: String?, baseQuery: String, minRetweets: Int64?, minFaves: Int64?,
        maxRequests: Int
    ) async throws {
        guard let db, let userRepo else { throw AppError("DB 未接続") }
        let query = SearchQueryOperators.compose(
            baseQuery: baseQuery, minRetweets: minRetweets, minFaves: minFaves)
        guard !query.isEmpty else { throw AppError("検索クエリを入力してください") }
        let slug = SearchSlug.from(query)
        let bucketId = SearchBuckets.bucketId(slug: slug)
        try SearchMetadata.write(
            bucketDir: db.userDir(bucketId), query: query,
            name: (name?.isEmpty == false) ? name : nil)
        try await userRepo.add(bucketId)
        try await reloadLists()
        select(.search(bucketId))
        startFetch(username: bucketId, mode: .search, maxRequests: maxRequests, searchQuery: query)
    }

    /// 検索の編集 (§9.4)。search.json を read-modify-write。
    func updateSearch(
        _ bucketId: String, name: String?, baseQuery: String,
        minRetweets: Int64?, minFaves: Int64?
    ) async throws {
        guard let db else { throw AppError("DB 未接続") }
        guard !isFetching else { throw AppError("取得実行中は編集できません") }
        let query = SearchQueryOperators.compose(
            baseQuery: baseQuery, minRetweets: minRetweets, minFaves: minFaves)
        try SearchMetadata.write(
            bucketDir: db.userDir(bucketId), query: query,
            name: (name?.isEmpty == false) ? name : nil)
        try await reloadLists()
    }

    // MARK: - 取得 (フェーズ⑤、§9)

    var fetcherConfigured: Bool {
        !settings.repoDir.isEmpty
            && FileManager.default.fileExists(atPath: settings.repoDir + "/main.py")
    }

    /// 取得系操作のガード (§1.3)。false なら案内を出して中止する。
    func ensureFetcherConfigured() -> Bool {
        if fetcherConfigured { return true }
        statusMessage = "取得機能を使うには fetcher の場所が必要です。設定から指定してください (既存データの閲覧はこのまま使えます)"
        return false
    }

    func startFetch(
        username: String, mode: FetchMode, maxRequests: Int?,
        searchQuery: String? = nil, backfillSince: String? = nil
    ) {
        guard ensureFetcherConfigured(), !isFetching, !isReadOnly else { return }
        isFetching = true
        fetchLogLines = []
        fetchTargetLabel = username
        let process = FetcherProcess(
            repoDir: settings.repoDir,
            pythonPath: settings.pythonPath.isEmpty ? nil : settings.pythonPath)
        fetcherProcess = process
        let dataDir = settings.effectiveDataDir
        statusMessage = "取得中: \(username)"
        Task {
            var result: FetchResult
            do {
                result = try await process.run(
                    username: username, mode: mode, dataDir: dataDir,
                    maxRequests: maxRequests, searchQuery: searchQuery,
                    backfillSince: backfillSince
                ) { [weak self] line in
                    Task { @MainActor in
                        self?.appendFetchLog(line)
                    }
                }
            } catch {
                appendFetchLog("エラー: \(error.localizedDescription)")
                result = FetchResult(exitCode: -1, cancelled: false)
            }
            await finishFetch(result: result, mode: mode, username: username)
        }
    }

    func cancelFetch() {
        fetcherProcess?.cancel()
    }

    private func appendFetchLog(_ line: String) {
        fetchLogLines.append(line)
        // ライブログは最大 2000 行 (§9.5)
        if fetchLogLines.count > 2000 {
            fetchLogLines.removeFirst(fetchLogLines.count - 2000)
        }
    }

    private func finishFetch(result: FetchResult, mode: FetchMode, username: String) async {
        let (message, hasIssues) = FetchOutcome.describeSingle(result, mode: mode)
        var display = message
        if let hint = FetchOutcome.environmentHint(fetchLogLines) {
            display += " — \(hint)"
        }
        statusMessage = "\(username): \(display)"
        // 取得完了後 (失敗・中断でも保存済みのため) 必ず差分取込 → 一覧再読込 (§9.1)
        if mode != .followings {
            _ = try? await importer?.importUser(username)
            try? await reloadLists()
            if currentUsernames.contains(username) {
                reloadPages()
            }
        }
        isFetching = false
        fetcherProcess = nil
        if hasIssues {
            // 失敗・中断・上限到達のときだけ完了時にログを自動表示 (§9.5)
            showFetchLogRequested = true
        }
        AppLog.info("取得完了 \(username): exit=\(result.exitCode) cancelled=\(result.cancelled)")
    }

    /// 表示中をすべて更新 (§4.6)。合計リクエスト上限を全体で管理する。
    func updateAllVisible(totalBudget: Int) {
        guard ensureFetcherConfigured(), !isFetching, !isReadOnly else { return }
        // 対象一覧は開始時にスナップショット
        let targets = (visibleUsers + visibleSearches).map {
            (username: $0.id, isSearch: $0.isSearch, query: $0.searchQuery)
        }
        guard !targets.isEmpty else { return }
        isFetching = true
        fetchLogLines = []
        fetchTargetLabel = "一括更新 (\(targets.count) 件)"
        let dataDir = settings.effectiveDataDir
        let process = FetcherProcess(
            repoDir: settings.repoDir,
            pythonPath: settings.pythonPath.isEmpty ? nil : settings.pythonPath)
        fetcherProcess = process

        Task {
            var remaining = totalBudget
            var succeeded = 0
            var consumedTotal = 0
            var failed: [String] = []
            var stopReason: String?
            for target in targets {
                if remaining <= 0 {
                    stopReason = "合計リクエスト上限に達しました"
                    break
                }
                let granted = remaining
                statusMessage = "取得中: \(target.username) (残り \(remaining) リクエスト)"
                let startLine = fetchLogLines.count
                var result: FetchResult
                do {
                    result = try await process.run(
                        username: target.username,
                        mode: target.isSearch ? .searchUpdate : .update,
                        dataDir: dataDir, maxRequests: granted,
                        searchQuery: target.query
                    ) { [weak self] line in
                        Task { @MainActor in
                            self?.appendFetchLog(line)
                        }
                    }
                } catch {
                    appendFetchLog("エラー: \(error.localizedDescription)")
                    result = FetchResult(exitCode: -1, cancelled: false)
                }
                let runLines = Array(fetchLogLines.dropFirst(min(startLine, fetchLogLines.count)))
                let consumed = FetchBudget.consumedOrGranted(
                    parsedConsumed: FetchBudget.parseConsumedRequests(runLines), granted: granted)
                consumedTotal += consumed
                remaining -= consumed
                if result.cancelled {
                    stopReason = "中断しました"
                    break
                }
                if result.exitCode == 0 || result.exitCode == FetchOutcome.budgetExhaustedExitCode {
                    succeeded += 1
                } else {
                    // 個別の失敗は飛ばして続行 (§4.6)
                    failed.append(target.username)
                }
                _ = try? await importer?.importUser(target.username)
            }
            let (summary, hasIssues) = FetchOutcome.describeBatch(
                succeeded: succeeded, total: targets.count,
                consumedTotal: consumedTotal, failed: failed, stopReason: stopReason)
            statusMessage = summary
            try? await reloadLists()
            reloadPages()
            isFetching = false
            fetcherProcess = nil
            if hasIssues {
                showFetchLogRequested = true
            }
        }
    }

    /// フォロー中を一括登録 (§4.7)。取得後の確認は View 側で行うため、
    /// ここでは fetch → ファイル読取 → 登録の 2 段階に分ける。
    func fetchFollowings(source: String, maxRequests: Int) async -> FetchResult? {
        guard ensureFetcherConfigured(), !isFetching, !isReadOnly else { return nil }
        isFetching = true
        fetchLogLines = []
        fetchTargetLabel = "フォロー一覧: @\(source)"
        let process = FetcherProcess(
            repoDir: settings.repoDir,
            pythonPath: settings.pythonPath.isEmpty ? nil : settings.pythonPath)
        fetcherProcess = process
        defer {
            isFetching = false
            fetcherProcess = nil
        }
        do {
            let result = try await process.run(
                username: source, mode: .followings,
                dataDir: settings.effectiveDataDir, maxRequests: maxRequests
            ) { [weak self] line in
                Task { @MainActor in
                    self?.appendFetchLog(line)
                }
            }
            return result
        } catch {
            appendFetchLog("エラー: \(error.localizedDescription)")
            showFetchLogRequested = true
            return nil
        }
    }

    /// _followings/<source>.jsonl から users へ一括登録 + タグ付与 (§4.7)。
    func registerFollowings(source: String, tagIds: [Int64]) async throws -> Int {
        guard let userRepo, let tagRepo else { throw AppError("DB 未接続") }
        let entries = FollowingsFile.read(
            dataDir: settings.effectiveDataDir, sourceUsername: source)
        guard !entries.isEmpty else { return 0 }
        let added = try await userRepo.addMany(
            entries.map { (username: $0.username, displayName: $0.displayName) })
        // 既存ユーザーにも同じタグを付ける (冪等)
        try await tagRepo.assignMany(usernames: entries.map(\.username), tagIds: tagIds)
        try await reloadLists()
        return added.count
    }

    // MARK: - 終了処理

    /// サイドバーの実測幅 (§2.2)。ドラッグのたびに再描画しないよう @Observable の
    /// 追跡対象にしない — 終了時に settings へ移すだけ
    @ObservationIgnored private var measuredSidebarWidth: Double?

    func noteSidebarWidth(_ width: Double) {
        guard width > 0 else { return }
        measuredSidebarWidth = width
    }

    /// 現在のウィンドウ矩形 (§2.2)。移動・リサイズのたびに来るので、
    /// サイドバー幅と同じく @Observable の追跡対象にしない
    @ObservationIgnored private var currentWindowFrame: CGRect?

    func noteWindowFrame(_ frame: CGRect) {
        guard frame.width > 0, frame.height > 0 else { return }
        currentWindowFrame = frame
    }

    func shutdown() async {
        if let width = measuredSidebarWidth {
            settings.sidebarWidth = WindowPlacement.clampSidebarWidth(width)
        }
        if let frame = currentWindowFrame {
            settings.windowFrame = frame
        }
        await readQueue?.shutdown()
        db?.checkpointAndClose()
        settings.save()
    }
}

struct AppError: LocalizedError {
    let message: String
    init(_ message: String) { self.message = message }
    var errorDescription: String? { message }
}
