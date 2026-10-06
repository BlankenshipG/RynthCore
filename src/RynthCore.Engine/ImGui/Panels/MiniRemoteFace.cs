// ============================================================================
//  RynthCore.Engine - ImGui/Panels/MiniRemoteFace.cs
//  The Mini Remote: a small grid of slots the player fills with items; a click
//  uses that item. Engine only, ImGui only. (An idea from Silentkelpie's rynth
//  PR #1, done as an engine face.)
//
//    slots     the item's icon and carried count; an empty slot shows +
//    click     uses the slot's item (the same object if still carried, else a
//              stack with its name); on an empty slot, puts the selected
//              inventory item there
//    right     Set to the selected item, Clear, More / Fewer slots
//    drop      drag an item from the game's inventory (or the Inventory panel)
//              onto a slot to put it there (UiDropTargets, one target a slot)
//    status    the use waiting for the server, or how the last one ended
//
//  A click never calls AC from the render thread: it posts an InventoryModel
//  Use request, which AC's main thread sends at its next tick behind the
//  client's own item gate (one item action at a time; a click while one waits
//  is refused, not queued). Slots are saved per character (ItemHudStore).
// ============================================================================

using System;
using System.Globalization;
using System.Numerics;
using ImGuiNET;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.UI.Data;
using RynthCore.Engine.UI.ScriptWindows;
using static RynthCore.Engine.ImGuiBackend.Panels.FaceKit;

namespace RynthCore.Engine.ImGuiBackend.Panels;

internal sealed class MiniRemoteFace : IImGuiPanel
{
    public const string Title = "Mini Remote";

    public static void Register()
    {
        ImGuiPanelHost.Register(Title,
            new PanelSpec(new Vector2(184, 150), new Vector2(110, 96), EdgeToEdge: true, GripInBody: true),
            () => new MiniRemoteFace());
        ItemHudStore.EnsureLoadQueued();
    }

    private const float Slot = 36f, Gap = 4f, IconSize = 32f, StatusH = 18f;
    private const string MenuId = "##miniremote_menu";
    private static readonly string[] DropKeys = BuildDropKeys();

    private static string[] BuildDropKeys()
    {
        var keys = new string[ItemHudCharacter.MaxSlots];
        for (int i = 0; i < keys.Length; i++) keys[i] = "MiniRemote#" + i.ToString(CultureInfo.InvariantCulture);
        return keys;
    }

    private readonly HudItemCache _items = new();
    private string _message = "";
    private uint _messageColor;
    private long _messageUntilMs;
    private int _menuSlot = -1;
    private bool _openMenu;

    // Per-slot resolved item and count text, rebuilt when the carried items or the slots change.
    private int _resolvedItemsVersion = -1;
    private ItemHudCharacter? _resolvedFor;
    private uint[] _slotItem = Array.Empty<uint>();
    private string[] _slotCount = Array.Empty<string>();

    public void OnShown()
    {
        ItemHudStore.EnsureLoadQueued();
        InventoryModel.Subscribe();
    }

    public void OnHidden()
    {
        InventoryModel.Unsubscribe();
        _menuSlot = -1;
    }

    public void Draw()
    {
        float w = Begin(out Vector2 origin, out Vector2 size);
        InventoryView view = InventoryModel.Current;
        HudItem[] items = _items.Get(view);
        ItemHudCharacter data = ItemHudStore.Current;
        bool ready = ItemHudStore.Loaded && ItemHudStore.HasCharacter && view.PlayerId != 0;
        Resolve(data, items);

        float gridH = Math.Max(Slot, Remaining(origin, size) - StatusH - GripOverlap());
        DrawGrid(data, items, w, gridH, ready);
        DrawStatus(ready);
        End(origin, size);
        DrawMenu(data, items);
    }

    private void Resolve(ItemHudCharacter data, HudItem[] items)
    {
        if (ReferenceEquals(data, _resolvedFor) && _resolvedItemsVersion == _items.Version) return;
        _resolvedFor = data;
        _resolvedItemsVersion = _items.Version;
        int n = data.Slots.Length;
        _slotItem = new uint[n];
        _slotCount = new string[n];
        for (int i = 0; i < n; i++)
        {
            RemoteSlot? s = data.Slots[i];
            if (s == null) { _slotCount[i] = ""; continue; }
            _slotItem[i] = ItemHudMath.Resolve(items, s);
            long c = s.Name.Length > 0 ? ItemHudMath.Count(items, s.Name) : _slotItem[i] != 0 ? 1 : 0;
            _slotCount[i] = c > 1 ? c.ToString(CultureInfo.InvariantCulture) : c == 0 ? "0" : "";
        }
    }

