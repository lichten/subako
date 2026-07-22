"""疎通確認用スクリプト。1〜2リクエストだけ投げて実レスポンスの構造を確認する。

使い方:
    python probe.py <username>

実行すると user-info と user-tweets(1ページ) の生レスポンスを
probe_output/ に保存し、メディアフィールドの形を標準出力に表示する。
"""

import json
import logging
import os
import sys
from pathlib import Path

from dotenv import load_dotenv

from sorsa_fetcher.client import SorsaClient
from sorsa_fetcher.media import extract_photo_urls
from sorsa_fetcher.storage import tweet_id_of


def main():
    if len(sys.argv) < 2:
        print("使い方: python probe.py <username>")
        return 1
    username = sys.argv[1].lstrip("@")

    logging.basicConfig(level=logging.INFO, format="%(levelname)s %(message)s")
    load_dotenv()
    api_key = os.environ.get("SORSA_API_KEY")
    if not api_key:
        print("SORSA_API_KEY を .env に設定してください")
        return 1

    out_dir = Path("probe_output")
    out_dir.mkdir(exist_ok=True)
    client = SorsaClient(api_key)

    info = client.user_info(username)
    (out_dir / "user_info.json").write_text(
        json.dumps(info, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"--- user-info -> {out_dir / 'user_info.json'}")

    resp = client.user_tweets(username)
    (out_dir / "user_tweets_page1.json").write_text(
        json.dumps(resp, ensure_ascii=False, indent=2), encoding="utf-8")
    tweets = resp.get("tweets") or []
    print(f"--- user-tweets page1 -> {out_dir / 'user_tweets_page1.json'}")
    print(f"取得件数: {len(tweets)} / next_cursor: {bool(resp.get('next_cursor'))}")

    for tweet in tweets:
        urls = extract_photo_urls(tweet)
        flags = []
        if tweet.get("retweeted_status") or tweet.get("retweeted_tweet"):
            flags.append("RT")
        if tweet.get("is_reply") or tweet.get("in_reply_to_tweet_id"):
            flags.append("Reply")
        print(f"  id={tweet_id_of(tweet)} {'/'.join(flags) or 'Tweet'} 画像={len(urls)}")
        for u in urls:
            print(f"    {u}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
