#!/bin/bash
# Submits the DMG to Apple notarytool, then staples the ticket.
# Credentials stay in the login keychain. This script never embeds a password.
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "error: notarize.sh must be run on macOS. This machine is $(uname -s)." >&2
  exit 2
fi

profile="${NOTARY_KEYCHAIN_PROFILE:-}"
dmg="${1:-}"
if [[ -z "${profile}" || -z "${dmg}" ]]; then
  echo "error: usage: NOTARY_KEYCHAIN_PROFILE=cleanslate scripts/notarize.sh release/CleanSlate-1.0.0-mac-universal.dmg" >&2
  echo "Store the profile once with:" >&2
  echo "  xcrun notarytool store-credentials cleanslate --apple-id you@example.com --team-id TEAMID" >&2
  echo "Notarization was not submitted." >&2
  exit 2
fi
if [[ ! -f "${dmg}" ]]; then
  echo "error: ${dmg} does not exist. Package a signed app with scripts/package-dmg.sh first." >&2
  exit 1
fi

xcrun notarytool submit "${dmg}" --keychain-profile "${profile}" --wait
xcrun stapler staple "${dmg}"
xcrun stapler validate "${dmg}"
spctl --assess --type open --context context:primary-signature --verbose=2 "${dmg}" || \
  echo "warning: spctl could not assess the DMG. Check the notarytool log before distributing it."
echo "Stapled ${dmg}."
