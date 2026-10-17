using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RynthCore.Engine.Plugins;

namespace RynthCore.Engine
{
    /// <summary>Stub of the engine's log: the lines go to stdout, where the parent test reads them.</summary>
    internal static class RynthLog
    {
        internal static void Plugin(string msg) => Console.WriteLine("[PLG] " + msg);
        internal static void Info(string msg) => Console.WriteLine("[INF] " + msg);
        internal static void Warn(string msg) => Console.WriteLine("[WRN] " + msg);
    }
}

namespace EngineSettingsTests
{
    // Offline tests for saving engine.json. See the csproj.
    internal static class Program
    {
        private static int _fails, _asserts;
        private static void Check(bool cond, string msg)
        {
            _asserts++;
            if (!cond) { _fails++; Console.WriteLine($"  [FAIL] {msg}"); }
        }
        private static void Eq<T>(T actual, T expected, string msg) =>
            Check(EqualityComparer<T>.Default.Equals(actual, expected), $"{msg}: expected '{expected}', got '{actual}'");

        private static string _root = "";

        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--child")
                return Child(args.Skip(1).ToArray());

            _root = Path.Combine(Path.GetTempPath(), "rynth-engine-settings-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            try
            {
                Run(nameof(MergeKeepsForeignKeys), MergeKeepsForeignKeys);
                Run(nameof(MergeEmptyOrMissingFile), MergeEmptyOrMissingFile);
                Run(nameof(MergeRefusesUnreadableFile), MergeRefusesUnreadableFile);
                Run(nameof(WriteAtomicCreatesAndReplaces), WriteAtomicCreatesAndReplaces);
                Run(nameof(OwnedKeysMatchEngineSettings), OwnedKeysMatchEngineSettings);
                Run(nameof(SaveKeepsLauncherKeys), SaveKeepsLauncherKeys);
                Run(nameof(SaveRoundTripsOwnedKeys), SaveRoundTripsOwnedKeys);
                Run(nameof(AutoIdModeRoundTrips), AutoIdModeRoundTrips);
                Run(nameof(SaveWithoutFileWritesOwnedKeys), SaveWithoutFileWritesOwnedKeys);
                Run(nameof(SaveLeavesMalformedFileAlone), SaveLeavesMalformedFileAlone);
                Run(nameof(MaxHotReloadsReadAndKept), MaxHotReloadsReadAndKept);
                Run(nameof(CloseOnServerDisconnectDefaultOnAndSaved), CloseOnServerDisconnectDefaultOnAndSaved);
                Run(nameof(IltServerNamesEdit), IltServerNamesEdit);
            }
            finally
            {
                try { Directory.Delete(_root, true); } catch { }
            }
            Console.WriteLine();
            Console.WriteLine(_fails == 0 && _asserts > 0 ? $"PASS ({_asserts} assertions)" : $"FAIL ({_fails} of {_asserts} assertions failed)");
            return _fails == 0 && _asserts > 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            int before = _fails;
            try { test(); }
            catch (Exception ex) { _fails++; _asserts++; Console.WriteLine($"  [FAIL] {name} threw {ex.GetType().Name}: {ex.Message}"); }
            Console.WriteLine($"{(_fails == before ? "ok  " : "FAIL")} {name}");
        }

        // ── The real EngineSettings, in a child process ─────────────────────────

