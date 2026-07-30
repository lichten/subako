# Mac 版実装ノート

Windows 版と同じデータを読む Mac 版ビューアを実装するための、文書横断の要点集。

## 1. 読み順

1. [data-layer.md](data-layer.md) — ディレクトリ構成・JSONL・SQLite・取込契約(最重要)
2. [viewer-features.md](viewer-features.md) — 再現すべき機能と挙動の網羅
3. [fetcher-cli.md](fetcher-cli.md) — 取得機能を持たせる場合の CLI 契約

最小スコープは**閲覧のみ**(Python も API キーも不要 — viewer-features.md §1.3)。
取得機能は Python fetcher が macOS でそのまま動くため、後付けで CLI 契約に従えばよい。

## 2. 必ず揃える共有契約

複数実装(C# / Python / 新実装)で**規則が一致していないとデータが壊れる・読めなくなる**もの。
新実装ではこの表の単位でテストを移植すること。

| # | 契約 | 参照 | Windows 実装 | Python 実装 | 検証に使えるテスト |
|---|---|---|---|---|---|
| 1 | ツイート ID 抽出順 (`id_str`→`id`→`tweet_id`) | data-layer.md §2 | `Data/TweetJsonParser.cs` | `sorsa_fetcher/storage.py` | `TweetJsonParserTests.cs` |
| 2 | `created_at` 4 形式 + ISO フォールバック | §2 | 同上 | `sorsa_fetcher/fetcher.py` | 同上 |
| 3 | 画像の列挙順・URL 重複除去・`idx` 連番 | §3 | `TweetJsonParser.ExtractMedia` | `media.extract_photo_urls` | `TweetJsonParserTests.cs`, `QuotedImageTests.cs` |
| 4 | full_text からの画像 URL 抽出正規表現 | §3 | `TweetJsonParser.MediaUrlsFromText` | `media._media_urls_from_text` | `TweetJsonParserTests.cs` |
| 5 | 拡張子決定規則 | §3 | `TweetJsonParser.ExtOf` | `media.to_original_size` | 同上 |
| 6 | 検索 slug 生成 | §1.5 | `Data/SearchSlug.cs` | `fetcher.slugify_query` | `SearchSlugTests.cs`(**Python 実出力を期待値としてコメント付きで固定済み** — 新実装の検証にそのまま流用可) |
| 7 | アイコン/サムネイルキャッシュのファイル名 `<sha1(url) 小文字hex>.<ext>` | §3.5 | `Services/IconCache.cs` | (ビューア専用) | 専用テストなし — 新実装は実データの `data/icons/` の既存ファイル名と突き合わせて検証 |
| 8 | JSONL 取込(完結行のみ + `jsonl_offset`、バッチと同一トランザクション) | §5 | `Data/JsonlImporter.cs` | (書き手は append-only のみ) | `JsonlImporterTests.cs` |

`viewer/TweetViewer.Tests/` のテストは仕様の実行可能な裏付けになっている。特に
`TweetJsonParserTests.cs` と `JsonlImporterTests.cs` は data-layer.md の条項とほぼ 1:1。
ほかに `MergedTimelineTests.cs`(統合表示の重複排除とページング)、
`AscendingOrderTests.cs` / `DateRangeQueryTests.cs`(並び順・期間フィルタの SQL 規則)、
`SchemaMigrationTests.cs`(v1 の DDL 実物とマイグレーション)が移植元として有用。

## 3. SQLite・並行性

- `viewer.db` は**共有データフォルダ内**にある。接続設定を Windows 版に合わせること:
  `journal_mode=WAL` / `busy_timeout=5000` / `synchronous=NORMAL`。
- 書き手はプロセス内で直列化(Windows 版は単一の書込ロック)。プロセス間・マシン間の
  排他は SQLite 任せなので、**同時に書くのは常に 1 台**(data-layer.md §6)。
- クラウド同期(Google Drive 等)はファイル単位・任意タイミングで同期するため、
  `viewer.db` / `-wal` / `-shm` の組が不整合な瞬間があり得る。**Mac / Windows の同時起動は
  不可**と考えること。終了時に `PRAGMA wal_checkpoint(TRUNCATE)` を明示すると
  同期の安全性が上がる(Windows 版は未実施)。
- `read_state`(既読)は tweet_id 単位のグローバル共有なので、片方の PC で読んだ結果は
  そのまま他方に反映されるのが設計意図。ただし同期の衝突はファイル丸ごと
  「最後に同期した方が勝つ」ため、両方で書いていると既読ロストが起きうる。
  Mac 版に**閲覧専用モード(read_state を書かない)**を用意する価値がある。
- 読み取り専用で開くなら SQLite の `mode=ro&immutable=1` 相当が安全
  (同期中の WAL に巻き込まれない)。
- **列名指定で読むこと**: 実 DB はマイグレーション由来で列順が新規 DB と異なる
  (data-layer.md §4.2 の注記)。`SELECT *` + 序数アクセスは禁止。
- schema_version が自分の対応バージョンより新しい DB は開かずエラーにする。

## 4. パス・ファイル名

- ユーザー名の検証は**英数字と `_` のみ**(viewer-features.md §9.2)。これがフォルダ名の
  安全性を担保している。新実装は ASCII 限定 `^[A-Za-z0-9_]+$`(Python 側の規則)に
  揃えること — Windows 版の判定は Unicode の文字・数字も通す実装差がある
  (§9.2 の注意を参照)。
- 検索バケットの ID `searches/<slug>` は**パス区切りを含む文字列**としてコード中を流れる
  (`users.username` にもこのまま入る)。macOS では `/` がネイティブの区切りなので
  そのまま結合できる。ID として比較するときは `searches/` プレフィクスで判定。
- 画像の実ファイルは拡張子が期待とずれていることがある。`jpg, png, webp, gif, jpeg` の順の
  探索フォールバックを実装すること(viewer-features.md §11.2)。
- 設定・ログはデータフォルダに入れない(マシンローカル)。Mac なら
  `~/Library/Application Support/Subako/` と `~/Library/Logs/Subako/` 等が相当。

## 5. Windows 版の既知の課題(新実装では最初から回避)

- **画像ビューアの「ブラウザで開く」**: アーカイブ名から無条件に
  `https://x.com/<name>/status/<id>` を組むため、検索バケット由来では壊れた URL になる。
  タイムライン側の規則(author 不明なら `https://x.com/i/web/status/<id>`)に統一すること
  (viewer-features.md §6.2)。
- **固定サイズダイアログ**: Windows 版はダイアログ 8 個が固定サイズで、文言変更・翻訳で
  レイアウトが破綻する設計負債が記録されている(release-plan.md 付録 B)。
  最初から内容依存サイズにする。
- **非永続の状態**(viewer-features.md §2.2): 表示モード・統合タイムライン・期間フィルタ・
  画像倍率は Windows 版では意図的に保存していない。Mac 版で保存するのは自由だが、
  データフォルダ側の仕様には影響しない(設定はマシンローカルのため)。

## 6. Mac 側での開発の始め方

1. **リポジトリを clone**: `git clone https://github.com/lichten/subako.git`(private)。
   Mac 版のコードは同一リポジトリ内に置く(例: `mac/` ディレクトリ)。CI
   (`.github/workflows/ci.yml`) は Windows ランナーなので、Mac 版のビルドを CI に
   載せる場合は macOS ランナーのジョブを追加する。
2. **データを用意**: 実データは Google Drive 共有フォルダにある。Mac の Google Drive
   クライアントで同期し、そのフォルダをデータフォルダとして使う。
   閲覧開発だけなら **Python も API キーも不要**(§1)。
   - 開発初期は **Windows 側のビューアを閉じた状態で、読み取り専用**
     (`mode=ro&immutable=1`)で開くのが安全(§3)。書込(既読・タグ)の実装は
     動作が安定してから。
   - 実データを使わない試験には、fetcher の JSONL を数行コピーした小さな `data/` を
     手元に作ればよい(`viewer.db` は無ければ自分で作る側なので不要)。
3. **データが読めることを確認**(アプリを書き始める前の疎通確認):
   ```sh
   sqlite3 "file:<データフォルダ>/viewer.db?mode=ro&immutable=1" \
     "SELECT value FROM schema_meta WHERE key='schema_version';
      SELECT username, COUNT(*) FROM tweets GROUP BY username;"
   head -1 "<データフォルダ>/<username>/tweets.jsonl" | python3 -m json.tool
   ```
   schema_version が 7(またはこの文書群が対応する版)であること、JSONL が
   data-layer.md §2 の形で読めることを確認する。
4. **実装順の目安**: §1 の読み順で仕様を把握 →
   ①共有契約のうち読み取りに必要なもの(JSONL パーサ・画像パス解決)+ §2 の表の
   テスト移植 → ②SQLite 読み取り(タイムラインの keyset ページング)→
   ③UI(タイムライン → メディア → フィルタ)→ ④書込(既読・タグ)→
   ⑤取得機能(fetcher-cli.md。Python fetcher は macOS でそのまま動く)。
5. **Windows 側のテストは Mac では実行できない**点に注意:
   `viewer/TweetViewer.Tests` は `net10.0-windows` + WPF 依存のため macOS では
   ビルドできない。**実行可能な仕様書としてコードを読む**用途に使い、期待値
   (特に `SearchSlugTests.cs` の Python 実出力コメント)を新実装のテストへ移植する。
   Python 側の `pytest` は Mac でも動く(API キー・ネットワーク不要)。
6. **fetcher を動かす場合**(任意):
   ```sh
   python3 -m venv .venv && source .venv/bin/activate
   pip install -r requirements.txt
   cp .env.example .env   # SORSA_API_KEY を記入
   python main.py <username> --output-dir "<データフォルダ>" --max-requests 5  # 小さく試す
   ```
   `--output-dir` を必ず共有データフォルダに合わせること(fetcher-cli.md §2)。

## 7. スコープの参考

Windows 版に無い機能(viewer-features.md §12)のうち、Mac 版での差別化候補として
記録されているもの: **ローカル全文検索**(アーカイブ済みツイートの検索。現状の「検索」は
X 全体への API 検索の保存)。そのほかスレッド表示・動画再生・ツイート単位タグなどは
意図的に未実装。追加する場合も**共有データ(data-layer.md)の互換を壊さないこと**
(新テーブルを足すなら schema_version の規則に従う)。
