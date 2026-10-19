// ============================================================================
//  RynthCore.Engine - Plugins/PluginContractVersion.cs
//  The plugin API's version: the number of the last table (RynthCoreAPI) layout.
//
//  One file, compiled into three assemblies so they can't disagree:
//    - the engine (this folder), which hands it to plugins in RynthCoreAPI.Version;
//    - the SDK (RynthCore.PluginSdk), as RynthCoreHost.CurrentApiVersion;
//    - the launcher (RynthCore.App.Avalonia), as the API of the engine it installed,
//      to hold back feed plugins whose minEngineApi is newer.
//  tools\PluginManifestTests also checks the SDK's table covers this version.
// ============================================================================

namespace RynthCore.Engine.Plugins;

/// <summary>Current API version. Bump when adding fields to RynthCoreAPI (and the SDK's RynthCoreApiNative).</summary>
internal static class PluginContractVersion
{
    // v81: GetMergeStackResult (was v77 on QOL-items, then v80; renumbered after upstream's v77-v80).
    // v82: SalvageDirect (salvage without the salvage panel; was v81 on feature/salvage-direct).
    // v83: GetObjectServerAge and RemoveClientObject (ghost creatures, 2026-10-12).
    // v84: GetObjectProperties (every property of an object, for /ub propertydump; 2026-10-13).
    // v85: ArmConfirmation and GetLastConfirmation (answer the server's yes/no prompt, e.g. the
    //      craft chance, for a plugin; from SilentKelpie's build, 2026-10-15).
    // v86: GetServerStateJson (the server list, active server and owning plugins for every plugin;
    //      server plugins declare their server in their manifest; from SilentKelpie's build).
    // v87: GetWieldedItems (what the player wields and where, from the server's wield / contain /
    //      drop messages: EquipmentTracker; the client's own fields go stale, 2026-10-18).
    // v88: managed plugins find their common dependencies in Runtime\PluginShared (installed with
    //      RynthCore), so the update feed ships each managed plugin as one DLL; releases build the
    //      plugins managed (collectible: a reload frees them) and they declare minEngineApi 88.
    public const uint Current = 88;
}
