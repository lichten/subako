# データ層仕様(プラットフォームフリー)

Windows 版ビューア(`viewer/TweetViewer`)・Python fetcher・将来の Mac / Android 版
ビューアが共有するデータの規約。この文書だけで別プラットフォームの
閲覧ツールを実装できることを目的とする。

## 1. ディレクトリ構成

```
data/
├── viewer.db                 # SQLite (このドキュメントの §4)
├── icons/                    # ユーザーアイコンキャッシュ (§3.5)
├── <username>/               # X のスクリーン名 (@ なし)
│   ├── tweets.jsonl          # 生ツイートアーカイブ (正データ、§2)
│   ├── state.json            # fetcher 私有 (§6)
│   └── images/               # 画像 (§3)
└── searches/<slug>/          # キーワード検索バケット (§1.5)
    ├── tweets.jsonl          # 検索結果 (正データ、§2 と同契約)
    ├── search.json           # クエリ原文メタデータ (§1.5)
    ├── state.json            # fetcher 私有 (§6)
    └── images/               # 画像 (§3)
```

### 1.5 キーワード検索バケット (`data/searches/<slug>/`)

`/search-tweets` による任意クエリの検索結果を保存する。取得は
`python main.py --search "<query>" [--search-name <slug>] [--max-requests N]`。

