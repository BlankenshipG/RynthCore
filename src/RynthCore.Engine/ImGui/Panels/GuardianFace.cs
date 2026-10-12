// ============================================================================
//  RynthCore.Engine - ImGui/Panels/GuardianFace.cs
//  Temple of Enlightenment guardians (riddle answers, Give, the phrasebook),
//  drawn by RynthAi through RynthPluginRenderEmbed ("guardian"). Part of the
//  ILT section: registered when RynthAi is installed, opened from the bar's ILT
//  button or "/rc guardian" on Infinite Leaftide only. ImGui-only.
//
//  The panel is user-sized: the plugin fills the body it is given. Until
//  RynthAi can draw it (not logged in, too old for the surface, or its embed
//  failed) the body says why.
// ============================================================================

using System;
using System.Numerics;
using ImGuiNET;
using RynthCore.Engine.Plugins;
using static RynthCore.Engine.ImGuiBackend.Panels.FaceKit;

namespace RynthCore.Engine.ImGuiBackend.Panels;

internal sealed class GuardianFace : IImGuiPanel
{
    public const string Title = "Guardian";

    public static void Register() => ImGuiPanelHost.Register(Title,
        new PanelSpec(new Vector2(540, 520), new Vector2(320, 220), EdgeToEdge: true, GripInBody: true),
        () => new GuardianFace());

    /// <summary>The RynthAi surface for this panel (NUL-terminated for the export).</summary>
    private static ReadOnlySpan<byte> Surface => "guardian\0"u8;

    // Its export failed this session: say so instead of calling it every frame.
    private bool _embedOff;

    public void OnShown() { }

    public void OnHidden() { }

    public void Draw()
    {
        float w = Begin(out Vector2 origin, out Vector2 size);
        float h = Math.Max(60f, Remaining(origin, size) - GripOverlap());
        Vector2 start = ImGuiNET.ImGui.GetCursorScreenPos();
        int result = PluginManager.EmbedFailed;
        if (!_embedOff)
        {
            ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6, 4));
            bool open = ImGuiNET.ImGui.BeginChild("##guardian_rynthai", new Vector2(w, h), ImGuiChildFlags.AlwaysUseWindowPadding,
                ImGuiWindowFlags.NoBackground);
            ImGuiNET.ImGui.PopStyleVar();
            try
            {
                if (open)
                {
                    // The plugin's widgets use the default UI font, as in its own windows.
                    ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.Ui11));
                    try { result = PluginManager.RenderEmbed("RynthAi", Surface, ImGuiNET.ImGui.GetContentRegionAvail(), out _); }
                    finally { ImGuiNET.ImGui.PopFont(); }
                }
                else result = PluginManager.EmbedDrawn;   // clipped: nothing to draw, nothing wrong
            }
            finally { ImGuiNET.ImGui.EndChild(); }
            if (result == PluginManager.EmbedFailed) _embedOff = true;
        }

        if (result != PluginManager.EmbedDrawn)
        {
            ImGuiNET.ImGui.SetCursorScreenPos(start + new Vector2(0, 4));
            Label(result switch
            {
                PluginManager.EmbedNoPlugin => "The Guardian tools come with RynthAi; it isn't loaded.",
                PluginManager.EmbedFailed => "RynthAi couldn't draw the Guardian tools (this build may be too old). See the log.",
                _ => "Waiting for RynthAi (log in; ILT features turn on once the server reports them).",
            }, Mute);
        }
        End(origin, size);
    }
}
