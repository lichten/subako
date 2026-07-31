#!/bin/bash
# アプリケーションアイコン (Resources/Subako.icns) を再生成する。
# 図形の定義は Sources/subako-icongen — Windows 版 (tools/icongen) と同一の比率にすること。
# 使い方: mac/Scripts/make-icon.sh
set -euo pipefail

cd "$(dirname "$0")/.."
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

swift run subako-icongen "$WORK"
mkdir -p Resources
iconutil -c icns "$WORK/Subako.iconset" -o Resources/Subako.icns

echo "created: $(pwd)/Resources/Subako.icns"
