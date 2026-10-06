using System.Globalization;
using RynthCore.Engine.UI.Data;

namespace MetaEditTests;

/// <summary>
/// The Meta editor's rules without AC or ImGui: what a new state may be called, when an edit is
/// saved, which file is backed up once per session, the "Set current" landblock / landcell text,
/// and the rule edits behind "+ New state..." and "+ Add".
/// </summary>
internal static class Program
{
    private static int _checks, _failed;
    private static string _case = "";

    private static int Main()
    {
        StateNames();
        AutosaveDebounce();
        BackupOncePerSession();
        LocationText();
        Placeholder();
        AddCondition();
        AddAction();
        TakesData();
        NoKillsMinutes();
        SameContentAndLocate();

        Console.WriteLine();
        Console.WriteLine(_failed == 0 ? $"PASS ({_checks} checks)" : $"FAIL: {_failed} of {_checks} checks");
        return _failed == 0 ? 0 : 1;
    }

    // ── New state names ──────────────────────────────────────────────────

    private static void StateNames()
    {
        Case("new state names");
        string[] states = { "Default", "Combat", "Loot Run" };
        Eq(MetaStateName.Problem("  Buff  ", states, out string n), null, "a new name is fine");
        Eq(n, "Buff", "trimmed");
        True(MetaStateName.Problem("", states, out _) != null, "empty refused");
        True(MetaStateName.Problem("   ", states, out _) != null, "blank refused");
        True(MetaStateName.Problem(null, states, out _) != null, "null refused");
        True(MetaStateName.Problem("Combat", states, out _) != null, "duplicate refused");
        True(MetaStateName.Problem(" combat ", states, out _)?.Contains("Combat") == true, "duplicate ignoring case, names the existing one");
        True(MetaStateName.Problem("loot run", states, out _) != null, "duplicate with a space, any case");
        True(MetaStateName.Problem("A{b}", states, out _) != null, "braces refused (.af writes STATE: {name})");
        True(MetaStateName.Problem("a\tb", states, out _) != null, "tab refused");
        True(MetaStateName.Problem(new string('x', MetaStateName.MaxLength + 1), states, out _) != null, "too long refused");
        Eq(MetaStateName.Problem(new string('x', MetaStateName.MaxLength), states, out _), null, "longest allowed");
        Eq(MetaStateName.Problem("Default 2", states, out _), null, "a name containing an existing one is fine");
        Eq(MetaStateName.Problem("Ärger", Array.Empty<string>(), out _), null, "non-ASCII fine");
    }

    // ── Autosave ─────────────────────────────────────────────────────────

    private static void AutosaveDebounce()
    {
        Case("autosave debounce");
        var a = new MetaAutosave();
        False(a.Dirty, "clean at start");
        False(a.Due(10_000, typing: false), "nothing to save");
        False(a.Take(), "nothing to take");

        a.Changed(1000);
        True(a.Dirty, "dirty after a change");
        False(a.Due(1000 + 749, typing: false), "not before 750 ms");
        True(a.Due(1000 + 750, typing: false), "due at 750 ms");

        a.Changed(1500);   // another change restarts the clock
        False(a.Due(1000 + 800, typing: false), "a later change restarts the clock");
        True(a.Due(1500 + 750, typing: false), "due 750 ms after the last change");

        False(a.Due(1500 + 5000, typing: true), "never while a box is being typed in");
        True(a.Due(1500 + 5000, typing: false), "due as soon as the box is left");

        True(a.Take(), "take the pending edit");
        False(a.Dirty, "clean after taking");
        False(a.Due(99_999, typing: false), "nothing due after a save");

        a.Changed(2000);
        True(a.Take(), "a flush (Back / close / meta switch) takes it before the debounce");
        False(a.Take(), "only once");

        a.Changed(3000);
        a.Clear();
        False(a.Dirty, "Clear drops it (deleted rule / discarded draft)");

        // Typing burst: one save, not one per key.
        var b = new MetaAutosave();
        int saves = 0;
        for (int t = 0; t <= 3000; t += 16)
        {
            bool typing = t < 2000;
            if (typing && t % 160 == 0) b.Changed(t);   // a key every 160 ms for 2 s
            if (b.Due(t, typing) && b.Take()) saves++;
        }
        Eq(saves, 1, "a 2 s typing burst saves once, after the box is left");
    }

    // ── Backup ───────────────────────────────────────────────────────────

