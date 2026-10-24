#!/bin/bash
# Builds a universal CleanSlate.app. Signing is intentionally off here; use sign.sh afterwards.
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
  -project CleanSlate.xcodeproj \
  -scheme CleanSlate \
  -configuration Release \
  -destination 'generic/platform=macOS' \
  ARCHS='arm64 x86_64' \
  ONLY_ACTIVE_ARCH=NO \
  CODE_SIGNING_ALLOWED=NO \
  -derivedDataPath build/DerivedData \
  build

app="$(find build/DerivedData -path '*Build/Products/Release/CleanSlate.app' -type d | head -1)"
if [[ -z "${app}" ]]; then
  echo "error: Xcode did not produce CleanSlate.app." >&2
  exit 1
fi
rm -rf build/CleanSlate.app
cp -R "${app}" build/CleanSlate.app
echo "Built unsigned build/CleanSlate.app (universal if both architectures were available)."
echo "This is a local development build. It is not signed or notarized."
