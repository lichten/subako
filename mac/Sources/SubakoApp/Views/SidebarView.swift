import SwiftUI
import SubakoCore

/// 左ペイン: タグフィルタ + 統合トグル + ユーザー一覧 + 検索一覧 (§4)。
struct SidebarView: View {
    @Bindable var app: AppModel
    @Binding var sheet: SheetKind?

    var body: some View {
        VStack(spacing: 0) {
            header
            Divider()
            List {
                Section {
                    ForEach(app.visibleUsers) { item in
                        archiveRow(item)
                    }
                } header: {
                    HStack {
                        Text("ユーザー")
                        Spacer()
                        Button {
                            sheet = .addUser
                        } label: {
                            Image(systemName: "plus")
                        }
                        .buttonStyle(.borderless)
                        .help("ユーザーを追加")
                        .disabled(app.isReadOnly)
                    }
                }
                Section {
                    ForEach(app.visibleSearches) { item in
                        archiveRow(item)
                    }
                } header: {
                    HStack {
                        Text("検索")
                        Spacer()
                        Button {
                            sheet = .addSearch
                        } label: {
                            Image(systemName: "plus")
                        }
                        .buttonStyle(.borderless)
                        .help("キーワード検索を保存")
                        .disabled(app.isReadOnly || app.isFetching)
                    }
                }
            }
            .listStyle(.sidebar)
            .scrollContentBackground(.hidden)
        }
        .background(Theme.sidebarBackground)
    }

    // MARK: - 上部 (タグフィルタ・統合・一括操作)

