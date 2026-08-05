# Subako macOS 版

Windows 版 (`viewer/TweetViewer`) と同じデータフォルダを読む macOS ネイティブビューア。
Swift + SwiftUI。仕様の正典は [docs/mac-port-notes.md](../docs/mac-port-notes.md) と、
そこから参照される data-layer.md / viewer-features.md / fetcher-cli.md。

SQLite は同梱ビルド ([swift-toolchain-sqlite](https://github.com/swiftlang/swift-toolchain-sqlite))
を使う。**macOS 標準の libsqlite3 は使えない**: Google Drive が同期アップロード用に
開いている `viewer.db-wal` へハードリンクを作ると、標準 SQLite の保護機構が
「API 違反 (vnode linked while in use)」として接続を無効化し、以後の全操作が
disk I/O error (SQLITE_IOERR_VNODE) になるため。Windows 版も自前 SQLite
(SQLitePCLRaw) を同梱しており、これで両者の挙動が揃う。

## 構成

```
mac/
├── Package.swift            # SPM (SubakoCore + SubakoApp + subako-smoke)
├── Sources/SubakoCore/      # UI 非依存の共有契約層 (パーサ・DB・取込・fetcher 連携)
│   ├── Parsing/             # TweetJsonParser (契約 #1–#5) / SearchSlug (#6) / DateParsers
│   ├── Database/            # ViewerDatabase (WAL・マイグレーション・ro モード) /
│   │                        #   TweetRepository (keyset ページング・統合TL) / User / Tag
│   ├── Import/              # JsonlImporter (契約 #8) / RawVideoEntityReader
│   ├── Files/               # IconCacheKey (#7) / LocalMediaFiles / SearchMetadata / Followings
│   ├── Fetch/               # FetchArguments / FetchBudget / FetchOutcome / FetcherProcess
│   └── Text/                # Linkifier / SearchQueryOperators / TweetURL
├── Sources/SubakoApp/       # SwiftUI アプリ本体
├── Sources/subako-smoke/    # 実データ疎通確認 CLI (mac-port-notes §6.3)
├── Tests/SubakoCoreTests/   # C# テスト (viewer/TweetViewer.Tests) からの移植
├── Sources/subako-icongen/  # アプリアイコンの描画 (tools/icongen の移植)
├── Resources/               # Subako.icns (make-icon.sh で再生成) / Info.plist (Xcode 用)
├── Subako.xcodeproj/        # Xcode で .app を実行・デバッグするためのプロジェクト
└── Scripts/                 # make-app.sh (.app 組み立て) / make-icon.sh (アイコン)
```

## ビルド・テスト

swift-tools-version 6.2 のため、ビルドには Swift 6.2+ (Xcode 26 以降) が必要。

```sh
cd mac
swift test          # SubakoCore のユニットテスト (CI と同じ)
swift build         # デバッグビルド
./Scripts/lint.sh   # SwiftLint (CI と同条件、警告も失敗扱い)
./Scripts/make-app.sh   # mac/dist/Subako.app を生成 (release + ad-hoc 署名)
```

開発中は `swift run SubakoApp` でも起動できる。

### Xcode で開発する

`mac/Subako.xcodeproj` を開くと、Run で Info.plist・アイコン付きの Subako.app が
ビルド・起動され、ブレークポイントや Instruments が使える。ソースは
`Sources/SubakoApp` / `Sources/SubakoCore` を synchronized folder で共有するため、
ファイルの追加・削除で pbxproj の編集は不要。`swift build` / `swift test` /
make-app.sh のワークフローには影響しない (xcodeproj は SPM からは不可視)。

SubakoCore は xcodeproj 内では静的ライブラリターゲットとしてビルドする。
ローカルパッケージ参照にしない理由: Xcode はパッケージ依存を
`-suppress-warnings` 付きでビルドするため、Package.swift の
`treatAllWarnings` が生成する `-warnings-as-errors` と衝突してビルドが
失敗する (Swift 6.3 で修正予定の既知の問題。swift-package-manager#9517)。
SQLite は同じ理由を避けて swift-toolchain-sqlite をリモートパッケージ参照で
直接リンクする (C ターゲットのみなので衝突しない)。

- **fetcher 自動検出の注意**: Xcode の Run では実行バイナリが DerivedData 配下に
  置かれるため、`Bundle.main.bundlePath` の祖先を遡るリポジトリ自動検出
  (AppSettings) は失敗する。初回に設定画面で fetcher の場所と Python パスを
  手動設定すれば `~/Library/Application Support/Subako/settings.json` に保存され、
  make-app.sh 版とも共有される。
- **二重管理箇所** (片方だけ変えないこと):
  - Info.plist: `Resources/Info.plist` (Xcode) ⇔ `Scripts/make-app.sh` の heredoc。
    バージョン更新時は make-app.sh の `VERSION` と Info.plist の
    `CFBundleShortVersionString` / `CFBundleVersion` を揃える。
  - 厳格化設定: `Package.swift` の `strictSwiftSettings` ⇔ pbxproj の target 設定
    (`OTHER_SWIFT_FLAGS` + `SWIFT_UPCOMING_FEATURE_*`、Subako / SubakoCore の 2 ターゲット)。
  - swift-toolchain-sqlite の依存宣言と pin: `mac/Package.swift` + `mac/Package.resolved` ⇔
    pbxproj の XCRemoteSwiftPackageReference +
    `Subako.xcodeproj/project.xcworkspace/xcshareddata/swiftpm/Package.resolved`。

## 静的解析・警告ポリシー

```sh
brew install swiftlint   # 初回のみ (CI は 0.65.0 に固定)
./Scripts/lint.sh        # = swiftlint lint --strict
```

- 設定は `.swiftlint.yml` (テスト向けの緩和は `Tests/.swiftlint.yml`)。
  CI (test-mac) では違反が 1 件でもあるとジョブが失敗する。
  既存コード由来の既知の逸脱 (AppModel.swift の行数など) は
  `// swiftlint:disable` コメントで理由付きで明示してあり、
  新規の disable コメント追加は原則しない。
- コンパイラ警告もエラー扱い (`Package.swift` の `treatAllWarnings`)。ただし
  deprecation 警告だけは SDK 更新で突然増えるため警告のまま残している。
- Swift 7 で必須になる予定の `ExistentialAny` / `MemberImportVisibility` を
  先行有効化している。

## アプリケーションアイコン

`Resources/Subako.icns` を commit してあり、`make-app.sh` はそれを複製するだけ
(Windows 版が `subako.ico` を commit しているのと同じ扱い)。再生成するときは:

```sh
./Scripts/make-icon.sh   # Sources/subako-icongen で描画 → iconutil で .icns 化
```

図形の定義は `Sources/subako-icongen/main.swift`。**Windows 版
(`tools/icongen/Program.cs`) と同一の比率にすること** — 片方だけ直すと
両プラットフォームで見た目がずれる。詳細は docs/mac-port-notes.md §4.7。

## 実データとの疎通確認 (アプリより先に)

Google Drive で同期したデータフォルダに対して (**Windows 側ビューアは閉じた状態で**):

```sh
swift run subako-smoke "<データフォルダ>"            # 読み取り専用 (mode=ro&immutable=1)
swift run subako-smoke "<コピーしたフォルダ>" --import  # 書込 + 差分取込の検証
```

## 設定・ログの置き場所 (マシンローカル — データフォルダには置かない)

- 設定: `~/Library/Application Support/Subako/settings.json`
- ログ: `~/Library/Logs/Subako/yyyyMMdd.log` (7 世代)

設定の「閲覧専用モード」を有効にすると viewer.db を `mode=ro&immutable=1` で開き、
既読・タグ等を一切書き込まない (Windows 側との同時利用時の既読ロスト対策 —
mac-port-notes §3)。

## Windows 版仕様との既知差分 (今後の課題)

- キーボードスクロール (§5.7) は Windows 版より対応キーが多い。
  Windows は ↑/↓ / PageUp / PageDown / Home / End の 6 キーだが、Mac 版は
  macOS と主要ブラウザの慣習に合わせて Space / Shift+Space (1 画面送り) と
  Command+↑ / Command+↓ (先頭・末尾) を追加し、メディアグリッドにも適用している
  (Windows はタイムラインのみ)。移動量の規則は共通。
- ウィンドウ位置・サイズとサイドバー幅は Windows 版と同じく settings.json に持つ
  (§2.2)。SwiftUI の WindowGroup はサイズしか復元せず、位置は起動元アプリのある
  画面へ移し替えてしまうため自前で復元している (docs/mac-port-notes.md §4.6)。
  最大化 (ズーム) 状態は保存しない。
- クラッシュ時の既読フラッシュ専用ハンドラは無し (既読キューは 1 秒周期で
  書き込むため損失は最大 1 秒分)。
- ページ読込に失敗したとき、Windows 版は未処理例外としてダイアログを出して終了するが、
  Mac 版はページングを止めずにリスト内へ「読み込みに失敗しました / 再試行」を出す
  (mac-port-notes §3 のとおり、クラウド同期由来の一時的な失敗が起こりうるため)。
- タイムライン画像の右クリックに「このサイズを既定にする」があり、既定の表示倍率を
  settings.json (defaultImageScale) に保存できる。Windows 版は常に等倍・非永続
  (viewer-features.md §5.3)。設定はマシンローカルのため mac-port-notes の
  「非永続の状態」の許容範囲内。個別画像の一時変更は従来どおりセッション限り。
- View メニューに「文字を大きく (⌘+) / 文字を小さく (⌘-) / 標準サイズ (⌘0)」があり、
  タイムラインの文字サイズを settings.json (timelineFontScale) に保存できる。
  Windows 版に同等機能は無い (設定はマシンローカルのため mac-port-notes の許容範囲内)。
  倍率の段階と丸めは `SubakoCore/Text/TimelineFontScale.swift` が正典で、
  拡大対象は文字のみ (アイコン径・画像・余白は基準値のまま)。

## 取得機能 (fetcher 連携) の準備

取得はリポジトリルートの Python fetcher (`main.py`) を子プロセス起動する
(docs/fetcher-cli.md)。API キーはアプリではなく **fetcher 側の `.env`** に置く:

```sh
cd <リポジトリルート>
python3 -m venv .venv && .venv/bin/pip install -r requirements.txt
cp .env.example .env    # SORSA_API_KEY を記入
```

アプリの設定画面で「fetcher の場所」にリポジトリルート、「Python パス」に
`.venv/bin/python3` の**絶対パス**を指定する。GUI アプリはログインシェルの
PATH を見ないため、venv を使うなら Python パスの明示が実質必須。
なお `settings.json` の直接編集はアプリ起動中の保存で上書きされるため、
設定は必ずアプリの設定画面から行うこと。

実 API での E2E 検証は 2026-07-31 に実施済み (更新・バックフィル・検索・
フォロー一覧・不足画像・中断・上限到達 exit=10・キー未設定ヒントの 8 経路)。
