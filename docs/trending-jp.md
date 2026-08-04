# 「今日、日本で話題のツイート」の抽出設計

Sorsa API で「その日、日本語圏で話題になっているツイート」を取り出すための操作設計。
本文書は**調査結果と運用手順**であり、fetcher / ビューアの実装契約ではない
(組み込む場合の注意は §7)。API の基礎は [fetcher-cli.md](fetcher-cli.md) §6、
検索バケットの規約は [data-layer.md](data-layer.md) §1.5。

数値はすべて **2026-08-04 に実 API で実測**した値 (§9)。

## 1. 結論 — 推奨する操作

`POST /v3/search-tweets` を **`order: "popular"`** で 1 本引き、`next_cursor` で
終端まで辿る。検索語は使わず**演算子だけ**でクエリを組む。

```json
{
  "query": "lang:ja min_faves:50000 -filter:retweets since:2026-08-02_15:00:00_UTC until:2026-08-03_15:00:00_UTC",
  "order": "popular"
}
```

- 検索語なし (演算子のみ) のクエリは **HTTP 200 で正常に通る** — 実測で確認済み。
- `order` の既定は API 側では `"popular"` (X 検索の「話題」タブ相当) だが、
  Subako の `sorsa_fetcher/client.py` は `"latest"` を既定にしているため、
  **この用途では明示指定が必須**。
- 1 ページ 20 件。丸一日 + `min_faves:50000` で **84 件 / 6 ページ**が実測値
  (6 ページ目で `next_cursor` が消え、きれいに終端する)。

## 2. クエリの組み立て

| 要素 | 値 | 理由 |
|---|---|---|
| `lang:ja` | 固定 | 日本語ツイートに限定。X 検索で「日本」を切る唯一の実用手段 (地域指定の `near:` は座標付きツイートが 1〜2% しか無く使えない) |
| `min_faves:N` | 可変 (§3) | 「話題」の閾値。`view_count` に相当する演算子は無いのでいいね数で代替する |
| `-filter:retweets` | 固定 | 同一内容の RT が上位を埋めるのを防ぐ |
| `since:` / `until:` | §4 | JST 当日の境界 |
| `-filter:replies` | 任意 | ノイズは減るが、伸びたリプライ (大喜利など) も落ちる。既定では付けない |

- `min_retweets:` / `min_replies:` / `from:` / `-語` も併用できる
  (演算子一覧は [data-layer.md](data-layer.md) §1.5)。
- 演算子は 1 クエリ **22〜23 個が上限**。超過分は**黙って無視される**ので、
  条件を足しすぎないこと。
- `min_faves:` は**厳密に効く**: `min_faves:20000` の全 398 件で最小いいね数は
  20,028、閾値未満は 0 件だった。

## 3. 閾値のキャリブレーション

日本語圏の母数は大きい。**丸一日 (JST 2026-08-03) の実測**:

| `min_faves` | 件数 | ページ数 | 終端 |
|---|---|---|---|
| 20,000 | **398 件以上** | 20 ページで打ち切り | 未到達 |
| 50,000 | 84 件 | 6 | `next_cursor` 無し |
| 100,000 | 15 件 | 2 | `next_cursor` 無し |
| 200,000 | 2 件 | 2 | `next_cursor` 無し |

上位 20〜100 件に収めたいなら **丸一日で `min_faves:50000`〜`100000`** が当たり。
`10000` や `5000` は「その日の話題」ではなく「日本語の人気ツイート一般」になる。

### 3.1 当日進行中は閾値を大きく下げる

いいね数は**クエリ時点の値**なので、投稿からの経過時間が短いほど小さい。
同じ `min_faves:10000` でも:

- JST 2026-08-04 の 00:00〜10:40 (10.7 時間経過) → **28 件**で終端
- JST 2026-08-03 の丸一日 (投稿から 24 時間以上経過) → **140 件以上**

したがって、

- **前日分を翌日に取る**運用: 閾値 50,000〜100,000。件数が安定し再現性がある。**推奨**。
- **当日進行中に取る**運用: 閾値は 1/5〜1/10 (5,000〜10,000) に下げる。ただし
  取得時刻によって件数が大きく変わり、朝の取得では夕方以降のツイートが入らない。

