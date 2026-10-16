// ============================================================================
//  RynthCore.Engine - ImGui/Panels/OracleSkillsEmbed.cs
//  Bridges RynthOracle's Skills / Attributes / Progress tabs to the engine
//  SkillsFace body so raise/train/progression stay in one place under Oracle.
//  Oracle sets ActiveTab while those tabs are selected; ScriptWindowFace for
//  the Oracle window draws the embed after the script tab strip.
// ============================================================================

using System.Threading;

namespace RynthCore.Engine.ImGuiBackend.Panels;

/// <summary>Which SkillsFace body Oracle wants drawn inside its window (-1 = none).</summary>
internal static class OracleSkillsEmbed
{
    /// <summary>0 = Skills, 1 = Attributes, 2 = Progress (ILT Progression). -1 = off.</summary>
    private static int _activeTab = -1;

    /// <summary>Oracle panel key prefix used by ScriptWindowRegistry ("script:…").</summary>
    public const string OraclePanelKeyFragment = "RynthOracle/Oracle";

    public static int ActiveTab => Volatile.Read(ref _activeTab);

    /// <summary>Called from the Oracle plugin (pump/render) when a Skills-family tab is shown.</summary>
    public static void SetActiveTab(int tab) => Volatile.Write(ref _activeTab, tab);

    /// <summary>Clears the embed when Oracle leaves Skills / Attributes / Progress.</summary>
    public static void Clear() => Volatile.Write(ref _activeTab, -1);

    public static bool IsOraclePanel(string panelKey) =>
        panelKey.Contains(OraclePanelKeyFragment, System.StringComparison.OrdinalIgnoreCase);
}