        /// <summary>Runs in the child: one EngineSettings call against RYNTHCORE_ENGINE_SETTINGS.</summary>
        private static int Child(string[] args)
        {
            try
            {
                switch (args[0])
                {
                    case "hook-off": EngineSettings.SetUiHookEnabled(args[1], false); break;
                    case "add-plugin": EngineSettings.AddPluginPath(args[1]); break;
                    case "imgui-off": EngineSettings.EnableImGuiBackend = false; break;
                    case "dcclose-off": EngineSettings.CloseOnServerDisconnect = false; break;
                    case "dcclose-on": EngineSettings.CloseOnServerDisconnect = true; break;
                    case "autoid":
                        if (!RynthCore.Engine.Compatibility.AutoIdPolicy.TryParseMode(args[1], out var aim)) throw new ArgumentException(args[1]);
                        EngineSettings.AutoIdMode = aim;
                        break;
                    case "ilt-add": Console.WriteLine("Changed=" + EngineSettings.EditIltServerName(args[1], add: true)); break;
                    case "ilt-remove": Console.WriteLine("Changed=" + EngineSettings.EditIltServerName(args[1], add: false)); break;
                    case "dump":
                        Console.WriteLine("AutoIdMode=" + EngineSettings.AutoIdMode);
                        Console.WriteLine("PluginPaths=" + string.Join("|", EngineSettings.PluginPaths));
                        Console.WriteLine("StartTooltip=" + EngineSettings.IsUiHookEnabled("StartTooltip"));
                        Console.WriteLine("DecalBridge=" + EngineSettings.DecalBridgeMode);
                        Console.WriteLine("ImGuiBackend=" + EngineSettings.EnableImGuiBackend);
                        Console.WriteLine("ServerMessageStream=" + EngineSettings.ServerMessageStream);
                        Console.WriteLine("MaxHotReloads=" + EngineSettings.MaxHotReloads);
                        Console.WriteLine("AllowInWorldAutoHotReload=" + EngineSettings.AllowInWorldAutoHotReload);
                        Console.WriteLine("CloseOnServerDisconnect=" + EngineSettings.CloseOnServerDisconnect);
                        Console.WriteLine("IltServerNames=" + (EngineSettings.IltServerNames is { } n ? string.Join("|", n) : "(none)"));
                        break;
                    default: throw new ArgumentException(args[0]);
                }
                Console.WriteLine("CHILD-OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CHILD-THREW {ex.GetType().Name}: {ex.Message}");
                return 2;
            }
        }

        private static (int Exit, string Out) RunChild(string settingsPath, params string[] args)
        {
            var psi = new ProcessStartInfo(Environment.ProcessPath!)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("--child");
            foreach (string a in args) psi.ArgumentList.Add(a);
            psi.Environment["RYNTHCORE_ENGINE_SETTINGS"] = settingsPath;
            using Process p = Process.Start(psi)!;
            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, output);
        }

        private static string NewDir(string name)
        {
            string dir = Path.Combine(_root, name);
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static JsonElement ReadJson(string path)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.Clone();
        }

        private static string Raw(JsonElement el) => el.GetRawText().Replace(" ", "").Replace("\r", "").Replace("\n", "");

        private static void NoTempFilesLeft(string dir) =>
            Eq(string.Join(",", Directory.GetFiles(dir).Select(Path.GetFileName).Where(n => n != "engine.json")), "", "only engine.json in the folder (no temp file left)");

        // A launcher-written engine.json: the engine's keys plus the launcher's (and an unknown tool's).
        private const string LauncherFile = """
            {
              "PluginPaths": [ "C:\\Games\\RynthSuite\\Plugins\\RynthCore.Plugin.RynthAi.dll" ],
              "EnableImGuiShell": false,
              "EnableImGuiBackend": true,
              "EnableDcompOverlay": true,
              "DecalBridge": "On",
              "DecalInGameImGui": true,
              "LoggingLevel": "Debug",
              "LauncherWindow": { "X": 10, "Y": -4, "Docked": [ "Plugins", { "Tab": 2 } ], "Note": null },
              "SomeNumber": 1.50,
              "Unicode": "Dereth \u00e9 \u2014"
            }
            """;

