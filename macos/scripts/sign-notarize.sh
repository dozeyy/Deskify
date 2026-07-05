#!/bin/bash
# Notarizes the DMG with Apple and staples the ticket, so Gatekeeper accepts
# Deskify on other people's Macs without warnings. Requires a Developer ID
# identity and notarization credentials — fill these in scripts/config.sh.
#
#   ./scripts/sign-notarize.sh
#
# Prerequisites (one-time):
#   1. A "Developer ID Application" certificate in your keychain.
#   2. Store notary credentials once:
#        xcrun notarytool store-credentials Deskify-Notary \
#            --apple-id you@example.com --team-id ABCDE12345
#   3. Set SIGN_IDENTITY, TEAM_ID, NOTARY_PROFILE in scripts/config.sh.
set -euo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/config.sh"

if is_adhoc; then
    cat >&2 <<'EOF'
error: notarization needs a real Developer ID, not ad-hoc signing.
       Set SIGN_IDENTITY (and TEAM_ID / NOTARY_PROFILE) in scripts/config.sh.
       See the header of this script for the one-time setup steps.
EOF
    exit 1
fi

# Ensure a freshly signed DMG exists.
"$MACOS_ROOT/scripts/make-dmg.sh"

echo "==> Submitting $(basename "$DMG_PATH") to Apple notary service…"
if [[ -n "${APPLE_ID:-}" && -n "${APPLE_APP_PASSWORD:-}" && -n "${TEAM_ID:-}" ]]; then
    # Credential path (CI-friendly).
    xcrun notarytool submit "$DMG_PATH" \
        --apple-id "$APPLE_ID" \
        --password "$APPLE_APP_PASSWORD" \
        --team-id "$TEAM_ID" \
        --wait
else
    # Stored keychain profile path (recommended for a workstation).
    xcrun notarytool submit "$DMG_PATH" \
        --keychain-profile "$NOTARY_PROFILE" \
        --wait
fi

echo "==> Stapling the notarization ticket…"
xcrun stapler staple "$DMG_PATH"
xcrun stapler validate "$DMG_PATH"

echo "==> Notarized and stapled: $DMG_PATH"
echo "    Ready for public distribution."
