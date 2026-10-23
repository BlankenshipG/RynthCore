// ============================================================================
//  RynthCore.Engine - Compatibility/AcKeymapText.cs
//
//  Changing a key in AC's keymap file, as text: the pure part (no engine
//  dependencies, so tools/KeymapTests compiles it as is with Hotkeys/
//  HotkeyModel.cs). AC reads the file when it starts (gmClient::InitKeymap)
//  and writes its keys back when it closes (gmClient::Cleanup); RynthCore
//  changes a binding by editing the file and having every running client load
//  it again (AcKeymapLive), so a key changed on the item bar is the key the
//  retail toolbar and every client answer to.
//
//    Bindings
//    [
//      QuickslotCommands
//      [
//        UseQuickSlot_1 [ "" [ 0 DIK_1 ] ]
//        UseQuickSlot_1 [ "" [ 0 DIK_1 ] 0x00000002 ]
//      ]
//    ]
//
//  A binding takes its key, as AC's Configure Keyboard does: the same key with
//  the same modifiers comes off every other action in the groups the game
//  reads while playing. The text-editing groups keep theirs (a chat box still
//  has Home and Ctrl+C). The key names written are the ones AC writes itself
//  (DIK_PGUP, DIK_UPARROW...), all in its own name table; any name it accepts
//  is matched (DIK_PRIOR is DIK_PGUP).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using RynthCore.Engine.Hotkeys;

namespace RynthCore.Engine.Compatibility;

internal static class AcKeymapText
{
    /// <summary>One binding line: where it is, its group and action, the device (0 keyboard), the key (canonical DIK name), the modifier mask.</summary>
    internal readonly record struct Line(int Index, string Group, string Action, int Device, string Dik, uint Meta);

    /// <summary>Groups for typing: a key bound there never conflicts with one the game uses while playing.</summary>
    public static readonly string[] TextGroups = { "EditControls", "CopyAndPasteControls", "DialogBoxes", "ScrollableControls" };

    /// <summary>The group a new key for an action no group has yet goes in.</summary>
    public static string? DefaultGroup(string action) =>
        action.StartsWith("UseQuickSlot_", StringComparison.OrdinalIgnoreCase) ? "QuickslotCommands" : null;

