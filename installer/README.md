# Windows setup

Build a self-contained Windows x64 setup with Inno Setup 6:

```powershell
.\tools\packaging\build-setup.ps1 -Version 0.3.0
```

An initial publish may need network access to restore the Microsoft runtime
packs. Inno Setup must already be installed, or pass `-IsccPath`.
To package a previously published self-contained payload:

```powershell
.\tools\packaging\build-setup.ps1 -Version 0.3.0 -PublishedDirectory .\artifacts\installer-payload\win-x64
```

Output: `artifacts/setup/TikzEdt-Modern-0.3.0-Setup-win-x64.exe`, a SHA-256
checksum, and a build manifest. The payload contains the latest source build,
the .NET runtime, parser dependencies, syntax definition and snippet catalogues.
TeX/PDF utilities are prerequisites, not bundled installers.

Setup installs per-user, creates Start-menu shortcuts, optionally creates a
desktop shortcut, and registers an uninstaller. It does not change file
associations, install fonts, alter PATH, or remove user data. The fixed AppId
allows later releases to update the same installation. No code-signing
certificate is configured, so the setup is unsigned.

For unattended testing use `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-`
and a dedicated `/DIR=...`; `/LOG=...` records installation details. The
post-install launch action is skipped in silent mode.

`tools/packaging/test-setup.ps1` performs an install/uninstall round trip in a
dedicated workspace test folder, checks every payload file hash and verifies
the shortcut and uninstall registration. It refuses to run when a normal
TikzEdt Modern installation already exists. This test temporarily creates a
per-user Start-menu group and uninstall entry; both are removed afterward.
