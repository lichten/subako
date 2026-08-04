# ビューア機能仕様(プラットフォームフリー)

Windows 版ビューア (`viewer/TweetViewer`、アプリ名 Subako) が持つ全機能の挙動仕様。
Mac 版など別プラットフォームのビューアを実装する際に、このドキュメントと
[data-layer.md](data-layer.md)(データ仕様)・[fetcher-cli.md](fetcher-cli.md)(取得側の契約)だけで
機能の全体像を把握できることを目的とする。挙動はプラットフォーム非依存に記述し、
各項目に (参考) として Windows 実装ファイルを付す。

## 1. 起動・セットアップ

### 1.1 初回セットアップ

- 設定にデータフォルダも fetcher の場所も無いときだけ、初回セットアップダイアログを表示する。
- 入力は**データフォルダ 1 項目のみ**(既定値: `マイドキュメント\Subako` 相当)。
  フォルダ選択ボタンあり。存在しなければ作成する。
- 「他の PC で取得済みのデータフォルダ(クラウド同期フォルダ等)を選べばそのまま閲覧できる」
  ことを案内する。キャンセル(終了)を選ぶとアプリを終了する。
- (参考) `Views/FirstRunDialog.xaml(.cs)`, `App.xaml.cs`

### 1.2 起動シーケンス

1. ログ初期化(§11.4)・設定読込。
2. `viewer.db` を開く(なければ作成)。schema_version が自分より新しければ
   エラー表示して終了(data-layer.md §4.2)。必要ならマイグレーション。
3. **既存データの自動登録**: `data/` 直下で **直下に `tweets.jsonl` を持つ**フォルダを
   `users` へ、`data/searches/` 直下の同条件フォルダを検索バケットとして登録する。
   条件はこれだけで、`_` プレフィクスの特別扱いは実装しない
   (`_trash/` / `_followings/` が拾われないのは、それらの**直下**に `tweets.jsonl` が
   無いため。data-layer.md §1.6)。
4. users / 検索バケット / タグを読み込み、前回終了時のタグフィルタを復元。
5. **全アーカイブの JSONL を差分取込**(data-layer.md §5)。ステータスバーに
   `<name> を取込中… NN% (N件)` を表示し、完了で `準備完了`。
6. 表示中(タグフィルタ適用後)の先頭ユーザーを自動選択。
- (参考) `App.xaml.cs`, `ViewModels/MainViewModel.cs` (`InitializeAsync`),
  `Data/UserRepository.cs` (`RegisterExistingDataDirsAsync` / `RegisterExistingSearchDirsAsync`)

### 1.3 閲覧専用モード

fetcher の場所(`main.py` のあるフォルダ)が未設定でも、閲覧機能はすべて使える。
取得系の操作をしたときだけ「取得機能を使うには fetcher の場所が必要。設定から指定を。
既存データの閲覧はこのまま使える」と案内して中止する。
**閲覧だけなら Python も API キーも不要**というのが製品の前提(README)。
- (参考) `MainWindow.xaml.cs` (`EnsureFetcherConfigured`)

## 2. 設定と永続化

### 2.1 設定ダイアログ(3 項目)

| 項目 | 意味 | 検証 |
|---|---|---|
| データフォルダ | `data/` 一式の場所。クラウド同期フォルダで共有可能な旨の注意文つき | 存在しないパスはエラー |
| fetcher の場所 | `main.py` のあるフォルダ。空欄 = 取得機能を使わない | 指定時は `main.py` の存在を検証 |
| Python パス | 空欄なら PATH の python を使う | — |

データフォルダを変更したら「今すぐ再起動しますか?」を出し、Yes で自プロセスを再起動する。
- (参考) `Views/SettingsDialog.xaml(.cs)`

### 2.2 永続化される状態(Windows 版は `%APPDATA%\Subako\settings.json`)

設定ファイルはデータフォルダの**外**(マシンローカル)に置く。Mac 版は
`~/Library/Application Support/` 等の同等地に独自に持てばよく、共有しない。

