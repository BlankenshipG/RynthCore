// ============================================================================
//  RynthCore.Engine - UI/Data/MetaData.cs
//  RynthAi's meta (rules, files, states, .af source) for both Meta faces
//  (docs/IMGUI_PARITY_PLAN.md §2.13).
//
//  RynthPluginGetMetaJson frees its previous buffer on each call, so the hub
//  is its only caller. RynthPluginSendMetaCommand only queues: the plugin
//  applies the command on its next tick, so a command defers the refresh to
//  after that tick (UiSource.RequestRefreshAfterPluginTick) and the next
//  snapshot already shows the result.
//
//  The faces edit their own copies (Clone) for instant feedback; the next
//  snapshot replaces the copy.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RynthCore.Engine.Plugins;

namespace RynthCore.Engine.UI.Data;

internal sealed class MetaRuleDto
{
    public string State { get; set; } = "Default";
    public int Condition { get; set; }
    public string ConditionData { get; set; } = string.Empty;
    public int Action { get; set; }
    public string ActionData { get; set; } = string.Empty;
    public List<MetaRuleDto> Children { get; set; } = new();
    public List<MetaRuleDto> ActionChildren { get; set; } = new();
    public bool Enabled { get; set; } = true;
    public long LastFiredMs { get; set; } = 99999;

    public MetaRuleDto Clone()
    {
        var r = new MetaRuleDto
        {
            State = State, Condition = Condition, ConditionData = ConditionData,
            Action = Action, ActionData = ActionData, Enabled = Enabled, LastFiredMs = LastFiredMs,
        };
        foreach (MetaRuleDto c in Children) r.Children.Add(c.Clone());
        foreach (MetaRuleDto a in ActionChildren) r.ActionChildren.Add(a.Clone());
        return r;
    }
}

internal sealed class MetaFile
{
    public string Path { get; set; } = string.Empty;
    public string Display { get; set; } = string.Empty;
}

/// <summary>The plugin's answer to the last Source "Apply" (set_source). Seq counts applies.</summary>
internal sealed class MetaApplyResult
{
    public long Seq { get; set; }
    public bool Ok { get; set; }
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// One Meta Manager rule ("when TRIGGER, load META"): the settings the panel edits (also the
/// mm_add / mm_update wire format) plus the plugin's read-only status.
/// </summary>
internal sealed class ScheduleRuleDto
{
    public bool Enabled { get; set; } = true;
    /// <summary>0 quest ready, 1 after N minutes on the meta, 2 every N minutes, 3 countdown.</summary>
    public int Trigger { get; set; }
    public string Quest { get; set; } = string.Empty;
    public double Minutes { get; set; } = 30;
    public string Meta { get; set; } = string.Empty;
    public string OnlyOnMeta { get; set; } = string.Empty;
    public bool Repeat { get; set; } = true;

