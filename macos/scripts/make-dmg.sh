#!/bin/bash
# Packages Deskify.app into a standard, drag-to-install .dmg with an
# /Applications symlink. Builds the app first if it isn't already present.
#
#   ./scripts/make-dmg.sh
#
# Output: build/Deskify-<version>.dmg
set -euo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/config.sh"

[[ -d "$APP_PATH" ]] || "$MACOS_ROOT/scripts/package-app.sh"

STAGING="$BUILD_DIR/dmg-staging"
echo "==> Staging DMG contents…"
rm -rf "$STAGING" "$DMG_PATH"
mkdir -p "$STAGING"
cp -R "$APP_PATH" "$STAGING/"
ln -s /Applications "$STAGING/Applications"      # drag-to-install target

echo "==> Building $(basename "$DMG_PATH")…"
hdiutil create \
    -volname "$APP_NAME" \
    -srcfolder "$STAGING" \
    -ov -format UDZO \
    "$DMG_PATH" >/dev/null
rm -rf "$STAGING"

# A signed DMG is what you distribute; notarization (below) staples to it.
if ! is_adhoc; then
    echo "==> Signing DMG with '$SIGN_IDENTITY'…"
    codesign --force --sign "$SIGN_IDENTITY" "$DMG_PATH"
fi

echo "==> Done: $DMG_PATH"
echo "    For public release, notarize next:  ./scripts/sign-notarize.sh"
