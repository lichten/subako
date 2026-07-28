"""Sorsa API クライアント。認証・スロットリング・リトライを担当する。"""

import logging
import time

import requests

BASE_URL = "https://api.sorsa.io/v3"

logger = logging.getLogger(__name__)


class SorsaApiError(Exception):
    """リトライしても回復しなかった API エラー。"""

    def __init__(self, message, status_code=None, body=None):
        super().__init__(message)
        self.status_code = status_code
        self.body = body


class SorsaClient:
    def __init__(self, api_key, requests_per_second=5.0, max_retries=5, timeout=30):
        self._session = requests.Session()
        self._session.headers.update(
            {"ApiKey": api_key, "Content-Type": "application/json"}
        )
        self._min_interval = 1.0 / requests_per_second if requests_per_second > 0 else 0
        self._max_retries = max_retries
        self._timeout = timeout
        self._last_request_at = 0.0
        self.request_count = 0

    def _throttle(self):
        elapsed = time.monotonic() - self._last_request_at
        if elapsed < self._min_interval:
            time.sleep(self._min_interval - elapsed)
        self._last_request_at = time.monotonic()

    def _post(self, path, payload):
        return self._request("POST", path, json=payload)

    def _get(self, path, params):
        return self._request("GET", path, params=params)

    def _request(self, method, path, json=None, params=None):
        url = BASE_URL + path
        last_error = None
        for attempt in range(self._max_retries + 1):
            self._throttle()
            try:
                resp = self._session.request(
                    method, url, json=json, params=params, timeout=self._timeout
                )
                self.request_count += 1
            except requests.RequestException as exc:
                last_error = exc
                wait = min(2 ** attempt, 60)
                logger.warning("%s へのリクエスト失敗 (%s)。%d秒後にリトライします", path, exc, wait)
                time.sleep(wait)
                continue

            if resp.status_code == 200:
                return resp.json()

            if resp.status_code == 429 or resp.status_code >= 500:
                last_error = SorsaApiError(
                    f"{path} が HTTP {resp.status_code} を返しました",
                    status_code=resp.status_code,
                    body=resp.text[:500],
                )
                wait = min(2 ** attempt, 60)
                logger.warning(
                    "%s が HTTP %d。%d秒後にリトライします", path, resp.status_code, wait
                )
                time.sleep(wait)
                continue

            # 4xx (429以外) はリトライしても無駄なので即時エラー
            raise SorsaApiError(
                f"{path} が HTTP {resp.status_code} を返しました: {resp.text[:500]}",
                status_code=resp.status_code,
                body=resp.text[:500],
            )

        raise SorsaApiError(f"{path} がリトライ上限に達しました: {last_error}")

    def user_info(self, username):
        return self._get("/info", {"username": username})

    def follows(self, username, cursor=None):
        """指定ユーザーがフォロー中のアカウント一覧 (1ページ最大200件)。"""
        params = {"username": username}
        if cursor:
            params["next_cursor"] = cursor
        return self._get("/follows", params)

    def user_tweets(self, username, cursor=None):
        payload = {"username": username}
        if cursor:
            payload["next_cursor"] = cursor
        return self._post("/user-tweets", payload)

    def search_tweets(self, query, cursor=None, order="latest"):
        payload = {"query": query, "order": order}
        if cursor:
            payload["next_cursor"] = cursor
        return self._post("/search-tweets", payload)
