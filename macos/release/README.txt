`CleanSlate-1.0.1-mac-universal.dmg` is an ad-hoc signed universal build from GitHub Actions.
It is not notarized. macOS will warn the first time you open it: right-click CleanSlate, choose Open, then Open again.
Dragging the app to Applications does not need a developer account.

Later release builds that you sign with your own Developer ID replace this file. They are produced on a Mac with:

On macOS, from the macos directory:

  scripts/build.sh
  scripts/sign.sh                  # requires DEVELOPER_ID_APPLICATION
  scripts/package-dmg.sh 1.0.1
  scripts/notarize.sh release/CleanSlate-1.0.1-mac-universal.dmg
  scripts/verify-release.sh

An unsigned build from build.sh is a local development build. It is not notarized and Gatekeeper will not treat it as trusted.
