import SwiftUI
import SubakoCore

// すべてのダイアログは内容依存サイズ (固定サイズ禁止 — docs/mac-port-notes.md §5)。
// ダイアログ群を 1 ファイルに集約しているため行数超過を許容 (.swiftlint.yml 参照)。
// swiftlint:disable file_length

/// 初回セットアップ (§1.1): データフォルダ 1 項目のみ。
struct FirstRunSheet: View {
    @Bindable var app: AppModel
    @Environment(\.dismiss) private var dismiss
    @State private var dataDir = AppSettings.defaultDataDir

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Subako へようこそ").font(.headline)
            Text("ツイートアーカイブを保存するデータフォルダを選んでください。\n他の PC で取得済みのデータフォルダ (クラウド同期フォルダ等) を選べば、そのまま閲覧できます。")
                .font(.system(size: 12))
                .foregroundStyle(Theme.auxText)
            HStack {
                TextField("データフォルダ", text: $dataDir)
                    .frame(minWidth: 320)
                Button("選択...") {
                    pickFolder { dataDir = $0 }
                }
            }
            HStack {
                Spacer()
                Button("終了") {
                    NSApp.terminate(nil)
                }
                Button("開始") {
                    app.settings.dataDir = dataDir
                    app.settings.save()
                    try? FileManager.default.createDirectory(
                        atPath: dataDir, withIntermediateDirectories: true)
                    dismiss()
                    Task { await app.openAndInitialize() }
                }
                .keyboardShortcut(.defaultAction)
                .disabled(dataDir.isEmpty)
            }
        }
        .padding(20)
    }
}

/// 設定 (§2.1 の 3 項目 + Mac 版独自の閲覧専用モード)。
struct SettingsSheet: View {
    @Bindable var app: AppModel
    @Environment(\.dismiss) private var dismiss
    @State private var dataDir = ""
    @State private var repoDir = ""
    @State private var pythonPath = ""
    @State private var readOnlyMode = false
    @State private var error: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("設定").font(.headline)
            Grid(alignment: .leading, verticalSpacing: 8) {
                GridRow {
                    Text("データフォルダ")
                    HStack {
                        TextField("", text: $dataDir).frame(minWidth: 300)
                        Button("選択...") { pickFolder { dataDir = $0 } }
                    }
                }
                GridRow {
                    Text("")
                    Text("クラウド同期フォルダを指定すると他の PC とデータを共有できます")
                        .font(.system(size: 11)).foregroundStyle(Theme.auxTextLight)
                }
                GridRow {
                    Text("fetcher の場所")
                    HStack {
                        TextField("main.py のあるフォルダ (空欄 = 取得機能を使わない)", text: $repoDir)
                            .frame(minWidth: 300)
                        Button("選択...") { pickFolder { repoDir = $0 } }
                    }
                }
                GridRow {
                    Text("Python パス")
                    TextField("空欄なら PATH の python3 を使う", text: $pythonPath)
                        .frame(minWidth: 300)
                }
                GridRow {
                    Text("閲覧専用モード")
                    VStack(alignment: .leading, spacing: 2) {
                        Toggle("既読・タグなどを書き込まない (mode=ro)", isOn: $readOnlyMode)
                        Text("Windows 側と同時に使うときの既読ロストを防げます。変更は再起動後に有効")
                            .font(.system(size: 11)).foregroundStyle(Theme.auxTextLight)
                    }
                }
            }
            if let error {
                Text(error).foregroundStyle(.red).font(.system(size: 12))
            }
            HStack {
                Spacer()
                Button("キャンセル") { dismiss() }
                Button("保存") { save() }
                    .keyboardShortcut(.defaultAction)
            }
        }
        .padding(20)
        .onAppear {
            dataDir = app.settings.dataDir
            repoDir = app.settings.repoDir
            pythonPath = app.settings.pythonPath
            readOnlyMode = app.settings.readOnlyMode
        }
    }

    private func save() {
        if !dataDir.isEmpty, !FileManager.default.fileExists(atPath: dataDir) {
            error = "データフォルダが存在しません: \(dataDir)"
            return
        }
        if !repoDir.isEmpty, !FileManager.default.fileExists(atPath: repoDir + "/main.py") {
            error = "指定フォルダに main.py が見つかりません: \(repoDir)"
            return
        }
        let dataDirChanged = dataDir != app.settings.dataDir
        let readOnlyChanged = readOnlyMode != app.settings.readOnlyMode
        app.settings.dataDir = dataDir
        app.settings.repoDir = repoDir
        app.settings.pythonPath = pythonPath
        app.settings.readOnlyMode = readOnlyMode
        app.settings.save()
        dismiss()
        if dataDirChanged || readOnlyChanged {
            Task { await app.openAndInitialize() }
        }
    }
}

