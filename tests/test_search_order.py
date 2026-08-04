"""検索の並び順 (order) の配線と、期間指定クエリのバックフィル拒否。

docs/trending-jp.md §10.3-1 / §10.3-2 の契約:
- order は /search-tweets の全ページに送られる
- order 変更はカーソルの意味を変えるのでクエリ変更と同じくリセット対象
- order キーの無い既存バケットは "latest" 扱い (リセットしない)
- since:/until: を含むクエリのバックフィルは 30 日窓と競合するので拒否する
"""

import pytest

from sorsa_fetcher.fetcher import (
    DEFAULT_ORDER,
    PeriodQueryBackfillError,
    TweetFetcher,
    has_period_operator,
)
from sorsa_fetcher.storage import Storage

TRENDING_QUERY = (
    "(lang:ja -filter:retweets"
    " since:2026-08-02_15:00:00_UTC until:2026-08-03_15:00:00_UTC) min_faves:50000"
)


class FakeClient:
    """search_tweets の呼び出しを記録する。responses を順に返し、尽きたら終端ページ。"""

    def __init__(self, responses=None):
        self._responses = responses or [{"tweets": [], "next_cursor": None}]
        self.request_count = 0
        self.calls = []

    def search_tweets(self, query, cursor=None, order=DEFAULT_ORDER):
        self.calls.append({"query": query, "cursor": cursor, "order": order})
        index = min(self.request_count, len(self._responses) - 1)
        self.request_count += 1
        return self._responses[index]


def page(ids, next_cursor):
    return {
        "tweets": [{"id": str(i), "full_text": f"t{i}"} for i in ids],
        "next_cursor": next_cursor,
    }


def make_fetcher(tmp_path, client, order=DEFAULT_ORDER):
    storage = Storage(str(tmp_path / "searches"), "trending-jp")
    return TweetFetcher(client, storage, None, order=order), storage


def test_orderは全ページのリクエストに送られる(tmp_path):
    client = FakeClient([page([1], "c1"), page([2], "c2"), page([], None)])
    fetcher, _ = make_fetcher(tmp_path, client, order="popular")

    fetcher.fetch_search("猫")

    assert client.request_count == 3
    assert [c["order"] for c in client.calls] == ["popular"] * 3


def test_既定はlatestで従来どおり(tmp_path):
    client = FakeClient([page([1], None)])
    fetcher, _ = make_fetcher(tmp_path, client)

    fetcher.fetch_search("猫")

    assert client.calls[0]["order"] == "latest"


def test_order変更でカーソルをリセットする(tmp_path):
    fetcher, storage = make_fetcher(tmp_path, FakeClient(), order="latest")
    storage.save_state({
        "query": "猫",
        "order": "latest",
        "search_cursor": "abc",
        "search_done": True,
        "backfill_done_windows": ["2026-01-01..2026-01-31"],
    })

    fetcher2, _ = make_fetcher(tmp_path, FakeClient([page([1], None)]), order="popular")
    fetcher2.fetch_search("猫")

    state = storage.load_state()
    assert state["order"] == "popular"
    assert "backfill_done_windows" not in state
    # リセット後は初回ページングとして走り、終端で search_done が立ち直す
    assert state["search_done"] is True


def test_新規バケットはリセット扱いにしない(tmp_path, caplog):
    """state が空なら消すものが無い。popular 指定でも警告を出さないこと。"""
    client = FakeClient([page([1], None)])
    fetcher, storage = make_fetcher(tmp_path, client, order="popular")

    with caplog.at_level("WARNING"):
        fetcher.fetch_search("猫")

    assert "リセット" not in caplog.text
    assert storage.load_state()["order"] == "popular"


def test_orderキーの無い既存バケットはリセットされない(tmp_path):
    """この変更より前に作られたバケットが初回実行でリセットされないこと。"""
    fetcher, storage = make_fetcher(tmp_path, FakeClient(), order=DEFAULT_ORDER)
    storage.save_state({
        "query": "猫",
        "search_done": True,
        "backfill_done_windows": ["2026-01-01..2026-01-31"],
    })

    client = FakeClient([page([1], None)])
    fetcher2, _ = make_fetcher(tmp_path, client, order=DEFAULT_ORDER)
    fetcher2.fetch_search("猫")

    state = storage.load_state()
    assert state["backfill_done_windows"] == ["2026-01-01..2026-01-31"]
    assert state["order"] == "latest"


@pytest.mark.parametrize("query, expected", [
    (TRENDING_QUERY, True),
    ("猫 since:2026-01-01", True),
    ("猫 until:2026-01-01", True),
    ("猫 since_time:1735689600", True),
    ("猫 UNTIL:2026-01-01", True),
    ("猫 lang:ja min_faves:100", False),
    ("", False),
])
def test_期間演算子の検出(query, expected):
    assert has_period_operator(query) is expected


def test_期間指定クエリのバックフィルは拒否される(tmp_path):
    client = FakeClient()
    fetcher, storage = make_fetcher(tmp_path, client, order="popular")

    with pytest.raises(PeriodQueryBackfillError):
        fetcher.fetch_search(TRENDING_QUERY, backfill=True)

    # 1 リクエストも消費せず、完了済み窓も記録しない
    assert client.request_count == 0
    assert "backfill_done_windows" not in storage.load_state()


def test_期間指定のないクエリのバックフィルは通る(tmp_path):
    client = FakeClient([page([1], None)])
    fetcher, _ = make_fetcher(tmp_path, client)

    fetcher.fetch_search("猫", backfill=True)

    assert client.request_count > 0
    assert any("since:" in c["query"] for c in client.calls)
