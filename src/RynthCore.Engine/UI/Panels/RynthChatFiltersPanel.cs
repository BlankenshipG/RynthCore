// ============================================================================
//  RynthCore.Engine — UI/Panels/RynthChatFiltersPanel.cs
//  Editor for RynthChat filters. Registered as its own "ChatFilters" panel
//  (dockable/floatable).
//
//  Two pages:
//    • Standard — canned UB-IT-style filters (RynthChatPresets) grouped by
//      Combat / Casting / Items / Social / NPCs / Pets. Tick to hide; type a
//      tab name to move the lines there instead. Searchable.
//    • Custom   — user rules. Match mode Contains / Starts with / Ends with
//      (plain text, no regex needed) or Regex. First match wins (▲▼ reorder).
//  A tester at the bottom shows which filter would catch a pasted line.
//
//  Evaluation order (RynthChatPanel.EffectiveTab): custom rules, then presets.
//  Tab set → matching lines MOVE to that tab (still visible in All).
//  Tab empty → matching lines are hidden everywhere.
//  No ComboBox / popups: the overlay's popup hosting is unreliable, so
//  pickers are click-to-cycle buttons.
// ============================================================================

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using RynthCore.Engine.ImGuiBackend;

namespace RynthCore.Engine.UI.Panels;

internal static class RynthChatFiltersPanel
{
    private static readonly IBrush PanelBg     = new SolidColorBrush(Color.FromArgb(0xF2, 0x0A, 0x12, 0x1A));
    private static readonly IBrush FieldBg     = new SolidColorBrush(Color.FromArgb(0xFF, 0x0A, 0x12, 0x1A));
    private static readonly IBrush FieldBorder = new SolidColorBrush(Color.FromArgb(0xFF, 0x26, 0x40, 0x59));
    private static readonly IBrush AccentBg    = new SolidColorBrush(Color.FromArgb(0xFF, 0x26, 0x4C, 0x59));
    private static readonly IBrush ButtonBg    = new SolidColorBrush(Color.FromArgb(0xFF, 0x0F, 0x1F, 0x2E));
    private static readonly IBrush GroupFg     = new SolidColorBrush(Color.FromArgb(0xFF, 0xE6, 0xB4, 0x50));
    private static readonly IBrush ResultFg    = new SolidColorBrush(Color.FromArgb(0xFF, 0x9F, 0xD8, 0xFF));

    // Channels the tester can pretend a line arrived on (must match the plugin's ChatClassifier names).
    private static readonly string[] TestChannels = { "System", "Combat", "Chat", "Channels", "Other" };

    private static readonly string[] ModeLabels = { "Regex", "Contains", "Starts with", "Ends with" };

    internal static Control Create()
    {
        RynthChatPanel.EnsureSettingsLoaded();

        bool showStandard = true;
        string search = "";

        // ── Page switcher + search ───────────────────────────────────────
        var standardBtn = PageButton("Standard filters");
        var customBtn   = PageButton("Custom rules");
        var searchBox   = Field("search standard filters…", 170);
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(4, 4, 4, 2),
            Children = { standardBtn, customBtn, searchBox },
        };

        var hint = new TextBlock
        {
            FontSize = 9,
            Foreground = Brushes.Gray,
            Margin = new Thickness(4, 0, 4, 2),
            TextWrapping = TextWrapping.Wrap,
        };

        var rows = new StackPanel { Orientation = Orientation.Vertical, Spacing = 3, Margin = new Thickness(4) };

        var addBtn = new Button
        {
            Content = "+ Add rule",
            FontSize = 10,
            Padding = new Thickness(8, 3),
            Margin = new Thickness(4, 2, 4, 2),
            Background = AccentBg,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
        };

