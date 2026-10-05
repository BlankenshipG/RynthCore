using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using RynthCore.Engine.Plugins;
using RynthCore.PluginSdk;
using RynthCore.PluginSdk.Manifest;

// What the SDK targets add to a plugin built with RynthPluginMinEngineApi=71 (see ManifestAttributeDrivesRuntimeMinimum).
[assembly: RynthPluginManifest(71)]

// Offline tests for plugin manifests: parsing, the engine's gate (API + dependencies + order),
// reading the embedded resource from PE files, and the SDK's API version. See the csproj.
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
    private const uint Api = PluginContractVersion.Current;

    private static int Main(string[] args)
    {
        _root = Path.Combine(Path.GetTempPath(), "rynth-manifest-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        try
        {
            Run(nameof(ParsesFullManifest), ParsesFullManifest);
            Run(nameof(ParsesShorthandAndIgnoresUnknown), ParsesShorthandAndIgnoresUnknown);
            Run(nameof(RejectsBrokenManifests), RejectsBrokenManifests);
            Run(nameof(RoundTripsThroughJson), RoundTripsThroughJson);
            Run(nameof(EngineApiCheck), EngineApiCheck);
            Run(nameof(VersionComparisons), VersionComparisons);
            Run(nameof(NoManifestsLoadExactlyAsListed), NoManifestsLoadExactlyAsListed);
            Run(nameof(TooNewPluginIsRefusedOthersKeepOrder), TooNewPluginIsRefusedOthersKeepOrder);
            Run(nameof(RequiredDependencyMissingRefuses), RequiredDependencyMissingRefuses);
            Run(nameof(OptionalDependencyNeverRefusesOrReorders), OptionalDependencyNeverRefusesOrReorders);
            Run(nameof(RefusalCascadesDownTheChain), RefusalCascadesDownTheChain);
            Run(nameof(RequiredDependenciesStartFirst), RequiredDependenciesStartFirst);
            Run(nameof(DependencyVersions), DependencyVersions);
            Run(nameof(DependencyOnPluginWithoutManifest), DependencyOnPluginWithoutManifest);
            Run(nameof(RequiredCycleRefusesNothing), RequiredCycleRefusesNothing);
            Run(nameof(CheckRequiredStartedAtInit), CheckRequiredStartedAtInit);
            Run(nameof(ReadsResourceFromPe32AndPe32Plus), ReadsResourceFromPe32AndPe32Plus);
            Run(nameof(NoResourceAndNotAPe), NoResourceAndNotAPe);
            Run(nameof(GatePlanFromFiles), GatePlanFromFiles);
            Run(nameof(SdkApiVersionCannotDrift), SdkApiVersionCannotDrift);
            Run(nameof(TrainingStructsMirror), TrainingStructsMirror);
            Run(nameof(ManifestAttributeDrivesRuntimeMinimum), ManifestAttributeDrivesRuntimeMinimum);
            foreach (string dll in args)
                Run("Plugin " + Path.GetFileName(dll), () => CheckBuiltPlugin(dll));
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

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static RynthPluginManifest M(string name, uint min = 66, string version = "2026.10.5.1", uint max = 0,
                                         params (string Name, string Min, bool Optional)[] deps)
    {
        var m = new RynthPluginManifest { Name = name, Version = version, MinEngineApi = min, MaxEngineApi = max };
        foreach (var d in deps) m.Dependencies.Add(new RynthPluginDependency { Name = d.Name, MinVersion = d.Min, Optional = d.Optional });
        return m;
    }

    private static PluginCandidate C(string name, RynthPluginManifest? m = null, string fileVersion = "") =>
        new($@"C:\Games\RynthSuite\{name}\RynthCore.Plugin.{name}.dll", m, fileVersion);

    private static string Names(IEnumerable<PluginCandidate> list) => string.Join(",", list.Select(c => c.Name));

    // ── Parsing ──────────────────────────────────────────────────────────────

    private static void ParsesFullManifest()
    {
        var m = RynthPluginManifest.Parse("""
            {
              "schema": 1,
              "name": "RynthOracle",
              "version": "2026.10.5.1",
              "minEngineApi": 71,
              "maxEngineApi": 90,
              "dependencies": [
                { "name": "RynthAi", "optional": true },
                { "name": "RynthNav", "minVersion": "2026.10.1.1" }
              ],
              "author": "RynthSuite",
              "description": "Quests, character, titles and leaderboards.",
              "homepage": "https://aelrynth.com/rynth.html",
              "license": "MIT"
            }
            """);
        Eq(m.Name, "RynthOracle", "name");
        Eq(m.Version, "2026.10.5.1", "version");
        Eq(m.MinEngineApi, 71u, "minEngineApi");
        Eq(m.MaxEngineApi, 90u, "maxEngineApi");
        Eq(m.Dependencies.Count, 2, "dependency count");
        Check(m.Dependencies[0].Optional && m.Dependencies[0].Name == "RynthAi", "first dependency optional RynthAi");
        Check(!m.Dependencies[1].Optional && m.Dependencies[1].MinVersion == "2026.10.1.1", "second dependency required with min version");
        Eq(m.Author, "RynthSuite", "author");
        Eq(m.License, "MIT", "license");
        Eq(m.Homepage, "https://aelrynth.com/rynth.html", "homepage");
    }

    private static void ParsesShorthandAndIgnoresUnknown()
    {
        var m = RynthPluginManifest.Parse("""{ "schema": 2, "name": "X", "minEngineApi": "70", "dependencies": ["RynthNav@2026.10.1.1", "RynthAi"], "futureField": { "a": 1 } }""");
        Eq(m.Schema, 2, "a later schema is read");
        Eq(m.MinEngineApi, 70u, "minEngineApi as a string");
        Eq(m.Dependencies[0].Name, "RynthNav", "shorthand name");
        Eq(m.Dependencies[0].MinVersion, "2026.10.1.1", "shorthand version");
        Check(!m.Dependencies[1].Optional, "shorthand is required");
        var bare = RynthPluginManifest.Parse("""{ "name": "Bare" }""");
        Eq(bare.MinEngineApi, 0u, "no minEngineApi means no limit");
        Eq(bare.Dependencies.Count, 0, "no dependencies");
    }

    private static void RejectsBrokenManifests()
    {
        string[] bad =
        {
            "not json",
            "[]",
            """{ "version": "1.0" }""",
            """{ "name": "" }""",
            """{ "name": "X", "minEngineApi": 76, "maxEngineApi": 70 }""",
            """{ "name": "X", "minEngineApi": -1 }""",
            """{ "name": "X", "minEngineApi": "abc" }""",
            """{ "name": "X", "dependencies": ["X"] }""",
            """{ "name": "X", "dependencies": [{ "name": "Y", "minVersion": "soon" }] }""",
            """{ "name": "X", "dependencies": [42] }""",
            """{ "name": "X", "dependencies": {} }""",
            """{ "name": "X", "schema": 0 }""",
        };
        foreach (string json in bad)
        {
            Check(RynthPluginManifest.TryParse(json, out string err) == null && err.Length > 0, $"refused: {json}");
        }
    }

    private static void RoundTripsThroughJson()
    {
        var m = M("RynthAi", 66, "2026.10.5.1", 0, ("RynthNav", "", true), ("RynthLua", "2026.9.1.1", false));
        m.Description = "The bot: \"combat\", buffs\nand more";
        m.License = "MIT";
        var back = RynthPluginManifest.Parse(m.ToJson());
        Eq(back.Name, m.Name, "name");
        Eq(back.Description, m.Description, "description with quotes and newline");
        Eq(back.Dependencies.Count, 2, "dependencies");
        Check(back.Dependencies[0].Optional && !back.Dependencies[1].Optional, "optional flags");
        Eq(back.Dependencies[1].MinVersion, "2026.9.1.1", "min version");
        Check(!m.ToJson().Contains("maxEngineApi"), "no maxEngineApi written when unset");
    }

    private static void EngineApiCheck()
    {
        Eq(M("RynthOracle", 77).CheckEngine(76), "RynthOracle needs a newer RynthCore (API 77)", "too new");
        Check(M("RynthOracle", 76).CheckEngine(76) == null, "exact minimum runs");
        Check(M("RynthOracle", 66).CheckEngine(76) == null, "older minimum runs");
        Check(M("Old", 60, max: 70).CheckEngine(76)?.Contains("too old") == true, "past maxEngineApi refused");
        Check(M("Old", 60, max: 76).CheckEngine(76) == null, "at maxEngineApi runs");
        Eq(RynthPluginManifest.NameFromFile(@"C:\x\RynthCore.Plugin.RynthNav.dll"), "RynthNav", "name from file");
        Eq(RynthPluginManifest.NameFromFile(@"C:\x\Other.dll"), "Other", "name from other file");
    }

    private static void VersionComparisons()
    {
        Check(PluginVersion.Satisfies("2026.10.5.1", "2026.10.1.1"), "newer satisfies");
        Check(PluginVersion.Satisfies("2026.10.1.1", "2026.10.1.1"), "equal satisfies");
        Check(!PluginVersion.Satisfies("2026.9.30.4", "2026.10.1.1"), "older doesn't");
        Check(PluginVersion.Satisfies("2026.10.5.1+8ca322f", "2026.10.5"), "build metadata ignored, 3 vs 4 parts");
        Check(PluginVersion.Satisfies("1.0.0", "2026.10.1.1"), "dev build satisfies anything");
        Check(PluginVersion.Satisfies("1.0.0+abc", "2026.10.1.1"), "dev build with commit satisfies");
        Check(PluginVersion.IsDevBuild("1.0.0.0") && !PluginVersion.IsDevBuild("1.0.1"), "dev build detection");
        Check(!PluginVersion.Satisfies("", "2026.10.1.1"), "no version doesn't satisfy a minimum");
        Check(PluginVersion.Satisfies("", ""), "no minimum, anything goes");
        Check(PluginVersion.TryParse("v2.0.0-beta", out var v) && v.Major == 2, "v prefix and prerelease");
    }

    // ── Resolution (the engine's load plan) ──────────────────────────────────

    private static void NoManifestsLoadExactlyAsListed()
    {
        var list = new[] { C("RynthChat"), C("RynthAi"), C("RynthNav"), C("Zed") };
        PluginResolution r = PluginManifestResolver.Resolve(list, Api);
        Eq(Names(r.Order), "RynthChat,RynthAi,RynthNav,Zed", "order unchanged");
        Eq(r.Refused.Count, 0, "nothing refused");
        Eq(r.Notes.Count, 0, "nothing to say");
    }

    private static void TooNewPluginIsRefusedOthersKeepOrder()
    {
        var list = new[] { C("RynthChat"), C("RynthOracle", M("RynthOracle", Api + 1)), C("RynthAi", M("RynthAi", 66)) };
        PluginResolution r = PluginManifestResolver.Resolve(list, Api);
        Eq(Names(r.Order), "RynthChat,RynthAi", "the rest load in order");
        Eq(r.Refused.Count, 1, "one refused");
        Eq(r.Refused[0].Reason, $"RynthOracle needs a newer RynthCore (API {Api + 1})", "reason fit for chat");
        Check(r.Refused[0].IsEngineMismatch, "engine mismatch flagged");
    }

    private static void RequiredDependencyMissingRefuses()
    {
        var list = new[] { C("RynthLua", M("RynthLua", 66, deps: ("RynthAi", "", false))), C("RynthChat") };
        PluginResolution r = PluginManifestResolver.Resolve(list, Api);
        Eq(Names(r.Order), "RynthChat", "dependent not loaded");
        Eq(r.Refused.Single().Reason, "RynthLua needs the RynthAi plugin, which is not in the plugin list", "reason");
        Check(!r.Refused[0].IsEngineMismatch, "dependency refusal flagged");
    }

    private static void OptionalDependencyNeverRefusesOrReorders()
    {
        // RynthAi and RynthLua can each use the other: optional both ways is no cycle and no reorder.
        var list = new[]
        {
            C("RynthAi", M("RynthAi", 66, deps: new[] { ("RynthNav", "", true), ("RynthLua", "", true), ("RynthNet", "", true) })),
            C("RynthLua", M("RynthLua", 66, deps: ("RynthAi", "", true))),
            C("RynthNet", M("RynthNet", 66, deps: ("RynthAi", "", true))),
        };
        PluginResolution r = PluginManifestResolver.Resolve(list, Api);
        Eq(Names(r.Order), "RynthAi,RynthLua,RynthNet", "order unchanged");
        Eq(r.Refused.Count, 0, "nothing refused");
        Check(r.Notes.Any(n => n.Contains("optional plugin RynthNav is not loaded")), "missing optional noted");
    }

    private static void RefusalCascadesDownTheChain()
    {
        var list = new[]
        {
            C("C", M("C", 66, deps: ("B", "", false))),
            C("B", M("B", 66, deps: ("A", "", false))),
            C("A", M("A", Api + 5)),
            C("D"),
        };
        PluginResolution r = PluginManifestResolver.Resolve(list, Api);
        Eq(Names(r.Order), "D", "only the independent plugin loads");
        Eq(r.Refused.Count, 3, "three refused");
        Check(r.Refused.Any(x => x.Plugin.Name == "B" && x.Reason == "B needs A, which was not loaded"), "B refused for A");
        Check(r.Refused.Any(x => x.Plugin.Name == "C" && x.Reason == "C needs B, which was not loaded"), "C refused for B");
    }

    private static void RequiredDependenciesStartFirst()
    {
        var list = new[]
        {
            C("RynthChat"),
            C("Addon", M("Addon", 66, deps: ("RynthNav", "", false))),
            C("RynthTracker"),
            C("RynthNav", M("RynthNav", 66)),
            C("RynthVision"),
        };
        PluginResolution r = PluginManifestResolver.Resolve(list, Api);
        Eq(Names(r.Order), "RynthChat,RynthNav,Addon,RynthTracker,RynthVision", "RynthNav moved before Addon, the rest as listed");
    }

    private static void DependencyVersions()
    {
        var tooOld = new[] { C("Addon", M("Addon", 66, deps: ("RynthNav", "2026.10.1.1", false))), C("RynthNav", M("RynthNav", 66, "2026.9.30.2")) };
        PluginResolution r = PluginManifestResolver.Resolve(tooOld, Api);
        Eq(Names(r.Order), "RynthNav", "too-old dependency refuses the dependent");
        Eq(r.Refused.Single().Reason, "Addon needs RynthNav 2026.10.1.1 or newer (this one is 2026.9.30.2)", "reason");

        var dev = new[] { C("Addon", M("Addon", 66, deps: ("RynthNav", "2026.10.1.1", false))), C("RynthNav", M("RynthNav", 66, "1.0.0")) };
        r = PluginManifestResolver.Resolve(dev, Api);
        Eq(Names(r.Order), "RynthNav,Addon", "a dev build satisfies");
        Check(r.Notes.Any(n => n.Contains("dev build")), "dev build noted");

        var optOld = new[] { C("Addon", M("Addon", 66, deps: ("RynthNav", "2026.10.1.1", true))), C("RynthNav", M("RynthNav", 66, "2026.9.1.1")) };
        r = PluginManifestResolver.Resolve(optOld, Api);
        Eq(r.Refused.Count, 0, "an old optional dependency refuses nothing");
        Check(r.Notes.Any(n => n.Contains("older")), "old optional noted");
    }

    private static void DependencyOnPluginWithoutManifest()
    {
        // RynthNav from before manifests: found by its file name, versioned by its version resource.
        var list = new[] { C("Addon", M("Addon", 66, deps: ("RynthNav", "2026.10.1.1", false))), C("RynthNav", null, "2026.10.4.3+abcdef") };
        PluginResolution r = PluginManifestResolver.Resolve(list, Api);
        Eq(Names(r.Order), "RynthNav,Addon", "satisfied by file name and version resource");

        var noVersion = new[] { C("Addon", M("Addon", 66, deps: ("RynthNav", "2026.10.1.1", false))), C("RynthNav") };
        r = PluginManifestResolver.Resolve(noVersion, Api);
        Check(r.Refused.Single().Reason.Contains("has no version"), "an unversioned plugin can't meet a minimum");

        var anyVersion = new[] { C("Addon", M("Addon", 66, deps: ("RynthNav", "", false))), C("RynthNav") };
        r = PluginManifestResolver.Resolve(anyVersion, Api);
        Eq(r.Refused.Count, 0, "no minimum: any copy will do");
    }

    private static void RequiredCycleRefusesNothing()
    {
        var list = new[] { C("A", M("A", 66, deps: ("B", "", false))), C("B", M("B", 66, deps: ("A", "", false))), C("Z") };
        PluginResolution r = PluginManifestResolver.Resolve(list, Api);
        Eq(r.Refused.Count, 0, "a cycle isn't a reason to refuse");
        Eq(r.Order.Count, 3, "all load");
        Check(r.Notes.Any(n => n.Contains("cycle")), "cycle noted");
    }

    private static void CheckRequiredStartedAtInit()
    {
        var m = M("Addon", 66, deps: new[] { ("RynthNav", "", false), ("RynthAi", "", true) });
        Check(PluginManifestGate.CheckRequiredStarted(null, _ => null) == null, "no manifest, no check");
        Check(PluginManifestGate.CheckRequiredStarted(m, n => n == "RynthNav" ? true : null) == null, "required started, optional absent: fine");
        Eq(PluginManifestGate.CheckRequiredStarted(m, n => n == "RynthNav" ? false : true), "Addon needs RynthNav, which failed to start", "required failed");
        Eq(PluginManifestGate.CheckRequiredStarted(m, _ => null), "Addon needs RynthNav, which is not loaded", "required missing");
    }

    // ── PE files ─────────────────────────────────────────────────────────────

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr BeginUpdateResourceW(string file, bool deleteExisting);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UpdateResourceW(IntPtr h, IntPtr type, string name, ushort lang, byte[] data, uint cb);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool EndUpdateResourceW(IntPtr h, bool discard);

    /// <summary>What the build's RynthEmbedPluginManifest task does.</summary>
    private static void Embed(string path, string json, string name = RynthPluginManifest.ResourceName)
    {
        IntPtr h = BeginUpdateResourceW(path, false);
        if (h == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        byte[] data = Encoding.UTF8.GetBytes(json);
        if (!UpdateResourceW(h, (IntPtr)RynthPluginManifest.ResourceType, name, 0, data, (uint)data.Length))
        {
            int err = Marshal.GetLastWin32Error();
            EndUpdateResourceW(h, true);
            throw new Win32Exception(err);
        }
        if (!EndUpdateResourceW(h, false)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static string CopyOf(string source, string name)
    {
        string dest = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8], name);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(source, dest);
        return dest;
    }

    private static string ThisDll => typeof(Program).Assembly.Location;
    private static string ThisExe => Path.ChangeExtension(ThisDll, ".exe");

    private static void ReadsResourceFromPe32AndPe32Plus()
    {
        // The test's own IL DLL is PE32 (AnyCPU); its apphost .exe is a native PE32+ image.
        foreach (string source in new[] { ThisDll, ThisExe })
        {
            string copy = CopyOf(source, "RynthCore.Plugin.Probe" + Path.GetExtension(source));
            Check(PluginManifestReader.Read(copy) == null, $"{Path.GetFileName(source)}: no manifest before embedding");
            string json = M("Probe", 71, deps: ("RynthAi", "", true)).ToJson();
            Embed(copy, json);
            RynthPluginManifest? m = PluginManifestReader.Read(copy);
            Check(m != null, $"{Path.GetFileName(source)}: manifest read back");
            Eq(m?.MinEngineApi ?? 0, 71u, $"{Path.GetFileName(source)}: minEngineApi");
            Eq(PluginManifestReader.ReadJson(copy), json, $"{Path.GetFileName(source)}: exact JSON");
            Check(!string.IsNullOrEmpty(System.Diagnostics.FileVersionInfo.GetVersionInfo(copy).ProductVersion),
                  $"{Path.GetFileName(source)}: version resource survives");
            // Replace in place (a rebuild): the new one wins.
            Embed(copy, M("Probe", 72).ToJson());
            Eq(PluginManifestReader.Read(copy)?.MinEngineApi ?? 0, 72u, $"{Path.GetFileName(source)}: re-embedded");
            // Readable while another process has it open for execution-style sharing (the engine's shadow copy case).
            using (new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.Read))
                Check(PluginManifestReader.Read(copy) != null, $"{Path.GetFileName(source)}: readable while open");
        }
        // Lower-case name in the file still matches (resource names are case-insensitive).
        string lower = CopyOf(ThisDll, "lower.dll");
        Embed(lower, M("Lower", 66).ToJson(), "rynth_plugin_manifest");
        Eq(PluginManifestReader.Read(lower)?.Name, "Lower", "case-insensitive resource name");
    }

    private static void NoResourceAndNotAPe()
    {
        string text = Path.Combine(_root, "notes.dll");
        File.WriteAllText(text, "this is not a DLL, just text long enough to have a header-sized body .................................");
        try { PluginManifestReader.Read(text); Check(false, "a text file should throw"); }
        catch (InvalidDataException) { Check(true, "text file refused as not a PE"); }

        string tiny = Path.Combine(_root, "tiny.dll");
        File.WriteAllBytes(tiny, new byte[] { 0x4D, 0x5A });
        try { PluginManifestReader.Read(tiny); Check(false, "a 2-byte file should throw"); }
        catch (InvalidDataException) { Check(true, "truncated file refused"); }

        // Another RCDATA resource, not ours: still "no manifest".
        string other = CopyOf(ThisDll, "other.dll");
        Embed(other, "{}", "SOMETHING_ELSE");
        Check(PluginManifestReader.Read(other) == null, "other resources are not a manifest");
    }

    private static void GatePlanFromFiles()
    {
        string dir = Path.Combine(_root, "suite");
        Directory.CreateDirectory(dir);
        string P(string name) => Path.Combine(dir, $"RynthCore.Plugin.{name}.dll");
        File.Copy(ThisDll, P("RynthChat"));                        // no manifest: loads as before
        File.Copy(ThisDll, P("RynthOracle"));
        Embed(P("RynthOracle"), M("RynthOracle", Api + 1).ToJson());
        File.Copy(ThisDll, P("RynthAi"));
        Embed(P("RynthAi"), M("RynthAi", 66, deps: ("RynthLua", "", true)).ToJson());
        File.Copy(ThisDll, P("Broken"));
        Embed(P("Broken"), "{ \"name\": ");                       // broken JSON: loads as without one
        File.Copy(ThisDll, P("Needy"));
        Embed(P("Needy"), M("Needy", 66, deps: ("RynthNav", "", false)).ToJson());

        var paths = new[] { P("RynthChat"), P("RynthOracle"), P("RynthAi"), P("Broken"), P("Needy") };
        PluginManifestGate.LoadPlan plan = PluginManifestGate.Plan(paths, Api, _ => "2026.10.4.3");
        Eq(string.Join(",", plan.Load.Select(l => RynthPluginManifest.NameFromFile(l.Path))), "RynthChat,RynthAi,Broken", "load list");
        Check(plan.Load.Single(l => l.Path == P("RynthChat")).Manifest == null, "RynthChat has no manifest");
        Check(plan.Load.Single(l => l.Path == P("RynthAi")).Manifest?.Name == "RynthAi", "RynthAi carries its manifest");
        Eq(plan.Refused.Count, 2, "two refused");
        Check(plan.Refused.Any(r => r.Reason == $"RynthOracle needs a newer RynthCore (API {Api + 1})"), "Oracle refused for API");
        Check(plan.Refused.Any(r => r.Reason.StartsWith("Needy needs the RynthNav plugin")), "Needy refused for RynthNav");
        Check(plan.Log.Any(l => l.Contains("Broken") && l.Contains("manifest unreadable")), "broken manifest logged");
        Check(plan.Log.Any(l => l.Contains("optional plugin RynthLua")), "optional note logged");

        PluginManifestGate.LoadPlan none = PluginManifestGate.Plan(new[] { P("RynthChat"), P("Broken") }, Api);
        Eq(none.Load.Count, 2, "no manifests: everything loads");
        Check(none.Log.Any(l => l.Contains("no plugin has a manifest")), "said once");
    }

    // ── The SDK's version ────────────────────────────────────────────────────

    private static void SdkApiVersionCannotDrift()
    {
        Eq(RynthCoreHost.CurrentApiVersion, PluginContractVersion.Current, "RynthCoreHost.CurrentApiVersion");
        Check(RynthCoreHost.BaselineApiVersion <= RynthCoreHost.CurrentApiVersion, "baseline at or below current");

        // The SDK's copy of the table must reach as far as the engine's (same field count, same size).
        int sdkFields = typeof(RynthCoreApiNative).GetFields().Length;
        int engineFields = typeof(RynthCoreAPI).GetFields().Length;
        Eq(sdkFields, engineFields, "RynthCoreApiNative fields vs the engine's RynthCoreAPI (add the new fields to the SDK)");
        Eq(Marshal.SizeOf<RynthCoreApiNative>(), Marshal.SizeOf<RynthCoreAPI>(), "table size");

        // The build targets' copy, used to refuse a RynthPluginMinEngineApi the SDK doesn't know.
        string targets = FindUp(Path.Combine("src", "RynthCore.PluginSdk", "build", "RynthCore.PluginSdk.targets"));
        Match match = Regex.Match(File.ReadAllText(targets), @"<RynthCoreSdkApiVersion>(\d+)</RynthCoreSdkApiVersion>");
        Check(match.Success, "targets declare RynthCoreSdkApiVersion");
        Eq(match.Success ? uint.Parse(match.Groups[1].Value) : 0u, PluginContractVersion.Current, "RynthCoreSdkApiVersion in RynthCore.PluginSdk.targets");
    }

    // v79: the SDK's copies of the training structs must be the engine's, byte for byte.
    private static void TrainingStructsMirror()
    {
        Eq(Marshal.SizeOf<RynthCore.PluginSdk.TrainingInfoNative>(), 64, "SDK TrainingInfoNative size");
        Eq(Marshal.SizeOf<RynthCore.Engine.Plugins.TrainingInfoNative>(), 64, "engine TrainingInfoNative size");
        Eq(Marshal.SizeOf<RynthCore.PluginSdk.TrainingEntryNative>(), 64, "SDK TrainingEntryNative size");
        Eq(Marshal.SizeOf<RynthCore.Engine.Plugins.TrainingEntryNative>(), 64, "engine TrainingEntryNative size");
        foreach (var f in typeof(RynthCore.Engine.Plugins.TrainingInfoNative).GetFields())
            Eq((int)Marshal.OffsetOf<RynthCore.PluginSdk.TrainingInfoNative>(f.Name), (int)Marshal.OffsetOf<RynthCore.Engine.Plugins.TrainingInfoNative>(f.Name), "TrainingInfoNative." + f.Name + " offset");
        foreach (var f in typeof(RynthCore.Engine.Plugins.TrainingEntryNative).GetFields())
            Eq((int)Marshal.OffsetOf<RynthCore.PluginSdk.TrainingEntryNative>(f.Name), (int)Marshal.OffsetOf<RynthCore.Engine.Plugins.TrainingEntryNative>(f.Name), "TrainingEntryNative." + f.Name + " offset");
        Eq(RynthCore.PluginSdk.TrainingInfoFlags.Busy, RynthCore.Engine.Plugins.TrainingInfoFlags.Busy, "info flag bits");
        Eq(RynthCore.PluginSdk.TrainingEntryFlags.Trainable, RynthCore.Engine.Plugins.TrainingEntryFlags.Trainable, "entry flag bits");
        // The three v79 slots are the table's last three, in this order.
        var names = typeof(RynthCore.Engine.Plugins.RynthCoreAPI).GetFields().Select(f => f.Name).TakeLast(3).ToArray();
        Check(names.SequenceEqual(new[] { "GetTrainingInfoFn", "RaiseFn", "TrainSkillFn" }), "v79 slots last: " + string.Join(",", names));
        var sdkNames = typeof(RynthCore.PluginSdk.RynthCoreApiNative).GetFields().Select(f => f.Name).TakeLast(3).ToArray();
        Check(sdkNames.SequenceEqual(names), "SDK v79 slots in the same order");
    }

    private sealed class ProbePlugin : RynthCore.PluginCore.RynthPluginBase { }
    private sealed class OverridingPlugin : RynthCore.PluginCore.RynthPluginBase { public override uint MinimumApiVersion => 70; }

    private static void ManifestAttributeDrivesRuntimeMinimum()
    {
        // RynthPluginBase.MinimumApiVersion defaults to the assembly's [RynthPluginManifest(min)] ...
        Eq(new ProbePlugin().MinimumApiVersion, 71u, "default follows the manifest attribute");
        // ... an explicit override still wins ...
        Eq(new OverridingPlugin().MinimumApiVersion, 70u, "an override wins");
        // ... and an assembly without one (RynthCore.PluginSdk itself has none) gets the baseline.
        Check(typeof(RynthCoreHost).Assembly.GetCustomAttributes(typeof(RynthPluginManifestAttribute), false).Length == 0,
              "the SDK assembly carries no manifest attribute");
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

    // ── Real plugin DLLs (arguments) ─────────────────────────────────────────

    private static void CheckBuiltPlugin(string dll)
    {
        RynthPluginManifest? m = PluginManifestReader.Read(dll);
        Check(m != null, $"{dll}: has a manifest");
        if (m == null) return;
        Console.WriteLine($"     {PluginManifestGate.Describe(m)}");
        Eq(m.Name, RynthPluginManifest.NameFromFile(dll), $"{Path.GetFileName(dll)}: manifest name matches the file");
        Check(m.MinEngineApi >= RynthCoreHost.BaselineApiVersion, $"{m.Name}: minEngineApi {m.MinEngineApi} at or above the SDK baseline");
        Check(m.CheckEngine(PluginContractVersion.Current) == null, $"{m.Name}: runs on this engine (API {PluginContractVersion.Current})");
        Check(m.Description.Length > 0, $"{m.Name}: has a description");
        Check(!string.IsNullOrEmpty(System.Diagnostics.FileVersionInfo.GetVersionInfo(dll).ProductVersion), $"{m.Name}: version resource intact");
    }
}
