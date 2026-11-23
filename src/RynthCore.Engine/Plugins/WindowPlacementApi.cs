// ============================================================================
//  RynthCore.Engine - Plugins/WindowPlacementApi.cs
//  Plugin API v98 GetWindowPlacement / SetWindowPlacement: a plugin's own floating
//  ImGui windows keep their spot per character (UI/WindowPlacementStore).
// ============================================================================

using System;
using System.Runtime.InteropServices;
using RynthCore.Engine.UI;

namespace RynthCore.Engine.Plugins;

internal static unsafe class WindowPlacementApi
{
    /// <summary>1 found (xywh filled), 0 none saved, -1 bad key or no character yet.</summary>
    public static int Get(byte* key, float* xywh)
    {
        try
        {
            if (key == null || xywh == null) return -1;
            string? k = Marshal.PtrToStringUTF8((IntPtr)key);
            if (!WindowPlacementStore.ValidKey(k)) return -1;
            if (WindowPlacementStore.FileNameFor(Compatibility.SessionStateRegistry.LastServerName,
                    Compatibility.SessionStateRegistry.LastCharacterName) == null) return -1;
            if (!WindowPlacementStore.TryGet(k!, out float x, out float y, out float w, out float h)) return 0;
            xywh[0] = x; xywh[1] = y; xywh[2] = w; xywh[3] = h;
            return 1;
        }
        catch { return -1; }
    }

    /// <summary>1 saved, 0 no character yet, -1 bad key or numbers.</summary>
    public static int Set(byte* key, float x, float y, float w, float h)
    {
        try
        {
            if (key == null) return -1;
            string? k = Marshal.PtrToStringUTF8((IntPtr)key);
            if (!WindowPlacementStore.ValidKey(k) || !float.IsFinite(x) || !float.IsFinite(y)
                || !float.IsFinite(w) || !float.IsFinite(h)) return -1;
            return WindowPlacementStore.Set(k!, x, y, w, h) ? 1 : 0;
        }
        catch { return -1; }
    }
}