        private static void SaveKeepsLauncherKeys()
        {
            string dir = NewDir("keeps");
            string path = Path.Combine(dir, "engine.json");
            File.WriteAllText(path, LauncherFile);
            JsonElement before = ReadJson(path);

            var (exit, output) = RunChild(path, "hook-off", "StartTooltip");
            Eq(exit, 0, "child exit code (" + output.Trim() + ")");

            JsonElement after = ReadJson(path);
            foreach (string key in new[] { "EnableDcompOverlay", "LoggingLevel", "LauncherWindow", "SomeNumber", "Unicode" })
            {
                Check(after.TryGetProperty(key, out JsonElement v), $"launcher key {key} kept");
                if (after.TryGetProperty(key, out v))
                    Check(JsonElement.DeepEquals(v, before.GetProperty(key)), $"launcher key {key} value unchanged: {Raw(v)}");
            }
            Eq(after.GetProperty("SomeNumber").GetRawText(), "1.50", "a number keeps its exact text");
            Eq(after.GetProperty("Unicode").GetString(), "Dereth \u00e9 \u2014", "non-ASCII text intact");

            // The engine's own keys: only the one changed (DisabledUiHooks) is written; every other
            // key keeps exactly what the file had (since 2026-10-17: another client's change to
            // those must not be overwritten with this client's stale copy).
            Eq(string.Join(",", after.GetProperty("DisabledUiHooks").EnumerateArray().Select(e => e.GetString())), "StartTooltip", "DisabledUiHooks written");
            Eq(Raw(after.GetProperty("PluginPaths")), Raw(before.GetProperty("PluginPaths")), "PluginPaths kept");
            Eq(after.GetProperty("EnableImGuiShell").GetBoolean(), false, "EnableImGuiShell kept");
            Eq(after.GetProperty("DecalBridge").GetString(), "On", "DecalBridge On kept");
            foreach (JsonProperty p in before.EnumerateObject())
                if (p.Name != "DisabledUiHooks")
                    Check(after.TryGetProperty(p.Name, out JsonElement kept) && JsonElement.DeepEquals(kept, p.Value), $"unchanged key {p.Name} keeps the file's value");
            foreach (string key in new[] { "EnablePlugins", "EnableDatShareHook", "EnableAvaloniaOverlay", "EnableD3D9Hook", "EnableEngine", "EngineHookCount", "EnableHangMinidump", "PreventIdleLogoff" })
                Check(after.TryGetProperty(key, out _) == before.TryGetProperty(key, out _), $"owned key {key} not added or dropped by an unrelated save");

            // No key twice.
            var names = after.EnumerateObject().Select(p => p.Name).ToList();
            Eq(names.Count, names.Distinct(StringComparer.Ordinal).Count(), "no duplicate keys");
            NoTempFilesLeft(dir);

            // A second save (another owned key) still keeps them.
            (exit, output) = RunChild(path, "imgui-off");
            Eq(exit, 0, "second child exit code (" + output.Trim() + ")");
            after = ReadJson(path);
            Eq(after.GetProperty("EnableImGuiBackend").GetBoolean(), false, "EnableImGuiBackend saved");
            Check(after.TryGetProperty("EnableDcompOverlay", out _) && after.TryGetProperty("LauncherWindow", out _), "launcher keys survive a second save");
            Eq(after.GetProperty("DisabledUiHooks").GetArrayLength(), 1, "first save's DisabledUiHooks kept");

            // Two clients: one saved EnableImGuiBackend=false above; "another client" now turns the
            // shell back on in the file. A later save of a different key here must not put the
            // stale EnableImGuiShell back (the last-writer-wins reset of 2026-10-16).
            var doc = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            doc["EnableImGuiShell"] = true;
            File.WriteAllText(path, doc.ToJsonString());
            (exit, output) = RunChild(path, "hook-off", "AnotherHook");
            Eq(exit, 0, "third child exit code (" + output.Trim() + ")");
            after = ReadJson(path);
            Eq(after.GetProperty("EnableImGuiShell").GetBoolean(), true, "another client's EnableImGuiShell survives this client's save");
        }

        private static void SaveRoundTripsOwnedKeys()
        {
            string dir = NewDir("roundtrip");
            string path = Path.Combine(dir, "engine.json");
            File.WriteAllText(path, LauncherFile.Replace("\"LoggingLevel\"", "\"ServerMessageStream\": false, \"LoggingLevel\""));
            string plugin = Path.Combine(dir, "RynthCore.Plugin.RynthNav.dll");
            Eq(RunChild(path, "add-plugin", plugin).Exit, 0, "add-plugin");
            Eq(RunChild(path, "hook-off", "StartTooltip").Exit, 0, "hook-off");

            var (exit, output) = RunChild(path, "dump");
            Eq(exit, 0, "dump");
            Check(output.Contains("PluginPaths=C:\\Games\\RynthSuite\\Plugins\\RynthCore.Plugin.RynthAi.dll|" + plugin), "plugin paths read back in order: " + output);
            Check(output.Contains("StartTooltip=False"), "the hook is off after a reload");
            Check(output.Contains("DecalBridge=On"), "DecalBridge read back");
            Check(output.Contains("ServerMessageStream=False"), "ServerMessageStream false read back");
            Check(ReadJson(path).TryGetProperty("EnableDcompOverlay", out _), "launcher key still there");
        }

