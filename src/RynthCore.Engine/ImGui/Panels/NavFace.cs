// ============================================================================
//  RynthCore.Engine - ImGui/Panels/NavFace.cs
//  ImGui face of the Nav panel (UI/Panels/NavPanel.cs): active nav + status,
//  Start/Stop, route type and insert mode, the Breadcrumbs and Route overlay
//  toggles, Add Waypoint / Portal (not yet) /
//  Recall / Clear / Save / Dungeon Patrol, a chat waypoint, Save As, the nav
//  file picker, the nav point reach (RynthAi's FollowNavMin, the same row as
//  Settings > Navigation), and the waypoint list (click selects the insert
//  point, X deletes, => marks the active one).
//
//  Data: UiSources.Nav (hub) and NavCommands. Button actions change a private
//  copy at once; the plugin applies commands immediately, so the next
//  snapshot (requested right after) replaces the copy. The reach row reads
//  UiSources.Settings and saves through SettingsCommands, like the Settings face.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using ImGuiNET;
using RynthCore.Engine.UI.Data;
using static RynthCore.Engine.ImGuiBackend.Panels.FaceKit;

namespace RynthCore.Engine.ImGuiBackend.Panels;

internal sealed class NavFace : IImGuiPanel
{
    public const string Title = "Nav";

    public static void Register() => ImGuiPanelHost.Register(Title,
        new PanelSpec(new Vector2(400, 560), new Vector2(360, 380), EdgeToEdge: true),
        () => new NavFace());

    private static readonly string[] Actions =
    {
        PhosphorIcons.MapPinPlus + " Add Waypoint", PhosphorIcons.Spiral + " Add Portal", PhosphorIcons.MagicWand + " Add Recall",
        PhosphorIcons.Trash + " Clear Route", PhosphorIcons.FloppyDisk + " Save Route", PhosphorIcons.Footprints + " Dungeon Patrol",
    };
    private const string StartNav = PhosphorIcons.Play + " Start Navigation", StopNav = PhosphorIcons.Stop + " Stop Navigation";
    private const string AddChat = PhosphorIcons.Plus + " Add Chat", SaveAs = PhosphorIcons.FloppyDisk + " Save As";
    private static readonly bool[] ActionsEnabled = { true, false, true, true, true, true };

    private NavPayload _data = new();
    private long _seenVersion = -1;
    private readonly List<string> _pointText = new();
    private int _selected = -1, _addMode;
    private bool _typing;
    private string _routeLabel = "Route: Once", _insertLabel = "Insert: End", _activeLabel = "Active Nav: None (Unsaved)";
    private string _fileLabel = "Select nav file...";
    private string[] _navFiles = Array.Empty<string>();
    private readonly byte[] _chat = new byte[256];
    private int _chatLoadedFor = -1;   // waypoint whose command is in _chat (-1: none)

