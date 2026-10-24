# Manual validation on Windows 10 and Windows 11

CleanSlate was built and unit/integration-tested on Linux. Everything below has **not** been executed yet. Run it on a
**disposable or backed-up** Windows 10 (21H2 or newer) machine and a Windows 11 machine, using a standard user account
and, where noted, an administrator account. Record the result of each step.

Prepare once: build with `pwsh build/publish.ps1`, copy the single `artifacts\release\CleanSlate-<version>-win-x64.exe` to the test PC (or install the
.NET 10 SDK there and use `dotnet run`). Note the exact version string for the results. All of `dotnet test CleanSlate.sln -c Release` (including `CleanSlate.WindowsTests`)
should pass first.

## 1. Launch and appearance

- [ ] The window opens wide (about 1120x680). The home screen shows the ring button on the left and five targets on the right with **no scroll bar**, in both light and dark themes. The ring spins while a scan or clean runs.

- [ ] Starts without a console window or UAC prompt (standard user). Title bar shows the CleanSlate icon.
- [ ] Layout matches the spec: **Clean Now** in the center, five rows (System Junk, Chrome, Edge, Registry, Privacy), a settings gear at the top right.
- [ ] Switch Windows between light and dark app mode while the app is open: colors and the title bar follow. Turn off "Follow the Windows light/dark theme" in Settings: it stays light.
- [ ] Resize to the minimum size and maximize; at 100%, 150% and 200% scaling and when dragging between monitors with different scaling, nothing is clipped or blurry.
- [ ] Keyboard only: Tab reaches every control with a visible focus ring; Space/Enter activate; Esc leaves Settings/Review/Details.
- [ ] Narrator or NVDA reads the row names and status, the button text and the switch labels.
- [ ] Launching a second copy brings the first window forward instead of opening another.

## 2. Scan and one-click clean (standard user)

Create test files first: a few files older than 2 days in `%TEMP%` (set their modified time back with PowerShell
`(Get-Item f).LastWriteTime = (Get-Date).AddDays(-3)`), one file modified a minute ago, a file in a subfolder, and a
read-only old file.

- [ ] Click **Clean Now**. Rows show live progress, then real results. Old files are gone; the recent and read-only files remain; the summary byte count matches what you removed (compare folder size before and after).
- [ ] Open a file from `%TEMP%` in Notepad, then run again: that file is skipped and listed under details, not an error dialog.
- [ ] **Cancel** during a scan and during a cleanup (use a large temp folder): the app returns to Ready and says what happened; no partial-state crash.
- [ ] Documents, Desktop, Downloads and OneDrive content are untouched after every run.
- [ ] System Junk row mentions what needs administrator rights (Windows Temp, system logs, system dumps) instead of failing.
- [ ] Settings: turn off "User temporary files", run again: nothing in `%TEMP%` is touched.

## 3. Administrator behavior

- [ ] In a standard session click **Restart as administrator...**: confirm dialog, then the Windows UAC prompt. Declining keeps the app running unchanged with an explanation.
- [ ] Accepting restarts elevated (Settings shows "Running as administrator"); the admin hint is gone; Windows Temp, system WER, system dumps and old logs are now scanned.
- [ ] The app never prompts for elevation by itself.

## 4. Chrome and Edge

Use profiles with real browsing history, bookmarks and a saved password you can check afterward; create a second profile.

- [ ] With Chrome open and *Close running browsers* on: a dialog names Chrome. Choosing *Skip browsers* cleans everything else and leaves Chrome open with nothing of it deleted.
- [ ] Accepting closes Chrome (windows close first; check that no "Chrome didn't shut down correctly" bubble appears), then its caches are cleaned, and after reopening you are **still signed in** to the Google account and to a site with a saved cookie login. Repeat for Edge with a Microsoft account.
- [ ] With *Ask before closing* off the browser closes without a dialog. With *Close running browsers* off a running browser is skipped.
- [ ] Close Chrome (and background processes: "Continue running background apps" off, or end them): run again. Cache folders of **both** profiles are cleaned. Bookmarks, passwords, history, cookies, extensions and settings are intact and the browser starts normally.
- [ ] Same for Edge (also check that Startup Boost does not make Edge count as running; if it does, the row must say so rather than deleting).
- [ ] Enable "Website storage": a consent dialog appears; the review screen then lists it separately with a warning; declining there leaves it untouched.

## 4b. Temp, Prefetch and installer

- [ ] As administrator: `C:\Windows\Prefetch` loses only `*.pf` files (`Layout.ini`, `ReadyBoot` stay). Windows Temp and `%TEMP%` lose files that are not in use, including recent ones when *Include files created in the last few hours* is on. Locked files are listed as skipped.
- [ ] Run the exe from Downloads: the install offer appears. *No* runs once without installing. *Yes* installs to `%LOCALAPPDATA%\Programs\CleanSlate`, opens the installed copy, adds a Start menu entry and an Apps & features entry showing the version.
- [ ] Run a **newer** exe: the offer says "Replace". After *Yes* the old process is gone, the new version number shows in Settings, settings and history are still there, and the startup entry (if enabled) points to the installed exe.
- [ ] Settings > Installation > Uninstall (and, separately, Windows Settings > Apps > CleanSlate > Uninstall): program folder, Start menu entry and startup entry disappear a few seconds after exit; data is kept or deleted according to your answer. `CleanSlate.exe --uninstall --quiet` removes the program silently and keeps data.
- [ ] A `portable.txt` next to the exe suppresses the install offer.