    // ── Grid ────────────────────────────────────────────────────────────

    private void DrawGrid(ItemHudCharacter data, HudItem[] items, float w, float h, bool ready)
    {
        Vector2 start = ImGuiNET.ImGui.GetCursorScreenPos();
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        int cols = Math.Max(1, (int)((w + Gap) / (Slot + Gap)));
        bool dragging = ready && UiDropTargets.AnyDragInProgress;
        int clicked = -1;

        for (int i = 0; i < data.Slots.Length; i++)
        {
            var p = new Vector2(start.X + (i % cols) * (Slot + Gap), start.Y + (i / cols) * (Slot + Gap));
            var max = p + new Vector2(Slot, Slot);
            RemoteSlot? s = data.Slots[i];

            // One drop target a slot, so the item lands where it was let go.
            UiDropTargets.Publish(DropKeys[i], Title, p, max, ready);
            if (UiDropTargets.TryTake(DropKeys[i], out uint dropped) && ready)
                SetSlot(i, dropped, items);

            ImGuiNET.ImGui.SetCursorScreenPos(p);
            ImGuiNET.ImGui.PushID(i);
            if (ImGuiNET.ImGui.InvisibleButton("##slot", new Vector2(Slot, Slot))) clicked = i;
            bool hot = ImGuiNET.ImGui.IsItemHovered();
            if (ImGuiNET.ImGui.IsItemClicked(ImGuiMouseButton.Right)) { _menuSlot = i; _openMenu = true; }
            ImGuiNET.ImGui.PopID();

            bool dropHot = dragging && UiDropTargets.IsCursorOver(DropKeys[i]);
            dl.AddRectFilled(p, max, dropHot ? Green : hot ? Lighten(BtnFill) : BtnFill, 3);
            dl.AddRect(p, max, dropHot || dragging ? Teal : BtnBord, 3);

            uint id = s != null && i < _slotItem.Length ? _slotItem[i] : 0;
            Vector2 ip = p + new Vector2((Slot - IconSize) * 0.5f, (Slot - IconSize) * 0.5f);
            if (s == null)
            {
                PhosphorIcons.DrawCentered(dl, ImGuiFonts.Get(UiFont.Ui14), PhosphorIcons.Plus, p, new Vector2(Slot, Slot), Faded(Mute));
                if (hot) ImGuiNET.ImGui.SetItemTooltip("Empty slot: click to put the selected inventory item here, or drag an item onto it.");
                continue;
            }
            if (id != 0 && ScriptIcons.TryGet(ScriptIconKind.Object, id, out IntPtr tex))
                dl.AddImage(tex, ip, ip + new Vector2(IconSize, IconSize));
            else
            {
                string initial = s.Name.Length > 0 ? s.Name[..1].ToUpperInvariant() : "?";
                Vector2 ts = ImGuiNET.ImGui.CalcTextSize(initial);
                dl.AddText(p + (new Vector2(Slot, Slot) - ts) * 0.5f, Faded(Text), initial);
            }
            string count = _slotCount[i];
            if (count.Length > 0)
            {
                Vector2 cs = ImGuiNET.ImGui.CalcTextSize(count);
                Vector2 cp = max - cs - new Vector2(3, 1);
                dl.AddText(cp + new Vector2(1, 1), 0xE0000000, count);
                dl.AddText(cp, id == 0 ? Amber : Text, count);
            }
            if (hot)
                ImGuiNET.ImGui.SetItemTooltip(id == 0
                    ? $"{s.Name}: none carried.\nRight-click for options."
                    : $"{s.Name} ({count switch { "" => "1", _ => count }} carried)\nClick to use. Right-click for options.");
        }
        int rows = (data.Slots.Length + cols - 1) / cols;
        ImGuiNET.ImGui.SetCursorScreenPos(start);
        ImGuiNET.ImGui.Dummy(new Vector2(w, Math.Min(h, rows * (Slot + Gap))));

        if (clicked >= 0 && ready) Click(data, items, clicked);
        else if (clicked >= 0) ShowMessage(ItemHudStore.Loaded ? "Waiting for your character..." : "Loading...", Amber);
    }

    private void Click(ItemHudCharacter data, HudItem[] items, int i)
    {
        RemoteSlot? s = data.Slots[i];
        if (s == null)
        {
            uint sel = InventoryModel.SelectedItem;
            if (sel != 0) SetSlot(i, sel, items);
            else ShowMessage("Select an inventory item (or drag one here) to fill this slot.", Amber);
            return;
        }
        uint id = i < _slotItem.Length ? _slotItem[i] : 0;
        if (id == 0) { ShowMessage($"No {s.Name} carried.", Amber); return; }
        // Posted to AC's main thread (InventoryModel's tick), never sent from here.
        var request = new InventoryRequest { Kind = InventoryActionKind.Use, ItemId = id, ItemName = s.Name };
        if (!InventoryModel.Request(request, out string why)) ShowMessage(why, Amber);
    }