        private static void AutoIdModeRoundTrips()
        {
            string dir = NewDir("autoid");
            string path = Path.Combine(dir, "engine.json");
            File.WriteAllText(path, LauncherFile);
            var (exit, output) = RunChild(path, "dump");
            Eq(exit, 0, "dump");
            Check(output.Contains("AutoIdMode=Limited"), "default is Limited: " + output);

            Eq(RunChild(path, "autoid", "off").Exit, 0, "set off");
            Eq(ReadJson(path).GetProperty("AutoIdMode").GetString(), "Off", "saved as \"Off\"");
            (exit, output) = RunChild(path, "dump");
            Check(output.Contains("AutoIdMode=Off"), "Off read back: " + output);

            Eq(RunChild(path, "autoid", "all").Exit, 0, "set all");
            (exit, output) = RunChild(path, "dump");
            Check(output.Contains("AutoIdMode=All"), "All read back: " + output);

            Eq(RunChild(path, "autoid", "limited").Exit, 0, "back to limited");
            Check(!ReadJson(path).TryGetProperty("AutoIdMode", out _), "the default leaves the key out");
            Check(ReadJson(path).TryGetProperty("EnableDcompOverlay", out _), "launcher key still there");

            File.WriteAllText(path, LauncherFile.Replace("\"LoggingLevel\"", "\"AutoIdMode\": \"bogus\", \"LoggingLevel\""));
            (exit, output) = RunChild(path, "dump");
            Check(output.Contains("AutoIdMode=Limited"), "an unknown value reads as the default: " + output);
        }

        private static void SaveWithoutFileWritesOwnedKeys()
        {
            string dir = NewDir("nofile");
            string path = Path.Combine(dir, "engine.json");
            var (exit, output) = RunChild(path, "hook-off", "CheckTooltip");
            Eq(exit, 0, "child exit code (" + output.Trim() + ")");
            Check(File.Exists(path), "engine.json created");
            JsonElement after = ReadJson(path);
            Eq(after.GetProperty("DisabledUiHooks")[0].GetString(), "CheckTooltip", "the change saved");
            Check(after.EnumerateObject().All(p => EngineSettings.OwnedKeys.Contains(p.Name)), "only engine keys in a new file");
            NoTempFilesLeft(dir);
        }

        private static void SaveLeavesMalformedFileAlone()
        {
            foreach ((string name, string content) in new[]
            {
                ("truncated", "{ \"PluginPaths\": [ \"C:\\\\Games\\\\a.dll\" ], \"EnableDcompOverlay\": tr"),
                ("not-json", "this is not json"),
                ("array-root", "[ 1, 2, 3 ]"),
            })
            {
                string dir = NewDir("bad-" + name);
                string path = Path.Combine(dir, "engine.json");
                File.WriteAllText(path, content);
                byte[] before = File.ReadAllBytes(path);

                var (exit, output) = RunChild(path, "hook-off", "StartTooltip");
                Eq(exit, 0, $"{name}: no crash ({output.Trim()})");
                Check(output.Contains("[WRN] EngineSettings: not saving"), $"{name}: says why it didn't save");
                Check(File.ReadAllBytes(path).SequenceEqual(before), $"{name}: the file is left exactly as it was");
                NoTempFilesLeft(dir);
            }

            // An empty file (an old non-atomic save cut short) holds nothing to lose: it is rewritten.
            string emptyDir = NewDir("bad-empty");
            string emptyPath = Path.Combine(emptyDir, "engine.json");
            File.WriteAllText(emptyPath, "");
            Eq(RunChild(emptyPath, "hook-off", "StartTooltip").Exit, 0, "empty: no crash");
            Eq(ReadJson(emptyPath).GetProperty("DisabledUiHooks")[0].GetString(), "StartTooltip", "empty: rewritten with the change");
        }

