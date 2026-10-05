// ============================================================================
//  RynthCore.Engine - UI/Data/CharmsData.cs
//  RynthAi's ACECustom charm tracker for the Settings panel's "Charms
//  Tracking" tab. RynthAi owns the charm registry (WCIDs, ability flags) and
//  the per-character "seen" memory; this side only shows the snapshot.
// ============================================================================

using System;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using RynthCore.Engine.Plugins;

namespace RynthCore.Engine.UI.Data;

/// <summary>RynthPluginGetCharmsJson's payload.</summary>
internal sealed class CharmsInfo
{
    /// <summary>False off ILT-like worlds (or before RynthAi's ILT Hub exists).</summary>
    [JsonPropertyName("available")]     public bool Available { get; set; }
    [JsonPropertyName("world")]         public string World { get; set; } = "";
    [JsonPropertyName("total")]         public int Total { get; set; }
    [JsonPropertyName("acquiredCount")] public int AcquiredCount { get; set; }
    [JsonPropertyName("activeCount")]   public int ActiveCount { get; set; }
    [JsonPropertyName("charms")]        public CharmRow[] Charms { get; set; } = Array.Empty<CharmRow>();
}

internal sealed class CharmRow
{
    [JsonPropertyName("name")]     public string Name { get; set; } = "";
    [JsonPropertyName("effect")]   public string Effect { get; set; } = "";
    /// <summary>"carried" (in pack / equipped now), "seen" (carried before, now elsewhere) or "no".</summary>
    [JsonPropertyName("acquired")] public string Acquired { get; set; } = "no";
    /// <summary>Copies carried right now.</summary>
    [JsonPropertyName("count")]    public int Count { get; set; }
    /// <summary>Best tier carried or last seen; 0 until an appraisal reports it.</summary>
    [JsonPropertyName("tier")]     public int Tier { get; set; }
    [JsonPropertyName("maxTier")]  public int MaxTier { get; set; }
    /// <summary>Ability toggled on: "on", "off" or "unknown".</summary>
    [JsonPropertyName("active")]   public string Active { get; set; } = "unknown";
    /// <summary>Server-side global switch as far as the client has learned it: "on", "off" or "unknown".</summary>
    [JsonPropertyName("server")]   public string Server { get; set; } = "unknown";
    /// <summary>yyyy-MM-dd the charm was last carried (empty when never).</summary>
    [JsonPropertyName("lastSeen")] public string LastSeen { get; set; } = "";
}

[JsonSerializable(typeof(CharmsInfo))]
internal sealed partial class CharmsJsonContext : JsonSerializerContext { }

/// <summary>
/// RynthPluginGetCharmsJson (single static buffer: one caller only) while the Settings panel
/// is open. Every 2 s is plenty: charm state only changes when one is picked up or toggled.
/// </summary>
internal sealed unsafe class CharmsSource : UiSource<CharmsInfo>
{
    private delegate* unmanaged[Cdecl]<IntPtr> _getCharmsJson;

    public CharmsSource() : base("Charms", periodMs: 2000) { }

    protected internal override void Poll()
    {
        if (_getCharmsJson == null)
            _getCharmsJson = (delegate* unmanaged[Cdecl]<IntPtr>)PluginExportBinder.Resolve("RynthAi", "RynthPluginGetCharmsJson");
        if (_getCharmsJson == null) return;
        IntPtr p = _getCharmsJson();
        string json = p == IntPtr.Zero ? "{}" : Marshal.PtrToStringAnsi(p) ?? "{}";
        CharmsInfo info;
        try { info = JsonSerializer.Deserialize(json, CharmsJsonContext.Default.CharmsInfo) ?? new CharmsInfo(); }
        catch (JsonException) { info = new CharmsInfo(); }
        Publish(info);
    }

    protected internal override void Reset()
    {
        _getCharmsJson = null;
        ClearSnapshot();
    }
}
