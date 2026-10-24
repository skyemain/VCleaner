Release artifacts are produced on a Mac. They are not checked in.

On macOS, from the macos directory:

  scripts/build.sh
  scripts/sign.sh                  # requires DEVELOPER_ID_APPLICATION
  scripts/package-dmg.sh 1.0.0
  scripts/notarize.sh release/CleanSlate-1.0.0-mac-universal.dmg
  scripts/verify-release.sh

An unsigned build from build.sh is a local development build. It is not notarized and Gatekeeper will not treat it as trusted.
