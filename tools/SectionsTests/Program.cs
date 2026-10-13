using RynthCore.Engine.UI.Sections;

namespace SectionsTests;

/// <summary>
/// The Mini Remote's sections, offline: per-character server overrides (server_force.txt),
/// plugin manifests (RynthPluginGetSectionsJson) and the section catalog built from them.
/// </summary>
internal static class Program
{
    private static int _checks, _failed;
    private static string _case = "";

    private static int Main()
    {
        ForceText();
        ForceFile();
        ManifestBasics();
        ManifestRejects();
        ManifestLimits();
        CatalogCore();
        CatalogServers();
        CatalogPluginSections();
        DeclaredServers();

        Console.WriteLine();
        Console.WriteLine($"{_checks - _failed}/{_checks} checks passed.");
        if (_checks == 0) { Console.WriteLine("ABORT: no checks ran."); return 1; }
        Console.WriteLine(_failed == 0 ? "ALL SECTIONS TESTS PASSED." : $"{_failed} FAILED.");
        return _failed == 0 ? 0 : 1;
    }

    // ── Overrides ──────────────────────────────────────────────────────

    private static void ForceText()
    {
        Case("force text");
        Check(ServerForceText.TryParse("on", out ServerForce m) && m == ServerForce.On, "on");
        Check(ServerForceText.TryParse(" OFF ", out m) && m == ServerForce.Off, "OFF, padded");
        Check(ServerForceText.TryParse("auto", out m) && m == ServerForce.Auto, "auto");
        Check(!ServerForceText.TryParse("maybe", out _) && !ServerForceText.TryParse(null, out _), "junk and null");
        Check(ServerForceText.Name(ServerForce.On) == "on" && ServerForceText.Name(ServerForce.Auto) == "auto", "names");
        Check(ServerForceText.Apply(ServerForce.On, false), "On wins over not detected");
        Check(!ServerForceText.Apply(ServerForce.Off, true), "Off wins over detected");
        Check(ServerForceText.Apply(ServerForce.Auto, true) && !ServerForceText.Apply(ServerForce.Auto, false), "Auto follows detection");
    }

    private static void ForceFile()
    {
        Case("force file");
        var f = new ServerForceFile();
        Check(f.Set("Kelpie@ILT", "ilt", ServerForce.On), "set on");
        Check(!f.Set("Kelpie@ILT", "ilt", ServerForce.On), "same value again is no change");
        Check(f.Get("kelpie@ilt", "ILT") == ServerForce.On, "keys are case-insensitive");
        Check(f.Get("Other", "ilt") == ServerForce.Auto, "another character is Auto");
        Check(f.Set("Kelpie@ILT", "aelrynth", ServerForce.Off), "a second server");
        Check(f.For("Kelpie@ILT").Count == 2, "For lists both");

        var round = ServerForceFile.Parse(f.Write().Split('\n'));
        Check(round.Get("Kelpie@ILT", "ilt") == ServerForce.On && round.Get("Kelpie@ILT", "aelrynth") == ServerForce.Off, "round trip");

        Check(f.Set("Kelpie@ILT", "ilt", ServerForce.Auto) && f.Get("Kelpie@ILT", "ilt") == ServerForce.Auto, "Auto removes");
        Check(!f.Set("Kelpie@ILT", "ilt", ServerForce.Auto), "removing again is no change");
        Check(!f.Set("bad\tkey", "ilt", ServerForce.On) && !f.Set("k", "a\nb", ServerForce.On), "tabs and line breaks refused");
        Check(!f.Set(new string('x', ServerForceFile.MaxFieldLength + 1), "ilt", ServerForce.On), "overlong key refused");
        Check(!f.Set("", "ilt", ServerForce.On) && !f.Set("k", " ", ServerForce.On), "empty fields refused");

        var damaged = ServerForceFile.Parse(new[] { "# comment", "", "a\tilt", "a\tilt\tmaybe", "a\tilt\tauto", "b\tilt\ton\textra" });
        Check(damaged.For("a").Count == 0, "short, unknown and auto lines are skipped");
        Check(damaged.Get("b", "ilt") == ServerForce.On, "extra fields are ignored");
        Check(ServerForceFile.Parse(null).For("a").Count == 0, "no file");
    }

    // ── Manifests ──────────────────────────────────────────────────────

