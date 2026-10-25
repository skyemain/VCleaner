#!/bin/bash
# Builds a universal Clenzy.app. Signing is intentionally off here; use sign.sh afterwards.
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "error: build.sh must be run on macOS with Xcode. This machine is $(uname -s)." >&2
  exit 2
fi
if ! command -v xcodebuild >/dev/null; then
  echo "error: xcodebuild was not found. Install Xcode and select it with xcode-select." >&2
  exit 2
fi

xcodebuild \
  -project Clenzy.xcodeproj \
  -scheme Clenzy \
  -configuration Release \
  -destination 'generic/platform=macOS' \
  ARCHS='arm64 x86_64' \
  ONLY_ACTIVE_ARCH=NO \
  CODE_SIGNING_ALLOWED=NO \
  -derivedDataPath build/DerivedData \
  build

app="$(find build/DerivedData -path '*Build/Products/Release/Clenzy.app' -type d | head -1)"
if [[ -z "${app}" ]]; then
  echo "error: Xcode did not produce Clenzy.app." >&2
  exit 1
fi
rm -rf build/Clenzy.app
cp -R "${app}" build/Clenzy.app
echo "Built unsigned build/Clenzy.app (universal if both architectures were available)."
echo "This is a local development build. It is not signed or notarized."
