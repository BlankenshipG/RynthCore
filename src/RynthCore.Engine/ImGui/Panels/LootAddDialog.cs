// ============================================================================
//  RynthCore.Engine - ImGui/Panels/LootAddDialog.cs
//  "Add to loot profile": the small popup that turns a clicked item into a
//  loot rule (2026-10-04). Opened from the Inventory panel's right-click menu
//  and from the Loot Editor's "Add selected item"; /ra loot add is the chat
//  twin (no popup, defaults straight in).
//
//    item      its name and the profile the rule goes to
//    match     name + class (default), name only, items like this
//    T11       "Include T11 attributes": also require the item's T11 tier,
//              grade, damage %, modifiers, slot special and Cast on Strike,
//              each at least this item's (any match; 2026-10-07)
//    action    Keep, Keep # (with the count), Salvage, Sell, Read
//    name      the rule's name (empty: RynthAi's default, "Keep Copper Pea")
//    preview   the rule in words, where it goes in the list and why, notes
//    Add       RynthAi inserts it, saves (old file kept as .bak) and reloads
//
//  RynthAi builds the rule (LootSdk LootItemRules) and does the insert and the
//  save; this face only posts item_preview / item_add / item_close through
//  LootEditCommands and shows LootEditStateDto.ItemDraft. A face owns one of
//  these and calls Draw once per frame outside any child window.
// ============================================================================

using System;
using System.Globalization;
using System.Numerics;
using ImGuiNET;
using RynthCore.Engine.UI.Data;
using static RynthCore.Engine.ImGuiBackend.Panels.FaceKit;

namespace RynthCore.Engine.ImGuiBackend.Panels;

internal sealed class LootAddDialog
{
    private static readonly string[] MatchLabels = { "Name + class", "Name only", "Items like this" };
    private static readonly string[] MatchTips =
    {
        "This exact item: its name and its kind (object class).",
        "Anything with this exact name.",
        "Its kind plus key properties: material, workmanship, slot, armor / damage, ratings and spells, at least as good as this one. No name.",
    };
    private static readonly int[] ActionIds =
        { LootEditVocabulary.Keep, LootEditVocabulary.KeepUpTo, LootEditVocabulary.Salvage, LootEditVocabulary.Sell, LootEditVocabulary.Read };
    private static readonly string[] ActionLabels = { "Keep", "Keep #", "Salvage", "Sell", "Read" };

    private readonly string _id;
    private bool _open, _openRequested, _subscribed;
    private uint _itemId;
    private string _itemName = string.Empty;
    private bool _toOpenProfile;

    // What the popup asks for (0 / -1 = RynthAi's default).
    private int _match, _action, _keep = -1;
    private bool _includeT11;
    private readonly byte[] _name = new byte[128];
    private int _keepEdit = 1;

    // Seqs are unique across popups (the inventory's and the editor's share RynthAi's one draft):
    // a popup only takes an answer to something it asked.
    private static int s_seq;
    private readonly System.Collections.Generic.HashSet<int> _mine = new();
    private int _seq;
    private DateTime _sentAt;
    private long _seenVersion = -1;
    private LootEditItemDraftDto? _draft;
    private bool _adding;
    private DateTime _addedAt;
    private bool _justAdded;

    /// <summary>True once after an add went through (the Loot Editor scrolls to the new rule).</summary>
    public bool TakeAdded()
    {
        bool v = _justAdded;
        _justAdded = false;
        return v;
    }

    public LootAddDialog(string id) => _id = id;

    public bool IsOpen => _open;

    /// <summary>
    /// Opens the popup for <paramref name="itemId"/> (0: the item selected in the
    /// game, resolved once). <paramref name="toOpenProfile"/>: the rule goes to the
    /// profile open in the Loot Editor, else to the one RynthAi loots with.
    /// </summary>
    public void Open(uint itemId, string itemName, bool toOpenProfile)
    {
        _itemId = itemId;
        _itemName = itemName ?? string.Empty;
        _toOpenProfile = toOpenProfile;
        _match = 0;
        _action = 0;
        _keep = -1;
        _includeT11 = false;
        Array.Clear(_name);
        _draft = null;
        _adding = false;
        _addedAt = default;
        _mine.Clear();
        if (!_subscribed)
        {
            UiSources.LootEdit.Subscribe();
            _subscribed = true;
        }
        _openRequested = true;
        Send("item_preview");
    }