| キー | 内容 |
|---|---|
| `RepoDir` | fetcher の場所(未設定なら実行ファイルの祖先から `main.py` + `sorsa_fetcher/` を自動検出) |
| `PythonPath` | Python 実行ファイルのパス |
| `DataDir` | データフォルダ(空なら `RepoDir/data`) |
| `WindowLeft/Top/Width/Height`, `WindowMaximized` | ウィンドウ配置。復元時に画面外なら既定値へ |
| `SidebarWidth` | サイドバー幅(既定 260) |
| `UnreadOnly` | 「未読のみ」フィルタの状態 |
| `Ascending` | 「古い順」の状態(false = 新しい順) |
| `TagFilterId` | タグフィルタ(null = すべて、-1 = (タグなし)、その他 = tag_id) |

**永続化されない状態**(再起動で既定に戻る): 表示モード(タイムライン/メディア)、
統合タイムラインの ON/OFF、選択中のユーザー/検索、期間フィルタ、画像の表示倍率。
- (参考) `AppSettings.cs`, `MainWindow.xaml.cs` (`OnClosing`)

## 3. 画面構成

単一メインウィンドウの 3 ペイン構成:

- **左: サイドバー**(既定幅 260、ドラッグで 180〜600 に可変)— §4
- **右: ツールバー + コンテンツ**(タイムライン or メディアグリッド)— §5〜§7
- **下: ステータスバー**(全幅)— 取得中のみ左に進行中インジケータと
  「取得中 — ログを表示」リンク(§9.5)。右に一行通知(取込進捗・削除結果・エラー等)。

ツールバーの並び(左から): `タイムライン`/`メディア` 切替 → `未読のみ` トグル →
`古い順` トグル → 期間フィルタ(年/月/日 + `◀` `▶` `×`)→ `インデックス再構築` → `設定`。

その他のウィンドウ: 画像ビューア(§6.2)と取得ログ(§9.5)は非モーダルの子ウィンドウ、
それ以外のダイアログはすべてモーダル。
- (参考) `MainWindow.xaml`

## 4. サイドバー

### 4.1 ユーザー一覧

- 1 行 = 丸型アイコン(32px 相当、未取得はプレースホルダ)+ 表示名(カラー絵文字対応)+
  `@username NNN件` + タグチップ(複数、折り返し)+ 未読バッジ(件数入り、0 なら非表示)+
  `更新` ボタン(取得中は無効)。
- 並び順: username の大文字小文字無視昇順(COLLATE NOCASE)。
- 行選択で右ペインをそのユーザーのタイムラインに切り替える。
- 右クリックメニュー: `更新 (差分取得)` / `全期間を取得 (バックフィル)...` /
  `不足画像を取得 (API 不使用)` / `タグ`(§4.4)/ `削除...`(§10.1)。
- `＋` ボタンでユーザー追加(§9.2)。
- (参考) `MainWindow.xaml`, `ViewModels/UserItemViewModel.cs`, `Data/UserRepository.cs`

### 4.2 検索一覧(保存済みキーワード検索)

- サイドバー下部の独立セクション。1 行 = 🔍 マーク + ラベル + `NNN件` + タグチップ + 未読バッジ。
- ラベルは `search.json` の `name` → `query` → フォルダ名の順でフォールバック
  (data-layer.md §1.5)。ツールチップにクエリ原文を表示。
- 右クリックメニュー: `更新 (差分取得)...` / `過去期間を取得 (バックフィル)...` /
  `不足画像を取得 (API 不使用)` / `タグ` / `編集 (名称・クエリ)...`(§9.4)/ `削除...`。
- `＋` で新規検索(§9.3)。
- (参考) `ViewModels/SearchItemViewModel.cs`, `Data/SearchMetadata.cs`

### 4.3 タグフィルタ

- サイドバー上部の ComboBox + 解除ボタン `×`。未選択 = 全件表示。
- 先頭に疑似項目 **`(タグなし)`**(タグが 1 つも付いていない行のみ表示)。
- ユーザー一覧と検索一覧の**両方**に同時適用。
- 選択中のユーザー/検索がフィルタで隠れたら、表示中の先頭ユーザーへ自動移動(空ペイン回避)。
- 統合タイムライン中にフィルタを変えると対象集合を組み直す。
- 状態は終了時に保存され次回復元(§2.2 の `TagFilterId`)。
- (参考) `ViewModels/MainViewModel.cs` (`MatchesTagFilter` / `RestoreTagFilter`)

### 4.4 タグの付け外し・整理