    private static void SetUtf8(byte[] buffer, string text)
    {
        Array.Clear(buffer);
        byte[] b = System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty);
        Array.Copy(b, buffer, Math.Min(b.Length, buffer.Length - 1));
    }
    private readonly byte[] _saveName = new byte[128];
    private readonly byte[] _reach = new byte[16];
    private RynthAiSettings? _settings;      // last Settings snapshot (null until fetched)
    private long _settingsVersion = -1;
    private bool _reachTyping;
    private readonly Picker _picker = new("##nav_pick");

    public void OnShown()
    {
        UiSources.Nav.Subscribe();
        UiSources.Nav.RequestRefresh();
        UiSources.Settings.Subscribe();
        UiSources.Settings.RequestRefresh();
    }

    public void OnHidden()
    {
        UiSources.Nav.Unsubscribe();
        UiSources.Settings.Unsubscribe();
    }

    public void Draw()
    {
        TakeSnapshot();
        float w = Begin(out Vector2 origin, out Vector2 size);
        bool typing = false;
        NavPayload d = _data;
        bool navActive = d.MacroRunning && d.NavigationEnabled;

        // Active nav + status
        ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.UiBold11));
        Label(_activeLabel, Amber);
        ImGuiNET.ImGui.PopFont();
        if (d.NavStatusLine.Length > 0) Label(d.NavStatusLine, d.NavIsStuck ? Amber : Green);

        // Start / Stop
        Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
        if (Button("##startstop", navActive ? StopNav : StartNav, p, new Vector2(w, 28), Text, navActive ? StopBg : StartBg))
        {
            if (navActive)
            {
                NavCommands.Send(new NavCmd { Cmd = "stopNav" });
                _data.NavigationEnabled = false;
            }
            else
            {
                NavCommands.Send(new NavCmd { Cmd = "startNav" });
                _data.MacroRunning = true;
                _data.NavigationEnabled = true;
            }
        }
        NextLine(p, 32);
        Separator(w);

        // Route type | Insert mode
        p = ImGuiNET.ImGui.GetCursorScreenPos();
        float half = (w - 4) / 2;
        if (Button("##route", _routeLabel, p, new Vector2(half, 22), Text, BtnFill, leftAlign: true))
            _picker.Open(new Vector2(p.X, p.Y + 22), NavCommands.RouteTypes, NavCommands.RouteIndex(_data.RouteType), i =>
            {
                int routeType = NavCommands.RouteTypeFor(i);
                NavCommands.Send(new NavCmd { Cmd = "setRouteType", RouteType = routeType });
                _data.RouteType = routeType;
                UpdateLabels();
            });
        if (Button("##insert", _insertLabel, new Vector2(p.X + half + 4, p.Y), new Vector2(half, 22), Text, BtnFill, leftAlign: true))
            _picker.Open(new Vector2(p.X + half + 4, p.Y + 22), NavCommands.AddModes, _addMode, i =>
            {
                _addMode = i;
                UpdateLabels();
            });
        NextLine(p, 26);

        // Breadcrumbs | Route overlay (RynthAi's NavOverlaySettings; saved per character)
        p = ImGuiNET.ImGui.GetCursorScreenPos();
        if (OverlayToggle("##crumbs", "Breadcrumbs", _data.TrackBreadcrumbs, p, half,
                "Record the trail you walk and draw it on the ground (/ra navtrail on|off).\nTurning it off hides the trail; /ra navtrail clear wipes it."))
        {
            bool on = !_data.TrackBreadcrumbs;
            NavCommands.Send(new NavCmd { Cmd = "setBreadcrumbs", On = on });
            _data.TrackBreadcrumbs = on;
        }
        if (OverlayToggle("##routeovl", "Route overlay", _data.ShowRouteOverlay, new Vector2(p.X + half + 4, p.Y), half,
                "Draw the route in the world: waypoint rings and lines, waypoint labels and the guide line\nto the active waypoint (/ra navoverlay on|off). The waypoint HUD window is separate."))
        {
            bool on = !_data.ShowRouteOverlay;
            NavCommands.Send(new NavCmd { Cmd = "setRouteOverlay", On = on });
            _data.ShowRouteOverlay = on;
        }
        NextLine(p, 26);

        typing |= ReachRow(w);

        // Actions
        int action = WrapButtons("nav_act", Actions, w, 24, ActionsEnabled);
        switch (action)
        {
            case 0: NavCommands.Send(new NavCmd { Cmd = "addWaypoint", AddMode = _addMode, InsertAt = _selected }); break;
            case 2:
                _picker.Open(ImGuiNET.ImGui.GetIO().MousePos, NavCommands.RecallLabels, -1, i =>
                    NavCommands.Send(new NavCmd { Cmd = "addRecall", SpellId = NavCommands.RecallIds[i], AddMode = _addMode, InsertAt = _selected }));
                break;
            case 3:
                NavCommands.Send(new NavCmd { Cmd = "clearRoute" });
                _data.Points.Clear();
                _selected = -1;
                RebuildPointText();
                break;
            case 4: NavCommands.Send(new NavCmd { Cmd = "saveRoute" }); break;
            case 5: NavCommands.Send(new NavCmd { Cmd = "dunPatrol" }); break;
        }

        // Chat waypoint: sent as if typed when the route reaches it. Selecting an
        // existing chat waypoint loads its command here; Update saves the edit.
        bool chatSelected = _selected >= 0 && _selected < _data.Points.Count
                            && _data.Points[_selected].Type == "Chat";
        if (_selected != _chatLoadedFor)
        {
            if (chatSelected) SetUtf8(_chat, _data.Points[_selected].Text);
            else if (_chatLoadedFor >= 0) Array.Clear(_chat);   // leaving a chat point: don't keep its text
            _chatLoadedFor = chatSelected ? _selected : -1;
        }
        p = ImGuiNET.ImGui.GetCursorScreenPos();
        float addW = ButtonWidth(AddChat);
        float updW = chatSelected ? ButtonWidth("Update") + 4 : 0;
        bool chatEnter = TextBox("##nav_chat", _chat, p, w - addW - updW - 4, "Chat command, e.g. /ub usei Healing Kit",
            out bool chatActive, ImGuiInputTextFlags.EnterReturnsTrue);
        typing |= chatActive;
        bool update = chatSelected
            && Button("##updchat", "Update", new Vector2(p.X + w - addW - updW, p.Y), new Vector2(updW - 4, 22), Text, BtnFill);
        bool addChat = Button("##addchat", AddChat, new Vector2(p.X + w - addW, p.Y), new Vector2(addW, 22), Text, BtnFill);
        if (update || (chatEnter && chatSelected))
        {
            string text = Utf8(_chat).Trim();
            if (text.Length > 0)
            {
                NavCommands.Send(new NavCmd { Cmd = "editChat", Index = _selected, Text = text });
                _data.Points[_selected].Text = text;
                _data.Points[_selected].Desc = "[Chat] " + text;
                RebuildPointText();
            }
        }
        else if (chatEnter || addChat)
        {
            string text = Utf8(_chat).Trim();
            if (text.Length > 0)
            {
                NavCommands.Send(new NavCmd { Cmd = "addChat", Text = text, AddMode = _addMode, InsertAt = _selected });
                Array.Clear(_chat);
                _chatLoadedFor = -1;
            }
        }
        NextLine(p, 26);

        // Save as a named nav
        p = ImGuiNET.ImGui.GetCursorScreenPos();
        float saveW = ButtonWidth(SaveAs);
        bool saveEnter = TextBox("##nav_saveas", _saveName, p, w - saveW - 4, "Nav name...", out bool saveActive, ImGuiInputTextFlags.EnterReturnsTrue);
        typing |= saveActive;
        bool saveAs = Button("##saveas", SaveAs, new Vector2(p.X + w - saveW, p.Y), new Vector2(saveW, 22), Text, BtnFill);
        if (saveEnter || saveAs)
        {
            string name = Utf8(_saveName).Trim();
            if (name.Length > 0)
            {
                NavCommands.Send(new NavCmd { Cmd = "saveRoute", NavName = name });
                Array.Clear(_saveName);
            }
        }
        NextLine(p, 26);

        // Nav file picker
        if (_navFiles.Length > 0)
        {
            p = ImGuiNET.ImGui.GetCursorScreenPos();
            float lw = ImGuiNET.ImGui.CalcTextSize("Nav:").X + 6;
            ImGuiNET.ImGui.GetWindowDrawList().AddText(new Vector2(p.X, p.Y + (22 - ImGuiNET.ImGui.GetFontSize()) * 0.5f), Mute, "Nav:");
            if (Button("##navfile", _fileLabel, new Vector2(p.X + lw, p.Y), new Vector2(w - lw, 22), Text, BtnFill, leftAlign: true))
                _picker.Open(new Vector2(p.X + lw, p.Y + 22), _navFiles, Array.IndexOf(_navFiles, _data.ActiveNavName), i =>
                    NavCommands.Send(new NavCmd { Cmd = "loadNav", NavName = _navFiles[i] }));
            NextLine(p, 26);
        }

        // Waypoints
        Separator(w);
        Label($"Waypoints ({_data.Points.Count})", Mute);
        float listH = Math.Max(60, Remaining(origin, size));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.ChildBg, PanelBg);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 1));
        ImGuiNET.ImGui.BeginChild("##nav_points", new Vector2(w, listH));
        Waypoints(ImGuiNET.ImGui.GetContentRegionAvail().X);
        ImGuiNET.ImGui.EndChild();
        ImGuiNET.ImGui.PopStyleVar();
        ImGuiNET.ImGui.PopStyleColor();

        _typing = typing;
        End(origin, size);
        _picker.Draw();
    }

    /// <summary>
    /// A checkbox-style toggle button: ticked box and teal text when on, empty box and muted
    /// text when off. Returns true when clicked (the caller flips the value).
    /// </summary>
    private static bool OverlayToggle(string id, string label, bool on, Vector2 pos, float width, string tooltip)
    {
        string text = (on ? PhosphorIcons.CheckSquare : PhosphorIcons.Square) + " " + label;
        bool clicked = Button(id, text, pos, new Vector2(width, 22), on ? Teal : Mute, BtnFill, leftAlign: true);
        if (ImGuiNET.ImGui.IsItemHovered()) ImGuiNET.ImGui.SetTooltip(tooltip);
        return clicked;
    }

    /// <summary>
    /// Nav point reach (yd): how close nav gets to each nav point before moving on. Label,
    /// [-] value [+]; the typed value commits on Enter or when the box loses focus. Saves the
    /// whole settings payload (a copy of the latest snapshot), as the Settings face does.
    /// Hidden until RynthAi's settings have been fetched. Returns true while typing.
    /// </summary>
    private bool ReachRow(float w)
    {
        var snap = UiSources.Settings.Current;
        if (snap != null && snap.Version != _settingsVersion && !_reachTyping)
        {
            _settingsVersion = snap.Version;
            _settings = snap.Value.Clone();
            WriteUtf8(_reach, _settings.FollowNavMin.ToString("0.0#", CultureInfo.InvariantCulture));
        }
        if (_settings == null) return false;

        Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        string label = SettingsSchema.NavPointReachLabel;
        float labelW = ImGuiNET.ImGui.CalcTextSize(label).X;
        dl.AddText(new Vector2(p.X, p.Y + (22 - ImGuiNET.ImGui.GetFontSize()) * 0.5f), Mute, label);
        ImGuiNET.ImGui.SetCursorScreenPos(p);
        ImGuiNET.ImGui.InvisibleButton("##reach_label", new Vector2(Math.Max(1, labelW), 22));
        if (ImGuiNET.ImGui.IsItemHovered()) ImGuiNET.ImGui.SetTooltip(SettingsSchema.NavPointReachTip);

        const float stepW = 22, boxW = 52;
        float right = p.X + w;
        float value = _settings.FollowNavMin;
        if (Button("##reach_minus", "-", new Vector2(right - stepW * 2 - boxW - 8, p.Y), new Vector2(stepW, 22), Text, BtnFill))
            SaveReach(value - 0.1f);
        bool enter = TextBox("##reach", _reach, new Vector2(right - stepW - boxW - 4, p.Y), boxW, "",
            out bool active, ImGuiInputTextFlags.CharsDecimal | ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.EnterReturnsTrue);
        bool commit = enter || ImGuiNET.ImGui.IsItemDeactivatedAfterEdit();
        if (Button("##reach_plus", "+", new Vector2(right - stepW, p.Y), new Vector2(stepW, 22), Text, BtnFill))
            SaveReach(value + 0.1f);
        if (commit)
        {
            if (float.TryParse(Utf8(_reach), NumberStyles.Float, CultureInfo.InvariantCulture, out float typed)) SaveReach(typed);
            else WriteUtf8(_reach, value.ToString("0.0#", CultureInfo.InvariantCulture));   // bad input reverts
        }
        _reachTyping = active;
        NextLine(p, 26);
        return active;
    }

    private void SaveReach(float yards)
    {
        if (_settings == null || float.IsNaN(yards)) return;
        yards = MathF.Round(Math.Clamp(yards, 0.5f, 20f) * 10f) / 10f;
        _settings.FollowNavMin = yards;
        WriteUtf8(_reach, yards.ToString("0.0#", CultureInfo.InvariantCulture));
        SettingsCommands.Save(_settings.Clone());
    }

    private void Waypoints(float w)
    {
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        Vector2 clipMin = ImGuiNET.ImGui.GetWindowPos(), clipMax = clipMin + ImGuiNET.ImGui.GetWindowSize();
        for (int i = 0; i < _data.Points.Count; i++)
        {
            Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
            const float h = 22;
            if (p.Y + h < clipMin.Y || p.Y > clipMax.Y) { ImGuiNET.ImGui.Dummy(new Vector2(w, h)); continue; }
            bool active = i == _data.ActiveNavIndex, selected = i == _selected;
            ImGuiNET.ImGui.PushID(i);
            // Row click selects (drawn first so the X button on top wins).
            ImGuiNET.ImGui.SetCursorScreenPos(p);
            ImGuiNET.ImGui.SetNextItemAllowOverlap();   // the X button on top gets its clicks
            if (ImGuiNET.ImGui.InvisibleButton("##row", new Vector2(w, h))) _selected = i;
            dl.AddRectFilled(p, p + new Vector2(w, h), selected ? Selected : i % 2 == 0 ? PanelBg : RowAlt);
            if (IconButton("##del", PhosphorIcons.X, new Vector2(p.X + 2, p.Y + 2), new Vector2(18, 18), Text, DeleteBg, font: UiFont.Ui11))
            {
                NavCommands.Send(new NavCmd { Cmd = "deletePoint", Index = i });
                if (_selected == i) _selected = -1;
                else if (_selected > i) _selected--;
                _data.Points.RemoveAt(i);
                RebuildPointText();
                ImGuiNET.ImGui.PopID();
                return;
            }
            float ty = p.Y + (h - ImGuiNET.ImGui.GetFontSize()) * 0.5f;
            if (active) dl.AddText(new Vector2(p.X + 24, ty), Teal, "=>");
            dl.PushClipRect(p, p + new Vector2(w - 2, h), true);
            dl.AddText(new Vector2(p.X + 44, ty), active ? Teal : Text, i < _pointText.Count ? _pointText[i] : "");
            dl.PopClipRect();
            ImGuiNET.ImGui.PopID();
            ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(p.X, p.Y + h));
            ImGuiNET.ImGui.Dummy(new Vector2(w, 0));
        }
    }

    private void TakeSnapshot()
    {
        var snap = UiSources.Nav.Current;
        if (snap == null || snap.Version == _seenVersion || _typing) return;
        _seenVersion = snap.Version;
        _data = snap.Value.Clone();
        _navFiles = _data.NavFiles.ToArray();
        if (_selected >= _data.Points.Count) _selected = -1;
        RebuildPointText();
        UpdateLabels();
    }

    private void RebuildPointText()
    {
        _pointText.Clear();
        for (int i = 0; i < _data.Points.Count; i++) _pointText.Add(NavCommands.PointText(_data.Points[i], i));
    }

    private void UpdateLabels()
    {
        _activeLabel = "Active Nav: " + (string.IsNullOrEmpty(_data.ActiveNavName) ? "None (Unsaved)" : _data.ActiveNavName);
        _routeLabel = "Route: " + NavCommands.RouteTypes[NavCommands.RouteIndex(_data.RouteType)];
        _insertLabel = "Insert: " + NavCommands.AddModes[_addMode];
        int fileIdx = string.IsNullOrEmpty(_data.ActiveNavName) ? -1 : _data.NavFiles.IndexOf(_data.ActiveNavName);
        _fileLabel = fileIdx >= 0 ? _data.NavFiles[fileIdx] : "Select nav file...";
    }
}
