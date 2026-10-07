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
    // v80: GetMergeStackResult (was v77 on QOL-items, renumbered after upstream's v77-v79).
    public const uint Current = 80;
}
