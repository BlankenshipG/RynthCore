# Changelog — Installer previous-release archive (2026-04-22)

## Summary

- **`installer\Build-Installer.ps1`:** When **`-Version`** is supplied and Inno Setup is about to run, if **`installer\previous-release\last-built-version.txt`** differs from that version and **`installer\Output\RynthBundle-Setup.exe`** exists, the existing setup is copied to **`installer\previous-release\RynthBundle-Setup-<last-version>.exe`** before the new compile overwrites `Output\`.
- After a **successful** `ISCC` run, **`last-built-version.txt`** is rewritten to the new `-Version` string.
- **`.gitignore`:** `installer/previous-release/*.exe` so archived installers stay local; **`last-built-version.txt`** remains tracked (seeded at **0.4.1**).
- **`BUILD.md`:** Documents the behavior under the installer section.

## Reason

Keeps the last shipping bundle installer on disk when bumping **`-Version`**, without manual copies.
