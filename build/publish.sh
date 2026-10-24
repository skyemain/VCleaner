#!/usr/bin/env bash
# Cross-publishes the self-contained win-x64 folder from Linux or macOS. Output is NOT code-signed and the
# resulting app can only be run on Windows. For the Windows-native flow (including the Windows-only tests) use publish.ps1.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
configuration="${CONFIGURATION:-Release}"
output="${1:-$root/artifacts/publish/win-x64}"

cd "$root"
dotnet restore CleanSlate.sln
dotnet build CleanSlate.sln -c "$configuration" --no-restore
dotnet test CleanSlate.sln -c "$configuration" --no-build --filter "FullyQualifiedName!~WindowsTests"

rm -rf "$output"
dotnet publish src/CleanSlate.App/CleanSlate.App.csproj -c "$configuration" -r win-x64 --self-contained true \
  -o "$output" -p:PublishReadyToRun=false -p:DebugType=none -p:DebugSymbols=false

test -f "$output/CleanSlate.exe"
echo "Published to $output ($(du -sh "$output" | cut -f1)). Not code-signed; run it on Windows."
