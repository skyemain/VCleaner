#!/usr/bin/env bash
# Creates a SELF-SIGNED code-signing certificate for testing the signing pipeline.
# It does NOT make SmartScreen trust the app. Only machines where you import the .cer as a trusted root and publisher see a valid
# signature. Real distribution needs a certificate from a public CA or Azure Artifact Signing (docs/SIGNING.md).
#
#   build/new-dev-certificate.sh [output-dir] [password]
set -euo pipefail
out="${1:-artifacts/dev-cert}"
password="${2:-cleanslate-dev}"
mkdir -p "$out"

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
