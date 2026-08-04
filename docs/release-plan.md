# 公開準備タスク一覧 (release plan)

アプリを GitHub で OSS として公開し、ビルド済みバイナリを GitHub Releases で配布するまでに必要な作業の洗い出し。2026-07 時点のコードベース監査に基づく。

**前提 (決定済み)**

- GitHub でソース公開 + Releases でバイナリ配布
- 対象者は「技術に明るい人」を基本とし、インストーラー 1 つで動くことも目指す
- Sorsa API の使用権は配布しない — 利用者が各自でキーを取得する
- UI は当面日本語のみ (対象読者が日本語話者の間は許容)

**使い方**: 各タスクのチェックボックスを完了時に埋め、必要なら完了日を添える。フェーズ 0 → 1 → 2 が公開の必須経路。3・4 は並行して進められる。5 は最後。

規模の目安: **S** = 1 セッション以内 / **M** = 数セッション / **L** = 設計から必要。

---

## フェーズ 0: 方針決定 (すべての前提)

コードに触る前に決める必要がある事項。ここが決まらないとフェーズ 3 以降が手戻りになる。

### 0-1. アプリ名の決定 (M — 決定自体は早くても、確認事項が多い)

- [x] 名称を決める → **Subako (巣箱)** に決定 (2026-07-28)。「鳥のものを手元に保管する家」= ローカルアーカイブの比喩。
  候補調査の記録: Saezuri (playwell の既存クライアント)・Buncho (既存 iOS/Android クライアント)・
  Tomarigi (校正ツール + Bluesky クライアント)・Torikago (既存 OSS クライアント)・Torinote (App Store に同名) は
  衝突により回避。Subako の既知の同名は無関係の休止 GitHub リポジトリ・ボカロ曲・ゲーム会社 (株式会社スバコ) のみ

現状は 4 つの名前が混在している:

| 場所 | 現在の名前 |
|---|---|
| リポジトリ名 | `TestTwitterAPIIO` (スクラッチ時代の仮名) |
| アセンブリ / exe / 設定フォルダ | `TweetViewer` (`%APPDATA%\TweetViewer`) |
| メインウィンドウのタイトル | `Tweet Viewer` (スペース入り。`MainWindow.xaml:8`) |
| README の見出し | 「Sorsa API ツイート全取得ツール」(Python CLI の説明) |

決定時の確認チェックリスト:

- [x] **X/Twitter の商標に抵触しない名前にする** — Subako は「Tweet」「Twitter」を含まない
- [x] 同名の既存 OSS・アプリの最終確認 → **失効**。「public 化前に再確認」という条件付きの
  項目だったが、フェーズ 5 で public 化を実施済み (2026-07-28)。GitHub 検索の結果で
  判断して公開したため、この時点での再確認は行わない。Microsoft Store への出品を
  検討する段階になったら (付録 B) その時点で改めて調べる
- [x] GitHub のリポジトリ名として使える表記か → `subako` で可
- [x] 名前の波及範囲を把握した上で決める → フェーズ 3-2 で実施済み

### 0-2. ライセンスの決定 (S)

- [x] 自分のコードのライセンスを決める → **MIT** に決定 (2026-07-28)

理由: 依存がすべて MIT / Apache-2.0 / WTFPL / BSD-3 で整合し、個人 OSS の事実上の標準で、利用側の心理的障壁が最も低い。単独著作者のため将来のバージョンからの変更は常に可能。

### 0-3. リポジトリの扱いの決定 (S)

- [x] 「現リポジトリを rename して public 化」か「新リポジトリへ移す」かを決める →
  **現リポジトリを `subako` に rename して履歴ごと public 化**に決定 (2026-07-28)

決定の根拠と受け入れた事項:

