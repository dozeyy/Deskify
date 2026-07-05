#!/bin/bash
# Compiles Deskify in Release as a universal (arm64 + x86_64) binary via SwiftPM.
# This is the pure command-line / CI path — no Xcode project required.
#
#   ./scripts/build-release.sh            # release, universal
#   CONFIG=debug ./scripts/build-release.sh
#
# Prints the built binary path on stdout (last line) for the packager to consume.
set -euo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/config.sh"

CONFIG="${CONFIG:-release}"
cd "$MACOS_ROOT"

echo "==> swift build ($CONFIG, arm64 + x86_64)…" >&2
swift build -c "$CONFIG" --arch arm64 --arch x86_64 >&2

# Resolve the produced binary across SwiftPM layout variations.
BIN="$(swift build -c "$CONFIG" --arch arm64 --arch x86_64 --show-bin-path)/Deskify"
if [[ ! -f "$BIN" ]]; then
    echo "error: built binary not found at $BIN" >&2
    exit 1
fi

# Sanity-check it really is universal.
if command -v lipo >/dev/null; then
    echo "==> Architectures: $(lipo -archs "$BIN")" >&2
fi

echo "$BIN"
