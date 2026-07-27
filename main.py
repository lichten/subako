"""特定 X(Twitter)ユーザーの全ツイートを Sorsa API で取得して保存する CLI。

使い方:
    python main.py <username> [--output-dir data] [--max-pages N]
                   [--skip-images] [--backfill] [--fresh] [--rps 5]
    python main.py --search "<query>" [--search-name NAME] [--max-requests N]
    python main.py <username> --images-only          # API を使わず画像だけ補完
    python main.py --search-name NAME --images-only  # 検索バケットの画像だけ補完

検索クエリには X 標準の検索演算子がそのまま使える:
    スペース区切り = AND / OR (大文字) / "フレーズ" / (グループ) / -除外 /
    lang:ja / min_faves:N / min_retweets:N / from:user / since: / until:
例: (sts2 OR "slay the spire 2" OR スレスパ2) lang:ja
"""

import argparse
import json
import logging
import os
import sys
from datetime import datetime, timezone

from dotenv import load_dotenv

from sorsa_fetcher.client import SorsaClient, SorsaApiError
from sorsa_fetcher.fetcher import RequestBudgetExhausted, TweetFetcher, slugify_query
from sorsa_fetcher.media import MediaDownloader
from sorsa_fetcher.storage import Storage


def download_missing_images(storage, logger):
    """保存済み JSONL を読み直して、まだ落ちていない画像だけ取得する (API 不使用)。

    通常の取得経路 (fetcher._handle_page) は「JSONL に新規追加されたツイート」しか
    ダウンローダに渡さないため、抽出ロジックの改良で新たに拾えるようになった画像
    (引用先・RT元) は既存アーカイブに対してこの経路で補完する。
    """
    downloader = MediaDownloader(storage.images_dir)
    scanned = 0
    for tweet in storage.iter_tweets():
        scanned += 1
        downloader.download_for_tweet(tweet)
        if scanned % 500 == 0:
            logger.info(
                "走査 %d件 / 新規DL=%d / スキップ(既存)=%d / 失敗=%d",
                scanned, downloader.downloaded, downloader.skipped, len(downloader.failed),
            )
    logger.info(
        "完了: 走査=%d件 / 新規DL=%d / スキップ(既存)=%d / 失敗=%d / 保存先=%s",
        scanned, downloader.downloaded, downloader.skipped,
        len(downloader.failed), storage.images_dir,
    )
    if downloader.failed:
        state = storage.load_state()
        state["failed_images"] = downloader.failed
        storage.save_state(state)
    return 0


