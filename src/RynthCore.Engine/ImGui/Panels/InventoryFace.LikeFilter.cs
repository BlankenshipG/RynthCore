// ============================================================================
//  RynthCore.Engine - ImGui/Panels/InventoryFace.LikeFilter.cs
//  "Filter inventory like this" (right-click an item, 2026-10-24): the
//  "Add to loot profile" popup in a filter mode (LootAddDialog) builds a rule
//  from the item with the same match and attribute checklist, and RynthAi
//  judges the panel's items with its looter's evaluator. The list then shows
//  only the matches, under a bar that names the filter, counts what it can't
//  judge yet (not identified: "Identify N") and clears it.
//
//  The matches are RynthAi's answer for a list of ids, so the panel asks again
//  (find_refresh, every two seconds while it shows) to pick up new loot and
//  identifies; RynthAi answers only when something changed. The filter lasts
//  until it is cleared or the panel is hidden; Clear filters (closing the
//  Classic tools row) clears it too, and in Classic it shows in the tools row,
//  which opens when the filter is applied.
//
//  "Find like this in Global Inventory" uses the same popup; RynthInventory
//  does the filtering there (/ginv like), so nothing of it lives here.
//  Threads: the panel's (render) thread only.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using ImGuiNET;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.UI.Data;
using static RynthCore.Engine.ImGuiBackend.Panels.FaceKit;

namespace RynthCore.Engine.ImGuiBackend.Panels;

internal sealed partial class InventoryFace
{
    private static readonly long LikeRefreshTicks = Stopwatch.Frequency * 2;

    private bool _likeActive, _likeSubscribed;
    private string _likeLabel = string.Empty, _likeTip = string.Empty, _likeError = string.Empty;
    private readonly HashSet<uint> _likeIds = new();
    private readonly List<uint> _likeUnknown = new();
    // Bumped whenever the matches change, so the list rebuilds.
    private int _likeVersion;
    private int _builtLikeKey = int.MinValue;
    private bool _likePass;
    private long _likeRefreshAt;
    // The answers this panel waits for: the Filter that started it, then its refreshes.
    private int _likeAppliedSeq;
    private readonly HashSet<int> _likeAsked = new();
    private long _likeSeenVersion = -1;

    /// <summary>The like filter hides items now (in Classic, only while the tools row is open).</summary>
    private bool LikeOn => _likeActive && (!InventorySettingsStore.Classic || InventorySettingsStore.ShowTools);

    /// <summary>Opens the filter popup for <paramref name="it"/> (Global: Global Inventory instead of this panel).</summary>
    private void OpenLikeFilter(InventoryItem it, bool global)
    {
        _lootAdd.Candidates = LikeCandidates;
        _lootAdd.Found = ApplyLike;
        _lootAdd.Open(it.Id, it.Name, toOpenProfile: false, global ? LootAddMode.FilterGlobal : LootAddMode.FilterInventory);
    }

    /// <summary>The tools row's filter button: the same popup with every attribute and no item.</summary>
    private void OpenBlankFilter()
    {
        _lootAdd.Candidates = LikeCandidates;
        _lootAdd.Found = ApplyLike;
        _lootAdd.Open(0, string.Empty, toOpenProfile: false, LootAddMode.FilterInventory, blank: true);
    }

    /// <summary>Every item the panel knows: the packs' (pack-slot items included) and the worn ones.</summary>
    private static List<uint> LikeCandidates()
    {
        InventoryView view = InventoryModel.Current;
        var ids = new List<uint>(view.ItemCount + view.Worn.Length + 8);
        var seen = new HashSet<uint>();
        foreach (InventoryPack p in view.Packs)
            foreach (InventoryItem it in p.Items)
                if (seen.Add(it.Id)) ids.Add(it.Id);
        foreach (InventoryItem it in view.Worn)
            if (seen.Add(it.Id)) ids.Add(it.Id);
        return ids;
    }

    /// <summary>RynthAi's answer to Filter: show its matches from now on.</summary>
    private void ApplyLike(LootEditItemFindDto f)
    {
        if (!_likeSubscribed)
        {
            UiSources.LootEdit.Subscribe();
            _likeSubscribed = true;
        }
        _likeActive = true;
        _likeLabel = f.Label.Length > 0 ? f.Label : "like " + f.ItemName;
        _likeTip = "Filter " + _likeLabel + "\n" + string.Join("\n", f.Lines);
        _likeError = string.Empty;
        _likeAppliedSeq = f.Seq;
        _likeAsked.Clear();
        _likeSeenVersion = UiSources.LootEdit.Current?.Version ?? -1;
        SetLikeResult(f);
        _likeRefreshAt = Stopwatch.GetTimestamp() + LikeRefreshTicks;
        // Classic shows the filter bar in its tools row.
        if (InventorySettingsStore.Classic && !InventorySettingsStore.ShowTools) InventorySettingsStore.SetShowTools(true);
    }

    private void SetLikeResult(LootEditItemFindDto f)
    {
        _likeIds.Clear();
        foreach (uint id in f.Ids) _likeIds.Add(id);
        _likeUnknown.Clear();
        _likeUnknown.AddRange(f.Unknown);
        _likeVersion++;
    }

