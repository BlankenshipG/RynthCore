// ============================================================================
//  RynthCore.Engine - ImGui/Panels/MetaFace.Editor.cs
//  The Meta panel's rule editor and its state pickers (2026-10-05 clean-up):
//
//    [<- Back]  Edit rule                                    Saved
//    State: [Combat                                       v]
//    +- Conditions ------ [+ Add] -+  +- Actions --------- [+ Add] -+
//    | [Landblock ==            v] |  | [Chat Command           v] |
//    | [A9B40000     ][Set current] |  | [/say hello               ] |
//    +-----------------------------+  +-----------------------------+
//    [Revert]                                          [Delete rule]
//
//  Conditions and Actions sit side by side, each scrolling on its own; under
//  SideBySideMinWidth they stack. "+ Add" turns a single condition into the
//  first of an All (a single action into the first of a DoAll), or adds one
//  more to an All / Any / DoAll.
//
//  Saving: every change marks the rule dirty (MetaAutosave); 0.75 s after the
//  last change, and never while a text box is being typed in, the rule goes
//  to RynthAi as add_rule (a new rule, the first time, and only once its
//  condition and action are filled in: MetaRuleEdit.ReadyToAdd) or
//  update_rule, and RynthAi writes the file. Back, closing the panel and switching metas save
//  at once. update_rule keeps the running rule's fired latch (RynthAi
//  MetaRuleEdits), so a save never re-fires a rule, restarts the state's
//  clocks or changes the current state.
//
//  New states: every state picker (the rule's State, Set / Call Meta State
//  targets, the bottom bar) ends with "+ New state...". Picking it turns the
//  picker into a name box with OK / Cancel (Enter / Esc); the name is trimmed,
//  must not be empty, contain { } or match an existing state (ignoring case,
//  as RynthAi does). A state exists only while a rule uses it (RynthAi lists
//  its rules' states; the .af file has a STATE: block per state with rules),
//  so a new state is never left empty:
//   - the rule's own State picker moves this rule into it;
//   - a Set / Call Meta State target and the bottom bar add an empty
//     Never -> None rule in it at once (it never fires; edit or delete it).
//     The bottom bar also makes it the current state, like any state picked
//     there, and opens that rule.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.UI.Data;

namespace RynthCore.Engine.ImGuiBackend.Panels;

internal sealed partial class MetaFace
{
    /// <summary>Under this width Conditions and Actions stack instead of sitting side by side.</summary>
    private const float SideBySideMinWidth = 400;

    private MetaRuleDto? _editOriginal;   // as opened (Revert); null for a new rule
    private MetaRuleDto? _lastSaved;      // as last sent to RynthAi; finds the rule again by content
    private bool _editChanged;            // set by this frame's widgets
    private bool _typingNow, _editTyping; // a data box is being typed in (this frame / last frame)
    private bool _editConfirmDelete;
    private string _editFile = string.Empty;   // the meta file the open rule belongs to
    private string _listNote = string.Empty;   // shown over the rule list for a few seconds
    private long _listNoteAt;

    // The one state picker turned into a name box (ImGui id, 0 = none).
    private uint _namingKey;
    private readonly byte[] _nameBuf = new byte[64];
    private bool _nameFocus, _nameWasActive;
    private string? _nameProblem;
    private const string NewStateEntry = PhosphorIcons.Plus + " New state...";

    private const string BackLabel = PhosphorIcons.ArrowLeft + " Back";
    private const string AddLabel = PhosphorIcons.Plus + " Add";
    private const string SetCurrentLabel = PhosphorIcons.MapPin + " Set current";

    // =====================================================================
    //  Open / save / leave
    // =====================================================================

    private void OpenEditor(int index, MetaRuleDto rule)
    {
        FlushEdit();
        _editIndex = index;
        _editFile = EditFileOf(_data.CurrentMetaPath);
        _editRule = index >= 0 ? rule.Clone() : rule;
        _editOriginal = index >= 0 ? rule.Clone() : null;
        _lastSaved = index >= 0 ? rule.Clone() : null;
        _autosave.Clear();
        _fields.Clear();
        _editConfirmDelete = false;
        _editTyping = false;
        CancelNaming();
        _view = View.Editor;
    }

