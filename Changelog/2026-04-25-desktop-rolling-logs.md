# Desktop log rotation (2026-04-25)

- New library **`RynthCore.DesktopLog`** with **`DesktopRollingLog`**: all lines append to **`Desktop\\{fileStem}.log`**. When a file would exceed **10 MB**, the current file is moved to **`Desktop\\RynthLogs\\`**, named **`{fileStem}-roll-yyyyMMdd-HHmmssfff.log`**, then a new active file is created. For each **file stem** and each **local calendar day**, at most **10** such rolled files are kept (oldest for that day deleted first so the new segment fits the cap).
- **Three log stems in use**
  - **`RynthCore`** (was `RynthCore.log` only) — in-process engine output, same path as before.
  - **`RynthCore-Launcher`** — one line on Avalonia app startup; writes **`RynthCore-Launcher.log`**, same rotation.
  - **`RynthCore-Injector`** — injector console lines to **`RynthCore-Injector.log`**, same rotation; success message references all three and the `RynthLogs` archive folder.
- **Build stamp** updated to `2026-04-25-v58-desktop-log-rotate`.