        // engine.json "MaxHotReloads" (the loader's hot-reload cap): default 20, a hand-set value
        // (including -1 = no cap) is read and survives the engine's own saves, the default is
        // never written, and a value that isn't a whole number is ignored.
        private static void MaxHotReloadsReadAndKept()
        {
            string dir = NewDir("maxreloads");
            string path = Path.Combine(dir, "engine.json");

            File.WriteAllText(path, LauncherFile);
            var (exit, output) = RunChild(path, "dump");
            Eq(exit, 0, "dump (missing)");
            Eq(DumpValue(output, "MaxHotReloads"), "20", "missing: the default");
            Eq(RunChild(path, "hook-off", "StartTooltip").Exit, 0, "save with the default");
            Check(!ReadJson(path).TryGetProperty("MaxHotReloads", out _), "the default isn't written to the file");

            foreach ((string json, string expected) in new[] { ("5", "5"), ("-1", "-1"), ("0", "0") })
            {
                File.WriteAllText(path, LauncherFile.Replace("\"LoggingLevel\"", $"\"MaxHotReloads\": {json}, \"LoggingLevel\""));
                (exit, output) = RunChild(path, "dump");
                Eq(exit, 0, $"{json}: dump");
                Eq(DumpValue(output, "MaxHotReloads"), expected, $"{json}: read back");
                Eq(RunChild(path, "hook-off", "CheckTooltip").Exit, 0, $"{json}: save");
                Eq(ReadJson(path).GetProperty("MaxHotReloads").GetRawText(), json, $"{json}: kept by the engine's save");
                Eq(DumpValue(RunChild(path, "dump").Out, "MaxHotReloads"), expected, $"{json}: still the same after the save");
            }

            foreach (string bad in new[] { "\"lots\"", "2.5", "true", "null" })
            {
                File.WriteAllText(path, LauncherFile.Replace("\"LoggingLevel\"", $"\"MaxHotReloads\": {bad}, \"LoggingLevel\""));
                (exit, output) = RunChild(path, "dump");
                Eq(exit, 0, $"{bad}: dump");
                Eq(DumpValue(output, "MaxHotReloads"), "20", $"{bad}: ignored, the default");
            }
        }

        // engine.json "CloseOnServerDisconnect" (close the client on AC's Disconnected screen):
        // default on and not written while on; the Settings checkbox turning it off writes false,
        // which a later start reads back; turning it on again drops it from the file; a value
        // that isn't true/false is ignored.
        private static void CloseOnServerDisconnectDefaultOnAndSaved()
        {
            string dir = NewDir("dcclose");
            string path = Path.Combine(dir, "engine.json");

            File.WriteAllText(path, LauncherFile);
            Eq(DumpValue(RunChild(path, "dump").Out, "CloseOnServerDisconnect"), "True", "missing: on");
            Eq(RunChild(path, "hook-off", "StartTooltip").Exit, 0, "save with the default");
            Check(!ReadJson(path).TryGetProperty("CloseOnServerDisconnect", out _), "the default isn't written to the file");

            var (exit, output) = RunChild(path, "dcclose-off");
            Eq(exit, 0, "turn off (" + output.Trim() + ")");
            Eq(ReadJson(path).GetProperty("CloseOnServerDisconnect").GetRawText(), "false", "off is saved");
            Check(ReadJson(path).TryGetProperty("LauncherWindow", out _), "launcher keys kept");
            Eq(DumpValue(RunChild(path, "dump").Out, "CloseOnServerDisconnect"), "False", "off read back at the next start");
            Eq(RunChild(path, "hook-off", "CheckTooltip").Exit, 0, "another save");
            Eq(ReadJson(path).GetProperty("CloseOnServerDisconnect").GetRawText(), "false", "off survives other saves");

            Eq(RunChild(path, "dcclose-on").Exit, 0, "turn on");
            Check(!ReadJson(path).TryGetProperty("CloseOnServerDisconnect", out _), "on again: dropped from the file");
            Eq(DumpValue(RunChild(path, "dump").Out, "CloseOnServerDisconnect"), "True", "on read back");

            foreach (string bad in new[] { "\"no\"", "0", "null" })
            {
                File.WriteAllText(path, LauncherFile.Replace("\"LoggingLevel\"", $"\"CloseOnServerDisconnect\": {bad}, \"LoggingLevel\""));
                Eq(DumpValue(RunChild(path, "dump").Out, "CloseOnServerDisconnect"), "True", $"{bad}: ignored, on");
            }
        }