    private const string GoodManifest = """
    {
      "version": 1,
      "servers": [ { "id": "ilt", "name": "Infinite Leaftide", "worldNames": ["*Leaftide*"], "hosts": ["infiniteleaftide.online"] },
                   { "id": "Frost-Haven", "name": "Frosthaven", "worldNames": ["Frosthaven"], "hosts": [] } ],
      "sections": [ { "id": "quests", "label": "Quests", "order": 650 } ],
      "contributions": [
        { "section": "pets", "server": "", "label": "Pets", "surface": "section:pets", "primary": true, "width": 560, "height": 580 },
        { "section": "skills", "server": "ilt", "label": "Progression", "command": "/rc progression", "tooltip": "line one\nline two" },
        { "section": "quests", "label": "Quests", "surface": "section:quests", "primary": true },
        { "section": "char", "server": "ilt", "label": "Off now", "command": "/ra x", "enabled": false, "note": "Not reported." }
      ],
      "future": { "ignored": true }
    }
    """;

    private static void ManifestBasics()
    {
        Case("manifest basics");
        SectionManifest m = SectionManifestParser.Parse("RynthAi", GoodManifest, out string err);
        Check(err.Length == 0, "no error: " + err);
        Check(m.Plugin == "RynthAi", "plugin name kept");
        Check(m.Servers.Length == 2 && m.Servers[1].Id == "frost-haven", "server ids lower-cased");
        Check(m.Servers[0].WorldNames.SequenceEqual(new[] { "*Leaftide*" }) && m.Servers[0].Hosts.Length == 1, "server lists");
        Check(m.Sections.Length == 1 && m.Sections[0].Order == 650, "a plugin section with its order");
        Check(m.Contributions.Length == 4, "four contributions");
        SectionContribution pets = m.Contributions[0];
        Check(pets.IsSurface && pets.IsUniversal && pets.Primary && pets.Width == 560 && pets.Height == 580, "surface fields");
        SectionContribution prog = m.Contributions[1];
        Check(!prog.IsSurface && prog.Command == "/rc progression" && prog.Server == "ilt" && prog.Enabled, "command fields");
        Check(prog.Tooltip.Contains('\n'), "tooltips keep line breaks");
        Check(m.Contributions[2].Order == 1000, "order defaults to 1000");
        Check(!m.Contributions[3].Enabled && m.Contributions[3].Note == "Not reported.", "disabled with a note");
    }

    private static void ManifestRejects()
    {
        Case("manifest rejects");
        Check(SectionManifestParser.Parse("p", null, out string e).Contributions.Length == 0 && e == "empty", "null");
        Check(SectionManifestParser.Parse("p", "[1,2]", out e).Contributions.Length == 0 && e.Length > 0, "not an object");
        Check(SectionManifestParser.Parse("p", "{ bad", out e).Contributions.Length == 0 && e.StartsWith("bad JSON"), "bad JSON");
        Check(SectionManifestParser.Parse("p", """{"version":0}""", out e).Contributions.Length == 0 && e.Length > 0, "version 0");

        string deep = new string('[', 40) + new string(']', 40);
        Check(SectionManifestParser.Parse("p", "{\"x\":" + deep + "}", out e).Contributions.Length == 0 && e.Length > 0, "too deep");

        SectionManifest m = SectionManifestParser.Parse("p", """
        { "contributions": [
            { "section": "Bad Id!", "label": "x", "command": "/a" },
            { "section": "char", "label": "", "command": "/a" },
            { "section": "char", "label": "no action" },
            { "section": "char", "label": "not a command", "command": "say hi" },
            { "section": "char", "label": "two lines", "command": "/a\n/b" },
            { "section": "char", "label": "popout", "surface": "x:popout" },
            { "section": "char", "label": "odd surface", "surface": "a b" },
            { "section": "char", "label": "bad server", "server": "no way", "command": "/a" },
            { "section": "char", "label": "Line\nbreak", "command": "/ok", "width": -5, "height": 99999 }
        ] }
        """, out e);
        Check(e.Length == 0, "entries are skipped, not the manifest");
        Check(m.Contributions.Length == 1, "only the good entry survives");
        SectionContribution c = m.Contributions.Single();
        Check(c.Label == "Line break", "labels lose line breaks");
        Check(c.Width == 0 && c.Height == SectionManifestParser.MaxSurfaceSize, "sizes clamped");

        Check(SectionManifestParser.Id("ab_c-1") == "ab_c-1" && SectionManifestParser.Id("A B") == "", "ids");
        Check(SectionManifestParser.Id(new string('a', SectionManifestParser.MaxIdLength + 1)) == "", "long ids");
        Check(SectionManifestParser.Command("/x") == "/x" && SectionManifestParser.Command("/") == "", "commands");
        Check(SectionManifestParser.Surface("section:ilt-options") == "section:ilt-options", "a section surface");
    }

