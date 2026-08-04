import SwiftUI
import SubakoCore

enum SheetKind: Identifiable {
    case settings
    case firstRun
    case addUser
    case addSearch
    case trending
    case searchEdit(ArchiveItem)
    case addTag(ArchiveItem)
    case manageTags
    case deleteArchive(ArchiveItem)
    case backfill(ArchiveItem)
    case searchUpdate(ArchiveItem)
    case searchBackfill(ArchiveItem)
    case updateAll
    case importFollowings

    var id: String {
        switch self {
        case .settings: return "settings"
        case .firstRun: return "firstRun"
        case .addUser: return "addUser"
        case .addSearch: return "addSearch"
        case .trending: return "trending"
        case .searchEdit(let i): return "searchEdit:\(i.id)"
        case .addTag(let i): return "addTag:\(i.id)"
        case .manageTags: return "manageTags"
        case .deleteArchive(let i): return "deleteArchive:\(i.id)"
        case .backfill(let i): return "backfill:\(i.id)"
        case .searchUpdate(let i): return "searchUpdate:\(i.id)"
        case .searchBackfill(let i): return "searchBackfill:\(i.id)"
        case .updateAll: return "updateAll"
        case .importFollowings: return "importFollowings"
        }
    }
}

/// メインウィンドウ: 3 ペイン構成 (§3)。
struct MainWindow: View {
    @Bindable var app: AppModel
    @State private var sheet: SheetKind?
    @State private var mediaViewer = MediaViewerController()
    @State private var fetchLogWindow = FetchLogWindowController()
    /// サイドバー幅 (§2.2 で永続化)。前回終了時の値で開く
    @State private var sidebarWidth: Double

    init(app: AppModel) {
        self.app = app
        _sidebarWidth = State(
            initialValue: WindowPlacement.clampSidebarWidth(app.settings.sidebarWidth))
    }

    var body: some View {
        VStack(spacing: 0) {
            // HSplitView は使わない。ウィンドウをリサイズするとサイドバーまで比例して
            // 広がってしまい、Windows 版 (幅は固定、本文側が伸縮) と挙動が変わるため
            HStack(spacing: 0) {
                SidebarView(app: app, sheet: $sheet)
                    .frame(width: sidebarWidth)
                SidebarSplitter(width: $sidebarWidth) {
                    app.noteSidebarWidth(sidebarWidth)
                }
                VStack(spacing: 0) {
                    toolbar
                    Divider()
                    content
                }
                .frame(minWidth: 480, maxWidth: .infinity)
            }
            Divider()
            statusBar
        }
        .frame(minWidth: 800, minHeight: 500)
        // 前回終了時のウィンドウ配置を復元する (§2.2)
        .background(WindowFrameKeeper(saved: app.settings.windowFrame) { frame in
            app.noteWindowFrame(frame)
        })
        .sheet(item: $sheet) { kind in
            sheetView(kind)
        }
        .onChange(of: app.needsFirstRun) {
            if app.needsFirstRun {
                sheet = .firstRun
            }
        }
        .onChange(of: app.showFetchLogRequested) {
            if app.showFetchLogRequested {
                app.showFetchLogRequested = false
                fetchLogWindow.show(app: app)
            }
        }
        .task {
            await app.startup()
            if app.needsFirstRun {
                sheet = .firstRun
            }
        }
        .alert("viewer.db を開けません", isPresented: Binding(
            get: { app.openError != nil },
            set: { if !$0 { app.openError = nil } })) {
            Button("設定を開く") { sheet = .settings }
            Button("OK", role: .cancel) {}
        } message: {
            Text(app.openError ?? "")
        }
    }

    // MARK: - ツールバー (§3)

    private var toolbar: some View {
        HStack(spacing: 10) {
            Picker("", selection: $app.viewMode) {
                Text("タイムライン").tag(ViewMode.timeline)
                Text("メディア").tag(ViewMode.media)
            }
            .pickerStyle(.segmented)
            .frame(width: 180)

            Toggle("未読のみ", isOn: Binding(
                get: { app.unreadOnly },
                set: { app.setUnreadOnly($0) }))
                .toggleStyle(.checkbox)
                .disabled(app.viewMode == .media)   // メディアビューでは無効 (§6.1)

            Toggle("古い順", isOn: Binding(
                get: { app.ascending },
                set: { app.setAscending($0) }))
                .toggleStyle(.checkbox)

            dateFilterControls

            Spacer()

            Button("インデックス再構築") {
                Task { await app.rebuildIndex() }
            }
            .disabled(!isUserSelected || app.isFetching || app.isReadOnly)

            Button {
                sheet = .settings
            } label: {
                Image(systemName: "gearshape")
            }
            .help("設定")
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 6)
    }

    private var isUserSelected: Bool {
        if case .user = app.selection { return true }
        return false
    }

