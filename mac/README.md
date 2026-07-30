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
└── Scripts/make-app.sh      # Subako.app バンドルの組み立て
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

- ピクセル単位のキーボードスクロール (viewer-features §5.7) は未実装
  (標準のスクロール挙動)。
- ウィンドウ配置・サイドバー幅の保存は macOS 標準の状態復元に任せている。
- クラッシュ時の既読フラッシュ専用ハンドラは無し (既読キューは 1 秒周期で
  書き込むため損失は最大 1 秒分)。
- 取得機能 (fetcher 連携) は実 API キーでの E2E 検証が未実施。