    private var header: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 4) {
                Picker("タグ", selection: $app.tagFilter) {
                    Text("すべて").tag(TagFilter.all)
                    Text("(タグなし)").tag(TagFilter.untagged)
                    ForEach(app.tags, id: \.tagId) { tag in
                        Text(tag.name).tag(TagFilter.tag(tag.tagId))
                    }
                }
                .labelsHidden()
                Button {
                    app.tagFilter = .all
                } label: {
                    Image(systemName: "xmark.circle.fill")
                }
                .buttonStyle(.borderless)
                .disabled(app.tagFilter == .all)
                .help("タグフィルタを解除")
            }
            HStack(spacing: 8) {
                Toggle(isOn: Binding(
                    get: { app.selection == .all },
                    set: { on in
                        if on {
                            app.select(.all)
                        } else {
                            app.selectFirstVisible()
                        }
                    })) {
                    Text("すべて")
                }
                .toggleStyle(.button)
                .help("統合タイムライン (表示中のユーザーと検索をすべて混ぜる)")

                Button {
                    sheet = .updateAll
                } label: {
                    Image(systemName: "arrow.triangle.2.circlepath")
                }
                .help("表示中をすべて更新")
                .disabled(app.isFetching || app.isReadOnly)

                Button {
                    sheet = .importFollowings
                } label: {
                    Image(systemName: "arrow.down.to.line")
                }
                .help("フォロー中を一括登録")
                .disabled(app.isFetching || app.isReadOnly)

                Spacer()
            }
        }
        .padding(8)
    }

    // MARK: - 行

    @ViewBuilder
    private func archiveRow(_ item: ArchiveItem) -> some View {
        let selected = app.selection == (item.isSearch ? .search(item.id) : .user(item.id))
        HStack(spacing: 6) {
            if item.isSearch {
                Text("🔍").font(.system(size: 16))
                    .frame(width: 32, height: 32)
            } else {
                CachedIconView(url: item.row.iconUrl, size: 32, cache: app.iconCache)
            }
            VStack(alignment: .leading, spacing: 1) {
                Text(item.displayLabel)
                    .font(.system(size: 12, weight: .semibold))
                    .lineLimit(1)
                HStack(spacing: 4) {
                    Text(item.isSearch
                        ? "\(item.row.tweetCount)件"
                        : "@\(item.row.username) \(item.row.tweetCount)件")
                        .font(.system(size: 10))
                        .foregroundStyle(Theme.auxTextLight)
                        .lineLimit(1)
                }
                if !item.tagIds.isEmpty {
                    FlowLayout(spacing: 2) {
                        ForEach(item.tagIds, id: \.self) { tagId in
                            if let tag = app.tags.first(where: { $0.tagId == tagId }) {
                                TagChipView(name: tag.name)
                            }
                        }
                    }
                }
            }
            Spacer(minLength: 2)
            UnreadBadgeView(count: item.unread)
            Button("更新") {
                startUpdate(item)
            }
            .font(.system(size: 10))
            .buttonStyle(.bordered)
            .controlSize(.mini)
            .disabled(app.isFetching || app.isReadOnly)
        }
        .padding(.vertical, 2)
        .contentShape(Rectangle())
        .onTapGesture {
            app.select(item.isSearch ? .search(item.id) : .user(item.id))
        }
        .listRowBackground(selected ? Theme.accent.opacity(0.15) : nil)
        .contextMenu {
            contextMenu(item)
        }
        .help(item.isSearch ? (item.searchQuery ?? "") : item.displayLabel)
    }

    private func startUpdate(_ item: ArchiveItem) {
        app.startFetch(
            username: item.id,
            mode: item.isSearch ? .searchUpdate : .update,
            maxRequests: item.isSearch ? 500 : nil,
            searchQuery: item.searchQuery)
    }

    @ViewBuilder
    private func contextMenu(_ item: ArchiveItem) -> some View {
        Button(item.isSearch ? "更新 (差分取得)..." : "更新 (差分取得)") {
            startUpdate(item)
        }
        .disabled(app.isFetching || app.isReadOnly)
        Button(item.isSearch ? "過去期間を取得 (バックフィル)..." : "全期間を取得 (バックフィル)...") {
            sheet = item.isSearch ? .searchBackfill(item) : .backfill(item)
        }
        .disabled(app.isFetching || app.isReadOnly)
        Button("不足画像を取得 (API 不使用)") {
            app.startFetch(username: item.id, mode: .imagesOnly, maxRequests: nil)
        }
        .disabled(app.isFetching || app.isReadOnly)
        Divider()
        Menu("タグ") {
            // 開くたびに現在のタグ一覧から再構築。チェック式で連続付け外し可 (§4.4)
            ForEach(app.tags, id: \.tagId) { tag in
                Toggle(tag.name, isOn: Binding(
                    get: { item.tagIds.contains(tag.tagId) },
                    set: { on in
                        Task { await app.setTag(tag.tagId, on: item.id, assigned: on) }
                    }))
            }
            if !app.tags.isEmpty {
                Divider()
            }
            Button("新しいタグ...") {
                sheet = .addTag(item)
            }
            Button("タグの整理...") {
                sheet = .manageTags
            }
        }
        .disabled(app.isReadOnly)
        if item.isSearch {
            Button("編集 (名称・クエリ)...") {
                sheet = .searchEdit(item)
            }
            .disabled(app.isFetching || app.isReadOnly)
        }
        Divider()
        Button("削除...") {
            sheet = .deleteArchive(item)
        }
        .disabled(app.isFetching || app.isReadOnly)
    }
}

/// タグチップ等の折り返しレイアウト。
struct FlowLayout: Layout {
    var spacing: CGFloat = 4

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let width = proposal.width ?? 300
        var x: CGFloat = 0
        var y: CGFloat = 0
        var rowHeight: CGFloat = 0
        for subview in subviews {
            let size = subview.sizeThatFits(.unspecified)
            if x > 0, x + size.width > width {
                x = 0
                y += rowHeight + spacing
                rowHeight = 0
            }
            x += size.width + spacing
            rowHeight = max(rowHeight, size.height)
        }
        return CGSize(width: width, height: y + rowHeight)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var x = bounds.minX
        var y = bounds.minY
        var rowHeight: CGFloat = 0
        for subview in subviews {
            let size = subview.sizeThatFits(.unspecified)
            if x > bounds.minX, x + size.width > bounds.maxX {
                x = bounds.minX
                y += rowHeight + spacing
                rowHeight = 0
            }
            subview.place(at: CGPoint(x: x, y: y), proposal: ProposedViewSize(size))
            x += size.width + spacing
            rowHeight = max(rowHeight, size.height)
        }
    }
}