    /// <summary>
    /// Another meta was loaded while a rule was open (the Meta Manager, a chat command): the rule's
    /// index means nothing in that meta, so the editor closes without saving into it. RynthAi
    /// saving a .met as its .af is the same meta.
    /// </summary>
    private void EditorGuard()
    {
        if (string.Equals(_editFile, EditFileOf(_data.CurrentMetaPath), StringComparison.OrdinalIgnoreCase)) return;
        bool lost = _autosave.Dirty;
        _autosave.Clear();
        CancelNaming();
        _view = View.List;
        _listNote = lost
            ? "Another meta was loaded while you edited a rule; your last change to it was not saved."
            : "Another meta was loaded, so the rule editor closed.";
        _listNoteAt = NowMs;
    }

    private static string EditFileOf(string? metaPath) =>
        string.IsNullOrEmpty(metaPath) ? string.Empty : MetaBackupPlan.WrittenFile(metaPath);

    private void LeaveEditor()
    {
        FlushEdit();
        CancelNaming();
        _view = View.List;
    }

    /// <summary>Saves a pending edit now (Back, panel closed, meta switch).</summary>
    private void FlushEdit()
    {
        if (_view == View.Editor && _autosave.Dirty) CommitEdit();
    }

    /// <summary>Sends the edited rule: add_rule the first time for a new rule, then update_rule.</summary>
    private void CommitEdit()
    {
        if (!string.Equals(_editFile, EditFileOf(_data.CurrentMetaPath), StringComparison.OrdinalIgnoreCase))
        {
            EditorGuard();   // never save into a meta loaded meanwhile
            return;
        }
        if (!_autosave.Take()) return;
        MetaRuleDto send = _editRule.Clone();
        if (_editIndex < 0)
        {
            _editIndex = _data.Rules.Count;   // add_rule appends
            SendEdit(new MetaCmd { Op = "add_rule", Rule = send });
            _data.Rules.Add(send.Clone());
        }
        else
        {
            // Commands address rules by index: find the rule again in case the snapshot moved it.
            if (_lastSaved != null) _editIndex = MetaRuleEdit.Locate(_data.Rules, _lastSaved, _editIndex);
            SendEdit(new MetaCmd { Op = "update_rule", Index = _editIndex, Rule = send });
            if (_editIndex < _data.Rules.Count) _data.Rules[_editIndex] = send.Clone();
        }
        _lastSaved = send.Clone();
        RebuildCaches();
    }

    private void DeleteEditedRule()
    {
        _autosave.Clear();
        if (_editIndex >= 0)
        {
            int index = _lastSaved != null ? MetaRuleEdit.Locate(_data.Rules, _lastSaved, _editIndex) : _editIndex;
            if (index >= 0 && index < _data.Rules.Count)
            {
                SendEdit(new MetaCmd { Op = "delete_rule", Index = index });
                _data.Rules.RemoveAt(index);
                _expanded.Clear();
                RebuildCaches();
            }
        }
        CancelNaming();
        _view = View.List;
    }

    // =====================================================================
    //  Editor view
    // =====================================================================