- ユーザー/検索行の右クリック「タグ」サブメニューは開くたびに現在のタグ一覧から再構築。
  各タグはチェック式で、メニューを閉じずに連続で付け外しできる。
- 末尾に `新しいタグ...`(名前入力 → 作成して即付与。同名(大小無視)既存ならそれを付与)と
  `タグの整理...`(タグ名 + 付与人数の一覧から削除。付与済みなら確認ダイアログ)。
- タグは**ユーザー/検索バケット単位**。ツイート単体には付けられない。
- (参考) `MainWindow.xaml.cs` (`TagMenu_SubmenuOpened`), `Views/AddTagDialog.xaml(.cs)`,
  `Views/ManageTagsDialog.xaml(.cs)`, `Data/TagRepository.cs`

### 4.5 統合タイムライン(すべて)

- サイドバー上部のトグル。ON にすると**表示中**(タグフィルタ適用後)のユーザーと検索を
  すべて混ぜた統合タイムラインを表示する。
- 個別のユーザー/検索の選択とは相互排他の第 3 の選択状態(サイドバーのハイライトは消える)。
- 同一 `tweet_id` がユーザーアーカイブと検索バケットの両方にある場合は 1 件に重複排除し、
  **代表は実ユーザーアーカイブ優先**(`searches/` を劣後、同順位は username 昇順)。
  SQL 上は LIMIT より前に `ROW_NUMBER() OVER (PARTITION BY tweet_id ...)` で行う
  (LIMIT 後に間引くとページ件数が欠ける)。
- 手動 OFF で先頭ユーザーの表示へ戻る。状態は**非永続**。
- (参考) `ViewModels/MainViewModel.cs` (`IsAllTimeline` / `VisibleArchives`),
  `Data/TweetRepository.cs` (`GetPageAsync`)

### 4.6 表示中をすべて更新

- サイドバー上部のボタン。対象は表示中(タグフィルタ適用後)のユーザー → 検索の順。
- ダイアログで対象件数と**合計リクエスト上限**(既定 100)を入力。上限は全体の合計で、
  到達した時点で残りをスキップして停止する(未消化分の持ち越しはしない)。
- 個別の失敗は飛ばして続行し、最後に
  `完了 N/M 件 / 消費 N リクエスト / 失敗 N 件 — <停止理由>` を報告。
- 対象一覧は開始時にスナップショットする(処理中の一覧更新に影響されない)。
- 各実行の消費リクエスト数は fetcher の完了ログから読む(fetcher-cli.md §4)。
- (参考) `Views/UpdateAllDialog.xaml(.cs)`, `ViewModels/FetchDialogViewModel.cs` (`RunBatchAsync`),
  `Services/FetchBudget.cs`

### 4.7 フォロー中を一括登録

- サイドバーの `⇩` ボタン。ダイアログで①フォロー元アカウント ②付けるタグ(既存から複数選択)
  ③新しいタグ(カンマ・全角カンマ・読点・改行区切りで複数)④最大リクエスト数(既定 50)を入力。
- **タグ 0 件では実行できない**(後からその集合を選び直せなくなるため)。
- 既に `data/_followings/<source>.jsonl` があれば「API を使わずにこれを登録しますか?」と
  提案する(再課金の回避)。
- 取得後に件数を提示して確認(`@x のフォロー N 件をユーザーとして登録します`)。
  登録は users への一括 INSERT + タグ付与のみで、**ツイートは取得しない**。
  既存ユーザーにも同じタグを付ける(冪等)。データフォルダも作らない(data-layer.md §1.7)。
- 0 件なら「取得できませんでした(中断・非公開・存在しないアカウントの可能性)」。
- (参考) `Views/ImportFollowingsDialog.xaml(.cs)`, `ViewModels/MainViewModel.cs`
  (`ImportFollowingsAsync`), `Data/FollowingsFile.cs`, `Data/UserRepository.cs` (`AddManyAsync`)

## 5. タイムライン表示

### 5.1 ツイートカードの構成(上から)

1. 行左端の**未読アクセントバー**(幅 3px 相当、未読 = アクセント色、既読 = 透明)
2. 作者アイコン(40px 丸。**RT は RT元作者のアイコン**)
3. `○○ さんがリツイート` ヘッダ(RT のみ、緑)
4. ヘッダ行: 表示名(太字・カラー絵文字)+ `@username` + 右寄せ日時
   (`yyyy-MM-dd HH:mm`、**ローカル時刻**)
