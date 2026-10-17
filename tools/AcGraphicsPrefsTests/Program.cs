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
    // ── Effective values: the character's override wins; FullScreen follows the mode ──
    var cfg = new AcGraphicsConfig { WindowMode = AcGraphicsPrefs.ModeBorderlessFill };
    cfg.Values[AcGraphicsPrefs.ResolutionId] = "1920x1080";
    cfg.Values["Render.TextureFiltering"] = "Anisotropic";
    string kelpie = AcGraphicsPrefs.CharacterKey("InfiniteLeaftide", "Silentkelpie");
    cfg.Characters[kelpie] = new AcGraphicsOverride { Resolution = "1280x720", WindowMode = AcGraphicsPrefs.ModeFullScreen };

    Check(kelpie == "infiniteleaftide|silentkelpie", "character key is server|character, lower case");
    Check(AcGraphicsPrefs.CharacterKey("", "X") == "" && AcGraphicsPrefs.CharacterKey("S", " ") == "", "no key without server and character");
    Check(cfg.EffectiveResolution(kelpie) == "1280x720", "override resolution wins");
    Check(cfg.EffectiveResolution("other|char") == "1920x1080", "global resolution otherwise");
    Check(cfg.EffectiveWindowMode(kelpie) == AcGraphicsPrefs.ModeFullScreen, "override window mode wins");
    Check(cfg.EffectiveWindowMode("") == AcGraphicsPrefs.ModeBorderlessFill, "global window mode without a character");

    Dictionary<string, string> forKelpie = cfg.IniValuesFor(kelpie);
    Check(forKelpie[AcGraphicsPrefs.FullScreenId] == "True", "full screen mode writes FullScreen=True");
    Check(forKelpie[AcGraphicsPrefs.ResolutionId] == "1280x720", "INI gets the override resolution");
    Dictionary<string, string> forOther = cfg.IniValuesFor("other|char");
    Check(forOther[AcGraphicsPrefs.FullScreenId] == "False", "borderless writes FullScreen=False");
    Check(forOther["Render.TextureFiltering"] == "Anisotropic", "render values pass through");

    var empty = new AcGraphicsConfig();
    Check(!empty.HasAny && empty.IniValuesFor("a|b").Count == 0, "nothing set: nothing written");

    // ── graphics.json round-trip ──
    string json = Path.Combine(dir, "graphics.json");
    cfg.Save(json);
    AcGraphicsConfig back = AcGraphicsConfig.Load(json);
    Check(back.WindowMode == AcGraphicsPrefs.ModeBorderlessFill, "window mode round-trips");
    Check(back.Values.Count == 2 && back.Values["render.texturefiltering"] == "Anisotropic", "values round-trip (ids case-insensitive)");
    Check(back.Characters.TryGetValue(kelpie, out AcGraphicsOverride? o) && o.Resolution == "1280x720" && o.WindowMode == AcGraphicsPrefs.ModeFullScreen,
        "character override round-trips");
    Check(!File.Exists(json + "." + Environment.ProcessId + ".tmp"), "no temp file left");
    File.WriteAllText(json, "{\"windowMode\":\"Bogus\",\"characters\":{\"a|b\":{\"resolution\":\"12x7\"}}}");
    AcGraphicsConfig bad = AcGraphicsConfig.Load(json);
    Check(bad.WindowMode == null && bad.Characters.Count == 0, "unknown mode and bad resolution are dropped");

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