    // Status (the plugin's; not sent back).
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Fired { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int FireCount { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long LastFiredMs { get; set; }
    /// <summary>When it is due (unix ms): 0 = not known / not running, -1 = never.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long DueAtMs { get; set; }
    /// <summary>A few words ("waiting: combat", "error: ...") or empty when the due time says it all.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? State { get; set; }

    internal ScheduleRuleDto MemberwiseCloneRule() => (ScheduleRuleDto)MemberwiseClone();

    public ScheduleRuleDto CloneSettings() => new()
    {
        Enabled = Enabled, Trigger = Trigger, Quest = Quest, Minutes = Minutes,
        Meta = Meta, OnlyOnMeta = OnlyOnMeta, Repeat = Repeat,
    };
}

/// <summary>RynthAi's Meta Manager: its settings, poll state and rules (the "schedule" object).</summary>
internal sealed class SchedulePayload
{
    public bool Enabled { get; set; }
    public int PollMinutes { get; set; } = 5;
    public int MaxWaitSeconds { get; set; } = 60;
    public int MinGapSeconds { get; set; } = 30;
    public bool AllowWhileStopped { get; set; }
    public bool Polling { get; set; }
    public bool ServerDisabled { get; set; }
    public bool HaveQuestList { get; set; }
    public int QuestCount { get; set; }
    public long LastPollMs { get; set; }
    public string LastSwitch { get; set; } = string.Empty;
    public long LastSwitchMs { get; set; }
    /// <summary>The rule that fires next (first due rule, or the one waiting to switch); -1 none.</summary>
    public int FirstMatch { get; set; } = -1;
    public List<ScheduleRuleDto> Rules { get; set; } = new();

    /// <summary>A copy the face may change for instant feedback (the snapshot stays as published).</summary>
    public SchedulePayload Clone()
    {
        var p = (SchedulePayload)MemberwiseClone();
        p.Rules = new List<ScheduleRuleDto>(Rules.Count);
        foreach (ScheduleRuleDto r in Rules) p.Rules.Add(r.MemberwiseCloneRule());
        return p;
    }
}

internal sealed class MetaPayload
{
    public bool EnableMeta { get; set; }
    public bool MetaDebug { get; set; }
    public string CurrentState { get; set; } = "Default";
    public string CurrentMetaPath { get; set; } = string.Empty;
    public List<MetaRuleDto> Rules { get; set; } = new();
    public List<MetaFile> Files { get; set; } = new();
    public List<string> States { get; set; } = new();
    public List<string> NavFiles { get; set; } = new();
    public List<string> EmbeddedNavKeys { get; set; } = new();
    public string SourceText { get; set; } = string.Empty;
    /// <summary>Null from a plugin that doesn't report apply results yet (it reports to chat).</summary>
    public MetaApplyResult? ApplyResult { get; set; }
    /// <summary>The Meta Manager; null from a RynthAi without it.</summary>
    public SchedulePayload? Schedule { get; set; }

    /// <summary>Deep copy of the rules; the rest is shared (faces never change it).</summary>
    public MetaPayload Clone()
    {
        var p = (MetaPayload)MemberwiseClone();
        p.Rules = new List<MetaRuleDto>(Rules.Count);
        foreach (MetaRuleDto r in Rules) p.Rules.Add(r.Clone());
        p.Schedule = Schedule?.Clone();
        return p;
    }
}

internal sealed class MetaCmd
{
    public string Op { get; set; } = string.Empty;
    public int Index { get; set; } = -1;
    public string Value { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public MetaRuleDto? Rule { get; set; }
    /// <summary>mm_add / mm_update: the Meta Manager rule (settings only).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ScheduleRuleDto? ScheduleRule { get; set; }
}

[JsonSerializable(typeof(MetaPayload))]
[JsonSerializable(typeof(MetaCmd))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = false, IncludeFields = false)]
internal partial class MetaJsonContext : JsonSerializerContext { }

/// <summary>RynthPluginGetMetaJson while a Meta face is open (2 s, and after each command).</summary>
internal sealed unsafe class MetaSource : UiSource<MetaPayload>
{
    private delegate* unmanaged[Cdecl]<IntPtr> _getMetaJson;
    private string? _lastJson;

    public MetaSource() : base("Meta", periodMs: 2000) { }

    protected internal override void Poll()
    {
        if (_getMetaJson == null)
            _getMetaJson = (delegate* unmanaged[Cdecl]<IntPtr>)PluginExportBinder.Resolve("RynthAi", "RynthPluginGetMetaJson");
        if (_getMetaJson == null) return;
        IntPtr ptr = _getMetaJson();
        string? json = ptr == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(ptr);
        if (string.IsNullOrEmpty(json) || json == _lastJson) return;
        _lastJson = json;
        MetaPayload? parsed = JsonSerializer.Deserialize(json, MetaJsonContext.Default.MetaPayload);
        if (parsed != null) Publish(parsed);
    }

    protected internal override void Reset()
    {
        _getMetaJson = null;
        _lastJson = null;
        ClearSnapshot();
    }
}

/// <summary>RynthPluginSendMetaCommand on the pump thread. Any thread may call Send.</summary>
internal static unsafe class MetaCommands
{
    private static delegate* unmanaged[Cdecl]<IntPtr, void> _sendMetaCommand;

    static MetaCommands()
    {
        PluginManager.PluginsUnloaded += () => _sendMetaCommand = null;
    }

    /// <summary><paramref name="cmd"/> (and its Rule) must not be changed after this call.</summary>
    public static void Send(MetaCmd cmd)
    {
        UiDataHub.Post("Meta " + cmd.Op, () =>
        {
            if (_sendMetaCommand == null)
                _sendMetaCommand = (delegate* unmanaged[Cdecl]<IntPtr, void>)PluginExportBinder.Resolve("RynthAi", "RynthPluginSendMetaCommand");
            if (_sendMetaCommand == null) return;
            string json = JsonSerializer.Serialize(cmd, MetaJsonContext.Default.MetaCmd);
            IntPtr ansi = Marshal.StringToHGlobalAnsi(json);
            try { _sendMetaCommand(ansi); }
            finally { Marshal.FreeHGlobal(ansi); }
            UiSources.Meta.RequestRefreshAfterPluginTick();
        });
    }

    public static void Simple(string op, int index = -1, string value = "", string path = "")
        => Send(new MetaCmd { Op = op, Index = index, Value = value, Path = path });
}

/// <summary>Names, hints and .af keywords shared by the Meta faces (mirror RynthSuite's MetaSchema).</summary>
internal static class MetaVocabulary
{
    public const string MetaFolder = @"C:\Games\RynthSuite\RynthAi\MetaFiles";

    public static readonly string[] ConditionNames =
    {
        "Never", "Always", "All", "Any", "Chat Message", "Pack Slots <=",
        "Seconds in State >=", "Character Death", "Any Vendor Open",
        "Vendor Closed", "Inventory Item Count <=", "Inventory Item Count >=",
        "Monster Name Count Within Dist", "Monster Priority Count Within Dist",
        "Need To Buff", "No Monsters Within Dist", "Landblock ==",
        "Landcell ==", "Portalspace Entered", "Portalspace Exited", "Not",
        "Seconds in State (P) >=", "Time Left On Spell >=", "Time Left On Spell <=",
        "Burden % >=", "Dist Any Route PT >=", "Expression",
        "Chat Message Capture", "Navroute Empty",
        "Main Health <=", "Main Health % >=", "Main Mana <=", "Main Mana % >=",
        "Main Stam <=", "Vitae % >=",
    };

    public static readonly string[] ConditionHints =
    {
        "", "", "(sub-conditions)", "(sub-conditions)", "Regex pattern", "Min slots (e.g. 5)",
        "Seconds (e.g. 10)", "", "", "",
        "name,count (e.g. Mana Stone,5)", "name,count (e.g. Mana Stone,5)",
        "name regex,distance,count", "count,distance (e.g. 1,20)",
        "", "Distance (e.g. 20)", "Hex (e.g. A9B40000)", "Hex (e.g. A9B40000)",
        "", "", "(sub-conditions)", "Seconds (e.g. 10)",
        "spellId,seconds (e.g. 2293,30)", "spellId,seconds (e.g. 2293,30)",
        "Percentage (e.g. 250)", "Distance (e.g. 10)", "Expression",
        "Regex pattern", "",
        "", "", "", "", "", "Vitae % (e.g. 5)",
    };

    public static readonly string[] ActionNames =
    {
        "None", "Chat Command", "Set Meta State", "Embedded Nav Route", "All",
        "Call Meta State", "Return From Call", "Expression Action", "Chat Expression",
        "Set Watchdog", "Clear Watchdog", "Get RA Option", "Set RA Option",
        "Create View", "Destroy View", "Destroy All Views",
    };

    public static readonly string[] ActionHints =
    {
        "", "e.g. /say hello", "State name", "Route name", "(sub-actions)",
        "State name", "", "Expression", "Expression",
        "state;meters;seconds (e.g. Default;10;60)", "", "Option name", "OptionName;Value",
        "", "", "",
    };

    // ── .af source keywords (keyword form, as written in the file) ──────────
    public static readonly string[] StructKeywords = { "STATE", "IF", "DO", "NAV" };

    public static readonly string[] ConditionKeywords =
    {
        "Never", "Always", "All", "Any", "ChatMatch", "MainSlotsLE",
        "SecsInStateGE", "Death", "VendorOpen", "VendorClosed",
        "ItemCountLE", "ItemCountGE",
        "MobsInDist_Name", "MobsInDist_Priority",
        "NeedToBuff", "NoMobsInDist", "BlockE", "CellE",
        "IntoPortal", "ExitPortal", "Not",
        "PSecsInStateGE", "SecsOnSpellGE", "SecsOnSpellLE",
        "BuPercentGE", "DistToRteGE", "Expr",
        "ChatCapture", "NavEmpty",
        "MainHealthLE", "MainHealthPHE", "MainManaLE", "MainManaPHE",
        "MainStamLE", "VitaePHE",
    };

    public static readonly string[] ActionKeywords =
    {
        "None", "Chat", "SetState", "EmbedNav", "DoAll",
        "CallState", "Return", "DoExpr", "ChatExpr",
        "SetWatchdog", "ClearWatchdog", "GetOpt", "SetOpt",
        "CreateView", "DestroyView", "DestroyAllViews",
    };

    /// <summary>All=2, Any=3, Not=20 hold sub-conditions.</summary>
    public static bool IsCompositeCondition(int idx) => idx is 2 or 3 or 20;

    /// <summary>All (DoAll) = 4 holds sub-actions.</summary>
    public static bool IsAllAction(int idx) => idx == 4;

    /// <summary>Sub-conditions nest this deep (Not/All/Any under Not/All/Any).</summary>
    public const int MaxSubConditionDepth = 4;

    public static string ConditionName(int idx) =>
        idx >= 0 && idx < ConditionNames.Length ? ConditionNames[idx] : $"Cond({idx})";

    public static string ActionName(int idx) =>
        idx >= 0 && idx < ActionNames.Length ? ActionNames[idx] : $"Act({idx})";

    public static string ConditionHint(int idx) =>
        idx >= 0 && idx < ConditionHints.Length ? ConditionHints[idx] : "";

    public static string ActionHint(int idx) =>
        idx >= 0 && idx < ActionHints.Length ? ActionHints[idx] : "";

    /// <summary>One condition as text, nested ones included: "Not No Monsters Within Dist: 5", "Any(Chat Message: …, Navroute Empty)".</summary>
    public static string CondText(MetaRuleDto c, int depth = 0)
    {
        string name = ConditionName(c.Condition);
        if (IsCompositeCondition(c.Condition) && c.Children.Count > 0 && depth < MaxSubConditionDepth)
        {
            if (c.Condition == 20) return $"Not {CondText(c.Children[0], depth + 1)}";
            var sb = new StringBuilder(name).Append('(');
            for (int i = 0; i < c.Children.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(CondText(c.Children[i], depth + 1));
            }
            return sb.Append(')').ToString();
        }
        return string.IsNullOrEmpty(c.ConditionData) ? name : $"{name}: {c.ConditionData}";
    }

    public static string ActText(MetaRuleDto a)
    {
        string name = ActionName(a.Action);
        return string.IsNullOrEmpty(a.ActionData) ? name : $"{name}: {a.ActionData}";
    }

    /// <summary>A rule row's condition column: composite ones show the first child and "(+N)".</summary>
    public static string RowConditionText(MetaRuleDto rule)
    {
        string name = ConditionName(rule.Condition);
        if (IsCompositeCondition(rule.Condition) && rule.Children.Count > 0)
        {
            string first = CondText(rule.Children[0]);
            return rule.Children.Count > 1 ? $"{name}: {first}  (+{rule.Children.Count - 1})" : $"{name}: {first}";
        }
        return string.IsNullOrEmpty(rule.ConditionData) ? name : $"{name}: {rule.ConditionData}";
    }

    /// <summary>A rule row's action column: DoAll shows the first sub-action and "(+N)".</summary>
    public static string RowActionText(MetaRuleDto rule)
    {
        string name = ActionName(rule.Action);
        if (IsAllAction(rule.Action) && rule.ActionChildren.Count > 0)
        {
            string first = ActText(rule.ActionChildren[0]);
            return rule.ActionChildren.Count > 1 ? $"{name}: {first}  (+{rule.ActionChildren.Count - 1})" : $"{name}: {first}";
        }
        return string.IsNullOrEmpty(rule.ActionData) ? name : $"{name}: {rule.ActionData}";
    }

    /// <summary>The state picker's list: the meta's states, with Default first if it is missing.</summary>
    public static string[] StatePicks(MetaPayload d)
    {
        var states = new List<string>(d.States);
        if (!states.Contains("Default")) states.Insert(0, "Default");
        return states.ToArray();
    }

    /// <summary>Embedded Nav's route picker: nav files, then "[emb] key" for embedded routes.</summary>
    public static string[] RoutePicks(MetaPayload d)
    {
        var routes = new List<string>();
        foreach (string n in d.NavFiles) if (n != "None") routes.Add(n);
        foreach (string k in d.EmbeddedNavKeys) if (!routes.Contains(k)) routes.Add($"[emb] {k}");
        return routes.ToArray();
    }

    /// <summary>Nav names for source completion (nav files and embedded keys, no duplicates).</summary>
    public static IReadOnlyList<string> CompletionNavs(MetaPayload d)
    {
        var combined = new List<string>(d.NavFiles.Count + d.EmbeddedNavKeys.Count);
        combined.AddRange(d.NavFiles);
        foreach (string n in d.EmbeddedNavKeys) if (!combined.Contains(n)) combined.Add(n);
        return combined;
    }

    /// <summary>The file picker's current entry; "-- None --" without a loaded file.</summary>
    public static string CurrentFileDisplay(MetaPayload d)
    {
        if (!string.IsNullOrEmpty(d.CurrentMetaPath) && d.Files.Count > 1)
            foreach (MetaFile f in d.Files)
                if (string.Equals(f.Path, d.CurrentMetaPath, StringComparison.OrdinalIgnoreCase))
                    return f.Display;
        return "-- None --";
    }

    public static int CurrentFileIndex(MetaPayload d)
    {
        if (string.IsNullOrEmpty(d.CurrentMetaPath)) return 0;
        return d.Files.FindIndex(f => string.Equals(f.Path, d.CurrentMetaPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The rule list grouped by state (sorted by state name), each group's rules
    /// in list order: the order the plugin's move_up/move_down swap within.
    /// </summary>
    public static List<(string State, List<int> Rules)> GroupByState(List<MetaRuleDto> rules)
    {
        var byState = new Dictionary<string, List<int>>();
        var order = new List<string>();
        for (int i = 0; i < rules.Count; i++)
        {
            string s = rules[i].State;
            if (!byState.TryGetValue(s, out List<int>? list))
            {
                byState[s] = list = new List<int>();
                order.Add(s);
            }
            list.Add(i);
        }
        order.Sort(StringComparer.CurrentCulture);
        var groups = new List<(string, List<int>)>(order.Count);
        foreach (string s in order) groups.Add((s, byState[s]));
        return groups;
    }

    /// <summary>
    /// The rule index move_up (-1) or move_down (+1) swaps <paramref name="index"/>
    /// with: the neighbour in the same state, or -1 at the group's end.
    /// </summary>
    public static int SwapPartner(List<MetaRuleDto> rules, int index, int direction)
    {
        if (index < 0 || index >= rules.Count) return -1;
        string state = rules[index].State;
        for (int j = index + direction; j >= 0 && j < rules.Count; j += direction)
            if (string.Equals(rules[j].State, state, StringComparison.Ordinal))
                return j;
        return -1;
    }
}