@MainActor
func pickFolder(_ completion: @escaping (String) -> Void) {
    let panel = NSOpenPanel()
    panel.canChooseDirectories = true
    panel.canChooseFiles = false
    panel.canCreateDirectories = true
    if panel.runModal() == .OK, let url = panel.url {
        completion(url.path)
    }
}

/// ユーザー追加 (§9.2)。英数字と _ のみ。
struct AddUserSheet: View {
    @Bindable var app: AppModel
    @Environment(\.dismiss) private var dismiss
    @State private var name = ""
    @State private var error: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("ユーザーを追加").font(.headline)
            TextField("ユーザー名 (@ なし)", text: $name)
                .frame(minWidth: 260)
            if let error {
                Text(error).foregroundStyle(.red).font(.system(size: 12))
            }
            HStack {
                Spacer()
                Button("キャンセル") { dismiss() }
                Button("追加") {
                    Task {
                        do {
                            let added = try await app.addUser(name)
                            dismiss()
                            if added, app.fetcherConfigured {
                                // 新規登録かつツイート 0 件 → 取得を提案 (§9.2)
                                let normalized = UsernameRules.normalize(name)
                                confirmInitialFetch(normalized)
                            }
                        } catch {
                            self.error = error.localizedDescription
                        }
                    }
                }
                .keyboardShortcut(.defaultAction)
                .disabled(name.isEmpty)
            }
        }
        .padding(20)
    }

    private func confirmInitialFetch(_ username: String) {
        let alert = NSAlert()
        alert.messageText = "@\(username) を追加しました"
        alert.informativeText = "今すぐツイートを取得しますか?"
        alert.addButton(withTitle: "取得する")
        alert.addButton(withTitle: "あとで")
        if alert.runModal() == .alertFirstButtonReturn {
            app.startFetch(username: username, mode: .update, maxRequests: nil)
        }
    }
}

/// 新規検索の保存 (§9.3)。
struct SearchSheet: View {
    @Bindable var app: AppModel
    @Environment(\.dismiss) private var dismiss
    @State private var name = ""
    @State private var query = ""
    @State private var minRetweets = ""
    @State private var minFaves = ""
    @State private var maxRequests = "50"
    @State private var error: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("キーワード検索を保存").font(.headline)
            Grid(alignment: .leading, verticalSpacing: 8) {
                GridRow {
                    Text("名称")
                    TextField("(任意。空欄ならクエリを表示)", text: $name).frame(minWidth: 340)
                }
                GridRow {
                    Text("検索クエリ")
                    TextField("例: (sts2 OR \"slay the spire 2\") lang:ja", text: $query)
                        .frame(minWidth: 340)
                }
                GridRow {
                    Text("")
                    Text("スペース区切り = AND / OR (大文字) / \"...\" = 完全一致 / -語 = 除外 / lang:ja / from:user / since: until:")
                        .font(.system(size: 11)).foregroundStyle(Theme.auxTextLight)
                }
                GridRow {
                    Text("RT数 ≥")
                    TextField("(空欄 = 制限なし)", text: $minRetweets).frame(width: 120)
                }
                GridRow {
                    Text("いいね数 ≥")
                    TextField("(空欄 = 制限なし)", text: $minFaves).frame(width: 120)
                }
                GridRow {
                    Text("最大リクエスト数")
                    TextField("", text: $maxRequests).frame(width: 120)
                }
            }
            Text("上限に達しても安全に中断され、取得済み分は保存されます。再実行で続きから再開します")
                .font(.system(size: 11)).foregroundStyle(Theme.auxTextLight)
            if let error {
                Text(error).foregroundStyle(.red).font(.system(size: 12))
            }
            HStack {
                Spacer()
                Button("キャンセル") { dismiss() }
                Button("保存して取得") {
                    guard let limit = Int(maxRequests), limit > 0 else {
                        error = "最大リクエスト数は正の整数で入力してください"
                        return
                    }
                    guard app.ensureFetcherConfigured() else {
                        dismiss()
                        return
                    }
                    Task {
                        do {
                            try await app.createSearch(
                                name: name, baseQuery: query,
                                minRetweets: Int64(minRetweets), minFaves: Int64(minFaves),
                                maxRequests: limit)
                            dismiss()
                        } catch {
                            self.error = error.localizedDescription
                        }
                    }
                }
                .keyboardShortcut(.defaultAction)
                .disabled(query.isEmpty)
            }
        }
        .padding(20)
    }
}

