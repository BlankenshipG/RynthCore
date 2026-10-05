// ============================================================================
//  RynthCore.Engine - ImGui/Panels/MetaFace.cs
//  ImGui face of the Meta panel (docs/IMGUI_PARITY_PLAN.md §2.13), with the
//  Avalonia face's look and three views:
//
//   List    file picker / refresh / Save / Save As; Visual|Source; Meta, Debug;
//           rules grouped by state (collapsible, red while firing), rows with
//           enable, expand, up/down within the state, duplicate, delete; click
//           a row to edit it; State picker and New Rule at the bottom.
//   Editor  state, condition (with nested All/Any/Not sub-conditions), action
//           (state and route pickers, DoAll sub-actions); Add/Save, Cancel.
//   Source  the .af text in a coloured editor (CodeEditor) with Ctrl+Space
//           completion (and after "IF: " etc.); Apply shows the plugin's real
//           result; unapplied edits survive switching views; Revert.
//
//  Data: UiSources.Meta (hub snapshot) and MetaCommands. List edits change a
//  private copy at once and the next snapshot (taken after the plugin applied
//  the command) replaces it.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using ImGuiNET;
using RynthCore.Engine.UI.Data;

namespace RynthCore.Engine.ImGuiBackend.Panels;

internal sealed partial class MetaFace : IImGuiPanel
{
    /// <summary>A large face that changes on its own only when the meta state moves: 4 Hz idle when popped out.</summary>
    public int PopOutIdleHz => 4;

    public const string Title = "Meta";

    public static void Register() => ImGuiPanelHost.Register(Title,
        new PanelSpec(new Vector2(440, 540), new Vector2(380, 300), EdgeToEdge: true, GripInBody: true),
        () => new MetaFace());

    // ── Palette (MetaPanel's, ARGB) ─────────────────────────────────────
    private static readonly uint Teal = C(0xFF26D9E6), Amber = C(0xFFE8B333), Green = C(0xFF33CC66),
        Red = C(0xFFE04848), Mute = C(0xFFC8D6E4), TextDim = C(0xFFF2F7FC), ShellBg = C(0xFF0A0F14),
        PanelBg = C(0xFF141F29), RowAlt = C(0xFF101822), BtnFill = C(0xFF16283A), BtnBord = C(0xFF34587A),
        Fired = C(0xFF3A1212), BtnOn = C(0xFF0E2E3A), StateHdrA = C(0xFF0E1E30), StateHdrB = C(0xFF162A40),
        DelFill = C(0xFF7A1414), NewFill = C(0xFF0E2E1A), PickSel = C(0xFF1A2E42);
    private static uint C(uint argb) => RynthTheme.Argb(argb);
    private static uint Faded(uint abgr) => (abgr & 0x00FFFFFF) | ((abgr >> 25) << 24);   // half alpha

    private enum View { List, Editor, Source, Schedule }

    // ── Snapshot copy + caches rebuilt when it changes ───────────────────
    private MetaPayload _data = new();
    private long _seenVersion = -1;
    private List<(string State, List<int> Rules)> _groups = new();
    private string[] _condText = Array.Empty<string>(), _actText = Array.Empty<string>();
    private string[] _groupLabel = Array.Empty<string>();
    private bool[] _groupFiring = Array.Empty<bool>();
    private string[] _statePicks = Array.Empty<string>(), _statesOnly = Array.Empty<string>();
    private string[] _routePicks = Array.Empty<string>(), _fileDisplays = Array.Empty<string>();
    private string _fileDisplay = "-- None --", _sourceFileLabel = "File: Unsaved";
    private IReadOnlyList<string> _completionNavs = Array.Empty<string>();

    // Ellipsized row text, recomputed only when the column width changes.
    private readonly Dictionary<string, (float Width, string Shown)> _fit = new();

    private View _view = View.List;
    private readonly HashSet<string> _collapsed = new();
    private readonly HashSet<int> _expanded = new();
    private bool _saveAs, _saveAsFocus;
    private readonly byte[] _saveName = new byte[128];

    // ── Rule editor ──────────────────────────────────────────────────────
    private int _editIndex = -1;
    private MetaRuleDto _editRule = new() { Action = 1 };
    private sealed class FieldBuffer
    {
        public readonly byte[] Bytes = new byte[2048];
        public string? Shown;
        public bool Active;     // being typed in (last frame)
    }
    private readonly Dictionary<(MetaRuleDto Rule, bool Action), FieldBuffer> _fields = new();

    // ── Source ───────────────────────────────────────────────────────────
    private CodeEditor? _editor;
    private string _sourceBaseline = string.Empty;   // snapshot text the editor was last loaded with
    private bool _sourceStale;                        // the meta changed under unapplied edits
    private (int Line, int Column) _cursor = (-1, -1);
    private string _cursorLabel = string.Empty;
    private uint _lastChange;
    private long _applySeqBefore = -2;                // -2: no apply pending
    private DateTime _applySentAt, _msgAt;
    private string _msg = string.Empty;
    private bool _msgOk = true;

    // Completion list.
    private enum SuggestionKind { Struct, Cond, Action, State, Nav }
    private readonly List<(string Text, SuggestionKind Kind)> _compItems = new();
    private readonly List<int> _compShown = new();
    private bool _compOpen;
    private int _compSel, _compTop, _compLine;
    private string _compAnchor = string.Empty;         // the line up to where the completed word starts
    private string _compWord = string.Empty;
    private const int CompRows = 10;

    public void OnShown()
    {
        UiSources.Meta.Subscribe();
        UiSources.Meta.RequestRefresh();
        CodeEditor.SetMetaVocabulary(MetaVocabulary.StructKeywords, MetaVocabulary.ConditionKeywords, MetaVocabulary.ActionKeywords);
    }

    public void OnHidden()
    {
        UiSources.Meta.Unsubscribe();
        _editor?.Dispose();
        _editor = null;
    }

    // =====================================================================
    //  Frame
    // =====================================================================

