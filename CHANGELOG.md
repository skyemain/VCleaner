# macOS changelog

## 1.0.4

- Replace the footer pressure label with five compact app memory percentages, including readable helpers.
- Replace unreliable allocation-based RAM purging with a per-app, confirmed normal-quit review.
- Add first-clean and on-demand access guidance, a Full Disk Access settings link and per-folder checks for Safari and system caches.
- Clean accessible system-cache folders even if another cache is blocked.
- Rename System Health to System Overview and explain its reports and actions.

## 1.0.3

- Make the mac branch macOS-only, with native sources, tests, resources and packaging.
- Remove remaining Edge cleaner code and approved paths.
- Report protected Safari cache directories and nested permission failures; offer to quit running browsers when cleaning from review.
- Include old Help Viewer, Spotlight suggestions and Tips caches in the explicit System Junk allowlist, with seven-day retention.
- Explain System Junk contents and retention periods in Settings.
- Show live RAM usage, total memory and pressure with a bottom Clean up RAM control.
- Match excluded paths across filesystem aliases and fix disabling automatic login cleaning.

## Earlier builds

The repository includes a historical 1.0.1 disk image. Source updates through 1.0.2 added Safari/WebKit cache roots and RAM cleanup. Use the latest successful macOS workflow artifact for current fixes.