/// 検索の編集 (§9.4)。クエリを基本クエリ / RT数下限 / いいね数下限に分解して表示。
struct SearchEditSheet: View {
    @Bindable var app: AppModel
    let item: ArchiveItem
    @Environment(\.dismiss) private var dismiss
    @State private var name = ""
    @State private var query = ""
    @State private var minRetweets = ""
    @State private var minFaves = ""
    @State private var error: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("検索の編集").font(.headline)
            Grid(alignment: .leading, verticalSpacing: 8) {
                GridRow {
                    Text("名称")
                    TextField("(任意)", text: $name).frame(minWidth: 340)
                }
                GridRow {
                    Text("検索クエリ")
                    TextField("", text: $query).frame(minWidth: 340)
                }
                GridRow {
                    Text("RT数 ≥")
                    TextField("", text: $minRetweets).frame(width: 120)
                }
                GridRow {
                    Text("いいね数 ≥")
                    TextField("", text: $minFaves).frame(width: 120)
                }
            }
            Text("取得済みツイートは残ります。クエリを変更した場合、取得進捗 (カーソル・バックフィル済み期間) は次回実行時に自動リセットされます")
                .font(.system(size: 11)).foregroundStyle(Theme.auxTextLight)
            if let error {
                Text(error).foregroundStyle(.red).font(.system(size: 12))
            }
            HStack {
                Spacer()
                Button("キャンセル") { dismiss() }
                Button("保存") {
                    Task {
                        do {
                            try await app.updateSearch(
                                item.id, name: name, baseQuery: query,
                                minRetweets: Int64(minRetweets), minFaves: Int64(minFaves))
                            dismiss()
                        } catch {
                            self.error = error.localizedDescription
                        }
                    }
                }
                .keyboardShortcut(.defaultAction)
                .disabled(query.isEmpty)
            }
        }
        .padding(20)
        .onAppear {
            name = item.searchLabel == item.searchQuery ? "" : (item.searchLabel ?? "")
            let (base, rt, fav) = SearchQueryOperators.split(item.searchQuery ?? "")
            query = base
            minRetweets = rt.map(String.init) ?? ""
            minFaves = fav.map(String.init) ?? ""
        }
    }
}

/// 新しいタグ (§4.4)。作成して即付与。
struct AddTagSheet: View {
    @Bindable var app: AppModel
    let target: ArchiveItem
    @Environment(\.dismiss) private var dismiss
    @State private var name = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("新しいタグ").font(.headline)
            TextField("タグ名", text: $name).frame(minWidth: 240)
            HStack {
                Spacer()
                Button("キャンセル") { dismiss() }
                Button("作成して付与") {
                    Task {
                        if let tagId = try? await app.createTag(name: name) {
                            await app.setTag(tagId, on: target.id, assigned: true)
                        }
                        dismiss()
                    }
                }
                .keyboardShortcut(.defaultAction)
                .disabled(name.isEmpty)
            }
        }
        .padding(20)
    }
}