    // Action [ "name" [ device KEY sub? ] mask? activation? ]
    private static readonly Regex BindingLine = new(
        @"^\s*([A-Za-z0-9_]+)\s*\[\s*""[^""]*""\s*\[\s*(\d+)\s+([A-Za-z0-9_]+)[^\]]*\]\s*(0x[0-9A-Fa-f]+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // n [ device KEY ]
    private static readonly Regex MetaLine = new(@"^\s*(\d+)\s*\[\s*\d+\s+([A-Za-z0-9_]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Name = new(@"^[A-Za-z0-9_]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // ── Text in and out ─────────────────────────────────────────────────────

    /// <summary>The file's lines and its line ending ("\r\n" as AC writes it).</summary>
    public static List<string> Split(string text, out string newline)
    {
        newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = new List<string>(text.Split('\n'));
        for (int i = 0; i < lines.Count; i++) lines[i] = lines[i].TrimEnd('\r');
        return lines;
    }

    public static string Join(List<string> lines, string newline) => string.Join(newline, lines);

    // ── Key names ───────────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DIK_PRIOR"] = "DIK_PGUP", ["DIK_NEXT"] = "DIK_PGDN", ["DIK_UP"] = "DIK_UPARROW", ["DIK_DOWN"] = "DIK_DOWNARROW",
        ["DIK_RIGHT"] = "DIK_RIGHTARROW", ["DIK_LEFTARROW"] = "DIK_LEFT", ["DIK_BACKSPACE"] = "DIK_BACK",
        ["DIK_CAPSLOCK"] = "DIK_CAPITAL", ["DIK_LALT"] = "DIK_LMENU", ["DIK_RALT"] = "DIK_RMENU",
        ["DIK_MULTIPLY"] = "DIK_NUMPADSTAR", ["DIK_DIVIDE"] = "DIK_NUMPADSLASH", ["DIK_ADD"] = "DIK_NUMPADPLUS",
        ["DIK_SUBTRACT"] = "DIK_NUMPADMINUS", ["DIK_NUMPADPERIOD"] = "DIK_DECIMAL",
    };

    /// <summary>One name per key (upper case): DIK_PRIOR and DIK_PGUP are both DIK_PGUP.</summary>
    public static string Canonical(string dik)
    {
        string d = dik.Trim().ToUpperInvariant();
        return Aliases.TryGetValue(d, out string? c) ? c : d;
    }

    /// <summary>
    /// The DirectInput name AC writes for a Windows virtual key (the engine's hotkey keys), or null for
    /// one AC's keymap can't hold (F16 and up).
    /// </summary>
    public static string? DikForVk(int vk)
    {
        if (vk is >= 'A' and <= 'Z' or >= '0' and <= '9') return "DIK_" + (char)vk;
        if (vk is >= 0x70 and <= 0x7E) return "DIK_F" + (vk - 0x70 + 1);            // F1-F15
        if (vk is >= 0x60 and <= 0x69) return "DIK_NUMPAD" + (vk - 0x60);           // Num0-Num9
        return vk switch
        {
            0x6A => "DIK_NUMPADSTAR", 0x6B => "DIK_NUMPADPLUS", 0x6D => "DIK_NUMPADMINUS", 0x6E => "DIK_DECIMAL", 0x6F => "DIK_NUMPADSLASH",
            0x08 => "DIK_BACK", 0x09 => "DIK_TAB", 0x0D => "DIK_RETURN", 0x13 => "DIK_PAUSE", 0x20 => "DIK_SPACE",
            0x21 => "DIK_PGUP", 0x22 => "DIK_PGDN", 0x23 => "DIK_END", 0x24 => "DIK_HOME",
            0x25 => "DIK_LEFT", 0x26 => "DIK_UPARROW", 0x27 => "DIK_RIGHTARROW", 0x28 => "DIK_DOWNARROW",
            0x2D => "DIK_INSERT", 0x2E => "DIK_DELETE", 0x91 => "DIK_SCROLL",
            0xBA => "DIK_SEMICOLON", 0xBB => "DIK_EQUALS", 0xBC => "DIK_COMMA", 0xBD => "DIK_MINUS", 0xBE => "DIK_PERIOD",
            0xBF => "DIK_SLASH", 0xC0 => "DIK_GRAVE", 0xDB => "DIK_LBRACKET", 0xDC => "DIK_BACKSLASH", 0xDD => "DIK_RBRACKET",
            0xDE => "DIK_APOSTROPHE",
            _ => null,
        };
    }

    // ── Reading ─────────────────────────────────────────────────────────────

    /// <summary>The MetaKeys block: each modifier key (canonical name) and its number (its bit is 1 &lt;&lt; (n - 1)).</summary>
    public static Dictionary<string, int> MetaKeys(List<string> lines)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string section = string.Empty;
        foreach (string raw in lines)
        {
            string t = raw.Trim();
            if (t is "Devices" or "MetaKeys" or "Bindings") { section = t; continue; }
            if (section != "MetaKeys") continue;
            Match m = MetaLine.Match(t);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int n) && n is >= 1 and <= 32)
                map.TryAdd(Canonical(m.Groups[2].Value), n);
        }
        return map;
    }

    /// <summary>
    /// The modifier mask for Ctrl/Alt/Shift by the file's own MetaKeys numbers (the default file: Shift 1,
    /// Ctrl 2, Alt 4). False, with the reason, when the file has no metakey for one of them.
    /// </summary>
    public static bool TryMeta(List<string> lines, HotkeyMods mods, out uint mask, out string error)
    {
        mask = 0;
        error = string.Empty;
        Dictionary<string, int> meta = MetaKeys(lines);
        bool Add(string left, string right, string what, ref uint m, out string err)
        {
            err = string.Empty;
            if (meta.TryGetValue(left, out int n) || meta.TryGetValue(right, out n)) { m |= 1u << (n - 1); return true; }
            err = $"Your keymap has no {what} modifier (MetaKeys).";
            return false;
        }
        if ((mods & HotkeyMods.Shift) != 0 && !Add("DIK_LSHIFT", "DIK_RSHIFT", "Shift", ref mask, out error)) return false;
        if ((mods & HotkeyMods.Ctrl) != 0 && !Add("DIK_LCONTROL", "DIK_RCONTROL", "Ctrl", ref mask, out error)) return false;
        if ((mods & HotkeyMods.Alt) != 0 && !Add("DIK_LMENU", "DIK_RMENU", "Alt", ref mask, out error)) return false;
        return true;
    }

    /// <summary>Every binding line in the Bindings block, with its group.</summary>
    public static List<Line> Bindings(List<string> lines)
    {
        var list = new List<Line>();
        bool inBindings = false;
        int depth = 0;
        string group = string.Empty;
        for (int i = 0; i < lines.Count; i++)
        {
            string t = lines[i].Trim();
            if (t.Length == 0 || t.StartsWith('#')) continue;
            if (!inBindings)
            {
                if (t == "Bindings") { inBindings = true; depth = 0; }
                continue;
            }
            if (t == "[") { depth++; continue; }
            if (t == "]") { depth--; if (depth <= 0) inBindings = false; continue; }
            if (depth == 1 && Name.IsMatch(t)) { group = t; continue; }
            Match b = BindingLine.Match(t);
            if (!b.Success) continue;
            uint mask = 0;
            if (b.Groups[4].Success) uint.TryParse(b.Groups[4].Value.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out mask);
            list.Add(new Line(i, group, b.Groups[1].Value, int.Parse(b.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                Canonical(b.Groups[3].Value), mask));
        }
        return list;
    }

    public static bool IsTextGroup(string group) => Array.Exists(TextGroups, g => g.Equals(group, StringComparison.OrdinalIgnoreCase));

    /// <summary>The bindings of this key with these modifiers in the groups the game reads while playing.</summary>
    public static List<Line> Uses(List<string> lines, int device, string dik, uint meta)
    {
        string d = Canonical(dik);
        return Bindings(lines).FindAll(l => l.Device == device && l.Dik == d && l.Meta == meta && !IsTextGroup(l.Group));
    }

    // ── Changing ────────────────────────────────────────────────────────────

    /// <summary>
    /// Binds the key to <paramref name="action"/>, taking it from every other action that has it (game
    /// groups). <paramref name="taken"/>: the bindings it came off. Returns false (lines unchanged) with
    /// <paramref name="error"/> when there is nowhere to put it.
    /// </summary>
    public static bool Bind(List<string> lines, string action, int device, string dik, uint meta, out List<Line> taken, out string error)
    {
        error = string.Empty;
        string d = Canonical(dik);
        List<Line> all = Bindings(lines);
        taken = all.FindAll(l => l.Device == device && l.Dik == d && l.Meta == meta && !IsTextGroup(l.Group)
                                 && !l.Action.Equals(action, StringComparison.OrdinalIgnoreCase));
        bool already = all.Exists(l => l.Device == device && l.Dik == d && l.Meta == meta
                                       && l.Action.Equals(action, StringComparison.OrdinalIgnoreCase));

        // Where the new line goes: after the action's last line, else at the end of its group.
        string? group = null;
        int insertAt = -1;
        if (!already)
        {
            foreach (Line l in all)
                if (l.Action.Equals(action, StringComparison.OrdinalIgnoreCase) && !IsTextGroup(l.Group)) { group = l.Group; insertAt = l.Index + 1; }
            if (insertAt < 0)
            {
                group = DefaultGroup(action);
                if (group == null) { error = $"No group for {action} in the keymap."; taken = new(); return false; }
            }
        }

        // Take the key: drop the other lines (bottom up so the indexes hold).
        for (int k = taken.Count - 1; k >= 0; k--)
        {
            lines.RemoveAt(taken[k].Index);
            if (insertAt > taken[k].Index) insertAt--;
        }
        if (already) return true;

        if (insertAt >= 0)
        {
            lines.Insert(insertAt, Indent(lines[insertAt - 1]) + Format(action, device, d, meta));
            return true;
        }
        int close = GroupClose(lines, group!, out int open);
        if (close >= 0)
        {
            lines.Insert(close, Indent(lines[close]) + "  " + Format(action, device, d, meta));
            return true;
        }
        if (open >= 0)
        {
            // The group's name with no [ ] block under it (an empty group).
            string gi = Indent(lines[open]);
            lines.InsertRange(open + 1, new[] { gi + "[", gi + "  " + Format(action, device, d, meta), gi + "]" });
            return true;
        }
        int end = BindingsClose(lines);
        if (end < 0) { error = "The keymap has no Bindings block."; return false; }
        lines.InsertRange(end, new[] { string.Empty, "  " + group, "  [", "    " + Format(action, device, d, meta), "  ]" });
        return true;
    }

    /// <summary>Removes the action's binding of this key with these modifiers. False when it had none.</summary>
    public static bool Unbind(List<string> lines, string action, int device, string dik, uint meta)
    {
        string d = Canonical(dik);
        List<Line> mine = Bindings(lines).FindAll(l => l.Device == device && l.Dik == d && l.Meta == meta
                                                      && l.Action.Equals(action, StringComparison.OrdinalIgnoreCase));
        for (int k = mine.Count - 1; k >= 0; k--) lines.RemoveAt(mine[k].Index);
        return mine.Count > 0;
    }

    /// <summary>A binding line as AC writes it: <c>UseQuickSlot_1 [ "" [ 0 DIK_1 ] 0x00000002 ]</c>.</summary>
    public static string Format(string action, int device, string dik, uint meta) =>
        meta == 0 ? $"{action} [ \"\" [ {device} {dik} ] ]" : $"{action} [ \"\" [ {device} {dik} ] 0x{meta:X8} ]";

    /// <summary>An action's name as the player reads it: "shortcut slot 5", "Toggle Spellbook Panel".</summary>
    public static string Describe(string action)
    {
        if (action.StartsWith("UseQuickSlot_", StringComparison.OrdinalIgnoreCase)) return "shortcut slot " + action[13..];
        var sb = new StringBuilder(action.Length + 8);
        for (int i = 0; i < action.Length; i++)
        {
            char c = action[i];
            if (c == '_') { sb.Append(' '); continue; }
            if (i > 0 && char.IsUpper(c) && (char.IsLower(action[i - 1]) || i + 1 < action.Length && char.IsLower(action[i + 1]) && char.IsUpper(action[i - 1])))
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static string Indent(string line)
    {
        int n = 0;
        while (n < line.Length && line[n] == ' ') n++;
        return line[..n];
    }

    /// <summary>The line of the group's closing ']' (and of its name), or -1.</summary>
    private static int GroupClose(List<string> lines, string group, out int nameLine)
    {
        nameLine = -1;
        bool inBindings = false;
        int depth = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            string t = lines[i].Trim();
            if (!inBindings)
            {
                if (t == "Bindings") inBindings = true;
                continue;
            }
            if (t == "[")
            {
                depth++;
                continue;
            }
            if (t == "]")
            {
                if (nameLine >= 0 && depth == 2) return i;
                depth--;
                if (depth <= 0) return -1;
                continue;
            }
            if (depth == 1 && Name.IsMatch(t))
            {
                if (nameLine >= 0) return -1;   // the next group: ours had no [ ] block
                if (t.Equals(group, StringComparison.OrdinalIgnoreCase)) nameLine = i;
            }
        }
        return -1;
    }

    /// <summary>The line of the Bindings block's closing ']', or -1.</summary>
    private static int BindingsClose(List<string> lines)
    {
        bool inBindings = false;
        int depth = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            string t = lines[i].Trim();
            if (!inBindings)
            {
                if (t == "Bindings") inBindings = true;
                continue;
            }
            if (t == "[") depth++;
            else if (t == "]" && --depth == 0) return i;
        }
        return -1;
    }
}