    private void SetSlot(int i, uint itemId, HudItem[] items)
    {
        string name = ItemCountFace.NameOf(itemId, items);
        if (name.Length == 0) { ShowMessage("That item's name isn't known yet; try again in a moment.", Amber); return; }
        if (!ItemHudMath.TryFind(items, itemId, out _))
            ShowMessage($"{name} isn't in your inventory; the slot uses one once you carry it.", Mute);
        else ShowMessage($"Slot {i + 1}: {name}.", Mute);
        if (!ItemHudStore.Update(d => d.WithSlot(i, new RemoteSlot(itemId, name)), out string why)) ShowMessage(why, Amber);
    }

    // ── Status ──────────────────────────────────────────────────────────

    private void ShowMessage(string text, uint color)
    {
        _message = text;
        _messageColor = color;
        _messageUntilMs = Environment.TickCount64 + 3500;
    }

    private void DrawStatus(bool ready)
    {
        InventoryPending? pending = InventoryModel.Pending;
        InventoryResult? last = InventoryModel.LastResult;
        long nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        string text;
        uint color;
        if (_message.Length > 0 && Environment.TickCount64 < _messageUntilMs) { text = _message; color = _messageColor; }
        else if (pending != null) { text = pending.WaitingForGame ? "Waiting for the game: " + pending.Label : pending.Label + "..."; color = Teal; }
        else if (last != null && nowTicks - last.AtTicks < System.Diagnostics.Stopwatch.Frequency * 4) { text = last.Text; color = last.Ok ? Green : Amber; }
        else if (!ready) { text = ItemHudStore.Loaded ? "Waiting for your character..." : "Loading..."; color = Mute; }
        else { text = "Click: use. Right-click: options."; color = Faded(Mute); }
        Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        float w = ImGuiNET.ImGui.GetContentRegionAvail().X - BodyGripRoom();
        dl.PushClipRect(p, p + new Vector2(Math.Max(10, w), StatusH), true);
        dl.AddText(p + new Vector2(0, 2), color, text);
        dl.PopClipRect();
        ImGuiNET.ImGui.Dummy(new Vector2(Math.Max(10, w), StatusH));
    }

    private static float BodyGripRoom() => ImGuiPanelHost.BodyGripReserve;

    // ── Right-click menu ────────────────────────────────────────────────

    private void DrawMenu(ItemHudCharacter data, HudItem[] items)
    {
        if (_openMenu)
        {
            _openMenu = false;
            ImGuiNET.ImGui.OpenPopup(MenuId);
        }
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.PopupBg, ShellBg);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Border, Teal);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Text, Text);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 6));
        bool open = ImGuiNET.ImGui.BeginPopup(MenuId);
        ImGuiNET.ImGui.PopStyleVar();
        ImGuiNET.ImGui.PopStyleColor(3);
        if (!open) return;

        int i = _menuSlot;
        RemoteSlot? s = i >= 0 && i < data.Slots.Length ? data.Slots[i] : null;
        Label(i >= 0 ? $"Slot {i + 1}{(s != null ? ": " + s.Name : " (empty)")}" : "Mini Remote", Mute);
        uint sel = InventoryModel.SelectedItem;
        string selName = sel != 0 && ItemHudMath.TryFind(items, sel, out HudItem it) ? it.Name : "";
        if (ImGuiNET.ImGui.MenuItem(selName.Length > 0 ? "Set to " + selName : "Set to the selected item", "", false, i >= 0 && selName.Length > 0))
            SetSlot(i, sel, items);
        if (ImGuiNET.ImGui.MenuItem("Clear slot", "", false, s != null))
            if (!ItemHudStore.Update(d => d.WithSlot(i, null), out string why)) ShowMessage(why, Amber);
        ImGuiNET.ImGui.Separator();
        int n = data.Slots.Length;
        if (ImGuiNET.ImGui.MenuItem($"More slots ({n} now)", "", false, n < ItemHudCharacter.MaxSlots))
            ItemHudStore.Update(d => d.WithSlotCount(d.Slots.Length + 4), out _);
        if (ImGuiNET.ImGui.MenuItem("Fewer slots (drops the last 4)", "", false, n > ItemHudCharacter.MinSlots))
            ItemHudStore.Update(d => d.WithSlotCount(d.Slots.Length - 4), out _);
        ImGuiNET.ImGui.EndPopup();
    }
}