    private static void BackupOncePerSession()
    {
        Case("one backup per session");
        var p = new MetaBackupPlan();
        string farm = Path.Combine("C:\\Games", "RynthSuite", "RynthAi", "MetaFiles", "farm.af");
        Eq(p.Before(farm), (farm, farm + ".bak"), "first save backs the file up");
        Eq(p.Before(farm), null, "not again this session");
        Eq(p.Before(farm.ToUpperInvariant()), null, "same file in another case");
        string other = Path.ChangeExtension(farm, null) + "2.af";
        Eq(p.Before(other), (other, other + ".bak"), "another meta: its own backup");
        Eq(p.Before(farm), null, "switching back: still once per file");
        p.NewSession();
        Eq(p.Before(farm), (farm, farm + ".bak"), "a new session backs up again (overwrites the .bak)");
        Eq(p.Before(""), null, "a meta without a file: nothing");
        Eq(p.Before(null), null, "null path: nothing");

        string met = Path.ChangeExtension(farm, ".met");
        string metAf = Path.ChangeExtension(farm, ".af");
        var q = new MetaBackupPlan();
        Eq(q.Before(met), (metAf, metAf + ".bak"), "a .met meta saves as the .af beside it: that is what is backed up");
        Eq(MetaBackupPlan.WrittenFile(farm), farm, ".af written as is");
    }

    // ── Set current ──────────────────────────────────────────────────────

    private static void LocationText()
    {
        Case("set current (landblock / landcell)");
        Eq(MetaLocation.LandblockText(0xA9B4001C), "A9B40000", "landblock: 8 digits, low word zero");
        Eq(MetaLocation.LandcellText(0xA9B4001C), "A9B4001C", "landcell: the whole cell");
        Eq(MetaLocation.LandcellText(0x00070143), "00070143", "leading zeros kept");
        Eq(MetaLocation.Text(16, 0x8B370105), "8B370000", "Landblock == is condition 16");
        Eq(MetaLocation.Text(17, 0x8B370105), "8B370105", "Landcell == is condition 17");
        Eq(MetaLocation.Text(15, 0x8B370105), null, "other conditions: none");
        True(MetaLocation.TakesPosition(16) && MetaLocation.TakesPosition(17) && !MetaLocation.TakesPosition(18), "TakesPosition");

        // A copy of RynthAi's MetaManager reading (Landblock_EQ / Landcell_EQ); RynthAiHostTests
        // run the real one against MetaLocation.
        foreach (uint cell in new uint[] { 0xA9B4001C, 0x00070143, 0x0000FFFF, 0x00010001, 0xFFFF0100, 0x12340031 })
        {
            True(BlockMatches(MetaLocation.LandblockText(cell), cell), $"landblock text matches its own cell {cell:X8}");
            True(CellMatches(MetaLocation.LandcellText(cell), cell), $"landcell text matches its own cell {cell:X8}");
            False(BlockMatches(MetaLocation.LandblockText(cell), cell ^ 0x00010000), $"not the next landblock {cell:X8}");
            False(CellMatches(MetaLocation.LandcellText(cell), cell ^ 1), $"not the next cell {cell:X8}");
        }
    }

    private static bool BlockMatches(string data, uint cellId)
    {
        uint landblock = cellId >> 16;
        if (uint.TryParse(data, NumberStyles.HexNumber, null, out uint hex))
        {
            if (hex > 0xFFFF) hex >>= 16;
            return landblock == hex;
        }
        return false;
    }

    private static bool CellMatches(string data, uint cellId) =>
        uint.TryParse(data, NumberStyles.HexNumber, null, out uint hex) && cellId == hex;

    // ── Rule edits ───────────────────────────────────────────────────────

    private static void Placeholder()
    {
        Case("a new state's first rule");
        MetaRuleDto r = MetaRuleEdit.Placeholder("Buff");
        Eq(r.State, "Buff", "in the new state");
        Eq(r.Condition, 0, "Never");
        Eq(r.Action, 0, "None");
        True(r.Enabled, "enabled (it can't fire anyway)");
        True(MetaRuleEdit.IsPlaceholder(r), "is the placeholder");
        r.ActionData = "x";
        False(MetaRuleEdit.IsPlaceholder(r), "with data it isn't");
        False(MetaRuleEdit.IsPlaceholder(new MetaRuleDto { Condition = 1 }), "Always isn't");
    }