def main():
    parser = argparse.ArgumentParser(
        description="Sorsa API で特定ユーザーのツイートを全取得し、画像も保存する"
    )
    parser.add_argument("username", nargs="?", help="対象ユーザーのスクリーン名 (@なし)")
    parser.add_argument("--search", metavar="QUERY",
                        help="X 全体をキーワード検索して <output-dir>/searches/ に保存する "
                             "(username と排他。X 標準の検索演算子が使える)")
    parser.add_argument("--search-name",
                        help="検索の保存フォルダ名 (省略時はクエリから自動生成)")
    parser.add_argument("--output-dir", default="data", help="保存先ディレクトリ (既定: data)")
    parser.add_argument("--max-pages", type=int, default=None,
                        help="タイムライン取得の最大ページ数 (試験実行用)")
    parser.add_argument("--skip-images", action="store_true", help="画像をダウンロードしない")
    parser.add_argument("--images-only", action="store_true",
                        help="API を一切使わず、保存済み JSONL を読み直して未取得の画像だけ"
                             "ダウンロードする (引用先・RT元の画像の後追い補完用)")
    parser.add_argument("--backfill", action="store_true",
                        help="タイムライン取得後に search-tweets の期間分割検索で補完する")
    parser.add_argument("--fresh", action="store_true",
                        help="state.json を無視して最初からページングし直す (既存ツイートの重複保存はしない)")
    parser.add_argument("--update", action="store_true",
                        help="先頭からページングし、ページ全体が既知ツイートになったら停止する差分取得")
    parser.add_argument("--rps", type=float, default=5.0,
                        help="1秒あたりのリクエスト数上限 (既定: 5、Sorsa の上限は 20)")
    parser.add_argument("--max-requests", type=int, default=None,
                        help="この実行で消費する API リクエスト数の上限。到達したら中断し (exit 10)、"
                             "再実行で続きから再開する")
    parser.add_argument("--backfill-since", default="2014-01-01", metavar="YYYY-MM-DD",
                        help="検索バックフィルで過去へ遡る開始日 (既定: 2014-01-01。"
                             "X の検索インデックスは 2014 年以前をほぼ返さない)")
    args = parser.parse_args()

    if args.images_only:
        if bool(args.username) == bool(args.search_name):
            parser.error(
                "--images-only は username か --search-name のどちらか一方を指定してください")
    elif bool(args.username) == bool(args.search):
        parser.error("username か --search のどちらか一方を指定してください")
    try:
        backfill_since = datetime.strptime(
            args.backfill_since, "%Y-%m-%d").replace(tzinfo=timezone.utc)
    except ValueError:
        parser.error("--backfill-since は YYYY-MM-DD 形式で指定してください")

    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    logger = logging.getLogger("main")

    if args.images_only:
        if args.search_name:
            storage = Storage(os.path.join(args.output_dir, "searches"), args.search_name)
        else:
            storage = Storage(args.output_dir, args.username.lstrip("@"))
        return download_missing_images(storage, logger)

    load_dotenv()
    api_key = os.environ.get("SORSA_API_KEY")
    if not api_key:
        logger.error(".env または環境変数に SORSA_API_KEY を設定してください")
        return 1

    client = SorsaClient(api_key, requests_per_second=args.rps)

    if args.search:
        if args.fresh:
            logger.warning("--search 指定時は --fresh を無視します")
        name = args.search_name or slugify_query(args.search)
        storage = Storage(os.path.join(args.output_dir, "searches"), name)
        # クエリ原文をプラットフォーム共通メタデータとして保存 (ビューアも読み書きする)。
        # 既存とクエリが異なる場合は「取得済みを残したままのクエリ変更」として
        # query キーだけ差し替える (name / created_at 等の他キーは保持)
        meta_path = storage.base_dir / "search.json"
        meta = {}
        if meta_path.exists():
            try:
                meta = json.loads(meta_path.read_text(encoding="utf-8"))
            except json.JSONDecodeError:
                logger.warning("search.json が壊れているため作り直します")
                meta = {}
        saved_query = meta.get("query")
        if saved_query is not None and saved_query != args.search:
            logger.warning(
                "検索クエリを変更します: %r → %r (取得済みツイートは保持)",
                saved_query, args.search,
            )
        meta["query"] = args.search
        meta.setdefault("created_at", datetime.now(timezone.utc).isoformat())
        meta_path.write_text(
            json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8")
    else:
        storage = Storage(args.output_dir, args.username.lstrip("@"))

    if args.fresh and not args.search:
        state = storage.load_state()
        state.pop("timeline_cursor", None)
        state.pop("timeline_done", None)
        storage.save_state(state)

    downloader = None if args.skip_images else MediaDownloader(storage.images_dir)
    fetcher = TweetFetcher(client, storage,
                           None if args.search else args.username.lstrip("@"),
                           downloader=downloader, max_pages=args.max_pages,
                           max_requests=args.max_requests)

    try:
        if args.search:
            if args.update and args.backfill:
                logger.warning("--update 指定時は --backfill を無視します")
            fetcher.fetch_search(
                args.search, update=args.update,
                backfill=args.backfill and not args.update,
                backfill_since=backfill_since)
        elif args.update:
            if args.fresh or args.backfill:
                logger.warning("--update 指定時は --fresh / --backfill を無視します")
            fetcher.fetch_timeline_update()
        else:
            fetcher.fetch_timeline()
            report = fetcher.report_completeness()
            if args.backfill:
                created_at = report.get("account_created_at") if report else None
                fetcher.backfill(account_created_at=created_at)
    except RequestBudgetExhausted:
        logger.warning(
            "リクエスト上限 %d に達したため中断しました。再実行すると続きから再開します",
            args.max_requests,
        )
        return 10
    except SorsaApiError as exc:
        logger.error("API エラーで中断しました: %s", exc)
        logger.error("再実行すれば途中から再開できます")
        return 1
    except KeyboardInterrupt:
        logger.warning("中断されました。再実行すれば途中から再開できます")
        return 130
    finally:
        if downloader:
            logger.info(
                "画像: 新規DL=%d / スキップ(既存)=%d / 失敗=%d",
                downloader.downloaded, downloader.skipped, len(downloader.failed),
            )
            if downloader.failed:
                state = storage.load_state()
                state["failed_images"] = downloader.failed
                storage.save_state(state)
        logger.info(
            "完了: 新規保存=%d件 / 総保存=%d件 / APIリクエスト=%d回 / 保存先=%s",
            fetcher.total_new, len(storage.seen_ids),
            client.request_count, storage.base_dir,
        )
    return 0


if __name__ == "__main__":
    sys.exit(main())