/// タグの整理 (§4.4)。タグ名 + 付与人数の一覧から削除。
struct ManageTagsSheet: View {
    @Bindable var app: AppModel
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("タグの整理").font(.headline)
            if app.tags.isEmpty {
                Text("タグはありません").foregroundStyle(Theme.auxTextLight)
            }
            ForEach(app.tags, id: \.tagId) { tag in
                HStack {
                    Text(tag.name)
                    Text("(\(tag.userCount) 件に付与)")
                        .font(.system(size: 11)).foregroundStyle(Theme.auxTextLight)
                    Spacer()
                    Button("削除") {
                        confirmDelete(tag)
                    }
                    .controlSize(.small)
                }
                .frame(minWidth: 300)
            }
            HStack {
                Spacer()
                Button("閉じる") { dismiss() }
                    .keyboardShortcut(.defaultAction)
            }
        }
        .padding(20)
    }

    private func confirmDelete(_ tag: TagRow) {
        if tag.userCount > 0 {
            let alert = NSAlert()
            alert.messageText = "タグ「\(tag.name)」を削除しますか?"
            alert.informativeText = "\(tag.userCount) 件のユーザー/検索から外れます。"
            alert.addButton(withTitle: "削除")
            alert.addButton(withTitle: "キャンセル")
            guard alert.runModal() == .alertFirstButtonReturn else { return }
        }
        Task { await app.deleteTag(tag.tagId) }
    }
}

/// アーカイブ削除 (§10.1)。既定ボタンはキャンセル (誤削除防止)。
struct DeleteArchiveSheet: View {
    @Bindable var app: AppModel
    let item: ArchiveItem
    @Environment(\.dismiss) private var dismiss
    @State private var deleteData = false
    @State private var error: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(item.isSearch
                ? "検索「\(item.displayLabel)」を削除しますか?"
                : "@\(item.row.username) を削除しますか?")
                .font(.headline)
            Text("保存済みのツイート \(item.row.tweetCount)件のインデックスと既読以外の付随情報が削除されます。")
                .font(.system(size: 12))
            Toggle("ツイートデータと画像も完全に削除する", isOn: $deleteData)
            Text(deleteData
                ? "復元できません。同じ内容を揃えるには API リクエストを消費して取り直しになります。"
                : "フォルダは data/_trash/ へ移動します。戻せば次回起動時に再登録され、既読状態も復元されます。")
                .font(.system(size: 11))
                .foregroundStyle(deleteData ? .red : Theme.auxTextLight)
            if let error {
                Text(error).foregroundStyle(.red).font(.system(size: 12))
            }
            HStack {
                Spacer()
                Button("キャンセル") { dismiss() }
                    .keyboardShortcut(.defaultAction)   // 既定はキャンセル
                Button("削除") {
                    Task {
                        do {
                            try await app.deleteArchive(item.id, deleteData: deleteData)
                            dismiss()
                        } catch {
                            self.error = error.localizedDescription
                        }
                    }
                }
            }
        }
        .padding(20)
    }
}

/// 数値上限入力の共通ダイアログ (§9.6)。
private struct LimitField: View {
    let label: String
    @Binding var text: String

    var body: some View {
        Grid(alignment: .leading) {
            GridRow {
                Text(label)
                TextField("", text: $text).frame(width: 120)
            }
        }
    }
}

/// バックフィル (ユーザー): 最大リクエスト数 (既定 500)。
struct BackfillSheet: View {
    @Bindable var app: AppModel
    let item: ArchiveItem
    @Environment(\.dismiss) private var dismiss
    @State private var maxRequests = "500"

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("@\(item.row.username) の全期間を取得 (バックフィル)").font(.headline)
            LimitField(label: "最大リクエスト数", text: $maxRequests)
            Text("上限に達しても安全に中断され、取得済み分は保存されます。再実行で続きから再開します")
                .font(.system(size: 11)).foregroundStyle(Theme.auxTextLight)
            HStack {
                Spacer()
                Button("キャンセル") { dismiss() }
                Button("開始") {
                    guard let limit = Int(maxRequests), limit > 0 else { return }
                    dismiss()
                    app.startFetch(username: item.id, mode: .backfill, maxRequests: limit)
                }
                .keyboardShortcut(.defaultAction)
                .disabled(Int(maxRequests).map { $0 <= 0 } ?? true)
            }
        }
        .padding(20)
    }
}

