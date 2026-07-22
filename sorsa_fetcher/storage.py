"""JSONL 追記・既存 ID 読込・進捗 state の管理。"""

import json
import logging
from pathlib import Path

logger = logging.getLogger(__name__)


def tweet_id_of(tweet):
    """Tweet オブジェクトから ID を文字列で取り出す(スキーマ差異に耐える)。"""
    for key in ("id_str", "id", "tweet_id"):
        value = tweet.get(key)
        if value is not None:
            return str(value)
    return None


class Storage:
    def __init__(self, output_dir, username):
        self.base_dir = Path(output_dir) / username
        self.images_dir = self.base_dir / "images"
        self.tweets_path = self.base_dir / "tweets.jsonl"
        self.state_path = self.base_dir / "state.json"
        self.base_dir.mkdir(parents=True, exist_ok=True)
        self.images_dir.mkdir(parents=True, exist_ok=True)
        self.seen_ids = self._load_seen_ids()

    def _load_seen_ids(self):
        seen = set()
        if not self.tweets_path.exists():
            return seen
        with self.tweets_path.open("r", encoding="utf-8") as f:
            for line_no, line in enumerate(f, 1):
                line = line.strip()
                if not line:
                    continue
                try:
                    tid = tweet_id_of(json.loads(line))
                except json.JSONDecodeError:
                    logger.warning("tweets.jsonl の %d 行目が壊れています。スキップします", line_no)
                    continue
                if tid:
                    seen.add(tid)
        logger.info("既存ツイート %d 件を読み込みました", len(seen))
        return seen

    def append_tweets(self, tweets):
        """未保存のツイートだけを JSONL に追記し、新規追加分のリストを返す。"""
        new_tweets = []
        with self.tweets_path.open("a", encoding="utf-8") as f:
            for tweet in tweets:
                tid = tweet_id_of(tweet)
                if tid is None or tid in self.seen_ids:
                    continue
                f.write(json.dumps(tweet, ensure_ascii=False) + "\n")
                self.seen_ids.add(tid)
                new_tweets.append(tweet)
        return new_tweets

    def iter_tweets(self):
        if not self.tweets_path.exists():
            return
        with self.tweets_path.open("r", encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                try:
                    yield json.loads(line)
                except json.JSONDecodeError:
                    continue

    def load_state(self):
        if self.state_path.exists():
            try:
                return json.loads(self.state_path.read_text(encoding="utf-8"))
            except json.JSONDecodeError:
                logger.warning("state.json が壊れています。初期状態から始めます")
        return {}

    def save_state(self, state):
        self.state_path.write_text(
            json.dumps(state, ensure_ascii=False, indent=2), encoding="utf-8"
        )