5. `@x への返信` ヘッダ(返信のみ、青)
6. 本文(URL リンク化 §5.5 + カラー絵文字)。
   **RT は "RT @x: …" の切り詰め文ではなく RT元ツイートの全文を表示する**
7. 画像(折り返し配置。origin 0 = 本文 と origin 2 = RT元 をここに出す)
8. 動画サムネイル(§5.4)
9. 引用ブロック(枠線 + 角丸): 引用先アイコン(20px)+ `表示名 @username` + 引用本文 +
   引用先画像(origin 1)+ 引用先動画サムネイル
10. カウント行: `返信 N  RT N  いいね N  表示 N`。
    **RT では返信数を出さない**。**表示回数は 0 なら項目ごと省略**
- (参考) `MainWindow.xaml`, `ViewModels/TweetItemViewModel.cs`

### 5.2 ツイートの右クリックメニュー

| 項目 | 挙動 |
|---|---|
| ブラウザで開く | `https://x.com/<author>/status/<id>`。投稿者を特定できない場合(author が無くアーカイブ名がバケット ID の場合)は `https://x.com/i/web/status/<id>` |
| `@xxx をユーザーに追加` | カードの作者(RT なら RT元作者)を users へ登録。**表示中のユーザー/検索に付いているタグを引き継ぐ**。表示は切り替えない。新規登録かつツイート 0 件なら「今すぐ取得しますか?」を提案 |
| 既読/未読を切り替え | 即時 DB 書込 + 未読数の楽観更新 |

- (参考) `ViewModels/TweetItemViewModel.cs` (`OpenInBrowser`),
  `ViewModels/MainViewModel.cs` (`AddAuthorFromTweetAsync`)

### 5.3 画像の表示サイズ変更

- 画像の右クリックで `小さく (×0.5)` / `等倍に戻す` / `大きく (×2)` / `もっと大きく (×4)`。
  現在値にチェック印。
- 基準サイズ: 本文画像 400×280(デコード幅 400px)、引用先画像 260×180(デコード幅 260px)。
  拡大時はデコード画素も倍率分増やす(上限 2048px)——縮小表示の再拡大でぼやけないため。
- **セッション限り**。リスト再構築(ユーザー切替・絞り込み変更・再起動)で等倍に戻る。
- (参考) `ViewModels/ImageScale.cs`, `ViewModels/TweetImageViewModel.cs`

### 5.4 動画サムネイル

データ仕様・URL 解決規則は data-layer.md §3.6。表示仕様:

- 表示順: **X 添付動画 → 本文リンク由来(YouTube / ニコニコ)**。同一サムネイル URL は先勝ちで 1 件。
- サイズ: 本文 320×180、引用ブロック内 240×135。
- クリックで動画ページ(X 添付動画は mp4 の直 URL)を既定ブラウザで開く。
- サムネイル取得に失敗したら枠ごと出さない(壊れ画像を見せない)。
- (参考) `Services/Linkifier.cs` (`ExtractVideoLinks`), `Services/NicoThumbnail.cs`,
  `Data/RawVideoEntityReader.cs`, `ViewModels/LinkThumbnailViewModel.cs`

### 5.5 本文の URL リンク化

- `https?://\S+` をリンク化してクリックで既定ブラウザ。
- 日本語文中対策として、URL 末尾の約物(`。、」』】!?…` 等)はリンクに含めない。
  スキームだけの断片はリンク化しない。
- 非 URL 部分は絵文字をカラー描画(Windows 版は Emoji.Wpf)。
- (参考) `Services/Linkifier.cs`, `Behaviors/LinkifiedTextBehavior.cs`

### 5.6 無限スクロール(ページング)

- タイムライン 1 ページ **200 件**、メディアグリッド **120 件**(3 列 × 40 行)。
- 末尾まで残り約 2 画面分を切ったら自動で次ページをロード。リストが空の間は発火しない
  (リセット直後の誤発火を防ぐ)。
- ページングは keyset カーソル方式(data-layer.md §4 の `sort_key`, `id_int`[, `idx`])。
  OFFSET は使わない。
