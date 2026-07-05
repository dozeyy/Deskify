#!/bin/bash
# Assembles a production-ready Deskify.app from the SwiftPM release binary:
# builds it, lays out the bundle, installs the icon and Info.plist, then code
# signs (ad-hoc by default, Developer ID + hardened runtime when configured).
#
#   ./scripts/package-app.sh
#
# Output: build/Deskify.app
set -euo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/config.sh"

# 1. Compile (universal Release).
BIN="$("$MACOS_ROOT/scripts/build-release.sh")"

# 2. Icons (skips silently if sips is unavailable — the app still runs).
"$MACOS_ROOT/scripts/make-icons.sh" || echo "warning: icon generation skipped" >&2

# 3. Bundle layout.
echo "==> Assembling $APP_PATH…"
rm -rf "$APP_PATH"
mkdir -p "$APP_PATH/Contents/MacOS" "$APP_PATH/Contents/Resources"
cp "$BIN" "$APP_PATH/Contents/MacOS/$APP_NAME"
cp "$MACOS_ROOT/Resources/Info.plist" "$APP_PATH/Contents/Info.plist"
[[ -f "$BUILD_DIR/Deskify.icns" ]] && cp "$BUILD_DIR/Deskify.icns" "$APP_PATH/Contents/Resources/Deskify.icns"
printf 'APPL????' > "$APP_PATH/Contents/PkgInfo"

# Keep the bundle's version in sync with config.sh without a plist editor round-trip.
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $MARKETING_VERSION" "$APP_PATH/Contents/Info.plist" 2>/dev/null || true
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $BUILD_NUMBER" "$APP_PATH/Contents/Info.plist" 2>/dev/null || true
/usr/libexec/PlistBuddy -c "Set :CFBundleIdentifier $BUNDLE_ID" "$APP_PATH/Contents/Info.plist" 2>/dev/null || true

# 4. Sign. Ad-hoc for local use; Developer ID + hardened runtime for release.
ENTITLEMENTS="$MACOS_ROOT/Resources/Deskify.entitlements"
if is_adhoc; then
    echo "==> Signing ad-hoc (local development only)…"
    codesign --force --deep --sign - "$APP_PATH"
    echo "    NOTE: ad-hoc signatures change every rebuild, so macOS may ask you"
    echo "    to re-grant Accessibility after each rebuild. Set SIGN_IDENTITY in"
    echo "    scripts/config.sh to a Developer ID for a stable identity."
else
    echo "==> Signing with '$SIGN_IDENTITY' (hardened runtime)…"
    codesign --force --deep --options runtime \
        --entitlements "$ENTITLEMENTS" \
        --sign "$SIGN_IDENTITY" \
        ${TEAM_ID:+--timestamp} \
        "$APP_PATH"
fi

codesign --verify --deep --strict --verbose=2 "$APP_PATH" || true
echo "==> Done: $APP_PATH"