同じ日を時間差で 2 回取ると、いいね数の増えたツイートが後から閾値を超えて入ってくる。
JSONL は ID で重複排除されるため**後から入った分だけが追記**され、既存行のいいね数は
更新されない ([data-layer.md](data-layer.md) §2 は追記専用)。数値の鮮度が要るなら
バケットを作り直すこと。

## 4. 期間境界 (JST)

`since:` / `until:` の日付形式は UTC 基準なので、JST の暦日を取るには
**`YYYY-MM-DD_HH:MM:SS_UTC` 形式**を使う (JST 00:00 = 前日 15:00 UTC)。

```
JST 2026-08-03 の丸一日:
  since:2026-08-02_15:00:00_UTC until:2026-08-03_15:00:00_UTC
JST 2026-08-04 の 0 時から現在まで:
  since:2026-08-03_15:00:00_UTC        (until 省略)
```

- `until:YYYY-MM-DD` の日付形式は **exclusive** (その日を含まない)。
  `_HH:MM:SS_UTC` 形式も同様に上端を含まない半開区間として扱ってよい
  (ビューアの期間フィルタ `[from, to)` と同じ流儀 — [viewer-features.md](viewer-features.md) §7.3)。
- 「直近 24 時間」でよければ `within_time:24h` の方が単純 (境界計算が不要)。
  ただし暦日ではないので、日次で積み上げる運用には向かない。
- ローカル 0 時 → epoch の変換はビューア側に既にある
  (`mac/Sources/SubakoCore/Database/DateRangeFilter.swift`)。表示側で当日に
  絞り直す場合はこれを再利用する。

## 5. ページングと停止条件

- 封筒は他エンドポイントと同じ `{"tweets": [...], "next_cursor": "..."}`。
- 停止条件は既存 `sorsa_fetcher/fetcher.py` の規則をそのまま使える:
  1. `next_cursor` が空 → 終端
  2. 空ページが 3 連続 (`_MAX_CONSECUTIVE_EMPTY_PAGES`) → 終端とみなす
  3. 既知 ID のみのページ → 差分取得として終了
- **この規模ではページングは安定**。`min_faves:20000` で 20 ページ (398 件) 辿って
  **重複ゼロ**、終端付近では 1 ページ 8 件や 19 件の端数も正常に返る。
- ただし数万件規模の広いクエリでは、単一カーソルは 70 ページ前後から重複を返し
  90 ページ前後で進まなくなることが知られている。**深追いが必要になる閾値設定は
  そもそも「話題」の抽出になっていない** (§3) ので、10 ページ程度を上限にしてよい。

## 6. コスト

Pro プラン $0.00199/リクエスト、リトライも 1 リクエストとして数える
([fetcher-cli.md](fetcher-cli.md) §6)。

| 操作 | リクエスト | 費用 |
|---|---|---|
| 前日分を `min_faves:50000` で全件 (84 件) | 6 | 約 $0.012 |
| 閾値の当たりを取り直す試行 | +1〜2 | 約 $0.004 |
| **1 日あたり合計** | **7〜8** | **約 $0.016** |

毎日回して月 240 リクエスト / 約 $0.5。Starter の月間割当 10,100 リクエストに対して
無視できる。§8 のトレンド起点方式に切り替えると 1 日 50〜100 リクエストになる。

## 7. Subako への組み込み

1. **`order` の配線 — 実装済み。** `main.py` に `--order latest|popular` があり、
   省略時は `search.json` の `order` キー、それも無ければ `latest` にフォールバックする。
   `--order` を明示すると `search.json` に保存され、以後どのプラットフォームから
   更新しても引き継がれる。`order` はカーソルの意味を変えるため、`state.json` の
   `query` と同様に**不一致検知でカーソルをリセットする**
   (`order` キーが無い既存バケットは `latest` とみなすのでリセットされない)。
   参照: [fetcher-cli.md](fetcher-cli.md) §2、[data-layer.md](data-layer.md) §1.5。
2. **`--backfill` とは併用不可 — fetcher が拒否する。** クエリ自体に `since:`/`until:`
   (および `since_time:`/`until_time:`) を含む場合、30 日窓バックフィルと競合するため
   **exit 1 で落ちる**。期間を絞った検索は 1 回の取得で完結するのでバックフィルは不要。