- (参考) `Behaviors/InfiniteScrollBehavior.cs`, `ViewModels/TweetListViewModel.cs`,
  `ViewModels/MediaGridViewModel.cs`, `Data/TweetRepository.cs`

### 5.7 キーボードスクロール

- リストの既定動作(項目選択の移動)ではなく**ピクセル単位スクロール**にする:
  ↑/↓ = 48px、PageUp/PageDown = `max(表示域×0.875, 表示域−48px)`、Home = 先頭、End = 末尾。
- 端に達してもキーイベントを消費し、選択移動へフォールバックさせない。
- 表示切替でリストが入れ替わったら先頭へスクロールを戻す。
- 期間フィルタ適用中に 0 件なら中央に
  `この期間のツイートはありません` / `この期間のメディアはありません` を表示
  (ロード完了後にのみ出す)。
- (参考) `Behaviors/KeyboardScrollBehavior.cs`, `Behaviors/ScrollToTopOnResetBehavior.cs`

## 6. メディアビュー

### 6.1 メディアグリッド

- ツールバーの `メディア` で切替。3 列の正方形サムネイルグリッド(セル辺 = リスト幅 ÷ 3)。
- 表示対象は**本人の投稿画像のみ**: `origin = 0` かつ `tweet_type != 1`(RT 除外)。
  引用先・RT元の画像は出さない。
- 期間フィルタ(§7.3)と古い順(§7.2)は有効。**「未読のみ」は無効**(トグルを無効化表示)。
- (参考) `ViewModels/MediaGridViewModel.cs`, `Data/TweetRepository.cs` (`GetMediaPageAsync`)

### 6.2 画像ビューア

- セルクリックで別ウィンドウ(暗背景)を開き、**原寸デコード**で表示。
- `◀` `▶` ボタンと ←/→ キーで前後の画像へ移動(移動範囲は開いた時点のロード済み一覧)。
- 下部に本文(高さ制限つき・省略記号)+ `yyyy-MM-dd HH:mm   (i/N)` +
  `ブラウザで開く` / `閉じる`(Esc)。
- `ブラウザで開く` の URL 規則はタイムライン側 (§5.2) と共通。両方が
  `Services/TweetUrl.cs` (Mac 版は `SubakoCore/Text/TweetURL.swift`) の 1 実装を使う。
  かつては画像ビューアだけが `https://x.com/<archive名>/status/<id>` を無条件に
  組んでいて、検索バケット由来 (archive 名が `searches/<slug>`) では壊れた URL に
  なっていた。
- (参考) `Views/MediaViewerWindow.xaml(.cs)`, `Services/TweetUrl.cs`

## 7. フィルタ・並び順

### 7.1 未読のみ

- `read_state` に行が無いツイートだけ表示。メディアビューでは無効。
- 状態は終了時に保存・次回復元。
- (参考) `ViewModels/MainViewModel.cs` (`UnreadOnly`)

### 7.2 古い順(昇順)

- 既定は新しい順(`sort_key` 降順, `id_int` 降順)。ON で両列とも昇順。
- タイムラインとメディアビューの両方に効く。メディアの第 3 キー `idx` だけは
  方向によらず常に昇順(ツイート内の画像順)。
- keyset カーソルの比較演算子も方向に合わせて反転する(`<` ⇄ `>`)。
- 状態は終了時に保存・次回復元。
- (参考) `Data/TweetRepository.cs`, `viewer/TweetViewer.Tests/AscendingOrderTests.cs`

### 7.3 期間フィルタ(年 / 月 / 日)

- 年の選択肢は**表示対象の実データ範囲だけ**を列挙(`MIN/MAX sort_key` をローカル暦に変換)。
  先頭は `(すべて)` 固定。
- 月は 1〜12 固定で年を選ぶまで無効。日は年月の実日数分を動的生成、月を選ぶまで無効。
- 年を変えると月日をクリア、月を変えると日をクリア。
- `◀` `▶` は選択中の**最も細かい粒度**で前後へ移動(日選択中 = 1 日、月 = 1 か月、年 = 1 年)。
  実データ範囲の外へは移動できない(ボタン無効化で端を明示)。`×` で解除。
