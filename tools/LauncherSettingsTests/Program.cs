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
                Check(builder.BuildMaskedArguments(ace, noPw).Contains("-v \"\""), "preview of an account without a password");
                Check(builder.BuildArguments(ace, noPw).Contains("-v \"\""), "no saved password still launches as before (empty)");

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