    /// 期間フィルタ (§7.3): 年/月/日 + ◀ ▶ ×。
    private var dateFilterControls: some View {
        HStack(spacing: 4) {
            Picker("年", selection: Binding(
                get: { app.dateFilter.year },
                set: { app.setDateFilter(year: $0, month: nil, day: nil) })) {
                Text("(すべて)").tag(Int?.none)
                ForEach(app.dateFilter.years, id: \.self) { year in
                    Text(String(year)).tag(Int?.some(year))
                }
            }
            .labelsHidden()
            .frame(width: 90)

            Picker("月", selection: Binding(
                get: { app.dateFilter.month },
                set: { app.setDateFilter(year: app.dateFilter.year, month: $0, day: nil) })) {
                Text("-").tag(Int?.none)
                ForEach(1...12, id: \.self) { m in
                    Text("\(m)月").tag(Int?.some(m))
                }
            }
            .labelsHidden()
            .frame(width: 70)
            .disabled(app.dateFilter.year == nil)

            Picker("日", selection: Binding(
                get: { app.dateFilter.day },
                set: { app.setDateFilter(
                    year: app.dateFilter.year, month: app.dateFilter.month, day: $0) })) {
                Text("-").tag(Int?.none)
                if let year = app.dateFilter.year, let month = app.dateFilter.month {
                    ForEach(1...DateRangeFilter.daysIn(
                        year: year, month: month, timeZone: .current), id: \.self) { d in
                        Text("\(d)日").tag(Int?.some(d))
                    }
                }
            }
            .labelsHidden()
            .frame(width: 70)
            .disabled(app.dateFilter.month == nil)

            Button {
                app.stepDateFilter(-1)
            } label: {
                Image(systemName: "chevron.backward")
            }
            .disabled(!app.canStepDateFilter(-1))

            Button {
                app.stepDateFilter(1)
            } label: {
                Image(systemName: "chevron.forward")
            }
            .disabled(!app.canStepDateFilter(1))

            Button {
                app.setDateFilter(year: nil, month: nil, day: nil)
            } label: {
                Image(systemName: "xmark")
            }
            .disabled(!app.dateFilter.isActive)
        }
    }

    // MARK: - コンテンツ

    @ViewBuilder
    private var content: some View {
        switch app.viewMode {
        case .timeline:
            TimelineView(app: app) { item, image in
                openViewerFromTimeline(item: item, image: image)
            }
        case .media:
            MediaGridView(app: app) { item in
                openViewerFromGrid(item)
            }
        }
    }

    private func openViewerFromTimeline(item: TweetItem, image: ResolvedImage) {
        // カード上の画像クリック: そのツイートの画像列を対象に開く
        let all = item.bodyImages + item.quotedImages
        let entries = all.map {
            MediaViewerEntry(
                localPath: $0.localPath, text: item.displayText,
                dateText: item.localDateText, statusURL: item.statusURL)
        }
        let index = all.firstIndex(of: image) ?? 0
        mediaViewer.show(entries: entries, index: index)
    }

    private func openViewerFromGrid(_ item: MediaItem) {
        // 移動範囲は開いた時点のロード済み一覧 (§6.2)
        let entries = app.mediaItems.map { m in
            MediaViewerEntry(
                localPath: m.localPath,
                text: m.media.fullText,
                dateText: TweetItem.displayDate(fromUtc: m.media.createdAtUtc),
                statusURL: TweetURL.status(
                    author: SearchBuckets.isBucketId(m.media.username) ? nil : m.media.username,
                    tweetId: m.media.tweetId))
        }
        let index = app.mediaItems.firstIndex(of: item) ?? 0
        mediaViewer.show(entries: entries, index: index)
    }

    // MARK: - ステータスバー (§3)

    private var statusBar: some View {
        HStack(spacing: 8) {
            if app.isFetching {
                ProgressView()
                    .controlSize(.small)
                Button("取得中 — ログを表示") {
                    fetchLogWindow.show(app: app)
                }
                .buttonStyle(.link)
                Button("中断") {
                    app.cancelFetch()
                }
                .controlSize(.small)
            }
            if app.isReadOnly, app.db != nil {
                Text("閲覧専用モード")
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundStyle(.orange)
            }
            Spacer()
            Text(app.statusMessage)
                .font(.system(size: 11))
                .foregroundStyle(Theme.auxText)
                .lineLimit(1)
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 4)
    }

    // MARK: - シート

    @ViewBuilder
    private func sheetView(_ kind: SheetKind) -> some View {
        switch kind {
        case .settings:
            SettingsSheet(app: app)
        case .firstRun:
            FirstRunSheet(app: app)
        case .addUser:
            AddUserSheet(app: app)
        case .addSearch:
            SearchSheet(app: app)
        case .trending:
            TrendingSheet(app: app)
        case .searchEdit(let item):
            SearchEditSheet(app: app, item: item)
        case .addTag(let item):
            AddTagSheet(app: app, target: item)
        case .manageTags:
            ManageTagsSheet(app: app)
        case .deleteArchive(let item):
            DeleteArchiveSheet(app: app, item: item)
        case .backfill(let item):
            BackfillSheet(app: app, item: item)
        case .searchUpdate(let item):
            SearchUpdateSheet(app: app, item: item)
        case .searchBackfill(let item):
            SearchBackfillSheet(app: app, item: item)
        case .updateAll:
            UpdateAllSheet(app: app)
        case .importFollowings:
            ImportFollowingsSheet(app: app)
        }
    }
}