/// 検索の差分更新: 最大リクエスト数 (既定 500)。バックフィルと同ダイアログの
/// タイトル違い (§9.6)。ユーザー TL の更新と違い、検索は放置期間が長いと新着が
/// 大量になり得るため上限入力を必須にする (Windows 版 MenuSearchUpdate_Click と同じ)。
struct SearchUpdateSheet: View {
    @Bindable var app: AppModel
    let item: ArchiveItem
    @Environment(\.dismiss) private var dismiss
    @State private var maxRequests = "500"

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("「\(item.displayLabel)」を更新 (差分取得)").font(.headline)
            LimitField(label: "最大リクエスト数", text: $maxRequests)
            Text("上限に達しても安全に中断され、取得済み分は保存されます。再実行で続きから再開します")
                .font(.system(size: 11)).foregroundStyle(Theme.auxTextLight)
            HStack {
                Spacer()
                Button("キャンセル") { dismiss() }
                Button("開始") {
                    guard let limit = Int(maxRequests), limit > 0 else { return }
                    dismiss()
                    app.startFetch(
                        username: item.id, mode: .searchUpdate,
                        maxRequests: limit, searchQuery: item.searchQuery)
                }
                .keyboardShortcut(.defaultAction)
                .disabled(Int(maxRequests).map { $0 <= 0 } ?? true)
            }
        }
        .padding(20)
    }
}

/// 検索バックフィル: 遡る開始日 + 最大リクエスト数 (既定 2014-01-01 / 500)。
struct SearchBackfillSheet: View {
    @Bindable var app: AppModel
    let item: ArchiveItem
    @Environment(\.dismiss) private var dismiss
    @State private var since = "2014-01-01"
    @State private var maxRequests = "500"

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("「\(item.displayLabel)」の過去期間を取得").font(.headline)
            Grid(alignment: .leading, verticalSpacing: 8) {
                GridRow {
                    Text("遡る開始日")
                    TextField("YYYY-MM-DD", text: $since).frame(width: 140)
                }
                GridRow {
                    Text("最大リクエスト数")
                    TextField("", text: $maxRequests).frame(width: 120)
                }
            }
            Text("検索 API は 2014 年より前をほぼ返しません。上限到達時は再実行で続きから再開します")
                .font(.system(size: 11)).foregroundStyle(Theme.auxTextLight)
            HStack {
                Spacer()
                Button("キャンセル") { dismiss() }
                Button("開始") {
                    guard let limit = Int(maxRequests), limit > 0 else { return }
                    dismiss()
                    app.startFetch(
                        username: item.id, mode: .searchBackfill, maxRequests: limit,
                        searchQuery: item.searchQuery, backfillSince: since)
                }
                .keyboardShortcut(.defaultAction)
                .disabled(Int(maxRequests).map { $0 <= 0 } ?? true)
            }
        }
        .padding(20)
    }
}

/// 表示中をすべて更新 (§4.6): 合計リクエスト上限 (既定 100)。
struct UpdateAllSheet: View {
    @Bindable var app: AppModel
    @Environment(\.dismiss) private var dismiss
    @State private var budget = "100"

    var body: some View {
        let count = app.visibleUsers.count + app.visibleSearches.count
        VStack(alignment: .leading, spacing: 12) {
            Text("表示中をすべて更新").font(.headline)
            Text("対象: \(count) 件 (ユーザー \(app.visibleUsers.count) / 検索 \(app.visibleSearches.count))")
                .font(.system(size: 12))
            LimitField(label: "合計リクエスト上限", text: $budget)
            Text("上限は全体の合計です。達した時点で残りをスキップして停止します")
                .font(.system(size: 11)).foregroundStyle(Theme.auxTextLight)
            HStack {
                Spacer()
                Button("キャンセル") { dismiss() }
                Button("開始") {
                    guard let limit = Int(budget), limit > 0 else { return }
                    dismiss()
                    app.updateAllVisible(totalBudget: limit)
                }
                .keyboardShortcut(.defaultAction)
                .disabled(count == 0 || (Int(budget).map { $0 <= 0 } ?? true))
            }
        }
        .padding(20)
    }
}

/// フォロー中を一括登録 (§4.7)。タグ 0 件では実行できない。
struct ImportFollowingsSheet: View {
    @Bindable var app: AppModel
    @Environment(\.dismiss) private var dismiss
    @State private var source = ""
    @State private var selectedTags: Set<Int64> = []
    @State private var newTags = ""
    @State private var maxRequests = "50"
    @State private var error: String?
    @State private var running = false

    private var parsedNewTags: [String] {
        newTags
            .split(whereSeparator: { ",、，\n".contains($0) })
            .map { $0.trimmingCharacters(in: .whitespaces) }
            .filter { !$0.isEmpty }
    }

