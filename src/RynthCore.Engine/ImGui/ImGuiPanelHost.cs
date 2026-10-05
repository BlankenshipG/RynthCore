// ============================================================================
//  RynthCore.Engine - ImGui/ImGuiPanelHost.cs
//  Engine-side ImGui panel faces (docs/IMGUI_PARITY_PLAN.md §4.1).
//
//  A panel docked in the client is drawn here. Popped out, the same face
//  is drawn into its own window by ImGuiPopOuts (Tom, 2026-09-29: pop-outs are
//  ImGui too; Avalonia is being retired). The host owns each face's window: the
//  Avalonia-matching frame (24 px header with DRAG, title, text size, pop-out
//  and close; 16 px resize grip, all Phosphor icons), placement restore/persistence shared
//  with the Avalonia face through PanelStateStore, and per-panel error isolation.
//
//  Threads:
//    Register        engine init.
//    RegisterDynamic / Unregister / RequestSize / RequestPos
//                    any thread (script windows, from the plugin pump thread);
//                    DrawOrder is copy-on-write, removal finishes in DrawAll;
//                    requests are applied in DrawPanel.
//    Open / Close    any thread (router: game thread / Avalonia UI thread).
//                    Applied on AC's render thread at the next DrawAll.
//    DrawAll         AC's render thread, inside the ImGui frame.
//    IsOpen/AnyOpen  any thread; a copy-on-write set, so frame reads never lock.
//  Panel Draw code runs on AC's thread: it reads hub snapshots and posts
//  commands only (no plugin calls, no AC reads, no locks, no file I/O).
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using RynthCore.Engine.UI;

namespace RynthCore.Engine.ImGuiBackend;

internal enum PanelChrome
{
    /// <summary>DRAG + title + pop-out + close header, resize grip.</summary>
    Standard,
    /// <summary>No header or grip: the panel draws its own drag surface (RynthAi, Radar).</summary>
    None,
}

/// <summary>How a face's window looks and sizes, in Avalonia logical units.</summary>
internal sealed record PanelSpec(
    Vector2 DefaultSize,
    Vector2 MinSize,
    PanelChrome Chrome = PanelChrome.Standard,
    uint? Background = null,     // null: panel glass; 0: transparent (the panel paints its own)
    uint? BorderColor = null,    // null: teal
    float Rounding = RynthTheme.PanelRounding,
    // A face is registered only once it's finished, so finished panels open in
    // ImGui unless the player picked another face (/rc ui). Popped out is Avalonia.
    PanelFace CodeDefault = PanelFace.ImGui,
    bool EdgeToEdge = false,     // body has no padding and never scrolls (Radar)
    bool GoldGrip = false,       // the Radar's 14 px gold grip, inset 4 px
    bool NoPopOut = false,       // no pop-out button
    // Standard chrome without the 18 px strip under the body for the grip: the
    // grip sits over the body's bottom-right corner and the face keeps that
    // corner free (ImGuiPanelHost.BodyGripReserve wide) - Chat puts it on its
    // input row, beside Tell; Tracker, Status, Log, Monsters, Meta, Lua,
    // ChatFilters and RynthNav keep their bottom rows short of it. A body that
    // scrolls ends above the grip instead (the reserve is then 0).
    bool GripInBody = false,
    // OR'd into the body child's window flags (script windows: NoScrollbar,
    // NoScrollWithMouse). 0 = no change.
    ImGuiWindowFlags BodyFlags = ImGuiWindowFlags.None,
    // Where the panel first opens (logical units) when nothing is saved for its
    // key; null = the cascade. (Script windows: a FirstUseEver position.)
    Vector2? DefaultPos = null,
    // With nothing saved and no DefaultPos: open centred in the game view rather
    // than in the top-left cascade, where popped-out panels (separate windows
    // above the game frame) usually sit and would hide it.
    bool OpenCentered = false);

/// <summary>An ImGui panel face. One instance per open; dropped on close.</summary>
internal interface IImGuiPanel
{
    /// <summary>Opened: subscribe to hub sources. AC thread.</summary>
    void OnShown() { }

    /// <summary>Closed: unsubscribe. AC thread.</summary>
    void OnHidden() { }

    /// <summary>Draws the body (below the header). AC thread, inside the panel's child region.</summary>
    void Draw();

    /// <summary>Minimum size (logical units) when it depends on the panel's state; null = the spec's.</summary>
    Vector2? MinSize => null;

    /// <summary>
    /// True while the panel should let clicks through to the game (e.g. the
    /// radar's "hold Ctrl to interact" option): the window takes no input.
    /// </summary>
    bool ClickThrough => false;

    /// <summary>
    /// Popped out and not hovered or in use: how often the face is redrawn (Hz).
    /// Its pictures are only pushed when they change; this bounds the cost of
    /// building the frame. Hovered or just used, a pop-out runs at 30 Hz.
    /// </summary>
    int PopOutIdleHz => 10;

    /// <summary>
    /// True while the face has nothing to show docked (the Dungeon Map outdoors): it
    /// stays open and subscribed, but the host draws no window for it until this is
    /// false again. Popped out it is drawn as usual. AC thread; keep it cheap.
    /// </summary>
    bool HiddenWhileDocked => false;
}

internal static class ImGuiPanelHost
{
    private sealed class Entry
    {
        /// <summary>The key: PanelStateStore row, router and command name.</summary>
        public required string Title;
        /// <summary>What the header shows (the key for engine panels; a script window's own title).</summary>
        public required string DisplayTitle;
        /// <summary>Registered at run time (script windows, RegisterDynamic); never restored by RestoreOnce.</summary>
        public bool Dynamic;
        /// <summary>Unregistered: removed once it has closed (AC thread, DrawAll).</summary>
        public bool PendingRemoval;
        public required PanelSpec Spec;
        public required Func<IImGuiPanel> Factory;
        public required string WindowId;       // built once: no per-frame string work
        public required string BothTitle;
        public IImGuiPanel? Instance;
        public bool Shown;
        public bool Popped;        // drawn in its own window (ImGuiPopOuts), not in the client
        public bool BodyScrolls;   // last frame the body had a vertical scrollbar
        public float LastGripRoom; // body height kept free for the grip (held during a resize)
        public bool NeedsPlacement;
        public Vector2 Pos, Size;              // client pixels, as last drawn
        public Vector2 PersistedPos, PersistedSize;
        // The saved spot when the client was too small for it at placement (AC's 800x600
        // character select): re-applied as the display grows, dropped once it fits or the
        // player moves the panel. Without this a panel saved near the right or bottom edge
        // came back pulled inwards on every restart.
        public Vector2? DesiredPos;
        public Vector2 DesiredForDisplay;
        public Vector2? RequestedSize;        // logical units, applied next frame (under Sync)
        public Vector2? RequestedPos;         // logical units, applied next frame (under Sync)
        public volatile bool HasRequest;      // RequestedSize or RequestedPos is set
        public int Failures;
        public string? Error;
    }

