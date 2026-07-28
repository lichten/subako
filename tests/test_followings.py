"""/follows のページング終了条件のテスト (標準ライブラリの unittest のみ)。

実行: python -m unittest discover -s tests

このテストが存在する理由: /follows は終端を next_cursor="0" (文字列) で表すため
falsy 判定では止まらず、しかも "0" を送ると 1 ページ目が返り続ける。実 API での
確認をフォロー数の多いアカウントだけで行うと終端に到達せず見逃す。
"""
import logging
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from main import collect_followings, is_terminal_cursor          # noqa: E402
from sorsa_fetcher.fetcher import RequestBudgetExhausted          # noqa: E402

logging.disable(logging.CRITICAL)


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


class IsTerminalCursorTests(unittest.TestCase):
    def test_終端とみなす値(self):
        for value in (None, "", "  ", "0", "-1"):
            self.assertTrue(is_terminal_cursor(value), repr(value))

    def test_継続する値(self):
        for value in ("DAAHCgABHN3ukRE", "1", "00", "0abc"):
            self.assertFalse(is_terminal_cursor(value), repr(value))


class CollectFollowingsTests(unittest.TestCase):
    def test_文字列ゼロのカーソルで終端とみなす(self):
        # 実測された @Lichten18 の挙動: 39 人 + next_cursor="0" が返り続ける
        client = FakeClient([page(["a", "b"], "0")])
        users = []

        collect_followings(client, "src", users)

        self.assertEqual(["a", "b"], [u["username"] for u in users])
        self.assertEqual(1, client.request_count)

    def test_カーソルが無い場合も終端(self):
        client = FakeClient([page(["a"], None)])
        users = []

        collect_followings(client, "src", users)

        self.assertEqual(1, client.request_count)

    def test_複数ページを連結する(self):
        client = FakeClient([
            page(["a", "b"], "c1"),
            page(["c"], "0"),
        ])
        users = []

        collect_followings(client, "src", users)

        self.assertEqual(["a", "b", "c"], [u["username"] for u in users])
        self.assertEqual([None, "c1"], client.cursors)

    def test_重複は除去する(self):
        client = FakeClient([
            page(["a", "b"], "c1"),
            page(["b", "c"], "0"),
        ])
        users = []

        collect_followings(client, "src", users)

        self.assertEqual(["a", "b", "c"], [u["username"] for u in users])

    def test_新規0件のページで打ち切る(self):
        # 番兵値を知らなくても抜けられること (本命の安全網)。
        # カーソルは毎回変わるので終端判定にもカーソル同一判定にも引っかからない
        client = FakeClient([
            page(["a"], "c1"),
            page(["a"], "c2"),
            page(["a"], "c3"),
        ])
        users = []

        collect_followings(client, "src", users)

        self.assertEqual(["a"], [u["username"] for u in users])
        self.assertEqual(2, client.request_count)

    def test_空ページで打ち切る(self):
        client = FakeClient([
            page(["a"], "c1"),
            page([], "c2"),
        ])
        users = []

        collect_followings(client, "src", users)

        self.assertEqual(2, client.request_count)

    def test_カーソルが前回と同じなら打ち切る(self):
        # 新規は増え続けるがカーソルが進まないケース
        client = FakeClient([
            page(["a"], "same"),
            page(["b"], "same"),
            page(["c"], "same"),
        ])
        users = []

        collect_followings(client, "src", users)

        self.assertEqual(["a", "b"], [u["username"] for u in users])
        self.assertEqual(2, client.request_count)

    def test_上限に達したら例外だが取得済みは残る(self):
        client = FakeClient([
            page(["a"], "c1"),
            page(["b"], "c2"),
            page(["c"], "c3"),
        ])
        users = []

        with self.assertRaises(RequestBudgetExhausted):
            collect_followings(client, "src", users, max_requests=2)

        self.assertEqual(["a", "b"], [u["username"] for u in users])
        self.assertEqual(2, client.request_count)


if __name__ == "__main__":
    unittest.main()