    private void EditorView(Vector2 size)
    {
        _editChanged = false;
        _typingNow = false;
        MetaRuleDto r = _editRule;
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        Vector2 top = ImGuiNET.ImGui.GetCursorScreenPos();
        float w = size.X;

        // [<- Back]  New rule / Edit rule                Saved
        float backW = BtnW(BackLabel, 60);
        if (Button(BackLabel + "##back", top, new Vector2(backW, 22), Mute, BtnFill, BtnBord))
        {
            LeaveEditor();
            return;
        }
        ImGuiNET.ImGui.SetItemTooltip("Back to the rule list (saves this rule)");
        float indW = SaveIndicatorWidth(editor: true);
        ImFontPtr bold = ImGuiFonts.Get(UiFont.UiBold11);
        float tx = top.X + backW + 8;
        string title = _editIndex < 0 ? "New rule" : "Edit rule";
        ImGuiNET.ImGui.PushFont(bold);
        dl.AddText(new Vector2(tx, top.Y + (22 - bold.FontSize) * 0.5f), Teal, Fit(title, Math.Max(10, top.X + w - indW - 8 - tx)));
        ImGuiNET.ImGui.PopFont();
        SaveIndicator(new Vector2(top.X + w - indW, top.Y), indW, 22);

        // State: [picker]
        float y = top.Y + 26;
        float lw = ImGuiNET.ImGui.CalcTextSize("State:").X + 6;
        dl.AddText(new Vector2(top.X, y + (22 - ImGuiNET.ImGui.GetFontSize()) * 0.5f), Mute, "State:");
        string? state = StatePicker("##ed_state", r.State, _statePicks, IndexOf(_statePicks, r.State),
            new Vector2(top.X + lw, y), w - lw, out _);
        if (state != null && state != r.State)
        {
            r.State = state;   // a new state is made by moving this rule into it
            _editChanged = true;
        }
        y += 22 + ProblemLine("##ed_state", new Vector2(top.X + lw, y + 22), w - lw) + 6;

        // Conditions | Actions
        const float footerH = 24;
        float colsH = Math.Max(60, top.Y + size.Y - y - footerH - 6);
        if (w >= SideBySideMinWidth)
        {
            float cw = (w - 6) * 0.5f;
            Column("##ed_conds", new Vector2(top.X, y), new Vector2(cw, colsH), actions: false);
            Column("##ed_acts", new Vector2(top.X + cw + 6, y), new Vector2(w - cw - 6, colsH), actions: true);
        }
        else
        {
            float ch = (colsH - 6) * 0.5f;
            Column("##ed_conds", new Vector2(top.X, y), new Vector2(w, ch), actions: false);
            Column("##ed_acts", new Vector2(top.X, y + ch + 6), new Vector2(w, colsH - ch - 6), actions: true);
        }

        EditorFooter(new Vector2(top.X, y + colsH + 6), w, footerH);
        ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(top.X, y + colsH + 6 + footerH));
        ImGuiNET.ImGui.Dummy(new Vector2(0, 0));

