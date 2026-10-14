# 2026.10.5.58 - launcher: first login on a new server, every character on the account

Previous release: 2026.10.5.57. Launcher project version 0.1.6 (was 0.1.5).

## Problem

An account set up for a new server never got in. The `SK` profile (Conquest,
`acconquest.ddns.net:9000`, and earlier Aelrynth) had no password saved, so
the launcher started the client with `-v ""`. The client refuses an empty
password before it connects: it shows "You can only run the game from the
launcher program" and exits about 4 seconds later. All six `SK` launches in the
logs (Aelrynth, 2026-10-13 08:31 to 23:47; Conquest, 23:48) ended this way, and
the launcher only recorded each one as a crash.

An empty password would fail on the server too. ACE creates a new account on its
first login (`AllowAutoAccountCreation`), but only when a password is sent, and
it boots a login without one.

Also:

- A new account profile had no server picked unless you chose one, and the
  launch list then put it on the first server profile.
- The launch list showed "(None - no auto-login) | offline" as if it were a
  character name.

## Change

- **First Login dialog.** A manual launch of an account with no usable password
  (none saved, or one that can't be decrypted on this Windows user) asks for it
  first, instead of starting a client that can only fail. It has:
  - An optional list of the account names used in your other profiles, to pick
    an account you already have.
  - The account name. On ACE, a new name creates the account on this first
    login, if the server allows it.
  - A password with confirmation, saved encrypted like the other accounts. If
    another profile has the same account name with a saved password, a checkbox
    reuses it (the encrypted copy is copied, never the plain text).
  - "Log in as": stop at character select, or any character already known for
    the account.

  Cancelling skips that account and logs why. Automatic launches don't show the
  dialog. They skip the account, logging it at most once every 5 minutes.
- **Characters on the account.** With no character chosen, the client stops at
  character select, where you can create characters. The engine already saves
  the account's character list from character select, including newly created
  characters.
  - Stopping at character select before any character is known saves an empty
    choice, not "(None)". Once the first character exists and is the only one,
    the next launch logs it in.
  - With several characters, the launch list's dropdown, and the new "Log in as"
    in Edit Account, list them all. You pick which one logs in, or stop at
    character select.
- **Account Profile dialog.**
  - New "Log in as" list.
  - A new account defaults to the server selected in the list.
  - A server must be picked when there are server profiles.
  - Account names over 50 characters (ACE's limit), or with ':' on GDLE (whose
    login argument is `account:password`), are refused.
- **Launch list status.** "Needs password | launch to set up" for an account
  without one, and "Character select" instead of the "(None)" text.
- **Never an empty password.**
  - `AcLaunchArgumentBuilder.BuildArguments` refuses to build a command line with
    an empty password, with the reason.
  - The launch preview says the password is missing instead of showing `-v ""`.
  - `RynthCore.Injector launch` refuses too.

## Tests

- tools/LauncherSettingsTests: 152 checks pass.
  - The old checks that an account without a password "still launches as before
    (empty)" now check that it's refused, with the reason, on ACE and GDLE.
  - New: account-name checks, and a "first login on a new server" scenario:
    - known account names
    - reusing another profile's saved password (the copied encrypted password
      still decrypts)
    - the Log in as choices and what each saves
    - the dialog's input checks
- tools/AelrynthTests: 89/89.
- The launcher and the injector build. The dialogs were not shown in a test run.

## Files

- `src/RynthCore.App/FirstLoginRules.cs` (new), `AcLaunchArgumentBuilder.cs`, `LaunchAccountProfile.cs`
- `src/RynthCore.App.Avalonia/FirstLoginDialog.axaml` and `.axaml.cs` (new)
- `src/RynthCore.App.Avalonia/AccountProfileDialog.axaml` and `.axaml.cs`, `MainWindow.axaml.cs`,
  `RynthCore.App.Avalonia.csproj`
- `src/RynthCore.Injector/LaunchCommand.cs`
- `tools/LauncherSettingsTests/Program.cs`, `LauncherSettingsTests.csproj`

## Install

- Launcher only; the engine stays at 2026.10.5.57. Published with
  `-p:Version=2026.10.5.58` (framework-dependent, win-x86). The apphost
  `RynthCore.App.Avalonia.exe` is installed as `RynthCore.exe`.
- The previous launcher (2026.10.5.55) is backed up in
  `C:\Games\RynthCore\PreviousReleases\launcher-2026.10.5.55`. To roll back,
  close the launcher and copy those five files over `C:\Games\RynthCore`.
- Running game clients don't depend on the launcher and keep running.