- git 履歴に**秘密情報 (API キー・.env・data/・probe_output/) は無い**ことを監査で確認済み (`git log --all --diff-filter=A --name-only` と キー断片の `git log -S` で検証)
- 履歴に含まれる作者メール `lichten.dev@gmail.com` (全コミット) と、フェーズ 1-3 で HEAD から除去する実在ハンドルが**過去コミットに残ること**を了解のうえ公開する
- コミットメッセージに設計判断が記録された開発履歴を公開する価値を優先した
- **rename の実施はフェーズ 5 (public 化の直前)**。GitHub の Web UI (Settings → Rename) か `gh repo rename subako` で行う。旧 URL は自動リダイレクトされる。実施タイミングになったら作業者に明示的に知らせること

---

## フェーズ 1: 公開の前提整備 (法務・衛生)

コード変更ほぼなし。フェーズ 0 の決定後すぐ着手できる。

### 1-1. LICENSE ファイルの追加 (S)

- [x] リポジトリ直下に `LICENSE` を置く → MIT License (Copyright (c) 2026 Lichten) を追加 (2026-07-28)

### 1-2. THIRD-PARTY-NOTICES.md の作成 (S)

- [x] 依存ライブラリのライセンス表記ファイルを作る (2026-07-28)
- [x] Typography.OpenFont / Typography.GlyphLayout のライセンスを確認 → MIT (一部 Apache-2.0 / FreeType License 等の混在)。あわせて Emoji.Wpf 同梱の Twemoji Mozilla フォント内の Twemoji アートワーク (CC-BY 4.0, © Twitter) の帰属表示も追加 (2026-07-28)

監査で確定した依存一覧:

| パッケージ | ライセンス | 義務 |
|---|---|---|
| CommunityToolkit.Mvvm 8.4.2 | MIT | 著作権表示 |
| Microsoft.Data.Sqlite 10.0.10 | MIT | 著作権表示 |
| Microsoft.Xaml.Behaviors.Wpf 1.1.142 | MIT | 著作権表示 |
| SQLitePCLRaw.bundle_e_sqlite3 3.0.2 | Apache-2.0 | NOTICE の伝搬 (§4(d)) |
| Emoji.Wpf 0.3.4 | WTFPL | 義務なし (ただし OSI 非承認。Store 審査等で目を引く可能性) |
| Stfu 0.1.1 (transitive) | WTFPL | 同上 |
| Typography.* (transitive) | **要確認** (LayoutFarm Typography、おそらく Apache-2.0/MIT) | 確認後に記載 |
| requests >=2.31 (Python) | Apache-2.0 | 著作権表示 |
| python-dotenv >=1.0 (Python) | BSD-3-Clause | 著作権表示 |
| pytest / xunit ほかテスト系 | MIT / Apache-2.0 | 配布物に含まれないため不要 |

### 1-3. ソースから実在ハンドルを除去 (S)

- [x] `viewer/TweetViewer.Tests/LinkifierTests.cs` — 架空チャンネル (`examplech` / `@example_ch`) に差し替え (2026-07-28)
- [x] `viewer/TweetViewer/Data/TweetRepository.cs` — 「数万ツイート規模のアーカイブで」に言い換え (2026-07-28)
- [x] `tests/test_followings.py` — 「フォローが 1 ページに収まるアカウント」に一般化 (2026-07-28)
- [x] `viewer/TweetViewer.Tests/FetchBudgetTests.cs` — `alice` に置換 (2026-07-28)

### 1-4. README への免責事項・利用上の注意の追加 (S)

- [x] 免責: Sorsa API の利用は利用者自身の責任であること (2026-07-28)
- [x] 取得データは第三者の著作物であり、私的利用に留めること (2026-07-28)
- [x] X の利用規約への言及。「公式 X API の 3,200 件制限なし」の比較表現は「全期間の取得に対応」に緩和 (2026-07-28)
- [x] X 社・Sorsa と無関係な個人開発物であることの明記 (2026-07-28)

### 1-5. 配布フローの運用ルールを明文化 (S)

- [x] 「配布物は必ず `dotnet publish` の出力フォルダからのみ作る (作業ツリーを zip しない)」を README の「配布物の作成 (開発者向け)」節に明文化 (2026-07-28)

