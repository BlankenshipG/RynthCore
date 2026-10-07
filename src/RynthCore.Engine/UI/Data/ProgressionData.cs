// ============================================================================
//  RynthCore.Engine - UI/Data/ProgressionData.cs
//  RynthAi's ILT progression planners (augmentations, enlightenment) and the
//  infinite-attribute raiser for the Skills panel's Progression tab. RynthAi owns the math and the persisted
//  inputs; this side only shows the snapshot and sends "prog ..." remote
//  commands (RynthAiCommands.ApplyRemoteCommand) for edits and actions.
// ============================================================================

using System;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using RynthCore.Engine.Plugins;

namespace RynthCore.Engine.UI.Data;

/// <summary>RynthPluginGetProgressionJson's payload. Display strings arrive preformatted.</summary>
internal sealed class ProgressionInfo
{
    /// <summary>False off ILT-like worlds (or before the ILT Hub exists): the tab is hidden.</summary>
    [JsonPropertyName("available")] public bool Available { get; set; }
    [JsonPropertyName("world")]     public string World { get; set; } = "";
    [JsonPropertyName("aug")]       public AugPlanner Aug { get; set; } = new();
    [JsonPropertyName("enl")]       public EnlPlanner Enl { get; set; } = new();
    /// <summary>Absent from an older RynthAi: stays empty and the section shows "update RynthAi".</summary>
    [JsonPropertyName("attr")]      public AttrRaiser? Attr { get; set; }
}

/// <summary>RynthAi's IltAttributeRaiser: server costs ("/xp all") and the raise settings.</summary>
internal sealed class AttrRaiser
{
    /// <summary>The raiser can't run here; <see cref="Reason"/> says why.</summary>
    [JsonPropertyName("off")]        public bool Off { get; set; }
    [JsonPropertyName("reason")]     public string Reason { get; set; } = "";
    [JsonPropertyName("auto")]       public bool Auto { get; set; }
    /// <summary>Minutes between auto-raise runs.</summary>
    [JsonPropertyName("every")]      public int Every { get; set; } = 5;
    /// <summary>0 priority, 1 round robin, 2 cheapest first.</summary>
    [JsonPropertyName("mode")]       public int Mode { get; set; }
    [JsonPropertyName("running")]    public bool Running { get; set; }
    /// <summary>XP reserve, preformatted ("0", "1.50B").</summary>
    [JsonPropertyName("keep")]       public string Keep { get; set; } = "0";
    /// <summary>XP reserve in full ("1,234,567"): what the reserve box edits.</summary>
    [JsonPropertyName("keepFull")]   public string KeepFull { get; set; } = "";
    [JsonPropertyName("status")]     public string Status { get; set; } = "";
    [JsonPropertyName("lastRun")]    public string LastRun { get; set; } = "";
    [JsonPropertyName("unassigned")] public string Unassigned { get; set; } = "";
    /// <summary>Time to the next auto run ("" when auto is off).</summary>
    [JsonPropertyName("next")]       public string Next { get; set; } = "";
    [JsonPropertyName("costsAge")]   public string CostsAge { get; set; } = "";
    /// <summary>In raise-priority order.</summary>
    [JsonPropertyName("rows")]       public AttrRow[] Rows { get; set; } = Array.Empty<AttrRow>();
}

internal sealed class AttrRow
{
    /// <summary>/attr abbreviation: str end coo qui foc sel hea sta man.</summary>
    [JsonPropertyName("key")]    public string Key { get; set; } = "";
    [JsonPropertyName("label")]  public string Label { get; set; } = "";
    [JsonPropertyName("vital")]  public bool Vital { get; set; }
    /// <summary>Ticked: "Raise now" and auto-raise may spend on it.</summary>
    [JsonPropertyName("on")]     public bool On { get; set; }
    /// <summary>Base value RynthAi last saw in a server reply (-1 = unknown; the panel prefers the client's).</summary>
    [JsonPropertyName("base")]   public int Base { get; set; } = -1;
    /// <summary>Next-level cost from "/xp all", preformatted ("?" until read).</summary>
    [JsonPropertyName("cost")]   public string Cost { get; set; } = "?";
    [JsonPropertyName("afford")] public bool Afford { get; set; }
}

