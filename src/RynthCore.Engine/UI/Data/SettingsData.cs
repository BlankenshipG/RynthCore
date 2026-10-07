// ============================================================================
//  RynthCore.Engine - UI/Data/SettingsData.cs
//  RynthAi's Advanced Settings (docs/IMGUI_PARITY_PLAN.md §2.4): the payload,
//  its hub source and save command, and the ONE schema both faces draw from
//  (UI/Panels/SettingsPanel.cs, ImGui/Panels/SettingsFace.cs).
//
//  To add a setting: add the property to RynthAiSettings (it must match
//  RynthAi's SettingsBridgePayload) and a row to SettingsSchema.Tabs. Both
//  faces pick it up. The payload always carries every property, so a save
//  can't send a field missing (RynthAi applied missing fields as 0, 2026-09-27).
// ============================================================================

using System;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RynthCore.Engine.UI.Data;

/// <summary>Mirrors RynthAi's SettingsBridgePayload (RynthPluginGet/SetSettingsJson).</summary>
internal sealed class RynthAiSettings
{
    // Display
    public bool ShowTargetStaminaMana { get; set; }
    // UI
    public bool SuppressRetailRadar { get; set; }
    // No rows (RynthAi's plugin-drawn radar/chat windows are gone); kept so the payload
    // still carries them - a field missing from a save is applied as false.
    public bool ShowRynthRadar { get; set; }
    public bool RadarClickThrough { get; set; }
    // Retail-chatbox suppression is owned by the RynthChat plugin ("Hide retail chat"
    // in its gear menu); routing it through RynthAi's settings push fought that toggle.
    public bool ShowRynthChat { get; set; }
    public bool ChatClickThrough { get; set; }
    public bool SuppressRetailPowerbar { get; set; }
    // Misc
    public bool EnableFPSLimit { get; set; }
    public int TargetFPSFocused { get; set; } = 60;
    public int TargetFPSBackground { get; set; } = 30;
    public bool EnableAutocram { get; set; }
    public bool PeaceModeWhenIdle { get; set; }
    public bool StartMacroOnLogin { get; set; }
    public bool PatrolOnLogin { get; set; }
    // Decal bridge: stop RynthAi's macro when VTank's starts (one bot per client). Default on;
    // an older RynthAi doesn't send it, and a missing field must not read as "off".
    public bool YieldToVTank { get; set; } = true;
    // Server Features (RynthAi ServerFeatureGate): world names that turn on the ILT Hub, the
    // Progression planners and the attribute raiser, plus a manual override. An older RynthAi
    // doesn't send them; RynthAi lays a save over its current values, so these defaults are
    // only ever shown, never applied. ServerFeaturesStatus is read-only (RynthAi ignores it).
    public string FeatureServerNames { get; set; } = "InfiniteLeaftide, *Leaftide*";
    public bool ForceServerFeatures { get; set; }
    public string ServerFeaturesStatus { get; set; } = string.Empty;
    public bool EnableRaycasting { get; set; }
    public bool UseArcs { get; set; }
    public float BowArcVelocity { get; set; } = 25f;
    public float MissileArcClearance { get; set; } = 0.5f;
    // Vendoring (RynthAi AutoVendor; defaults match LegacyUiSettings)
    public bool AutoVendorEnabled { get; set; }
    public bool AutoVendorEnableBuying { get; set; } = true;
    public bool AutoVendorEnableSelling { get; set; } = true;
    public bool AutoVendorTestMode { get; set; } = true;
    public bool AutoVendorThink { get; set; }
    public bool AutoVendorShowMerchantInfo { get; set; } = true;
    public bool AutoVendorOnlyFromMainPack { get; set; }
    public int AutoVendorTries { get; set; } = 4;
    public int AutoVendorTriesTime { get; set; } = 5000;
    public bool LosDebugLog { get; set; }
    public float CrossbowArcVelocity { get; set; } = 40f;
    public float AtlatlArcVelocity { get; set; } = 22f;
    // Arc spells' horizontal speed: ACE flies every player arc at 40 m/s across the ground.
    public float MagicArcVelocity { get; set; } = 40f;
    public int BlacklistAttempts { get; set; } = 3;
    public int BlacklistTimeoutSec { get; set; } = 30;
    // Not shown in the panel, but carried so a save doesn't send them missing.
    public int BlacklistCastSettleMs { get; set; } = 1500;
    public int MonsterDisengageRange { get; set; }
    public int TargetNoProgressTimeoutSec { get; set; }
    public int GiveQueueIntervalMs { get; set; } = 150;
    // Recharge
    public int HealAt { get; set; } = 60;
    public int RestamAt { get; set; } = 30;
    public int GetManaAt { get; set; } = 40;
    public int TopOffHP { get; set; } = 95;
    public int TopOffStam { get; set; } = 95;
    public int TopOffMana { get; set; } = 95;
    public int HealOthersAt { get; set; } = 50;
    public int RestamOthersAt { get; set; } = 10;
    public int InfuseOthersAt { get; set; } = 10;
    // Kits, potions and safety stops
    public bool UsePotions { get; set; } = true;
    public bool UseBuffItems { get; set; } = true;
    public int MakeRationsBelow { get; set; } = 5;
    public bool UseKitsInMagicMode { get; set; } = true;
    public bool PeaceModeForKits { get; set; }
    public int KitMinSuccessPct { get; set; } = 70;
    public int EmergencyHealAt { get; set; } = 30;
    public int StaminaToHealthAt { get; set; } = 30;
    public int StaminaToHealthMinStamina { get; set; } = 20;
    public bool StopMacroOnDeath { get; set; }
    public bool StopMacroOnNoComponents { get; set; }
    public bool StopLootingWhenPackFull { get; set; } = true;
    public bool StopMacroWhenPackFull { get; set; }
    // Melee Combat
    public bool UseRecklessness { get; set; }
    public int MeleeAttackPower { get; set; } = -1;
    public int MeleeAttackHeight { get; set; } = 1;
    public int MissileAttackPower { get; set; } = -1;
    public int MissileAttackHeight { get; set; } = 1;
    public bool UseNativeAttack { get; set; } = true;
    public bool SummonPets { get; set; }
    public int PetMinMonsters { get; set; } = 1;
    // Spell Combat
    public int SpellCastIntervalMs { get; set; } = 400;
    public int AttackSpellIntervalMs { get; set; } = 1500;
    public bool CastDispelSelf { get; set; }
    public int MinRingTargets { get; set; } = 4;
    public int BlastRange { get; set; }
    public int MinBlastTargets { get; set; } = 3;
    public int MinSkillLevelTier1 { get; set; } = 35;
    public int MinSkillLevelTier2 { get; set; } = 85;
    public int MinSkillLevelTier3 { get; set; } = 135;
    public int MinSkillLevelTier4 { get; set; } = 185;
    public int MinSkillLevelTier5 { get; set; } = 235;
    public int MinSkillLevelTier6 { get; set; } = 285;
    public int MinSkillLevelTier7 { get; set; } = 335;
    public int MinSkillLevelTier8 { get; set; } = 435;
    // Ranges
    public int MonsterRange { get; set; } = 50;
    public int RingRange { get; set; } = 5;
    public int ApproachRange { get; set; } = 4;
    public double CorpseApproachRangeMax { get; set; } = 10.0;
    public double CorpseApproachRangeMin { get; set; } = 2.0;
    // Navigation
    public bool BoostNavPriority { get; set; }
    public float FollowNavMin { get; set; } = 1.5f;
    public float NavRingThickness { get; set; } = 6f;
    public float NavLineThickness { get; set; } = 6f;
    public float NavHeightOffset { get; set; } = 0.05f;
    public float NavSlopeSink { get; set; } = 1.5f;
    public bool ShowTerrainPassability { get; set; } = true;
    public bool OpenDoors { get; set; }
    public float OpenDoorRange { get; set; } = 5f;
    public bool AutoUnlockDoors { get; set; }
    public int MovementMode { get; set; }
    public float NavStopTurnAngle { get; set; } = 20f;
    public float NavResumeTurnAngle { get; set; } = 10f;
    public float NavDeadZone { get; set; } = 4f;
    public float NavSweepMult { get; set; } = 2.5f;
    public float NavLookaheadYards { get; set; } = 4f;
    public float NavShortcutYards { get; set; } = 1f;
    public float NavTurnRateDegPerSec { get; set; } = 270f;
    public float NavTier1TurnSpeed { get; set; } = 3f;
    public float PostPortalDelaySec { get; set; } = 4f;
    public float T2Speed { get; set; } = 1f;
    public float T2WalkWithinYd { get; set; } = 5f;
    public float T2DistanceTo { get; set; } = 0.5f;
    public float T2ReissueMs { get; set; } = 2000f;
    public float T2MaxRangeYd { get; set; } = 500f;
    public int T2MaxLandblocks { get; set; } = 3;
    // Buffing
    public bool EnableBuffing { get; set; } = true;
    public bool RebuffWhenIdle { get; set; }
    public int RebuffSecondsRemaining { get; set; } = 300;
    public int RebuffTopOffSecondsRemaining { get; set; } = 1200;
    public int BuffMinSkillLevelTier1 { get; set; } = 35;
    public int BuffMinSkillLevelTier2 { get; set; } = 85;
    public int BuffMinSkillLevelTier3 { get; set; } = 135;
    public int BuffMinSkillLevelTier4 { get; set; } = 185;
    public int BuffMinSkillLevelTier5 { get; set; } = 235;
    public int BuffMinSkillLevelTier6 { get; set; } = 285;
    public int BuffMinSkillLevelTier7 { get; set; } = 335;
    public int BuffMinSkillLevelTier8 { get; set; } = 435;
    // Crafting
    public bool EnableMissileCrafting { get; set; } = true;
    public string MissileCraftingState { get; set; } = string.Empty;
    public bool MissileCraftingActive { get; set; }
    public string MissileCraftingStatus { get; set; } = string.Empty;
    // Looting
    public bool EnableLooting { get; set; }
    public bool BoostLootPriority { get; set; }
    public bool LootOnlyRareCorpses { get; set; }
    public bool LootJumpEnabled { get; set; }
    public int LootJumpHeight { get; set; } = 10;
    public int LootOwnership { get; set; }
    public bool EnableAutostack { get; set; } = true;
    public bool ReadUnknownScrolls { get; set; } = true;   // VTank's default
    public bool EnableCombineSalvage { get; set; } = true;
    public bool CombineBagsDuringSalvage { get; set; } = true;
    public int LootInterItemDelayMs { get; set; } = 50;
    public int LootContentSettleMs { get; set; } = 100;
    public int LootEmptyCorpseMs { get; set; } = 300;
    public int LootClosingDelayMs { get; set; } = 200;
    public int LootAssessWindowMs { get; set; } = 200;
    public int LootRetryTimeoutMs { get; set; } = 500;
    public int LootOpenRetryMs { get; set; } = 1500;
    public int LootCorpseTimeoutMs { get; set; } = 12000;
    public int SalvageOpenDelayFirstMs { get; set; } = 400;
    public int SalvageOpenDelayFastMs { get; set; } = 50;
    public int SalvageAddDelayFirstMs { get; set; } = 600;
    public int SalvageAddDelayFastMs { get; set; } = 50;
    public int SalvageSalvageDelayMs { get; set; } = 50;
    public int SalvageResultDelayFirstMs { get; set; } = 1000;
    public int SalvageResultDelayFastMs { get; set; } = 250;

