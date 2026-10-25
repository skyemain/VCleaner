# macOS changelog

## 1.0.6

- Move live storage, battery time and battery health cards onto the home screen beside the RAM monitor; remove the separate System Overview sheet.
- Include eligible home Trash files in manual Clean without a separate Trash control. Clearly disclose permanent deletion; login cleans still leave Trash alone.
- Clean accessible Safari caches even when another cache is blocked. Leave shared WebKit containers alone and keep blocked-folder guidance in Settings.
- Clean Up RAM requests normal quits from smaller measured apps, preserving the top five, Finder and CleanSlate, then opens the top-app review. Save/cancel dialogs remain under each app's control; RAM results use observed readings.
- Clarify Full Disk Access setup for Trash and Safari, including re-adding the installed copy after ad-hoc-signed updates.

## 1.0.5

- Replace the text-heavy System Overview with RAM, storage, battery time and battery-health cards.
- Open Settings directly from the gear and ⌘,; consolidate the access guide into its Cleaning Access tab.
- Integrate home Trash into System Junk with per-clean confirmation and a leave-and-continue choice.
- Include hidden stale temporary files and archived rotated logs; recheck file age immediately before deletion.
- Explain tab/document closing and browser Memory Saver alongside normal app quitting for RAM management.

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