3. **バケットは 1 つに固定する。** クエリが日替わりになるので slug も日替わりに
   なってしまう。`--search-name trending-jp` を固定して `--search` のクエリだけ
   差し替えれば、slug は不変 ID なので同一バケット `searches/trending-jp` に
   日々積み上がる。fetcher は query 変更を検知して `search_cursor` / `search_done`
   をリセットするため、毎日「初回ページング」として走る — この用途では望ましい挙動。
4. **並び順は API の popular 順のまま保存する。** ローカル再ランキングはしない。
   `tweets` のインデックスは `ix_tweets_user_sort` 1 本だけで、エンゲージメント順
   ソートには新インデックスと keyset カーソルのスコア版が要る
   ([data-layer.md](data-layer.md) §4.2)。`schema_version` の bump も不要のまま済む。
   なお **popular 順は「いいね数の降順」ではない** — X 自身の関連度順で、
   実測でも 27,714 → 14,974 → 10,904 → 13,426 のように前後する。
5. JSONL のいいね数キーは **`likes_count`**、DB 列は `like_count`
   ([data-layer.md](data-layer.md) §2)。

## 8. フォールバック — `/trends` 起点の 2 段構え

閾値方式で拾えない話題 (いいねは伸びないが投稿数が多いハッシュタグ等) を取りたい
場合は、X 自身のトレンド判定を使う。

1. `GET /v3/trends?woeid=23424856` (日本。東京は `1118370`)。
   レスポンスは **`{"trends": [{name, query, url}, ...]}`** で **50 件**返る
   (配列直接ではない)。`query` は URL エンコード済みなので**デコードしてから**
   検索に渡す。ページングも履歴モードも無い。
2. 各トレンド語について `POST /search-tweets` を `order:"popular"` + `lang:ja` +
   §4 の `since:` で 1 ページずつ。
3. `tweet_id` で重複排除。

精度は高いが 1 回あたり 51〜101 リクエスト (≒ $0.1〜0.2/日) と桁が変わる。
トレンドは数分で入れ替わるので、結果は 5〜10 分キャッシュする。

## 9. 実測の再現手順

リポジトリ直下 (`.env` に `SORSA_API_KEY`) で:

```bash
set -a; . ./.env; set +a
curl -s https://api.sorsa.io/v3/search-tweets \
  -H "ApiKey: $SORSA_API_KEY" -H 'Content-Type: application/json' \
  -d '{"query":"lang:ja min_faves:50000 -filter:retweets since:2026-08-02_15:00:00_UTC until:2026-08-03_15:00:00_UTC","order":"popular"}' \
  | python3 -m json.tool | head -60
```

確認済みの事実 (2026-08-04 実測):

| 項目 | 結果 |
|---|---|
| 演算子のみのクエリ | HTTP 200、正常に結果を返す |
| `lang:ja` | 返却ツイートの `lang` はすべて `ja` |
| `min_faves:N` | 厳密に効く (398 件中、閾値未満 0 件) |
| `since:` の JST 境界 | 返却ツイートの `created_at` がすべて指定窓内 |
| `order` の効果 | `popular` と `latest` で同一クエリの 1 ページ目が 20 件中 15 件一致 / 5 件相違。`latest` は厳密な時刻降順、`popular` は関連度順 |
| `next_cursor` | 返る。終端で消える |
| `/trends?woeid=23424856` | HTTP 200、`{"trends": [...]}` で 50 件 |

同一ツイートのいいね数が数十秒差の 2 リクエストで 27,714 → 27,759 と変化した。
**カウントはライブ値**であり、取得時刻に依存する (§3.1)。

## 10. クロスプラットフォーム影響 (Mac 版に実装した場合)