    private static void AddCondition()
    {
        Case("+ Add condition");
        var r = new MetaRuleDto { State = "S", Condition = 16, ConditionData = "A9B40000" };
        True(MetaRuleEdit.CanAddCondition(r), "can add to a single condition");
        MetaRuleEdit.AddCondition(r);
        Eq(r.Condition, MetaRuleEdit.All, "a single condition becomes an All");
        Eq(r.ConditionData, "", "the All has no data");
        Eq(r.Children.Count, 2, "old + new");
        Eq(r.Children[0].Condition, 16, "the old condition first");
        Eq(r.Children[0].ConditionData, "A9B40000", "with its data");
        Eq(r.Children[1].Condition, MetaRuleEdit.Never, "the new one is Never until picked");
        Eq(r.Children[1].State, "S", "same state");
        MetaRuleEdit.AddCondition(r);
        Eq(r.Children.Count, 3, "an All gets one more");

        var any = new MetaRuleDto { Condition = MetaRuleEdit.Any };
        MetaRuleEdit.AddCondition(any);
        Eq(any.Children.Count, 1, "an empty Any gets its first");

        var not = new MetaRuleDto { Condition = MetaRuleEdit.Not };
        True(MetaRuleEdit.CanAddCondition(not), "an empty Not can take one");
        MetaRuleEdit.AddCondition(not);
        Eq(not.Children.Count, 1, "Not got its operand");
        False(MetaRuleEdit.CanAddCondition(not), "a full Not can't");
        MetaRuleEdit.AddCondition(not);
        Eq(not.Children.Count, 1, "adding to a full Not does nothing");
        Eq(not.Condition, MetaRuleEdit.Not, "still a Not");
    }

    private static void AddAction()
    {
        Case("+ Add action");
        var r = new MetaRuleDto { Action = 1, ActionData = "/say hi" };
        MetaRuleEdit.AddAction(r);
        Eq(r.Action, MetaRuleEdit.DoAll, "a single action becomes a DoAll");
        Eq(r.ActionData, "", "the DoAll has no data");
        Eq(r.ActionChildren.Count, 2, "old + new");
        Eq(r.ActionChildren[0].ActionData, "/say hi", "old first, data kept");
        Eq(r.ActionChildren[1].Action, MetaRuleEdit.NoAction, "the new one is None until picked");
        MetaRuleEdit.AddAction(r);
        Eq(r.ActionChildren.Count, 3, "a DoAll gets one more");
    }

    private static void TakesData()
    {
        Case("which conditions / actions have a value");
        foreach (int c in new[] { 0, 1, 7, 8, 9, 14, 18, 19, 28, 2, 3, 20 })
            False(MetaRuleEdit.ConditionTakesData(c), $"condition {c} has no value");
        foreach (int c in new[] { 4, 5, 6, 10, 11, 12, 13, 15, 16, 17, 21, 22, 23, 24, 25, 26, 27, 29, 30, 31, 32, 33, 34, 35 })
            True(MetaRuleEdit.ConditionTakesData(c), $"condition {c} has a value");
        foreach (int a in new[] { 0, 4, 6, 10, 15 })
            False(MetaRuleEdit.ActionTakesData(a), $"action {a} has no value");
        foreach (int a in new[] { 1, 2, 3, 5, 7, 8, 9, 11, 12, 13, 14 })
            True(MetaRuleEdit.ActionTakesData(a), $"action {a} has a value");

        Case("a new rule is added once filled in");
        False(MetaRuleEdit.ReadyToAdd(new MetaRuleDto { Condition = 1, Action = 1 }), "Always -> Chat with no command: not yet");
        True(MetaRuleEdit.ReadyToAdd(new MetaRuleDto { Condition = 1, Action = 1, ActionData = "/say hi" }), "Always -> /say hi");
        False(MetaRuleEdit.ReadyToAdd(new MetaRuleDto { Condition = 1, Action = 1, ActionData = "   " }), "blank command: not yet");
        False(MetaRuleEdit.ReadyToAdd(new MetaRuleDto { Condition = 16, Action = 6 }), "Landblock with no value: not yet");
        True(MetaRuleEdit.ReadyToAdd(new MetaRuleDto { Condition = 16, ConditionData = "A9B40000", Action = 6 }), "Landblock -> Return");
        True(MetaRuleEdit.ReadyToAdd(MetaRuleEdit.Placeholder("S")), "Never -> None");
        var all = new MetaRuleDto { Condition = MetaRuleEdit.All, Action = 6 };
        False(MetaRuleEdit.ReadyToAdd(all), "an empty All: not yet");
        all.Children.Add(new MetaRuleDto { Condition = 4 });
        False(MetaRuleEdit.ReadyToAdd(all), "an All with an empty Chat Message: not yet");
        all.Children[0].ConditionData = "hello";
        True(MetaRuleEdit.ReadyToAdd(all), "All(Chat Message: hello) -> Return");
        var doAll = new MetaRuleDto { Condition = 1, Action = MetaRuleEdit.DoAll };
        False(MetaRuleEdit.ReadyToAdd(doAll), "an empty DoAll: not yet");
        doAll.ActionChildren.Add(new MetaRuleDto { Action = 2 });
        False(MetaRuleEdit.ReadyToAdd(doAll), "DoAll(Set Meta State with no state): not yet");
        doAll.ActionChildren[0].ActionData = "Buff";
        True(MetaRuleEdit.ReadyToAdd(doAll), "DoAll(Set Meta State Buff)");
    }