- 期間境界は**ローカル 0 時**の半開区間 `[from, to)`(保存は UTC・表示はローカルのため。
  例: JST では月初 0〜9 時のツイートが前月に漏れないように)。
- 表示対象を切り替えて選択年が範囲外になったら全期間へ自動リセット。
- タイムライン・メディアビュー両方に有効。**非永続**。
- (参考) `ViewModels/MainViewModel.cs`, `Models/DateRangeFilter.cs`
  (タイムゾーン依存は `DateRangeFilter` に隔離し、テストは JST 固定で検証)

## 8. 未読管理

### 8.1 スクロール自動既読

- スクロールが **300ms 静止**したら、ビューポート内に完全表示されているカード
  (およびビューポートより背が高く下端を通過したカード)を既読にする。
- 初期表示分も対象(表示直後にタイマー開始)。
- (参考) `Behaviors/ScrollReadBehavior.cs`

### 8.2 書込のバッチ化

- 既読化は UI へ楽観反映してからキューに積み、**1 秒間隔 または 100 件到達**で
  1 トランザクションにまとめて書く。
- 表示対象の切替時・アプリ終了時・クラッシュ時にフラッシュする。書込失敗分は再キュー。
- (参考) `Services/ReadMarkQueue.cs`

### 8.3 アーカイブ横断の既読共有

- `read_state` は `tweet_id` 単位で全アーカイブ共通(data-layer.md §4.1)。
  あるアーカイブで既読にすると、同じツイートを含む**他の全アーカイブ**
  (タグフィルタで隠れているものも含む)の未読数も同時に減らす。
- DB 書込は 1 回。ページ取得時に「複数アーカイブに存在するツイート」の対応表を
  併せて引いておき、既読化時にカウント更新をファンアウトする。
- 未読の可視化はサイドバーの未読バッジとカード左端のアクセントバーの 2 箇所。
- (参考) `Data/TweetRepository.cs` (`LoadDuplicateArchives`),
  `ViewModels/TweetListViewModel.cs` (`NotifyUnreadDelta`)

## 9. データ取得(fetcher 連携)

CLI 契約(引数・exit code・ログ書式)は [fetcher-cli.md](fetcher-cli.md)。ここでは UI 挙動のみ。

### 9.1 共通挙動

- 取得は**同時に 1 つ**(実行中は全「更新」系ボタンを無効化)。
- fetcher 未設定なら案内して中止(§1.3)。
- 取得完了後(失敗・中断でもページ単位で保存済みのため)必ず差分取込 → 一覧再読込 →
  表示中の対象に影響があればリスト再構築を行う。
- (参考) `MainWindow.xaml.cs` (`StartFetch` / `RunFetchAsync`),
  `ViewModels/MainViewModel.cs` (`OnFetchCompletedAsync`)

### 9.2 ユーザー追加

- `@` なしユーザー名を入力(`@` は自動除去)。**英数字と `_` のみ**許可、
  それ以外は「ユーザー名は英数字と _ のみ使用できます」で拒否
  (フォルダ名・バケット ID との衝突防止)。
- **実装差の注意**: Python 側(`--followings` の検証)は ASCII 限定
  `^[A-Za-z0-9_]+$` だが、Windows 版の判定は Unicode の文字・数字も通してしまう
  (`char.IsLetterOrDigit`)。X のユーザー名は ASCII のみなので、
  新実装は **ASCII 限定**(Python 側の規則)に揃えることを推奨。
- 追加後にそのユーザーを選択し、ツイート 0 件なら「今すぐツイートを取得しますか?」。
- (参考) `Views/AddUserDialog.xaml(.cs)`, `ViewModels/MainViewModel.cs` (`NormalizeUsername`)

### 9.3 キーワード検索の新規保存

- 入力: ①名称(任意。空欄ならクエリを表示)②検索クエリ(X 標準演算子。ダイアログに
  構文説明と例を表示)③`RT数 ≥` / `いいね数 ≥`(空欄 = 制限なし)④最大リクエスト数(既定 50)。
- 下限は `min_retweets:` / `min_faves:` としてクエリ末尾に合成する。このとき元クエリを
  `(...)` で包む(OR の結合順が壊れないように)。