`viewer.db` / `tweets.jsonl` / `search.json` は Windows 版 (C#)・Mac 版 (Swift)・
Python fetcher の 3 者が共有するため、片方だけの機能追加が他方に波及しうる。
本章は「この機能を Mac 版に実装したとき Windows 版に何が起きるか」の調査結果。

結論: **§10.4 のルールを守れば実害は無い**。守らない場合、`schema_version` の
変更だけは **Windows 版が起動できなくなる**一発アウトになる。

### 10.1 影響が出ないもの (共有データ層はそのまま通る)

| 項目 | 根拠 |
|---|---|
| バケットが Windows のサイドバーにも出る | 起動時の自動登録の条件は「そのディレクトリ直下に `tweets.jsonl` があるか」だけ (`viewer/TweetViewer/Data/UserRepository.cs` `RegisterExistingSearchDirsAsync`)。`searches/<slug>` 規約に従う限り Windows 側の追加コードは不要 |
| `search.json` の**未知キーが保持される** | C# `Data/SearchMetadata.cs` / Swift `Files/SearchMetadata.swift` / Python `main.py` の 3 実装すべてが read-modify-write。`order` キーを置ける |
| JSONL の未知フィールドが無視される | 両パーサとも型ガード付きのキー引き (`TweetJsonParser.cs` / `JSONValue.swift`) で、厳格デコードではない |
| クエリ文字列が壊れない | Windows は `ProcessStartInfo.ArgumentList` 渡し (`Services/FetchProcessService.cs`)。シェルを介さないので `since:2026-08-02_15:00:00_UTC` / `-filter:retweets` / 括弧 / 引用符はそのまま fetcher に届く |
| 既読・タグが共有される | `read_state` は `tweet_id` 単位、`user_tags` は `username` (= バケット ID) 単位 ([data-layer.md](data-layer.md) §4.1) |

### 10.2 一発で壊れるもの

**`schema_version` を上げると Windows 版が起動できなくなる。**
両実装とも「自分の対応版より新しい DB は開かない」ガードを持つ
(`viewer/TweetViewer/Data/ViewerDatabase.cs` / `mac/Sources/SubakoCore/Database/ViewerDatabase.swift`。
定数はどちらも 7)。挙動は**非対称**:

- **Mac**: 例外を catch して「viewer.db を開けません」アラート + 設定を開くボタン。
  アプリは生き残る (`Views/MainWindow.swift`)。
- **Windows**: `App.xaml.cs` の `EnsureCreated()` に try/catch が無い。
  `DispatcherUnhandledException` に落ちて汎用の「予期しないエラーが発生したため
  終了します」ダイアログ → `Shutdown(1)`。**アプリごと終了する。**

さらに C# は版チェックの**前**に `PRAGMA journal_mode=WAL` と DDL 一式を実行している
(判定はその後)。将来 `tweets` の列を削除・改名する版を作ると、Windows 側は
分かりやすいメッセージではなく生の `no such column` エラーになる。Swift は同じ
トランザクション内で判定するのでロールバックされる。

→ **§7-4 の「popular 順のまま保存し、ローカル再ランキングをしない」方針は、
そのままこの事故を回避する条件**になっている。エンゲージメント順ソートを
入れたくなったら、Windows 版も同時に更新して配布しない限りやってはいけない。

同時起動は元から不可 ([mac-port-notes.md](mac-port-notes.md) §3: クラウド同期下では
`viewer.db` / `-wal` / `-shm` が不整合になる瞬間がある)。トレンド取得を足しても
この前提は変わらないが、「Mac で毎日取得する」運用にするなら **Windows を閉じてから**。
加えて Windows は終了時に `wal_checkpoint` を実行しないため、Mac の閲覧専用モード
(`mode=ro&immutable=1`) は WAL を無視して**古いスナップショットを黙って読む**。

### 10.3 静かにずれるもの (データは壊れないが挙動が変わる)

1. **`order` が latest に戻る — 対策済み。** Windows の右クリック「更新 (差分取得)」と
   「表示中をすべて更新」(`MainWindow.xaml.cs`) は `search.json` の query をそのまま
   `--search` に渡す。`--order` を知らないので、素朴に実装すると popular バケットに
   latest の結果が混ざっていた。
   → **対策 (実装済み)**: `order` を `search.json` に持たせ、`main.py` が `--order`
   未指定時に `search.json` の `order` を採用する。fetcher が読むので **Windows 側の
   コードは 1 行も変えずに popular が維持される**。
   ```json
   {
     "query": "lang:ja min_faves:50000 -filter:retweets since:...",
     "name": "今日の話題 (日本)",
     "order": "popular",
     "created_at": "..."
   }
   ```
   - `--order` の**既定は `"latest"` のまま**にする (既存の全検索の挙動を変えないため)。
   - `order` はカーソルの意味を変えるので、`state.json` の `query` と同様に
     不一致検知でリセットする対象に加える (§7-1)。
   - **`main.py` は Windows の publish 出力に同梱される**
     (`viewer/TweetViewer/TweetViewer.csproj` の `BundleFetcherAndNotices`)。
     Windows 側の `main.py` が古いままだと `order` を読めないので、Python を変えたら
     **Windows 版も再ビルド・再配置が必要**。

2. **バックフィルの since/until 二重付与 — 対策済み。** Windows の
   「過去期間を取得 (バックフィル)」はどのバケットにも無条件で出る。
   `sorsa_fetcher/fetcher.py` が `(query) since:A until:B` を合成するため、既に
   `since:`/`until:` を持つトレンドクエリでは `(q since:X until:Y) since:A until:B` に
   なり、**結果ゼロのまま `backfill_done_windows` に「完了」と記録されていた**。
   → **対策 (実装済み)**: fetcher が期間演算子入りクエリのバックフィルを exit 1 で
   拒否する。ビューア側でメニューを無効化するのはその上の親切機能。§7-2 と同じ。

3. **編集ダイアログの往復でクエリ文字列が変わる。** `SearchQueryOperators` の
   `Split`/`Compose` は `min_faves:` / `min_retweets:` だけを抜き出し、残り全部を
   `(...)` で包み直す (C#/Swift 同一実装)。
   `lang:ja min_faves:50000 -filter:retweets since:...` を編集画面で開いて保存する
   だけで `(lang:ja -filter:retweets since:...) min_faves:50000` に化ける。
   意味は同じだが**文字列が変わる**ため、fetcher が「クエリ変更」と見なして
   `search_cursor` / `search_done` / `backfill_done_windows` をリセットする。
   → 対策: Mac が書くクエリを最初から**正準形**
   `(<その他すべての演算子>) min_retweets:N min_faves:M` にしておけば往復で不変。
   - `-min_faves:5` (否定形) と引用符内の `min_faves:` は**本当に壊れる**
     (`q -min_faves:5` → `(q -) min_faves:5` となり否定が消える)。使わないこと。

4. **一括更新に巻き込まれる。** Windows の「表示中をすべて更新」はトレンドバケットも
   差分更新の対象に含め、合計リクエスト上限を消費する。タグフィルタで表示から外すか、
   上限に織り込むこと。

5. **メモリ上の古いクエリ。** Windows 起動中に Mac が `search.json` を書き換えても、
   Windows は次の一覧再読込 (起動時 / 取得完了後 / 編集後 / 削除後) まで古い query を
   保持する。その状態で「更新」を押すと**旧クエリで取りにいく**。

### 10.4 実装時のルール (Windows を壊さないための条件)

1. **`schema_version` を上げない。`tweets` に列を足さない。ローカル再ランキングをしない。**
2. **`data/` 直下に新しいディレクトリを作らない。** 作るなら**直下に `tweets.jsonl` を
   置かない** (トレンド語のキャッシュ等)。置くと両ビューアがユーザーとして登録する。
   `_` 始まりでも防げない — 自動登録の判定は `tweets.jsonl` の有無だけ (§10.1)。
3. バケットは既存の `searches/<slug>` 規約に完全準拠させる。slug は不変 ID
   ([data-layer.md](data-layer.md) §1.5)。
4. `order` は `search.json` に持たせ、`main.py` がフォールバックで読む。
   `--order` の既定は `"latest"` を維持する。
5. クエリは正準形で書く。`-min_faves:` は使わない。
6. Python を変えたら Windows 版に同梱されている `main.py` も更新する。

### 10.5 併せて見つかった既存の弱点 (本件のスコープ外)

- `App.xaml.cs` の `EnsureCreated()` に try/catch が無く、schema too new が
  クラッシュ扱いになる。Mac 版と同じくアラート表示にすべき。
- C# 側に「新しすぎる `schema_version`」のテストが無い
  (Swift は `mac/Tests/SubakoCoreTests/SchemaMigrationTests.swift` にある)。
- Windows は終了時に `PRAGMA wal_checkpoint(TRUNCATE)` を実行しない (Mac は実行する)。
  クラウド同期下では最大のリスク ([mac-port-notes.md](mac-port-notes.md) §3)。
- **slug の 40 字切詰めが C# だけ単位が違う。** C# は UTF-16 コードユニット
  (`Data/SearchSlug.cs`)、Swift/Python はコードポイント。40 字を超え、かつ非 BMP 文字
  (絵文字等) を含むクエリでは**同じクエリから別のフォルダ名が生成される**。
  トレンドクエリは ASCII 演算子中心なので実害は無いが、共有契約
  ([mac-port-notes.md](mac-port-notes.md) §2 の項目 6) の逸脱として記録しておく。