    /// <summary>A private copy for a face to edit (all members are values or strings).</summary>
    public RynthAiSettings Clone() => (RynthAiSettings)MemberwiseClone();
}

[JsonSerializable(typeof(RynthAiSettings))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = false, IncludeFields = false)]
internal partial class RynthAiSettingsJsonContext : JsonSerializerContext { }

/// <summary>RynthPluginGetSettingsJson while a Settings face is open (5 s, and right after each save).</summary>
internal sealed unsafe class SettingsSource : UiSource<RynthAiSettings>
{
    private delegate* unmanaged[Cdecl]<IntPtr> _getSettingsJson;
    private string? _lastJson;

    public SettingsSource() : base("Settings", periodMs: 5000) { }

    protected internal override void Poll()
    {
        if (_getSettingsJson == null)
            _getSettingsJson = (delegate* unmanaged[Cdecl]<IntPtr>)PluginExportBinder.Resolve("RynthAi", "RynthPluginGetSettingsJson");
        if (_getSettingsJson == null) return;
        IntPtr ptr = _getSettingsJson();
        string? json = ptr == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(ptr);
        if (string.IsNullOrEmpty(json) || json == _lastJson) return;
        _lastJson = json;
        RynthAiSettings? parsed = JsonSerializer.Deserialize(json, RynthAiSettingsJsonContext.Default.RynthAiSettings);
        if (parsed != null) Publish(parsed);
    }

    protected internal override void Reset()
    {
        _getSettingsJson = null;
        _lastJson = null;
        ClearSnapshot();
    }
}

/// <summary>Saves the whole settings payload through RynthPluginSetSettingsJson, on the pump thread.</summary>
internal static unsafe class SettingsCommands
{
    private static delegate* unmanaged[Cdecl]<IntPtr, void> _setSettingsJson;

    static SettingsCommands()
    {
        Plugins.PluginManager.PluginsUnloaded += () => _setSettingsJson = null;
    }

    /// <summary>
    /// <paramref name="settings"/> must be a copy the caller no longer changes
    /// (it is serialized later, on the pump thread). Any thread.
    /// </summary>
    public static void Save(RynthAiSettings settings)
    {
        UiDataHub.Post("RynthAi settings save", () =>
        {
            if (_setSettingsJson == null)
                _setSettingsJson = (delegate* unmanaged[Cdecl]<IntPtr, void>)PluginExportBinder.Resolve("RynthAi", "RynthPluginSetSettingsJson");
            if (_setSettingsJson == null) return;
            string json = JsonSerializer.Serialize(settings, RynthAiSettingsJsonContext.Default.RynthAiSettings);
            IntPtr ansi = Marshal.StringToHGlobalAnsi(json);
            try { _setSettingsJson(ansi); }
            finally { Marshal.FreeHGlobal(ansi); }
            UiSources.Settings.RequestRefresh();
        });
    }
}

