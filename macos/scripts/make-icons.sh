#!/bin/bash
# Generates every macOS app-icon size from the single master PNG
# (Resources/deskify-icon.png, the shared Deskify mark extracted from the
# Windows deskify.ico). Produces BOTH:
#   * Resources/Assets.xcassets/AppIcon.appiconset/*.png  (for the Xcode build)
#   * build/Deskify.icns                                  (for the SPM/manual .app)
#
# Run once on a Mac (needs sips + iconutil, both bundled with macOS). Safe to
# re-run; the build scripts call it automatically.
set -euo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/config.sh"

if [[ ! -f "$ICON_MASTER" ]]; then
    echo "error: master icon not found at $ICON_MASTER" >&2
    exit 1
fi
if ! command -v sips >/dev/null || ! command -v iconutil >/dev/null; then
    echo "error: sips/iconutil not found — run this on macOS." >&2
    exit 1
fi

APPICONSET="$MACOS_ROOT/Resources/Assets.xcassets/AppIcon.appiconset"
ICONSET="$BUILD_DIR/Deskify.iconset"
mkdir -p "$APPICONSET" "$ICONSET"

# size:filename pairs for the asset catalog (matches AppIcon.appiconset/Contents.json)
render() { sips -s format png -z "$1" "$1" "$ICON_MASTER" --out "$2" >/dev/null; }

echo "==> Rendering app-icon sizes from $(basename "$ICON_MASTER")…"
render 16   "$APPICONSET/icon_16.png"
render 32   "$APPICONSET/icon_16@2x.png"
render 32   "$APPICONSET/icon_32.png"
render 64   "$APPICONSET/icon_32@2x.png"
render 128  "$APPICONSET/icon_128.png"
render 256  "$APPICONSET/icon_128@2x.png"
render 256  "$APPICONSET/icon_256.png"
render 512  "$APPICONSET/icon_256@2x.png"
render 512  "$APPICONSET/icon_512.png"
render 1024 "$APPICONSET/icon_512@2x.png"

# .icns for the command-line/SPM bundle (iconutil expects the classic names).
render 16   "$ICONSET/icon_16x16.png"
render 32   "$ICONSET/icon_16x16@2x.png"
render 32   "$ICONSET/icon_32x32.png"
render 64   "$ICONSET/icon_32x32@2x.png"
render 128  "$ICONSET/icon_128x128.png"
render 256  "$ICONSET/icon_128x128@2x.png"
render 256  "$ICONSET/icon_256x256.png"
render 512  "$ICONSET/icon_256x256@2x.png"
render 512  "$ICONSET/icon_512x512.png"
render 1024 "$ICONSET/icon_512x512@2x.png"
iconutil -c icns "$ICONSET" -o "$BUILD_DIR/Deskify.icns"
rm -rf "$ICONSET"

echo "==> Icons written to Assets.xcassets and build/Deskify.icns"