        // /rc server names ilt add|remove (2026.10.5.46): the first edit starts from the defaults,
        // the edit is saved with the launcher's keys kept, and repeats, unknown, empty and overlong
        // patterns change nothing.
        private static void IltServerNamesEdit()
        {
            string dir = NewDir("iltnames");
            string path = Path.Combine(dir, "engine.json");
            File.WriteAllText(path, LauncherFile);
            JsonElement before = ReadJson(path);

            Eq(DumpValue(RunChild(path, "dump").Out, "IltServerNames"), "(none)", "no list in the file");
            Eq(DumpValue(RunChild(path, "ilt-add", "Frosthaven").Out, "Changed"), "True", "add");
            string defaults = string.Join("|", RynthCore.Engine.Compatibility.IltDetect.DefaultServerNames);
            Eq(DumpValue(RunChild(path, "dump").Out, "IltServerNames"), defaults + "|Frosthaven", "the defaults kept, the new one added");
            Eq(Raw(ReadJson(path).GetProperty("LauncherWindow")), Raw(before.GetProperty("LauncherWindow")), "the launcher's keys kept");
            NoTempFilesLeft(dir);

            Eq(DumpValue(RunChild(path, "ilt-add", "FROSTHAVEN").Out, "Changed"), "False", "adding again (any case) changes nothing");
            Eq(DumpValue(RunChild(path, "ilt-remove", "nope").Out, "Changed"), "False", "removing an unknown pattern changes nothing");
            Eq(DumpValue(RunChild(path, "ilt-add", " ").Out, "Changed"), "False", "an empty pattern is refused");
            Eq(DumpValue(RunChild(path, "ilt-add", new string('x', EngineSettings.MaxIltServerNameLength + 1)).Out, "Changed"), "False",
                "an overlong pattern is refused");
            Eq(DumpValue(RunChild(path, "ilt-remove", "frosthaven").Out, "Changed"), "True", "remove (any case)");
            Eq(DumpValue(RunChild(path, "dump").Out, "IltServerNames"), defaults, "back to the defaults");
        }

        /// <summary>The value of a "Name=value" line of the child's dump output ("" if none).</summary>
        private static string DumpValue(string output, string name)
        {
            foreach (string line in output.Split('\n'))
            {
                string l = line.TrimEnd('\r');
                if (l.StartsWith(name + "=", StringComparison.Ordinal)) return l.Substring(name.Length + 1);
            }
            return "";
        }

        // ── EngineSettingsFile, direct ──────────────────────────────────────────

        private static byte[] Owned(string json) => Encoding.UTF8.GetBytes(json);
        private static readonly HashSet<string> Keys = new(StringComparer.Ordinal) { "A", "B", "Optional" };

        private static void MergeKeepsForeignKeys()
        {
            string existing = """{ "A": 1, "Foreign": { "x": [1, {"y": null}] }, "Optional": true, "B": "old", "Other": "kept", "Foreign": 99 }""";
            byte[]? merged = EngineSettingsFile.Merge(existing, Owned("""{ "A": 2, "B": "new" }"""), Keys, out string? err);
            Check(merged != null && err == null, "merged");
            JsonElement r = JsonDocument.Parse(merged!).RootElement;
            Eq(string.Join(",", r.EnumerateObject().Select(p => p.Name)), "A,B,Foreign,Other", "ours first in our order, then the others in file order, each once");
            Eq(r.GetProperty("A").GetInt32(), 2, "owned A updated");
            Eq(r.GetProperty("B").GetString(), "new", "owned B updated");
            Eq(Raw(r.GetProperty("Foreign")), """{"x":[1,{"y":null}]}""", "nested foreign object kept whole (first of a duplicate key)");
            Check(!r.TryGetProperty("Optional", out _), "an owned key not written this time is left out");

            // A key we write but forgot to list as owned is still written once (ours wins).
            byte[]? m2 = EngineSettingsFile.Merge("""{ "New": "file" }""", Owned("""{ "New": "ours" }"""), Keys, out _);
            JsonElement r2 = JsonDocument.Parse(m2!).RootElement;
            Eq(r2.EnumerateObject().Count(), 1, "unlisted key written once");
            Eq(r2.GetProperty("New").GetString(), "ours", "ours wins");

            // Keys are case-sensitive, like the engine's reads: "a" is not "A".
            byte[]? m3 = EngineSettingsFile.Merge("""{ "a": 5 }""", Owned("""{ "A": 1 }"""), Keys, out _);
            Eq(JsonDocument.Parse(m3!).RootElement.GetProperty("a").GetInt32(), 5, "a differently-cased key is someone else's");
        }