// ── Schema ─────────────────────────────────────────────────────────────────

internal enum SettingKind { Bool, Int, Float, Double, Combo, Section, Spacer, Note, CraftingStatus, Button, Text, Status }

/// <summary>
/// One row of a settings tab. Numeric values go through double (bools as
/// 0/1, combos as the index). <see cref="Gates"/> marks rows whose value
/// shows or hides other rows (the Avalonia face rebuilds the tab on change).
/// </summary>
internal sealed record SettingRow(
    SettingKind Kind,
    string Label,
    string? Tooltip = null,
    Func<RynthAiSettings, double>? Get = null,
    Action<RynthAiSettings, double>? Set = null,
    double Min = 0,
    double Max = 0,
    double Step = 1,
    string[]? Items = null,
    Func<RynthAiSettings, bool>? VisibleWhen = null,
    bool Gates = false,
    // Button rows: the button's caption (Label is the row label) and its action. Any thread.
    string? ButtonText = null,
    Action? Click = null,
    // Text rows: the string value (committed on Enter / focus loss); Status rows: GetText only.
    Func<RynthAiSettings, string>? GetText = null,
    Action<RynthAiSettings, string>? SetText = null,
    // Text rows: greyed hint shown while the box is empty.
    string? Hint = null)
{
    public bool IsVisible(RynthAiSettings s) => VisibleWhen == null || VisibleWhen(s);
}

internal sealed record SettingsTab(string Name, SettingRow[] Rows);

internal static class SettingsSchema
{
    // Also drawn on the Nav panel (ImGui/Panels/NavFace.cs).
    public const string NavPointReachLabel = "Nav point reach (yd)";
    public const string NavPointReachTip =
        "How close to get to each nav point before moving on to the next, in yards. VTank calls it " +
        "Follow/Nav Min Distance (metas: FollowNavMin, or VTank's NavCloseStopRange). Follow stops this " +
        "close to its leader, and the nav marker ring shows it. Dungeon doorways use at most 1 yd.";

    // Also shown by the dashboard's Ranges slide-out (ImGui/Panels/RangesSlideOut.cs), which
    // edits these same rows (found by label with Find).
    public const string MonsterRangeTip =
        "How far away a monster can be for the bot to attack it, in yards. Monsters inside it also\n" +
        "count as \"in combat\" for buffing and the In Combat heal/stamina/mana thresholds.";
    public const string RingRangeTip =
        "Ring spells replace a monster's other shape when at least Min Ring Targets monsters are\n" +
        "within this many yards (the monster's rule needs Ring on).";
    public const string ApproachRangeTip =
        "VTank's Approach Distance, in yards (metas: ApproachRange or ApproachDistance). Saved with\n" +
        "the profile for metas; RynthAi's own combat doesn't use it at the moment.";
    public const string CorpseMaxTip =
        "Corpses within this many yards are walked to and looted (Looting on). Further ones are left.";

    /// <summary>The first row with <paramref name="label"/> on any tab (null if none). Not for per-frame use.</summary>
    public static SettingRow? Find(string label)
    {
        foreach (SettingsTab tab in Tabs)
            foreach (SettingRow row in tab.Rows)
                if (row.Label == label) return row;
        return null;
    }

    public static readonly string[] AttackHeights = { "Low", "Medium", "High" };
    public static readonly string[] LootOwnershipModes = { "My Kills Only", "Fellowship Kills", "All Corpses" };

    private static SettingRow Bool(string label, Func<RynthAiSettings, bool> get, Action<RynthAiSettings, bool> set,
        string? tip = null, Func<RynthAiSettings, bool>? when = null, bool gates = false) =>
        new(SettingKind.Bool, label, tip, s => get(s) ? 1 : 0, (s, v) => set(s, v != 0), VisibleWhen: when, Gates: gates);

    private static SettingRow Int(string label, Func<RynthAiSettings, int> get, Action<RynthAiSettings, int> set,
        int min, int max, int step, string? tip = null, Func<RynthAiSettings, bool>? when = null) =>
        new(SettingKind.Int, label, tip, s => get(s), (s, v) => set(s, (int)Math.Round(v)), min, max, step, VisibleWhen: when);

    private static SettingRow Float(string label, Func<RynthAiSettings, float> get, Action<RynthAiSettings, float> set,
        float min, float max, float step, string? tip = null, Func<RynthAiSettings, bool>? when = null) =>
        new(SettingKind.Float, label, tip, s => get(s), (s, v) => set(s, (float)v), min, max, step, VisibleWhen: when);

    private static SettingRow Double(string label, Func<RynthAiSettings, double> get, Action<RynthAiSettings, double> set,
        double min, double max, double step, string? tip = null) =>
        new(SettingKind.Double, label, tip, get, set, min, max, step);

    private static SettingRow Combo(string label, string[] items, Func<RynthAiSettings, int> get, Action<RynthAiSettings, int> set,
        string? tip = null) =>
        new(SettingKind.Combo, label, tip, s => get(s), (s, v) => set(s, (int)v), 0, items.Length - 1, 1, items);

    // Text size rows are stored by the engine (PanelTextScale), not in the
    // RynthAi profile, so the settings object is ignored here.
    private static SettingRow TextSize(string label, string panelTitle) =>
        Combo("   " + label, PanelTextScale.SizeNames, _ => PanelTextScale.GetOwn(panelTitle), (_, v) => PanelTextScale.Set(panelTitle, v),
            "Text size for this panel in the game: 0 = normal, each step 10% bigger, yourjokin = 2.5x.")
        with { VisibleWhen = _ => PanelTextScale.PerPanel };

    private static SettingRow Section(string text, Func<RynthAiSettings, bool>? when = null) =>
        new(SettingKind.Section, text, VisibleWhen: when);

    private static SettingRow Spacer(Func<RynthAiSettings, bool>? when = null) =>
        new(SettingKind.Spacer, "", VisibleWhen: when);

    private static SettingRow Note(string text, Func<RynthAiSettings, bool>? when = null) =>
        new(SettingKind.Note, text, VisibleWhen: when);

    private static SettingRow Button(string label, string buttonText, Action click, string? tip = null) =>
        new(SettingKind.Button, label, tip, ButtonText: buttonText, Click: click);

    private static SettingRow Text(string label, Func<RynthAiSettings, string> get, Action<RynthAiSettings, string> set,
        string? tip = null, string? hint = null) =>
        new(SettingKind.Text, label, tip, GetText: get, SetText: set, Hint: hint);

    // A one-line status RynthAi computes (read-only; empty = hidden).
    private static SettingRow Status(Func<RynthAiSettings, string> get) =>
        new(SettingKind.Status, "", GetText: get, VisibleWhen: s => !string.IsNullOrEmpty(get(s)));

    private static SettingRow[] Tiers(Func<RynthAiSettings, int>[] get, Action<RynthAiSettings, int>[] set)
    {
        var rows = new SettingRow[get.Length];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = Int($"Level {i + 1}", get[i], set[i], 1, 500, 5);
        return rows;
    }