- **slug 生成規則** (Python `sorsa_fetcher.fetcher.slugify_query` と
  C# `TweetViewer.Data.SearchSlug.From` で同一に保つこと):
  不正文字と空白の連続 (`\ / : * ? " < > | \s`) を `_` に置換 → 前後の `_` を除去 →
  40 字に切詰め (空なら `search`) → `-` + クエリ原文 UTF-8 の SHA1 先頭 8 hex。
- **search.json**: `{"query": "<クエリ原文>", "name": "<任意の表示名>"?, "created_at": "<ISO 8601>"}`。
  書き手は fetcher (初回作成・`--search` 実行時のクエリ同期) と
  ビューア (クエリ変更・名称設定)。いずれも**他キーを保持する read-modify-write** で
  更新すること。表示ラベルは `name` → `query` → フォルダ名の順でフォールバック。
- **クエリ変更** (取得済みツイートを残したまま条件変更):
  - slug (フォルダ名 = バケット ID) は**不変の ID** であり、変更後はクエリの
    SHA1 と一致しなくなるが無害 (取得は常に `--search-name` で明示指定)。
  - fetcher は `state.json` 内の `query` と実行クエリの不一致を検知すると、
    `search_cursor` / `search_done` / `backfill_done_windows` を**自動リセット**する
    (旧クエリの結果空間に対する進捗のため)。tweets.jsonl は ID 重複排除で保持され、
    次回取得は新クエリの初回ページングとして未知分のみ追記する。
- **検索クエリ構文**: X 標準の検索演算子を素通しする。
  | 構文 | 意味 |
  |---|---|
  | `語1 語2` (スペース区切り) | AND |
  | `語1 OR 語2` (OR は大文字) | OR |
  | `"slay the spire 2"` | フレーズ完全一致 |
  | `(...)` | グループ化 (OR と AND の併用時に必須) |
  | `-語` | 除外 |
  | `lang:ja` | 言語フィルタ |
  | `min_faves:N` / `min_retweets:N` | いいね数 / RT数の下限 (サーバー側フィルタ) |
  | `from:user` / `since:YYYY-MM-DD` / `until:YYYY-MM-DD` | 投稿者・期間 |

  例: `(sts2 OR "slay the spire 2" OR スレスパ2) lang:ja min_faves:10`
- **取得モード** (ユーザーアーカイブと対称):
  - 指定なし: 初回は `state.json` の `search_cursor` / `search_done` で中断再開つき
    全ページング、`search_done` 後は差分
  - `--update`: 常に最新から差分 (新しい順に辿り、非空ページ全件既知で停止)
  - `--backfill [--backfill-since YYYY-MM-DD]`: 初回ページングを完走した後、
    検索カーソルの終端より古い期間を `(query) since:.. until:..` の30日窓で
    最古保存ツイートから `--backfill-since` (既定 2014-01-01) まで遡って補完。
    完了済み窓はユーザーバックフィルと同じ `backfill_done_windows` に記録され再開可。
    クエリ自体に `since:` / `until:` を含む検索は窓指定と競合するため非推奨。
- ビューアはバケットを `users.username = "searches/<slug>"` の仮想行として登録し、
  ユーザー一覧とは別の「検索」セクションに表示する (§4.2 注記)。

## 2. tweets.jsonl の契約

- UTF-8。**1行 = 1 ツイートの JSON オブジェクト**、行は必ず `\n` で終端。
- 追記専用(append-only)。書き手(fetcher)は ID 重複を書き込まない。
- ツイート ID の抽出順: `id_str` → `id` → `tweet_id`(最初に見つかった
  非空値を文字列化したものが正準 ID)。
- `created_at` は次の4形式 + ISO 8601 フォールバックで解釈する:
  1. `Wed Oct 10 20:19:24 +0000 2018`(旧 Twitter 形式)
  2. `YYYY-MM-DDTHH:MM:SS±zzzz`
  3. `YYYY-MM-DDTHH:MM:SS.ffffff±zzzz`
  4. `YYYY-MM-DD HH:MM:SS±zzzz`
- 主なフィールド(Sorsa API 実測。**欠損に耐えること** — best effort):
  `id`, `created_at`, `full_text`, `user{username, display_name, ...}`,
  `retweeted_status`(RT 時は元ツイートの完全なオブジェクト、それ以外 null),
  `quoted_status`(引用時、同上), `is_quote_status`, `is_reply`,
  `in_reply_to_tweet_id`, `in_reply_to_username`, `lang`,
  `reply_count`, `retweet_count`, `likes_count`, `view_count`,
  `entities`(**配列**: `[{"type":"photo","link":..., "preview":...}]`)。
- 種別判定(排他、この優先順): `retweeted_status` 非 null → **RT** /
  `is_reply` true または `in_reply_to_tweet_id` 非 null → **Reply** /
  `is_quote_status` true または `quoted_status` 非 null → **Quote** / それ以外 → **Tweet**。
- 時系列ソートは `created_at` 由来の epoch 秒で行うこと(2010年以前の
  非 snowflake ID が存在するため **ID 順ソートは不可**)。同時刻は ID 数値降順。

## 3. 画像の命名規則

`images/<tweet_id>_<idx>.<ext>`

- `idx` は 1 始まり。対象ツイート本体 → `quoted_status` → `retweeted_status`
  (→ `quoted_tweet` → `retweeted_tweet`)の順に photo エンティティを列挙し、
  URL 重複を除いた順序。
- エンティティ列挙: `entities` が配列ならその要素で `type ∈ {photo, image}`、
  dict なら `entities.media`、加えて `extended_entities.media` / `extendedEntities.media`。
- URL 選択: `preview, media_url_https, media_url, url, link, expanded_url` の
  非空文字列のうち `pbs.twimg.com` を含むものを優先、なければ先頭。
- `<ext>`: pbs.twimg.com の URL はパス末尾の `\.(\w{3,4})$`、なければ
  `format` クエリパラメータ、なければ `jpg`。非 pbs はパス拡張子、なければ `jpg`。
- ダウンロード失敗により実ファイルが存在しない場合がある。ビューアは
  jpg/png/webp/gif/jpeg の拡張子探索とプレースホルダ表示で耐えること。

### 3.5 ユーザーアイコンキャッシュ (`data/icons/`)

- ファイル名: `<sha1(元URL) の小文字hex>.<ext>`(ext は §3 の拡張子規則を元 URL に適用)
- キーは JSONL 中の `profile_image_url` そのまま(`_normal` 付き)。取得時は
  `_normal` → `_bigger` に置換した URL を優先し、404 なら元 URL でフォールバック
- **派生データ**: 消しても再取得できる。全プラットフォームのビューアで共有可

## 4. viewer.db(SQLite)

### 4.1 権威規則(最重要)

| テーブル | 区分 | 意味 |
|---|---|---|
| `tweets`, `tweet_media` | **派生** | tweets.jsonl からいつでも再構築可。手編集禁止 |
| `users`, `read_state` | **正データ** | 破棄禁止。rebuild しても保持すること |
| `tags`, `user_tags` | **正データ** | ユーザーへの独自タグ。JSONL から再構築不能。破棄禁止 |
| `schema_meta` | メタ | `schema_version` を格納 |

`tweet_media` は `tweet_id` 単位で全バケット共有 (複合 PK 化後も tweet_id キーのまま)。
特定 username の派生リセットやバケット削除では、`tweets` の行削除後に
**どこからも参照されなくなった孤児行のみ** 削除すること
(`DELETE FROM tweet_media WHERE tweet_id NOT IN (SELECT tweet_id FROM tweets)`)。

`read_state` は `tweets` への FK を持たない。rebuild で `tweets` を
DELETE しても既読状態は残る。孤児行(対応ツイートが無い read_state)は
無害であり、**削除してはならない**。

### 4.2 DDL(schema_version = 5)

```sql
CREATE TABLE schema_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);

CREATE TABLE users (
  username       TEXT PRIMARY KEY COLLATE NOCASE,  -- @ なしスクリーン名
  display_name   TEXT,
  icon_url       TEXT,                             -- 最新ツイートの profile_image_url
  added_at       TEXT NOT NULL,                    -- ISO 8601 UTC
  last_import_at TEXT,
  jsonl_offset   INTEGER NOT NULL DEFAULT 0        -- 取込済みバイトオフセット
);

CREATE TABLE tweets (
  tweet_id             TEXT NOT NULL,      -- §2 の正準 ID
  id_int               INTEGER NOT NULL,   -- ID の数値表現 (ソートのタイブレーク)
  username             TEXT NOT NULL,      -- アーカイブ所有ユーザーまたはバケット ID (searches/<slug>)
  author_username      TEXT,               -- 実投稿者 (JSONL の user 由来。バケット表示に使う)
  author_display_name  TEXT,
  author_icon_url      TEXT,
  created_at_utc       TEXT NOT NULL,      -- ISO 8601 UTC ("" = パース不能)
  sort_key             INTEGER NOT NULL,   -- epoch 秒 (0 = パース不能 → 最古側)
  tweet_type           INTEGER NOT NULL,   -- 0=tweet 1=RT 2=reply 3=quote (§2 の判定)
  full_text            TEXT NOT NULL,
  lang                 TEXT,
  in_reply_to_username TEXT,
  rt_username          TEXT, rt_display_name TEXT, rt_text TEXT,
  rt_icon_url          TEXT,                -- RT元作者の profile_image_url
  quoted_username      TEXT, quoted_display_name TEXT, quoted_text TEXT,
  quoted_icon_url      TEXT,                -- 引用先ユーザーの profile_image_url
  like_count           INTEGER NOT NULL DEFAULT 0,
  retweet_count        INTEGER NOT NULL DEFAULT 0,
  reply_count          INTEGER NOT NULL DEFAULT 0,
  view_count           INTEGER NOT NULL DEFAULT 0,
  media_count          INTEGER NOT NULL DEFAULT 0,
  raw_offset           INTEGER NOT NULL,   -- tweets.jsonl 内の行先頭バイト位置
  raw_length           INTEGER NOT NULL,   -- 改行を含まない行バイト長
  PRIMARY KEY (username, tweet_id)         -- 同一ツイートがアーカイブとバケット双方に存在し得る
) WITHOUT ROWID;
CREATE INDEX ix_tweets_user_sort ON tweets(username, sort_key DESC, id_int DESC);

CREATE TABLE tweet_media (
  tweet_id   TEXT NOT NULL,
  idx        INTEGER NOT NULL,             -- §3 の 1 始まり index
  source_url TEXT,
  ext        TEXT NOT NULL,                -- §3 の拡張子規則
  origin     INTEGER NOT NULL DEFAULT 0,   -- 0=本文 / 1=引用先 / 2=RT元 (§3 の列挙順に対応)
  PRIMARY KEY (tweet_id, idx)
) WITHOUT ROWID;

CREATE TABLE read_state (
  tweet_id TEXT PRIMARY KEY,
  username TEXT NOT NULL,
  read_at  TEXT NOT NULL                   -- ISO 8601 UTC
) WITHOUT ROWID;
CREATE INDEX ix_read_state_user ON read_state(username);

CREATE TABLE tags (
  tag_id INTEGER PRIMARY KEY,              -- rowid エイリアス
  name   TEXT NOT NULL UNIQUE COLLATE NOCASE
);

CREATE TABLE user_tags (
  username TEXT NOT NULL COLLATE NOCASE,   -- = users.username (FK なし、read_state と同流儀)
  tag_id   INTEGER NOT NULL,
  PRIMARY KEY (username, tag_id)
) WITHOUT ROWID;
CREATE INDEX ix_user_tags_tag ON user_tags(tag_id);
```

- `raw_offset` / `raw_length` により、詳細表示は生 JSONL から該当行を
  シーク読みできる(DB に生 JSON は保存しない)。
- 未読 = `tweets LEFT JOIN read_state` で `read_state.tweet_id IS NULL`。
- スキーマ変更時は `schema_version` を上げる。読み手は自分より新しい
  バージョンの DB を開いてはならない(エラー表示)。
- **マイグレーション規則**: 古いバージョンの DB を開いた実装は、列追加
  (`ALTER TABLE ... ADD COLUMN`)後に派生データ(`tweets`/`tweet_media`)を
  リセットし `jsonl_offset = 0` にして再取込させてよい。正データ
  (`users` の既存行・`read_state`)は必ず保全すること。
  v1 → v2 の差分: `users.icon_url` / `tweets.rt_icon_url` / `tweets.quoted_icon_url` の追加。
  v2 → v3 の差分: `tweet_media.origin` の追加(メディア欄 = `origin=0 AND tweet_type != 1` で抽出)。
  v3 → v4 の差分: `tags` / `user_tags` テーブルの追加のみ。**派生データの
  リセット・再取込は不要**(テーブル追加だけのバージョンアップでは
  リセットしないこと)。
  v4 → v5 の差分: `tweets` に author 3列追加 + 主キーを `(username, tweet_id)` に
  変更(PK 変更のため `tweets` は DROP → CREATE)。派生リセット + `jsonl_offset = 0`。
  正データ保全は従来どおり。
- **検索バケットの users 行**: `username = "searches/<slug>"`。ビューアの
  ユーザー一覧からは `username NOT LIKE 'searches/%'` で除外し「検索」
  セクションに表示する。バケット行の `display_name` / `icon_url` は更新しない
  (§5 参照。表示ラベルは search.json の query)。

## 5. 取込(JSONL → SQLite)の契約

1. `users.jsonl_offset` から `tweets.jsonl` をシークし、**`\n` で完結した
   行のみ**をパース・取込する。末尾の不完全行は取り込まず、オフセットも
   進めない(fetcher の並行追記に対して安全)。
2. 壊れた行・ID の無い行はスキップして数える(取込を止めない)。
3. バッチ(500行程度)ごとに 1 トランザクションで `INSERT OR IGNORE` し、
   **同一トランザクション内で `jsonl_offset` を更新**する(クラッシュ再開安全)。
4. `jsonl_offset > ファイル長` を検出したら JSONL が作り直されたとみなし、
   そのユーザーの `tweets` / `tweet_media` を DELETE・offset=0 で全再取込
   (rebuild)。`read_state` は不触。
5. 手動 rebuild も同じ手順。**`read_state` と `users` は決して消さない**。
6. 取込後の `users.display_name` / `icon_url` の更新 (最新ツイートの user
   オブジェクト由来) は、**検索バケット (`searches/`) では行わない**
   (投稿者がバラバラのため。ラベルは search.json の query を使う)。

## 6. 並行性・その他

- SQLite は **WAL モード必須**。全接続で `busy_timeout`(5000ms 推奨)を設定。
- 書き手は同時に1つ(取込・既読書込はアプリ内で直列化)。読みは並行可。
- `viewer.db` を削除すると既読状態とユーザー登録が失われる(派生データは
  再構築できるが、正データは戻らない)。`-wal` / `-shm` ファイルも DB と
  一体として扱うこと。
- `state.json` は fetcher 私有。ビューアは読み書きしない。
- 同一ユーザーに対する fetcher(CLI/GUI 更新)の同時実行は非推奨
  (JSONL は行単位で整合するが、API リクエストが無駄になる)。
- **データフォルダの場所**: `data/` 一式はポータブル。Windows ビューアでは
  設定(データフォルダ)で任意の場所を指定でき、GUI からの取得は自動的に
  `--output-dir` でその場所へ書き込む。CLI を手動実行する場合は
  `--output-dir` を同じ場所に合わせること。
- **クラウド同期フォルダでの共有**(Google Drive 等): 可能だが書き手は
  常に1台に限ること。PC を切り替える前にビューアを閉じ(WAL チェックポイント
  とフラッシュのため)、同期完了を待ってから他方で開く。