        private static void MergeEmptyOrMissingFile()
        {
            foreach (string? existing in new[] { null, "", "  \r\n " })
            {
                byte[]? merged = EngineSettingsFile.Merge(existing, Owned("""{ "A": 1 }"""), Keys, out string? err);
                Check(merged != null && err == null, $"'{existing}': merged");
                Eq(Raw(JsonDocument.Parse(merged!).RootElement), """{"A":1}""", $"'{existing}': only ours");
            }
        }

        private static void MergeRefusesUnreadableFile()
        {
            foreach (string bad in new[] { "{ \"A\": ", "nope", "[1]", "\"text\"", "null", "{ \"A\": 1 } trailing" })
            {
                byte[]? merged = EngineSettingsFile.Merge(bad, Owned("""{ "A": 1 }"""), Keys, out string? err);
                Check(merged == null && !string.IsNullOrEmpty(err), $"'{bad}': refused with a reason");
            }
        }

        private static void WriteAtomicCreatesAndReplaces()
        {
            string dir = NewDir("atomic");
            string path = Path.Combine(dir, "engine.json");
            EngineSettingsFile.WriteAtomic(path, Encoding.UTF8.GetBytes("{\"v\":1}"));
            Eq(File.ReadAllText(path), "{\"v\":1}", "created");
            EngineSettingsFile.WriteAtomic(path, Encoding.UTF8.GetBytes("{\"v\":2}"));
            Eq(File.ReadAllText(path), "{\"v\":2}", "replaced");
            NoTempFilesLeft(dir);

            // A file another process holds open without delete sharing: the swap fails after its
            // retries, the old content stays whole, and the temp file is cleaned up.
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bool threw = false;
                try { EngineSettingsFile.WriteAtomic(path, Encoding.UTF8.GetBytes("{\"v\":3}")); }
                catch (IOException) { threw = true; }
                catch (UnauthorizedAccessException) { threw = true; }
                Check(threw, "a locked file makes the save fail (the caller logs it)");
            }
            Eq(File.ReadAllText(path), "{\"v\":2}", "locked: old content intact");
            NoTempFilesLeft(dir);
        }

        // Every key EngineSettings.cs reads or writes must be in OwnedKeys (and the other way round):
        // a new setting left off the list would be kept from the old file instead of saved.
        private static void OwnedKeysMatchEngineSettings()
        {
            string source = File.ReadAllText(FindUp(Path.Combine("src", "RynthCore.Engine", "Plugins", "EngineSettings.cs")));
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in Regex.Matches(source, @"TryGetProperty\(""(\w+)"""))
                used.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(source, @"\bw\.Write\w*\(""(\w+)"""))
                used.Add(m.Groups[1].Value);
            Check(used.Count >= 16, $"found the keys in EngineSettings.cs ({used.Count})");
            foreach (string key in used)
                Check(EngineSettings.OwnedKeys.Contains(key), $"EngineSettings.OwnedKeys lists \"{key}\"");
            foreach (string key in EngineSettings.OwnedKeys)
                Check(used.Contains(key), $"OwnedKeys \"{key}\" is read or written by EngineSettings.cs");
        }

        private static string FindUp(string relative)
        {
            for (DirectoryInfo? d = new(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                string p = Path.Combine(d.FullName, relative);
                if (File.Exists(p)) return p;
            }
            throw new FileNotFoundException(relative);
        }
    }
}