    private static void ManifestLimits()
    {
        Case("manifest limits");
        var items = string.Join(",", Enumerable.Range(0, 300).Select(i => $$"""{"section":"char","label":"L{{i}}","command":"/c {{i}}"}"""));
        SectionManifest m = SectionManifestParser.Parse("p", "{\"contributions\":[" + items + "]}", out _);
        Check(m.Contributions.Length == SectionManifestParser.MaxContributions, "contributions capped");

        var servers = string.Join(",", Enumerable.Range(0, 40).Select(i => $$"""{"id":"s{{i}}"}"""));
        m = SectionManifestParser.Parse("p", "{\"servers\":[" + servers + "]}", out _);
        Check(m.Servers.Length == SectionManifestParser.MaxServers, "servers capped");

        string big = "{\"x\":\"" + new string('a', SectionManifestParser.MaxJsonLength) + "\"}";
        Check(SectionManifestParser.Parse("p", big, out string e).Contributions.Length == 0 && e.StartsWith("too large"), "size capped");

        string longLabel = new string('L', 200);
        m = SectionManifestParser.Parse("p", $$"""{"contributions":[{"section":"char","label":"{{longLabel}}","command":"/a"}]}""", out _);
        Check(m.Contributions[0].Label.Length == SectionManifestParser.MaxLabelLength, "labels capped");
    }

    // ── Catalog ────────────────────────────────────────────────────────

    private static readonly ServerState IltOn = new("ilt", "Infinite Leaftide", true, true, ServerForce.Auto, true, "world");
    private static readonly ServerState IltOff = IltOn with { Active = false, Detected = false };
    private static readonly ServerState AelOff = new("aelrynth", "Aelrynth", false, false, ServerForce.Auto, true, "");

    private static bool Panels(string title) => title is "Radar" or "Skills" or "Nav" or "Inventory" or "Items" or "Chat";

    private static void CatalogCore()
    {
        Case("catalog core");
        List<SectionView> views = SectionCatalog.Build(Array.Empty<SectionManifest>(), Panels, new[] { IltOff, AelOff });
        string[] ids = views.Select(v => v.Id).ToArray();
        Check(ids.SequenceEqual(new[] { "char", "map", "skills", "combat", "nav", "inventory", "crafting", "spells", "chat", "friends", "fellowship", "pets" }),
            "the twelve core sections, in order: " + string.Join(",", ids));
        Check(views.All(v => v.Core), "all core");
        Check(views[0].Panes.Single().Kind == SectionPaneKind.Overview, "Char starts with the overview");
        Check(views[1].Panes.Select(p => p.Panel).SequenceEqual(new[] { "Radar" }), "Map has only the panels that exist");
        Check(views[5].Panes.Select(p => p.Panel).SequenceEqual(new[] { "Inventory", "Items" }), "Inventory keeps panel order");
        Check(!views[3].HasContent && !views[11].HasContent, "Combat (no panels here) and Pets are empty");
        Check(SectionCatalog.FindCore("PETS")?.Id == "pets" && SectionCatalog.FindCore("remote") == null, "FindCore");
        Check(SectionCatalog.Build(Array.Empty<SectionManifest>(), _ => throw new InvalidOperationException(), new[] { IltOff })
            .All(v => v.Panes.All(p => p.Kind != SectionPaneKind.CorePanel)), "a throwing panel check means no panels");
    }

