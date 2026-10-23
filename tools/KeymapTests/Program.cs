// Offline checks for AcKeymapText (see KeymapTests.csproj).
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.Hotkeys;

namespace KeymapTests;

internal static class Program
{
    private static int _failed, _passed;

    private static void Check(bool ok, string what)
    {
        if (ok) { _passed++; return; }
        _failed++;
        Console.WriteLine("FAIL: " + what);
    }

    private static void Eq<T>(T actual, T expected, string what) =>
        Check(EqualityComparer<T>.Default.Equals(actual, expected), $"{what}: got '{actual}', expected '{expected}'");

    // The shape AC writes (CRLF, two-space groups, four-space bindings), cut down.
    private static readonly string Sample = string.Join("\r\n", new[]
    {
        "#Asheron's Call: Throne of Destiny Keymap File",
        "\"User Defined Keymap\" [ 0000003B-02D0-0000-4002-000016000000 ]",
        "",
        "Devices",
        "[",
        "  Keyboard [ GUID_SysKeyboard ]",
        "  Mouse [ GUID_SysMouse ]",
        "]",
        "",
        "MetaKeys",
        "[",
        "  1 [ 0 DIK_LSHIFT ]",
        "  2 [ 0 DIK_LCONTROL ]",
        "  2 [ 0 DIK_RCONTROL ]",
        "  3 [ 0 DIK_LMENU ]",
        "  3 [ 0 DIK_RALT ]",
        "  5 [ 1 DIMOFS_BUTTON3 ]",
        "]",
        "",
        "Bindings",
        "[",
        "  MovementCommands",
        "  [",
        "    MovementForward [ \"\" [ 0 DIK_W ] ]",
        "    MovementForward [ \"\" [ 0 DIK_UPARROW ] ]",
        "  ]",
        "",
        "  UICommands",
        "  [",
        "    ToggleSpellbookPanel [ \"\" [ 0 DIK_F5 ] ]",
        "    ToggleOptionsPanel [ \"\" [ 0 DIK_F11 ] ]",
        "    TogglePluginManager [ \"\" [ 0 DIK_F1 ] 0x00000003 ]",
        "  ]",
        "",
        "  QuickslotCommands",
        "  [",
        "    UseQuickSlot_1 [ \"\" [ 0 DIK_1 ] ]",
        "    UseQuickSlot_5 [ \"\" [ 0 DIK_5 ] ]",
        "    UseQuickSlot_14 [ \"\" [ 0 DIK_5 ] 0x00000004 ]",
        "    CreateShortcut [ \"\" [ 0 DIK_0 ] ]",
        "    UseQuickSlot_1 [ \"\" [ 0 DIK_1 ] 0x00000002 ]",
        "  ]",
        "",
        "  ToggleChatEntry",
        "",
        "  MouseCommands",
        "  [",
        "    SelectLeft [ \"\" [ 1 DIMOFS_BUTTON0 ] ]",
        "  ]",
        "",
        "  EditControls",
        "  [",
        "    CursorStartOfLine [ \"\" [ 0 DIK_HOME ] ]",
        "    CursorPreviousPage [ \"\" [ 0 DIK_PGUP ] ]",
        "  ]",
        "",
        "  CopyAndPasteControls",
        "  [",
        "    CopyText [ \"\" [ 0 DIK_C ] 0x00000002 ]",
        "  ]",
        "]",
        "",
    });

    private static List<string> Lines(string text = "") => AcKeymapText.Split(text.Length > 0 ? text : Sample, out _);

    private static List<AcKeymapText.Line> Of(List<string> lines, string action) =>
        AcKeymapText.Bindings(lines).FindAll(l => l.Action == action);