    // ── No Kills Within Minutes (2026-10-06) ─────────────────────────────

    private static void NoKillsMinutes()
    {
        Case("No Kills Within Minutes: the minutes box");
        Eq(MetaRuleEdit.NoKillsWithinMinutes, 35, "RynthAi's enum value (appended after Vitae % >=)");
        True(MetaRuleEdit.ConditionTakesData(MetaRuleEdit.NoKillsWithinMinutes), "takes a value");
        False(MetaRuleEdit.ReadyToAdd(new MetaRuleDto { Condition = 35, Action = 6 }), "no minutes yet: not added");
        True(MetaRuleEdit.ReadyToAdd(new MetaRuleDto { Condition = 35, ConditionData = "5", Action = 6 }), "5 minutes -> Return");
        foreach (string ok in new[] { "5", "0", "2.5", " 10 ", "0,5", "1e1" })
            True(MetaRuleEdit.MinutesOk(ok), $"'{ok}' is minutes");
        foreach (string bad in new[] { "", "abc", "-1", "5m", "NaN", "Infinity" })
            False(MetaRuleEdit.MinutesOk(bad), $"'{bad}' is not");
        False(MetaRuleEdit.NoKillsTooltip("5").Contains("isn't a number"), "a number: the plain explanation");
        True(MetaRuleEdit.NoKillsTooltip("five").Contains("never holds"), "not a number: says it never holds");
        False(MetaRuleEdit.NoKillsTooltip("").Contains("isn't a number"), "empty: the plain explanation");
    }

    private static void SameContentAndLocate()
    {
        Case("finding the edited rule again");
        MetaRuleDto R(string state, int cond, string data) => new() { State = state, Condition = cond, ConditionData = data, Action = 1, ActionData = "/x" };
        var a = R("S", 4, "a");
        var b = R("S", 4, "b");
        var c = R("T", 4, "a");
        True(MetaRuleEdit.SameContent(a, a.Clone()), "a clone is the same");
        var fired = a.Clone();
        fired.LastFiredMs = 5;
        True(MetaRuleEdit.SameContent(a, fired), "LastFiredMs doesn't count");
        False(MetaRuleEdit.SameContent(a, b), "data differs");
        False(MetaRuleEdit.SameContent(a, c), "state differs");
        var child1 = a.Clone(); child1.Children.Add(R("S", 1, ""));
        var child2 = a.Clone(); child2.Children.Add(R("S", 0, ""));
        False(MetaRuleEdit.SameContent(child1, child2), "children compared");
        False(MetaRuleEdit.SameContent(a, child1), "child count compared");

        var list = new List<MetaRuleDto> { b, a, c };
        Eq(MetaRuleEdit.Locate(list, a, 1), 1, "still at its index");
        Eq(MetaRuleEdit.Locate(list, a, 0), 1, "moved: found by content");
        Eq(MetaRuleEdit.Locate(list, R("S", 4, "zzz"), 2), 2, "not in the snapshot yet: the hint");
        Eq(MetaRuleEdit.Locate(list, a, 7), 1, "a hint past the end: found");
        var dupes = new List<MetaRuleDto> { a.Clone(), b, a.Clone(), c, a.Clone() };
        Eq(MetaRuleEdit.Locate(dupes, a, 3), 2, "duplicates: the nearest to the hint");
    }

    // ── Checks ───────────────────────────────────────────────────────────

    private static void Case(string name)
    {
        _case = name;
        Console.WriteLine("== " + name);
    }

    private static void True(bool ok, string what)
    {
        _checks++;
        if (ok) return;
        _failed++;
        Console.WriteLine($"  FAIL [{_case}] {what}");
    }

    private static void False(bool ok, string what) => True(!ok, what);

    private static void Eq<T>(T actual, T expected, string what)
    {
        _checks++;
        if (EqualityComparer<T>.Default.Equals(actual, expected)) return;
        _failed++;
        Console.WriteLine($"  FAIL [{_case}] {what}: expected <{expected}>, got <{actual}>");
    }
}