    private static readonly object Sync = new();
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);
    // Copy-on-write under Sync: DrawAll iterates the list it read without holding the lock,
    // and dynamic entries are added and removed from the plugin pump thread.
    private static List<Entry> DrawOrder = new();
    private static volatile bool _removalPending;
    private enum RequestKind { Open, Close, PopOut, Redock, ToAvaloniaDocked }

    private static readonly ConcurrentQueue<(string Title, RequestKind Kind)> Requests = new();
    private static HashSet<string> _open = new(StringComparer.OrdinalIgnoreCase);
    private static bool _restored;
    private const int MaxFailures = 3;

    // ── Registry ───────────────────────────────────────────────────────

    /// <summary>
    /// Declares an ImGui face for <paramref name="title"/> (the same title the
    /// Avalonia face registers with OverlayHost). Engine init.
    /// </summary>
    public static void Register(string title, PanelSpec spec, Func<IImGuiPanel> factory)
    {
        lock (Sync)
        {
            if (Entries.ContainsKey(title)) return;
            var entry = new Entry
            {
                Title = title, DisplayTitle = title, Spec = spec, Factory = factory,
                WindowId = title + "##rc_panel", BothTitle = title + "  · ImGui",
            };
            Entries[title] = entry;
            DrawOrder = new List<Entry>(DrawOrder) { entry };
        }
    }

    /// <summary>
    /// Declares (or updates) a face registered at run time: a script window
    /// (UI/ScriptWindows). <paramref name="key"/> is the panel key (saved
    /// placement, commands); <paramref name="displayTitle"/> is what the header
    /// shows and may change on every call. An entry being removed is revived.
    /// Opening and closing stay separate (Open / Close). Any thread.
    /// </summary>
    public static void RegisterDynamic(string key, string displayTitle, PanelSpec spec, Func<IImGuiPanel> factory)
    {
        lock (Sync)
        {
            if (Entries.TryGetValue(key, out Entry? existing))
            {
                if (!existing.Dynamic) return;     // never replaces an engine panel
                existing.DisplayTitle = displayTitle;
                existing.Spec = spec;
                existing.Factory = factory;
                existing.PendingRemoval = false;
                return;
            }
            var entry = new Entry
            {
                Title = key, DisplayTitle = displayTitle, Spec = spec, Factory = factory, Dynamic = true,
                WindowId = key + "##rc_panel", BothTitle = displayTitle,
            };
            Entries[key] = entry;
            DrawOrder = new List<Entry>(DrawOrder) { entry };
        }
    }

    /// <summary>Changes a dynamic face's header text. Any thread.</summary>
    public static void SetDisplayTitle(string key, string displayTitle)
    {
        lock (Sync)
            if (Entries.TryGetValue(key, out Entry? e) && e.Dynamic) e.DisplayTitle = displayTitle;
    }

    /// <summary>
    /// Removes a face added with <see cref="RegisterDynamic"/>: it closes (its
    /// placement is saved as usual) and the entry goes once it has closed. Any thread.
    /// </summary>
    public static void Unregister(string key)
    {
        lock (Sync)
        {
            if (!Entries.TryGetValue(key, out Entry? e) || !e.Dynamic) return;
            e.PendingRemoval = true;
        }
        Close(key);
        _removalPending = true;
    }

    /// <summary>Drops unregistered entries that have closed. AC thread, after the requests are applied.</summary>
    private static void RemoveUnregistered()
    {
        if (!_removalPending) return;
        _removalPending = false;
        lock (Sync)
        {
            List<Entry>? next = null;
            foreach (Entry e in DrawOrder)
            {
                if (e.PendingRemoval && !e.Shown && !IsOpen(e.Title))
                {
                    next ??= new List<Entry>(DrawOrder);
                    next.Remove(e);
                    Entries.Remove(e.Title);
                }
                else if (e.PendingRemoval)
                {
                    _removalPending = true;   // still closing: look again next frame
                }
            }
            if (next != null) DrawOrder = next;
        }
    }

    public static bool HasFace(string title)
    {
        lock (Sync) return Entries.ContainsKey(title);
    }

    public static PanelFace CodeDefault(string title)
    {
        lock (Sync) return Entries.TryGetValue(title, out Entry? e) ? e.Spec.CodeDefault : PanelFace.Avalonia;
    }

    // ── Open state (any thread) ────────────────────────────────────────

    public static bool IsOpen(string title) => System.Threading.Volatile.Read(ref _open).Contains(title);

    /// <summary>True when any ImGui panel face is open.</summary>
    public static bool AnyOpen => System.Threading.Volatile.Read(ref _open).Count > 0;

    /// <summary>
    /// True while the host needs ImGui frames: a face is open, one is still
    /// shown, or a request is queued. Requests (close, pop out, hand off) are
    /// applied inside DrawAll, so frames must keep running until the last one
    /// is processed - otherwise closing or popping out the last open face left
    /// it unapplied (nothing saved, Avalonia never opened, still subscribed).
    /// </summary>
    public static bool NeedsFrame => AnyOpen || !Requests.IsEmpty || System.Threading.Volatile.Read(ref _shownCount) > 0;

    private static int _shownCount;

    public static void Open(string title) => Request(title, RequestKind.Open);

    public static void Close(string title) => Request(title, RequestKind.Close);

    /// <summary>Moves an open panel out of the client into its own window (ImGuiPopOuts).</summary>
    public static void PopOut(string title) => Request(title, RequestKind.PopOut);

    /// <summary>Moves a popped-out panel back into the client, where it was docked.</summary>
    public static void Redock(string title) => Request(title, RequestKind.Redock);

    /// <summary>The face's idle redraw rate while popped out (IImGuiPanel.PopOutIdleHz). AC thread.</summary>
    public static int PopOutIdleHz(string title)
    {
        Entry? entry;
        lock (Sync) Entries.TryGetValue(title, out entry);
        try { return Math.Clamp(entry?.Instance?.PopOutIdleHz ?? 10, 1, 30); } catch { return 10; }
    }

    /// <summary>For a face's own pop-out button: dock back (arrow in) while it is drawn in its own window, else pop out.</summary>
    public static string PopOutGlyph => ImGuiPopOuts.InPopOutFrame ? PhosphorIcons.ArrowSquareIn : PhosphorIcons.ArrowSquareOut;

    /// <summary>The pop-out button's tooltip, matching <see cref="PopOutGlyph"/>.</summary>
    public static string PopOutTooltip => ImGuiPopOuts.InPopOutFrame
        ? "Dock back into the game window" : "Pop out into a floating window";

    /// <summary>A face's own pop-out button: pops out, or docks back while popped out. AC thread, in Draw.</summary>
    public static void TogglePopOut(string title)
    {
        if (ImGuiPopOuts.InPopOutFrame) Redock(title);
        else PopOut(title);
    }

    /// <summary>True when the panel is open in its own window. Any thread (racy read).</summary>
    public static bool IsPopped(string title)
    {
        Entry? entry;
        lock (Sync) Entries.TryGetValue(title, out entry);
        return entry != null && entry.Popped;
    }

    /// <summary>
    /// The docked face switched from ImGui to Avalonia while open: close the
    /// ImGui face, save "docked, open" at its placement, then open the Avalonia
    /// face there. One ordered step, like PopOut.
    /// </summary>
    public static void HandOffToAvalonia(string title) => Request(title, RequestKind.ToAvaloniaDocked);

    private static void Request(string title, RequestKind kind)
    {
        lock (Sync)
        {
            if (!Entries.ContainsKey(title)) return;
            if (kind is RequestKind.PopOut or RequestKind.Redock)
            {
                // Moves an open panel between the client and its own window.
                if (!_open.Contains(title)) return;
            }
            else
            {
                bool open = kind == RequestKind.Open;
                var next = new HashSet<string>(_open, StringComparer.OrdinalIgnoreCase);
                if (!(open ? next.Add(title) : next.Remove(title))) return;
                System.Threading.Volatile.Write(ref _open, next);
            }
        }
        Requests.Enqueue((title, kind));
    }

    /// <summary>One line per face that is requested open or shown. Diagnostics; any thread (racy reads).</summary>
    public static List<string> DescribeFaces()
    {
        var lines = new List<string>();
        List<Entry> order;
        lock (Sync) order = DrawOrder;
        foreach (Entry e in order)
        {
            if (!e.Shown && !IsOpen(e.Title)) continue;
            lines.Add($"face {e.Title}: requested={IsOpen(e.Title)} shown={e.Shown} pos=({e.Pos.X:0},{e.Pos.Y:0}) size=({e.Size.X:0}x{e.Size.Y:0}) failures={e.Failures}{(e.Error != null ? " error=" + e.Error : "")}");
        }
        if (lines.Count == 0) lines.Add("faces: none open");
        return lines;
    }

    // ── Helpers for panel faces (AC thread, inside the panel's Draw) ─────

    /// <summary>
    /// Makes the last item a drag handle for its panel window (chromeless
    /// panels drag from their own title row, like the Avalonia face's
    /// AttachDragHandle). Clamped to the client area.
    /// </summary>
    public static void DragWindowWithLastItem()
    {
        if (ImGuiPopOuts.InPopOutFrame)
        {
            // Popped out: the OS window moves (it follows the cursor from here on).
            if (ImGuiNET.ImGui.IsItemActive() && ImGuiNET.ImGui.IsMouseDragging(ImGuiMouseButton.Left, 2f))
                ImGuiPopOuts.BeginMoveCurrent();
            return;
        }
        if (!ImGuiNET.ImGui.IsItemActive() || !ImGuiNET.ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0f)) return;
        // The panel body is a child window; move its parent (the panel window).
        _pendingDragDelta += ImGuiNET.ImGui.GetIO().MouseDelta;
    }

    /// <summary>Asks for a new window size (logical units), applied on the next frame it draws. Any thread.</summary>
    public static void RequestSize(string title, Vector2 logicalSize)
    {
        lock (Sync)
        {
            if (!Entries.TryGetValue(title, out Entry? entry)) return;
            entry.RequestedSize = logicalSize;
            entry.HasRequest = true;
        }
    }

    /// <summary>
    /// Asks for a new window position (logical units), applied on the next frame it
    /// draws docked, clamped to the client area. It is not saved as a move by the
    /// player. A request queued with an Open survives the open's placement. Any thread.
    /// </summary>
    public static void RequestPos(string title, Vector2 logicalPos)
    {
        lock (Sync)
        {
            if (!Entries.TryGetValue(title, out Entry? entry)) return;
            entry.RequestedPos = logicalPos;
            entry.HasRequest = true;
        }
    }

    /// <summary>
    /// True while an Open or Close for <paramref name="title"/> is queued but not yet
    /// applied on AC's thread (the requested state differs from what is drawn). Any thread (racy).
    /// </summary>
    public static bool IsTransitioning(string title)
    {
        Entry? entry;
        lock (Sync) Entries.TryGetValue(title, out entry);
        return entry != null && entry.Shown != IsOpen(title);
    }

    private static Vector2 _pendingDragDelta;

    // Chromeless-panel corner resize in progress: which panel, and where it began.
    private static string? _resizeTitle;
    private static Vector2 _resizeStartSize, _resizeStartMouse;

    /// <summary>
    /// While a GripInBody face draws: the width (px) at the body's right edge,
    /// across the bottom <see cref="BodyGripReserve"/> px, that the grip covers
    /// and the face must leave free. 0 for every other face. AC thread.
    /// </summary>
    public static float BodyGripReserve { get; private set; }

    // ── Frame (AC thread) ──────────────────────────────────────────────

    /// <summary>
    /// Draws every open face. <paramref name="uiScale"/> is the scale fonts
    /// were built for; placement uses the Avalonia overlay's scale so a panel
    /// lands where its Avalonia face would.
    /// </summary>
    public static void DrawAll(float uiScale)
    {
        float placeScale = PlacementScale();
        while (Requests.TryDequeue(out var req))
            Apply(req.Title, req.Kind, placeScale);
        RemoveUnregistered();

        List<Entry> order;
        lock (Sync) order = DrawOrder;
        foreach (Entry entry in order)
        {
            if (!entry.Shown || entry.Popped) continue;
            if (HiddenWhileDocked(entry))
            {
                if (_resizeTitle == entry.Title) _resizeTitle = null;   // hid mid-resize: free the grip
                continue;
            }
            DrawPanel(entry, uiScale, placeScale, null);
        }
    }

    private static bool HiddenWhileDocked(Entry entry)
    {
        if (entry.Error != null) return false;   // an error line is always shown
        try { return entry.Instance?.HiddenWhileDocked ?? false; }
        catch { return false; }
    }

    /// <summary>
    /// Draws a popped-out panel filling its own window (<paramref name="display"/>
    /// px). AC thread, called by ImGuiPopOuts with the pop-out's context current.
    /// </summary>
    public static void DrawPopped(string title, float uiScale, Vector2 display)
    {
        Entry? entry;
        lock (Sync) Entries.TryGetValue(title, out entry);
        if (entry == null || !entry.Shown || !entry.Popped) return;
        DrawPanel(entry, uiScale, PlacementScale(), display);
    }

    /// <summary>A pop-out window was moved or resized by the user: save it. Game thread.</summary>
    public static void OnPopOutGeometry(string title, int left, int top, int width, int height)
    {
        Entry? entry;
        lock (Sync) Entries.TryGetValue(title, out entry);
        if (entry == null || !entry.Popped) return;
        PersistPopped(entry, open: true, left, top, width, height, PlacementScale());
    }

    /// <summary>
    /// Once per engine load, in the world: docked panels that were open and
    /// whose docked face is ImGui (or Both) reopen here. (The Avalonia overlay
    /// skips those in its own restore.) AC thread.
    /// </summary>
    public static void RestoreOnce()
    {
        if (_restored) return;
        _restored = true;
        List<string> titles = new();
        // Script windows (dynamic) open when their owner says so, not from the saved state.
        lock (Sync)
            foreach (Entry e in Entries.Values)
                if (!e.Dynamic) titles.Add(e.Title);
        foreach (string title in titles)
        {
            if (!PanelStateStore.TryGetPanel(title, out PanelStateStore.PanelEntry saved)) continue;
            if (!saved.Open) continue;
            PanelFace face = PanelRouter.ResolveDockedFace(title);
            if (face is not (PanelFace.ImGui or PanelFace.Both)) continue;
            // Saved popped out: it reopens popped out (Apply reads the saved entry).
            Open(title);
        }
    }

    /// <summary>Client pixels per logical unit (the unit PanelSpec sizes, MinSize and RequestSize use).</summary>
    internal static float PlacementScale()
    {
        // Saved positions are in Avalonia logical units. Without the Avalonia overlay
        // (off, or the CoreCLR engine) the ImGui DPI scale is the same conversion.
        float s = AvaloniaOverlay.IsRunning ? AvaloniaOverlay.InputScale : EngineFrameController.FontScale;
        return s > 0f ? s : 1f;
    }

    private static void Apply(string title, RequestKind kind, float placeScale)
    {
        Entry? entry;
        lock (Sync) Entries.TryGetValue(title, out entry);
        if (entry == null) return;

        if (kind == RequestKind.PopOut)
        {
            if (entry.Shown && !entry.Popped) StartPopOut(entry, placeScale, fromSaved: false);
            return;
        }
        if (kind == RequestKind.Redock)
        {
            if (!entry.Shown || !entry.Popped) return;
            ImGuiPopOuts.Close(title);
            entry.Popped = false;
            entry.NeedsPlacement = true;   // back where it was docked
            Persist(entry, open: true, placeScale);
            return;
        }

        bool open = kind == RequestKind.Open;
        if (entry.Shown == open) return;

        if (open)
        {
            PlaceFromSavedState(entry, placeScale);
            entry.Failures = 0;
            entry.Error = null;
            try
            {
                entry.Instance = entry.Factory();
                entry.Instance.OnShown();
            }
            catch (Exception ex)
            {
                entry.Error = $"{title} failed to open: {ex.Message}";
                RynthLog.UI($"ImGuiPanelHost: {title} factory/OnShown threw {ex.GetType().Name}: {ex.Message}");
            }
            entry.Shown = true;
            System.Threading.Interlocked.Increment(ref _shownCount);
            // One panel on screen once: close any Avalonia copy of it (script windows have none).
            if (!entry.Dynamic) AvaloniaOverlay.CloseAvaloniaCopy(title);
            // Last left popped out: it opens popped out again, where it was.
            if (PanelRouter.CanPopOut && !entry.Spec.NoPopOut
                && PanelStateStore.TryGetPanel(title, out PanelStateStore.PanelEntry saved) && saved.Floating)
                StartPopOut(entry, placeScale, fromSaved: true);
            else
                Persist(entry, open: true, placeScale); // reopen here after a restart
        }
        else
        {
            bool wasPopped = entry.Popped;
            if (wasPopped)
            {
                ImGuiPopOuts.Close(title);
                entry.Popped = false;
            }
            try { entry.Instance?.OnHidden(); }
            catch (Exception ex) { RynthLog.UI($"ImGuiPanelHost: {title}.OnHidden threw {ex.GetType().Name}: {ex.Message}"); }
            entry.Instance = null;
            entry.Shown = false;
            System.Threading.Interlocked.Decrement(ref _shownCount);
            if (kind is RequestKind.PopOut or RequestKind.ToAvaloniaDocked)
            {
                // Save the placement as open (popped out or docked), then let the
                // Avalonia face open: its toggle reads that entry, so a popped-out
                // one opens at its saved screen position.
                Persist(entry, open: true, placeScale, floating: kind == RequestKind.PopOut);
                UiBackgroundWriter.Enqueue($"hand off {title}", () => AvaloniaOverlay.ActivateBarButton(title));
            }
            else if (wasPopped)
            {
                // Closed while popped out: it reopens popped out, where it was.
                PanelStateStore.TryGetPanel(title, out PanelStateStore.PanelEntry prior);
                PersistPopped(entry, open: false, (int)prior.FloatingLeft, (int)prior.FloatingTop,
                    (int)(entry.Size.X), (int)(entry.Size.Y), placeScale, keepSize: true);
            }
            else
            {
                Persist(entry, open: false, placeScale);
            }
        }
    }

    /// <summary>
    /// Opens <paramref name="entry"/>'s own window: at its saved popped-out
    /// position, or where it sits in the client. AC thread, main context current.
    /// </summary>
    private static void StartPopOut(Entry entry, float placeScale, bool fromSaved)
    {
        float s = UiScaleForChrome();
        Vector2 size = entry.Size;
        Vector2 origin = ImGuiPopOuts.ClientOriginOnScreen();
        int left = (int)(origin.X + entry.Pos.X), top = (int)(origin.Y + entry.Pos.Y);
        if (PanelStateStore.TryGetPanel(entry.Title, out PanelStateStore.PanelEntry saved)
            && (saved.FloatingLeft != 0 || saved.FloatingTop != 0) && (fromSaved || saved.Floating))
        {
            left = (int)Math.Round(saved.FloatingLeft);
            top = (int)Math.Round(saved.FloatingTop);
        }
        bool standard = entry.Spec.Chrome == PanelChrome.Standard;
        int caption = standard ? (int)(RynthTheme.HeaderHeight * s) : 0;
        int buttons = standard ? (int)HeaderButtonsWidth(entry, s, popped: true) : 0;
        ImGuiPopOuts.Open(entry.Title, left, top, (int)size.X, (int)size.Y, caption, buttons, s);
        entry.Popped = true;
        PersistPopped(entry, open: true, left, top, (int)size.X, (int)size.Y, placeScale);
    }

    private static float UiScaleForChrome() => EngineFrameController.FontScale;

    private static void PlaceFromSavedState(Entry entry, float placeScale)
    {
        Vector2 size = entry.Spec.DefaultSize;
        Vector2 pos;
        if (PanelStateStore.TryGetPanel(entry.Title, out PanelStateStore.PanelEntry saved) && (saved.Width > 0 || saved.Left != 0 || saved.Top != 0))
        {
            pos = new Vector2((float)saved.Left, (float)saved.Top);
            if (saved.Width > 0 && saved.Height > 0) size = new Vector2((float)saved.Width, (float)saved.Height);
        }
        else if (entry.Spec.DefaultPos is Vector2 defaultPos)
        {
            pos = defaultPos; // the face's own first spot (a script window's FirstUseEver position)
        }
        else if (entry.Spec.OpenCentered && CenteredLogicalPos(size, placeScale) is Vector2 centered)
        {
            pos = centered;
        }
        else
        {
            int shown = 0;
            lock (Sync) foreach (Entry e in DrawOrder) if (e.Shown) shown++;
            pos = new Vector2(100 + shown * 20, 100 + shown * 20); // Avalonia's cascade
        }

        size = Vector2.Max(size, entry.Spec.MinSize);
        // Side-by-side check: the ImGui copy sits offset from the Avalonia one.
        if (PanelRouter.ResolveDockedFace(entry.Title) == PanelFace.Both)
            pos += new Vector2(24, 24);

        entry.Pos = ClampToDisplay(pos * placeScale, size * placeScale);
        entry.Size = size * placeScale;
        entry.PersistedPos = entry.Pos;
        entry.PersistedSize = entry.Size;
        entry.NeedsPlacement = true;
        entry.DesiredPos = entry.Pos != pos * placeScale ? pos * placeScale : null;
        entry.DesiredForDisplay = ImGuiNET.ImGui.GetIO().DisplaySize;

        // Popped-out windows sit above the game frame, so a docked panel opening under
        // one would be invisible. The new spot is saved by the open's Persist, so the
        // panel comes back where it could be seen.
        if (ClearOfPopOuts(entry.Pos, entry.Size) is Vector2 clear)
        {
            RynthLog.UI($"ImGuiPanelHost: {entry.Title} would open under a popped-out window; " +
                        $"moved from ({entry.Pos.X:0},{entry.Pos.Y:0}) to ({clear.X:0},{clear.Y:0}).");
            entry.Pos = clear;
            entry.PersistedPos = clear;
            entry.DesiredPos = null;   // don't pull it back under the pop-out as the display grows
        }
    }

    /// <summary>Above this share of a docked panel hidden by popped-out windows, it opens elsewhere.</summary>
    private const float MaxCoveredFraction = 0.5f;
    /// <summary>Candidate spots per axis when looking for a clear place (a grid over the game view).</summary>
    private const int ClearSpotSteps = 8;

    /// <summary>
    /// A spot (client px) for a docked window of <paramref name="size"/> at <paramref name="pos"/>
    /// that popped-out windows hide less, nearest the original among the least-covered; null when
    /// at most half of it is covered or no spot is better.
    /// </summary>
    private static Vector2? ClearOfPopOuts(Vector2 pos, Vector2 size)
    {
        float area = size.X * size.Y;
        if (area <= 0) return null;
        List<(Vector2 Min, Vector2 Max)> covers = PopOutRectsInClient();
        if (covers.Count == 0) return null;
        float covered = CoveredArea(pos, size, covers);
        if (covered <= area * MaxCoveredFraction) return null;

        Vector2 display = ImGuiNET.ImGui.GetIO().DisplaySize;
        if (display.X <= 1 || display.Y <= 1) return null;
        Vector2 room = Vector2.Max(Vector2.Zero, display - size);

        // Pass 1: the least coverage any grid spot reaches. Pass 2: the nearest spot within
        // 2% of the panel's area of that, so a nearly-as-clear nearby spot beats a far one.
        float least = float.MaxValue;
        for (int i = 0; i <= ClearSpotSteps; i++)
            for (int j = 0; j <= ClearSpotSteps; j++)
                least = Math.Min(least, CoveredArea(GridSpot(room, i, j), size, covers));
        if (least >= covered) return null;

        float tolerance = least + area * 0.02f;
        Vector2? best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i <= ClearSpotSteps; i++)
            for (int j = 0; j <= ClearSpotSteps; j++)
            {
                Vector2 spot = GridSpot(room, i, j);
                if (CoveredArea(spot, size, covers) > tolerance) continue;
                float dist = Vector2.DistanceSquared(spot, pos);
                if (dist < bestDist) { bestDist = dist; best = spot; }
            }
        return best;
    }

    private static Vector2 GridSpot(Vector2 room, int i, int j) =>
        new(MathF.Round(room.X * i / ClearSpotSteps), MathF.Round(room.Y * j / ClearSpotSteps));

    /// <summary>Visible popped-out windows' rects in the game window's client px (ImGui coordinates).</summary>
    private static List<(Vector2 Min, Vector2 Max)> PopOutRectsInClient()
    {
        Vector2 origin = ImGuiPopOuts.ClientOriginOnScreen();
        var rects = new List<(Vector2, Vector2)>();
        foreach (var (left, top, width, height) in UI.LayeredWindow.VisibleContentRects())
        {
            var min = new Vector2(left, top) - origin;
            rects.Add((min, min + new Vector2(width, height)));
        }
        return rects;
    }

    /// <summary>Area of the window hidden by <paramref name="covers"/> (overlaps between covers count twice).</summary>
    private static float CoveredArea(Vector2 pos, Vector2 size, List<(Vector2 Min, Vector2 Max)> covers)
    {
        Vector2 max = pos + size;
        float total = 0;
        foreach (var (cMin, cMax) in covers)
        {
            float w = MathF.Min(max.X, cMax.X) - MathF.Max(pos.X, cMin.X);
            float h = MathF.Min(max.Y, cMax.Y) - MathF.Max(pos.Y, cMin.Y);
            if (w > 0 && h > 0) total += w * h;
        }
        return total;
    }

    /// <summary>
    /// Top-left (logical units) that centres a window of logical <paramref name="size"/> in the
    /// game view; null while the display size isn't known yet.
    /// </summary>
    private static Vector2? CenteredLogicalPos(Vector2 size, float placeScale)
    {
        Vector2 display = ImGuiNET.ImGui.GetIO().DisplaySize;
        if (display.X <= 1 || display.Y <= 1 || placeScale <= 0) return null;
        return Vector2.Max(Vector2.Zero, (display / placeScale - size) * 0.5f);
    }

    private static Vector2 ClampToDisplay(Vector2 pos, Vector2 size)
    {
        Vector2 display = ImGuiNET.ImGui.GetIO().DisplaySize;
        if (display.X <= 1 || display.Y <= 1) return pos;
        return new Vector2(
            Math.Clamp(pos.X, 0, Math.Max(0, display.X - size.X)),
            Math.Clamp(pos.Y, 0, Math.Max(0, display.Y - size.Y)));
    }

    /// <param name="popDisplay">The pop-out window's size when drawing popped out; null in the client.</param>
    private static void DrawPanel(Entry entry, float uiScale, float placeScale, Vector2? popDisplay)
    {
        PanelSpec spec = entry.Spec;
        bool popped = popDisplay.HasValue;
        bool standard = spec.Chrome == PanelChrome.Standard;
        bool both = PanelRouter.ResolveDockedFace(entry.Title) == PanelFace.Both;

        Vector2 minSize = (entry.Instance?.MinSize ?? spec.MinSize) * placeScale;
        if (popped)
        {
            // The panel fills its window; moving and resizing are the window's.
            // (At the pop-out's panel origin: its display is the work area around it.)
            ImGuiNET.ImGui.SetNextWindowPos(ImGuiPopOuts.PanelOrigin, ImGuiCond.Always);
            ImGuiNET.ImGui.SetNextWindowSize(popDisplay!.Value, ImGuiCond.Always);
            minSize = Vector2.Zero;
        }
        else
        {
            if (entry.HasRequest)
            {
                Vector2? reqSize, reqPos;
                lock (Sync)
                {
                    reqSize = entry.RequestedSize;
                    reqPos = entry.RequestedPos;
                    entry.RequestedSize = null;
                    entry.RequestedPos = null;
                    entry.HasRequest = false;
                }
                if (reqSize is Vector2 requested)
                {
                    entry.Size = Vector2.Max(requested * placeScale, minSize);
                    entry.NeedsPlacement = true;
                    // A script's size request is its own, not the player's: not saved.
                    if (entry.Dynamic) entry.PersistedSize = entry.Size;
                }
                if (reqPos is Vector2 requestedPos)
                {
                    entry.Pos = ClampToDisplay(requestedPos * placeScale, entry.Size);
                    entry.PersistedPos = entry.Pos;   // not a move by the player: nothing to save
                    entry.DesiredPos = null;
                    entry.NeedsPlacement = true;
                }
            }
            if (entry.DesiredPos is Vector2 want)
            {
                Vector2 display = ImGuiNET.ImGui.GetIO().DisplaySize;
                if (display != entry.DesiredForDisplay)
                {
                    entry.DesiredForDisplay = display;
                    Vector2 p = ClampToDisplay(want, entry.Size);
                    if (p != entry.Pos)
                    {
                        entry.Pos = p;
                        entry.PersistedPos = p;   // not a move by the player: nothing to save
                        entry.NeedsPlacement = true;
                    }
                    if (p == want) entry.DesiredPos = null;   // back where it was saved
                }
            }
            if (entry.NeedsPlacement)
            {
                ImGuiNET.ImGui.SetNextWindowPos(entry.Pos, ImGuiCond.Always);
                ImGuiNET.ImGui.SetNextWindowSize(entry.Size, ImGuiCond.Always);
                entry.NeedsPlacement = false;
            }
            ImGuiNET.ImGui.SetNextWindowSizeConstraints(minSize, new Vector2(float.MaxValue, float.MaxValue));
        }

        uint background = spec.Background ?? RynthTheme.PanelGlass;
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, spec.Rounding);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, spec.Chrome == PanelChrome.None ? 0f : 1f);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.WindowBg, background);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Border, spec.BorderColor ?? RynthTheme.PanelBorder);

        ImGuiWindowFlags flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
            | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoMove;
        bool clickThrough = false;
        // Popped out, a panel is always interactive (like the Avalonia pop-out was).
        if (!popped) try { clickThrough = entry.Instance?.ClickThrough ?? false; } catch { }
        if (clickThrough) flags |= ImGuiWindowFlags.NoInputs;

        bool childOpen = false;
        ImGuiNET.ImGui.Begin(entry.WindowId, flags);
        try
        {
            ImGuiNET.ImGui.PopStyleVar(3);  // the frame's own vars; the body gets normal padding
            float header = standard ? RynthTheme.HeaderHeight * uiScale : 0f;
            float grip = standard ? 18f * uiScale : 0f;

            if (standard && DrawHeader(entry, uiScale, both, popped))
                return; // closed, popped out or docked back this frame

            ImGuiNET.ImGui.SetCursorPos(new Vector2(0, header));
            // Per-panel text size (Settings > Display): the face's fonts come from the
            // set baked at that size (ImGuiFonts.Get); the header above keeps its size.
            // (Until a size change has been re-baked, the nearest size that is.)
            ImGuiFonts.CurrentStep = ImGuiFonts.BakedStepNear(PanelTextScale.Get(entry.Title));
            // Chromeless panels (the dashboard) have no frame to hold the grip, so it
            // sits over the body's bottom-right corner. It is handled by hand instead
            // of as an ImGui item: an item there competed with the face's own items
            // (the drag-anywhere surface) and with the body's scrollbar, so resizing
            // worked only sometimes. The mouse is tested against the corner directly,
            // the resize follows the mouse from where the drag began (no per-frame
            // drift), and the face's window-drag is ignored while resizing.
            // A GripInBody face (standard chrome, no strip under the body) is handled
            // the same way, over the corner the face leaves free.
            bool inlineGrip = standard && spec.GripInBody;
            bool manualGrip = (!standard || inlineGrip) && !clickThrough && !popped;   // popped: the window resizes itself
            Vector2 outerPos = ImGuiNET.ImGui.GetWindowPos(), outerSize = ImGuiNET.ImGui.GetWindowSize();
            float gSize = GripSize(entry, uiScale);
            Vector2 gMin = GripMin(entry, uiScale, outerPos + outerSize);
            bool resizing = false, gHot = false;
            if (manualGrip)
            {
                Vector2 mouse = ImGuiNET.ImGui.GetMousePos();
                bool over = ImGuiNET.ImGui.IsMouseHoveringRect(gMin, gMin + new Vector2(gSize, gSize), false)
                    && ImGuiNET.ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
                if (over && ImGuiNET.ImGui.IsMouseClicked(ImGuiMouseButton.Left) && _resizeTitle == null)
                {
                    _resizeTitle = entry.Title;
                    _resizeStartSize = outerSize;
                    _resizeStartMouse = mouse;
                }
                resizing = _resizeTitle == entry.Title;
                if (resizing && !ImGuiNET.ImGui.IsMouseDown(ImGuiMouseButton.Left))
                {
                    _resizeTitle = null;
                    resizing = false;
                }
                if (resizing)
                {
                    Vector2 display = ImGuiNET.ImGui.GetIO().DisplaySize;
                    Vector2 maxSize = display.X > 1 ? display - outerPos : new Vector2(float.MaxValue, float.MaxValue);
                    Vector2 next = Vector2.Clamp(_resizeStartSize + (mouse - _resizeStartMouse), minSize, Vector2.Max(minSize, maxSize));
                    if (next != outerSize) ImGuiNET.ImGui.SetWindowSize(next);
                }
                gHot = over || resizing;
                if (gHot) ImGuiNET.ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNWSE);
            }

            // While the body scrolls, end it above the corner so its scrollbar can't
            // take the grip's clicks. Held for the length of a resize so the body
            // doesn't flip between the two heights mid-drag.
            // (A popped GripInBody face too: its window's own corner grip sits there.)
            bool gripOverBody = manualGrip || (popped && inlineGrip);
            float gripRoom = standard && !inlineGrip ? grip
                : resizing ? entry.LastGripRoom
                : (entry.BodyScrolls && gripOverBody ? (gSize + (spec.GoldGrip ? 4f : 1f) * uiScale) : 0f);
            entry.LastGripRoom = gripRoom;
            Vector2 bodySize = ImGuiNET.ImGui.GetWindowSize() - new Vector2(0, header + gripRoom);
            if (bodySize.Y < 1) bodySize.Y = 1;
            ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, spec.EdgeToEdge ? Vector2.Zero : new Vector2(6, 4) * uiScale);
            ImGuiWindowFlags bodyFlags = ImGuiWindowFlags.NoBackground;
            if (spec.EdgeToEdge) bodyFlags |= ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
            bodyFlags |= spec.BodyFlags;
            if (clickThrough) bodyFlags |= ImGuiWindowFlags.NoInputs;
            childOpen = ImGuiNET.ImGui.BeginChild("##body", bodySize, ImGuiChildFlags.AlwaysUseWindowPadding, bodyFlags);
            ImGuiNET.ImGui.PopStyleVar();

            if (childOpen)
            {
                if (entry.Error != null)
                {
                    // Never a format string: the error names the panel key (a script window's
                    // key is script text, e.g. a hud called "50%s") and an exception message.
                    ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.4f, 0.3f, 1f));
                    ImGuiNET.ImGui.PushTextWrapPos(0f);
                    ImGuiNET.ImGui.TextUnformatted(entry.Error);
                    ImGuiNET.ImGui.PopTextWrapPos();
                    ImGuiNET.ImGui.PopStyleColor();
                }
                else if (entry.Instance != null)
                {
                    // Only while the body covers the corner (not when it scrolls and ends above it).
                    BodyGripReserve = inlineGrip && gripRoom == 0f ? gSize + 1f * uiScale : 0f;
                    try { DrawBody(entry); }
                    finally { BodyGripReserve = 0f; }
                }
                // Grip painted on top of the face when the body covers the corner
                // (a GripInBody face's even while it lets clicks through: its slot stays).
                if ((manualGrip || inlineGrip || (popped && !standard)) && gripRoom == 0f)
                    GripPaint(entry, uiScale, gMin, gHot, ImGuiNET.ImGui.GetWindowDrawList());
                entry.BodyScrolls = ImGuiNET.ImGui.GetScrollMaxY() > 0f;
            }
            ImGuiNET.ImGui.EndChild();
            childOpen = false;

            if (gripOverBody && gripRoom > 0f)
                GripPaint(entry, uiScale, gMin, gHot, ImGuiNET.ImGui.GetWindowDrawList());
            // Framed panels: the grip is an ordinary item in the frame's corner.
            // Popped out it's only painted: the window's own corner resizes it.
            if (standard && !inlineGrip && popped)
                GripPaint(entry, uiScale, GripMin(entry, uiScale, outerPos + outerSize), false, ImGuiNET.ImGui.GetWindowDrawList());
            else if (standard && !inlineGrip && !clickThrough)
                DrawGrip(entry, uiScale, minSize);
            if (resizing) _pendingDragDelta = Vector2.Zero;   // the face's window-drag doesn't also move it

            // A drag started from the panel's own surface (DragWindowWithLastItem).
            if (popped) _pendingDragDelta = Vector2.Zero;
            if (_pendingDragDelta != Vector2.Zero)
            {
                ImGuiNET.ImGui.SetWindowPos(ClampToDisplay(ImGuiNET.ImGui.GetWindowPos() + _pendingDragDelta, ImGuiNET.ImGui.GetWindowSize()));
                _pendingDragDelta = Vector2.Zero;
            }
        }
        finally
        {
            ImGuiFonts.CurrentStep = 0;
            if (childOpen) ImGuiNET.ImGui.EndChild();
            if (!popped)
            {
                // Popped out, Pos/Size keep the docked placement to return to.
                entry.Pos = ImGuiNET.ImGui.GetWindowPos();
                entry.Size = ImGuiNET.ImGui.GetWindowSize();
            }
            ImGuiNET.ImGui.End();
            ImGuiNET.ImGui.PopStyleColor(2);
        }

        // Persist once a move/resize has finished (not every frame of a drag).
        if (!popped && !ImGuiNET.ImGui.IsMouseDown(ImGuiMouseButton.Left)
            && (entry.Pos != entry.PersistedPos || entry.Size != entry.PersistedSize))
        {
            entry.PersistedPos = entry.Pos;
            entry.PersistedSize = entry.Size;
            entry.DesiredPos = null;   // the player placed it: that is the spot now
            Persist(entry, open: true, placeScale);
        }
    }

    /// <summary>The panel whose face is drawing right now (names a text box's owner in the input log). AC thread.</summary>
    internal static string? DrawingTitle { get; private set; }

    private static void DrawBody(Entry entry)
    {
        DrawingTitle = entry.Title;
        try
        {
            entry.Instance!.Draw();
        }
        catch (Exception ex)
        {
            entry.Failures++;
            RynthLog.UI($"ImGuiPanelHost: {entry.Title}.Draw threw {ex.GetType().Name}: {ex.Message} ({entry.Failures}/{MaxFailures})");
            if (entry.Failures >= MaxFailures)
                entry.Error = $"{entry.Title} stopped drawing after repeated errors: {ex.Message}";
        }
        finally
        {
            DrawingTitle = null;
        }
    }

    private static float HeaderButtonsWidth(Entry entry, float s, bool popped)
    {
        float btn = 20f * s;
        bool second = popped || (PanelRouter.CanPopOut && !entry.Spec.NoPopOut);
        // close, [pop-out / dock back], text size
        return btn + 2 * s + (second ? btn + 2 * s : 0) + btn + 4 * s;
    }

    /// <summary>The standard header. Returns true when the panel was closed, popped out or docked back.</summary>
    private static bool DrawHeader(Entry entry, float s, bool bothTag, bool popped)
    {
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        Vector2 winPos = ImGuiNET.ImGui.GetWindowPos();
        float width = ImGuiNET.ImGui.GetWindowWidth();
        float h = RynthTheme.HeaderHeight * s;
        float r = entry.Spec.Rounding;
        dl.AddRectFilled(winPos, winPos + new Vector2(width, h), RynthTheme.PanelHeader, r, ImDrawFlags.RoundCornersTop);

        float btn = 20f * s;
        // In Both (side-by-side) the Avalonia copy owns pop-out.
        bool canPopOut = popped || (PanelRouter.CanPopOut && !bothTag && !entry.Spec.NoPopOut);
        float buttonsWidth = HeaderButtonsWidth(entry, s, popped);

        // Drag surface: the header minus the buttons. Popped out, the window drags
        // itself from this band (LayeredWindow.CaptionHeight) and never shows it to ImGui.
        ImGuiNET.ImGui.SetCursorPos(Vector2.Zero);
        ImGuiNET.ImGui.InvisibleButton("##drag", new Vector2(Math.Max(1, width - buttonsWidth), h));
        if (!popped && ImGuiNET.ImGui.IsItemActive() && ImGuiNET.ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0f))
        {
            Vector2 next = ClampToDisplay(winPos + ImGuiNET.ImGui.GetIO().MouseDelta, ImGuiNET.ImGui.GetWindowSize());
            ImGuiNET.ImGui.SetWindowPos(next);
        }

        // Grip dots + DRAG (teal, 9) + title (10), vertically centred.
        ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.Ui9));
        float dragY = (h - ImGuiNET.ImGui.GetTextLineHeight()) * 0.5f;
        dl.AddText(winPos + new Vector2(6 * s, dragY), RynthTheme.DragLabel, DragLabel);
        float dragW = ImGuiNET.ImGui.CalcTextSize(DragLabel).X - 2 * s;
        ImGuiNET.ImGui.PopFont();
        ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.Ui10));
        float titleY = (h - ImGuiNET.ImGui.GetTextLineHeight()) * 0.5f;
        string title = bothTag ? entry.BothTitle : entry.DisplayTitle;
        dl.AddText(winPos + new Vector2(8 * s + dragW + 6 * s, titleY), ImGuiNET.ImGui.GetColorU32(ImGuiCol.Text), title);

        bool gone = false;
        float y = (h - btn) * 0.5f;
        float x = width - 4 * s - btn;
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        ImGuiNET.ImGui.SetCursorPos(new Vector2(x, y));
        if (HeaderIconButton("##close", PhosphorIcons.X, btn))
        {
            Close(entry.Title);
            gone = true;
        }
        ImGuiNET.ImGui.SetItemTooltip("Close");
        if (canPopOut)
        {
            ImGuiNET.ImGui.SetCursorPos(new Vector2(x - 2 * s - btn, y));
            if (popped)
            {
                if (HeaderIconButton("##redock", PhosphorIcons.ArrowSquareIn, btn))
                {
                    Redock(entry.Title);
                    gone = true;
                }
                ImGuiNET.ImGui.SetItemTooltip("Dock back into the game window");
            }
            else
            {
                if (HeaderIconButton("##popout", PhosphorIcons.ArrowSquareOut, btn))
                {
                    PopOut(entry.Title);
                    gone = true;
                }
                ImGuiNET.ImGui.SetItemTooltip("Pop out into a floating window");
            }
        }

        // Text size (the same per-panel setting as Settings > Display).
        float tsX = x - (canPopOut ? btn + 2 * s : 0) - 2 * s - btn;
        ImGuiNET.ImGui.SetCursorPos(new Vector2(tsX, y));
        int step = PanelTextScale.Get(entry.Title);
        if (HeaderIconButton("##textsize", PhosphorIcons.TextAa, btn, step != 0 ? RynthTheme.DragLabel : (uint?)null))
            ImGuiNET.ImGui.OpenPopup(TextSizePopupId);
        if (ImGuiNET.ImGui.BeginItemTooltip())
        {
            ImGuiNET.ImGui.TextUnformatted("Text size: " + PanelTextScale.Describe(step));
            ImGuiNET.ImGui.TextDisabled("Click to change. Ctrl+wheel on the title bar steps it.");
            ImGuiNET.ImGui.EndTooltip();
        }
        ImGuiNET.ImGui.PopStyleVar();

        // Ctrl+wheel anywhere on the title bar steps this window's size. Only the
        // header: the body's wheel belongs to the face (lists, chat, radar zoom).
        // Popped out, the drag band is the window's own caption and never reaches
        // ImGui, so there it works over the buttons.
        ImGuiIOPtr io = ImGuiNET.ImGui.GetIO();
        if (!gone && io.KeyCtrl && io.MouseWheel != 0f
            && ImGuiNET.ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem)
            && ImGuiNET.ImGui.IsMouseHoveringRect(winPos, winPos + new Vector2(width, h), false))
        {
            PanelTextScale.SetForWindow(entry.Title, step + (io.MouseWheel > 0f ? 1 : -1));
        }

        // Under the header, right-aligned to the button: never over a pop-out's drag band.
        ImGuiNET.ImGui.SetNextWindowPos(winPos + new Vector2(tsX + btn, h), ImGuiCond.Appearing, new Vector2(1, 0));
        DrawTextSizePopup(entry.Title, s);

        ImGuiNET.ImGui.PopFont();
        return gone;
    }

    private const string TextSizePopupId = "##textsize_pop";

    private static string[]? _textSizeMenuLabels;

    /// <summary>
    /// A "Text size" submenu for a chromeless panel's own menu (it has no title bar
    /// to hold the button): the steps, then "Use for all windows". AC thread.
    /// </summary>
    public static void TextSizeMenu(string title)
    {
        if (!ImGuiNET.ImGui.BeginMenu("Text size")) return;
        try
        {
            string[] labels = _textSizeMenuLabels ??= BuildTextSizeMenuLabels();
            int step = PanelTextScale.Get(title);
            for (int i = 0; i < labels.Length; i++)
                if (ImGuiNET.ImGui.MenuItem(labels[i], "", i == step) && i != step)
                    PanelTextScale.SetForWindow(title, i);
            ImGuiNET.ImGui.Separator();
            if (ImGuiNET.ImGui.MenuItem("Use for all windows"))
                PanelTextScale.UseForAll(step);
        }
        finally
        {
            ImGuiNET.ImGui.EndMenu();
        }
    }

    private static string[] BuildTextSizeMenuLabels()
    {
        var labels = new string[PanelTextScale.SizeNames.Length];
        for (int i = 0; i < labels.Length; i++)
            labels[i] = i == 0 ? "Normal" : PanelTextScale.Describe(i);
        return labels;
    }

    /// <summary>The title bar's text size popup: smaller / bigger, a slider over the steps, reset, use everywhere.</summary>
    private static void DrawTextSizePopup(string title, float s)
    {
        if (!ImGuiNET.ImGui.BeginPopup(TextSizePopupId)) return;
        try
        {
            int step = PanelTextScale.Get(title);
            int max = PanelTextScale.SizeNames.Length - 1;
            int all = PanelTextScale.GetAll();
            float btn = 20f * s;

            ImGuiNET.ImGui.TextUnformatted("Text size");
            ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
            ImGuiNET.ImGui.BeginDisabled(step <= 0);
            if (HeaderIconButton("##ts_smaller", PhosphorIcons.Minus, btn))
                PanelTextScale.SetForWindow(title, step - 1);
            ImGuiNET.ImGui.EndDisabled();
            ImGuiNET.ImGui.SetItemTooltip("Smaller");
            ImGuiNET.ImGui.SameLine(0, 4 * s);
            ImGuiNET.ImGui.BeginDisabled(step >= max);
            if (HeaderIconButton("##ts_bigger", PhosphorIcons.Plus, btn))
                PanelTextScale.SetForWindow(title, step + 1);
            ImGuiNET.ImGui.EndDisabled();
            ImGuiNET.ImGui.SetItemTooltip("Bigger");
            ImGuiNET.ImGui.PopStyleVar();
            ImGuiNET.ImGui.SameLine(0, 8 * s);
            ImGuiNET.ImGui.AlignTextToFramePadding();
            ImGuiNET.ImGui.TextUnformatted(PanelTextScale.Describe(step));

            int pick = step;
            ImGuiNET.ImGui.SetNextItemWidth(btn * 2 + 4 * s + 8 * s + 110 * s);
            if (ImGuiNET.ImGui.SliderInt("##ts_step", ref pick, 0, max, "%d", ImGuiSliderFlags.AlwaysClamp) && pick != step)
                PanelTextScale.SetForWindow(title, pick);

            ImGuiNET.ImGui.BeginDisabled(step == all);
            if (ImGuiNET.ImGui.Button(PhosphorIcons.ArrowCounterClockwise + " Reset"))
                PanelTextScale.ResetWindow(title);
            ImGuiNET.ImGui.EndDisabled();
            ImGuiNET.ImGui.SetItemTooltip(all == 0 ? "Back to normal size" : "Back to the All Panels size (Settings > Display)");
            ImGuiNET.ImGui.SameLine();
            if (ImGuiNET.ImGui.Button("Use for all windows"))
                PanelTextScale.UseForAll(step);
            ImGuiNET.ImGui.SetItemTooltip("Every in-game window at this size (Settings > Display > All Panels)");
        }
        finally
        {
            ImGuiNET.ImGui.EndPopup();
        }
    }

    private const string DragLabel = PhosphorIcons.DotsSixVertical + "DRAG";

    /// <summary>A header button (the theme's button look) with an icon centred on it.</summary>
    private static bool HeaderIconButton(string id, string icon, float size, uint? color = null)
    {
        Vector2 at = ImGuiNET.ImGui.GetCursorScreenPos();
        bool clicked = ImGuiNET.ImGui.Button(id, new Vector2(size, size));
        PhosphorIcons.DrawCentered(ImGuiNET.ImGui.GetWindowDrawList(), ImGuiFonts.Get(UiFont.Ui14), icon, at,
            new Vector2(size, size), color ?? ImGuiNET.ImGui.GetColorU32(ImGuiCol.Text));
        return clicked;
    }

    /// <summary>The resize grip at the bottom-right (16 px, teal, rounded outer corner).</summary>
    private static float GripSize(Entry entry, float s) => (entry.Spec.GoldGrip ? 14f : 16f) * s;

    /// <summary>Top-left of the grip for a corner (bottom-right point).</summary>
    private static Vector2 GripMin(Entry entry, float s, Vector2 corner)
    {
        float size = GripSize(entry, s);
        float inset = (entry.Spec.GoldGrip ? 4f : 1f) * s;
        return corner - new Vector2(size + inset, size + inset);
    }

    private static bool DrawGrip(Entry entry, float s, Vector2 minSize)
    {
        Vector2 winPos = ImGuiNET.ImGui.GetWindowPos();
        Vector2 winSize = ImGuiNET.ImGui.GetWindowSize();
        Vector2 min = GripMin(entry, s, winPos + winSize);
        bool hot = GripInteract(entry, s, minSize, min, winPos, winSize, null);
        GripPaint(entry, s, min, hot, ImGuiNET.ImGui.GetWindowDrawList());
        return hot;
    }

    /// <summary>
    /// The grip's hit area at <paramref name="min"/>; dragging it resizes the panel
    /// window (<paramref name="windowName"/>, or the current window when null).
    /// Returns true while hovered or dragged.
    /// </summary>
    private static bool GripInteract(Entry entry, float s, Vector2 minSize, Vector2 min, Vector2 winPos, Vector2 winSize,
        string? windowName)
    {
        float size = GripSize(entry, s);
        ImGuiNET.ImGui.SetCursorScreenPos(min);
        ImGuiNET.ImGui.InvisibleButton("##grip", new Vector2(size, size));
        bool hot = ImGuiNET.ImGui.IsItemHovered() || ImGuiNET.ImGui.IsItemActive();
        if (hot) ImGuiNET.ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNWSE);
        if (ImGuiNET.ImGui.IsItemActive() && ImGuiNET.ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0f))
        {
            Vector2 display = ImGuiNET.ImGui.GetIO().DisplaySize;
            Vector2 maxSize = display.X > 1 ? display - winPos : new Vector2(float.MaxValue, float.MaxValue);
            Vector2 next = Vector2.Clamp(winSize + ImGuiNET.ImGui.GetIO().MouseDelta, minSize, Vector2.Max(minSize, maxSize));
            if (windowName != null) ImGuiNET.ImGui.SetWindowSize(windowName, next);
            else ImGuiNET.ImGui.SetWindowSize(next);
        }
        return hot;
    }

    private static void GripPaint(Entry entry, float s, Vector2 min, bool hot, ImDrawListPtr dl)
    {
        bool gold = entry.Spec.GoldGrip;
        float size = GripSize(entry, s);
        Vector2 max = min + new Vector2(size, size);
        uint accent = gold ? RynthTheme.RadarGold : RynthTheme.GripGlyph;
        uint fill = gold ? (hot ? RynthTheme.RadarGripHover : RynthTheme.RadarGripIdle) : (hot ? RynthTheme.GripHover : RynthTheme.GripIdle);
        dl.AddRectFilled(min, max, fill, gold ? 0f : 4f * s, ImDrawFlags.RoundCornersBottomRight);
        dl.AddLine(min, new Vector2(max.X, min.Y), accent);
        PhosphorIcons.DrawCentered(dl, ImGuiFonts.Get(UiFont.Ui11), PhosphorIcons.Resize, min, new Vector2(size, size), accent);
    }

    /// <summary>
    /// Saves open state and docked placement (converted back to Avalonia
    /// logical units), keeping the popped-out screen position. Written off
    /// AC's thread, in order with the router's own writes.
    /// </summary>
    /// <summary>
    /// Saves a popped-out panel: open state, its window's screen position and size
    /// (the size in Avalonia logical units, like the docked size), and the docked
    /// position it returns to. Written off AC's thread, in order.
    /// </summary>
    private static void PersistPopped(Entry entry, bool open, int left, int top, int width, int height, float placeScale, bool keepSize = false)
    {
        string title = entry.Title;
        Vector2 docked = entry.Pos / placeScale;
        Vector2 size = new Vector2(width, height) / placeScale;
        UiBackgroundWriter.Enqueue($"persist {title}", () =>
        {
            PanelStateStore.TryGetPanel(title, out PanelStateStore.PanelEntry prior);
            double w = keepSize && prior.Width > 0 ? prior.Width : size.X;
            double h = keepSize && prior.Height > 0 ? prior.Height : size.Y;
            PanelStateStore.SetPanel(title, new PanelStateStore.PanelEntry(
                open, docked.X, docked.Y, w, h, true, left, top));
        });
    }

    private static void Persist(Entry entry, bool open, float placeScale, bool floating = false)
    {
        string title = entry.Title;
        Vector2 pos = entry.Pos / placeScale;
        Vector2 size = entry.Size / placeScale;
        if (PanelRouter.ResolveDockedFace(title) == PanelFace.Both)
            pos -= new Vector2(24, 24); // don't let the side-by-side offset drift the Avalonia face
        UiBackgroundWriter.Enqueue($"persist {title}", () =>
        {
            PanelStateStore.TryGetPanel(title, out PanelStateStore.PanelEntry prior);
            PanelStateStore.SetPanel(title, new PanelStateStore.PanelEntry(
                open, pos.X, pos.Y, size.X, size.Y, floating, prior.FloatingLeft, prior.FloatingTop));
        });
    }
}
