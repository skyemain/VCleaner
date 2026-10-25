#!/bin/bash
# Signs build/Clenzy.app with a Developer ID Application certificate.
# The identity comes from the environment. Nothing private is stored in the repository.
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "error: sign.sh must be run on macOS. This machine is $(uname -s)." >&2
  exit 2
fi

identity="${DEVELOPER_ID_APPLICATION:-}"
if [[ -z "${identity}" ]]; then
  echo "error: DEVELOPER_ID_APPLICATION is not set." >&2
  echo "Export the certificate name, for example:" >&2
  echo "  export DEVELOPER_ID_APPLICATION='Developer ID Application: Your Name (TEAMID)'" >&2
  echo "The app was not signed. Do not describe an unsigned build as trusted." >&2
  exit 2
fi

app="build/Clenzy.app"
if [[ ! -d "${app}" ]]; then
  echo "error: ${app} is missing. Run scripts/build.sh first." >&2
  exit 1
fi

codesign --force --options runtime --timestamp \
  --entitlements Clenzy/Resources/Clenzy.entitlements \
  --sign "${identity}" \
  "${app}"
codesign --verify --deep --strict --verbose=2 "${app}"
echo "Signed ${app} with hardened runtime and a secure timestamp."