- バケット ID = `searches/<slug>`。slug は Python と同一規則(data-layer.md §1.5)。
- `search.json` を**取得開始前に**書き、サイドバーに名称を即表示する。
- (参考) `Views/SearchDialog.xaml(.cs)`, `Data/SearchQueryOperators.cs`, `Data/SearchSlug.cs`

### 9.4 検索の編集(名称・クエリ)

- 現在のクエリを「基本クエリ / RT数下限 / いいね数下限」に分解して表示(合成の逆変換。
  外側 1 枚の括弧だけ外す)。保存で `search.json` を read-modify-write。
- 取得済みツイートは残るが、取得進捗(カーソル・バックフィル済み期間)は次回実行時に
  fetcher が自動リセットする旨を画面に明示する(data-layer.md §1.5 のクエリ変更規則)。
- **取得実行中は編集不可**。
- (参考) `Views/SearchEditDialog.xaml(.cs)`, `ViewModels/MainViewModel.cs` (`UpdateSearchAsync`)

### 9.5 取得ログウィンドウ

- **開始時には表示しない**。実行中はステータスバーの「取得中 — ログを表示」リンクで開ける。
- 等幅フォントのライブログ(最大 2000 行、超過は先頭から破棄、自動で末尾へ追従)+
  進行中インジケータ + `中断` ボタン(プロセスツリーごと停止)。
- 実行中にウィンドウを閉じる操作は「隠す」に読み替える(中断への導線と位置を保つ)。
- **失敗・中断・上限到達のときだけ**完了時に自動表示する。成功して一度も開かれなければ
  そのまま破棄。アプリ終了時は強制クローズ。
- (参考) `Views/UpdateLogWindow.xaml(.cs)`, `ViewModels/FetchDialogViewModel.cs`

### 9.6 各取得ダイアログの入力と既定値

| ダイアログ | 入力 | 既定値 |
|---|---|---|
| バックフィル(ユーザー) | 最大リクエスト数 | 500 |
| 検索の差分更新 | 最大リクエスト数(バックフィルと同ダイアログのタイトル違い) | 500 |
| 検索バックフィル | 遡る開始日(`YYYY-MM-DD`)+ 最大リクエスト数 | 2014-01-01 / 500 |
| 新規検索 | §9.3 の 4 項目 | 上限 50 |
| 表示中をすべて更新 | 合計リクエスト上限 | 100 |
| フォロー中一括登録 | §4.7 の 4 項目 | 上限 50 |

いずれも上限は正の整数のみ受け付ける。「上限に達しても安全に中断され、取得済み分は保存される。
再実行で続きから再開する」旨を案内する(フォロー一覧のみ再開不可 — fetcher-cli.md §3)。
- (参考) `Views/BackfillDialog.xaml(.cs)`, `Views/SearchBackfillDialog.xaml(.cs)`,
  `Views/UpdateAllDialog.xaml(.cs)`

### 9.7 結果メッセージと環境不備ヒント

- exit code とモードに応じた文言を出す(fetcher-cli.md §3)。中断は exit 0 でも
  「中断しました(途中までの取得分は保存済み)」として要注意扱い。
- ログ本文から環境不備を検出してヒントを付す:
  `ModuleNotFoundError` → 「依存パッケージ未導入。fetcher のフォルダで
  `pip install -r requirements.txt`」/ `SORSA_API_KEY` → 「API キー未設定。`.env` に設定」。
- Python 自体が起動できない場合は「Python 3 が必要」+ インストール案内 + 設定への導線。
- (参考) `ViewModels/FetchOutcome.cs`, `Services/FetchProcessService.cs`

## 10. 削除・メンテナンス

### 10.1 アーカイブ削除(ユーザー / 検索)

- 確認ダイアログ: 見出し(`@alice を削除しますか?` / `検索「X」を削除しますか?`)+
  `保存済みのツイート N件のインデックスと既読以外の付随情報が削除されます。` +
  チェックボックス `ツイートデータと画像も完全に削除する`。
  - **OFF(既定)**: フォルダを `data/_trash/` へ移動(data-layer.md §1.6)。
    「フォルダを戻せば次回起動時に再登録され、既読状態も復元される」旨を表示。
  - **ON**: `tweets.jsonl` と画像を完全削除。「復元できない。同じ内容を揃えるには
    API リクエストを消費して取り直しになる」旨を表示。
  - 既定ボタンは**キャンセル**(Enter での誤削除防止)。