    public void Draw()
    {
        TakeSnapshot();

        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        Vector2 origin = ImGuiNET.ImGui.GetCursorScreenPos();
        Vector2 size = ImGuiNET.ImGui.GetContentRegionAvail();
        dl.AddRectFilled(origin, origin + size, ShellBg, 4);

        ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.Ui10));
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, 4));
        Vector2 inner = origin + new Vector2(6, 6);
        Vector2 innerSize = size - new Vector2(12, 12);
        // The body has no padding (EdgeToEdge): indent so every new line, not
        // just the first, starts 6 px in from the border.
        ImGuiNET.ImGui.SetCursorScreenPos(inner);
        ImGuiNET.ImGui.Indent(6);
        switch (_view)
        {
            case View.List: ListView(inner, innerSize); break;
            // The editor scrolls: it ends above the resize grip (GripInBody).
            case View.Editor: EditorView(innerSize - new Vector2(0, FaceKit.GripOverlap())); break;
            case View.Source: SourceView(inner, innerSize); break;
            case View.Schedule: ScheduleView(inner, innerSize); break;
        }
        ImGuiNET.ImGui.Unindent(6);
        ImGuiNET.ImGui.PopStyleVar();
        ImGuiNET.ImGui.PopFont();

        dl.AddRect(origin, origin + size, BtnBord, 4);
    }

    private void TakeSnapshot()
    {
        var snap = UiSources.Meta.Current;
        if (snap == null || snap.Version == _seenVersion) return;
        _seenVersion = snap.Version;
        MetaPayload fresh = snap.Value;
        CheckApplyResult(fresh);
        _data = fresh.Clone();
        RebuildCaches();

        // Keep the source editor on the plugin's text unless it has unapplied edits.
        if (_editor != null && fresh.SourceText != _sourceBaseline)
        {
            if (_editor.IsDirty) _sourceStale = true;
            else LoadSource(fresh.SourceText);
        }
    }

    private void RebuildCaches()
    {
        List<MetaRuleDto> rules = _data.Rules;
        _groups = MetaVocabulary.GroupByState(rules);
        _condText = new string[rules.Count];
        _actText = new string[rules.Count];
        for (int i = 0; i < rules.Count; i++)
        {
            _condText[i] = MetaVocabulary.RowConditionText(rules[i]);
            _actText[i] = MetaVocabulary.RowActionText(rules[i]);
        }
        _groupLabel = new string[_groups.Count];
        _groupFiring = new bool[_groups.Count];
        for (int g = 0; g < _groups.Count; g++)
        {
            bool firing = false;
            foreach (int i in _groups[g].Rules) firing |= rules[i].LastFiredMs < 1500;
            _groupFiring[g] = firing;
            _groupLabel[g] = firing ? $"{_groups[g].State}  ({_groups[g].Rules.Count})  — firing" : $"{_groups[g].State}  ({_groups[g].Rules.Count})";
        }
        _statePicks = MetaVocabulary.StatePicks(_data);
        _statesOnly = _data.States.ToArray();
        _routePicks = MetaVocabulary.RoutePicks(_data);
        _fileDisplays = new string[_data.Files.Count];
        for (int i = 0; i < _fileDisplays.Length; i++) _fileDisplays[i] = _data.Files[i].Display;
        _fileDisplay = MetaVocabulary.CurrentFileDisplay(_data);
        _sourceFileLabel = "File: " + (string.IsNullOrEmpty(_data.CurrentMetaPath) ? "Unsaved" : System.IO.Path.GetFileName(_data.CurrentMetaPath));
        _completionNavs = MetaVocabulary.CompletionNavs(_data);
        _fit.Clear();
    }

    // =====================================================================
    //  List view
    // =====================================================================

    private void ListView(Vector2 origin, Vector2 size)
    {
        float w = size.X;
        LoadSaveBar(w);
        Separator(w);
        ModeRow(w, source: false);
        Separator(w);

        const float bottomH = 24;
        float listH = Math.Max(40, origin.Y + size.Y - ImGuiNET.ImGui.GetCursorScreenPos().Y - bottomH - 9);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.ChildBg, 0);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 0));
        ImGuiNET.ImGui.BeginChild("##meta_rules", new Vector2(w, listH));
        RuleList(ImGuiNET.ImGui.GetContentRegionAvail().X);
        ImGuiNET.ImGui.EndChild();
        ImGuiNET.ImGui.PopStyleVar();
        ImGuiNET.ImGui.PopStyleColor();

        Separator(w);
        BottomBar(w);
    }

    private void LoadSaveBar(float w)
    {
        if (_saveAs)
        {
            float btn = 52, sx = ImGuiNET.ImGui.GetCursorScreenPos().X, y = ImGuiNET.ImGui.GetCursorScreenPos().Y;
            float boxW = w - btn - 26 - 8;
            if (_saveAsFocus) { ImGuiNET.ImGui.SetKeyboardFocusHere(); _saveAsFocus = false; }
            bool enter = TextBox("##save_name", _saveName, boxW, "Filename (no ext)...", ImGuiInputTextFlags.EnterReturnsTrue, out _);
            if (Button(SaveLabel + "##save_as_go", new Vector2(sx + boxW + 4, y), new Vector2(btn, 22), TextDim, BtnFill, BtnBord) || enter)
            {
                string n = Utf8(_saveName).Trim();
                if (n.Length > 0)
                    MetaCommands.Simple("save_file", path: System.IO.Path.Combine(MetaVocabulary.MetaFolder, n + ".af"));
                _saveAs = false;
            }
            if (IconButton("##save_as_x", PhosphorIcons.X, new Vector2(sx + boxW + 8 + btn, y), new Vector2(26, 22), TextDim, BtnFill, BtnBord,
                    font: UiFont.Ui14))
                _saveAs = false;
            ImGuiNET.ImGui.SetItemTooltip("Cancel");
            ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(sx, y + 22));
            ImGuiNET.ImGui.Dummy(new Vector2(0, 0));
            return;
        }

        Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
        const float refreshW = 26, saveW = 52, saveAsW = 60;
        float pickW = w - refreshW - saveW - saveAsW - 12;
        int picked = Picker("##file", _fileDisplay, _fileDisplays, MetaVocabulary.CurrentFileIndex(_data), p, pickW);
        if (picked >= 0 && picked < _data.Files.Count)
            MetaCommands.Simple("load_file", path: _data.Files[picked].Path);

        float x = p.X + pickW + 4;
        if (IconButton("##refresh", PhosphorIcons.ArrowsClockwise, new Vector2(x, p.Y), new Vector2(refreshW, 22), TextDim, BtnFill, BtnBord,
                font: UiFont.Ui14))
            UiSources.Meta.RequestRefresh();
        ImGuiNET.ImGui.SetItemTooltip("Refresh file list");
        x += refreshW + 4;
        if (Button(SaveLabel + "##save", new Vector2(x, p.Y), new Vector2(saveW, 22), TextDim, BtnFill, BtnBord))
        {
            if (!string.IsNullOrEmpty(_data.CurrentMetaPath)) MetaCommands.Simple("save_file", path: _data.CurrentMetaPath);
            else BeginSaveAs();
        }
        x += saveW + 4;
        if (Button("Save As##save_as", new Vector2(x, p.Y), new Vector2(saveAsW, 22), TextDim, BtnFill, BtnBord))
            BeginSaveAs();
        ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(p.X, p.Y + 22));
        ImGuiNET.ImGui.Dummy(new Vector2(0, 0));
    }

    private void BeginSaveAs()
    {
        _saveAs = true;
        _saveAsFocus = true;
        Array.Clear(_saveName);
    }

    // [Visual][Source][Schedule] | [x Meta][x Debug] (check-box icons; the toggles only in the list view)
    private void ModeRow(float w, bool source, bool schedule = false)
    {
        Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
        float x = p.X;
        if (Toggle("Visual##mode_v", ref x, p.Y, !source && !schedule)) ShowList();
        if (Toggle("Source##mode_s", ref x, p.Y, source)) ShowSource();
        if (Toggle(ScheduleLabel, ref x, p.Y, schedule)) ShowSchedule();

        if (!source && !schedule)
        {
            ImGuiNET.ImGui.GetWindowDrawList().AddLine(new Vector2(x + 2, p.Y + 2), new Vector2(x + 2, p.Y + 20), BtnBord);
            x += 8;
            if (Toggle(_data.EnableMeta ? MetaOn : MetaOff, ref x, p.Y, _data.EnableMeta))
            {
                _data.EnableMeta = !_data.EnableMeta;
                MetaCommands.Simple("set_enabled", value: _data.EnableMeta ? "true" : "false");
            }
            if (Toggle(_data.MetaDebug ? DebugOn : DebugOff, ref x, p.Y, _data.MetaDebug))
            {
                _data.MetaDebug = !_data.MetaDebug;
                MetaCommands.Simple("set_debug", value: _data.MetaDebug ? "true" : "false");
            }
        }
        else if (source && _editor != null)
        {
            // Ln/Col and the unapplied-edits marker, right-aligned.
            string right = _editor.IsDirty ? "● unapplied   " + _cursorLabel : _cursorLabel;
            float tw = ImGuiNET.ImGui.CalcTextSize(right).X;
            ImGuiNET.ImGui.GetWindowDrawList().AddText(new Vector2(p.X + w - tw, p.Y + (22 - ImGuiNET.ImGui.GetFontSize()) * 0.5f),
                _editor.IsDirty ? Amber : Mute, right);
        }
        ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(p.X, p.Y + 22));
        ImGuiNET.ImGui.Dummy(new Vector2(0, 0));
    }

    private void ShowList() => _view = View.List;

    private void ShowSource()
    {
        if (_view == View.Source) return;
        _view = View.Source;
        if (_editor == null)
        {
            _editor = new CodeEditor("##meta_source", CodeLanguage.Meta, CodeEditor.RynthPalette());
            LoadSource(_data.SourceText);
        }
        else if (!_editor.IsDirty && _sourceBaseline != _data.SourceText)
            LoadSource(_data.SourceText);
        _editor.RequestFocus();
    }

    // Condition/action split of the rule list (dragged in its header) and where
    // the text starts within a row (from the last row drawn).
    private static float _condFrac = Math.Clamp(RynthCore.Engine.UI.PanelColumnStore.Get("Meta.condFrac", 0.5f), 0.15f, 0.85f);
    private float _textStartOff;

    private void RuleList(float w)
    {
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        if (_groups.Count == 0)
        {
            ImGuiNET.ImGui.Dummy(new Vector2(0, 4));
            ImGuiNET.ImGui.TextColored(RynthTheme.Vec(0xFFC8D6E4), "  No rules. Click New Rule to add one.");
            return;
        }

        ImFontPtr bold = ImGuiFonts.Get(UiFont.UiBold11);
        List<MetaRuleDto> rules = _data.Rules;

        // Condition | Action header; drag the divider to move the split (saved per PC).
        if (_textStartOff > 0)
        {
            Vector2 hp = ImGuiNET.ImGui.GetCursorScreenPos();
            const float hh = 16;
            float tx = hp.X + _textStartOff, tw = Math.Max(1, hp.X + w - tx);
            float split = tw * _condFrac;
            float hy = hp.Y + (hh - bold.FontSize) * 0.5f;
            dl.AddText(bold, bold.FontSize, new Vector2(tx + 3, hy), C(0xFFC8D6E4), "Condition");
            dl.AddText(bold, bold.FontSize, new Vector2(tx + split + 4, hy), C(0xFFC8D6E4), "Action");
            if (FaceKit.ColumnDivider("##meta_split", tx + split, hp.Y, hh, ref split, 40,
                    _ => RynthCore.Engine.UI.PanelColumnStore.Set("Meta.condFrac", _condFrac)))
                _condFrac = Math.Clamp(split / tw, 0.15f, 0.85f);
            ImGuiNET.ImGui.Dummy(new Vector2(w, hh));
        }

        for (int g = 0; g < _groups.Count; g++)
        {
            (string state, List<int> members) = _groups[g];
            bool collapsed = _collapsed.Contains(state);
            bool firing = _groupFiring[g];

            // Header: caret, State (n) [— firing]; click toggles.
            ImGuiNET.ImGui.Dummy(new Vector2(0, 2));
            Vector2 hp = ImGuiNET.ImGui.GetCursorScreenPos();
            float hh = bold.FontSize + 6;
            ImGuiNET.ImGui.PushID(g);
            if (ImGuiNET.ImGui.InvisibleButton("##hdr", new Vector2(w, hh)))
            {
                if (collapsed) _collapsed.Remove(state); else _collapsed.Add(state);
            }
            if (ImGuiNET.ImGui.IsItemHovered()) ImGuiNET.ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGuiNET.ImGui.PopID();
            uint hc = firing ? Red : Amber;
            dl.AddRectFilled(hp, hp + new Vector2(w, hh), g % 2 == 0 ? StateHdrA : StateHdrB);
            dl.AddText(bold, bold.FontSize, hp + new Vector2(3, 3), hc, collapsed ? PhosphorIcons.CaretRight : PhosphorIcons.CaretDown);
            dl.AddText(bold, bold.FontSize, hp + new Vector2(18, 3), hc, _groupLabel[g]);
            if (collapsed) continue;

            for (int gi = 0; gi < members.Count; gi++)
            {
                int index = members[gi];
                if (index >= rules.Count) break;
                if (RuleRow(rules[index], index, gi, members.Count, w)) return;   // the list changed: redraw next frame
            }
        }
    }

    /// <summary>One rule row plus its expanded children. True when a click changed the list.</summary>
    private bool RuleRow(MetaRuleDto rule, int index, int gi, int groupCount, float w)
    {
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        bool firing = rule.LastFiredMs < 1500;
        bool hasChildren = (MetaVocabulary.IsCompositeCondition(rule.Condition) && rule.Children.Count > 0)
                        || (MetaVocabulary.IsAllAction(rule.Action) && rule.ActionChildren.Count > 0);
        bool expanded = _expanded.Contains(index);

        const float h = 20;
        Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
        dl.AddRectFilled(p, p + new Vector2(w, h), firing ? Fired : gi % 2 == 0 ? PanelBg : RowAlt);
        ImGuiNET.ImGui.PushID(index);
        bool changed = false;

        // Enable
        float x = p.X + 1;
        if (IconButton("##en", rule.Enabled ? PhosphorIcons.CheckSquare : PhosphorIcons.Square, new Vector2(x, p.Y + 2), new Vector2(14, 16),
                rule.Enabled ? Green : Mute, BtnFill, BtnBord))
        {
            rule.Enabled = !rule.Enabled;
            MetaCommands.Simple("set_rule_enabled", index, rule.Enabled ? "true" : "false");
        }
        ImGuiNET.ImGui.SetItemTooltip(rule.Enabled ? "Enabled — click to disable" : "Disabled — click to enable");
        x += 16;

        // Expand (only with children)
        if (hasChildren && IconButton("##exp", expanded ? PhosphorIcons.Minus : PhosphorIcons.Plus, new Vector2(x + 2, p.Y + 3), new Vector2(14, 14),
                Teal, BtnFill, BtnBord))
        {
            if (expanded) _expanded.Remove(index); else _expanded.Add(index);
        }
        x += 18;

        // Up / down within the state (Appendix A3: the plugin now swaps within the state too)
        if (IconButton("##up", PhosphorIcons.ArrowUp, new Vector2(x + 1, p.Y + 2), new Vector2(16, 16), Mute, BtnFill, BtnBord, enabled: gi > 0))
            changed |= Move(index, -1);
        ImGuiNET.ImGui.SetItemTooltip("Move up");
        x += 18;
        if (IconButton("##dn", PhosphorIcons.ArrowDown, new Vector2(x + 1, p.Y + 2), new Vector2(16, 16), Mute, BtnFill, BtnBord,
                enabled: gi < groupCount - 1))
            changed |= Move(index, +1);
        ImGuiNET.ImGui.SetItemTooltip("Move down");
        x += 18;

        // Duplicate
        if (IconButton("##dup", PhosphorIcons.Copy, new Vector2(x + 1, p.Y + 2), new Vector2(16, 16), Mute, BtnFill, BtnBord))
        {
            MetaCommands.Send(new MetaCmd { Op = "duplicate_rule", Index = index, Rule = rule.Clone() });
            _data.Rules.Insert(Math.Min(index + 1, _data.Rules.Count), rule.Clone());
            changed = true;
        }
        ImGuiNET.ImGui.SetItemTooltip("Duplicate rule");
        x += 18;

        // Delete
        if (IconButton("##del", PhosphorIcons.Trash, new Vector2(x + 1, p.Y + 2), new Vector2(18, 16), TextDim, DelFill, BtnBord))
        {
            MetaCommands.Simple("delete_rule", index);
            _data.Rules.RemoveAt(index);
            _expanded.Clear();
            changed = true;
        }
        ImGuiNET.ImGui.SetItemTooltip("Delete rule");
        x += 22;

        // Condition | action text; clicking it edits the rule.
        float textW = p.X + w - x;
        _textStartOff = x - p.X;
        ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(x, p.Y));
        if (ImGuiNET.ImGui.InvisibleButton("##edit", new Vector2(Math.Max(1, textW), h)))
            OpenEditor(index, rule);
        if (ImGuiNET.ImGui.IsItemHovered()) ImGuiNET.ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        ImGuiNET.ImGui.PopID();

        if (!changed)
        {
            float col = textW * _condFrac;
            float ty = p.Y + (h - ImGuiNET.ImGui.GetFontSize()) * 0.5f;
            uint condCol = firing ? Red : TextDim, actCol = firing ? Amber : C(0xFFC8D8E8);
            if (!rule.Enabled) { condCol = Faded(condCol); actCol = Faded(actCol); }
            dl.AddText(new Vector2(x + 3, ty), condCol, Fit(_condText[index], col - 5));
            dl.AddText(new Vector2(x + col + 2, ty), actCol, Fit(_actText[index], col - 4));
        }

        ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(p.X, p.Y + h));
        ImGuiNET.ImGui.Dummy(new Vector2(0, 0));

        if (changed)
        {
            RebuildCaches();
            return true;
        }

        if (hasChildren && expanded) ExpandedChildren(rule);
        return false;
    }

    private void ExpandedChildren(MetaRuleDto rule)
    {
        ImFontPtr f9 = ImGuiFonts.Get(UiFont.Ui9);
        ImGuiNET.ImGui.PushFont(f9);
        Vector4 amber = RynthTheme.Vec(0xFFE8B333), mute = RynthTheme.Vec(0xFFC8D6E4);
        ImGuiNET.ImGui.Indent(22);
        if (MetaVocabulary.IsCompositeCondition(rule.Condition) && rule.Children.Count > 0)
        {
            ImGuiNET.ImGui.TextColored(amber, "  " + MetaVocabulary.ConditionName(rule.Condition) + ":");
            foreach (MetaRuleDto child in rule.Children)
                ImGuiNET.ImGui.TextColored(mute, "     • " + MetaVocabulary.CondText(child));
        }
        if (MetaVocabulary.IsAllAction(rule.Action) && rule.ActionChildren.Count > 0)
        {
            ImGuiNET.ImGui.TextColored(amber, "  All (actions):");
            foreach (MetaRuleDto child in rule.ActionChildren)
                ImGuiNET.ImGui.TextColored(mute, "     • " + MetaVocabulary.ActText(child));
        }
        ImGuiNET.ImGui.Unindent(22);
        ImGuiNET.ImGui.PopFont();
    }

    private bool Move(int index, int direction)
    {
        int other = MetaVocabulary.SwapPartner(_data.Rules, index, direction);
        if (other < 0) return false;
        MetaCommands.Simple(direction < 0 ? "move_up" : "move_down", index);
        (_data.Rules[index], _data.Rules[other]) = (_data.Rules[other], _data.Rules[index]);
        _expanded.Clear();
        return true;
    }

    private void BottomBar(float w)
    {
        Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        float labelW = ImGuiNET.ImGui.CalcTextSize("State:").X;
        dl.AddText(new Vector2(p.X, p.Y + (24 - ImGuiNET.ImGui.GetFontSize()) * 0.5f), Mute, "State:");
        const float newW = 72;
        // The bar sits on the body's bottom edge: it stops short of the resize grip (GripInBody).
        float grip = ImGuiPanelHost.BodyGripReserve;
        float pickW = w - labelW - 4 - newW - 4 - grip;
        int picked = Picker("##cur_state", _data.CurrentState, _statePicks, Array.IndexOf(_statePicks, _data.CurrentState),
            new Vector2(p.X + labelW + 4, p.Y + 1), pickW);
        if (picked >= 0)
        {
            _data.CurrentState = _statePicks[picked];
            MetaCommands.Simple("set_state", value: _statePicks[picked]);
        }
        if (Button(PhosphorIcons.Plus + " New Rule##new", new Vector2(p.X + w - newW - grip, p.Y), new Vector2(newW, 24), TextDim, NewFill, Green))
            OpenEditor(-1, new MetaRuleDto { State = _data.CurrentState, Action = 1 });
        ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(p.X, p.Y + 24));
        ImGuiNET.ImGui.Dummy(new Vector2(0, 0));
    }

    // =====================================================================
    //  Rule editor view
    // =====================================================================

    private void OpenEditor(int index, MetaRuleDto rule)
    {
        _editIndex = index;
        _editRule = index >= 0 ? rule.Clone() : rule;
        _fields.Clear();
        _view = View.Editor;
    }

    private void EditorView(Vector2 size)
    {
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.ChildBg, 0);
        ImGuiNET.ImGui.BeginChild("##meta_editor", size);
        ImGuiNET.ImGui.PopStyleColor();
        float w = ImGuiNET.ImGui.GetContentRegionAvail().X;
        MetaRuleDto r = _editRule;

        if (FlowButton(PhosphorIcons.ArrowLeft + " Back to List##back", Mute, BtnFill, BtnBord)) _view = View.List;
        Separator(w);
        ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.UiBold11));
        ImGuiNET.ImGui.TextColored(RynthTheme.Vec(0xFF26D9E6), _editIndex == -1 ? "New Rule" : $"Edit Rule (index {_editIndex})");
        ImGuiNET.ImGui.PopFont();

        // State
        FieldLabel("State:");
        int picked = FlowPicker("##ed_state", r.State, _statePicks, Array.IndexOf(_statePicks, r.State), w);
        if (picked >= 0) r.State = _statePicks[picked];

        // Condition
        FieldLabel("Condition:");
        picked = FlowPicker("##ed_cond", MetaVocabulary.ConditionName(r.Condition), MetaVocabulary.ConditionNames, r.Condition, w);
        if (picked >= 0)
        {
            r.Condition = picked;
            if (!MetaVocabulary.IsCompositeCondition(picked)) r.Children.Clear();
            else if (picked == 20 && r.Children.Count > 1) r.Children.RemoveRange(1, r.Children.Count - 1);
        }
        if (!MetaVocabulary.IsCompositeCondition(r.Condition))
            DataBox("##ed_cdata", r, action: false, MetaVocabulary.ConditionHint(r.Condition), w);
        else
            SubRules(r.Children, r.Condition == 20 ? "Not (one condition):" : "Sub-Conditions", action: false, 0,
                r.Condition == 20 ? 1 : int.MaxValue, w);

        // Action
        ImGuiNET.ImGui.Dummy(new Vector2(0, 2));
        Separator(w);
        FieldLabel("Action:");
        picked = FlowPicker("##ed_act", MetaVocabulary.ActionName(r.Action), MetaVocabulary.ActionNames, r.Action, w);
        if (picked >= 0)
        {
            r.Action = picked;
            if (!MetaVocabulary.IsAllAction(picked)) r.ActionChildren.Clear();
        }
        if (!MetaVocabulary.IsAllAction(r.Action))
        {
            if (r.Action is 2 or 5)          // Set / Call Meta State
            {
                picked = FlowPicker("##ed_astate", r.ActionData.Length == 0 ? "Select state..." : r.ActionData,
                    _statesOnly, Array.IndexOf(_statesOnly, r.ActionData), w);
                if (picked >= 0) r.ActionData = _statesOnly[picked];
            }
            else if (r.Action == 3)          // Embedded Nav Route
            {
                picked = FlowPicker("##ed_route", r.ActionData.Length == 0 ? "Select route..." : r.ActionData,
                    _routePicks, Array.IndexOf(_routePicks, r.ActionData), w);
                if (picked >= 0) r.ActionData = _routePicks[picked];
            }
            else
                DataBox("##ed_adata", r, action: true, MetaVocabulary.ActionHint(r.Action), w);
        }
        else
            SubRules(r.ActionChildren, "Sub-Actions", action: true, 0, int.MaxValue, w);

        ImGuiNET.ImGui.Dummy(new Vector2(0, 2));
        Separator(w);

        // Add/Save, Cancel
        if (FlowButton(_editIndex == -1 ? "Add Rule##save_rule" : "Save Rule##save_rule", TextDim, NewFill, Green))
        {
            if (_editIndex == -1) MetaCommands.Send(new MetaCmd { Op = "add_rule", Rule = r.Clone() });
            else MetaCommands.Send(new MetaCmd { Op = "update_rule", Index = _editIndex, Rule = r.Clone() });
            _view = View.List;
        }
        ImGuiNET.ImGui.SameLine(0, 6);
        if (FlowButton("Cancel##cancel_rule", Mute, BtnFill, BtnBord)) _view = View.List;

        ImGuiNET.ImGui.EndChild();
    }

    // Sub-conditions nest (All/Any/Not under All/Any/Not, MaxSubConditionDepth
    // levels; a Not takes one); DoAll's sub-actions are a flat list.
    private void SubRules(List<MetaRuleDto> subs, string label, bool action, int depth, int maxCount, float w)
    {
        ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.UiBold11));
        ImGuiNET.ImGui.TextColored(RynthTheme.Vec(0xFFE8B333), label);
        ImGuiNET.ImGui.PopFont();

        string[] names = action ? MetaVocabulary.ActionNames : MetaVocabulary.ConditionNames;
        for (int i = 0; i < subs.Count; i++)
        {
            MetaRuleDto sub = subs[i];
            ImGuiNET.ImGui.PushID(i);
            Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
            float half = (w - 24) * 0.5f;

            // Appendix A4: the label indexes by the sub-rule's own kind.
            int current = action ? sub.Action : sub.Condition;
            string shown = action ? MetaVocabulary.ActionName(sub.Action) : MetaVocabulary.ConditionName(sub.Condition);
            int picked = Picker("##type", shown, names, current, p, half - 2, 20);
            if (picked >= 0)
            {
                if (action) sub.Action = picked;
                else
                {
                    sub.Condition = picked;
                    if (!MetaVocabulary.IsCompositeCondition(picked)) sub.Children.Clear();
                    else if (picked == 20 && sub.Children.Count > 1) sub.Children.RemoveRange(1, sub.Children.Count - 1);
                }
            }

            bool nests = !action && MetaVocabulary.IsCompositeCondition(sub.Condition) && depth < MetaVocabulary.MaxSubConditionDepth;
            if (nests)
                ImGuiNET.ImGui.GetWindowDrawList().AddText(new Vector2(p.X + half + 4, p.Y + (20 - ImGuiNET.ImGui.GetFontSize()) * 0.5f),
                    TextDim, sub.Condition == 20 ? "(the condition below)" : "(the conditions below)");
            else
            {
                ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(p.X + half + 2, p.Y));
                DataBox("##data", sub, action, "data...", half, 20);
            }

            bool removed = IconButton("##rm", PhosphorIcons.X, new Vector2(p.X + w - 20, p.Y), new Vector2(20, 20), TextDim, DelFill, BtnBord);
            ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(p.X, p.Y + 21));
            ImGuiNET.ImGui.Dummy(new Vector2(0, 0));

            if (!removed && nests)
            {
                ImGuiNET.ImGui.Indent(14);
                SubRules(sub.Children, sub.Condition switch { 20 => "Not:", 2 => "All of:", _ => "Any of:" },
                    action: false, depth + 1, sub.Condition == 20 ? 1 : int.MaxValue, w - 14);
                ImGuiNET.ImGui.Unindent(14);
            }
            ImGuiNET.ImGui.PopID();
            if (removed)
            {
                subs.RemoveAt(i);
                break;   // indices shifted: the rest draws next frame
            }
        }

        if (subs.Count >= maxCount) return;   // a Not already has its one operand
        ImGuiNET.ImGui.PushID(depth * 1000 + subs.Count);
        if (FlowButton(PhosphorIcons.Plus + " Add##add_sub", Teal, BtnFill, BtnBord, height: 20))
            subs.Add(new MetaRuleDto { State = _editRule.State });
        ImGuiNET.ImGui.PopID();
    }

    // =====================================================================
    //  Source view
    // =====================================================================

    private void LoadSource(string text)
    {
        _editor?.SetText(text);
        _sourceBaseline = text;
        _sourceStale = false;
        _lastChange = _editor?.ChangeCount ?? 0;
        CloseCompletion();
    }

    private void SourceView(Vector2 origin, Vector2 size)
    {
        float w = size.X;
        var dl = ImGuiNET.ImGui.GetWindowDrawList();

        // File: name  [Save]
        Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
        dl.AddText(new Vector2(p.X, p.Y + (22 - ImGuiNET.ImGui.GetFontSize()) * 0.5f), Amber, _sourceFileLabel);
        float lw = ImGuiNET.ImGui.CalcTextSize(_sourceFileLabel).X;
        if (Button(SaveLabel + "##src_save", new Vector2(p.X + lw + 8, p.Y), new Vector2(52, 22), TextDim, BtnFill, BtnBord)
            && !string.IsNullOrEmpty(_data.CurrentMetaPath))
            MetaCommands.Simple("save_file", path: _data.CurrentMetaPath);
        ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(p.X, p.Y + 22));
        ImGuiNET.ImGui.Dummy(new Vector2(0, 0));
        Separator(w);
        ModeRow(w, source: true);
        Separator(w);

        UpdateApplyTimeout();
        bool showMsg = _msg.Length > 0 && (_applySeqBefore != -2 || (DateTime.Now - _msgAt).TotalSeconds < 8);
        if (showMsg)
        {
            ImGuiNET.ImGui.PushTextWrapPos(ImGuiNET.ImGui.GetCursorPosX() + w);
            ImGuiNET.ImGui.TextColored(RynthTheme.Vec(_msgOk ? 0xFF33CC66 : 0xFFE04848), _msg);
            ImGuiNET.ImGui.PopTextWrapPos();
        }
        if (_sourceStale)
            ImGuiNET.ImGui.TextColored(RynthTheme.Vec(0xFFE8B333), "The meta changed since you started editing. Revert loads it.");

        if (_editor == null || !CodeEditor.Available)
        {
            ImGuiNET.ImGui.TextColored(RynthTheme.Vec(0xFFCC3333),
                "The source editor needs the new cimgui.dll (a full RynthCore deploy). Use the Avalonia face meanwhile: /rc ui Meta avalonia");
            return;
        }

        const float buttonsH = 26;
        float editorH = Math.Max(60, origin.Y + size.Y - ImGuiNET.ImGui.GetCursorScreenPos().Y - buttonsH - 4);
        ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.Code12));
        bool focused = _editor.Render(new Vector2(w, editorH));
        ImGuiNET.ImGui.PopFont();
        SourceAfterRender(focused);

        ImGuiNET.ImGui.Dummy(new Vector2(0, 2));
        if (FlowButton(PhosphorIcons.Check + " Apply##apply", TextDim, NewFill, Green)) Apply();
        ImGuiNET.ImGui.SameLine(0, 6);
        if (FlowButton(PhosphorIcons.ArrowCounterClockwise + " Revert##revert", Mute, BtnFill, BtnBord))
        {
            LoadSource(_data.SourceText);
            _editor.RequestFocus();
        }
    }

    private void Apply()
    {
        if (_editor == null) return;
        _applySeqBefore = _data.ApplyResult?.Seq ?? -1;
        _applySentAt = DateTime.Now;
        MetaCommands.Send(new MetaCmd { Op = "set_source", Text = _editor.GetText() });
        SetMsg("Applying…", ok: true);
    }

    /// <summary>The first snapshot with a newer ApplyResult carries this apply's result (Appendix A5).</summary>
    private void CheckApplyResult(MetaPayload fresh)
    {
        if (_applySeqBefore == -2) return;
        MetaApplyResult? r = fresh.ApplyResult;
        if (r == null || r.Seq <= _applySeqBefore) return;
        _applySeqBefore = -2;
        SetMsg(r.Text, r.Ok);
        if (r.Ok && _editor != null)
        {
            // Applied: the editor's text is now the meta. Keep the caret and
            // scroll; Revert shows the plugin's own formatting.
            _editor.MarkClean();
            _sourceBaseline = fresh.SourceText;
            _sourceStale = false;
        }
    }

    private void UpdateApplyTimeout()
    {
        if (_applySeqBefore == -2 || (DateTime.Now - _applySentAt).TotalSeconds <= 3) return;
        _applySeqBefore = -2;
        SetMsg("Sent. This RynthAi version reports the result in chat.", ok: true);
    }

    private void SetMsg(string text, bool ok)
    {
        _msg = text;
        _msgOk = ok;
        _msgAt = DateTime.Now;
    }

    // ── Completion ───────────────────────────────────────────────────────

    private void SourceAfterRender(bool focused)
    {
        CodeEditor ed = _editor!;
        (int Line, int Column) cursor = ed.Cursor;
        bool moved = cursor != _cursor;
        if (moved)
        {
            _cursor = cursor;
            _cursorLabel = string.Create(CultureInfo.InvariantCulture, $"Ln {cursor.Line + 1}, Col {cursor.Column + 1}");
        }
        uint change = ed.ChangeCount;
        bool typed = change != _lastChange;
        _lastChange = change;

        if (focused && ImGuiNET.ImGui.GetIO().KeyCtrl && ImGuiNET.ImGui.IsKeyPressed(ImGuiKey.Space))
            OpenCompletion(auto: false);
        else if (typed && focused && !_compOpen)
        {
            // "IF: " (a space right after a colon) lists what can follow.
            string prefix = ed.LinePrefix();
            if (prefix.EndsWith(": ", StringComparison.Ordinal)) OpenCompletion(auto: true, prefix);
        }
        else if (_compOpen && (typed || moved))
            UpdateCompletion();

        if (_compOpen) DrawCompletion(focused);
        ed.CompletionKeysHeld = _compOpen;
    }

    private void OpenCompletion(bool auto, string? prefix = null)   // auto: after ": " (else Ctrl+Space)
    {
        CodeEditor ed = _editor!;
        prefix ??= ed.LinePrefix();
        int start = prefix.Length;
        while (start > 0 && (char.IsLetterOrDigit(prefix[start - 1]) || prefix[start - 1] == '_')) start--;
        _compAnchor = prefix.Substring(0, start);
        _compWord = prefix.Substring(start);
        _compLine = ed.Cursor.Line;

        BuildSuggestions(prefix);
        if (_compItems.Count == 0) { CloseCompletion(); return; }
        _compOpen = true;
        Filter();
        if (_compShown.Count == 0) { CloseCompletion(); return; }
        _compSel = 0;
        _compTop = 0;
    }

    private void UpdateCompletion()
    {
        CodeEditor ed = _editor!;
        string prefix = ed.LinePrefix();
        if (ed.Cursor.Line != _compLine || !prefix.StartsWith(_compAnchor, StringComparison.Ordinal))
        {
            CloseCompletion();
            return;
        }
        _compWord = prefix.Substring(_compAnchor.Length);
        Filter();
        if (_compShown.Count == 0) { CloseCompletion(); return; }
        _compSel = Math.Clamp(_compSel, 0, _compShown.Count - 1);
    }

    private void CloseCompletion()
    {
        _compOpen = false;
        _compItems.Clear();
        _compShown.Clear();
    }

    /// <summary>Prefix matches first, then matches anywhere (case-insensitive).</summary>
    private void Filter()
    {
        _compShown.Clear();
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < _compItems.Count; i++)
            {
                string t = _compItems[i].Text;
                bool prefix = t.StartsWith(_compWord, StringComparison.OrdinalIgnoreCase);
                if (pass == 0 ? prefix : !prefix && t.Contains(_compWord, StringComparison.OrdinalIgnoreCase))
                    _compShown.Add(i);
            }
    }

    // MetaSourceEditor.BuildSuggestionsForContext.
    private void BuildSuggestions(string prefix)
    {
        _compItems.Clear();
        string t = prefix.TrimStart().TrimEnd();
        if (StartsWith(t, "IF:", out _))
        {
            foreach (string s in MetaVocabulary.ConditionKeywords) _compItems.Add((s, SuggestionKind.Cond));
            return;
        }
        if (StartsWith(t, "DO:", out string rest))
        {
            rest = rest.TrimStart();
            if (StartsWith(rest, "SetState", out _) || StartsWith(rest, "CallState", out _))
                foreach (string s in _data.States) _compItems.Add((s, SuggestionKind.State));
            else if (StartsWith(rest, "EmbedNav", out _))
                foreach (string s in _completionNavs) _compItems.Add((s, SuggestionKind.Nav));
            else
                foreach (string s in MetaVocabulary.ActionKeywords) _compItems.Add((s, SuggestionKind.Action));
            return;
        }
        if (StartsWith(t, "STATE:", out _))
        {
            foreach (string s in _data.States) _compItems.Add((s, SuggestionKind.State));
            return;
        }
        if (StartsWith(t, "NAV:", out _))
        {
            foreach (string s in _completionNavs) _compItems.Add((s, SuggestionKind.Nav));
            return;
        }
        _compItems.Add(("STATE:", SuggestionKind.Struct));
        _compItems.Add(("IF:", SuggestionKind.Struct));
        _compItems.Add(("DO:", SuggestionKind.Struct));
        _compItems.Add(("NAV:", SuggestionKind.Struct));
    }

    private static bool StartsWith(string text, string keyword, out string rest)
    {
        if (text.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
        {
            rest = text.Substring(keyword.Length);
            return true;
        }
        rest = string.Empty;
        return false;
    }

    private void Accept(int shownIndex)
    {
        string text = _compItems[_compShown[shownIndex]].Text;
        _editor!.ReplaceBeforeCursor(Encoding.UTF8.GetByteCount(_compWord), text);
        CloseCompletion();
        _lastChange = _editor.ChangeCount;   // don't re-trigger on our own insert
        _editor.RequestFocus();
    }

    private void DrawCompletion(bool editorFocused)
    {
        int n = _compShown.Count;
        int rows = Math.Min(n, CompRows);
        const float rowH = 18, width = 260;
        Vector2 pos = _editor!.CursorScreenPos + new Vector2(0, 2);
        Vector2 size = new(width, rows * rowH + 6);
        Vector2 display = ImGuiNET.ImGui.GetIO().DisplaySize;
        if (pos.Y + size.Y > display.Y) pos.Y -= size.Y + 20;       // flip above the line
        pos.X = Math.Clamp(pos.X, 0, Math.Max(0, display.X - width));

        // Keys (the editor ignores these while the list is open).
        if (editorFocused)
        {
            if (ImGuiNET.ImGui.IsKeyPressed(ImGuiKey.DownArrow)) _compSel = Math.Min(n - 1, _compSel + 1);
            if (ImGuiNET.ImGui.IsKeyPressed(ImGuiKey.UpArrow)) _compSel = Math.Max(0, _compSel - 1);
            if (ImGuiNET.ImGui.IsKeyPressed(ImGuiKey.PageDown)) _compSel = Math.Min(n - 1, _compSel + CompRows);
            if (ImGuiNET.ImGui.IsKeyPressed(ImGuiKey.PageUp)) _compSel = Math.Max(0, _compSel - CompRows);
            if (ImGuiNET.ImGui.IsKeyPressed(ImGuiKey.Escape)) { CloseCompletion(); return; }
            if (ImGuiNET.ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGuiNET.ImGui.IsKeyPressed(ImGuiKey.KeypadEnter)
                || ImGuiNET.ImGui.IsKeyPressed(ImGuiKey.Tab))
            {
                Accept(_compSel);
                return;
            }
        }
        if (_compSel < _compTop) _compTop = _compSel;
        if (_compSel >= _compTop + CompRows) _compTop = _compSel - CompRows + 1;
        _compTop = Math.Clamp(_compTop, 0, Math.Max(0, n - CompRows));

        // A tooltip-class window: drawn above the panels, doesn't grab focus on appearing.
        const ImGuiWindowFlags flags = ImGuiWindowFlags.Tooltip | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoDocking;
        ImGuiNET.ImGui.SetNextWindowPos(pos);
        ImGuiNET.ImGui.SetNextWindowSize(size);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.WindowBg, ShellBg);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Border, Teal);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(3, 3));
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 4f);
        ImGuiNET.ImGui.Begin("##meta_completion", flags);
        ImGuiNET.ImGui.PopStyleVar(3);
        ImGuiNET.ImGui.PopStyleColor(2);

        bool hovered = ImGuiNET.ImGui.IsWindowHovered();
        if (hovered)
        {
            float wheel = ImGuiNET.ImGui.GetIO().MouseWheel;
            if (wheel != 0) _compTop = Math.Clamp(_compTop - Math.Sign(wheel) * 3, 0, Math.Max(0, n - CompRows));
        }

        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        ImFontPtr code = ImGuiFonts.Get(UiFont.Code12);
        ImFontPtr small = ImGuiFonts.Get(UiFont.Ui9);
        float iw = ImGuiNET.ImGui.GetContentRegionAvail().X;
        int accept = -1;
        for (int r = 0; r < rows; r++)
        {
            int k = _compTop + r;
            if (k >= n) break;
            (string text, SuggestionKind kind) = _compItems[_compShown[k]];
            Vector2 ip = ImGuiNET.ImGui.GetCursorScreenPos();
            ImGuiNET.ImGui.PushID(r);
            if (ImGuiNET.ImGui.InvisibleButton("##c", new Vector2(iw, rowH))) accept = k;
            bool ih = ImGuiNET.ImGui.IsItemHovered();
            ImGuiNET.ImGui.PopID();
            bool sel = k == _compSel;
            if (sel || ih) dl.AddRectFilled(ip, ip + new Vector2(iw, rowH), sel ? PickSel : BtnFill, 2);
            dl.AddText(code, code.FontSize, ip + new Vector2(4, (rowH - code.FontSize) * 0.5f), KindColor(kind), text);
            string desc = KindLabel(kind);
            ImGuiNET.ImGui.PushFont(small);
            float dw = ImGuiNET.ImGui.CalcTextSize(desc).X;
            ImGuiNET.ImGui.PopFont();
            dl.AddText(small, small.FontSize, ip + new Vector2(iw - dw - 4, (rowH - small.FontSize) * 0.5f), Mute, desc);
        }
        ImGuiNET.ImGui.End();

        if (accept >= 0) Accept(accept);
        else if (!editorFocused && !hovered && !ImGuiNET.ImGui.IsMouseDown(ImGuiMouseButton.Left)) CloseCompletion();
    }

    private static uint KindColor(SuggestionKind kind) => kind switch
    {
        SuggestionKind.Struct => Teal,
        SuggestionKind.Cond => Amber,
        SuggestionKind.Action => C(0xFF7BB8FF),
        _ => TextDim,
    };

    private static string KindLabel(SuggestionKind kind) => kind switch
    {
        SuggestionKind.Struct => "structural keyword",
        SuggestionKind.Cond => "condition",
        SuggestionKind.Action => "action",
        SuggestionKind.State => "meta state",
        _ => "nav route",
    };

    // =====================================================================
    //  Widgets
    // =====================================================================

    /// <summary>A flat bordered button at <paramref name="pos"/>; the text after "##" is the id.</summary>
    private static bool Button(string labelId, Vector2 pos, Vector2 size, uint fg, uint bg, uint border, bool enabled = true)
    {
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        ImGuiNET.ImGui.SetCursorScreenPos(pos);
        bool clicked = ImGuiNET.ImGui.InvisibleButton(labelId, size) && enabled;
        bool hot = enabled && ImGuiNET.ImGui.IsItemHovered();
        uint fill = enabled && ImGuiNET.ImGui.IsItemActive() ? Darken(bg) : hot ? Lighten(bg) : bg;
        dl.AddRectFilled(pos, pos + size, fill, 3);
        dl.AddRect(pos, pos + size, border, 3);
        int cut = labelId.IndexOf("##", StringComparison.Ordinal);
        string label = cut >= 0 ? labelId.Substring(0, cut) : labelId;
        Vector2 ts = ImGuiNET.ImGui.CalcTextSize(label);
        dl.AddText(pos + (size - ts) * 0.5f, enabled ? fg : Faded(fg), label);
        return clicked;
    }

    private const string SaveLabel = PhosphorIcons.FloppyDisk + " Save";
    private const string MetaOn = PhosphorIcons.CheckSquare + " Meta##meta_on", MetaOff = PhosphorIcons.Square + " Meta##meta_on";
    private const string DebugOn = PhosphorIcons.CheckSquare + " Debug##meta_dbg", DebugOff = PhosphorIcons.Square + " Debug##meta_dbg";

    /// <summary>
    /// Button's look with a Phosphor icon centred on it; <paramref name="id"/> is
    /// the "##" id. Ui11 (about 10 px) suits the 14-20 px row buttons.
    /// </summary>
    private static bool IconButton(string id, string icon, Vector2 pos, Vector2 size, uint fg, uint bg, uint border,
        bool enabled = true, UiFont font = UiFont.Ui11)
    {
        bool clicked = Button(id, pos, size, fg, bg, border, enabled);
        PhosphorIcons.DrawCentered(ImGuiNET.ImGui.GetWindowDrawList(), ImGuiFonts.Get(font), icon, pos, size, enabled ? fg : Faded(fg));
        return clicked;
    }

    /// <summary>A button sized to its label, at the layout cursor.</summary>
    private static bool FlowButton(string labelId, uint fg, uint bg, uint border, float height = 22)
    {
        int cut = labelId.IndexOf("##", StringComparison.Ordinal);
        float tw = ImGuiNET.ImGui.CalcTextSize(cut >= 0 ? labelId.Substring(0, cut) : labelId).X;
        Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
        Vector2 size = new(tw + 20, height);
        bool clicked = Button(labelId, p, size, fg, bg, border);
        ImGuiNET.ImGui.SetCursorScreenPos(p);
        ImGuiNET.ImGui.Dummy(size);
        return clicked;
    }

    /// <summary>Visual/Source-style toggle at (x, y); advances x.</summary>
    private static bool Toggle(string labelId, ref float x, float y, bool on)
    {
        int cut = labelId.IndexOf("##", StringComparison.Ordinal);
        float tw = ImGuiNET.ImGui.CalcTextSize(labelId.Substring(0, cut)).X;
        var size = new Vector2(tw + 16, 22);
        bool clicked = Button(labelId, new Vector2(x, y), size, on ? Teal : Mute, on ? BtnOn : BtnFill, on ? Teal : BtnBord);
        x += size.X + 4;
        return clicked;
    }

    /// <summary>
    /// A picker button showing <paramref name="shown"/>; click opens a 240 px
    /// list (current item highlighted). Returns the picked index or -1.
    /// </summary>
    private static int Picker(string id, string shown, string[] items, int selected, Vector2 pos, float width, float height = 22)
    {
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        ImGuiNET.ImGui.PushID(id);
        ImGuiNET.ImGui.SetCursorScreenPos(pos);
        if (ImGuiNET.ImGui.InvisibleButton("##btn", new Vector2(Math.Max(1, width), height)) && items.Length > 0)
        {
            float listW = Math.Max(240, width);
            Vector2 display = ImGuiNET.ImGui.GetIO().DisplaySize;
            ImGuiNET.ImGui.SetNextWindowPos(new Vector2(Math.Clamp(pos.X, 0, Math.Max(0, display.X - listW)), pos.Y + height));
            ImGuiNET.ImGui.OpenPopup("##list");
        }
        bool hot = ImGuiNET.ImGui.IsItemHovered();
        dl.AddRectFilled(pos, pos + new Vector2(width, height), hot ? Lighten(BtnFill) : BtnFill, 3);
        dl.AddRect(pos, pos + new Vector2(width, height), BtnBord, 3);
        dl.PushClipRect(pos, pos + new Vector2(width - 4, height), true);
        dl.AddText(pos + new Vector2(6, (height - ImGuiNET.ImGui.GetFontSize()) * 0.5f), TextDim, shown);
        dl.PopClipRect();

        int picked = -1;
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.PopupBg, ShellBg);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Border, Teal);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 4f);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(2, 2));
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 1));
        ImGuiNET.ImGui.SetNextWindowSize(new Vector2(Math.Max(240, width), Math.Min(items.Length * 21 + 4, 320)));
        bool open = ImGuiNET.ImGui.BeginPopup("##list");
        ImGuiNET.ImGui.PopStyleVar(3);
        ImGuiNET.ImGui.PopStyleColor(2);
        if (open)
        {
            var pdl = ImGuiNET.ImGui.GetWindowDrawList();
            float iw = ImGuiNET.ImGui.GetContentRegionAvail().X;
            for (int i = 0; i < items.Length; i++)
            {
                ImGuiNET.ImGui.PushID(i);
                Vector2 ip = ImGuiNET.ImGui.GetCursorScreenPos();
                bool click = ImGuiNET.ImGui.InvisibleButton("##item", new Vector2(iw, 20));
                bool ih = ImGuiNET.ImGui.IsItemHovered();
                ImGuiNET.ImGui.PopID();
                bool sel = i == selected;
                pdl.AddRectFilled(ip, ip + new Vector2(iw, 20), sel ? PickSel : ih ? Lighten(BtnFill) : BtnFill);
                pdl.AddRect(ip, ip + new Vector2(iw, 20), BtnBord);
                pdl.AddText(ip + new Vector2(6, (20 - ImGuiNET.ImGui.GetFontSize()) * 0.5f), sel ? Teal : TextDim, items[i]);
                if (click)
                {
                    picked = i;
                    ImGuiNET.ImGui.CloseCurrentPopup();
                }
            }
            ImGuiNET.ImGui.EndPopup();
        }
        ImGuiNET.ImGui.PopID();
        ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(pos.X, pos.Y + height));
        ImGuiNET.ImGui.Dummy(new Vector2(0, 0));
        return picked;
    }

    /// <summary>A full-width picker at the layout cursor.</summary>
    private static int FlowPicker(string id, string shown, string[] items, int selected, float width)
    {
        Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
        int picked = Picker(id, shown, items, selected, p, width);
        ImGuiNET.ImGui.SetCursorScreenPos(p);
        ImGuiNET.ImGui.Dummy(new Vector2(width, 22));
        return picked;
    }

    /// <summary>A text box bound to a rule's ConditionData or ActionData, with a grey hint while empty.</summary>
    private void DataBox(string id, MetaRuleDto rule, bool action, string hint, float width, float height = 22)
    {
        if (!_fields.TryGetValue((rule, action), out FieldBuffer? buf))
            _fields[(rule, action)] = buf = new FieldBuffer();
        string value = action ? rule.ActionData : rule.ConditionData;
        if (!buf.Active && !ReferenceEquals(buf.Shown, value))
        {
            WriteUtf8(buf.Bytes, value);
            buf.Shown = value;
        }
        bool edited = TextBox(id, buf.Bytes, width, hint, ImGuiInputTextFlags.None, out buf.Active, height);
        if (edited)
        {
            string typed = Utf8(buf.Bytes);
            if (action) rule.ActionData = typed; else rule.ConditionData = typed;
            buf.Shown = typed;
        }
    }

    /// <summary>A bordered InputText with a hint drawn while it is empty. Returns InputText's result.</summary>
    private static bool TextBox(string id, byte[] buffer, float width, string hint, ImGuiInputTextFlags flags, out bool active, float height = 22)
    {
        Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
        ImGuiNET.ImGui.SetNextItemWidth(width);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.FrameBg, BtnFill);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Text, TextDim);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Border, BtnBord);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, (height - ImGuiNET.ImGui.GetFontSize()) * 0.5f));
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 3f);
        bool result = ImGuiNET.ImGui.InputText(id, buffer, (uint)buffer.Length, flags);
        active = ImGuiNET.ImGui.IsItemActive();
        ImGuiNET.ImGui.PopStyleVar(3);
        ImGuiNET.ImGui.PopStyleColor(3);
        if (buffer[0] == 0 && !active && hint.Length > 0)
            ImGuiNET.ImGui.GetWindowDrawList().AddText(p + new Vector2(5, (height - ImGuiNET.ImGui.GetFontSize()) * 0.5f), Faded(Mute), hint);
        return result;
    }

    private static void FieldLabel(string text)
    {
        ImGuiNET.ImGui.Dummy(new Vector2(0, 1));
        ImGuiNET.ImGui.TextColored(RynthTheme.Vec(0xFFC8D6E4), text);
    }

    private static void Separator(float w)
    {
        Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
        ImGuiNET.ImGui.GetWindowDrawList().AddLine(p + new Vector2(0, 2), p + new Vector2(w, 2), BtnBord);
        ImGuiNET.ImGui.Dummy(new Vector2(w, 5));
    }

    /// <summary><paramref name="text"/> cut to <paramref name="width"/> with "…", cached per text and width.</summary>
    private string Fit(string text, float width)
    {
        if (_fit.TryGetValue(text, out var hit) && Math.Abs(hit.Width - width) < 0.5f) return hit.Shown;
        string shown = text;
        if (ImGuiNET.ImGui.CalcTextSize(text).X > width)
        {
            int n = text.Length;
            while (n > 0 && ImGuiNET.ImGui.CalcTextSize(text.AsSpan(0, n).ToString() + "…").X > width) n--;
            shown = text.Substring(0, n) + "…";
        }
        _fit[text] = (width, shown);
        return shown;
    }

    private static string Utf8(byte[] buffer)
    {
        int len = Array.IndexOf(buffer, (byte)0);
        return Encoding.UTF8.GetString(buffer, 0, len < 0 ? buffer.Length : len);
    }

    private static void WriteUtf8(byte[] buffer, string text)
    {
        int max = buffer.Length - 1;
        int n = Encoding.UTF8.GetByteCount(text) <= max ? Encoding.UTF8.GetBytes(text, 0, text.Length, buffer, 0) : TruncatedCopy(buffer, text, max);
        buffer[n] = 0;
    }

    private static int TruncatedCopy(byte[] buffer, string text, int max)
    {
        byte[] all = Encoding.UTF8.GetBytes(text);
        int n = max;
        while (n > 0 && (all[n] & 0xC0) == 0x80) n--;   // don't split a UTF-8 sequence
        Array.Copy(all, buffer, n);
        return n;
    }

    private static uint Lighten(uint abgr) => Scale(abgr, 1.25f, 12);
    private static uint Darken(uint abgr) => Scale(abgr, 0.8f, 0);

    private static uint Scale(uint abgr, float k, int add)
    {
        uint a = abgr & 0xFF000000;
        uint r = (uint)Math.Min(255, (int)((abgr & 0xFF) * k) + add);
        uint g = (uint)Math.Min(255, (int)(((abgr >> 8) & 0xFF) * k) + add);
        uint b = (uint)Math.Min(255, (int)(((abgr >> 16) & 0xFF) * k) + add);
        return a | (b << 16) | (g << 8) | r;
    }
}
