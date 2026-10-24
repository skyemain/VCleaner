#!/usr/bin/env bash
# Creates the SELF-SIGNED code-signing certificate used by build/sign.sh. An existing certificate is kept so every build is
# signed by the same one (set FORCE=1 to replace it). Only PCs that trust the .cer see a valid signature (docs/SIGNING.md).
#
#   build/new-dev-certificate.sh [output-dir] [password]
set -euo pipefail
out="${1:-artifacts/dev-cert}"
password="${2:-cleanslate-dev}"
mkdir -p "$out"
if [ -f "$out/CleanSlate-dev.pfx" ] && [ "${FORCE:-0}" != 1 ]; then
  echo "A certificate already exists at $out/CleanSlate-dev.pfx; keeping it (FORCE=1 replaces it)."
  exit 0
fi

cat > "$out/openssl.cnf" <<'EOF'
[req]
distinguished_name = dn
x509_extensions = ext
prompt = no
[dn]
CN = CleanSlate Development
O = CleanSlate
[ext]
basicConstraints = critical,CA:FALSE
keyUsage = critical,digitalSignature
extendedKeyUsage = codeSigning
subjectKeyIdentifier = hash
EOF

openssl req -x509 -newkey rsa:3072 -sha256 -days 365 -nodes -config "$out/openssl.cnf" \
  -keyout "$out/dev.key" -out "$out/dev.crt"
openssl pkcs12 -export -inkey "$out/dev.key" -in "$out/dev.crt" -out "$out/CleanSlate-dev.pfx" -passout "pass:$password"
openssl x509 -in "$out/dev.crt" -outform der -out "$out/CleanSlate-dev.cer"
rm -f "$out/dev.key" "$out/openssl.cnf"
echo "Created $out/CleanSlate-dev.pfx (password: $password) and $out/CleanSlate-dev.cer"
echo "Never commit the .pfx. artifacts/ is git-ignored."
