#!/bin/bash
# SubakoApp (SPM executable) から Subako.app バンドルを組み立てる。
# 使い方: mac/Scripts/make-app.sh [出力ディレクトリ]  (既定: mac/dist)
set -euo pipefail

cd "$(dirname "$0")/.."
OUT="${1:-dist}"
VERSION="0.1.0"

swift build -c release

BIN=$(swift build -c release --show-bin-path)/SubakoApp
APP="$OUT/Subako.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

cp "$BIN" "$APP/Contents/MacOS/Subako"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key><string>Subako</string>
    <key>CFBundleIdentifier</key><string>dev.lichten.subako</string>
    <key>CFBundleName</key><string>Subako</string>
    <key>CFBundleDisplayName</key><string>Subako</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleShortVersionString</key><string>${VERSION}</string>
    <key>CFBundleVersion</key><string>${VERSION}</string>
    <key>LSMinimumSystemVersion</key><string>15.0</string>
    <key>NSHighResolutionCapable</key><true/>
    <key>NSHumanReadableCopyright</key><string>Copyright (c) 2026 Lichten (MIT License)</string>
</dict>
</plist>
PLIST

# ad-hoc 署名 (配布時は Developer ID + notarize に置き換える)
codesign --force --sign - "$APP"

echo "created: $APP"
