"""特定 X(Twitter)ユーザーの全ツイートを Sorsa API で取得して保存する CLI。

使い方:
    python main.py <username> [--output-dir data] [--max-pages N]
                   [--skip-images] [--backfill] [--fresh] [--rps 5]
"""

import argparse
import logging
import os
import sys

from dotenv import load_dotenv

from sorsa_fetcher.client import SorsaClient, SorsaApiError
from sorsa_fetcher.fetcher import RequestBudgetExhausted, TweetFetcher
from sorsa_fetcher.media import MediaDownloader
from sorsa_fetcher.storage import Storage


def main():
    parser = argparse.ArgumentParser(
        description="Sorsa API で特定ユーザーのツイートを全取得し、画像も保存する"
    )
    parser.add_argument("username", help="対象ユーザーのスクリーン名 (@なし)")
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

    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    logger = logging.getLogger("main")

    load_dotenv()
    api_key = os.environ.get("SORSA_API_KEY")
    if not api_key:
        logger.error(".env または環境変数に SORSA_API_KEY を設定してください")
        return 1

    username = args.username.lstrip("@")
    client = SorsaClient(api_key, requests_per_second=args.rps)
    storage = Storage(args.output_dir, username)

    if args.fresh:
        state = storage.load_state()
        state.pop("timeline_cursor", None)
        state.pop("timeline_done", None)
        storage.save_state(state)

    downloader = None if args.skip_images else MediaDownloader(storage.images_dir)
    fetcher = TweetFetcher(client, storage, username,
                           downloader=downloader, max_pages=args.max_pages,
                           max_requests=args.max_requests)

    try:
        if args.update:
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
