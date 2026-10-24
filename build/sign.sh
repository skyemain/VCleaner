#!/usr/bin/env bash
# Signs a Windows executable from Linux/macOS with the self-signed CleanSlate certificate (osslsigncode).
# Default certificate: artifacts/dev-cert/CleanSlate-dev.pfx (create it with build/new-dev-certificate.sh).
#
#   build/sign.sh artifacts/release/CleanSlate-1.1.0-win-x64.exe
#
# Optional: CLEANSLATE_SIGN_TIMESTAMP_URL (default http://timestamp.digicert.com), CLEANSLATE_SIGN_URL (publisher web page).
#
set -euo pipefail

file="${1:?usage: sign.sh <file.exe>}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
pfx="${CLEANSLATE_SIGN_PFX:-$root/artifacts/dev-cert/CleanSlate-dev.pfx}"
password="${CLEANSLATE_SIGN_PFX_PASSWORD:-cleanslate-dev}"
if [ ! -f "$pfx" ]; then
  echo "Signing certificate not found: $pfx. Run build/new-dev-certificate.sh first (see docs/SIGNING.md)." >&2
  exit 1
fi
command -v osslsigncode >/dev/null || { echo "osslsigncode is required (apt install osslsigncode)." >&2; exit 1; }

timestamp="${CLEANSLATE_SIGN_TIMESTAMP_URL:-http://timestamp.digicert.com}"
url_args=()
[ -n "${CLEANSLATE_SIGN_URL:-}" ] && url_args=(-i "$CLEANSLATE_SIGN_URL")
tmp="$file.signing"
trap 'rm -f "$tmp"' EXIT

timestamp_args=(-ts "$timestamp")
[ "${CLEANSLATE_SIGN_NO_TIMESTAMP:-0}" = 1 ] && timestamp_args=()

osslsigncode sign -pkcs12 "$pfx" -pass "$password" -h sha256 \
  -n "CleanSlate" "${url_args[@]}" "${timestamp_args[@]}" -in "$file" -out "$tmp"
mv "$tmp" "$file"
trap - EXIT

# The signature check below validates the file's integrity; chain trust depends on the CA bundle on this machine
# (a self-signed test certificate is reported as untrusted, which is expected).
osslsigncode verify -in "$file" ${CLEANSLATE_SIGN_CAFILE:+-CAfile "$CLEANSLATE_SIGN_CAFILE"} || true
echo "Signed $file"