internal sealed class AugPlanner
{
    /// <summary>The server reports /aug off.</summary>
    [JsonPropertyName("off")]        public bool Off { get; set; }
    [JsonPropertyName("status")]     public string Status { get; set; } = "";
    [JsonPropertyName("lumPerCoin")] public long LumPerCoin { get; set; }
    [JsonPropertyName("rows")]       public AugRow[] Rows { get; set; } = Array.Empty<AugRow>();
    [JsonPropertyName("total")]      public string Total { get; set; } = "";
    /// <summary>Empty when the bank holds enough Enlightened Coins.</summary>
    [JsonPropertyName("short")]      public string Short { get; set; } = "";
    [JsonPropertyName("banked")]     public string Banked { get; set; } = "";
}

internal sealed class AugRow
{
    [JsonPropertyName("key")]   public string Key { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("cur")]   public int Current { get; set; }
    [JsonPropertyName("tgt")]   public int Target { get; set; }
    /// <summary>Highest target allowed (the aug's cap, or the current level when the server reports more).</summary>
    [JsonPropertyName("cap")]   public int Cap { get; set; }
    [JsonPropertyName("lum")]   public string Lum { get; set; } = "-";
    [JsonPropertyName("coins")] public long Coins { get; set; }
}

internal sealed class EnlPlanner
{
    /// <summary>The server reports /enl off.</summary>
    [JsonPropertyName("off")]           public bool Off { get; set; }
    [JsonPropertyName("header")]        public string Header { get; set; } = "";
    [JsonPropertyName("next")]          public string Next { get; set; } = "";
    [JsonPropertyName("enl")]           public int Level { get; set; }
    [JsonPropertyName("target")]        public int Target { get; set; }
    [JsonPropertyName("plan")]          public string[] Plan { get; set; } = Array.Empty<string>();
    [JsonPropertyName("coinsPerToken")] public int CoinsPerToken { get; set; }
    [JsonPropertyName("tokensShort")]   public string TokensShort { get; set; } = "";
    /// <summary>Client-side requirements not yet met; empty = the client checks pass.</summary>
    [JsonPropertyName("blockers")]      public string[] Blockers { get; set; } = Array.Empty<string>();
    [JsonPropertyName("serverChecks")]  public string ServerChecks { get; set; } = "";
    [JsonPropertyName("auto")]          public bool Auto { get; set; }
    /// <summary>Auto-enlighten is disarmed every session until confirmed again.</summary>
    [JsonPropertyName("armed")]         public bool Armed { get; set; }
    [JsonPropertyName("spendFirst")]    public bool SpendFirst { get; set; }
    [JsonPropertyName("checkEvery")]    public int CheckEvery { get; set; }
    [JsonPropertyName("status")]        public string Status { get; set; } = "";
}

[JsonSerializable(typeof(ProgressionInfo))]
internal sealed partial class ProgressionJsonContext : JsonSerializerContext { }

/// <summary>
/// RynthPluginGetProgressionJson (single static buffer: one caller only) while the Skills
/// panel is open, and right after each Progression edit (RequestRefreshAfterPluginTick).
/// </summary>
internal sealed unsafe class ProgressionSource : UiSource<ProgressionInfo>
{
    private delegate* unmanaged[Cdecl]<IntPtr> _getProgressionJson;

    public ProgressionSource() : base("Progression", periodMs: 1000) { }

    protected internal override void Poll()
    {
        if (_getProgressionJson == null)
            _getProgressionJson = (delegate* unmanaged[Cdecl]<IntPtr>)PluginExportBinder.Resolve("RynthAi", "RynthPluginGetProgressionJson");
        if (_getProgressionJson == null) return;
        IntPtr p = _getProgressionJson();
        string json = p == IntPtr.Zero ? "{}" : Marshal.PtrToStringAnsi(p) ?? "{}";
        ProgressionInfo info;
        try { info = JsonSerializer.Deserialize(json, ProgressionJsonContext.Default.ProgressionInfo) ?? new ProgressionInfo(); }
        catch (JsonException) { info = new ProgressionInfo(); }
        Publish(info);
    }

    protected internal override void Reset()
    {
        _getProgressionJson = null;
        ClearSnapshot();
    }
}
