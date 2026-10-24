CleanSlate - ready-to-run Windows build (64-bit Windows 10 / 11)

  CleanSlate-<version>-win-x64.exe   the app. One file, no .NET needed. Double-click it; it offers to install itself.
  CleanSlate-dev.cer                 public certificate that signed the .exe (self-signed)
  SHA256SUMS.txt                     checksum of the .exe

First run on a PC
  1. Copy the .exe (and the .cer) to the PC.
  2. Optional, to show the signature as valid and the publisher as "CleanSlate Development":
     double-click CleanSlate-dev.cer > Install Certificate > Current User > place in
     "Trusted Root Certification Authorities", then repeat for "Trusted Publishers".
     (or from the repository: pwsh build\trust-certificate.ps1 -CertificatePath .\release\CleanSlate-dev.cer)
  3. If Windows SmartScreen says "Windows protected your PC": More info > Run anyway.
     Right-click the .exe > Properties > Unblock removes the prompt for files copied from the internet.

Installing and updating
  Running the exe offers to install it (no administrator rights). Running a newer exe later offers to replace the
  installed version, keeping your settings. Uninstall from Windows Settings > Apps, or CleanSlate Settings > Installation.
  Put an empty file named portable.txt next to the exe to skip the install offer.

The first launch is slower because the single file unpacks WPF's native libraries; later launches are faster.
This build has not been run on Windows by its author; see docs/VALIDATION.md for the manual test checklist.
