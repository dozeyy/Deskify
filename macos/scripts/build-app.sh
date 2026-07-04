#!/bin/bash
# Builds Deskify.app — a universal (Apple Silicon + Intel) macOS app bundle.
#
# Usage:   ./scripts/build-app.sh [--debug]
# Output:  build/Deskify.app
#
# Requires Xcode (or the Command Line Tools) on macOS 13+.
set -euo pipefail

cd "$(dirname "$0")/.."

CONFIG=release
if [[ "${1:-}" == "--debug" ]]; then CONFIG=debug; fi

echo "==> Building Deskify ($CONFIG, universal)…"
swift build -c "$CONFIG" --arch arm64 --arch x86_64

BIN=".build/apple/Products/$(tr '[:lower:]' '[:upper:]' <<< "${CONFIG:0:1}")${CONFIG:1}/Deskify"
if [[ ! -f "$BIN" ]]; then
    # Older SwiftPM layouts
    BIN=".build/$CONFIG/Deskify"
fi

APP="build/Deskify.app"
echo "==> Assembling $APP…"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/Deskify"
cp Resources/Info.plist "$APP/Contents/Info.plist"

# App icon: build an .icns from the shared Deskify mark (extracted from the
# Windows deskify.ico so both platforms carry the same brand mark).
if command -v sips >/dev/null && command -v iconutil >/dev/null; then
    echo "==> Building icon…"
    ICONSET="build/Deskify.iconset"
    rm -rf "$ICONSET" && mkdir -p "$ICONSET"
    SRC="Resources/deskify-icon.png"
    for SIZE in 16 32 64 128 256; do
        sips -z "$SIZE" "$SIZE" "$SRC" --out "$ICONSET/icon_${SIZE}x${SIZE}.png" >/dev/null
        DOUBLE=$((SIZE * 2))
        sips -z "$DOUBLE" "$DOUBLE" "$SRC" --out "$ICONSET/icon_${SIZE}x${SIZE}@2x.png" >/dev/null
    done
    iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/Deskify.icns"
    rm -rf "$ICONSET"
fi

# Ad-hoc signature so Gatekeeper and the Accessibility permission system have
# a stable identity for the app. Replace with a Developer ID for distribution.
echo "==> Signing (ad-hoc)…"
codesign --force --deep --sign - "$APP"

echo "==> Done: $APP"
echo "    First run: grant Accessibility access when prompted"
echo "    (System Settings → Privacy & Security → Accessibility)."