        // ── Tester ───────────────────────────────────────────────────────
        int testChannel = 0;
        var testBox = Field("test: paste a chat line here", 260);
        var testChanBtn = SmallBtn($"as {TestChannels[0]}", Brushes.White);
        ToolTip.SetTip(testChanBtn, "Channel the test line is treated as arriving on (click to change).");
        var makeRuleBtn = SmallBtn("Make rule", Brushes.White);
        ToolTip.SetTip(makeRuleBtn, "Create a custom 'Contains' rule from the test text (trim it down to the part that matters).");
        var testResult = new TextBlock
        {
            FontSize = 10,
            Foreground = ResultFg,
            Margin = new Thickness(4, 0, 4, 4),
            TextWrapping = TextWrapping.Wrap,
        };
        var testPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Margin = new Thickness(4, 2, 4, 2),
                    Children = { testBox, testChanBtn, makeRuleBtn },
                },
                testResult,
            },
        };

        void RefreshTest() =>
            testResult.Text = RynthChatPanel.DescribeTestLine(testBox.Text ?? "", TestChannels[testChannel]);

        var bottom = new StackPanel { Orientation = Orientation.Vertical, Children = { addBtn, testPanel } };

        var layout = new DockPanel { Background = PanelBg };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(hint, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        layout.Children.Add(header);
        layout.Children.Add(hint);
        layout.Children.Add(bottom);
        layout.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = rows,
        });

        void Changed()
        {
            RynthChatPanel.NotifyFiltersChanged();
            RefreshTest();
        }

        // ── Standard page ────────────────────────────────────────────────
        void BuildStandard()
        {
            rows.Children.Clear();
            foreach (string group in RynthChatPresets.Groups)
            {
                var presets = RynthChatPresets.All
                    .Where(p => p.Group == group && MatchesSearch(p, search))
                    .ToList();
                if (presets.Count == 0) continue;

                int on = RynthChatPresets.All.Count(p => p.Group == group && p.Enabled);
                int total = RynthChatPresets.All.Count(p => p.Group == group);
                var allOn  = SmallBtn("All on", Brushes.White);
                var allOff = SmallBtn("All off", Brushes.White);
                string g = group;
                allOn.Click += (_, _) =>
                {
                    foreach (var p in RynthChatPresets.All.Where(p => p.Group == g)) p.Enabled = true;
                    Changed();
                    BuildStandard();
                };
                allOff.Click += (_, _) =>
                {
                    foreach (var p in RynthChatPresets.All.Where(p => p.Group == g)) p.Enabled = false;
                    Changed();
                    BuildStandard();
                };
                rows.Children.Add(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Margin = new Thickness(0, 6, 0, 1),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"{group}  ({on}/{total} on)",
                            FontSize = 11,
                            FontWeight = FontWeight.Bold,
                            Foreground = GroupFg,
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                        allOn,
                        allOff,
                    },
                });

                foreach (var preset in presets)
                    rows.Children.Add(PresetRow(preset));
            }

            if (rows.Children.Count == 0)
                rows.Children.Add(Muted($"No standard filter matches \"{search}\"."));
        }

        Control PresetRow(RynthChatPresets.Preset p)
        {
            var check = new CheckBox
            {
                IsChecked = p.Enabled,
                Content = new TextBlock { Text = p.Label, FontSize = 10, Foreground = Brushes.White },
                MinWidth = 210,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(check, $"{p.Description}\n\nExample:\n{p.Example}");
            check.IsCheckedChanged += (_, _) =>
            {
                p.Enabled = check.IsChecked == true;
                Changed();
            };

            var tabBox = Field("hide", 110);
            tabBox.Text = p.Tab;
            ToolTip.SetTip(tabBox, "Leave empty to hide these lines. Type a tab name to move them to that tab instead.");
            tabBox.TextChanged += (_, _) =>
            {
                p.Tab = (tabBox.Text ?? "").Trim();
                if (p.Enabled) Changed();
            };

            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Margin = new Thickness(8, 0, 0, 0),
                Children = { check, tabBox },
            };
        }

        // ── Custom page ──────────────────────────────────────────────────
        void BuildCustom()
        {
            var filters = RynthChatPanel.Filters;
            rows.Children.Clear();
            for (int i = 0; i < filters.Count; i++)
                rows.Children.Add(CustomRow(filters, i));
            if (filters.Count == 0)
                rows.Children.Add(Muted("No custom rules yet — click \"+ Add rule\", or use \"Make rule\" on a test line."));
        }

        Control CustomRow(System.Collections.Generic.List<RynthChatPanel.ChatFilterRule> filters, int index)
        {
            var r = filters[index];

            var enabledCheck = new CheckBox
            {
                IsChecked = r.Enabled,
                Margin = new Thickness(0, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            enabledCheck.IsCheckedChanged += (_, _) =>
            {
                r.Enabled = enabledCheck.IsChecked == true;
                Changed();
            };

            var modeBtn = SmallBtn(ModeLabels[(int)r.Mode], Brushes.White);
            modeBtn.MinWidth = 72;
            ToolTip.SetTip(modeBtn, "Click to change how the text is matched.\n" +
                                    "Contains / Starts with / Ends with: plain text, case-insensitive.\n" +
                                    "Regex: regular expression against \"hh:mm:ss Sender: message\".");

            var patternBox = Field(PatternWatermark(r.Mode), 210);
            patternBox.Text = r.Pattern;
            patternBox.BorderBrush = r.Invalid && r.Pattern.Length > 0 ? Brushes.IndianRed : FieldBorder;
            patternBox.TextChanged += (_, _) =>
            {
                r.Pattern = patternBox.Text ?? "";
                r.Recompile();
                patternBox.BorderBrush = r.Invalid && r.Pattern.Length > 0 ? Brushes.IndianRed : FieldBorder;
                Changed();
            };

            modeBtn.Click += (_, _) =>
            {
                r.Mode = (RynthChatPanel.FilterMatchMode)(((int)r.Mode + 1) % ModeLabels.Length);
                modeBtn.Content = ModeLabels[(int)r.Mode];
                patternBox.Watermark = PatternWatermark(r.Mode);
                r.Recompile();
                patternBox.BorderBrush = r.Invalid && r.Pattern.Length > 0 ? Brushes.IndianRed : FieldBorder;
                Changed();
            };

            var tabBox = Field("hide", 100);
            tabBox.Text = r.Tab;
            ToolTip.SetTip(tabBox, "Leave empty to hide matching lines. Type a tab name to move them to that tab.");
            tabBox.TextChanged += (_, _) =>
            {
                r.Tab = (tabBox.Text ?? "").Trim();
                Changed();
            };

            var upBtn = SmallBtn("▲", Brushes.White);
            upBtn.IsEnabled = index > 0;
            upBtn.Click += (_, _) =>
            {
                if (index <= 0) return;
                filters.RemoveAt(index);
                filters.Insert(index - 1, r);
                BuildCustom();
                Changed();
            };

            var downBtn = SmallBtn("▼", Brushes.White);
            downBtn.IsEnabled = index < filters.Count - 1;
            downBtn.Click += (_, _) =>
            {
                if (index >= filters.Count - 1) return;
                filters.RemoveAt(index);
                filters.Insert(index + 1, r);
                BuildCustom();
                Changed();
            };

            var delBtn = SmallBtn("Delete", Brushes.IndianRed);
            delBtn.Click += (_, _) =>
            {
                filters.Remove(r);
                BuildCustom();
                Changed();
            };

            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children = { enabledCheck, modeBtn, patternBox, tabBox, upBtn, downBtn, delBtn },
            };
        }

        // ── Page switching ───────────────────────────────────────────────
        void ShowPage()
        {
            standardBtn.Background = showStandard ? AccentBg : ButtonBg;
            customBtn.Background   = showStandard ? ButtonBg : AccentBg;
            searchBox.IsVisible    = showStandard;
            addBtn.IsVisible       = !showStandard;
            hint.Text = showStandard
                ? "Tick a box to hide those lines. Type a tab name next to it to move them to that tab instead. " +
                  "Hover a filter for an example line. Custom rules are checked before these."
                : "Plain-text rules need no regex: pick Contains / Starts with / Ends with (click the mode button). " +
                  "First matching rule wins — reorder with ▲▼. Empty tab = hide; a tab name = move there.";
            if (showStandard) BuildStandard(); else BuildCustom();
        }

        standardBtn.Click += (_, _) => { showStandard = true; ShowPage(); };
        customBtn.Click   += (_, _) => { showStandard = false; ShowPage(); };
        searchBox.TextChanged += (_, _) =>
        {
            search = (searchBox.Text ?? "").Trim();
            if (showStandard) BuildStandard();
        };

        addBtn.Click += (_, _) =>
        {
            // New rules default to Contains — the easiest mode for non-regex users.
            var rule = new RynthChatPanel.ChatFilterRule { Mode = RynthChatPanel.FilterMatchMode.Contains };
            rule.Recompile();
            RynthChatPanel.Filters.Add(rule);
            BuildCustom();
            // Empty pattern is inert until typed; persisting keeps the row across a relaunch mid-edit.
            Changed();
        };

        testBox.TextChanged += (_, _) => RefreshTest();
        testChanBtn.Click += (_, _) =>
        {
            testChannel = (testChannel + 1) % TestChannels.Length;
            testChanBtn.Content = $"as {TestChannels[testChannel]}";
            RefreshTest();
        };
        makeRuleBtn.Click += (_, _) =>
        {
            string text = (testBox.Text ?? "").Trim();
            if (text.Length == 0) return;
            var rule = new RynthChatPanel.ChatFilterRule { Mode = RynthChatPanel.FilterMatchMode.Contains, Pattern = text };
            rule.Recompile();
            RynthChatPanel.Filters.Add(rule);
            showStandard = false;
            ShowPage();
            Changed();
        };

        ShowPage();
        RefreshTest();
        return layout;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static bool MatchesSearch(RynthChatPresets.Preset p, string search) =>
        search.Length == 0
        || p.Label.Contains(search, StringComparison.OrdinalIgnoreCase)
        || p.Description.Contains(search, StringComparison.OrdinalIgnoreCase)
        || p.Example.Contains(search, StringComparison.OrdinalIgnoreCase)
        || p.Group.Contains(search, StringComparison.OrdinalIgnoreCase);

    private static string PatternWatermark(RynthChatPanel.FilterMatchMode mode) => mode switch
    {
        RynthChatPanel.FilterMatchMode.Contains   => "text anywhere in the line",
        RynthChatPanel.FilterMatchMode.StartsWith => "line starts with…",
        RynthChatPanel.FilterMatchMode.EndsWith   => "line ends with…",
        _                                         => "regex (e.g. AutoRun)",
    };

    /// <summary>Single-line text box that hands keyboard input to Avalonia while focused.</summary>
    private static TextBox Field(string watermark, double width)
    {
        var box = new TextBox
        {
            Watermark = watermark,
            FontSize = 10,
            Width = width,
            Height = 24,
            Background = FieldBg,
            Foreground = Brushes.White,
            BorderBrush = FieldBorder,
            BorderThickness = new Thickness(1),
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(box, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Disabled);
        box.GotFocus  += (_, _) => Win32Backend.AvaloniaTextInputActive = true;
        box.LostFocus += (_, _) => Win32Backend.AvaloniaTextInputActive = false;
        return box;
    }

    private static Button SmallBtn(string label, IBrush fg) => new()
    {
        Content = label,
        FontSize = 10,
        Padding = new Thickness(5, 2),
        Background = ButtonBg,
        Foreground = fg,
        BorderThickness = new Thickness(0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Button PageButton(string label) => new()
    {
        Content = label,
        FontSize = 10,
        Padding = new Thickness(8, 3),
        Background = ButtonBg,
        Foreground = Brushes.White,
        BorderThickness = new Thickness(0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock Muted(string text) => new()
    {
        Text = text,
        FontSize = 10,
        Foreground = Brushes.Gray,
        Margin = new Thickness(2, 6),
        TextWrapping = TextWrapping.Wrap,
    };
}
