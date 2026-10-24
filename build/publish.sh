#!/usr/bin/env bash
# Cross-publishes CleanSlate for Windows x64 from Linux or macOS.
#
#   build/publish.sh                 development build, e.g. CleanSlate-1.1.0-dev.57-win-x64.exe
#   build/publish.sh --release       release build, e.g. CleanSlate-1.1.0-win-x64.exe
#   build/publish.sh --sign          also Authenticode-sign the .exe (see build/sign.sh)
#   build/publish.sh --folder        additionally keep the unpacked folder layout
#   build/publish.sh --skip-tests    skip the automated tests
#
# The result is a single self-contained .exe in artifacts/release/ plus SHA256SUMS.txt. The app only runs on Windows;
# for the Windows-native flow (including the Windows-only tests) use build/publish.ps1.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
configuration="${CONFIGURATION:-Release}"
release=0 sign=0 folder=0 skip_tests=0
for arg in "$@"; do
  case "$arg" in
    --release) release=1 ;;
    --sign) sign=1 ;;
    --folder) folder=1 ;;
    --skip-tests) skip_tests=1 ;;
    *) echo "Unknown option: $arg" >&2; exit 2 ;;
  esac
done

cd "$root"
base_version="$(tr -d '[:space:]' < VERSION)"
sha="$(git rev-parse --short=7 HEAD 2>/dev/null || echo unknown)"
count="$(git rev-list --count HEAD 2>/dev/null || echo 0)"
if [ "$release" = 1 ]; then
  version="$base_version"
  version_args=(-p:BuildNumber="$count" -p:SourceRevisionId="$sha")
  if [ -n "$(git status --porcelain 2>/dev/null)" ]; then
    echo "Warning: building a release from a working tree with uncommitted changes." >&2
  fi
else
  version="$base_version-dev.$count"
  version_args=(-p:BuildNumber="$count" -p:SourceRevisionId="$sha" -p:VersionSuffix="dev.$count")
fi
echo "Version: $version+$sha"

dotnet restore CleanSlate.sln
dotnet build CleanSlate.sln -c "$configuration" --no-restore "${version_args[@]}"
if [ "$skip_tests" = 0 ]; then
  dotnet test CleanSlate.sln -c "$configuration" --no-build --filter "FullyQualifiedName!~WindowsTests"
fi

out_dir="$root/artifacts/release"
work="$root/artifacts/publish-single"
exe_name="CleanSlate-$version-win-x64.exe"
rm -rf "$work"
mkdir -p "$out_dir"
dotnet publish src/CleanSlate.App/CleanSlate.App.csproj -c "$configuration" -r win-x64 --self-contained true -o "$work" \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true \
  -p:PublishReadyToRun=false -p:DebugType=none -p:DebugSymbols=false "${version_args[@]}"
test -f "$work/CleanSlate.exe"
cp "$work/CleanSlate.exe" "$out_dir/$exe_name"

if [ "$folder" = 1 ]; then
  folder_out="$root/artifacts/publish/win-x64"
  rm -rf "$folder_out"
  dotnet publish src/CleanSlate.App/CleanSlate.App.csproj -c "$configuration" -r win-x64 --self-contained true -o "$folder_out" \
    -p:PublishReadyToRun=false -p:DebugType=none -p:DebugSymbols=false "${version_args[@]}"
  echo "Folder layout: $folder_out"
fi

if [ "$sign" = 1 ]; then
  "$root/build/sign.sh" "$out_dir/$exe_name"
else
  echo "Not code-signed (use --sign). Windows SmartScreen will warn about unsigned downloads."
fi

(cd "$out_dir" && sha256sum "$exe_name" > SHA256SUMS.txt)
echo
echo "Built $out_dir/$exe_name ($(du -h "$out_dir/$exe_name" | cut -f1))"
cat "$out_dir/SHA256SUMS.txt"