    private void Send(string op)
    {
        _seq = System.Threading.Interlocked.Increment(ref s_seq);
        _mine.Add(_seq);
        _sentAt = DateTime.UtcNow;
        LootEditCommands.Send(new LootEditCmd
        {
            Op = op,
            Item = new LootEditItemRequestDto
            {
                ItemId = _itemId, Match = _match, Action = _action, KeepCount = _keep,
                RuleName = Utf8(_name).Trim(), ToOpenProfile = _toOpenProfile, IncludeT11 = _includeT11, Seq = _seq,
            },
        });
    }

    /// <summary>The owning face was hidden: drop the popup (and its subscription and draft).</summary>
    public void Cancel()
    {
        if (_open || _subscribed) Close();
        _openRequested = false;
    }

    private void Close()
    {
        if (_open || _subscribed) LootEditCommands.Simple("item_close");
        _open = false;
        if (_subscribed)
        {
            UiSources.LootEdit.Unsubscribe();
            _subscribed = false;
        }
    }

    /// <summary>Once per frame, outside any child window.</summary>
    public void Draw()
    {
        if (_openRequested)
        {
            _openRequested = false;
            _open = true;
            ImGuiNET.ImGui.SetNextWindowPos(ImGuiNET.ImGui.GetMousePos(), ImGuiCond.Always);
            ImGuiNET.ImGui.OpenPopup(_id);
        }
        if (!_open) return;
        TakeDraft();

        PushStyle();
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 8));
        bool visible = ImGuiNET.ImGui.BeginPopup(_id, ImGuiWindowFlags.AlwaysAutoResize);
        ImGuiNET.ImGui.PopStyleVar();
        if (!visible)
        {
            PopStyle();
            Close();   // clicked outside, or Esc
            return;
        }
        try
        {
            if (Body()) ImGuiNET.ImGui.CloseCurrentPopup();
        }
        finally
        {
            ImGuiNET.ImGui.EndPopup();
            PopStyle();
        }
    }

    private void TakeDraft()
    {
        var snap = UiSources.LootEdit.Current;
        if (snap == null || snap.Version == _seenVersion) return;
        _seenVersion = snap.Version;
        LootEditItemDraftDto? d = snap.Value.State?.ItemDraft;
        if (d == null || !_mine.Contains(d.Seq) || (_draft != null && d.Seq < _draft.Seq)) return;
        bool first = _draft == null;
        _draft = d;
        if (d.ItemId != 0) _itemId = d.ItemId;   // the selected item, from now on by id
        if (d.ItemName.Length > 0) _itemName = d.ItemName;
        if (d.Seq == _seq)
        {
            if (_adding)
            {
                _adding = false;
                if (d.Added)
                {
                    _addedAt = DateTime.UtcNow;
                    _justAdded = true;
                }
            }
            if (!ImGuiNET.ImGui.IsAnyItemActive() || first) _keepEdit = Math.Max(0, d.KeepCount);
        }
    }

    /// <summary>The popup's contents; true when it should close.</summary>
    private bool Body()
    {
        const float w = 380;
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Text, Amber);
        ImGuiNET.ImGui.TextUnformatted(PhosphorIcons.Plus + " Add to loot profile");
        ImGuiNET.ImGui.PopStyleColor();
        ImGuiNET.ImGui.TextUnformatted(_itemName.Length > 0 ? _itemName : "the selected item");
        ImGuiNET.ImGui.Separator();

        LootEditItemDraftDto? d = _draft;
        bool current = d != null && d.Seq == _seq;
        if (d == null)
        {
            bool slow = (DateTime.UtcNow - _sentAt).TotalSeconds > 3;
            ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Text, slow ? Amber : Mute);
            ImGuiNET.ImGui.PushTextWrapPos(ImGuiNET.ImGui.GetCursorPosX() + w);
            ImGuiNET.ImGui.TextUnformatted(slow
                ? "No answer from RynthAi. Is it loaded? (A RynthAi older than this feature doesn't answer; /ra loot add needs the new one too.)"
                : "Building the rule...");
            ImGuiNET.ImGui.PopTextWrapPos();
            ImGuiNET.ImGui.PopStyleColor();
            return CloseRow(w);
        }

        if (d.Added && _addedAt != default)
        {
            ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Text, Green);
            ImGuiNET.ImGui.PushTextWrapPos(ImGuiNET.ImGui.GetCursorPosX() + w);
            ImGuiNET.ImGui.TextUnformatted(d.Message + (d.TargetInUse ? ". RynthAi loots with it now." : " (not the profile RynthAi loots with)."));
            ImGuiNET.ImGui.PopTextWrapPos();
            ImGuiNET.ImGui.PopStyleColor();
            if ((DateTime.UtcNow - _addedAt).TotalSeconds > 2.5) return CloseNow();
            return CloseRow(w);
        }

        // Where it goes.
        if (d.TargetFile.Length > 0)
        {
            ImGuiNET.ImGui.TextDisabled("Profile:");
            ImGuiNET.ImGui.SameLine();
            ImGuiNET.ImGui.TextUnformatted(d.TargetFile + (d.TargetInUse ? "  (in use)" : "  (not the one RynthAi loots with)"));
            if (ImGuiNET.ImGui.IsItemHovered()) ImGuiNET.ImGui.SetTooltip(d.TargetPath);
        }

        bool changed = false;
        // Match.
        ImGuiNET.ImGui.TextDisabled("Match:");
        for (int i = 0; i < MatchLabels.Length; i++)
        {
            ImGuiNET.ImGui.SameLine();
            if (ImGuiNET.ImGui.RadioButton(MatchLabels[i] + "##m" + i.ToString(CultureInfo.InvariantCulture), d.Match == i) && d.Match != i)
            {
                _match = i;
                changed = true;
            }
            if (ImGuiNET.ImGui.IsItemHovered()) ImGuiNET.ImGui.SetTooltip(MatchTips[i]);
        }

        // T11 attributes. Shown for every item: a non-T11 item answers with a note saying so.
        bool t11 = _includeT11;
        if (ImGuiNET.ImGui.Checkbox("Include T11 attributes##la_t11", ref t11))
        {
            _includeT11 = t11;
            changed = true;
        }
        if (ImGuiNET.ImGui.IsItemHovered())
            ImGuiNET.ImGui.SetTooltip(
                "Also require this item's T11 tier (the server's stamped tier when it sends one), weapon grade,\n" +
                "damage %, gear grade, every modifier, slot special and Cast on Strike, each at least as good\n" +
                "as this one. Works with any match.\n" +
                "Not added: Can Wield (depends on your character), Zone Locked (depends on where it was assessed),\n" +
                "and property slots / Tainted (bag state, not quality).");
        if (!d.IsT11)
        {
            ImGuiNET.ImGui.SameLine();
            ImGuiNET.ImGui.TextDisabled("(not a T11 item)");
        }

        // Action.
        ImGuiNET.ImGui.TextDisabled("Action:");
        for (int i = 0; i < ActionIds.Length; i++)
        {
            ImGuiNET.ImGui.SameLine();
            if (ImGuiNET.ImGui.RadioButton(ActionLabels[i] + "##a" + i.ToString(CultureInfo.InvariantCulture), d.Action == ActionIds[i]) && d.Action != ActionIds[i])
            {
                _action = ActionIds[i];
                changed = true;
            }
        }
        if (d.Action == LootEditVocabulary.KeepUpTo)
        {
            ImGuiNET.ImGui.TextDisabled("Keep up to:");
            ImGuiNET.ImGui.SameLine();
            ImGuiNET.ImGui.SetNextItemWidth(110);
            ImGuiNET.ImGui.InputInt("##la_keep", ref _keepEdit, 1, 10);
            _keepEdit = Math.Max(0, _keepEdit);
            bool done = ImGuiNET.ImGui.IsItemDeactivatedAfterEdit() || (ImGuiNET.ImGui.IsItemEdited() && !ImGuiNET.ImGui.IsItemActive());
            if (done && _keepEdit != d.KeepCount)
            {
                _keep = _keepEdit;
                _action = LootEditVocabulary.KeepUpTo;
                changed = true;
            }
            ImGuiNET.ImGui.SameLine();
            ImGuiNET.ImGui.TextDisabled(d.Stackable ? "(counts stack sizes)" : "(items)");
        }

        // Name.
        ImGuiNET.ImGui.TextDisabled("Name:");
        ImGuiNET.ImGui.SameLine();
        ImGuiNET.ImGui.SetNextItemWidth(w - 50);
        ImGuiNET.ImGui.InputText("##la_name", _name, (uint)_name.Length);
        if (ImGuiNET.ImGui.IsItemDeactivatedAfterEdit()) changed = true;
        if (Utf8(_name).Trim().Length == 0 && d.DefaultRuleName.Length > 0)
            ImGuiNET.ImGui.TextDisabled("Left empty, the rule is named \"" + d.DefaultRuleName + "\".");

        // Preview.
        ImGuiNET.ImGui.Separator();
        ImGuiNET.ImGui.PushTextWrapPos(ImGuiNET.ImGui.GetCursorPosX() + w);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Text, current ? Teal : Faded(Teal));
        foreach (string line in d.Preview) ImGuiNET.ImGui.TextUnformatted(line);
        ImGuiNET.ImGui.PopStyleColor();
        if (d.OrderNote.Length > 0)
        {
            ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Text, Mute);
            ImGuiNET.ImGui.TextUnformatted(d.OrderNote);
            ImGuiNET.ImGui.PopStyleColor();
        }
        if (d.Notes.Count > 0)
        {
            ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Text, Amber);
            foreach (string n in d.Notes) ImGuiNET.ImGui.TextUnformatted(n);
            ImGuiNET.ImGui.PopStyleColor();
        }
        if (!d.Ok && d.Error.Length > 0)
        {
            ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Text, Red);
            ImGuiNET.ImGui.TextUnformatted(d.Error);
            ImGuiNET.ImGui.PopStyleColor();
        }
        ImGuiNET.ImGui.PopTextWrapPos();

        if (changed)
        {
            Send("item_preview");   // Add waits for the answer (the preview shows faded until then)
            current = false;
        }

        // Add / Cancel.
        Vector2 bp = ImGuiNET.ImGui.GetCursorScreenPos() + new Vector2(0, 4);
        bool canAdd = current && d.Ok && !_adding;
        string addLabel = _adding ? "Adding..." : PhosphorIcons.Plus + " Add rule";
        if (Button("##la_add", addLabel, bp, new Vector2(110, 24), canAdd ? Green : Mute, canAdd ? StartBg : BtnFill, canAdd))
        {
            _adding = true;
            Send("item_add");
        }
        if (ImGuiNET.ImGui.IsItemHovered())
            ImGuiNET.ImGui.SetTooltip(d.InsertAt >= 0
                ? $"Insert at {d.InsertAt + 1} of {d.RuleCount + 1}, save {d.TargetFile} (old file kept as .bak) and reload it"
                : "Add the rule");
        if (Button("##la_cancel", "Cancel", bp + new Vector2(116, 0), new Vector2(80, 24), Text, BtnFill))
            return CloseNow();
        ImGuiNET.ImGui.SetCursorScreenPos(bp + new Vector2(0, 28));
        ImGuiNET.ImGui.Dummy(new Vector2(w, 0));
        return false;
    }

    private bool CloseRow(float w)
    {
        Vector2 bp = ImGuiNET.ImGui.GetCursorScreenPos() + new Vector2(0, 4);
        bool close = Button("##la_close", "Close", bp, new Vector2(80, 24), Text, BtnFill);
        ImGuiNET.ImGui.SetCursorScreenPos(bp + new Vector2(0, 28));
        ImGuiNET.ImGui.Dummy(new Vector2(w, 0));
        return close && CloseNow();
    }

    private bool CloseNow()
    {
        Close();
        return true;
    }

    private static void PushStyle()
    {
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.PopupBg, ShellBg);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Border, Teal);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Text, Text);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.FrameBg, BtnFill);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Lighten(BtnFill));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.CheckMark, Teal);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Separator, BtnBord);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 4f);
    }

    private static void PopStyle()
    {
        ImGuiNET.ImGui.PopStyleVar();
        ImGuiNET.ImGui.PopStyleColor(7);
    }
}
