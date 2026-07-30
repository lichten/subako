# fetcher CLI 契約と Sorsa API 概要

ビューアが Python fetcher (`main.py` + `sorsa_fetcher/`) を子プロセスとして起動する際の契約。
別プラットフォームのビューアが取得機能を持つ場合、この契約を守れば Windows 版と同じ
fetcher をそのまま使える。データ側の規約(JSONL・state.json・画像命名)は
[data-layer.md](data-layer.md)。

## 1. 前提

- 依存は Python 3 + `requests` + `python-dotenv` のみ(`requirements.txt`)。
  OS 固有 API は使っていないため **macOS / Linux でもそのまま動く**。
- API キーは fetcher のフォルダ(リポジトリルート)の `.env` に `SORSA_API_KEY=...`。
  未設定なら exit 1。
- ログは Python の logging なので **stderr** に出る。ビューアは stdout / stderr の両方を
  捕捉すること。Windows 版は文字化け防止に子プロセスへ `PYTHONIOENCODING=utf-8` と
  `PYTHONUTF8=1` を設定して UTF-8 で読む(macOS では既定 UTF-8 のため通常不要)。
- 作業ディレクトリは fetcher のフォルダ(`.env` の解決のため)。
- 中断はプロセスツリーごと kill してよい(ページ単位で保存済み。§3 の exit 130 は
  Ctrl+C の場合で、kill では完了ログも出ない)。

## 2. コマンドライン契約

ビューアの 7 つの取得モードと生成する引数(Windows 実装 `Services/FetchProcessService.cs`):

| モード | 引数 | UI 上の入口 |
|---|---|---|
| Update | `main.py <user> --output-dir <dir> --update` | 「更新」ボタン / 右クリック更新 / 表示中をすべて更新 |
| Backfill | `main.py <user> --output-dir <dir> --backfill` | 右クリック「全期間を取得 (バックフィル)...」 |
| Search | `main.py --output-dir <dir> --search "<query>" --search-name <slug>` | 検索の新規保存 |
| SearchUpdate | Search と同じ + `--update` | 検索行「更新 (差分取得)...」 |
| SearchBackfill | Search と同じ + `--backfill [--backfill-since YYYY-MM-DD]` | 検索行「過去期間を取得...」 |
| ImagesOnly | `main.py <user> --images-only` または `--search-name <slug> --images-only`(+ `--output-dir`) | 「不足画像を取得 (API 不使用)」 |
| Followings | `main.py <user> --output-dir <dir> --followings` | 「フォロー中を一括登録」 |

- **`--output-dir` は常に明示する**(既定は `data` 相対パスのため、共有データフォルダ運用では
  必須)。ビューアの設定にあるデータフォルダをそのまま渡す。
- 上限を課すときは末尾に `--max-requests N`。
- `--search-name <slug>` は**常に明示する**(slug はバケットの不変 ID。クエリ変更後に
  slug を再計算すると別バケットになってしまう。data-layer.md §1.5)。

このほか CLI 手動実行用のオプションがある(ビューアは使わない):
`--max-pages N`(ページ数上限)/ `--skip-images`(画像 DL しない)/
`--fresh`(カーソルを無視して最初から)/ `--rps F`(リクエストレート、既定 5.0)。

## 3. exit code 契約

| exit code | 意味 | ビューアの扱い |
|---|---|---|
| 0 | 正常完了 | 「取得完了」。ただしユーザー操作による中断(kill)は exit code に関係なく「中断しました(途中までの取得分は保存済み)」として扱う |
| 1 | API エラー / `SORSA_API_KEY` 未設定 / 不正なユーザー名 | 「エラー終了」+ ログ自動表示 |
| 10 | `--max-requests` 到達(RequestBudgetExhausted) | モード別の案内: タイムライン/検索/バックフィルは「もう一度実行すると続きから再開」。**フォロー一覧のみ再開不可**(上限を増やして最初から) |
| 130 | KeyboardInterrupt(Ctrl+C) | 中断扱い |