    private static void CatalogServers()
    {
        Case("catalog servers");
        SectionManifest m = SectionManifestParser.Parse("RynthAi", GoodManifest, out _);

        List<SectionView> off = SectionCatalog.Build(new[] { m }, Panels, new[] { IltOff, AelOff });
        SectionView pets = off.Single(v => v.Id == "pets");
        Check(pets.Panes.Single().Kind == SectionPaneKind.Surface && pets.Panes[0].Contribution!.Surface == "section:pets", "a primary surface is a pane");
        SectionView skills = off.Single(v => v.Id == "skills");
        Check(skills.Groups.Length == 0, "ILT items hidden while ILT is off");

        List<SectionView> on = SectionCatalog.Build(new[] { m }, Panels, new[] { IltOn, AelOff });
        skills = on.Single(v => v.Id == "skills");
        Check(skills.Groups.Length == 1 && skills.Groups[0].Title == "Server: Infinite Leaftide" && skills.Groups[0].Server == IltOn, "ILT group while active");
        Check(skills.Groups[0].Items.Single().Command == "/rc progression", "its command");
        SectionView chr = on.Single(v => v.Id == "char");
        Check(chr.Groups.Single().Items.Single().Label == "Off now", "disabled items still listed (with their note)");

        var universal = SectionManifestParser.Parse("Other", """
        { "contributions": [ { "section": "skills", "label": "Z", "command": "/z", "order": 5 },
                             { "section": "skills", "label": "A", "command": "/a", "order": 5 } ] }
        """, out _);
        skills = SectionCatalog.Build(new[] { m, universal }, Panels, new[] { IltOn }).Single(v => v.Id == "skills");
        Check(skills.Groups.Select(g => g.Title).SequenceEqual(new[] { "Server: Infinite Leaftide", "Plugins" }), "server groups first, then Plugins");
        Check(skills.Groups[1].Items.Select(i => i.Label).SequenceEqual(new[] { "A", "Z" }), "equal orders sort by label");
    }

    private static void CatalogPluginSections()
    {
        Case("catalog plugin sections");
        SectionManifest m = SectionManifestParser.Parse("RynthAi", GoodManifest, out _);
        List<SectionView> views = SectionCatalog.Build(new[] { m }, Panels, new[] { IltOff });
        int quests = views.FindIndex(v => v.Id == "quests");
        Check(quests >= 0 && !views[quests].Core, "the plugin's Quests section shows");
        Check(views[quests - 1].Id == "crafting" && views[quests + 1].Id == "spells", "order 650 puts it between Crafting (600) and Spells (700)");
        var empty = SectionManifestParser.Parse("p", """{ "sections": [ { "id": "empty", "label": "Empty" } ] }""", out _);
        Check(SectionCatalog.Build(new[] { empty }, Panels, new[] { IltOff }).All(v => v.Id != "empty"), "an empty plugin section is hidden");
        var hijack = SectionManifestParser.Parse("p", """
        { "sections": [ { "id": "pets", "label": "Mine" }, { "id": "remote", "label": "Mine" } ],
          "contributions": [ { "section": "remote", "label": "x", "command": "/x" } ] }
        """, out _);
        views = SectionCatalog.Build(new[] { hijack }, Panels, new[] { IltOff });
        Check(views.Count(v => v.Id == "pets") == 1 && views.Single(v => v.Id == "pets").Label == "Pets", "core ids can't be redeclared");
        Check(views.All(v => v.Id != "remote"), "the remote id can't be declared");
        var dup = SectionManifestParser.Parse("q", """
        { "sections": [ { "id": "quests", "label": "Other quests", "order": 1 } ],
          "contributions": [ { "section": "quests", "label": "x", "command": "/x" } ] }
        """, out _);
        views = SectionCatalog.Build(new[] { m, dup }, Panels, new[] { IltOff });
        SectionView q = views.Single(v => v.Id == "quests");
        Check(q.Label == "Quests" && q.Order == 650, "the first declaration wins");
        Check(q.Panes.Length == 1 && q.Groups.Single().Items.Single().Plugin == "q", "both plugins' contributions land in it");
    }

    private static void DeclaredServers()
    {
        Case("declared servers");
        var decl = new SectionServerDecl("frosthaven", "Frosthaven", new[] { "Frosthaven*" }, new[] { "frosthaven.example" });
        Check(SectionCatalog.DetectDeclared(decl, "play.frosthaven.example", null, out string why) && why.Contains("host"), "by host");
        Check(SectionCatalog.DetectDeclared(decl, null, "Frosthaven PvE", out why) && why.Contains("world"), "by world name");
        Check(!SectionCatalog.DetectDeclared(decl, "infiniteleaftide.online", "InfiniteLeaftide", out _), "not another server");
        var none = new SectionServerDecl("x", "X", Array.Empty<string>(), Array.Empty<string>());
        Check(!SectionCatalog.DetectDeclared(none, "a", "b", out why) && why.StartsWith("no host or world-name list"), "no lists");
    }

    // ── Harness ────────────────────────────────────────────────────────

    private static void Case(string name)
    {
        _case = name;
        Console.WriteLine($"-- {name}");
    }

    private static void Check(bool ok, string what)
    {
        _checks++;
        if (ok) return;
        _failed++;
        Console.WriteLine($"FAIL [{_case}] {what}");
    }
}
