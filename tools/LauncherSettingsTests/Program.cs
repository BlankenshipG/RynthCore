using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using RynthCore.App;

namespace LauncherSettingsTests;

/// <summary>
/// The launcher's shared settings with "Decal + RynthCore" accounts, in a temp folder.
/// "Old launcher" = a launcher released before the Decal bridge: its InjectionMode has only
/// RynthCore and Decal, read with JsonStringEnumConverter, so "DecalBridge" makes the whole
/// appsettings.json unreadable for it.
/// </summary>
internal static class Program
{
    private static int _checks, _failed;
    private static string _dir = "";

    private static string Main_ => Path.Combine(_dir, "appsettings.json");
    private static string Bak => Main_ + ".bak";
    private static string Sidecar => Path.Combine(_dir, DecalAccountModes.FileName);

    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "rc-launcher-settings-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            Scenario("negative control: the old launcher can't read DecalBridge", root, () =>
            {
                File.WriteAllText(Main_, """{ "AccountProfiles": [ { "Id": "b", "InjectionMode": "DecalBridge" } ] }""");
                Check(!OldLauncherCanRead(Main_), "old reader rejects \"DecalBridge\" (so the checks below mean something)");
            });

            Scenario("save and load", root, () =>
            {
                AppSettings s = Sample();
                AppSettingsStore.Save(s);
                AppSettingsStore.Save(s);   // second save: a .bak exists too
                CheckSharedFilesSafe();
                Check(SharedMode("a") == "RynthCore", "a is RynthCore in appsettings.json");
                Check(SharedMode("b") == "RynthCore", "b (Decal + RynthCore) is written as RynthCore");
                Check(SharedMode("c") == "Decal", "c is Decal in appsettings.json");
                Check(SidecarIds().SequenceEqual(new[] { "b" }), "decal-accounts.json lists only b");
                Check(s.AccountProfiles[1].InjectionMode == InjectionMode.DecalBridge, "save leaves the in-memory mode alone");

                AppSettings loaded = AppSettingsStore.Load();
                Check(Mode(loaded, "a") == InjectionMode.RynthCore, "load: a RynthCore");
                Check(Mode(loaded, "b") == InjectionMode.DecalBridge, "load: b Decal + RynthCore");
                Check(Mode(loaded, "c") == InjectionMode.Decal, "load: c Decal");
                Check(AppSettingsStore.LastLoadDiagnostic == null && AppSettingsStore.LastMigrationNote == null, "load: no diagnostics");
            });

            Scenario("switch back to RynthCore", root, () =>
            {
                AppSettings s = Sample();
                AppSettingsStore.Save(s);
                s.AccountProfiles[1].InjectionMode = InjectionMode.RynthCore;
                AppSettingsStore.Save(s);
                Check(!File.Exists(Sidecar), "decal-accounts.json removed when no account uses the mode");
                Check(Mode(AppSettingsStore.Load(), "b") == InjectionMode.RynthCore, "load: b RynthCore");
            });

            Scenario("migrate a file written by a pre-release bridge build", root, () =>
            {
                string legacy = """
                { "EnginePath": "X", "AccountProfiles": [
                  { "Id": "a", "AccountName": "A", "InjectionMode": "RynthCore" },
                  { "Id": "b", "AccountName": "B", "InjectionMode": "DecalBridge" },
                  { "Id": "d", "AccountName": "D", "InjectionMode": 2 } ] }
                """;
                File.WriteAllText(Main_, legacy);
                File.WriteAllText(Bak, legacy);
                AppSettings loaded = AppSettingsStore.Load();
                Check(Mode(loaded, "b") == InjectionMode.DecalBridge, "b kept as Decal + RynthCore");
                Check(Mode(loaded, "d") == InjectionMode.DecalBridge, "d (numeric 2) kept as Decal + RynthCore");
                Check(loaded.EnginePath == "X", "other settings kept");
                Check(AppSettingsStore.LastMigrationNote?.Contains("Moved 2") == true, "migration note: " + AppSettingsStore.LastMigrationNote);
                CheckSharedFilesSafe();
                Check(SidecarIds().SequenceEqual(new[] { "b", "d" }), "decal-accounts.json lists b and d");

                AppSettingsStore.Load();
                Check(AppSettingsStore.LastMigrationNote == null, "second load: nothing left to migrate");
            });

            Scenario("an old launcher switches the account to Decal", root, () =>
            {
                AppSettingsStore.Save(Sample());
                OldLauncherEdit(accounts => SetMode(accounts, "b", "Decal"));
                AppSettings loaded = AppSettingsStore.Load();
                Check(Mode(loaded, "b") == InjectionMode.Decal, "the old launcher's choice wins");
                AppSettingsStore.Save(loaded);
                Check(!File.Exists(Sidecar), "the stale entry is dropped at the next save");
            });

            Scenario("an old launcher deletes the account", root, () =>
            {
                AppSettingsStore.Save(Sample());
                OldLauncherEdit(accounts =>
                {
                    for (int i = accounts.Count - 1; i >= 0; i--)
                        if ((string?)accounts[i]!["Id"] == "b") accounts.RemoveAt(i);
                });
                AppSettings loaded = AppSettingsStore.Load();
                Check(loaded.AccountProfiles.All(a => a.Id != "b"), "b is gone");
                AppSettingsStore.Save(loaded);
                Check(!File.Exists(Sidecar), "its entry is dropped at the next save");
            });

            Scenario("an old launcher saves without changes", root, () =>
            {
                AppSettingsStore.Save(Sample());
                OldLauncherEdit(_ => { });
                Check(Mode(AppSettingsStore.Load(), "b") == InjectionMode.DecalBridge, "b is still Decal + RynthCore");
            });

            Scenario("unreadable decal-accounts.json", root, () =>
            {
                AppSettingsStore.Save(Sample());
                File.WriteAllText(Sidecar, "{ not json");
                AppSettings loaded = AppSettingsStore.Load();
                Check(Mode(loaded, "b") == InjectionMode.RynthCore, "b runs as RynthCore");
                Check(Mode(loaded, "c") == InjectionMode.Decal, "other accounts unchanged");
                Check(AppSettingsStore.LastLoadDiagnostic == null, "appsettings.json not taken for corrupt");
                Check(AppSettingsStore.LastMigrationNote?.Contains("could not be read") == true, "note: " + AppSettingsStore.LastMigrationNote);
                loaded.AccountProfiles.First(a => a.Id == "b").InjectionMode = InjectionMode.DecalBridge;
                AppSettingsStore.Save(loaded);
                Check(SidecarIds().SequenceEqual(new[] { "b" }), "picking the mode again rewrites the file");
            });

            Scenario("a mode name this build doesn't know", root, () =>
            {
                File.WriteAllText(Main_, """{ "EnginePath": "Y", "AccountProfiles": [ { "Id": "z", "InjectionMode": "SomethingNew" }, { "Id": "y", "InjectionMode": 9 } ] }""");
                AppSettings loaded = AppSettingsStore.Load();
                Check(AppSettingsStore.LastLoadDiagnostic == null && loaded.EnginePath == "Y", "file still loads (not quarantined)");
                Check(Mode(loaded, "z") == InjectionMode.RynthCore && Mode(loaded, "y") == InjectionMode.RynthCore, "unknown modes read as RynthCore");
            });

            Scenario("unchanged list: decal-accounts.json is not rewritten", root, () =>
            {
                AppSettings s = Sample();
                AppSettingsStore.Save(s);
                DateTime old = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(Sidecar, old);
                AppSettingsStore.Save(s);
                Check(File.GetLastWriteTimeUtc(Sidecar) == old, "no rewrite when nothing changed");
                s.AccountProfiles[0].InjectionMode = InjectionMode.DecalBridge;
                AppSettingsStore.Save(s);
                Check(SidecarIds().SequenceEqual(new[] { "a", "b" }), "rewritten when the list changed");
                CheckSharedFilesSafe();
            });

            // ── saved account passwords (DPAPI) ─────────────────────────────────
            // Test-only passwords; nothing here touches the real settings file.
            const string Pw = "t3st pw \"quoted\" ü";
            const string Pw2 = "Second-Test-Pw";

            Scenario("password round trip", root, () =>
            {
                AppSettings s = Sample();
                s.AccountProfiles[0].SetPassword(Pw);
                AppSettingsStore.Save(s);
                AppSettingsStore.Save(s);   // .bak too
                CheckNoPlainText(Pw);
                CheckNoPlainText("t3st pw");
                Check(!RawAccount("a").ContainsKey("Password"), "no plain \"Password\" field written");
                Check(!string.IsNullOrEmpty((string?)RawAccount("a")["PasswordProtected"]), "PasswordProtected written");
                Check(OldLauncherCanRead(Main_), "the old launcher can still read the file");

                AppSettings loaded = AppSettingsStore.Load();
                LaunchAccountProfile a = loaded.AccountProfiles.First(x => x.Id == "a");
                Check(a.TryGetPasswordForLaunch(out string got, out _) && got == Pw, "decrypts to the saved password");
                Check(!a.PasswordNeedsReentry && a.HasSavedPassword, "saved, readable");
                Check(AppSettingsStore.LastPasswordMigrationNote == null && AppSettingsStore.LastPasswordProblemNote == null, "no password notes");

                var builder = new AcLaunchArgumentBuilder();
                LaunchServerProfile ace = Server(AcEmulatorKind.Ace), gdle = Server(AcEmulatorKind.Gdle);
                Check(builder.BuildArguments(ace, a).Contains("-v \"t3st pw \\\"quoted\\\" ü\""), "ACE args carry the password (-v)");
                Check(builder.BuildArguments(gdle, a).Contains("-a \"A:t3st pw \\\"quoted\\\" ü\""), "GDLE args carry account:password (-a)");
                string preview = builder.BuildMaskedArguments(ace, a);
                Check(!preview.Contains("t3st") && preview.Contains("-v " + AcLaunchArgumentBuilder.PasswordMask), "preview masked: " + preview);
                LaunchAccountProfile noPw = loaded.AccountProfiles.First(x => x.Id == "b");
                Check(AcLaunchArgumentBuilder.NeedsPassword(noPw) && !AcLaunchArgumentBuilder.NeedsPassword(a), "NeedsPassword: only the account without one");
                Check(builder.BuildMaskedArguments(ace, noPw).StartsWith("No password is saved for account 'B'"),
                    "preview of an account without a password says so: " + builder.BuildMaskedArguments(ace, noPw));
                // The client exits with "You can only run the game from the launcher program" on an
                // empty password (Conquest, 2026-10-13), so the launch is refused instead.
                string refusal = Throws(() => builder.BuildArguments(ace, noPw));
                Check(refusal.StartsWith("No password is saved for account 'B'"), "no saved password: launch refused: " + refusal);
                Check(Throws(() => builder.BuildArguments(gdle, noPw)).StartsWith("No password"), "GDLE refused too");

                // Account names: ACE's 50-character limit; GDLE's "account:password" argument.
                Check(AcLaunchArgumentBuilder.AccountNameProblem("SK", AcEmulatorKind.Ace) == null, "a short name is fine");
                Check(AcLaunchArgumentBuilder.AccountNameProblem(new string('x', 51), AcEmulatorKind.Ace)?.Contains("50") == true, "51 characters: too long");
                Check(AcLaunchArgumentBuilder.AccountNameProblem("a:b", AcEmulatorKind.Ace) == null, "a colon is fine on ACE");
                Check(AcLaunchArgumentBuilder.AccountNameProblem("a:b", AcEmulatorKind.Gdle) != null, "a colon is refused on GDLE");
                Check(AcLaunchArgumentBuilder.AccountNameProblem("  ", AcEmulatorKind.Ace) != null, "blank is refused");

                LaunchAccountProfile copy = a.Clone();
                Check(copy.PasswordProtected == a.PasswordProtected, "Clone keeps the encrypted password");
                copy.SetPassword(Pw2);
                Check(copy.TryGetPasswordForLaunch(out string got2, out _) && got2 == Pw2, "a new password replaces it");
                copy.ClearPassword();
                Check(!copy.HasSavedPassword && copy.TryGetPasswordForLaunch(out string none, out _) && none.Length == 0, "clear");
                Check(AccountPasswordProtection.Protect(Pw) != AccountPasswordProtection.Protect(Pw), "each encryption differs (salted blobs)");
            });

            Scenario("migrate plain-text passwords from an older file", root, () =>
            {
                string legacy = """
                { "EnginePath": "X", "AccountProfiles": [
                  { "Id": "a", "AccountName": "A", "Password": "PLAIN-PW-ONE", "InjectionMode": "RynthCore" },
                  { "Id": "b", "AccountName": "B", "Password": "PLAIN-PW-TWO", "InjectionMode": "RynthCore" },
                  { "Id": "c", "AccountName": "C", "Password": "", "InjectionMode": "Decal" },
                  { "Id": "d", "AccountName": "D" } ] }
                """;
                File.WriteAllText(Main_, legacy);
                File.WriteAllText(Bak, legacy);
                File.WriteAllText(Sidecar, """{ "DecalBridgeAccountIds": [ "b" ] }""");
                File.WriteAllText(Main_ + ".corrupt-20260101-000000", "{ old quarantined file");

                AppSettings loaded = AppSettingsStore.Load();
                Check(AppSettingsStore.LastPasswordMigrationNote?.StartsWith("migrated 2 account password(s) to encrypted storage") == true,
                    "one note: " + AppSettingsStore.LastPasswordMigrationNote);
                Check(AppSettingsStore.LastPasswordMigrationNote?.Contains("corrupt-") == true, "note points at the quarantined file");
                Check(PwOf(loaded, "a") == "PLAIN-PW-ONE" && PwOf(loaded, "b") == "PLAIN-PW-TWO", "passwords kept");
                Check(!loaded.AccountProfiles.First(x => x.Id == "c").HasSavedPassword, "empty plain password = none saved");
                Check(loaded.AccountProfiles.All(x => x.LegacyPlainPassword == null), "no plain text left on the profiles");
                CheckNoPlainText("PLAIN-PW-ONE");
                CheckNoPlainText("PLAIN-PW-TWO");
                Check(!File.ReadAllText(Main_).Contains("\"Password\"") && !File.ReadAllText(Bak).Contains("\"Password\""),
                    "no \"Password\" field in appsettings.json or .bak");
                Check(Mode(loaded, "b") == InjectionMode.DecalBridge && SidecarIds().SequenceEqual(new[] { "b" }),
                    "decal-accounts.json survives the migration save");
                Check(loaded.EnginePath == "X", "other settings kept");
                CheckNotesClean("PLAIN-PW-ONE", "PLAIN-PW-TWO");

                AppSettings again = AppSettingsStore.Load();
                Check(AppSettingsStore.LastPasswordMigrationNote == null, "second load: nothing left to migrate");
                Check(PwOf(again, "a") == "PLAIN-PW-ONE", "second load decrypts");
            });

            Scenario("undecryptable password (copied from another Windows user or PC)", root, () =>
            {
                AppSettings s = Sample();
                s.AccountProfiles[0].SetPassword(Pw);
                s.AccountProfiles[1].SetPassword(Pw2);
                s.AccountProfiles[2].SetPassword(Pw2);
                AppSettingsStore.Save(s);
                // Blobs this user can't open: a valid one with a byte changed (DPAPI's integrity
                // check fails as it does with another user's key), random bytes, and non-base64.
                byte[] blob = Convert.FromBase64String(s.AccountProfiles[0].PasswordProtected);
                blob[^5] ^= 0x5A;
                SetRaw("a", Convert.ToBase64String(blob));
                SetRaw("b", Convert.ToBase64String(Guid.NewGuid().ToByteArray()));
                SetRaw("c", "not base64 !!");

                AppSettings loaded = AppSettingsStore.Load();   // must not throw
                Check(AppSettingsStore.LastLoadDiagnostic == null, "the file is not taken for corrupt");
                Check(loaded.AccountProfiles.All(x => x.PasswordNeedsReentry), "all three marked \"password needs re-entering\"");
                Check(AppSettingsStore.LastPasswordProblemNote?.Contains("3 saved account password(s)") == true &&
                      AppSettingsStore.LastPasswordProblemNote.Contains("need re-entering"),
                    "problem note: " + AppSettingsStore.LastPasswordProblemNote);

                LaunchAccountProfile a = loaded.AccountProfiles.First(x => x.Id == "a");
                var builder = new AcLaunchArgumentBuilder();
                LaunchServerProfile ace = Server(AcEmulatorKind.Ace);
                string? error = null;
                try { builder.BuildArguments(ace, a); }
                catch (InvalidOperationException ex) { error = ex.Message; }
                Check(error?.Contains("needs re-entering") == true, "launch refused, not launched with an empty password: " + error);
                Check(builder.BuildMaskedArguments(ace, a).Contains("needs re-entering"), "preview says so");
                Check(!a.TryGetPasswordForLaunch(out string empty, out _) && empty.Length == 0, "TryGetPasswordForLaunch false");

                a.SetPassword(Pw2);   // re-entered in the account editor
                Check(!a.PasswordNeedsReentry && a.TryGetPasswordForLaunch(out string got, out _) && got == Pw2, "re-entering fixes it");
                AppSettingsStore.Save(loaded);
                AppSettings reloaded = AppSettingsStore.Load();
                Check(!reloaded.AccountProfiles.First(x => x.Id == "a").PasswordNeedsReentry, "fixed after reload");
                Check(reloaded.AccountProfiles.First(x => x.Id == "b").PasswordNeedsReentry, "the others still need re-entering");
                CheckNoPlainText(Pw2);
            });

            Scenario("nothing leaks into notes, previews or other files", root, () =>
            {
                File.WriteAllText(Main_, """{ "AccountProfiles": [ { "Id": "a", "AccountName": "A", "Password": "LEAK-CHECK-PW" } ] }""");
                AppSettings loaded = AppSettingsStore.Load();
                CheckNotesClean("LEAK-CHECK-PW");
                Check(!loaded.AccountProfiles[0].ToString().Contains("LEAK-CHECK-PW"), "display name has no password");
                Check(!new AcLaunchArgumentBuilder().BuildMaskedArguments(Server(AcEmulatorKind.Gdle), loaded.AccountProfiles[0]).Contains("LEAK-CHECK-PW"),
                    "GDLE preview masked");
                AppSettingsStore.Save(loaded);
                CheckNoPlainText("LEAK-CHECK-PW");
            });

            // ── First Login dialog rules (a new server: no password, no characters yet) ──
            Scenario("first login on a new server", root, () =>
            {
                var withPw = new LaunchAccountProfile { Id = "ilt", AccountName = "silentkelpie" };
                withPw.SetPassword(Pw);
                var noPwSame = new LaunchAccountProfile { Id = "other", AccountName = "SilentKelpie" };
                var conquest = new LaunchAccountProfile { Id = "new", AccountName = "SK", CharacterName = LaunchAccountProfile.NoneOption };
                var all = new List<LaunchAccountProfile> { withPw, noPwSame, conquest };

                Check(FirstLoginRules.KnownAccountNames(all, "new").SequenceEqual(new[] { "silentkelpie" }),
                    "known names: the others, distinct ignoring case, not this profile's own");

                Check(FirstLoginRules.SavedPasswordSource(all, "SILENTKELPIE", "new")?.Id == "ilt",
                    "the same name with a saved password can be reused (case ignored)");
                Check(FirstLoginRules.SavedPasswordSource(all, "SK", "new") == null, "a new name has none to reuse");
                Check(FirstLoginRules.SavedPasswordSource(all, "silentkelpie", "ilt") == null, "never the profile itself");

                var copy = new LaunchAccountProfile { AccountName = "silentkelpie" };
                copy.CopyPasswordFrom(withPw);
                Check(copy.PasswordProtected == withPw.PasswordProtected && copy.TryGetPasswordForLaunch(out string reused, out _) && reused == Pw,
                    "reuse copies the encrypted blob, and it decrypts");

                var none = new List<string>();
                List<string> firstChoices = FirstLoginRules.LoginChoices(none, conquest.CharacterName);
                Check(firstChoices.SequenceEqual(new[] { FirstLoginRules.StopAtCharacterSelect }),
                    "no characters yet: only stop at character select (the old (None) isn't listed)");
                Check(FirstLoginRules.CharacterNameFor(FirstLoginRules.StopAtCharacterSelect, none) == "",
                    "stop with none known saves \"\" so the first character created is picked next time");

                var chars = new List<string> { "Alpha", "Beta" };
                Check(FirstLoginRules.LoginChoices(chars, "Gamma").SequenceEqual(new[] { FirstLoginRules.StopAtCharacterSelect, "Gamma", "Alpha", "Beta" }),
                    "several characters: all listed, the saved one kept even if not seen yet");
                Check(FirstLoginRules.CharacterNameFor("Beta", chars) == "Beta", "picking a character saves it");
                Check(FirstLoginRules.CharacterNameFor(FirstLoginRules.StopAtCharacterSelect, chars) == LaunchAccountProfile.NoneOption,
                    "stop with characters known saves the explicit (None)");

                Check(FirstLoginRules.Problem("SK", "", "", false, AcEmulatorKind.Ace)?.StartsWith("Enter the password") == true, "a password is required");
                Check(FirstLoginRules.Problem("SK", "a", "b", false, AcEmulatorKind.Ace)?.Contains("don't match") == true, "the confirmation must match");
                Check(FirstLoginRules.Problem("SK", "pw", "pw", false, AcEmulatorKind.Ace) == null, "name + matching password: ready");
                Check(FirstLoginRules.Problem("silentkelpie", "", "", true, AcEmulatorKind.Ace) == null, "reusing a saved password needs no typing");
                Check(FirstLoginRules.Problem("a:b", "pw", "pw", false, AcEmulatorKind.Gdle) != null, "GDLE name with a colon refused");
                Check(FirstLoginRules.Problem("", "pw", "pw", false, AcEmulatorKind.Ace) != null, "a name is required");
            });

            // A saved UserPreferences.ini whose display mode the PC can't use made every launch
            // fail with "Could not initialize Direct3D"; a quick exit must undo that swap.
            Scenario("prefs swap: did the swap change the live file", root, () =>
            {
                byte[] a = "[Display]\nResolution=2560x1440\n"u8.ToArray();
                byte[] b = "[Display]\nResolution=1920x1080\n"u8.ToArray();
                Check(!UserPrefsSwapRecovery.SwapChangedPrefs(new UserPrefsSwap("s", "l", a, (byte[])a.Clone())), "same bytes: not the cause");
                Check(UserPrefsSwapRecovery.SwapChangedPrefs(new UserPrefsSwap("s", "l", a, b)), "different bytes: changed");
                Check(UserPrefsSwapRecovery.SwapChangedPrefs(new UserPrefsSwap("s", "l", a, null)), "no live file before: changed");
            });

            Scenario("prefs swap: failed stash set aside, live file put back", root, () =>
            {
                var (swap, legacy) = PrefsSwapFiles(legacyMatches: true);
                var now = new DateTime(2026, 10, 14, 16, 30, 5);
                UserPrefsRecoveryResult r = UserPrefsSwapRecovery.Recover(swap, quarantineStash: true, restoreLive: true, legacy, now);
                Check(!File.Exists(swap.StashPath), "stash no longer swappable");
                Check(r.QuarantinedStashPath == swap.StashPath + ".failed-20261014-163005" && File.Exists(r.QuarantinedStashPath), "stash kept as .failed-<time>");
                Check(!File.Exists(legacy) && r.QuarantinedLegacyPath != null, "identical name-only stash set aside too (it would re-seed)");
                Check(r.LiveRestored && File.ReadAllBytes(swap.LivePath).SequenceEqual(swap.PreviousLiveBytes!), "live file back to its pre-swap bytes");
            });

            Scenario("prefs swap: different name-only stash kept", root, () =>
            {
                var (swap, legacy) = PrefsSwapFiles(legacyMatches: false);
                UserPrefsRecoveryResult r = UserPrefsSwapRecovery.Recover(swap, true, true, legacy, DateTime.Now);
                Check(File.Exists(legacy) && r.QuarantinedLegacyPath == null, "name-only stash with other settings left alone");
            });

            Scenario("prefs swap: later swap or profile override", root, () =>
            {
                var (swap, legacy) = PrefsSwapFiles(legacyMatches: true);
                File.WriteAllText(swap.LivePath, "another account's settings");
                UserPrefsRecoveryResult r = UserPrefsSwapRecovery.Recover(swap, quarantineStash: false, restoreLive: false, legacy, DateTime.Now);
                Check(File.Exists(swap.StashPath) && File.Exists(legacy), "override: the user's file is never renamed");
                Check(!r.LiveRestored && File.ReadAllText(swap.LivePath) == "another account's settings", "later swap: live file untouched");
            });

            Scenario("prefs swap: no live file before", root, () =>
            {
                var (swap, _) = PrefsSwapFiles(legacyMatches: false);
                swap = swap with { PreviousLiveBytes = null };
                UserPrefsSwapRecovery.Recover(swap, true, true, null, DateTime.Now);
                Check(!File.Exists(swap.LivePath), "live file removed so the game writes its defaults");
            });

            // A kill while createdump.exe / WerFault.exe writes a client's crash dump cuts the dump
            // short; the launcher's kills wait for the writer, then Grace more, at most MaxHold.
            Scenario("crash dump writers: which client a command line names", root, () =>
            {
                Check(CrashDumpWriterRule.TargetPid("createdump",
                    "\"C:\\Games\\RynthCore\\Runtime\\dotnet\\createdump.exe\" -f \"C:\\Games\\RynthCore\\Logs\\dumps\\clr_29196_%t.dmp\" --withheap 29196") == 29196,
                    "createdump: the positional pid after the options");
                Check(CrashDumpWriterRule.TargetPid("createdump.exe", "createdump.exe 4321 -h") == 4321, "createdump: pid first, \".exe\" in the name");
                Check(CrashDumpWriterRule.TargetPid("createdump", "createdump --crashthread 777 --signal 6 -f 1234.dmp 4321") == 4321,
                    "createdump: numeric option values (--crashthread, --signal) aren't the pid");
                Check(CrashDumpWriterRule.TargetPid("createdump", "createdump -f x.dmp --withheap") == null, "createdump without a pid: none");
                Check(CrashDumpWriterRule.TargetPid("WerFault", @"C:\WINDOWS\SysWOW64\WerFault.exe -u -p 29196 -s 568") == 29196, "WerFault: the value after -p");
                Check(CrashDumpWriterRule.TargetPid("WerFault", @"C:\WINDOWS\system32\WerFault.exe -pss -s 1234 -r 5678 -p 29196 -ip 4000") == 29196,
                    "WerFault: -s/-r/-ip values aren't the pid");
                Check(CrashDumpWriterRule.TargetPid("werfault", "WerFault.exe /p 99 /s 1") == 99, "WerFault: /p, any case");
                Check(CrashDumpWriterRule.TargetPid("WerFault", "WerFault.exe -s 1234") == null, "WerFault without -p: none");
                Check(CrashDumpWriterRule.TargetPid("notepad", "notepad.exe 1234") == null, "not a dump writer: none");
                Check(CrashDumpWriterRule.TargetPid("createdump", null) == null && CrashDumpWriterRule.TargetPid("createdump", "") == null,
                    "no command line: none");
            });

            Scenario("crash dump writers: how long the kills wait", root, () =>
            {
                var now = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
                var H = CrashDumpWriterRule.Hold.Holding;
                Check(CrashDumpWriterRule.Decide(false, null, null, now) == CrashDumpWriterRule.Hold.None, "no writer ever: kill checks run");
                Check(CrashDumpWriterRule.Decide(true, null, null, now) == H, "a writer names the client: hold");
                Check(CrashDumpWriterRule.Decide(true, now, now.AddMinutes(-5), now) == H, "still writing after 5 min: hold");
                Check(CrashDumpWriterRule.Decide(false, now.AddSeconds(-59), now.AddMinutes(-1), now) == H, "writer gone 59 s ago: still held (60 s more)");
                Check(CrashDumpWriterRule.Decide(false, now.AddSeconds(-60), now.AddMinutes(-1), now) == CrashDumpWriterRule.Hold.None,
                    "writer gone 60 s ago: kill checks resume");
                Check(CrashDumpWriterRule.Decide(true, now, now.AddMinutes(-15), now) == CrashDumpWriterRule.Hold.Expired,
                    "held 15 min: expired, a writer that never ends can't shield a dead client");
                Check(CrashDumpWriterRule.Decide(false, now.AddSeconds(-10), now.AddMinutes(-16), now) == CrashDumpWriterRule.Hold.Expired,
                    "expired stays expired through the grace");
                Check(CrashDumpWriterRule.Grace == TimeSpan.FromSeconds(60), "the grace is 60 s");
            });
        }
        finally
        {
            AppSettingsStore.DirectoryOverride = null;
            try { Directory.Delete(root, recursive: true); } catch { }
        }

        Console.WriteLine(_failed == 0
            ? $"PASS: {_checks} checks"
            : $"FAIL: {_failed} of {_checks} checks failed");
        return _failed == 0 ? 0 : 1;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static void Scenario(string name, string root, Action body)
    {
        Console.WriteLine($"-- {name}");
        _dir = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        AppSettingsStore.DirectoryOverride = _dir;
        try { body(); }
        catch (Exception ex) { Check(false, $"threw {ex.GetType().Name}: {ex.Message}"); }
    }

    private static AppSettings Sample() => new()
    {
        EnginePath = @"C:\Games\RynthCore\Runtime\RynthCore.Loader.dll",
        AccountProfiles =
        [
            new LaunchAccountProfile { Id = "a", AccountName = "A", InjectionMode = InjectionMode.RynthCore },
            new LaunchAccountProfile { Id = "b", AccountName = "B", InjectionMode = InjectionMode.DecalBridge },
            new LaunchAccountProfile { Id = "c", AccountName = "C", InjectionMode = InjectionMode.Decal },
        ],
    };

    private static InjectionMode Mode(AppSettings s, string id) => s.AccountProfiles.First(a => a.Id == id).InjectionMode;

    /// <summary>
    /// A swap as the launcher leaves it: the failed per-server stash copied over the live file,
    /// plus the account's name-only stash (same bytes when <paramref name="legacyMatches"/>).
    /// </summary>
    private static (UserPrefsSwap Swap, string LegacyPath) PrefsSwapFiles(bool legacyMatches)
    {
        string stash = Path.Combine(_dir, "SK@Conquest.ini");
        string legacy = Path.Combine(_dir, "SK.ini");
        string live = Path.Combine(_dir, "UserPreferences.ini");
        byte[] bad = "[Display]\nResolution=2560x1440\nWindowed=False\n"u8.ToArray();
        byte[] good = "[Display]\nResolution=1920x1080\nWindowed=True\n"u8.ToArray();
        File.WriteAllBytes(stash, bad);
        File.WriteAllBytes(legacy, legacyMatches ? bad : good);
        File.WriteAllBytes(live, bad);
        return (new UserPrefsSwap(stash, live, bad, good), legacy);
    }

    private static void CheckSharedFilesSafe()
    {
        foreach (string path in new[] { Main_, Bak })
        {
            if (!File.Exists(path)) continue;
            string name = Path.GetFileName(path);
            Check(!File.ReadAllText(path).Contains("DecalBridge"), $"{name} has no \"DecalBridge\"");
            Check(OldLauncherCanRead(path), $"the old launcher can read {name}");
        }
    }

    private static string? SharedMode(string id)
    {
        JsonArray accounts = JsonNode.Parse(File.ReadAllText(Main_))!["AccountProfiles"]!.AsArray();
        return accounts.FirstOrDefault(a => (string?)a!["Id"] == id)?["InjectionMode"]?.ToString();
    }

    private static List<string> SidecarIds()
    {
        if (!File.Exists(Sidecar)) return new();
        JsonNode node = JsonNode.Parse(File.ReadAllText(Sidecar))!;
        return node["DecalBridgeAccountIds"]!.AsArray().Select(n => (string)n!).ToList();
    }

    /// <summary>An old launcher's save: the JSON round-trips with its edit applied.</summary>
    private static void OldLauncherEdit(Action<JsonArray> edit)
    {
        Check(OldLauncherCanRead(Main_), "old launcher loads the file before its edit");
        JsonNode root = JsonNode.Parse(File.ReadAllText(Main_))!;
        edit(root["AccountProfiles"]!.AsArray());
        File.Copy(Main_, Bak, overwrite: true);
        File.WriteAllText(Main_, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void SetMode(JsonArray accounts, string id, string mode)
    {
        foreach (JsonNode? a in accounts)
            if ((string?)a!["Id"] == id) a["InjectionMode"] = mode;
    }

    private enum OldInjectionMode { RynthCore = 0, Decal = 1 }

    private sealed class OldAccount
    {
        public string Id { get; set; } = "";
        public OldInjectionMode InjectionMode { get; set; }
    }

    private sealed class OldSettings
    {
        public List<OldAccount> AccountProfiles { get; set; } = new();
    }

    private static readonly JsonSerializerOptions OldOptions = new() { Converters = { new JsonStringEnumConverter() } };

    private static bool OldLauncherCanRead(string path)
    {
        try { JsonSerializer.Deserialize<OldSettings>(File.ReadAllText(path), OldOptions); return true; }
        catch (JsonException) { return false; }
    }

    private static LaunchServerProfile Server(AcEmulatorKind kind) => new()
    {
        Id = "s", Name = "Test", Host = "127.0.0.1", Port = 9000, Emulator = kind
    };

    /// <summary>The message <paramref name="action"/> throws ("" when it doesn't throw).</summary>
    private static string Throws(Action action)
    {
        try { action(); return string.Empty; }
        catch (Exception ex) { return ex.Message; }
    }

    private static string PwOf(AppSettings s, string id) =>
        s.AccountProfiles.First(a => a.Id == id).TryGetPasswordForLaunch(out string pw, out _) ? pw : "<unreadable>";

    private static JsonObject RawAccount(string id) =>
        JsonNode.Parse(File.ReadAllText(Main_))!["AccountProfiles"]!.AsArray()
            .First(a => (string?)a!["Id"] == id)!.AsObject();

    private static void SetRaw(string id, string passwordProtected)
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(Main_))!;
        foreach (JsonNode? a in root["AccountProfiles"]!.AsArray())
            if ((string?)a!["Id"] == id) a["PasswordProtected"] = passwordProtected;
        File.WriteAllText(Main_, root.ToJsonString());
    }

    /// <summary>No file the store wrote in the test folder (settings, .bak, .tmp, decal-accounts) holds the password.</summary>
    private static void CheckNoPlainText(string password)
    {
        foreach (string file in Directory.GetFiles(_dir))
        {
            if (file.Contains(".corrupt-")) continue;   // planted by the test itself
            Check(!File.ReadAllText(file).Contains(password), $"{Path.GetFileName(file)} has no plain-text password");
        }
    }

    /// <summary>The store's notes (what the launcher logs and shows in the activity panel) hold no password.</summary>
    private static void CheckNotesClean(params string[] passwords)
    {
        foreach (string? note in new[] { AppSettingsStore.LastLoadDiagnostic, AppSettingsStore.LastMigrationNote,
                     AppSettingsStore.LastPasswordMigrationNote, AppSettingsStore.LastPasswordProblemNote })
            foreach (string pw in passwords)
                Check(note == null || !note.Contains(pw), "note has no password: " + note);
    }

    private static void Check(bool ok, string what)
    {
        _checks++;
        if (ok) return;
        _failed++;
        Console.WriteLine($"   FAIL: {what}");
    }
}
