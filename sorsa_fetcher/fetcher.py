"""全ツイート取得ロジック。

主軸は /user-tweets のカーソルページング(Sorsa は 3,200 件制限なしで
最初の投稿まで遡及可能としている)。実際に途中で終端した場合の補完として、
/search-tweets を期間分割でページングする backfill を提供する。
"""

import logging
from datetime import datetime, timedelta, timezone

logger = logging.getLogger(__name__)

# 1ウィンドウでこの件数以上取れて終端した場合は検索キャップを疑い分割する
_WINDOW_SUSPECT_COUNT = 400
_MIN_WINDOW_DAYS = 2

_DATE_FORMATS = (
    "%a %b %d %H:%M:%S %z %Y",   # 旧Twitter形式: Wed Oct 10 20:19:24 +0000 2018
    "%Y-%m-%dT%H:%M:%S%z",
    "%Y-%m-%dT%H:%M:%S.%f%z",
    "%Y-%m-%d %H:%M:%S%z",
)


def parse_created_at(value):
    if not value:
        return None
    if isinstance(value, (int, float)):
        return datetime.fromtimestamp(value, tz=timezone.utc)
    text = str(value).strip().replace("Z", "+0000")
    for fmt in _DATE_FORMATS:
        try:
            return datetime.strptime(text, fmt)
        except ValueError:
            continue
    try:
        return datetime.fromisoformat(str(value).replace("Z", "+00:00"))
    except ValueError:
        return None


class TweetFetcher:
    def __init__(self, client, storage, username, downloader=None, max_pages=None):
        self.client = client
        self.storage = storage
        self.username = username
        self.downloader = downloader
        self.max_pages = max_pages
        self.total_new = 0

    def _handle_page(self, tweets):
        new_tweets = self.storage.append_tweets(tweets)
        self.total_new += len(new_tweets)
        if self.downloader:
            for tweet in new_tweets:
                self.downloader.download_for_tweet(tweet)
        return new_tweets

    # ---- フェーズ1: user-tweets タイムライン全取得 ----

    def fetch_timeline(self):
        state = self.storage.load_state()
        cursor = state.get("timeline_cursor")
        if state.get("timeline_done"):
            logger.info("タイムライン取得は完了済みです(state.json)。スキップします")
            return
        page = 0
        while True:
            if self.max_pages is not None and page >= self.max_pages:
                logger.info("--max-pages 上限 (%d) に達したので中断します", self.max_pages)
                break
            resp = self.client.user_tweets(self.username, cursor=cursor)
            tweets = resp.get("tweets") or []
            page += 1
            self._handle_page(tweets)
            cursor = resp.get("next_cursor")
            logger.info(
                "[timeline] page=%d 取得=%d 累計新規=%d 保存済み=%d",
                page, len(tweets), self.total_new, len(self.storage.seen_ids),
            )
            state["timeline_cursor"] = cursor
            self.storage.save_state(state)
            if not cursor or not tweets:
                state["timeline_done"] = True
                self.storage.save_state(state)
                logger.info("タイムラインの終端に到達しました")
                break

    # ---- 完全性チェック ----

    def report_completeness(self):
        try:
            info = self.client.user_info(self.username)
        except Exception as exc:
            logger.warning("user-info の取得に失敗しました: %s", exc)
            return None
        user = info.get("user") if isinstance(info.get("user"), dict) else info
        statuses_count = None
        for key in ("statuses_count", "tweets_count", "tweet_count"):
            if user.get(key) is not None:
                statuses_count = user[key]
                break
        created_at = parse_created_at(user.get("created_at") or user.get("createdAt"))
        oldest = self.oldest_saved_datetime()
        logger.info(
            "完全性チェック: 保存済み=%d件 / プロフィール上のツイート数=%s / "
            "最古の保存ツイート=%s / アカウント作成日=%s",
            len(self.storage.seen_ids),
            statuses_count,
            oldest.isoformat() if oldest else "不明",
            created_at.isoformat() if created_at else "不明",
        )
        logger.info("(削除済みツイートの分だけ保存件数が少なくなるのは正常です)")
        return {"statuses_count": statuses_count, "account_created_at": created_at}

    def oldest_saved_datetime(self):
        oldest = None
        for tweet in self.storage.iter_tweets():
            dt = parse_created_at(tweet.get("created_at") or tweet.get("createdAt"))
            if dt and (oldest is None or dt < oldest):
                oldest = dt
        return oldest

    # ---- フェーズ2 (フォールバック): search-tweets 期間分割 ----

    def backfill(self, account_created_at=None):
        oldest = self.oldest_saved_datetime()
        end = oldest or datetime.now(timezone.utc)
        start = account_created_at or datetime(2006, 3, 21, tzinfo=timezone.utc)
        if start >= end:
            logger.info("backfill 対象期間がありません")
            return
        state = self.storage.load_state()
        done_windows = set(state.get("backfill_done_windows") or [])
        logger.info(
            "backfill: %s 〜 %s を月単位で検索します",
            start.date(), end.date(),
        )
        # 新しい側から過去へ月単位で遡る
        window_end = end
        while window_end > start:
            window_start = max(window_end - timedelta(days=30), start)
            self._backfill_window(window_start, window_end, done_windows, state)
            window_end = window_start

    def _window_key(self, start, end):
        return f"{start.date()}..{end.date()}"

    def _backfill_window(self, start, end, done_windows, state):
        key = self._window_key(start, end)
        if key in done_windows:
            return
        query = (
            f"from:{self.username} "
            f"since:{start.date()} until:{end.date()} include:nativeretweets"
        )
        cursor = None
        window_count = 0
        while True:
            resp = self.client.search_tweets(query, cursor=cursor)
            tweets = resp.get("tweets") or []
            window_count += len(tweets)
            self._handle_page(tweets)
            cursor = resp.get("next_cursor")
            if not cursor or not tweets:
                break
        logger.info("[backfill] %s: %d件 (累計新規=%d)", key, window_count, self.total_new)
        # 大量に取れて終端した場合は検索側のキャップを疑い、期間を半分に割って再取得
        span = end - start
        if window_count >= _WINDOW_SUSPECT_COUNT and span.days > _MIN_WINDOW_DAYS:
            middle = start + span / 2
            logger.info("[backfill] %s は取得数が多いため分割して再確認します", key)
            self._backfill_window(start, middle, done_windows, state)
            self._backfill_window(middle, end, done_windows, state)
        done_windows.add(key)
        state["backfill_done_windows"] = sorted(done_windows)
        self.storage.save_state(state)
