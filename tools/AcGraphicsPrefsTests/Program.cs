// Offline checks for Graphics/AcGraphicsPrefs.cs (RynthAi Settings > Graphics). See the .csproj.
using RynthCore.Graphics;

int failures = 0, checks = 0;
void Check(bool ok, string what)
{
    checks++;
    if (ok) return;
    failures++;
    Console.WriteLine("FAIL  " + what);
}

string dir = Path.Combine(Path.GetTempPath(), "AcGraphicsPrefsTests-" + Environment.ProcessId);
Directory.CreateDirectory(dir);
try
{
    // ── Per-account profiles: each account's own; FullScreen follows the mode ──
    string kelpie = AcGraphicsPrefs.AccountKey("InfiniteLeaftide", "silentkelpie");
    string storm = AcGraphicsPrefs.AccountKey("InfiniteLeaftide", "SilentStorm");
    Check(kelpie == "infiniteleaftide|silentkelpie" && storm == "infiniteleaftide|silentstorm", "account key is server|account, lower case");
    Check(AcGraphicsPrefs.AccountKey("", "X") == "" && AcGraphicsPrefs.AccountKey("S", " ") == "", "no key without server and account");

    var kcfg = new AcGraphicsConfig { WindowMode = AcGraphicsPrefs.ModeFullScreen };
    kcfg.Values[AcGraphicsPrefs.ResolutionId] = "1280x720";
    var scfg = new AcGraphicsConfig { WindowMode = AcGraphicsPrefs.ModeBorderlessFill };
    scfg.Values["Render.TextureFiltering"] = "Anisotropic";
    var all = new AcGraphicsProfiles();
    all.Put(kelpie, kcfg);
    all.Put(storm, scfg);
    all.Put("x|y", new AcGraphicsConfig());
    Check(all.Accounts.Count == 2, "an empty profile isn't kept");

    Dictionary<string, string> forKelpie = all.For(kelpie).IniValues();
    Check(forKelpie[AcGraphicsPrefs.FullScreenId] == "True" && forKelpie[AcGraphicsPrefs.ResolutionId] == "1280x720",
        "Kelpie's profile: full screen at 1280x720");
    Dictionary<string, string> forStorm = all.For(storm).IniValues();
    Check(forStorm[AcGraphicsPrefs.FullScreenId] == "False" && !forStorm.ContainsKey(AcGraphicsPrefs.ResolutionId),
        "Storm's profile: borderless, no resolution of Kelpie's");
    Check(forStorm["Render.TextureFiltering"] == "Anisotropic", "render values pass through");
    Check(all.For("infiniteleaftide|nobody").IniValues().Count == 0, "an account without a profile writes nothing");

    // ── graphics.json round-trip; another account's entry survives a save ──
    string json = Path.Combine(dir, "graphics.json");
    all.Save(json);
    AcGraphicsProfiles back = AcGraphicsProfiles.Load(json);
    Check(back.For(kelpie).WindowMode == AcGraphicsPrefs.ModeFullScreen && back.For(kelpie).Values[AcGraphicsPrefs.ResolutionId] == "1280x720",
        "Kelpie's profile round-trips");
    Check(back.For(storm).Values["render.texturefiltering"] == "Anisotropic", "Storm's profile round-trips (ids case-insensitive)");
    Check(!File.Exists(json + "." + Environment.ProcessId + ".tmp"), "no temp file left");

    // A client edits only its own account: load, put, save (as AcGraphics.QueueSave does).
    AcGraphicsProfiles onDisk = AcGraphicsProfiles.Load(json);
    AcGraphicsConfig k2 = onDisk.For(kelpie).Clone();
    k2.Values.Remove(AcGraphicsPrefs.ResolutionId);
    onDisk.Put(kelpie, k2);
    onDisk.Save(json);
    AcGraphicsProfiles after = AcGraphicsProfiles.Load(json);
    Check(!after.For(kelpie).Values.ContainsKey(AcGraphicsPrefs.ResolutionId), "Kelpie's change saved");
    Check(after.For(storm).WindowMode == AcGraphicsPrefs.ModeBorderlessFill, "Storm's profile untouched by Kelpie's save");

    // The 2026.10.18.1 single shared set is ignored (it made every client fight over one resolution).
    File.WriteAllText(json, "{\"values\":{\"Display.Resolution\":\"1760x990\"},\"windowMode\":\"Windowed\",\"characters\":{}}");
    Check(AcGraphicsProfiles.Load(json).Accounts.Count == 0, "old global graphics.json is not applied to anyone");
    File.WriteAllText(json, "{\"accounts\":{\"a|b\":{\"windowMode\":\"Bogus\",\"values\":{}}}}");
    Check(AcGraphicsProfiles.Load(json).Accounts.Count == 0, "unknown mode dropped; empty profile not kept");

    // ── Resolution parsing ──
    Check(AcGraphicsPrefs.TryParseResolution("1760x990", out int w, out int h) && w == 1760 && h == 990, "parses 1760x990");
    Check(!AcGraphicsPrefs.TryParseResolution("800", out _, out _) && !AcGraphicsPrefs.TryParseResolution("100x50", out _, out _),
        "rejects junk and tiny sizes");
    Check(AcGraphicsPrefs.FormatFloat(0.891) == "0.89", "floats as AC writes them");

    // ── Applying to UserPreferences.ini ──
    string ini = Path.Combine(dir, "UserPreferences.ini");
    File.WriteAllText(ini,
        "[Render]\r\nFieldOfView=160.00\r\nTextureFiltering=Bilinear\r\n[Net]\r\nComputeUniquePort=True\r\n[Display]\r\nResolution=1760x990\r\nFullScreen=False\r\n");
    var values = new Dictionary<string, string>
    {
        ["Render.TextureFiltering"] = "Anisotropic",
        ["Display.Resolution"] = "1920x1080",
        ["Display.FullScreen"] = "False",
        ["Render.MultiPassAlpha"] = "False",
        ["Camera.Stiffness"] = "0.50",
    };
    List<string> changed = AcPrefsIni.Apply(ini, values);
    Dictionary<string, string> now = AcPrefsIni.Read(ini);
    Check(changed.Count == 4 && !changed.Contains("Display.FullScreen"), "only changed keys are reported");
    Check(now["Render.TextureFiltering"] == "Anisotropic" && now["Display.Resolution"] == "1920x1080", "keys replaced in place");
    Check(now["Render.MultiPassAlpha"] == "False", "missing key added to its section");
    Check(now["Camera.Stiffness"] == "0.50", "missing section added");
    Check(now["Render.FieldOfView"] == "160.00" && now["Net.ComputeUniquePort"] == "True", "other keys untouched");
    string[] lines = File.ReadAllLines(ini);
    int renderAt = Array.IndexOf(lines, "[Render]"), netAt = Array.IndexOf(lines, "[Net]");
    int mpaAt = Array.IndexOf(lines, "MultiPassAlpha=False");
    Check(mpaAt > renderAt && mpaAt < netAt, "added key sits inside [Render]");

    DateTime stamp = File.GetLastWriteTimeUtc(ini);
    Thread.Sleep(30);
    Check(AcPrefsIni.Apply(ini, values).Count == 0 && File.GetLastWriteTimeUtc(ini) == stamp, "no write when nothing changed");

    string fresh = Path.Combine(dir, "sub", "UserPreferences.ini");
    AcPrefsIni.Apply(fresh, new Dictionary<string, string> { ["Display.Resolution"] = "1280x720" });
    Check(AcPrefsIni.Read(fresh)["Display.Resolution"] == "1280x720", "creates a missing file");

    // ── Which INI files: the game folder's first (the one this install's clients use) ──
    string gameDir = Path.Combine(dir, "game");
    Directory.CreateDirectory(gameDir);
    string gameIni = Path.Combine(gameDir, "UserPreferences.ini");
    Check(AcGraphicsPrefs.IniPaths(gameDir)[0] != gameIni || File.Exists(gameIni), "no game-folder INI: listed only as the fallback");
    File.WriteAllText(gameIni, "[Display]\r\nResolution=1760x990\r\n");
    List<string> paths = AcGraphicsPrefs.IniPaths(gameDir);
    Check(paths.Count >= 1 && paths[0] == gameIni, "game folder's INI comes first when it exists");
    Check(paths.TrueForAll(File.Exists), "only existing files listed when the game folder has one");

    // ── Every Render setting has sane metadata ──
    foreach (AcGraphicsSetting s in AcGraphicsPrefs.RenderSettings)
    {
        Check(s.Section == "Render" && s.Key.Length > 0 && s.Label.Length > 0, $"{s.Id}: section, key, label");
        if (s.Kind == AcGraphicsKind.Choice) Check(s.Choices is { Length: > 1 }, $"{s.Id}: has choices");
        if (s.Kind == AcGraphicsKind.Float) Check(s.Max > s.Min && s.Step > 0, $"{s.Id}: slider range");
    }
    Check(AcGraphicsPrefs.WindowModeIds.Length == AcGraphicsPrefs.WindowModeLabels.Length, "a label per window mode");
}
finally
{
    try { Directory.Delete(dir, recursive: true); } catch { }
}

Console.WriteLine(failures == 0 ? $"PASS ({checks} checks)" : $"{failures} FAILURE(S) of {checks} checks");
return failures == 0 ? 0 : 1;
