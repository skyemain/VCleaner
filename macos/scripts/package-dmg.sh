#!/bin/bash
# Packages build/CleanSlate.app into a drag-and-drop disk image.
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "error: package-dmg.sh must be run on macOS. hdiutil is not available on $(uname -s)." >&2
  echo "No DMG was created, and none should be treated as a release." >&2
  exit 2
fi

version="${1:-1.0.7}"
app="build/CleanSlate.app"
if [[ ! -d "${app}" ]]; then
  echo "error: ${app} is missing. Run scripts/build.sh first." >&2
  exit 1
fi

stage="$(mktemp -d)"
trap 'rm -rf "${stage}"; hdiutil detach "/Volumes/CleanSlate" >/dev/null 2>&1 || true' EXIT
mkdir -p "${stage}/.background"
cp -R "${app}" "${stage}/CleanSlate.app"
ln -s /Applications "${stage}/Applications"
cat > "${stage}/Install CleanSlate.txt" <<'INSTALL'
Open CleanSlate.app in this disk image and choose Install & replace older versions.
It closes old copies, verifies and replaces the app in Applications, and opens the
installed copy to Cleaning Access. User settings and history are preserved.

For blocked Trash or Safari folders, choose each folder in the macOS picker.
If macOS still denies access, enable Full Disk Access for the installed copy,
quit and reopen CleanSlate, then recheck. Reinstalling cannot reset macOS privacy.

This build is ad-hoc signed and not notarized. You may need right-click > Open.
You can still drag the app to Applications if administrator approval is needed.
INSTALL
cp scripts/dmg-background.png "${stage}/.background/background.png"

raw="build/CleanSlate-raw.dmg"
final="release/CleanSlate-${version}-mac-universal.dmg"
rm -f "${raw}" "${final}"
mkdir -p release

# GitHub-hosted Macs have no Finder session, so icon positions cannot be set there.
# The image still contains the app and the Applications shortcut.
if [[ "${CI:-}" == "true" ]]; then
  hdiutil create -volname CleanSlate -srcfolder "${stage}" -ov -format UDZO "${final}"
  echo "Wrote ${final}"
  echo "This disk image is not notarized. macOS will ask before the first launch."
  exit 0
fi

hdiutil create -volname CleanSlate -srcfolder "${stage}" -ov -format UDRW "${raw}"
hdiutil attach "${raw}" -mountpoint /Volumes/CleanSlate

osascript <<'APPLESCRIPT' || echo "warning: Finder layout was skipped. The disk image still contains the app and the Applications shortcut."
tell application "Finder"
  tell disk "CleanSlate"
    open
    set current view of container window to icon view
    set toolbar visible of container window to false
    set statusbar visible of container window to false
    set bounds of container window to {120, 120, 840, 520}
    set viewOptions to the icon view options of container window
    set arrangement of viewOptions to not arranged
    set icon size of viewOptions to 96
    set background picture of viewOptions to file ".background:background.png"
    set position of item "CleanSlate.app" of container window to {180, 190}
    set position of item "Applications" of container window to {500, 190}
    close
    open
    update without registering applications
    delay 1
  end tell
end tell
APPLESCRIPT

chmod -Rf go-w /Volumes/CleanSlate || true
sync
hdiutil detach /Volumes/CleanSlate
hdiutil convert "${raw}" -format UDZO -imagekey zlib-level=9 -o "${final}"
rm -f "${raw}"
echo "Wrote ${final}"
echo "This disk image is not notarized until scripts/notarize.sh succeeds."
