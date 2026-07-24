"""特定 X(Twitter)ユーザーの全ツイートを Sorsa API で取得して保存する CLI。

使い方:
    python main.py <username> [--output-dir data] [--max-pages N]
                   [--skip-images] [--backfill] [--fresh] [--rps 5]
    python main.py --search "<query>" [--search-name NAME] [--max-requests N]

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
    args = parser.parse_args()

    if bool(args.username) == bool(args.search):
        parser.error("username か --search のどちらか一方を指定してください")

    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    logger = logging.getLogger("main")

    load_dotenv()
    api_key = os.environ.get("SORSA_API_KEY")
    if not api_key:
        logger.error(".env または環境変数に SORSA_API_KEY を設定してください")
        return 1

    client = SorsaClient(api_key, requests_per_second=args.rps)

    if args.search:
        if args.fresh or args.backfill or args.update:
            logger.warning("--search 指定時は --update / --fresh / --backfill を無視します")
        name = args.search_name or slugify_query(args.search)
        storage = Storage(os.path.join(args.output_dir, "searches"), name)
        # クエリ原文をプラットフォーム共通メタデータとして保存 (ビューアが読む)
        meta_path = storage.base_dir / "search.json"
        if meta_path.exists():
            try:
                saved_query = json.loads(meta_path.read_text(encoding="utf-8")).get("query")
            except json.JSONDecodeError:
                saved_query = None
            if saved_query is not None and saved_query != args.search:
                logger.error(
                    "保存フォルダ %s は別のクエリ %r で使用中です。--search-name で別名を指定してください",
                    storage.base_dir, saved_query,
                )
                return 1
        else:
            meta_path.write_text(
                json.dumps(
                    {"query": args.search,
                     "created_at": datetime.now(timezone.utc).isoformat()},
                    ensure_ascii=False, indent=2),
                encoding="utf-8")
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
            fetcher.fetch_search(args.search)
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