理由: git 履歴はクリーンだが、**作業ツリーには実キー入りの `.env`・実在 4 アカウントのアーカイブ `data/`・生 API 応答 `probe_output/` が存在する**。フォルダごと zip する配布フローは事故が確定している。

---

## フェーズ 2: 単体で動くアプリにする (最重要ブロッカー)

現状、**ダウンロードした人はアプリを一切使えない**。公開の実質的な前提。

### 2-1. 初回起動フローの作り直し (L)

- [x] `DetectRepoDir` 失敗 → エラーダイアログ → `Shutdown(1)` の流れを廃止する (2026-07-28)

現状 (`AppSettings.cs:71-82` / `App.xaml.cs:16-25`): exe の祖先ディレクトリから `main.py` を探し、見つからないと「`%APPDATA%\TweetViewer\settings.json` の RepoDir を設定してください」と表示して即終了する。これは「exe が git checkout の中にある」開発環境専用の仕組みで、公開ビルドでは必ず失敗する。しかも **RepoDir は設定ダイアログから編集できない** (`SettingsDialog` にあるのは DataDir と PythonPath だけ) ので、復旧手段が Notepad しかない。

- [x] 初回セットアップダイアログを新設 (`Views/FirstRunDialog`)。データフォルダの選択だけで閲覧を始められ、fetcher と Python は後から設定できる (2026-07-28)
- [x] `RepoDir` を設定ダイアログから編集可能に (参照ボタン + main.py の存在検証つき) (2026-07-28)
- [x] fetcher の配布方法を決める → **バイナリ配布物に `main.py` + `sorsa_fetcher/` + `requirements.txt` + `.env.example` を同梱する** (2026-07-28 決定)。既存の `DetectRepoDir` は exe 自身のフォルダを最初に見るため、同梱すれば自動検出される。publish への組み込みは 4-1 で実施

### 2-2. Python 未導入時の案内 (M)

- [x] Python 起動失敗 (`Win32Exception`) を「Python 3 が必要 + python.org + 設定の案内」に翻訳 (`FetchProcessService`) (2026-07-28)
- [x] `ModuleNotFoundError` の検出と `pip install -r requirements.txt` の案内 (`FetchOutcome.EnvironmentHint`。ログとリザルト行に表示) (2026-07-28)。設定画面からの実行ボタンは見送り (案内で十分と判断)
- [x] `SORSA_API_KEY` 未設定の検出と .env / キー取得先の案内 (同上) (2026-07-28)

### 2-3. ビューア単体モード (閲覧のみ) の保証 (M)

- [x] `RepoDir` 未設定でも起動・閲覧できるようにし、取得系の全入口 (`StartFetch` / 一括更新 / フォロー一括登録 / 検索追加) に案内つきガードを追加 (2026-07-28)。検索追加は空バケットが残らないようダイアログ表示前に弾く

想定シナリオ: 別 PC で取得したデータフォルダ (Google Drive 共有) を閲覧専用 PC で開く使い方は docs/data-layer.md §6 で既に想定されている。これを「Python 未導入の新規ユーザー」にも自然に開放する。

### 2-4. グローバル例外ハンドラ + ファイルログ (M)