上限到達 (10) が「安全な中断」である根拠: 取得はページ単位で JSONL に追記され、
再開カーソルは `state.json` に保存済みのため(フォロー一覧はカーソルを保存しない
設計 — data-layer.md §1.7)。

## 4. 完了ログ書式の契約

ツイート取得とフォロー一覧取得は、終了時(`finally`)に次の 1 行を必ず出す:

```
完了: 新規保存=%d件 / 総保存=%d件 / APIリクエスト=%d回 / 保存先=%s
```

- **`APIリクエスト=(\d+)回` が機械可読の契約**。ビューアの「表示中をすべて更新」は
  この値で合計リクエスト上限の残量を管理する(Windows 実装 `Services/FetchBudget.cs`)。
- 複数回出た場合は**最後の行**を採用。読み取れなかった実行は
  「割り当て分を全部使った」とみなす(安全側に倒す)。
- `finally` で出すため、上限到達・API エラーでも出る。出ないのはプロセスを kill
  したときだけ(そのときも安全側の解釈で辻褄が合う)。
- `--images-only` は API を使わないため書式が異なる
  (`完了: 走査=N件 / 新規DL=N / スキップ(既存)=N / 失敗=N / 保存先=...`)。
  リクエスト残量の管理対象にしないこと。

## 5. state.json(参考)

fetcher 私有のファイルで、**ビューアは読み書きしない**(data-layer.md §6)。
デバッグ時の参考としてキー一覧:

| キー | 内容 |
|---|---|
| `timeline_cursor` / `timeline_done` | タイムライン全ページングの再開カーソル / 完走フラグ(終端でカーソルは `""`) |
| `search_cursor` / `search_done` / `query` | 検索バケット用。`query` が実行クエリと不一致だと検索系進捗を自動リセット |
| `backfill_done_windows` | 完了済みバックフィル窓の配列(`"YYYY-MM-DD..YYYY-MM-DD"` 形式、30 日窓) |
| `failed_images` | 画像 DL 失敗の記録(`{tweet_id, url, error}` の配列) |

## 6. Sorsa API 概要

`sorsa_fetcher/client.py`。`BASE_URL = https://api.sorsa.io/v3`、認証はヘッダ
`ApiKey: <key>`。

| メソッド | HTTP | パス | 用途 |
|---|---|---|---|
| `user_info(username)` | GET | `/info` | プロフィール(`statuses_count` 等)。完全性チェック用 |
| `follows(username, cursor)` | GET | `/follows` | フォロー中一覧(1 ページ最大 200 件)。終端カーソルは `"0"`(文字列)— data-layer.md §1.7 の罠を参照 |
| `user_tweets(username, cursor)` | POST | `/user-tweets` | ユーザータイムライン(1 ページ約 20 件) |
| `search_tweets(query, cursor, order)` | POST | `/search-tweets` | キーワード検索(`order="latest"`) |

- レスポンスのトップレベルは `{"tweets": [...], "next_cursor": "..."}`(user-tweets 実測)。
  ツイートオブジェクトのフィールドは data-layer.md §2。
- リトライ: 429 と 5xx は指数バックオフ(最大 60 秒、5 回)。それ以外の 4xx は即エラー。
  リトライも 1 リクエストとして数える。スロットルは既定 5 rps(Sorsa 上限 20)。
- **実挙動の制約**(実測):
  - `/user-tweets` は全期間を返さず **850 件前後で終端**することがある
    (プロフィールの `statuses_count` より少ない)。不足分は検索バックフィルで補う設計。
  - `/search-tweets` は **2014 年より前をほぼ返さない**。
  - 入れ子の `quoted_status.entities` / `retweeted_status.entities` は常に空配列
    (data-layer.md §2)。
- 費用の目安(README): Pro プランで $0.00199/リクエスト、1 ページ ≒ 20 件
  → 約 $0.10 / 1,000 ツイート。新規アカウントには 100 リクエストの無料枠。
- 参考: Starter プランの月間割当は 10,100 リクエスト(実運用時の確認値。
  最新のプラン内容は [Sorsa のダッシュボード](https://api.sorsa.io) を参照)。