        if (_editChanged)
        {
            _autosave.Changed(NowMs);
            _editConfirmDelete = false;
        }
        _editTyping = _typingNow;
    }

    /// <summary>A bordered column: a fixed header (title, "+ Add") over a body that scrolls on its own.</summary>
    private void Column(string id, Vector2 pos, Vector2 size, bool actions)
    {
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        MetaRuleDto r = _editRule;
        const float hh = 24;
        dl.AddRectFilled(pos, pos + size, PanelBg, 3);
        dl.AddRectFilled(pos, pos + new Vector2(size.X, hh), StateHdrA, 3);
        dl.AddRect(pos, pos + size, BtnBord, 3);
        ImFontPtr bold = ImGuiFonts.Get(UiFont.UiBold11);
        dl.AddText(bold, bold.FontSize, pos + new Vector2(7, (hh - bold.FontSize) * 0.5f), Amber, actions ? "Actions" : "Conditions");

        bool canAdd = actions || MetaRuleEdit.CanAddCondition(r);
        float addW = BtnW(AddLabel, 52);
        if (Button(AddLabel + id + "_add", new Vector2(pos.X + size.X - addW - 3, pos.Y + 2), new Vector2(addW, hh - 4), Teal, BtnFill, BtnBord,
                enabled: canAdd))
        {
            if (actions) MetaRuleEdit.AddAction(r); else MetaRuleEdit.AddCondition(r);
            _editChanged = true;
        }
        SafeImGui.SetItemTooltip(actions
            ? (r.Action == MetaRuleEdit.DoAll ? "Add one more action" : "Add another action: this one and the new one run in order (All)")
            : !canAdd ? "A Not has one condition"
            : r.Condition is MetaRuleEdit.All or MetaRuleEdit.Any or MetaRuleEdit.Not ? "Add one more condition"
            : "Add another condition: the rule fires when all of them are true (All)");

        ImGuiNET.ImGui.SetCursorScreenPos(pos + new Vector2(5, hh + 4));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.ChildBg, 0);
        ImGuiNET.ImGui.BeginChild(id, new Vector2(Math.Max(10, size.X - 10), Math.Max(10, size.Y - hh - 8)));
        ImGuiNET.ImGui.PopStyleColor();
        float cw = Math.Max(40, ImGuiNET.ImGui.GetContentRegionAvail().X - 2);
        if (actions) ActionsBody(r, cw); else ConditionsBody(r, cw);
        ImGuiNET.ImGui.EndChild();
    }

    private void ConditionsBody(MetaRuleDto r, float w)
    {
        int picked = FlowPicker("##ed_cond", MetaVocabulary.ConditionName(r.Condition), MetaVocabulary.ConditionNames, r.Condition, w);
        if (picked >= 0 && picked != r.Condition)
        {
            SetCondition(r, picked);
            _editChanged = true;
        }
        if (!MetaVocabulary.IsCompositeCondition(r.Condition))
        {
            ConditionField("##ed_cdata", r, w, 22);
            return;
        }
        SmallLabel(r.Condition switch
        {
            MetaRuleEdit.Not => "Not: true while this one is false",
            MetaRuleEdit.All => "All of these:",
            _ => "Any of these:",
        });
        if (r.Children.Count == 0) Note("None yet: + Add (top right).");
        SubRules(r.Children, action: false, 0, r.Condition == MetaRuleEdit.Not ? 1 : int.MaxValue, w);
    }

    private void ActionsBody(MetaRuleDto r, float w)
    {
        int picked = FlowPicker("##ed_act", MetaVocabulary.ActionName(r.Action), MetaVocabulary.ActionNames, r.Action, w);
        if (picked >= 0 && picked != r.Action)
        {
            r.Action = picked;
            if (!MetaVocabulary.IsAllAction(picked)) r.ActionChildren.Clear();
            _editChanged = true;
        }
        if (!MetaVocabulary.IsAllAction(r.Action))
        {
            ActionField("##ed_adata", r, w, 22, problemLine: true);
            return;
        }
        SmallLabel("All of these, in order:");
        if (r.ActionChildren.Count == 0) Note("None yet: + Add (top right).");
        SubRules(r.ActionChildren, action: true, 0, int.MaxValue, w);
    }

    private static void SetCondition(MetaRuleDto c, int picked)
    {
        c.Condition = picked;
        if (!MetaVocabulary.IsCompositeCondition(picked)) c.Children.Clear();
        else if (picked == MetaRuleEdit.Not && c.Children.Count > 1) c.Children.RemoveRange(1, c.Children.Count - 1);
    }

    /// <summary>A condition's value at the cursor: a text box (with Set current for landblock / landcell),
    /// or a note when the condition takes nothing. Leaves the cursor under it.</summary>
    private void ConditionField(string id, MetaRuleDto c, float width, float height)
    {
        Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
        if (!MetaRuleEdit.ConditionTakesData(c.Condition) && c.ConditionData.Length == 0)
            NoteAt(p, "Nothing to set.", height);
        else if (MetaLocation.TakesPosition(c.Condition))
        {
            // "Set current" fills in where the player is, from the engine's per-frame pose copy
            // (no call into AC from here), in the 8-digit form RynthAi and VTank both read.
            float bw = Math.Min(BtnW(SetCurrentLabel, 70), width * 0.5f);
            float boxW = Math.Max(30, width - bw - 4);
            ImGuiNET.ImGui.SetCursorScreenPos(p);
            DataBox(id, c, action: false, MetaVocabulary.ConditionHint(c.Condition), boxW, height);
            string? here = PlayerPhysicsHooks.TryGetPlayerPoseSnapshot(out uint cell, out _, out _, out _, out _, out _, out _, out _) && cell != 0
                ? MetaLocation.Text(c.Condition, cell) : null;
            if (Button((bw >= BtnW(SetCurrentLabel, 0) ? SetCurrentLabel : PhosphorIcons.MapPin) + id + "_here",
                    new Vector2(p.X + width - bw, p.Y), new Vector2(bw, height), Teal, BtnFill, BtnBord, enabled: here != null) && here != null)
            {
                c.ConditionData = here;
                _editChanged = true;
            }
            SafeImGui.SetItemTooltip(here != null
                ? $"Set to where you are now: {here}"
                : "Your position isn't known right now (portal space, or not in the world).");
        }
        else
        {
            ImGuiNET.ImGui.SetCursorScreenPos(p);
            DataBox(id, c, action: false, MetaVocabulary.ConditionHint(c.Condition), width, height);
            if (c.Condition == MetaRuleEdit.NoKillsWithinMinutes)
                SafeImGui.SetItemTooltip(MetaRuleEdit.NoKillsTooltip(c.ConditionData).Replace("%", "%%"));   // a format string: typed text may hold %
        }
        ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(p.X, p.Y + height));
        ImGuiNET.ImGui.Dummy(new Vector2(0, 0));
    }

    /// <summary>An action's value at the cursor: a state picker (Set / Call Meta State), the route picker
    /// (Embedded Nav Route), a text box, or a note. Leaves the cursor under it.</summary>
    private void ActionField(string id, MetaRuleDto a, float width, float height, bool problemLine)
    {
        Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
        float extra = 0;
        if (a.Action is 2 or 5)   // Set / Call Meta State
        {
            string? target = StatePicker(id, a.ActionData.Length == 0 ? "Select state..." : a.ActionData, _statesOnly,
                IndexOf(_statesOnly, a.ActionData), p, width, out bool isNew, height);
            if (target != null)
            {
                if (isNew) CreateState(target, openIt: false);   // an empty first rule keeps it
                if (target != a.ActionData)
                {
                    a.ActionData = target;
                    _editChanged = true;
                }
            }
            if (problemLine) extra = ProblemLine(id, new Vector2(p.X, p.Y + height), width);
        }
        else if (a.Action == 3)   // Embedded Nav Route
        {
            int picked = Picker(id, a.ActionData.Length == 0 ? "Select route..." : a.ActionData, _routePicks,
                Array.IndexOf(_routePicks, a.ActionData), p, width, height);
            if (picked >= 0 && _routePicks[picked] != a.ActionData)
            {
                a.ActionData = _routePicks[picked];
                _editChanged = true;
            }
        }
        else if (!MetaRuleEdit.ActionTakesData(a.Action) && a.ActionData.Length == 0)
            NoteAt(p, "Nothing to set.", height);
        else
        {
            ImGuiNET.ImGui.SetCursorScreenPos(p);
            DataBox(id, a, action: true, MetaVocabulary.ActionHint(a.Action), width, height);
        }
        ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(p.X, p.Y + height + extra));
        ImGuiNET.ImGui.Dummy(new Vector2(0, 0));
    }

    // Sub-conditions nest (All/Any/Not under All/Any/Not, MaxSubConditionDepth levels; a Not takes
    // one); DoAll's sub-actions are a flat list. Each row: [type][value][x]; in a narrow column the
    // value goes on a second line. The top level's "+ Add" is in the column header.
    private void SubRules(List<MetaRuleDto> subs, bool action, int depth, int maxCount, float w)
    {
        string[] names = action ? MetaVocabulary.ActionNames : MetaVocabulary.ConditionNames;
        bool narrow = w < 300;
        const float rh = 20, xw = 20;
        for (int i = 0; i < subs.Count; i++)
        {
            MetaRuleDto sub = subs[i];
            ImGuiNET.ImGui.PushID(i);
            Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
            float pickW = narrow ? w - xw - 4 : (w - xw - 8) * 0.45f;

            int current = action ? sub.Action : sub.Condition;
            string shown = action ? MetaVocabulary.ActionName(sub.Action) : MetaVocabulary.ConditionName(sub.Condition);
            int picked = Picker("##type", shown, names, current, p, pickW, rh);
            if (picked >= 0 && picked != current)
            {
                if (action) sub.Action = picked; else SetCondition(sub, picked);
                _editChanged = true;
            }

            bool nests = !action && MetaVocabulary.IsCompositeCondition(sub.Condition) && depth < MetaVocabulary.MaxSubConditionDepth;
            bool removed = IconButton("##rm", PhosphorIcons.X, new Vector2(p.X + w - xw, p.Y), new Vector2(xw, rh), TextDim, DelFill, BtnBord);
            SafeImGui.SetItemTooltip(action ? "Remove this action" : "Remove this condition");

            float rowH = rh;
            if (nests)
            {
                if (!narrow)
                    NoteAt(new Vector2(p.X + pickW + 6, p.Y), sub.Condition == MetaRuleEdit.Not ? "(the condition below)" : "(the conditions below)", rh);
            }
            else
            {
                Vector2 vp = narrow ? new Vector2(p.X + 12, p.Y + rh + 2) : new Vector2(p.X + pickW + 4, p.Y);
                float vw = narrow ? w - 12 : w - pickW - 4 - xw - 4;
                ImGuiNET.ImGui.SetCursorScreenPos(vp);
                if (action) ActionField("##data", sub, vw, rh, problemLine: false);
                else ConditionField("##data", sub, vw, rh);
                if (narrow) rowH = rh * 2 + 2;
            }
            ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(p.X, p.Y + rowH + 2));
            ImGuiNET.ImGui.Dummy(new Vector2(0, 0));

            if (!removed && nests)
            {
                ImGuiNET.ImGui.Indent(14);
                SmallLabel(sub.Condition switch { MetaRuleEdit.Not => "Not:", MetaRuleEdit.All => "All of:", _ => "Any of:" });
                SubRules(sub.Children, action: false, depth + 1, sub.Condition == MetaRuleEdit.Not ? 1 : int.MaxValue, w - 14);
                ImGuiNET.ImGui.Unindent(14);
            }
            ImGuiNET.ImGui.PopID();
            if (removed)
            {
                subs.RemoveAt(i);
                _editChanged = true;
                break;   // indices shifted: the rest draws next frame
            }
        }

        // Nested groups get their own Add; the top level's is in the column header.
        if (depth == 0 || subs.Count >= maxCount) return;
        ImGuiNET.ImGui.PushID(depth * 1000 + subs.Count);
        if (FlowButton(AddLabel + "##add_sub", Teal, BtnFill, BtnBord, height: 20))
        {
            subs.Add(new MetaRuleDto { State = _editRule.State, Condition = MetaRuleEdit.Never });
            _editChanged = true;
        }
        ImGuiNET.ImGui.PopID();
    }

    /// <summary>[Revert] on the left (once something changed); Delete on its own at the far right.</summary>
    private void EditorFooter(Vector2 p, float w, float h)
    {
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        bool canRevert = _editOriginal != null && !MetaRuleEdit.SameContent(_editRule, _editOriginal);
        if (canRevert)
        {
            string label = PhosphorIcons.ArrowCounterClockwise + " Revert";
            if (Button(label + "##ed_revert", p, new Vector2(BtnW(label, 60), h), Mute, BtnFill, BtnBord))
            {
                _editRule = _editOriginal!.Clone();
                _fields.Clear();
                _editChanged = true;
            }
            ImGuiNET.ImGui.SetItemTooltip("Undo every change since you opened this rule (and save that)");
        }
        else
        {
            ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.Ui9));
            dl.AddText(new Vector2(p.X, p.Y + (h - ImGuiNET.ImGui.GetFontSize()) * 0.5f), Faded(Mute),
                _editIndex < 0 ? "Added once it is filled in, or on Back." : "Changes save on their own.");
            ImGuiNET.ImGui.PopFont();
        }

        bool draft = _editIndex < 0;
        if (!_editConfirmDelete)
        {
            string label = PhosphorIcons.Trash + (draft ? " Discard" : " Delete rule");
            float dw = BtnW(label, 70);
            if (Button(label + "##ed_del", new Vector2(p.X + w - dw, p.Y), new Vector2(dw, h), TextDim, DelFill, BtnBord))
            {
                if (draft) { _autosave.Clear(); LeaveEditor(); }
                else _editConfirmDelete = true;
            }
            SafeImGui.SetItemTooltip(draft ? "Drop this new rule (it was never added)" : "Delete this rule (asks first)");
        }
        else
        {
            float kw = BtnW("Keep", 50), yw = BtnW("Delete", 56);
            dl.AddText(new Vector2(p.X + w - kw - yw - 8 - ImGuiNET.ImGui.CalcTextSize("Delete this rule?").X - 6,
                p.Y + (h - ImGuiNET.ImGui.GetFontSize()) * 0.5f), Red, "Delete this rule?");
            if (Button("Delete##ed_del_yes", new Vector2(p.X + w - kw - yw - 4, p.Y), new Vector2(yw, h), TextDim, DelFill, Red))
            {
                DeleteEditedRule();
                return;
            }
            if (Button("Keep##ed_del_no", new Vector2(p.X + w - kw, p.Y), new Vector2(kw, h), Mute, BtnFill, BtnBord))
                _editConfirmDelete = false;
        }
    }

    // =====================================================================
    //  State pickers and new states
    // =====================================================================

    /// <summary>
    /// A state picker ending with "+ New state...". Returns the picked state, or a new state's name
    /// (<paramref name="isNew"/> true) once its name box is confirmed; else null.
    /// </summary>
    private string? StatePicker(string id, string shown, string[] items, int selected, Vector2 pos, float width, out bool isNew,
        float height = 22)
    {
        isNew = false;
        uint key = ImGuiNET.ImGui.GetID(id);
        if (_namingKey == key) return NamingBox(id, pos, width, height, out isNew);
        int picked = Picker(id, shown, items, selected, pos, width, height, NewStateEntry);
        if (picked == items.Length)
        {
            _namingKey = key;
            Array.Clear(_nameBuf);
            _nameFocus = true;
            _nameWasActive = false;
            _nameProblem = null;
            return null;
        }
        return picked >= 0 ? items[picked] : null;
    }

    /// <summary>The name box a state picker turns into: [name][OK][Cancel]; Enter = OK, Esc = Cancel.</summary>
    private string? NamingBox(string id, Vector2 pos, float width, float height, out bool isNew)
    {
        isNew = false;
        float bw = height + 4;
        float boxW = Math.Max(30, width - 2 * (bw + 3));
        ImGuiNET.ImGui.SetCursorScreenPos(pos);
        if (_nameFocus)
        {
            ImGuiNET.ImGui.SetKeyboardFocusHere();
            _nameFocus = false;
        }
        bool enter = TextBox(id + "_name", _nameBuf, boxW, "New state name...", ImGuiInputTextFlags.EnterReturnsTrue, out bool active, height);
        // Esc deactivates the box in the same frame, so "was it being typed in" is last frame's.
        bool esc = (active || _nameWasActive) && ImGuiNET.ImGui.IsKeyPressed(ImGuiKey.Escape, false);
        _nameWasActive = active;

        string typed = Utf8(_nameBuf);
        _nameProblem = MetaStateName.Problem(typed, _statePicks, out string name);
        if (_nameProblem != null && name.Length > 0)
            ImGuiNET.ImGui.GetWindowDrawList().AddRect(pos, pos + new Vector2(boxW, height), Red, 3);
        bool ok = IconButton(id + "_ok", PhosphorIcons.Check, new Vector2(pos.X + boxW + 3, pos.Y), new Vector2(bw, height), Green, NewFill, Green,
            enabled: _nameProblem == null);
        SafeImGui.SetItemTooltip(_nameProblem ?? $"Make the state '{name}' (Enter)");
        bool cancel = IconButton(id + "_cancel", PhosphorIcons.X, new Vector2(pos.X + boxW + bw + 6, pos.Y), new Vector2(bw, height),
            TextDim, BtnFill, BtnBord);
        ImGuiNET.ImGui.SetItemTooltip("Cancel (Esc)");
        ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(pos.X, pos.Y + height));
        ImGuiNET.ImGui.Dummy(new Vector2(0, 0));

        if ((enter || ok) && _nameProblem == null)
        {
            CancelNaming();
            AddLocalState(name);
            isNew = true;
            return name;
        }
        if (enter) _nameFocus = true;   // a bad name: keep typing (the reason shows under it)
        if (cancel || esc) CancelNaming();
        return null;
    }

    /// <summary>The name box's problem under a picker in the editor's flow; its height (0 when none).</summary>
    private float ProblemLine(string id, Vector2 pos, float width)
    {
        if (_namingKey == 0 || _namingKey != ImGuiNET.ImGui.GetID(id) || _nameProblem == null || Utf8(_nameBuf).Trim().Length == 0)
            return 0;
        ImFontPtr small = ImGuiFonts.Get(UiFont.Ui9);
        ImGuiNET.ImGui.PushFont(small);
        ImGuiNET.ImGui.GetWindowDrawList().AddText(pos + new Vector2(2, 1), Red, Fit(_nameProblem, width - 4));
        ImGuiNET.ImGui.PopFont();
        return small.FontSize + 3;
    }

    private void CancelNaming()
    {
        _namingKey = 0;
        _nameProblem = null;
    }

    /// <summary>Offers <paramref name="name"/> in every state picker until the snapshot lists it.</summary>
    private void AddLocalState(string name)
    {
        _localStates.Add(name);
        if (IndexOf(_statePicks, name) < 0) _statePicks = With(_statePicks, name);
        if (IndexOf(_statesOnly, name) < 0) _statesOnly = With(_statesOnly, name);
    }

    /// <summary>
    /// Makes state <paramref name="name"/> with its first rule, an empty Never -> None (it never fires),
    /// added at once: a state exists only while a rule uses it, so this is what keeps it through a
    /// save and reload. <paramref name="openIt"/> opens that rule in the editor.
    /// </summary>
    private void CreateState(string name, bool openIt)
    {
        AddLocalState(name);
        MetaRuleDto first = MetaRuleEdit.Placeholder(name);
        int index = _data.Rules.Count;   // add_rule appends
        SendEdit(new MetaCmd { Op = "add_rule", Rule = first.Clone() });
        _data.Rules.Add(first.Clone());
        RebuildCaches();
        if (openIt) OpenEditor(index, first);
    }

    // ── Small helpers ────────────────────────────────────────────────────

    private static int IndexOf(string[] items, string value)
    {
        for (int i = 0; i < items.Length; i++)
            if (string.Equals(items[i], value, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static void SmallLabel(string text)
    {
        ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.Ui9));
        SafeImGui.TextColored(RynthTheme.Vec(0xFFE8B333), text);
        ImGuiNET.ImGui.PopFont();
    }

    private static void Note(string text)
    {
        ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.Ui9));
        SafeImGui.TextColored(RynthTheme.Vec(0xFFC8D6E4), text);
        ImGuiNET.ImGui.PopFont();
    }

    private static void NoteAt(Vector2 p, string text, float height)
    {
        ImGuiNET.ImGui.GetWindowDrawList().AddText(new Vector2(p.X + 4, p.Y + (height - ImGuiNET.ImGui.GetFontSize()) * 0.5f), Faded(Mute), text);
    }
}