    private static int Main()
    {
        Reading();
        Names();
        Meta();
        BindTakes();
        BindKeepsTextGroups();
        BindAlready();
        BindNewSlot();
        BindMissingGroup();
        BindEmptyGroup();
        BindUnknownAction();
        Unbind();
        RoundTrip();
        Describe();
        RealFile();
        Console.WriteLine($"{_passed} passed, {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }

    private static void Reading()
    {
        List<string> lines = Lines();
        List<AcKeymapText.Line> all = AcKeymapText.Bindings(lines);
        Eq(all.Count, 14, "binding lines read");
        List<AcKeymapText.Line> slot1 = Of(lines, "UseQuickSlot_1");
        Eq(slot1.Count, 2, "UseQuickSlot_1 lines");
        Check(slot1.TrueForAll(l => l.Group == "QuickslotCommands"), "UseQuickSlot_1 in QuickslotCommands");
        Eq(slot1[1].Meta, 2u, "Ctrl+1's mask");
        Eq(Of(lines, "MovementForward")[1].Dik, "DIK_UPARROW", "an alias read as its one name");
        Eq(Of(lines, "SelectLeft")[0].Device, 1, "the mouse is device 1");
        Eq(Of(lines, "CursorPreviousPage")[0].Group, "EditControls", "group after an empty group");
        Eq(AcKeymapText.Uses(lines, 0, "DIK_F5", 0).Count, 1, "F5 used once");
        Eq(AcKeymapText.Uses(lines, 0, "DIK_HOME", 0).Count, 0, "Home in a text group isn't a game use");
        Eq(AcKeymapText.Uses(lines, 0, "DIK_5", 4).Count, 1, "Alt+5 used");
    }

    private static void Names()
    {
        Eq(AcKeymapText.DikForVk('A'), "DIK_A", "A");
        Eq(AcKeymapText.DikForVk('7'), "DIK_7", "7");
        Eq(AcKeymapText.DikForVk(0x7A), "DIK_F11", "F11");
        Eq(AcKeymapText.DikForVk(0x7E), "DIK_F15", "F15");
        Eq(AcKeymapText.DikForVk(0x7F), (string?)null, "F16: AC has none");
        Eq(AcKeymapText.DikForVk(0x21), "DIK_PGUP", "PageUp as AC writes it");
        Eq(AcKeymapText.DikForVk(0x26), "DIK_UPARROW", "Up as AC writes it");
        Eq(AcKeymapText.DikForVk(0x65), "DIK_NUMPAD5", "Num5");
        Eq(AcKeymapText.DikForVk(0x6A), "DIK_NUMPADSTAR", "Num*");
        Eq(AcKeymapText.DikForVk(0xC0), "DIK_GRAVE", "`");
        Eq(AcKeymapText.Canonical("dik_prior"), "DIK_PGUP", "PRIOR is PGUP");
        Eq(AcKeymapText.Canonical("DIK_UP"), "DIK_UPARROW", "UP is UPARROW");
        Eq(AcKeymapText.Canonical("DIK_LEFTARROW"), "DIK_LEFT", "LEFTARROW is LEFT");
        Eq(AcKeymapText.Canonical("DIK_F3"), "DIK_F3", "a plain name");
    }

    private static void Meta()
    {
        List<string> lines = Lines();
        Check(AcKeymapText.TryMeta(lines, HotkeyMods.None, out uint m0, out _) && m0 == 0, "no modifiers: 0");
        Check(AcKeymapText.TryMeta(lines, HotkeyMods.Ctrl, out uint m1, out _) && m1 == 2, "Ctrl: 0x2");
        Check(AcKeymapText.TryMeta(lines, HotkeyMods.Alt, out uint m2, out _) && m2 == 4, "Alt: 0x4 (metakey 3)");
        Check(AcKeymapText.TryMeta(lines, HotkeyMods.Ctrl | HotkeyMods.Shift, out uint m3, out _) && m3 == 3, "Ctrl+Shift: 0x3");
        List<string> noAlt = Lines(Sample.Replace("  3 [ 0 DIK_LMENU ]\r\n", "").Replace("  3 [ 0 DIK_RALT ]\r\n", ""));
        Check(!AcKeymapText.TryMeta(noAlt, HotkeyMods.Alt, out _, out string err) && err.Contains("Alt"), "no Alt metakey: an error naming it");
    }

    private static void BindTakes()
    {
        List<string> lines = Lines();
        Check(AcKeymapText.Bind(lines, "UseQuickSlot_10", 0, "DIK_F11", 0, out var taken, out _), "bind slot 10 to F11");
        Eq(taken.Count, 1, "one action had F11");
        Eq(taken.Count > 0 ? taken[0].Action : "", "ToggleOptionsPanel", "taken from Options");
        Eq(Of(lines, "ToggleOptionsPanel").Count, 0, "Options lost F11");
        List<AcKeymapText.Line> s10 = Of(lines, "UseQuickSlot_10");
        Check(s10.Count == 1 && s10[0].Group == "QuickslotCommands" && s10[0].Dik == "DIK_F11" && s10[0].Meta == 0, "slot 10 has F11 in QuickslotCommands");
        Check(s10.Count == 1 && lines[s10[0].Index] == "    UseQuickSlot_10 [ \"\" [ 0 DIK_F11 ] ]", "written as AC writes it: " + (s10.Count == 1 ? lines[s10[0].Index] : ""));
        // Taking Ctrl+1 for slot 2: only the Ctrl one goes.
        Check(AcKeymapText.Bind(lines, "UseQuickSlot_2", 0, "DIK_1", 2, out taken, out _), "bind slot 2 to Ctrl+1");
        Check(taken.Count == 1 && taken[0].Action == "UseQuickSlot_1" && taken[0].Meta == 2, "Ctrl+1 taken from slot 1");
        List<AcKeymapText.Line> s1 = Of(lines, "UseQuickSlot_1");
        Check(s1.Count == 1 && s1[0].Meta == 0, "slot 1 keeps plain 1");
        List<AcKeymapText.Line> s2 = Of(lines, "UseQuickSlot_2");
        Check(s2.Count == 1 && lines[s2[0].Index].EndsWith("[ 0 DIK_1 ] 0x00000002 ]"), "slot 2 has Ctrl+1");
        // An alias in the file is the same key: PRIOR takes PGUP's other game use, not EditControls'.
        Check(AcKeymapText.Bind(lines, "UseQuickSlot_3", 0, "DIK_PRIOR", 0, out taken, out _) && taken.Count == 0, "PgUp: only a text group has it");
        Eq(Of(lines, "CursorPreviousPage").Count, 1, "the chat box keeps PgUp");
    }

    private static void BindKeepsTextGroups()
    {
        List<string> lines = Lines();
        Check(AcKeymapText.Bind(lines, "UseQuickSlot_4", 0, "DIK_C", 2, out var taken, out _), "bind slot 4 to Ctrl+C");
        Eq(taken.Count, 0, "Ctrl+C isn't taken from copy");
        Eq(Of(lines, "CopyText").Count, 1, "copy keeps Ctrl+C");
        Check(AcKeymapText.Bind(lines, "UseQuickSlot_6", 0, "DIK_HOME", 0, out _, out _), "bind slot 6 to Home");
        Eq(Of(lines, "CursorStartOfLine").Count, 1, "the chat box keeps Home");
    }

    private static void BindAlready()
    {
        List<string> lines = Lines();
        int before = lines.Count;
        Check(AcKeymapText.Bind(lines, "UseQuickSlot_5", 0, "DIK_5", 0, out var taken, out _) && taken.Count == 0, "slot 5 already has 5");
        Eq(lines.Count, before, "nothing added");
        Eq(Of(lines, "UseQuickSlot_5").Count, 1, "still one line");
    }

    private static void BindNewSlot()
    {
        List<string> lines = Lines();
        Check(AcKeymapText.Bind(lines, "UseQuickSlot_12", 0, "DIK_F12", 0, out _, out _), "slot 12, no line yet");
        List<AcKeymapText.Line> s = Of(lines, "UseQuickSlot_12");
        Check(s.Count == 1 && s[0].Group == "QuickslotCommands", "goes in QuickslotCommands");
        // At the end of the group, before its ']'.
        Check(s.Count == 1 && lines[s[0].Index + 1].Trim() == "]", "at the group's end");
        // A second key goes right after the slot's own line.
        Check(AcKeymapText.Bind(lines, "UseQuickSlot_12", 0, "DIK_F12", 1, out _, out _), "slot 12, Shift+F12 too");
        s = Of(lines, "UseQuickSlot_12");
        Check(s.Count == 2 && s[1].Index == s[0].Index + 1, "next to the first");
    }

    private static void BindMissingGroup()
    {
        string text = Sample.Replace("  QuickslotCommands\r\n  [\r\n", "  OldSlots\r\n  [\r\n");
        List<string> lines = Lines(text);
        // The slot's existing lines are in OldSlots: a new key goes beside them.
        Check(AcKeymapText.Bind(lines, "UseQuickSlot_1", 0, "DIK_F3", 0, out _, out _), "slot 1 beside its lines");
        Check(Of(lines, "UseQuickSlot_1").TrueForAll(l => l.Group == "OldSlots"), "stays in its group");
        // A slot with no line and no QuickslotCommands: the group is made at the end of Bindings.
        Check(AcKeymapText.Bind(lines, "UseQuickSlot_9", 0, "DIK_9", 0, out _, out _), "slot 9, no group");
        List<AcKeymapText.Line> s9 = Of(lines, "UseQuickSlot_9");
        Check(s9.Count == 1 && s9[0].Group == "QuickslotCommands", "QuickslotCommands made");
        Eq(AcKeymapText.Bindings(lines).Count, 16, "every line still read");
    }

    private static void BindEmptyGroup()
    {
        // ToggleChatEntry is a name with no [ ] block: a binding there gets one.
        string text = Sample.Replace("  QuickslotCommands", "  QuickslotCommandsX").Replace("  ToggleChatEntry", "  QuickslotCommands");
        List<string> lines = Lines(text);
        Check(AcKeymapText.Bind(lines, "UseQuickSlot_11", 0, "DIK_F2", 0, out _, out _), "bind into an empty group");
        List<AcKeymapText.Line> s = Of(lines, "UseQuickSlot_11");
        Check(s.Count == 1 && s[0].Group == "QuickslotCommands", "in the empty group, now with a block");
        Eq(Of(lines, "SelectLeft").Count, 1, "the next group still read");
    }

    private static void BindUnknownAction()
    {
        List<string> lines = Lines();
        int before = lines.Count;
        Check(!AcKeymapText.Bind(lines, "SomeNewAction", 0, "DIK_F7", 0, out _, out string err) && err.Length > 0, "no group for an unknown action");
        Eq(lines.Count, before, "nothing changed");
    }

    private static void Unbind()
    {
        List<string> lines = Lines();
        Check(AcKeymapText.Unbind(lines, "UseQuickSlot_1", 0, "DIK_1", 2), "Ctrl+1 off slot 1");
        List<AcKeymapText.Line> s1 = Of(lines, "UseQuickSlot_1");
        Check(s1.Count == 1 && s1[0].Meta == 0, "plain 1 stays");
        Check(!AcKeymapText.Unbind(lines, "UseQuickSlot_1", 0, "DIK_1", 2), "again: nothing to remove");
        Check(AcKeymapText.Unbind(lines, "MovementForward", 0, "DIK_UP", 0), "by an alias");
        Eq(Of(lines, "MovementForward").Count, 1, "W stays");
    }

    private static void RoundTrip()
    {
        List<string> lines = AcKeymapText.Split(Sample, out string nl);
        Eq(nl, "\r\n", "CRLF kept");
        Eq(AcKeymapText.Join(lines, nl), Sample, "split and join give the same text");
        List<string> lf = AcKeymapText.Split(Sample.Replace("\r\n", "\n"), out string nl2);
        Eq(nl2, "\n", "LF kept");
        Eq(lf.Count, lines.Count, "same lines either way");
        Eq(AcKeymapText.Format("UseQuickSlot_1", 0, "DIK_1", 2), "UseQuickSlot_1 [ \"\" [ 0 DIK_1 ] 0x00000002 ]", "a line with a mask");
        Eq(AcKeymapText.Format("UseQuickSlot_1", 0, "DIK_1", 0), "UseQuickSlot_1 [ \"\" [ 0 DIK_1 ] ]", "a line without");
    }

    private static void Describe()
    {
        Eq(AcKeymapText.Describe("UseQuickSlot_5"), "shortcut slot 5", "a slot");
        Eq(AcKeymapText.Describe("ToggleSpellbookPanel"), "Toggle Spellbook Panel", "camel case");
        Eq(AcKeymapText.Describe("AltF4"), "Alt F4", "a key name");
        Eq(AcKeymapText.Describe("LOGOUT"), "LOGOUT", "all capitals");
    }

    private static void RealFile()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Asheron's Call", "Normal.keymap");
        if (!File.Exists(path)) { Console.WriteLine("(no Normal.keymap here: the real-file case is skipped)"); return; }
        string text = File.ReadAllText(path);
        List<string> lines = AcKeymapText.Split(text, out string nl);
        Eq(AcKeymapText.Join(lines, nl), text, "the real file round trips");
        int count = AcKeymapText.Bindings(lines).Count;
        Check(count > 50, $"the real file's bindings read ({count})");
        Check(Of(lines, "UseQuickSlot_1").Count >= 1, "the real file has slot 1");
        Check(AcKeymapText.TryMeta(lines, HotkeyMods.Ctrl, out uint ctrl, out _) && ctrl != 0, "the real file has Ctrl");
        // In memory only: slot 10 to F11 takes it from whatever has it, and every other line is still read.
        int had = AcKeymapText.Uses(lines, 0, "DIK_F11", 0).Count;
        Check(AcKeymapText.Bind(lines, "UseQuickSlot_10", 0, "DIK_F11", 0, out var taken, out _), "real file: bind slot 10 to F11");
        Eq(taken.Count, had, "real file: F11 taken from all its game uses");
        Eq(AcKeymapText.Bindings(lines).Count, count - had + 1, "real file: one line in, the taken ones out");
        Eq(AcKeymapText.Uses(lines, 0, "DIK_F11", 0).Count, 1, "real file: F11 does one thing now");
    }
}