    private static SettingRow[] Concat(params object[] parts)
    {
        var list = new System.Collections.Generic.List<SettingRow>();
        foreach (object p in parts)
        {
            if (p is SettingRow r) list.Add(r);
            else if (p is SettingRow[] rs) list.AddRange(rs);
        }
        return list.ToArray();
    }

    public static readonly SettingsTab[] Tabs =
    {
        new("Display", new[]
        {
            Bool("Show Target Stamina / Mana", s => s.ShowTargetStaminaMana, (s, v) => s.ShowTargetStaminaMana = v,
                "When enabled, displays stamina and mana bars for the selected target (requires appraisal data)."),
            Spacer(),
            Section("Text Size (in-game panels)"),
            Combo("All Panels", PanelTextScale.SizeNames, _ => PanelTextScale.GetAll(), (_, v) => PanelTextScale.SetAll(v),
                "Text size for every in-game panel: 0 = normal, each step 10% bigger, yourjokin = 2.5x."),
            Bool("Size Each Panel Separately", _ => PanelTextScale.PerPanel, (_, v) => PanelTextScale.PerPanel = v,
                "Off: every panel uses All Panels. On: set each panel's text size below.", gates: true),
            TextSize("RynthAi Dashboard", "RynthAi"),
            TextSize("Meta", "Meta"),
            TextSize("Damage", "Damage"),
            TextSize("Settings", "Settings"),
            TextSize("Monsters", "Monsters"),
            TextSize("Nav", "Nav"),
            TextSize("Items", "Items"),
            TextSize("Lua", "Lua"),
            TextSize("Radar", "Radar"),
            TextSize("Chat Filters", "ChatFilters"),
            TextSize("RynthNav", "RynthNav"),
            TextSize("Vision", "Vision"),
            TextSize("Status", "Status"),
            TextSize("Log", "Log"),
            TextSize("Tracker", "Tracker"),
        }),

        new("UI", new[]
        {
            Section("Radar"),
            Bool("Hide retail radar", s => s.SuppressRetailRadar, (s, v) => s.SuppressRetailRadar = v,
                "Suppress the game's built-in radar (bezel, compass, coords, blips)."),
            // "Show RynthRadar" / "Radar Click-Through" and "Show RynthChat" / "Chat Click-Through"
            // drove RynthAi's plugin-drawn radar and chat windows, which the engine no longer
            // draws (plugin ImGui rendering is RYNTHCORE_FORCE_IMGUI only). The Radar and Chat
            // panels open from the bar and have their own click-through (hold Ctrl) in their
            // gear menus. "Hide retail chat" lives in the Chat panel's gear menu
            // ("Hide retail chat"). The four fields stay in the payload so RynthAi and the
            // engine keep round-tripping them whichever is newer.
            Spacer(),
            Section("Power Bar"),
            Bool("Hide retail power bar", s => s.SuppressRetailPowerbar, (s, v) => s.SuppressRetailPowerbar = v,
                "Hide the vanilla attack/magic power bar that appears under the cursor while charging.\nThe bar's underlying combat state still works — only the on-screen widget is hidden."),
            Spacer(),
            Section("Vitals"),
            // Stored by the engine (RetailVitalsHooks, %LOCALAPPDATA%\RynthCore\retail_ui.txt),
            // not in the RynthAi profile, so the settings object is ignored here.
            Bool("Hide retail vitals bar", _ => Compatibility.RetailVitalsHooks.HideRetailVitals,
                (_, v) => Compatibility.RetailVitalsHooks.HideRetailVitals = v,
                "Hide the game's own health/stamina/mana bars; the RynthVision player plate and the RynthAi dashboard show them.\nSaved for this PC. Also: /rc ui retailvitals on|off."),
        }),

        new("Misc", new[]
        {
            Bool("Enable FPS Limit", s => s.EnableFPSLimit, (s, v) => s.EnableFPSLimit = v, gates: true),
            Int("Focused FPS", s => s.TargetFPSFocused, (s, v) => s.TargetFPSFocused = v, 10, 240, 1, when: s => s.EnableFPSLimit),
            Int("Background FPS", s => s.TargetFPSBackground, (s, v) => s.TargetFPSBackground = v, 5, 60, 1, when: s => s.EnableFPSLimit),
            Spacer(),
            Bool("Auto Cram", s => s.EnableAutocram, (s, v) => s.EnableAutocram = v,
                "Automatically moves items from your main pack into side packs.\nNote: recently used weapons stay in the main pack."),
            Bool("Peace Mode When Idle", s => s.PeaceModeWhenIdle, (s, v) => s.PeaceModeWhenIdle = v),
            Bool("Start Macro On Login", s => s.StartMacroOnLogin, (s, v) => s.StartMacroOnLogin = v,
                "Automatically starts the macro when RynthAi loads."),
            Bool("Patrol On Login", s => s.PatrolOnLogin, (s, v) => s.PatrolOnLogin = v,
                "Automatically starts dungeon patrol when RynthAi loads."),
            Bool("Yield to VTank", s => s.YieldToVTank, (s, v) => s.YieldToVTank = v,
                "With Decal: when VTank's macro starts (/vt start), RynthAi's macro stops, and it won't start while VTank runs.\nOne bot per client. Without Decal this does nothing. Also: /ra vtankyield on|off."),
            Bool("Enable Raycasting", s => s.EnableRaycasting, (s, v) => s.EnableRaycasting = v),
            Spacer(),
            Section("Server Features (ILT / infinite attributes)"),
            Status(s => s.ServerFeaturesStatus),
            Text("Servers", s => s.FeatureServerNames, (s, v) => s.FeatureServerNames = v,
                "Comma-separated world names that turn on the ILT Hub, the Skills panel's Progression planners\n" +
                "and the attribute raiser. * is a wildcard (*Leaftide* matches any name containing Leaftide).\n" +
                "Case does not matter. Saved with the RynthAi profile.",
                hint: "InfiniteLeaftide, *Leaftide*"),
            Bool("Force enable on this server (manual override)", s => s.ForceServerFeatures, (s, v) => s.ForceServerFeatures = v,
                "Turn the features on for whatever world you are on, even if it is not in the list.\n" +
                "Each feature still follows what the server reports (for example /xp off keeps the attribute raiser off)."),
            Spacer(),
            Section("Missile Arc Velocities (m/s)"),
            Bool("Use Arcs for Missile LoS", s => s.UseArcs, (s, v) => s.UseArcs = v,
                "A missile target must pass both the straight line and the real arrow arc, ceilings included (dungeons too).\nWhen off, all missile LoS checks are linear (eye-to-eye).", gates: true),
            Float("Bow", s => s.BowArcVelocity, (s, v) => s.BowArcVelocity = v, 10f, 60f, 0.5f,
                "Bow projectile speed (m/s). Lower = higher arc.", when: s => s.UseArcs),
            Float("Crossbow", s => s.CrossbowArcVelocity, (s, v) => s.CrossbowArcVelocity = v, 10f, 80f, 0.5f, when: s => s.UseArcs),
            Float("Atlatl", s => s.AtlatlArcVelocity, (s, v) => s.AtlatlArcVelocity = v, 10f, 60f, 0.5f, when: s => s.UseArcs),
            Float("Magic Arc", s => s.MagicArcVelocity, (s, v) => s.MagicArcVelocity = v, 10f, 60f, 0.5f,
                "Arc spells' horizontal speed (m/s). ACE: 40. Lower = higher arc.\nA monster rule with Arc on casts an arc only when this path reaches the target (walls, and ceilings in dungeons); otherwise its other shape (Streak, Blast) or a bolt. Needs Enable Raycasting. /ra lostest magic shows the arc to the selected mob."),
            Float("Arc Clearance (m)", s => s.MissileArcClearance, (s, v) => s.MissileArcClearance = MathF.Max(0f, v), 0f, 3f, 0.1f,
                "Extra headroom the arc must have at mid-flight. Raise it if arrows still hit ceilings; lower it if reachable mobs get skipped.", when: s => s.UseArcs),
            Bool("LoS Debug Log", s => s.LosDebugLog, (s, v) => s.LosDebugLog = v,
                "Log each blocked missile target and why (arc peak, where it hits) to the RynthCore log. /ra lostest bow tests the selected mob."),
            Spacer(),
            Section("Monster Blacklist"),
            Int("Attempts Before Blacklist", s => s.BlacklistAttempts, (s, v) => s.BlacklistAttempts = v, 1, 20, 1,
                "How many failed attack attempts on a mob before it gets blacklisted."),
            Int("Blacklist Timeout (sec)", s => s.BlacklistTimeoutSec, (s, v) => s.BlacklistTimeoutSec = v, 5, 120, 5,
                "How long a blacklisted mob is ignored before re-trying."),
            Int("No Progress Timeout (sec)", s => s.TargetNoProgressTimeoutSec, (s, v) => s.TargetNoProgressTimeoutSec = v, 0, 300, 10,
                "Blacklist a target after being engaged this many seconds without dealing damage.\n0 = disabled. Default 60s."),
            Spacer(),
            Section("Give Queue"),
            Int("Give Interval (ms)", s => s.GiveQueueIntervalMs, (s, v) => s.GiveQueueIntervalMs = v, 50, 2000, 50,
                "Minimum delay between each item sent by /ra givea commands."),
        }),

        new("Recharge", new[]
        {
            Section("In Combat — target within Monster Range (%)"),
            Int("Heal At", s => s.HealAt, (s, v) => s.HealAt = v, 0, 100, 1,
                "While a target is within Monster Range, cast Heal Self when HP < this %."),
            Int("Re-stam At", s => s.RestamAt, (s, v) => s.RestamAt = v, 0, 100, 1,
                "While a target is within Monster Range, cast Revitalize Self when Stamina < this %."),
            Int("Get Mana At", s => s.GetManaAt, (s, v) => s.GetManaAt = v, 0, 100, 1,
                "While a target is within Monster Range, cast Stamina to Mana Self when Mana < this % (needs stam > 15%)."),
            Spacer(),
            Section("Idle Top-Off — no targets in range (%)"),
            Int("Top HP", s => s.TopOffHP, (s, v) => s.TopOffHP = v, 0, 100, 1,
                "When no targets are within Monster Range, heal up to this HP %. Usually set higher than Heal At."),
            Int("Top Stam", s => s.TopOffStam, (s, v) => s.TopOffStam = v, 0, 100, 1,
                "When no targets are within Monster Range, re-stam up to this %."),
            Int("Top Mana", s => s.TopOffMana, (s, v) => s.TopOffMana = v, 0, 100, 1,
                "When no targets are within Monster Range, recharge mana up to this %."),
            Spacer(),
            Section("Kits & Potions"),
            Bool("Use Potions", s => s.UsePotions, (s, v) => s.UsePotions = v,
                "Drink health, mana and stamina potions from the Items panel when a vital is low\nand no spell can fix it (no spell, Life Magic untrained, out of components)."),
            Bool("Use Buff Items", s => s.UseBuffItems, (s, v) => s.UseBuffItems = v,
                "Use Asheron's Benediction, Asheron's Lesser Benediction and Blackmoor's Favor\nfrom your pack whenever their buff isn't on you (they're never used up)."),
            Int("Make Field Rations Below", s => s.MakeRationsBelow, (s, v) => s.MakeRationsBelow = v, 0, 100, 1,
                "Cook field rations (Cooking Pot on Dried Rations, 25 at a time) when you have fewer than this.\n0 = off. Rations restore stamina and are eaten like stamina potions."),
            Bool("Use Kits In Magic Mode", s => s.UseKitsInMagicMode, (s, v) => s.UseKitsInMagicMode = v,
                "Allow healing kits while in magic mode."),
            Bool("Peace Mode For Kits", s => s.PeaceModeForKits, (s, v) => s.PeaceModeForKits = v,
                "Switch to peace mode before using a healing kit."),
            Int("Emergency Heal At", s => s.EmergencyHealAt, (s, v) => s.EmergencyHealAt = v, 0, 100, 1,
                "At or under this health %, out of Magic mode a healing kit is used ahead of the\nnormal Heal At chain (kit, Heal Self, potion), and while a spell cast is pending a kit\nor potion is used. Stamina to Health has its own line below. 0 = off. Starts at 30."),
            Int("Stamina To Health At", s => s.StaminaToHealthAt, (s, v) => s.StaminaToHealthAt = v, 0, 100, 1,
                "At or under this health %, cast Stamina to Health Self ahead of the normal\nHeal At chain (out of Magic mode a kit is tried first). If it can't be cast,\nthe normal chain (kit, Heal Self, potion) runs. 0 = never cast it. Starts at 30."),
            Int("Stamina To Health Min Stamina", s => s.StaminaToHealthMinStamina, (s, v) => s.StaminaToHealthMinStamina = v, 0, 100, 1,
                "Cast Stamina to Health only while stamina is over this %, so it doesn't\ndrain you to nothing. Starts at 20."),
            Int("Kit Min Success %", s => s.KitMinSuccessPct, (s, v) => s.KitMinSuccessPct = v, 0, 100, 1,
                "Use a healing or stamina kit only when its chance to work is at least this %,\nworked out like the server does: Healing skill + the kit's bonus vs. what's missing.\nBelow it, the spell is cast instead. 0 = always use kits. Starts at 70."),
            Spacer(),
            Section("Safety Stops"),
            Bool("Stop Macro On Death", s => s.StopMacroOnDeath, (s, v) => s.StopMacroOnDeath = v,
                "Stop the macro when you die. Off by default: most metas run back on their own."),
            Bool("Stop Macro On No Components", s => s.StopMacroOnNoComponents, (s, v) => s.StopMacroOnNoComponents = v,
                "Stop the macro after 3 \"missing components\" failures within a minute."),
            Bool("Stop Macro When Pack Full", s => s.StopMacroWhenPackFull, (s, v) => s.StopMacroWhenPackFull = v,
                "Stop the macro when no pack has a free slot."),
            Spacer(),
            Section("Helper Settings (%) — NOT WIRED YET"),
            Int("Heal Others", s => s.HealOthersAt, (s, v) => s.HealOthersAt = v, 0, 100, 1,
                "Intended: cast Heal Other on a fellow when their HP < this %. Not implemented yet — slider is inert."),
            Int("Re-stam Others", s => s.RestamOthersAt, (s, v) => s.RestamOthersAt = v, 0, 100, 1,
                "Intended: cast Revitalize Other on a fellow when their Stamina < this %. Not implemented yet — slider is inert."),
            Int("Infuse Others", s => s.InfuseOthersAt, (s, v) => s.InfuseOthersAt = v, 0, 100, 1,
                "Intended: cast Infuse Mana Other on a fellow when their Mana < this %. Not implemented yet — slider is inert."),
        }),

        new("Melee Combat", new[]
        {
            Section("Attack Power & Height"),
            Bool("Use Recklessness", s => s.UseRecklessness, (s, v) => s.UseRecklessness = v,
                "When enabled and Recklessness is trained, auto power uses 80% instead of 100%."),
            Spacer(),
            Bool("Melee Auto Power", s => s.MeleeAttackPower < 0, (s, v) => s.MeleeAttackPower = v ? -1 : 100, gates: true),
            Int("Melee Power %", s => s.MeleeAttackPower, (s, v) => s.MeleeAttackPower = v, 0, 100, 5, when: s => s.MeleeAttackPower >= 0),
            Combo("Melee Attack Height", AttackHeights, s => s.MeleeAttackHeight, (s, v) => s.MeleeAttackHeight = v),
            Spacer(),
            Bool("Missile Auto Power", s => s.MissileAttackPower < 0, (s, v) => s.MissileAttackPower = v ? -1 : 100, gates: true),
            Int("Missile Power %", s => s.MissileAttackPower, (s, v) => s.MissileAttackPower = v, 0, 100, 5, when: s => s.MissileAttackPower >= 0),
            Combo("Missile Attack Height", AttackHeights, s => s.MissileAttackHeight, (s, v) => s.MissileAttackHeight = v),
            Spacer(),
            Bool("Use Native Attack", s => s.UseNativeAttack, (s, v) => s.UseNativeAttack = v,
                "Uses the client's combat pipeline for attacks.\nThe client handles turn-to-face naturally (no backwards arrows)."),
            Spacer(),
            Bool("Summon Pets", s => s.SummonPets, (s, v) => s.SummonPets = v),
            Int("Pet Min Monsters", s => s.PetMinMonsters, (s, v) => s.PetMinMonsters = v, 1, 20, 1),
        }),

        new("Spell Combat", Concat(
            Section("War/Void Casting Settings"),
            Int("Buff Spell Interval (ms)", s => s.SpellCastIntervalMs, (s, v) => s.SpellCastIntervalMs = v, 100, 1500, 50,
                "Delay between BUFF / utility spell casts (not combat).\nLower = faster buff chains. 400ms is a good balance.\nBelow 200ms may cause fizzles or dropped casts on laggy servers."),
            Int("Attack Spell Delay (ms)", s => s.AttackSpellIntervalMs, (s, v) => s.AttackSpellIntervalMs = v, 250, 5000, 50,
                "Delay between offensive (war/void) COMBAT casts only.\nSpacing casts ~1-2s (1500ms default) stops back-to-back\n\"You're too busy!\" refusals that drop casts and cost kills.\nDoes NOT affect buffing speed."),
            Bool("Cast Dispel Self", s => s.CastDispelSelf, (s, v) => s.CastDispelSelf = v),
            Spacer(),
            Section("Ring Spell Override"),
            Int("Min Ring Targets", s => s.MinRingTargets, (s, v) => s.MinRingTargets = v, 1, 20, 1,
                "If this many monsters are within ring range, ring spells are used instead of arc/bolt/streak."),
            Spacer(),
            Section("Blast Spell Override"),
            Int("Blast Range (yd)", s => s.BlastRange, (s, v) => s.BlastRange = v, 0, 100, 1,
                "0 = off: monsters with Blast on always cast blasts.\nAbove 0: a blast is cast only when Min Blast Targets monsters are\nwithin this range in the blast's fan toward the target (it fires 3\nprojectiles over 90 degrees); otherwise the monster's other shape, or a bolt."),
            Int("Min Blast Targets", s => s.MinBlastTargets, (s, v) => s.MinBlastTargets = v, 1, 20, 1,
                "How many monsters (the target included) must be in the blast's fan, within Blast Range."),
            Spacer(),
            Section("Spell Difficulty (Min Buffed Skill)"),
            Tiers(
                new Func<RynthAiSettings, int>[] { s => s.MinSkillLevelTier1, s => s.MinSkillLevelTier2, s => s.MinSkillLevelTier3, s => s.MinSkillLevelTier4,
                                                   s => s.MinSkillLevelTier5, s => s.MinSkillLevelTier6, s => s.MinSkillLevelTier7, s => s.MinSkillLevelTier8 },
                new Action<RynthAiSettings, int>[] { (s, v) => s.MinSkillLevelTier1 = v, (s, v) => s.MinSkillLevelTier2 = v, (s, v) => s.MinSkillLevelTier3 = v, (s, v) => s.MinSkillLevelTier4 = v,
                                                     (s, v) => s.MinSkillLevelTier5 = v, (s, v) => s.MinSkillLevelTier6 = v, (s, v) => s.MinSkillLevelTier7 = v, (s, v) => s.MinSkillLevelTier8 = v }))),

        new("Ranges", new[]
        {
            Section("Standard Ranges (Yards)"),
            Int("Monster Range", s => s.MonsterRange, (s, v) => s.MonsterRange = v, 1, 200, 1, MonsterRangeTip),
            Int("Ring Range", s => s.RingRange, (s, v) => s.RingRange = v, 1, 50, 1, RingRangeTip),
            Int("Approach Range", s => s.ApproachRange, (s, v) => s.ApproachRange = v, 1, 50, 1, ApproachRangeTip),
            Spacer(),
            Section("Corpse Acquisition (Yards)"),
            Double("Corpse Max (yd)", s => s.CorpseApproachRangeMax, (s, v) => s.CorpseApproachRangeMax = v, 0.5, 50.0, 0.5, CorpseMaxTip),
            Double("Corpse Min (yd)", s => s.CorpseApproachRangeMin, (s, v) => s.CorpseApproachRangeMin = v, 0.5, 20.0, 0.5,
                "How close to walk to a corpse before opening it, in yards (never more than Corpse Max)."),
        }),

        new("Navigation", new[]
        {
            Bool("Boost Nav Priority", s => s.BoostNavPriority, (s, v) => s.BoostNavPriority = v),
            Float(NavPointReachLabel, s => s.FollowNavMin, (s, v) => s.FollowNavMin = v, 0.5f, 20f, 0.1f, NavPointReachTip),
            Spacer(),
            Section("Nav Marker Display"),
            Float("Ring Thickness", s => s.NavRingThickness, (s, v) => s.NavRingThickness = v, 1f, 16f, 1f),
            Float("Line Thickness", s => s.NavLineThickness, (s, v) => s.NavLineThickness = v, 1f, 16f, 1f),
            Float("Height Offset", s => s.NavHeightOffset, (s, v) => s.NavHeightOffset = v, -5f, 5f, 0.05f,
                "Vertical offset for nav markers above the ground. Negative = lower."),
            Float("Slope Sink", s => s.NavSlopeSink, (s, v) => s.NavSlopeSink = v, 0f, 8f, 0.1f,
                "Extra downward offset on slopes only, per unit of terrain steepness. 0 = off; flat ground is unaffected. ~1.5 cancels the float for a default-radius ring."),
            Bool("Show Terrain Passability", s => s.ShowTerrainPassability, (s, v) => s.ShowTerrainPassability = v,
                "Highlight impassable terrain triangles in red."),
            Spacer(),
            Section("Doors"),
            Bool("Open Doors While Navigating", s => s.OpenDoors, (s, v) => s.OpenDoors = v,
                "Automatically open doors encountered during navigation.", gates: true),
            Float("Door Detection Range (yd)", s => s.OpenDoorRange, (s, v) => s.OpenDoorRange = v, 0.1f, 70f, 1f, when: s => s.OpenDoors),
            Bool("Auto-Unlock Doors", s => s.AutoUnlockDoors, (s, v) => s.AutoUnlockDoors = v,
                "If a door is locked, try to use a lockpick from your Consumable Items list.", when: s => s.OpenDoors),
            Spacer(),
            Section("Steering"),
            Float("Stop & Turn Angle", s => s.NavStopTurnAngle, (s, v) => s.NavStopTurnAngle = v, 1f, 90f, 1f,
                "Stop forward motion and turn in place when heading error exceeds this."),
            Float("Resume Run Angle", s => s.NavResumeTurnAngle, (s, v) => s.NavResumeTurnAngle = v, 1f, 45f, 1f,
                "Resume running once the turn-in-place error drops below this."),
            Float("Dead Zone", s => s.NavDeadZone, (s, v) => s.NavDeadZone = v, 0.5f, 20f, 0.5f,
                "Ignore heading corrections smaller than this."),
            Float("Sweep Detect Mult", s => s.NavSweepMult, (s, v) => s.NavSweepMult = v, 0.5f, 10f, 0.1f,
                "Closest-approach detection radius multiplier."),
            Float("Lookahead (yd)", s => s.NavLookaheadYards, (s, v) => s.NavLookaheadYards = MathF.Max(0f, v), 0f, 30f, 0.5f,
                "Within this distance of a waypoint, blend the aim point toward the next one to cut corners smoothly. 0 = off."),
            Float("Turn Rate (deg/s)", s => s.NavTurnRateDegPerSec, (s, v) => s.NavTurnRateDegPerSec = v, 30f, 720f, 15f,
                "Max turn speed while navigating. Higher = snappier turns, lower = gentler."),
            Float("Post-Portal Delay (s)", s => s.PostPortalDelaySec, (s, v) => s.PostPortalDelaySec = MathF.Max(0f, v), 0f, 30f, 0.25f,
                "Seconds to settle after any portal/recall teleport before nav resumes."),
            Spacer(when: s => s.MovementMode == 2),
            Section("Tier 2 Tuning", when: s => s.MovementMode == 2),
            Float("Speed", s => s.T2Speed, (s, v) => s.T2Speed = v, 0.1f, 5f, 0.1f, when: s => s.MovementMode == 2),
            Float("Walk Within (yd)", s => s.T2WalkWithinYd, (s, v) => s.T2WalkWithinYd = v, 1f, 50f, 1f, when: s => s.MovementMode == 2),
            Float("Stop Distance (yd)", s => s.T2DistanceTo, (s, v) => s.T2DistanceTo = v, 0.1f, 10f, 0.1f, when: s => s.MovementMode == 2),
            Float("Reissue Timeout (ms)", s => s.T2ReissueMs, (s, v) => s.T2ReissueMs = v, 100f, 10000f, 100f, when: s => s.MovementMode == 2),
            Float("Max Range (yd)", s => s.T2MaxRangeYd, (s, v) => s.T2MaxRangeYd = v, 50f, 2000f, 50f, when: s => s.MovementMode == 2),
            Int("Max Landblock Dist", s => s.T2MaxLandblocks, (s, v) => s.T2MaxLandblocks = v, 1, 20, 1, when: s => s.MovementMode == 2),
        }),

        new("Buffing", Concat(
            Bool("Enable Buffing", s => s.EnableBuffing, (s, v) => s.EnableBuffing = v),
            Bool("Rebuff When Idle", s => s.RebuffWhenIdle, (s, v) => s.RebuffWhenIdle = v),
            Int("Rebuff With (seconds left)", s => s.RebuffSecondsRemaining, (s, v) => s.RebuffSecondsRemaining = v, 30, 1800, 30,
                "Recast a self buff when its remaining duration drops below this value.\nDefault 300 (5 minutes). Lower values rebuff more eagerly."),
            Int("Also Refresh Under (seconds left)", s => s.RebuffTopOffSecondsRemaining, (s, v) => s.RebuffTopOffSecondsRemaining = v, 30, 3600, 60,
                "When a buff is due, also recast every other buff with less than this much time left,\nso they land together. Buffs with more time are left alone. Default 1200 (20 minutes).\nAt or below 'Rebuff With', only the expiring buff is recast."),
            Spacer(),
            Section("Buff Difficulty (Min Buffed Skill)"),
            Tiers(
                new Func<RynthAiSettings, int>[] { s => s.BuffMinSkillLevelTier1, s => s.BuffMinSkillLevelTier2, s => s.BuffMinSkillLevelTier3, s => s.BuffMinSkillLevelTier4,
                                                   s => s.BuffMinSkillLevelTier5, s => s.BuffMinSkillLevelTier6, s => s.BuffMinSkillLevelTier7, s => s.BuffMinSkillLevelTier8 },
                new Action<RynthAiSettings, int>[] { (s, v) => s.BuffMinSkillLevelTier1 = v, (s, v) => s.BuffMinSkillLevelTier2 = v, (s, v) => s.BuffMinSkillLevelTier3 = v, (s, v) => s.BuffMinSkillLevelTier4 = v,
                                                     (s, v) => s.BuffMinSkillLevelTier5 = v, (s, v) => s.BuffMinSkillLevelTier6 = v, (s, v) => s.BuffMinSkillLevelTier7 = v, (s, v) => s.BuffMinSkillLevelTier8 = v }))),

        new("Crafting", new[]
        {
            Section("Missile Ammo Crafting"),
            Bool("Enable Missile Crafting", s => s.EnableMissileCrafting, (s, v) => s.EnableMissileCrafting = v,
                "Auto-manage missile ammo when the ammo slot is empty or low.", gates: true),
            Note("(Disabled)", when: s => !s.EnableMissileCrafting),
            Spacer(when: s => s.EnableMissileCrafting),
            new SettingRow(SettingKind.CraftingStatus, "State:", VisibleWhen: s => s.EnableMissileCrafting && !string.IsNullOrEmpty(s.MissileCraftingState)),
        }),

        new("Looting", new[]
        {
            Bool("Enable Looting", s => s.EnableLooting, (s, v) => s.EnableLooting = v),
            Bool("Boost Loot Priority", s => s.BoostLootPriority, (s, v) => s.BoostLootPriority = v),
            Bool("Loot Only Rare Corpses", s => s.LootOnlyRareCorpses, (s, v) => s.LootOnlyRareCorpses = v),
            Bool("Learn Unknown Spells", s => s.ReadUnknownScrolls, (s, v) => s.ReadUnknownScrolls = v,
                "Loots scrolls of spells you don't know and can learn (magic school trained or\nspecialized, skill high enough), and reads them when it's safe. Scrolls already\nin your pack are read too. Needs Enable Looting for corpses."),
            Bool("Stop Looting When Pack Full", s => s.StopLootingWhenPackFull, (s, v) => s.StopLootingWhenPackFull = v,
                "Don't start on another corpse when no pack has a free slot (says so once in chat)."),
            Bool("Jump When Looting", s => s.LootJumpEnabled, (s, v) => s.LootJumpEnabled = v, gates: true),
            Int("Jump Height", s => s.LootJumpHeight, (s, v) => s.LootJumpHeight = v, 1, 100, 5, when: s => s.LootJumpEnabled),
            Spacer(),
            Section("Corpse Ownership"),
            Combo("Loot From", LootOwnershipModes, s => s.LootOwnership, (s, v) => s.LootOwnership = v),
            Spacer(),
            Section("Inventory Management"),
            Bool("Enable Autostack", s => s.EnableAutostack, (s, v) => s.EnableAutostack = v),
            Bool("Enable Autocram", s => s.EnableAutocram, (s, v) => s.EnableAutocram = v,
                "Automatically moves items from your main pack into side packs.\nNote: recently used weapons stay in the main pack."),
            Bool("Combine Salvage Bags", s => s.EnableCombineSalvage, (s, v) => s.EnableCombineSalvage = v,
                "After the salvage queue empties, move same-name bags together so the server merges them."),
            Bool("Combine Bags During Salvage", s => s.CombineBagsDuringSalvage, (s, v) => s.CombineBagsDuringSalvage = v,
                "When salvaging an item, also add any under-full salvage bag of the same material to the salvage panel."),
            Button("Floating HUDs", "Inventory HUDs...", () => RynthAiCommands.ApplyRemoteCommand("huds", "show"),
                "Opens the item count HUD / Mini Remote setup window (/ra huds)."),
            Spacer(),
            Section("Loot Timers (ms)"),
            Int("Inter-Item Delay", s => s.LootInterItemDelayMs, (s, v) => s.LootInterItemDelayMs = v, 0, 5000, 25),
            Int("Content Settle", s => s.LootContentSettleMs, (s, v) => s.LootContentSettleMs = v, 0, 5000, 25),
            Int("Empty Corpse Wait", s => s.LootEmptyCorpseMs, (s, v) => s.LootEmptyCorpseMs = v, 0, 5000, 25),
            Int("Closing Delay", s => s.LootClosingDelayMs, (s, v) => s.LootClosingDelayMs = v, 0, 5000, 25),
            Int("Assess Window", s => s.LootAssessWindowMs, (s, v) => s.LootAssessWindowMs = v, 0, 5000, 25),
            Int("Loot Retry Timeout", s => s.LootRetryTimeoutMs, (s, v) => s.LootRetryTimeoutMs = v, 0, 10000, 100),
            Int("Corpse Open Retry", s => s.LootOpenRetryMs, (s, v) => s.LootOpenRetryMs = v, 0, 10000, 100),
            Int("Corpse Timeout", s => s.LootCorpseTimeoutMs, (s, v) => s.LootCorpseTimeoutMs = v, 0, 60000, 500),
            Spacer(),
            Section("Salvage Timers (ms) - First / Fast"),
            Int("Open (First)", s => s.SalvageOpenDelayFirstMs, (s, v) => s.SalvageOpenDelayFirstMs = v, 0, 5000, 50),
            Int("Open (Fast)", s => s.SalvageOpenDelayFastMs, (s, v) => s.SalvageOpenDelayFastMs = v, 0, 2000, 25),
            Int("Add Item (First)", s => s.SalvageAddDelayFirstMs, (s, v) => s.SalvageAddDelayFirstMs = v, 0, 5000, 50),
            Int("Add Item (Fast)", s => s.SalvageAddDelayFastMs, (s, v) => s.SalvageAddDelayFastMs = v, 0, 2000, 25),
            Int("Salvage Click", s => s.SalvageSalvageDelayMs, (s, v) => s.SalvageSalvageDelayMs = v, 0, 2000, 25),
            Int("Result (First)", s => s.SalvageResultDelayFirstMs, (s, v) => s.SalvageResultDelayFirstMs = v, 0, 5000, 50),
            Int("Result (Fast)", s => s.SalvageResultDelayFastMs, (s, v) => s.SalvageResultDelayFastMs = v, 0, 2000, 25),
        }),

        new("Vendoring", new[]
        {
            Section("AutoVendor"),
            Bool("Enabled", s => s.AutoVendorEnabled, (s, v) => s.AutoVendorEnabled = v,
                "Buy and sell by a loot profile when a vendor opens (and allow /ub autovendor).\nProfile: <Vendor Name>.utl, else default.utl, in the AutoVendor folder."),
            Bool("Test Mode (only print what it would do)", s => s.AutoVendorTestMode, (s, v) => s.AutoVendorTestMode = v,
                "Lists what would be bought and sold without trading. Leave this on until the lists look right."),
            Bool("Buy", s => s.AutoVendorEnableBuying, (s, v) => s.AutoVendorEnableBuying = v),
            Bool("Sell", s => s.AutoVendorEnableSelling, (s, v) => s.AutoVendorEnableSelling = v),
            Bool("Only Sell From Main Pack", s => s.AutoVendorOnlyFromMainPack, (s, v) => s.AutoVendorOnlyFromMainPack = v),
            Bool("Show Merchant Info", s => s.AutoVendorShowMerchantInfo, (s, v) => s.AutoVendorShowMerchantInfo = v,
                "Print the vendor's buy/sell rates and max value when it opens."),
            Bool("Think When Finished", s => s.AutoVendorThink, (s, v) => s.AutoVendorThink = v,
                "Send 'AutoVendor finished: <vendor>' (and failures) as a /tell to yourself, for metas."),
            Spacer(),
            Section("/ub vendor open"),
            Int("Tries", s => s.AutoVendorTries, (s, v) => s.AutoVendorTries = v, 1, 20, 1),
            Int("Time Between Tries (ms)", s => s.AutoVendorTriesTime, (s, v) => s.AutoVendorTriesTime = v, 500, 30000, 250),
            Spacer(),
            Note("Never sold: equipped, attuned, bonded, retained, tinkered, imbued, inscribed, rare, zero value, packs, or anything a Keep rule could match."),
        }),
    };
}
