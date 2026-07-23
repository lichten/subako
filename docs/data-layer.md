# データ層仕様(プラットフォームフリー)

Windows 版ビューア(`viewer/TweetViewer`)・Python fetcher・将来の Mac / Android 版
ビューアが共有するデータの規約。この文書だけで別プラットフォームの
閲覧ツールを実装できることを目的とする。

## 1. ディレクトリ構成

```
data/
├── viewer.db                 # SQLite (このドキュメントの §4)
└── <username>/               # X のスクリーン名 (@ なし)
    ├── tweets.jsonl          # 生ツイートアーカイブ (正データ、§2)
    ├── state.json            # fetcher 私有 (§6)
    └── images/               # 画像 (§3)
```

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

## 4. viewer.db(SQLite)

### 4.1 権威規則(最重要)

| テーブル | 区分 | 意味 |
|---|---|---|
| `tweets`, `tweet_media` | **派生** | tweets.jsonl からいつでも再構築可。手編集禁止 |
| `users`, `read_state` | **正データ** | 破棄禁止。rebuild しても保持すること |
| `schema_meta` | メタ | `schema_version` を格納 |

`read_state` は `tweets` への FK を持たない。rebuild で `tweets` を
DELETE しても既読状態は残る。孤児行(対応ツイートが無い read_state)は
無害であり、**削除してはならない**。

### 4.2 DDL(schema_version = 1)

```sql
CREATE TABLE schema_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);

CREATE TABLE users (
  username       TEXT PRIMARY KEY COLLATE NOCASE,  -- @ なしスクリーン名
  display_name   TEXT,
  added_at       TEXT NOT NULL,                    -- ISO 8601 UTC
  last_import_at TEXT,
  jsonl_offset   INTEGER NOT NULL DEFAULT 0        -- 取込済みバイトオフセット
);

CREATE TABLE tweets (
  tweet_id             TEXT PRIMARY KEY,   -- §2 の正準 ID
  id_int               INTEGER NOT NULL,   -- ID の数値表現 (ソートのタイブレーク)
  username             TEXT NOT NULL,      -- アーカイブ所有ユーザー (= users.username)
  created_at_utc       TEXT NOT NULL,      -- ISO 8601 UTC ("" = パース不能)
  sort_key             INTEGER NOT NULL,   -- epoch 秒 (0 = パース不能 → 最古側)
  tweet_type           INTEGER NOT NULL,   -- 0=tweet 1=RT 2=reply 3=quote (§2 の判定)
  full_text            TEXT NOT NULL,
  lang                 TEXT,
  in_reply_to_username TEXT,
  rt_username          TEXT, rt_display_name TEXT, rt_text TEXT,
  quoted_username      TEXT, quoted_display_name TEXT, quoted_text TEXT,
  like_count           INTEGER NOT NULL DEFAULT 0,
  retweet_count        INTEGER NOT NULL DEFAULT 0,
  reply_count          INTEGER NOT NULL DEFAULT 0,
  view_count           INTEGER NOT NULL DEFAULT 0,
  media_count          INTEGER NOT NULL DEFAULT 0,
  raw_offset           INTEGER NOT NULL,   -- tweets.jsonl 内の行先頭バイト位置
  raw_length           INTEGER NOT NULL    -- 改行を含まない行バイト長
) WITHOUT ROWID;
CREATE INDEX ix_tweets_user_sort ON tweets(username, sort_key DESC, id_int DESC);

CREATE TABLE tweet_media (
  tweet_id   TEXT NOT NULL,
  idx        INTEGER NOT NULL,             -- §3 の 1 始まり index
  source_url TEXT,
  ext        TEXT NOT NULL,                -- §3 の拡張子規則
  PRIMARY KEY (tweet_id, idx)
) WITHOUT ROWID;

CREATE TABLE read_state (
  tweet_id TEXT PRIMARY KEY,
  username TEXT NOT NULL,
  read_at  TEXT NOT NULL                   -- ISO 8601 UTC
) WITHOUT ROWID;
CREATE INDEX ix_read_state_user ON read_state(username);
```

- `raw_offset` / `raw_length` により、詳細表示は生 JSONL から該当行を
  シーク読みできる(DB に生 JSON は保存しない)。
- 未読 = `tweets LEFT JOIN read_state` で `read_state.tweet_id IS NULL`。
- スキーマ変更時は `schema_version` を上げる。読み手は自分より新しい
  バージョンの DB を開いてはならない(エラー表示)。

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

## 6. 並行性・その他

- SQLite は **WAL モード必須**。全接続で `busy_timeout`(5000ms 推奨)を設定。
- 書き手は同時に1つ(取込・既読書込はアプリ内で直列化)。読みは並行可。
- `viewer.db` を削除すると既読状態とユーザー登録が失われる(派生データは
  再構築できるが、正データは戻らない)。`-wal` / `-shm` ファイルも DB と
  一体として扱うこと。
- `state.json` は fetcher 私有。ビューアは読み書きしない。
- 同一ユーザーに対する fetcher(CLI/GUI 更新)の同時実行は非推奨
  (JSONL は行単位で整合するが、API リクエストが無駄になる)。
