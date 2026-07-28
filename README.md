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

## テスト

```powershell
pip install -r requirements-dev.txt
pytest                                  # Python 側 (API は消費しない)
dotnet test viewer\TweetViewer.Tests\TweetViewer.Tests.csproj   # ビューア側
```

`pytest` は偽クライアントを差し込むので API キーもネットワークも不要です。
ビューアが起動中だと exe がロックされて `dotnet test` が失敗するので、先に終了してください。

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
| `--followings` | `username` がフォロー中の一覧を `_followings/<username>.jsonl` に書き出す(ツイートは取得しない。ビューアの一括登録用) |

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
- Sorsa は `/user-tweets` で全期間の取得に対応するとしていますが、実際の取得件数はプロフィール上のツイート数より少なくなることがあります(削除済みツイート・非公開期間の分は取得できません)。実行終了時に完全性チェックのログが出ます。
- 遡りが途中で止まった場合は `--backfill` を付けて再実行すると、`from:<username> since:... until:...` の月単位検索で補完を試みます。

## 免責事項・利用上の注意

- 本ソフトウェアは個人が開発した非公式ツールであり、X Corp. および Sorsa とは
  一切関係ありません。
- Sorsa API の利用 (アカウント作成・API キーの管理・課金・利用規約の遵守) は
  利用者自身の責任で行ってください。
- 取得したツイート・画像は第三者の著作物です。私的利用の範囲に留め、
  取得データの再配布・再公開は行わないでください。
- 本ツールの利用にあたっては X の利用規約およびコンテンツ表示に関する
  ポリシーを利用者自身が確認・遵守してください。
- 本ソフトウェアは無保証です (LICENSE 参照)。利用によって生じたいかなる
  損害についても作者は責任を負いません。

## 配布物の作成 (開発者向け)

配布用の zip・インストーラーは必ず `dotnet publish` の出力フォルダからのみ
作成すること。**リポジトリの作業ツリーを直接 zip しない** — 作業ツリーには
gitignore されているだけの非公開ファイル (`.env` の API キー、`data/` の
取得済みアーカイブ、`probe_output/` の生 API 応答) が存在するため。
