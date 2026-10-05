// ============================================================================
//  RynthCore.Engine - Plugins/LoadedPlugin.cs
//  Represents a single plugin DLL that has been loaded and had its exports
//  resolved. Holds the module handle and cached delegates.
// ============================================================================

using System;

namespace RynthCore.Engine.Plugins;

internal sealed class LoadedPlugin
{
    public string SourceFilePath { get; init; } = "";
    public string FilePath { get; init; } = "";
    public string FileName { get; init; } = "";
    public IntPtr ModuleHandle { get; init; }

    // ─── Required exports ────────────────────────────────────────────
    public PluginInitDelegate? Init { get; set; }
    public PluginShutdownDelegate? Shutdown { get; set; }

    // ─── Optional exports ────────────────────────────────────────────
    public PluginNameDelegate? GetName { get; set; }
    public PluginVersionDelegate? GetVersion { get; set; }
    public PluginOnLoginCompleteDelegate? OnLoginComplete { get; set; }
    public PluginOnLogoutDelegate? OnLogout { get; set; }
    public PluginOnUIInitializedDelegate? OnUIInitialized { get; set; }
    public PluginOnBusyCountIncrementedDelegate? OnBusyCountIncremented { get; set; }
    public PluginOnBusyCountDecrementedDelegate? OnBusyCountDecremented { get; set; }
    public PluginOnSelectedTargetChangeDelegate? OnSelectedTargetChange { get; set; }
    public PluginOnCombatModeChangeDelegate? OnCombatModeChange { get; set; }
    public IntPtr OnSmartBoxEventPtr { get; set; }
    public PluginOnSmartBoxEventDelegate? OnSmartBoxEvent { get; set; }
    public PluginOnDeleteObjectDelegate? OnDeleteObject { get; set; }
    public PluginOnCreateObjectDelegate? OnCreateObject { get; set; }
    public IntPtr OnUpdateObjectPtr { get; set; }
    public PluginOnUpdateObjectDelegate? OnUpdateObject { get; set; }
    public PluginOnUpdateObjectInventoryDelegate? OnUpdateObjectInventory { get; set; }
    public PluginOnViewObjectContentsDelegate? OnViewObjectContents { get; set; }
    public PluginOnStopViewingObjectContentsDelegate? OnStopViewingObjectContents { get; set; }
    public PluginOnVendorOpenDelegate? OnVendorOpen { get; set; }
    public PluginOnVendorCloseDelegate? OnVendorClose { get; set; }
    public IntPtr OnUpdateHealthPtr { get; set; }
    public PluginOnUpdateHealthDelegate? OnUpdateHealth { get; set; }
    public IntPtr OnCombatDamagePtr { get; set; }
    public PluginOnCombatDamageDelegate? OnCombatDamage { get; set; }
    public IntPtr OnKillNotificationPtr { get; set; }
    public PluginOnKillNotificationDelegate? OnKillNotification { get; set; }
    public IntPtr OnChatWindowTextPtr { get; set; }
    public PluginOnChatWindowTextDelegate? OnChatWindowText { get; set; }
    public PluginOnChatBarEnterDelegate? OnChatBarEnter { get; set; }
    public PluginBarActionDelegate? OnBarAction { get; set; }
    public PluginOnEnchantmentAddedDelegate? OnEnchantmentAdded { get; set; }
    public PluginOnEnchantmentRemovedDelegate? OnEnchantmentRemoved { get; set; }
    public PluginTickDelegate? Tick { get; set; }
    public PluginRenderDelegate? Render { get; set; }

    // ─── v77 UI exports (resolved lazily by PluginManager.ResolveUiExports) ───
    public bool UiExportsResolved { get; set; }
    public IntPtr OnScreenChangedPtr { get; set; }
    public IntPtr OnClientCleanupPtr { get; set; }
    public IntPtr OnTooltipShowPtr { get; set; }
    public IntPtr OnTooltipHidePtr { get; set; }
    public IntPtr OnDragStartPtr { get; set; }
    public IntPtr OnItemDroppedPtr { get; set; }

    // ─── v78 server messages (resolved when the plugin calls SetServerMessageInterest) ──
    /// <summary><c>void RynthPluginOnServerMessage(uint opcode, byte* data, int length)</c>, Cdecl.</summary>
    public IntPtr OnServerMessagePtr { get; set; }
    /// <summary>What the plugin asked for; null or empty = nothing.</summary>
    public Net.ServerMessageInterest? ServerMessageInterest { get; set; }

    // ─── Runtime state ───────────────────────────────────────────────
    public bool Initialized { get; set; }
    public bool LoginCompleteDispatched { get; set; }
    public bool UIInitializedDispatched { get; set; }
    public bool Failed { get; set; }
    public string DisplayName { get; set; } = "";
    public string VersionString { get; set; } = "";

    /// <summary>
    /// The manifest embedded in the DLL (read before loading it; see PluginManifestGate), or null
    /// for a plugin without one. Set by PluginManager after PluginLoader loads the plugin.
    /// </summary>
    public RynthCore.PluginSdk.Manifest.RynthPluginManifest? Manifest { get; set; }
    /// <summary>Why the plugin was not started (a required plugin missing or failed); empty otherwise.</summary>
    public string NotStartedReason { get; set; } = "";
}
