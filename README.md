# Sorsa API ツイート全取得ツール

[Sorsa API](https://api.sorsa.io) を使って、特定の X(Twitter)ユーザーのツイートを全期間分取得し、
JSONL とツイート添付画像をローカルに保存するツールです。

## セットアップ

```powershell
python -m venv .venv
.venv\Scripts\Activate.ps1
pip install -r requirements.txt
copy .env.example .env
# .env を開いて SORSA_API_KEY に実際のキーを設定
```

API キーは https://api.sorsa.io のダッシュボードから取得できます(新規アカウントに 100 リクエストの無料枠あり)。

## 使い方

### 1. 疎通確認(推奨: 最初に実行)

実レスポンスの構造(メディアフィールドの形、リツイート/リプライの含まれ方)を確認します。
2 リクエストだけ消費します。

```powershell
python probe.py <username>
```

生レスポンスが `probe_output/` に保存されます。

### 2. 試験実行

```powershell
python main.py <username> --max-pages 2
```

### 3. 本番実行(全件取得)

```powershell
python main.py <username>
```

### オプション

| オプション | 説明 |
|---|---|
| `--output-dir DIR` | 保存先(既定: `data`) |
| `--max-pages N` | タイムライン取得の最大ページ数(試験用。1ページ≒20件) |
| `--skip-images` | 画像をダウンロードしない |
| `--backfill` | タイムラインで遡り切れなかった期間を `search-tweets` の期間分割検索で補完 |
| `--fresh` | 進捗(state.json)を無視して最初からページングし直す(重複保存はされない) |
| `--rps N` | リクエストレート上限(既定 5、Sorsa の上限は 20) |

## 出力

```
data/<username>/
├── tweets.jsonl   # API が返した Tweet JSON をそのまま 1 行 1 件で保存
├── images/        # <tweet_id>_<連番>.<拡張子> (フルサイズ name=orig で取得)
└── state.json     # 進捗(カーソル・処理済みウィンドウ・画像失敗リスト)
```

- 途中で中断(Ctrl+C やエラー)しても、再実行すれば `state.json` と既存 `tweets.jsonl` の ID を見て続きから再開します。
- リツイート・リプライも保存対象です。リツイートは `retweeted_status` に元ツイートを含み、引用・リツイート先の画像も取得します。

## 制限・注意

- 課金はリクエスト単位(Pro プランで $0.00199/リクエスト、1 ページ≒20 件なので約 $0.10/1,000 ツイート)。大きなアカウントを取得する前に無料枠で試してください。
- Sorsa は `/user-tweets` で最初の投稿まで遡及可能(公式 X API の 3,200 件制限なし)としていますが、実際の取得件数はプロフィール上のツイート数より少なくなることがあります(削除済みツイート・非公開期間の分は取得できません)。実行終了時に完全性チェックのログが出ます。
- 遡りが途中で止まった場合は `--backfill` を付けて再実行すると、`from:<username> since:... until:...` の月単位検索で補完を試みます。