- [x] 3 種の未処理例外 (`Dispatcher` / `AppDomain` / `UnobservedTask`) を捕捉して記録 (2026-07-28)
- [x] クラッシュ時にも既読キューを flush (`App.FlushReadQueue` を通常終了と共用。二重フラッシュは null 化で防止) (2026-07-28)
- [x] `%APPDATA%\Subako\logs\` への日次ファイルログ (`Services/AppLog`。直近 7 日分保持) (2026-07-28)
- [x] クラッシュダイアログから「ログフォルダを開く」(Yes/No で explorer 起動) (2026-07-28)

現状は**ハンドラもログも一切無い**ため、公開後に不具合報告を受けても調査材料をユーザーに求められない。

---

## フェーズ 3: 製品らしさ (アイコン・名称・メタデータ)

フェーズ 0 の決定に依存。フェーズ 2 と並行可。

### 3-1. アプリケーションアイコンの作成 (M)

- [x] アイコンをデザイン → 巣箱 (切妻屋根 + 丸い入口 + 止まり木)。元データは生成コード `tools/icongen` としてリポジトリに保存 (再実行で同じ .ico が得られる)。README 用に `assets/icon/subako-256.png` も出力 (2026-07-28)
- [x] マルチサイズ `.ico` (16/32/48 = BMP エントリ、256 = PNG エントリ) を生成し `viewer/TweetViewer/subako.ico` に配置 (2026-07-28)
- [x] `<ApplicationIcon>` を設定し、ビルド後の exe からアイコン抽出できることを確認 (2026-07-28)
- [x] ウィンドウ左上での見え方を実機確認 (スクリーンショット `assets/screenshot-timeline.png` のタイトルバーに巣箱アイコンが表示されている。exe アイコンの既定継承で足りることを確認) (2026-07-28)

### 3-2. 名称の一斉置換 (M)

フェーズ 0-1 で決めた名前へ。波及範囲 (監査で確定):

- [x] `MainWindow.xaml` のウィンドウタイトル → `{x:Static local:AppInfo.Name}` で定数参照に (2026-07-28)
- [x] MessageBox キャプション — `AppInfo.Name` 定数を新設し全箇所 (24 箇所) を参照に置換 (2026-07-28)
- [x] `NicoThumbnail.cs` の User-Agent → `AppInfo.UserAgent` (アセンブリバージョン連動、例 "Subako/0.1") (2026-07-28)
- [x] `%APPDATA%\TweetViewer` → `%APPDATA%\Subako` + 旧フォルダからの設定移行 (`AppSettings.MigrateLegacySettings`。コピー方式なので旧バージョンに戻しても設定は残る) (2026-07-28)
- [x] アセンブリ名 → `Subako` (exe 名のみ変更。名前空間・フォルダ名は churn を避けて `TweetViewer` のまま据え置くと決定し、csproj にコメントで明記) (2026-07-28)
- [x] リポジトリ名 → フェーズ 5 で `subako` に変更済み (2026-07-28)
- [x] テストの一時フォルダ名 `TweetViewerTests` → `SubakoTests` (2026-07-28)

### 3-3. アセンブリメタデータとバージョン番号 (S)

- [x] `TweetViewer.csproj` に `Version` / `Product` / `Authors` / `Copyright` / `Description` を設定 (2026-07-28)
- [x] バージョニング規則を決める → SemVer。初期値 `0.1.0`、公開時に Git タグ `vX.Y.Z` と csproj の `Version` を一致させる (2026-07-28)

### 3-4. app.manifest の追加 (S)

- [x] DPI awareness (PerMonitorV2) を明示する `app.manifest` を追加 (2026-07-28)
- [x] 混在 DPI のマルチモニタでの表示を実機確認 (モニタ間移動でのにじみ無し・ダイアログの見切れ無し・ポップアップ位置正常・配置復元正常を確認) (2026-07-28)

---

## フェーズ 4: 配布物とドキュメント

### 4-1. publish 設定 (M)

- [x] self-contained + `PublishSingleFile` の publish profile (`Properties/PublishProfiles/win-x64.pubxml`) を作成 (2026-07-28)
- [x] pdb を配布物から除外 (`DebugType=none`。出力に *.pdb ゼロを確認) (2026-07-28)
- [x] fetcher 一式の同梱を csproj の `BundleFetcherAndNotices` ターゲットで実装 (main.py / sorsa_fetcher の *.py / requirements.txt / .env.example / LICENSE / THIRD-PARTY-NOTICES.md)。出力は単一 Subako.exe (約 138MB) + 同梱ファイルのみで `runtimes\` フォルダ無し (2026-07-28)
- [ ] **クリーンな Windows (Windows Sandbox) で起動確認** — 開発機は .NET SDK も Python も入っているため、素の環境での検証が必須。
  確認項目: ①初回セットアップ → データフォルダ選択で閲覧できる ②取得系の入口が
  案内つきガードで止まり `Shutdown(1)` しない ③同梱 `main.py --help` に `--order` がある
  (v0.1.0 の成果物は `--order` 追加前のため、**検証は v0.1.1 の成果物で行うこと**)

### 4-2. インストーラー (M)

- [x] Inno Setup スクリプト (`installer/subako.iss`) を作成。日英 2 言語、per-user 既定、デスクトップアイコンは任意 (2026-07-28)
- [ ] インストーラーの実機確認 (ローカルに Inno Setup 6 を入れて `iscc` するか、初回タグ push 時の Actions 生成物で確認)
- [ ] 将来 Microsoft Store を目指す場合は MSIX を別途検討 (今回はスコープ外としてメモのみ)

### 4-3. GitHub Actions (M)

- [x] CI (`.github/workflows/ci.yml`): push/PR ごとに dotnet test + pytest (windows-latest) (2026-07-28)
- [x] リリース (`.github/workflows/release.yml`): `v*` タグ push で test → publish → zip + Inno Setup インストーラーを**ドラフト** Release に添付 (公開は手動確認後) (2026-07-28)
- [x] 初回 push 後に CI が green になることを確認 (run 30334706220, 2026-07-28)。注: actions/checkout@v4 等に Node.js 20 非推奨の警告が出ている — 動作に支障は無いが、いずれ各 action のメジャーバージョンを上げる

### 4-4. README の書き直し (M)

- [x] 先頭をユーザー向けに再構成: 概要 + アイコン / インストール / API キー取得手順 / 主な機能の使い方 (スクリーンショットは 4-5 待ちで「準備中」と明記) (2026-07-28)
- [x] 開発者向け (ビルド・テスト・配布物作成・リリース手順) を後半に分離 (2026-07-28)
- [x] 既知の制限 (Windows 専用 / 日本語 UI / 従量課金 / 取得は Python 必要) とログ添付の案内を明記 (2026-07-28)

現状の README は全編 Python CLI の開発者向けで、**GUI ビューアの存在にほぼ触れていない** (言及はテストコマンドの 1 行のみ)。公開する製品はビューアなので主客転倒している。

### 4-5. スクリーンショット (S)

- [x] README 用のスクリーンショットを撮影し `assets/screenshot-timeline.png` として README 先頭に埋め込み (2026-07-28)
- [x] **実在アカウントのデータを写さない** — 架空アカウント 3 つの演出用データを生成して撮影 (全文・画像ともプログラム生成の架空コンテンツ) (2026-07-28)

---

## フェーズ 5: 公開作業 (最終チェックリスト)

- [x] リポジトリ名を `subako` に変更 (`gh repo rename`。旧 URL は自動リダイレクト、ローカル remote も更新済み) (2026-07-28)
- [x] public 化直前の最終確認 (3 項目ともクリーンを確認) (2026-07-28):
  - [x] `git log --all --diff-filter=A --name-only` に `.env` / `data/` / `probe_output/` が無い
  - [x] 実キーの断片で `git log --all -S` がヒットしない (0 件)
  - [x] 追跡ファイルに実在ハンドル・個人パスが残っていない
- [x] **public 化** — https://github.com/lichten/subako (2026-07-28)
- [x] 初回リリースタグ `v0.1.0` を push し、Actions がドラフト Release を作成 —
  subako-0.1.0-win-x64.zip (59MB) + subako-setup-0.1.0.exe (44MB) の添付を確認 (2026-07-28)。
  **ドラフトの公開は 4-1 (クリーン環境起動) と 4-2 (インストーラー実機) の検証後に手動で行う**
- [x] README にバッジ (CI / ライセンス / 最新リリース) (2026-07-28)
- [x] Issue テンプレート (バージョン記入 + ログ添付の案内) (2026-07-28)
- [x] CONTRIBUTING.md は当面作らないと判断 (Issue テンプレートで足りる。PR が来るようになったら再検討) (2026-07-28)

### 初回公開のやり直し — v0.1.1 で出す (2026-08-04)

v0.1.0 のドラフトは**検証前のまま塩漬けになっており、かつ成果物が古い**:
`main.py` はその後 `--order` 対応が入っており、Windows の publish には
`BundleFetcherAndNotices` で `main.py` が同梱されるため、v0.1.0 の zip / インストーラーは
**`order` を読めない fetcher を抱えている** (docs/trending-jp.md §10.4-6 の違反)。

- [x] コード側の残作業を完了 (schema ガード / wal_checkpoint / SearchSlug 単位 /
  画像ビューアの URL / 検索の order 保持 / 今日の話題の取得) (2026-08-04)
- [x] `TweetViewer.csproj` の `Version` と `installer/subako.iss` の `MyAppVersion` を
  `0.1.1` に更新 (2026-08-04)
- [ ] `v0.1.1` タグを push し、Actions のドラフト Release を生成
- [ ] 4-1 (クリーン環境起動) を **v0.1.1 の成果物で**実施
- [ ] 4-2 (インストーラー実機) を **v0.1.1 の成果物で**実施
- [ ] v0.1.1 ドラフトを公開し、v0.1.0 のドラフトは削除する

---

## 付録

### A. 依存関係と優先順位

```
フェーズ0 (名前・ライセンス・リポジトリ方針)
  ├─→ フェーズ1 (LICENSE / NOTICES / 衛生 / 免責)   ← コード変更ほぼ無し。すぐ終わる
  ├─→ フェーズ2 (初回起動 / Python案内 / 例外+ログ)  ← 最大の作業。これが無いと「使えない」
  └─→ フェーズ3 (アイコン / 改名 / メタデータ)       ← 2 と並行可
            └─→ フェーズ4 (publish / インストーラー / CI / README)
                      └─→ フェーズ5 (公開)
