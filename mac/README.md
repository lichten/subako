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
├── Resources/Subako.icns    # 生成済みアイコン (make-icon.sh で再生成)
└── Scripts/                 # make-app.sh (.app 組み立て) / make-icon.sh (アイコン)
```

## ビルド・テスト

```sh
cd mac
swift test          # SubakoCore のユニットテスト (CI と同じ)
swift build         # デバッグビルド
./Scripts/make-app.sh   # mac/dist/Subako.app を生成 (release + ad-hoc 署名)
```

開発中は `swift run SubakoApp` でも起動できる。Xcode を使う場合は
`mac/Package.swift` をそのまま開けばよい (xcodeproj は無い)。

## アプリケーションアイコン

`Resources/Subako.icns` を commit してあり、`make-app.sh` はそれを複製するだけ
(Windows 版が `subako.ico` を commit しているのと同じ扱い)。再生成するときは:

```sh
./Scripts/make-icon.sh   # Sources/subako-icongen で描画 → iconutil で .icns 化
```

図形の定義は `Sources/subako-icongen/IconGen.swift`。**Windows 版
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
- 取得機能 (fetcher 連携) は実 API キーでの E2E 検証が未実施。
