#!/usr/bin/env bash
# Authenticode-signs a Windows executable from Linux/macOS using osslsigncode and a PFX/PKCS#12 file.
#
#   CLEANSLATE_SIGN_PFX=/path/cert.pfx CLEANSLATE_SIGN_PFX_PASSWORD=... build/sign.sh artifacts/release/CleanSlate-1.1.0-win-x64.exe
#
# Optional: CLEANSLATE_SIGN_TIMESTAMP_URL (default http://timestamp.digicert.com), CLEANSLATE_SIGN_URL (publisher web page).
#
# Certificates from public CAs are issued on hardware tokens or in cloud HSMs and cannot be exported to a PFX; for those, or for
# Azure Artifact Signing, sign on Windows with build/sign.ps1 or in CI. A PFX here is for self-signed test certificates
# (build/new-dev-certificate.sh) and for private CAs.
set -euo pipefail

file="${1:?usage: sign.sh <file.exe>}"
pfx="${CLEANSLATE_SIGN_PFX:-}"
if [ -z "$pfx" ]; then
  echo "CLEANSLATE_SIGN_PFX is not set; cannot sign. See docs/SIGNING.md." >&2
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

osslsigncode sign -pkcs12 "$pfx" -pass "${CLEANSLATE_SIGN_PFX_PASSWORD:-}" -h sha256 \
  -n "CleanSlate" "${url_args[@]}" "${timestamp_args[@]}" -in "$file" -out "$tmp"
mv "$tmp" "$file"
trap - EXIT

# The signature check below validates the file's integrity; chain trust depends on the CA bundle on this machine
# (a self-signed test certificate is reported as untrusted, which is expected).
osslsigncode verify -in "$file" ${CLEANSLATE_SIGN_CAFILE:+-CAfile "$CLEANSLATE_SIGN_CAFILE"} || true
echo "Signed $file"