```

最短経路は **0 → 1 → 2 → 4-1 → 4-4 → 5** (アイコンとインストーラー無しの zip 配布で最小公開する場合)。ただし 3-1 (アイコン) と 3-3 (メタデータ) は小さい割に見栄えへの効果が大きいので、初回リリースに含めることを推奨。

### B. 今回やらないと決めたこと (将来の検討事項)

| 項目 | 理由・条件 |
|---|---|
| UI の英語化 | 対象者が日本語話者の間は保留。着手する場合は固定サイズダイアログ 8 個 (SettingsDialog ほか) が英語の文字長 (約 1.5 倍) で破綻するため、`SizeToContent` 化とセット |
| Python 部分の C# 移植 / Python ランタイム同梱 | 「インストーラー 1 つで完結」の最終形。効果は大きいが L 級の設計作業。まず 2-1〜2-3 の案内強化で凌ぐ |
| Microsoft Store (MSIX) | 審査・開発者アカウントが必要。GitHub 配布が軌道に乗ってから |
| 自動アップデート | 初回は Releases の手動ダウンロードで十分。バージョン表示 (3-3) が先 |

### C. 監査項目との対応表 (取りこぼしチェック)

| 監査での指摘 | 対応タスク |
|---|---|
| 初回起動が開発環境前提で壊れている | 2-1 |
| LICENSE が無い | 1-1 |
| サードパーティ表記が無い | 1-2 |
| アイコン・メタデータ・バージョン皆無 | 3-1, 3-3 |
| 名前が 4 つ混在 / `%APPDATA%` 移行 | 0-1, 3-2 |
| README がビューアを説明していない | 4-4 |
| 例外ハンドラ・ログ無し | 2-4 |
| 作業ツリーの .env / data / probe_output | 1-5, 5 |
| 実在ハンドルの焼き込み | 1-3, 5 |
| 日本語固定 UI・固定サイズダイアログ | 付録 B (保留として記録) |
| publish・インストーラー・CI 無し | 4-1, 4-2, 4-3 |
| ToS・免責の不備 | 1-4 |
| Python 未導入時の案内不足 | 2-2 |
