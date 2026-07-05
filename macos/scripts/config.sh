#!/bin/bash
# Central configuration for the Deskify build & distribution scripts.
# Edit the PLACEHOLDER values to sign and notarize for public distribution.
# Every value can also be overridden from the environment, e.g.
#   TEAM_ID=ABCDE12345 ./scripts/sign-notarize.sh

# --- Identity ---------------------------------------------------------------

# App metadata
export APP_NAME="${APP_NAME:-Deskify}"
export BUNDLE_ID="${BUNDLE_ID:-me.willhenry.deskify}"   # PLACEHOLDER — your reverse-DNS id
export MARKETING_VERSION="${MARKETING_VERSION:-0.1.0}"
export BUILD_NUMBER="${BUILD_NUMBER:-1}"

# --- Code signing -----------------------------------------------------------
# Local development uses ad-hoc signing ("-") so the project builds with no
# Apple account. For distribution, set these to your Developer ID details.

# "-" = ad-hoc (local only). For release:
#   "Developer ID Application: Your Name (TEAMID)"
export SIGN_IDENTITY="${SIGN_IDENTITY:--}"              # PLACEHOLDER for release

# Your 10-character Apple Developer Team ID (required for notarization).
export TEAM_ID="${TEAM_ID:-}"                           # PLACEHOLDER, e.g. ABCDE12345

# Notarization credentials. Recommended: store an app-specific password once in
# the keychain and reference it by profile name:
#   xcrun notarytool store-credentials Deskify-Notary \
#       --apple-id you@example.com --team-id ABCDE12345
# then leave NOTARY_PROFILE=Deskify-Notary below.
export NOTARY_PROFILE="${NOTARY_PROFILE:-Deskify-Notary}"   # PLACEHOLDER
# Alternatively (CI): set these instead of a keychain profile.
export APPLE_ID="${APPLE_ID:-}"                         # PLACEHOLDER
export APPLE_APP_PASSWORD="${APPLE_APP_PASSWORD:-}"     # PLACEHOLDER (app-specific password)

# --- Paths (derived) --------------------------------------------------------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export MACOS_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
export BUILD_DIR="$MACOS_ROOT/build"
export APP_PATH="$BUILD_DIR/$APP_NAME.app"
export DMG_PATH="$BUILD_DIR/$APP_NAME-$MARKETING_VERSION.dmg"
export ICON_MASTER="$MACOS_ROOT/Resources/deskify-icon.png"

is_adhoc() { [[ "$SIGN_IDENTITY" == "-" || -z "$SIGN_IDENTITY" ]]; }