## 5. Review mode and confirmations

- [ ] Enable "Always review before cleaning": Clean Now scans, then shows the review screen with sizes and risk. Deselect a category, confirm: only the remaining ones are cleaned.
- [ ] Enable "Empty the Recycle Bin" (consent dialog) with the bin non-empty: one-click flow stops at the review screen with a permanent-deletion warning; Cancel leaves the bin untouched; confirming empties it.
- [ ] Enable privacy cleanup: Run dialog history, typed Explorer paths and recent items are cleared only after review confirmation; pinned Quick Access items and jump lists remain.
- [ ] (Admin) Enable Windows Update cleanup and Delivery Optimization: both require review confirmation; DISM runs without a visible console window and the UI stays responsive; the result row does not claim a byte figure for DISM.

## 6. Registry review and repair

Create **test-only** entries (delete them afterward) in a PowerShell window:

```powershell
New-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name CleanSlateTest -Value '"C:\CleanSlateGone\gone.exe" /bg'
New-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\App Paths\cleanslategone.exe' -Value 'C:\CleanSlateGone\cleanslategone.exe'
New-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CleanSlateGhost' | Out-Null
Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CleanSlateGhost' DisplayName 'CleanSlate Ghost'
Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CleanSlateGhost' UninstallString '"C:\CleanSlateGone\uninstall.exe"'
```

- [ ] Clean Now never modifies these; the Registry row says "3 issues found" and offers "Review 3 registry fixes".
- [ ] The review page lists each entry with reason and evidence; nothing is pre-selected; Repair is disabled until something is selected.
- [ ] Repair one entry: confirmation dialog appears; after confirming, the entry is gone, a backup appears under Backups, and `%LOCALAPPDATA%\CleanSlate\registry-backups` has a JSON file. (Admin session: a restore point is attempted; Windows may skip it if one was made in the last 24 hours or System Protection is off. Standard session: the message explains it was skipped.)
- [ ] **Restore** the backup: the value/key returns. Change the value manually first and restore again: it refuses to overwrite.
- [ ] Add a Run entry pointing to a path that exists, to `D:\` removable media, and to `rundll32.exe x.dll,Y`: none of them is reported.
- [ ] A machine-wide (`HKLM`) broken Run entry shows "Requires administrator" and cannot be selected in a standard session.
- [ ] Remove all test entries afterward.

## 7. Settings, history, logs

- [ ] Toggles persist across restart (`%LOCALAPPDATA%\CleanSlate\settings.json`). "Reset all settings" restores defaults after confirmation.
- [ ] "Start CleanSlate when I sign in" adds/removes only `HKCU\...\Run\CleanSlate`; after sign-in the app starts (to the tray when "Minimize to the notification area" is on).
- [ ] Minimize to tray hides the window; the tray icon double-click restores it; Exit from the tray menu closes it (blocked with a message while a clean is running).
- [ ] A completion notification appears when the window is not in front, and not when it is.
- [ ] Excluded folder: add a folder with old junk inside `%TEMP%`; it is untouched. Remove the exclusion and it is cleaned.
- [ ] History lists each session; "Export report..." writes a readable text file; "Open logs folder" opens `logs`; "Clear history and logs" asks first. The logs contain `%USERPROFILE%` instead of your user name and no document names outside deleted cache paths.
- [ ] Corrupt `settings.json` by hand: the app starts with defaults and keeps a `.corrupt` copy.

## 8. Failure and robustness

- [ ] Run with `%TEMP%` on a different drive, and with a junction inside `%TEMP%` pointing to `C:\Users\<you>\Documents`: the junction target is never entered or deleted.
- [ ] Run while another CleanSlate action is in progress (try Settings during a scan): navigation is blocked.
- [ ] End the CleanSlate process from Task Manager in the middle of a clean, then start it again: it starts normally and the next scan reflects what was actually deleted.
- [ ] Very large profile (hundreds of thousands of files): UI stays responsive, memory stays reasonable, cancel responds within a couple of seconds. Note timings.

## 9. Distribution checks

- [ ] File properties of the `.exe`: product name, file version `MAJOR.MINOR.PATCH.N`, product version (`-dev.N+commit` for development builds), copyright and icon are present.
- [ ] Settings shows the same version string as the file properties, and the log's first line matches.
- [ ] First launch of the single file works from a read-only location and from `%USERPROFILE%\Downloads`; the second launch is noticeably faster.
- [ ] Signed build on a PC that trusts `CleanSlate-dev.cer`: Properties > Digital Signatures lists the signer and a timestamp, and `signtool verify /pa` succeeds.
- [ ] Signed build on a PC that does not trust the certificate: signature is reported untrusted and the publisher is unknown (expected). Record whether SmartScreen warned.
- [ ] Runs on a machine with no .NET installed.
