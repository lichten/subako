"""/follows のページング終了条件のテスト。

実行: pytest  (依存は requirements-dev.txt)

このテストが存在する理由: /follows は終端を next_cursor="0" (文字列) で表すため
falsy 判定では止まらず、しかも "0" を送ると 1 ページ目が返り続ける。実 API での
確認をフォロー数の多いアカウントだけで行うと終端に到達せず見逃す。
"""
import pytest

from main import collect_followings, is_terminal_cursor
from sorsa_fetcher.fetcher import RequestBudgetExhausted


class FakeClient:
    """responses を順に返す。足りなくなったら最後の応答を返し続ける
    (同じページを返し続ける API の挙動の再現)。"""

    def __init__(self, responses):
        self._responses = responses
        self.request_count = 0
        self.cursors = []

    def follows(self, username, cursor=None):
        self.cursors.append(cursor)
        index = min(self.request_count, len(self._responses) - 1)
        self.request_count += 1
        return self._responses[index]


def page(usernames, next_cursor):
    return {
        "users": [{"id": u, "username": u} for u in usernames],
        "next_cursor": next_cursor,
    }


def collect(responses, max_requests=None):
    """(取得できた username, 使ったクライアント) を返す。"""
    client = FakeClient(responses)
    users = []
    collect_followings(client, "src", users, max_requests)
    return [u["username"] for u in users], client


@pytest.mark.parametrize("value", [None, "", "  ", "0", "-1"])
def test_終端とみなすカーソル(value):
    assert is_terminal_cursor(value)


@pytest.mark.parametrize("value", ["DAAHCgABHN3ukRE", "1", "00", "0abc"])
def test_継続するカーソル(value):
    assert not is_terminal_cursor(value)


def test_文字列ゼロのカーソルで終端とみなす():
    # 実測された @Lichten18 の挙動: 39 人 + next_cursor="0" が返り続ける
    names, client = collect([page(["a", "b"], "0")])

    assert names == ["a", "b"]
    assert client.request_count == 1


def test_カーソルが無い場合も終端():
    _, client = collect([page(["a"], None)])

    assert client.request_count == 1


def test_複数ページを連結する():
    names, client = collect([
        page(["a", "b"], "c1"),
        page(["c"], "0"),
    ])

    assert names == ["a", "b", "c"]
    assert client.cursors == [None, "c1"]


def test_重複は除去する():
    names, _ = collect([
        page(["a", "b"], "c1"),
        page(["b", "c"], "0"),
    ])

    assert names == ["a", "b", "c"]


def test_新規0件のページで打ち切る():
    # 番兵値を知らなくても抜けられること (本命の安全網)。
    # カーソルは毎回変わるので終端判定にもカーソル同一判定にも引っかからない
    names, client = collect([
        page(["a"], "c1"),
        page(["a"], "c2"),
        page(["a"], "c3"),
    ])

    assert names == ["a"]
    assert client.request_count == 2


def test_空ページで打ち切る():
    _, client = collect([
        page(["a"], "c1"),
        page([], "c2"),
    ])

    assert client.request_count == 2


def test_カーソルが前回と同じなら打ち切る():
    # 新規は増え続けるがカーソルが進まないケース
    names, client = collect([
        page(["a"], "same"),
        page(["b"], "same"),
        page(["c"], "same"),
    ])

    assert names == ["a", "b"]
    assert client.request_count == 2


def test_上限に達したら例外だが取得済みは残る():
    client = FakeClient([
        page(["a"], "c1"),
        page(["b"], "c2"),
        page(["c"], "c3"),
    ])
    users = []

    with pytest.raises(RequestBudgetExhausted):
        collect_followings(client, "src", users, max_requests=2)

    assert [u["username"] for u in users] == ["a", "b"]
    assert client.request_count == 2
