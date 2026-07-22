"""ツイートからの画像 URL 抽出とダウンロード。

Sorsa の Tweet オブジェクトのメディア表現はドキュメント上
`entities` 配列の {"type": "photo", "link": ..., "preview": ...} とされるが、
実レスポンスでの揺れに備えて dict 形式の entities / extended_entities にも対応する。
"""

import logging
import re
from urllib.parse import urlparse, parse_qs

import requests

from .storage import tweet_id_of

logger = logging.getLogger(__name__)

_PHOTO_TYPES = {"photo", "image"}
_URL_KEYS = ("preview", "media_url_https", "media_url", "url", "link", "expanded_url")


def _iter_media_entries(tweet):
    entities = tweet.get("entities")
    if isinstance(entities, list):
        for entry in entities:
            if isinstance(entry, dict) and entry.get("type") in _PHOTO_TYPES:
                yield entry
    elif isinstance(entities, dict):
        for entry in entities.get("media") or []:
            if isinstance(entry, dict) and entry.get("type") in _PHOTO_TYPES:
                yield entry
    for key in ("extended_entities", "extendedEntities"):
        ext = tweet.get(key)
        if isinstance(ext, dict):
            for entry in ext.get("media") or []:
                if isinstance(entry, dict) and entry.get("type") in _PHOTO_TYPES:
                    yield entry


def _pick_image_url(entry):
    """メディアエントリから pbs.twimg.com の画像 URL を優先して選ぶ。"""
    candidates = [entry.get(k) for k in _URL_KEYS if isinstance(entry.get(k), str) and entry.get(k)]
    for url in candidates:
        if "pbs.twimg.com" in url:
            return url
    return candidates[0] if candidates else None


def extract_photo_urls(tweet):
    """ツイート本体と引用・リツイート先から photo の URL を重複なしで集める。"""
    urls = []
    seen = set()
    targets = [tweet]
    for key in ("quoted_status", "retweeted_status", "quoted_tweet", "retweeted_tweet"):
        nested = tweet.get(key)
        if isinstance(nested, dict):
            targets.append(nested)
    for target in targets:
        for entry in _iter_media_entries(target):
            url = _pick_image_url(entry)
            if url and url not in seen:
                seen.add(url)
                urls.append(url)
    return urls


def to_original_size(url):
    """pbs.twimg.com の URL をフルサイズ(name=orig)の URL と拡張子に正規化する。"""
    parsed = urlparse(url)
    if "pbs.twimg.com" not in parsed.netloc:
        ext = (re.search(r"\.(\w{3,4})$", parsed.path) or [None, "jpg"])[1]
        return url, ext
    query = parse_qs(parsed.query)
    ext_match = re.search(r"\.(\w{3,4})$", parsed.path)
    if ext_match:
        ext = ext_match.group(1)
        path = parsed.path[: -len(ext) - 1]
    else:
        ext = (query.get("format") or ["jpg"])[0]
        path = parsed.path
    full_url = f"https://{parsed.netloc}{path}?format={ext}&name=orig"
    return full_url, ext


class MediaDownloader:
    def __init__(self, images_dir, timeout=60):
        self.images_dir = images_dir
        self._session = requests.Session()
        self._timeout = timeout
        self.downloaded = 0
        self.skipped = 0
        self.failed = []

    def download_for_tweet(self, tweet):
        tid = tweet_id_of(tweet)
        if tid is None:
            return
        for index, url in enumerate(extract_photo_urls(tweet), 1):
            full_url, ext = to_original_size(url)
            dest = self.images_dir / f"{tid}_{index}.{ext}"
            if dest.exists():
                self.skipped += 1
                continue
            try:
                resp = self._session.get(full_url, timeout=self._timeout)
                if resp.status_code == 404 and full_url != url:
                    # orig 変換が通らない URL は元の URL で再試行
                    resp = self._session.get(url, timeout=self._timeout)
                resp.raise_for_status()
                dest.write_bytes(resp.content)
                self.downloaded += 1
            except requests.RequestException as exc:
                logger.warning("画像取得失敗 tweet=%s url=%s (%s)", tid, full_url, exc)
                self.failed.append({"tweet_id": tid, "url": full_url, "error": str(exc)})