    private var hasTags: Bool { !selectedTags.isEmpty || !parsedNewTags.isEmpty }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("フォロー中を一括登録").font(.headline)
            Grid(alignment: .leading, verticalSpacing: 8) {
                GridRow {
                    Text("フォロー元アカウント")
                    TextField("ユーザー名 (@ なし)", text: $source).frame(minWidth: 240)
                }
                GridRow {
                    Text("付けるタグ")
                    FlowLayout(spacing: 4) {
                        ForEach(app.tags, id: \.tagId) { tag in
                            Toggle(tag.name, isOn: Binding(
                                get: { selectedTags.contains(tag.tagId) },
                                set: { on in
                                    if on { selectedTags.insert(tag.tagId) } else { selectedTags.remove(tag.tagId) }
                                }))
                            .toggleStyle(.button)
                            .controlSize(.small)
                        }
                    }
                }
                GridRow {
                    Text("新しいタグ")
                    TextField("カンマ・読点・改行区切りで複数", text: $newTags).frame(minWidth: 240)
                }
                GridRow {
                    Text("最大リクエスト数")
                    TextField("", text: $maxRequests).frame(width: 120)
                }
            }
            Text("タグを 1 つ以上指定してください (後からこの集合を選び直せるように)。登録はユーザー追加のみで、ツイートは取得しません")
                .font(.system(size: 11)).foregroundStyle(Theme.auxTextLight)
            if let error {
                Text(error).foregroundStyle(.red).font(.system(size: 12))
            }
            HStack {
                Spacer()
                Button("キャンセル") { dismiss() }
                if running {
                    ProgressView().controlSize(.small)
                }
                Button("実行") { run() }
                    .keyboardShortcut(.defaultAction)
                    .disabled(source.isEmpty || !hasTags || running)
            }
        }
        .padding(20)
    }

    private func run() {
        let name = UsernameRules.normalize(source)
        guard UsernameRules.isValid(name) else {
            error = "ユーザー名は英数字と _ のみ使用できます"
            return
        }
        guard let limit = Int(maxRequests), limit > 0 else {
            error = "最大リクエスト数は正の整数で入力してください"
            return
        }
        running = true
        Task {
            defer { running = false }
            // 既存の _followings ファイルがあれば API を使わない再利用を提案 (§4.7)
            let existing = FollowingsFile.count(
                dataDir: app.settings.effectiveDataDir, sourceUsername: name)
            var needFetch = true
            if existing > 0 {
                let alert = NSAlert()
                alert.messageText = "取得済みのフォロー一覧があります (\(existing) 件)"
                alert.informativeText = "API を使わずにこれを登録しますか?"
                alert.addButton(withTitle: "これを登録")
                alert.addButton(withTitle: "取り直す")
                needFetch = alert.runModal() != .alertFirstButtonReturn
            }
            if needFetch {
                guard app.ensureFetcherConfigured() else {
                    dismiss()
                    return
                }
                guard let result = await app.fetchFollowings(source: name, maxRequests: limit)
                else {
                    error = "取得に失敗しました (ログを確認してください)"
                    return
                }
                if result.exitCode != 0
                    && result.exitCode != FetchOutcome.budgetExhaustedExitCode {
                    error = FetchOutcome.describeSingle(result, mode: .followings).message
                    return
                }
            }
            let count = FollowingsFile.count(
                dataDir: app.settings.effectiveDataDir, sourceUsername: name)
            guard count > 0 else {
                error = "取得できませんでした (中断・非公開・存在しないアカウントの可能性)"
                return
            }
            // 件数を提示して確認 (§4.7)
            let alert = NSAlert()
            alert.messageText = "@\(name) のフォロー \(count) 件をユーザーとして登録します"
            alert.addButton(withTitle: "登録")
            alert.addButton(withTitle: "キャンセル")
            guard alert.runModal() == .alertFirstButtonReturn else { return }
            do {
                var tagIds = Array(selectedTags)
                for tagName in parsedNewTags {
                    tagIds.append(try await app.createTag(name: tagName))
                }
                let added = try await app.registerFollowings(source: name, tagIds: tagIds)
                app.statusMessage = "フォロー一括登録: 新規 \(added) 件 / 全 \(count) 件"
                dismiss()
            } catch {
                self.error = error.localizedDescription
            }
        }
    }
}