    /// <summary>Clears the like filter (and tells RynthAi to drop its rule).</summary>
    private void ClearLike()
    {
        if (!_likeActive && !_likeSubscribed) return;
        if (_likeActive) LootEditCommands.Simple("find_close");
        _likeActive = false;
        _likeIds.Clear();
        _likeUnknown.Clear();
        _likeAsked.Clear();
        _likeError = string.Empty;
        _likeVersion++;
        if (_likeSubscribed)
        {
            UiSources.LootEdit.Unsubscribe();
            _likeSubscribed = false;
        }
    }

    /// <summary>Once per frame before the list rebuild: takes refresh answers and asks again when due.</summary>
    private void LikeTick()
    {
        if (!_likeActive) return;
        var snap = UiSources.LootEdit.Current;
        if (snap != null && snap.Version != _likeSeenVersion)
        {
            _likeSeenVersion = snap.Version;
            LootEditItemFindDto? f = snap.Value.State?.ItemFind;
            if (f != null && f.Scope == LootFindScopes.Inventory && f.Seq > _likeAppliedSeq && _likeAsked.Contains(f.Seq))
            {
                _likeAppliedSeq = f.Seq;
                _likeAsked.RemoveWhere(s => s <= f.Seq);
                if (f.Ok)
                {
                    _likeError = string.Empty;
                    SetLikeResult(f);
                }
                else
                    _likeError = f.Error;   // keep the last matches; the bar says what went wrong
            }
        }
        long now = Stopwatch.GetTimestamp();
        if (now < _likeRefreshAt) return;
        _likeRefreshAt = now + LikeRefreshTicks;
        int seq = LootAddDialog.NextSeq();
        if (_likeAsked.Count > 8) _likeAsked.Clear();   // answers that never came (RynthAi unloaded)
        _likeAsked.Add(seq);
        LootEditCommands.Send(new LootEditCmd
        {
            Op = "find_refresh",
            Item = new LootEditItemRequestDto { Seq = seq, Find = true, Scope = LootFindScopes.Inventory, Candidates = LikeCandidates() },
        });
    }

    /// <summary>Start of a list rebuild.</summary>
    private void BeginLikePass(bool likeOn) => _likePass = likeOn;

    /// <summary>Does the like filter keep <paramref name="item"/>?</summary>
    private bool LikeKeeps(InventoryItem item) => _likeIds.Contains(item.Id);

    /// <summary>The bar under the tools row while the like filter is on: what it is, what it can't judge, clear.</summary>
    private void LikeBar(float w)
    {
        if (!LikeOn) return;
        Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
        const float h = 22;
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        dl.AddRectFilled(p, p + new Vector2(w, h), Selected, 4);
        dl.AddRect(p, p + new Vector2(w, h), Teal, 4);
        ImFontPtr icons = ImGuiFonts.Get(UiFont.Ui11);
        PhosphorIcons.DrawCentered(dl, icons, PhosphorIcons.Funnel, p, new Vector2(20, h), Teal);

        // Right side: Identify N (when some can't be judged yet), then the clear button.
        float right = p.X + w - 2;
        if (IconButton("##inv_like_x", PhosphorIcons.X, new Vector2(right - 20, p.Y + 1), new Vector2(20, 20), Amber, BtnFill, font: UiFont.Ui11))
        {
            ClearLike();
            NextLine(p, h + 4);
            return;
        }
        SafeImGui.SetItemTooltip("Clear the filter");
        right -= 24;
        if (_likeUnknown.Count > 0)
        {
            string label = "Identify " + _likeUnknown.Count.ToString(CultureInfo.InvariantCulture);
            float bw = ImGuiNET.ImGui.CalcTextSize(label).X + 14;
            if (Button("##inv_like_id", label, new Vector2(right - bw, p.Y + 1), new Vector2(bw, 20), Amber, BtnFill))
                InventoryModel.QueueIdentify(_likeUnknown);
            SafeImGui.SetItemTooltip(_likeUnknown.Count.ToString(CultureInfo.InvariantCulture)
                + " item(s) hidden: the filter needs what an identify tells about them. Identify asks the server (a few a second).");
            right -= bw + 4;
        }

        string text = _likeError.Length > 0 ? _likeLabel + " - " + _likeError : _likeLabel;
        float tx = p.X + 22, maxW = Math.Max(10, right - tx - 4);
        string shown = Fit(text, maxW);
        dl.AddText(new Vector2(tx, p.Y + (h - ImGuiNET.ImGui.GetFontSize()) * 0.5f), _likeError.Length > 0 ? Amber : Teal, shown);
        ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(tx, p.Y));
        ImGuiNET.ImGui.InvisibleButton("##inv_like_lbl", new Vector2(maxW, h));
        if (ImGuiNET.ImGui.IsItemHovered()) SafeImGui.SetTooltip(_likeTip);
        NextLine(p, h + 4);
    }

    /// <summary><paramref name="text"/>, cut with "..." to fit <paramref name="width"/>.</summary>
    private static string Fit(string text, float width)
    {
        if (ImGuiNET.ImGui.CalcTextSize(text).X <= width) return text;
        for (int n = text.Length - 1; n > 0; n--)
        {
            string s = text.Substring(0, n) + "...";
            if (ImGuiNET.ImGui.CalcTextSize(s).X <= width) return s;
        }
        return "...";
    }
}
