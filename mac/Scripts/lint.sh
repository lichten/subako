#!/bin/sh
# SwiftLint を CI (ci.yml test-mac) と同じ条件で実行する。
# 初回: brew install swiftlint  (CI は 0.65.0 に固定 — それ以上を使うこと)
set -eu
cd "$(dirname "$0")/.."
if ! command -v swiftlint >/dev/null 2>&1; then
    echo "swiftlint が見つかりません: brew install swiftlint" >&2
    exit 1
fi
swiftlint lint --strict