- DB からは `tweets` / 孤児 `tweet_media` / `user_tags` / `users` を削除。
  **`read_state` は残す**(data-layer.md §4.1)。
- フォルダ操作の失敗は明示的にエラー表示する(フォルダが残ると次回起動の自動登録で
  復活してしまうため)。**取得実行中は削除不可**。
- (参考) `Views/DeleteArchiveDialog.xaml(.cs)`, `Services/ArchiveTrash.cs`,
  `Data/UserRepository.cs` (`DeleteArchiveAsync`)

### 10.2 インデックス再構築

- ツールバーのボタン(**ユーザー選択中**かつ取得中でないときのみ有効。
  検索バケット選択中・統合タイムライン中は無効)。
- 選択中ユーザーの派生データ(`tweets` / `tweet_media`)を消して JSONL から取り込み直す。
  **既読状態は保持**。進捗を % 表示し、完了後にリストを再構築。
- (参考) `ViewModels/MainViewModel.cs` (`RebuildCommand`), `Data/JsonlImporter.cs`
  (`RebuildUserAsync`)

## 11. その他の仕様

### 11.1 アイコン・サムネイルの取得挙動

キャッシュのパス・命名は data-layer.md §3.5 / §3.6。ビューア側の挙動:

- キャッシュキーは JSONL 中の URL そのまま。実際のダウンロードは `_normal` → `_bigger`
  置換 URL を先に試し、404 なら元 URL(**キー ≠ 取得先**の場合がある)。
- 同一 URL の並行要求は 1 ダウンロードに束ねる。失敗はセッション内で再試行しない
  (ネガティブキャッシュ)。書込は一時ファイル + 原子的リネーム。
- (参考) `Services/IconCache.cs`

### 11.2 画像ファイルの解決

- 期待パス `images/<tweet_id>_<idx>.<ext>` が無ければ `jpg, png, webp, gif, jpeg` の順に探索。
  どれも無ければその画像は表示から落とす(壊れ枠を見せない)。
- 統合タイムラインではページ内に複数アーカイブの行が混ざるため、`images/` の場所は
  **行ごとの `username`** から解決すること。
- (参考) `Services/LocalMediaFiles.cs`

### 11.3 日時の扱い

- 保存は UTC(`yyyy-MM-ddTHH:mm:ssZ`)、表示はローカル `yyyy-MM-dd HH:mm`。
- 期間フィルタの境界はローカル 0 時(§7.3)。

### 11.4 クラッシュ処理・ログ

- 未処理例外は「予期しないエラー。ログフォルダを開きますか?」を表示して終了。
  非 UI スレッド・未観測の非同期例外も記録する。**どの経路でも未書込の既読マークを
  フラッシュしてから落ちる**。
- ログはマシンローカル(Windows 版は `%APPDATA%\Subako\logs\yyyyMMdd.log`)に日次 1 ファイル、
  直近 7 個を保持。
- (参考) `App.xaml.cs`, `Services/AppLog.cs`

## 12. 未実装・既知の制約(新実装の判断材料)

Windows 版に**無い**もの(仕様上の欠落ではなく意図的な範囲):

- **ローカル全文検索**(サイドバーの「検索」は X 全体への API 検索の保存であり、
  取得済みアーカイブのキーワード検索ではない)
- ドラッグ & ドロップ
- ダークモード / テーマ切替(配色はハードコード: サイドバー `#F4F4F6`、
  未読・リンク `#1D9BF0`、罫線・タグチップ `#E1E8ED`、RT 緑 `#00BA7C`、補助文字 `#666`/`#888`)
- ツイート単体の詳細画面・スレッド(会話)表示(返信は `@x への返信` ヘッダのみ)
- 動画のアプリ内再生(サムネイル → ブラウザで開くのみ)
- ツイート単位のタグ付け(タグはアーカイブ単位)
- カスタムキーバインド・メニューバー・グローバルショートカット
- UI の多言語化(日本語固定。Windows 版は固定サイズダイアログが多く英語化で破綻する
  設計負債が記録されている — release-plan.md 付録 B。新実装は最初から可変サイズにするとよい)

README 記載の既知事項: Sorsa API の取得件数がプロフィール上のツイート数より
少なくなることがある(先方仕様)。
