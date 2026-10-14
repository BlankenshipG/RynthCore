using RynthCore.Engine.Compatibility;
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
        SelectionPlan();
        SelectionCurrentPick();
        SelectionActiveAndOwner();
        StateJson();
        RatesRises();
        RatesBank();
        RatesChat();

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
        Check(views[0].Label == SectionCatalog.SessionStatsLabel && views[0].Label == "Session Stats", "the char section is labelled Session Stats");

        // A plugin's universal primary surface in char (RynthAi's session stats) replaces the overview.
        var session = SectionManifestParser.Parse("RynthAi", """
        { "contributions": [ { "section": "char", "label": "Session stats", "surface": "section:session", "primary": true } ] }
        """, out _);
        SectionView chr = SectionCatalog.Build(new[] { session }, Panels, new[] { IltOff }).Single(v => v.Id == "char");
        Check(chr.Panes.Single().Kind == SectionPaneKind.Surface && chr.Panes[0].Contribution!.Surface == "section:session",
            "a universal primary surface drops the overview");
        // A server-scoped primary surface doesn't: the overview stays beside it.
        var scoped = SectionManifestParser.Parse("p", """
        { "contributions": [ { "section": "char", "server": "ilt", "label": "ILT", "surface": "section:ilt", "primary": true } ] }
        """, out _);
        chr = SectionCatalog.Build(new[] { scoped }, Panels, new[] { IltOn }).Single(v => v.Id == "char");
        Check(chr.Panes.Any(p => p.Kind == SectionPaneKind.Overview), "a server-scoped surface keeps the overview");
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

    // -- Server selection (Diagnostics picker, "/rc server pick", plugin API v85) --

    /// <summary>A server row as SectionRegistry builds it (Active = override applied to detection).</summary>
    private static ServerState Srv(string id, bool detected, ServerForce force, bool builtIn = false)
        => new(id, id.ToUpperInvariant(), ServerForceText.Apply(force, detected), detected, force, builtIn, detected ? "host matches" : "");

    private static void SelectionPlan()
    {
        Case("selection plan");
        var servers = new[] { Srv("ilt", true, ServerForce.Auto, true), Srv("aelrynth", false, ServerForce.Off, true), Srv("frost", false, ServerForce.Auto) };

        var pick = ServerSelection.Plan(servers, "FROST");
        Check(pick != null && pick.Count == 3, "a known id plans every server");
        Check(pick!.Single(p => p.Id == "frost").Force == ServerForce.On, "the pick is forced On (case-insensitive)");
        Check(pick.Where(p => p.Id != "frost").All(p => p.Force == ServerForce.Off), "every other server is forced Off");

        var auto = ServerSelection.Plan(servers, " Auto ");
        Check(auto != null && auto.All(p => p.Force == ServerForce.Auto), "auto resets every override");
        Check(ServerSelection.Plan(servers, "")!.All(p => p.Force == ServerForce.Auto), "empty means auto");
        Check(ServerSelection.Plan(servers, null)!.All(p => p.Force == ServerForce.Auto), "null means auto");
        Check(ServerSelection.Plan(servers, "nowhere") == null, "an unknown id is refused");
        Check(ServerSelection.Plan(Array.Empty<ServerState>(), "auto")!.Count == 0, "no servers, empty plan");
    }

    private static void SelectionCurrentPick()
    {
        Case("selection current pick");
        Check(ServerSelection.CurrentPick(new[] { Srv("ilt", true, ServerForce.Auto), Srv("ael", false, ServerForce.Auto) }) == ServerSelection.Auto, "no overrides is auto");
        Check(ServerSelection.CurrentPick(new[] { Srv("ilt", true, ServerForce.Off), Srv("ael", false, ServerForce.On) }) == "ael", "one On, rest Off is that pick");
        Check(ServerSelection.CurrentPick(new[] { Srv("ilt", true, ServerForce.On), Srv("ael", false, ServerForce.On) }) == null, "two On is a mix");
        Check(ServerSelection.CurrentPick(new[] { Srv("ilt", true, ServerForce.On), Srv("ael", false, ServerForce.Auto) }) == null, "one On, another Auto is a mix");
        Check(ServerSelection.CurrentPick(new[] { Srv("ilt", true, ServerForce.Off), Srv("ael", false, ServerForce.Auto) }) == null, "only Offs is a mix");
        Check(ServerSelection.CurrentPick(Array.Empty<ServerState>()) == ServerSelection.Auto, "no servers is auto");

        // A plan applied gives back the same pick.
        var servers = new[] { Srv("ilt", true, ServerForce.Auto), Srv("ael", false, ServerForce.Auto) };
        var applied = ServerSelection.Plan(servers, "ael")!.Select(p => Srv(p.Id, servers.Single(s => s.Id == p.Id).Detected, p.Force)).ToArray();
        Check(ServerSelection.CurrentPick(applied) == "ael", "plan round trip");
    }

    private static void SelectionActiveAndOwner()
    {
        Case("selection active and owner");
        Check(ServerSelection.ActiveServer(new[] { Srv("ilt", true, ServerForce.Auto), Srv("frost", true, ServerForce.Auto) })!.Id == "ilt", "first detected wins");
        Check(ServerSelection.ActiveServer(new[] { Srv("ilt", true, ServerForce.Auto), Srv("frost", false, ServerForce.On) })!.Id == "frost", "forced On beats detection");
        Check(ServerSelection.ActiveServer(new[] { Srv("ilt", true, ServerForce.Off) }) == null, "Off hides a detected server");
        Check(ServerSelection.ActiveServer(Array.Empty<ServerState>()) == null, "none");

        var pack = SectionManifestParser.Parse("RynthServerPack", """
        { "servers": [ { "id": "frost", "name": "Frosthaven", "hosts": [ "frost.example" ] } ] }
        """, out _);
        var ilt = SectionManifestParser.Parse("RynthILT", """
        { "contributions": [ { "section": "skills", "server": "ilt", "label": "A", "command": "/a" },
                             { "section": "char", "server": "ilt", "label": "B", "command": "/b" } ] }
        """, out _);
        var ai = SectionManifestParser.Parse("RynthAi", """
        { "contributions": [ { "section": "skills", "server": "ilt", "label": "C", "command": "/c" },
                             { "section": "skills", "server": "frost", "label": "D", "command": "/d" } ] }
        """, out _);
        var engine = SectionManifestParser.Parse("RynthCore", """
        { "servers": [ { "id": "ilt", "name": "Infinite Leaftide" } ] }
        """, out _);
        var all = new[] { engine, ai, ilt, pack };

        Check(ServerSelection.OwnerPlugin(all, "frost", "RynthCore") == "RynthServerPack", "the declaring plugin owns a server");
        Check(ServerSelection.OwnerPlugin(all, "ILT", "RynthCore") == "RynthILT", "else the most contributions (engine skipped)");
        Check(ServerSelection.OwnerPlugin(all, "nowhere", "RynthCore") == "", "nobody");
        var tie = SectionManifestParser.Parse("Alpha", """
        { "contributions": [ { "section": "skills", "server": "x", "label": "A", "command": "/a" } ] }
        """, out _);
        var tie2 = SectionManifestParser.Parse("Beta", """
        { "contributions": [ { "section": "skills", "server": "x", "label": "A", "command": "/a" } ] }
        """, out _);
        Check(ServerSelection.OwnerPlugin(new[] { tie2, tie }, "x", "RynthCore") == "Alpha", "ties go to the name that sorts first");
    }

    private static unsafe void StateJson()
    {
        Case("state json");
        var ilt = SectionManifestParser.Parse("RynthILT", """
        { "contributions": [ { "section": "skills", "server": "ilt", "label": "A", "command": "/a" } ] }
        """, out _);
        var servers = new[] { Srv("ilt", true, ServerForce.Auto, true), Srv("aelrynth", false, ServerForce.Auto, true) };
        string json = ServerStateJson.Build(servers, new[] { ilt }, 7, "infiniteleaftide.online", "Infinite \"Leaf\"", "RynthCore");

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        Check(root.GetProperty("schema").GetInt32() == ServerStateJson.Schema && root.GetProperty("revision").GetInt32() == 7, "schema and revision");
        Check(root.GetProperty("host").GetString() == "infiniteleaftide.online", "host");
        Check(root.GetProperty("world").GetString() == "Infinite \"Leaf\"", "world (quotes escaped)");
        Check(root.GetProperty("pick").GetString() == "auto", "pick auto");
        Check(root.GetProperty("active").GetString() == "ilt" && root.GetProperty("activeName").GetString() == "ILT", "active server");
        Check(root.GetProperty("activePlugin").GetString() == "RynthILT", "active server's owner plugin");
        var rows = root.GetProperty("servers");
        Check(rows.GetArrayLength() == 2, "every server listed");
        var first = rows[0];
        Check(first.GetProperty("id").GetString() == "ilt" && first.GetProperty("active").GetBoolean() && first.GetProperty("detected").GetBoolean(), "row state");
        Check(first.GetProperty("force").GetString() == "auto" && first.GetProperty("builtIn").GetBoolean(), "row override and built-in");
        Check(rows[1].GetProperty("plugin").GetString() == "" && rows[1].GetProperty("reason").GetString() == "", "row with no owner");

        var mixed = new[] { Srv("ilt", true, ServerForce.On), Srv("ael", false, ServerForce.On) };
        using (var m = System.Text.Json.JsonDocument.Parse(ServerStateJson.Build(mixed, Array.Empty<SectionManifest>(), 1, null, null, "RynthCore")))
            Check(m.RootElement.GetProperty("pick").GetString() == "mixed" && m.RootElement.GetProperty("host").GetString() == "", "mixed pick, null host");
        using (var n = System.Text.Json.JsonDocument.Parse(ServerStateJson.Build(new[] { Srv("ilt", false, ServerForce.Auto) }, Array.Empty<SectionManifest>(), 1, "h", "w", "RynthCore")))
            Check(n.RootElement.GetProperty("active").GetString() == "" && n.RootElement.GetProperty("activePlugin").GetString() == "", "no active server");

        // Write: the plugin API's buffer convention (length returned; nothing written when too small).
        int length = System.Text.Encoding.UTF8.GetByteCount(json);
        byte[] small = new byte[length];
        fixed (byte* p = small)
            Check(ServerStateJson.Write(json, p, small.Length) == length && small.All(b => b == 0), "too small: length only, nothing written");
        Check(ServerStateJson.Write(json, null, 0) == length, "null buffer: length only");
        byte[] big = new byte[length + 1];
        fixed (byte* p = big)
            Check(ServerStateJson.Write(json, p, big.Length) == length, "fits: length");
        Check(System.Text.Encoding.UTF8.GetString(big, 0, length) == json && big[length] == 0, "fits: text and NUL");
    }

    // ── Session rates (status feed / Session Stats) ────────────────────

    private static readonly DateTime T0 = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>XP and character luminance: only rises count, the first read is the baseline.</summary>
    private static void RatesRises()
    {
        Case("rates rises");
        var r = new SessionRates();
        Check(r.View(T0).Xp == 0 && r.View(T0).Seconds == 0, "zeros before the first sample");
        r.Sample(0, new SessionRatesSample(100, 5, null, null, null), T0);
        Check(r.Character == 0, "character 0 is ignored");

        r.Sample(7, new SessionRatesSample(1_000, 50, null, null, null), T0);
        Check(r.View(T0).Xp == 0 && r.View(T0).Lum == 0, "first read is the baseline");
        r.Sample(7, new SessionRatesSample(4_000, 80, null, null, null), T0.AddMinutes(30));
        r.Sample(7, new SessionRatesSample(4_000, 10, null, null, null), T0.AddMinutes(45));   // spent 70 lum
        r.Sample(7, new SessionRatesSample(5_000, 30, null, null, null), T0.AddHours(1));
        SessionRatesView v = r.View(T0.AddHours(1));
        Check(v.Xp == 4_000, "xp is the sum of rises: " + v.Xp);
        Check(v.Lum == 50, "a spend moves the baseline, it doesn't take earnings back: " + v.Lum);
        Check(Math.Abs(v.XpPerHour - 4_000) < 0.001 && Math.Abs(v.LumPerHour - 50) < 0.001, "per hour over one hour");
        Check(r.View(T0).Seconds == 1, "a session is at least one second long");

        r.Sample(8, new SessionRatesSample(9_000, 1, null, null, null), T0.AddHours(2));
        v = r.View(T0.AddHours(2));
        Check(r.Character == 8 && v.Xp == 0 && v.Lum == 0 && v.StartUtc == T0.AddHours(2), "a new character starts a new session");
    }

    /// <summary>ACECustom's bank (9005) and Aelrynth's banked (9102) / drawn (9103) totals.</summary>
    private static void RatesBank()
    {
        Case("rates bank");
        var r = new SessionRates();
        r.Sample(1, new SessionRatesSample(null, 100, 1_000, null, null), T0);
        r.Sample(1, new SessionRatesSample(null, 100, 1_500, null, null), T0.AddMinutes(1));
        Check(r.View(T0.AddMinutes(1)).Lum == 500, "a bank rise is income");
        r.Sample(1, new SessionRatesSample(null, 400, 1_200, null, null), T0.AddMinutes(2));
        Check(r.View(T0.AddMinutes(2)).Lum == 500, "a withdrawal (bank down, character up) isn't income once the bank is tracked");

        var a = new SessionRates();
        a.Sample(2, new SessionRatesSample(null, 10, null, 0, 0), T0);
        a.Sample(2, new SessionRatesSample(null, 10, null, 300, 0), T0.AddMinutes(1));
        Check(a.View(T0.AddMinutes(1)).Lum == 300, "auto-banked luminance was earned");
        a.Sample(2, new SessionRatesSample(null, 210, null, 300, 200), T0.AddMinutes(2));
        Check(a.View(T0.AddMinutes(2)).Lum == 300, "drawn luminance landing on the character isn't earned");
        a.Sample(2, new SessionRatesSample(null, 260, null, 300, 200), T0.AddMinutes(3));
        Check(a.View(T0.AddMinutes(3)).Lum == 350, "a real character rise after a draw still counts");
    }

    /// <summary>Deposits and incoming transfers seen in chat aren't income; the marker expires.</summary>
    private static void RatesChat()
    {
        Case("rates chat");
        Check(SessionRates.TryMovedIntoBank("Deposited 1,234 luminance into your bank.", out long n) && n == 1_234, "a deposit line");
        Check(SessionRates.TryMovedIntoBank("  Received 50 luminance from Kelpie.", out n) && n == 50, "a transfer line, padded");
        Check(!SessionRates.TryMovedIntoBank("Kelpie tells you, \"Deposited 5 luminance\"", out _), "a quoted line isn't a deposit");
        Check(!SessionRates.TryMovedIntoBank("Deposited luminance", out _) && !SessionRates.TryMovedIntoBank(null, out _), "no amount, null");
        Check(!SessionRates.TryMovedIntoBank("Deposited 99999999999999999999 luminance", out _), "overflow is rejected");

        var r = new SessionRates();
        r.Sample(1, new SessionRatesSample(null, 1_000, 0, null, null), T0);
        r.OnChat("Deposited 600 luminance", T0.AddSeconds(5));
        r.Sample(1, new SessionRatesSample(null, 400, 600, null, null), T0.AddSeconds(6));
        Check(r.View(T0.AddSeconds(6)).Lum == 0, "a deposit is moved money");
        r.Sample(1, new SessionRatesSample(null, 400, 700, null, null), T0.AddSeconds(7));
        Check(r.View(T0.AddSeconds(7)).Lum == 100, "the next bank rise after the deposit is income");

        r.OnChat("Received 50 luminance from Someone", T0.AddSeconds(10));
        r.Sample(1, new SessionRatesSample(null, 400, 800, null, null), T0.AddSeconds(100));
        Check(r.View(T0.AddSeconds(100)).Lum == 200, "the marker expires after 60 seconds");
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
