#!/bin/bash
# Checks the macOS sources for required files and for cleanup shortcuts that must never ship.
# This does not compile the app and does not claim a Mac build.
set -euo pipefail
cd "$(dirname "$0")/.."

required=(
  CleanSlate.xcodeproj/project.pbxproj
  CleanSlate/App/CleanSlateApp.swift
  CleanSlate/Core/CleaningEngine.swift
  CleanSlate/Services/SafeDeletionService.swift
  CleanSlate/Configuration/ProtectedPaths.swift
  CleanSlate/Cleaners/Chrome/ChromeCleaner.swift
  CleanSlate/Cleaners/Edge/EdgeCleaner.swift
  CleanSlate/Cleaners/Safari/SafariCleaner.swift
  CleanSlate/Resources/Info.plist
  CleanSlate/Resources/CleanSlate.entitlements
  scripts/build.sh
  scripts/package-dmg.sh
  scripts/sign.sh
  scripts/notarize.sh
  scripts/verify-release.sh
)
for path in "${required[@]}"; do
  test -f "${path}" || { echo "missing ${path}" >&2; exit 1; }
done

if grep -R -n -E 'rm -rf|sudo |SIGKILL|forceTerminate|tccutil|csrutil' --include='*.swift' CleanSlate CleanSlateTests; then
  echo "error: a Swift file contains a forbidden cleanup or security bypass." >&2
  exit 1
fi

echo "Source layout looks complete. This check did not compile or package the app."
