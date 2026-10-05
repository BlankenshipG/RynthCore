using System.Globalization;
using RynthCore.Engine.ImGuiBackend.Hud;
using RynthCore.Engine.UI.Data;

namespace RynthCore.Engine.UI
{
    /// <summary>Stub: counts saves, never runs them (no file I/O in the tests).</summary>
    internal static class UiBackgroundWriter
    {
        public static int Enqueued;
        public static void Enqueue(string label, Action work) => Enqueued++;
    }
}

namespace PlateSizingTests
{
    using S = MonsterHudSettings;

    /// <summary>
    /// The nameplate sizing settings (per plate type: bar width, bar height, text size) and the
    /// ArgbOpacity helper behind the Vision overlay opacity sliders.
    /// </summary>
    internal static class Program
    {
        private static int _checks, _failed;
        private static string _case = "";

        private static int Main()
        {
            DefaultsAreTodaysLook();
            RoundTrip();
            OldFileLoads();
            Clamping();
            ResetSizesOnlyTouchesSizes();
            ResetAllResetsSizes();
            RefactorKeepsEveryKey();
            OpacityDefaults();
            OpacityRoundTripsEveryAlpha();
            OpacitySetKeepsRgb();

            Console.WriteLine();
            Console.WriteLine($"{_checks - _failed}/{_checks} checks passed.");
            Console.WriteLine(_failed == 0 ? "ALL PLATE SIZING TESTS PASSED." : $"{_failed} FAILED.");
            return _failed == 0 ? 0 : 1;
        }

        private static (string Key, Func<float> Get, Action<float> Set, float Min, float Max)[] Sizes => new (string, Func<float>, Action<float>, float, float)[]
        {
            ("monsterBarWidth", () => S.MonsterBarWidth, v => S.MonsterBarWidth = v, S.MinBarWidth, S.MaxBarWidth),
            ("monsterBarHeight", () => S.MonsterBarHeight, v => S.MonsterBarHeight = v, S.MinBarHeight, S.MaxBarHeight),
            ("monsterTextSize", () => S.MonsterTextSize, v => S.MonsterTextSize = v, S.MinTextSize, S.MaxTextSize),
            ("npcTextSize", () => S.NpcTextSize, v => S.NpcTextSize = v, S.MinTextSize, S.MaxTextSize),
            ("playerTextSize", () => S.PlayerTextSize, v => S.PlayerTextSize = v, S.MinTextSize, S.MaxTextSize),
            ("selfBarWidth", () => S.SelfBarWidth, v => S.SelfBarWidth = v, S.MinBarWidth, S.MaxBarWidth),
            ("selfBarHeight", () => S.SelfBarHeight, v => S.SelfBarHeight = v, S.MinBarHeight, S.MaxBarHeight),
            ("selfTextSize", () => S.SelfTextSize, v => S.SelfTextSize = v, S.MinTextSize, S.MaxTextSize),
        };

        private static string[] Written()
        {
            var sw = new StringWriter(CultureInfo.InvariantCulture);
            S.WriteAll(sw);
            return sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
        }

        // ── nameplate sizes ─────────────────────────────────────────────────

        private static void DefaultsAreTodaysLook()
        {
            Case("defaults: every size multiplier is 1 (today's look), inside its range");
            foreach (var s in Sizes)
            {
                Check(s.Get() == 1.0f, $"{s.Key} defaults to 1");
                Check(s.Min <= 1.0f && 1.0f <= s.Max, $"{s.Key} range [{s.Min}, {s.Max}] holds 1");
            }
            string[] lines = Written();
            foreach (var s in Sizes)
                Check(lines.Contains(s.Key + "=1"), $"{s.Key}=1 written");
        }

        private static void RoundTrip()
        {
            Case("save/load: non-default sizes come back");
            float[] values = { 1.75f, 0.5f, 1.25f, 0.8f, 2f, 2.5f, 3f, 0.65f };
            var sizes = Sizes;
            for (int i = 0; i < sizes.Length; i++) sizes[i].Set(values[i]);
            S.Scale = 1.3f; S.SelfNumbers = true;
            string[] lines = Written();

            S.ResetToDefaults();
            Check(S.MonsterBarWidth == 1f && S.SelfTextSize == 1f, "reset before reload");
            S.ApplyLines(lines);
            for (int i = 0; i < sizes.Length; i++)
                Check(MathF.Abs(sizes[i].Get() - values[i]) < 0.001f, $"{sizes[i].Key} = {values[i]} after reload (got {sizes[i].Get()})");
            Check(MathF.Abs(S.Scale - 1.3f) < 0.001f && S.SelfNumbers, "other settings in the same file still load");
            S.ResetToDefaults();
        }

        private static void OldFileLoads()
        {
            Case("a file from before 2026-10-05 (no size keys) loads with sizes at 1");
            S.MonsterBarWidth = 2f; S.NpcTextSize = 1.5f;
            S.ResetToDefaults();
            S.ApplyLines(new[]
            {
                "# RynthCore monster nameplates - auto-generated, hand-edits OK. /rc plates in game.",
                "enabled=1", "scale=1.2", "opacity=0.8", "selfNumbers=1", "npcNames=1",
            });
            foreach (var s in Sizes) Check(s.Get() == 1f, $"{s.Key} stays 1");
            Check(MathF.Abs(S.Scale - 1.2f) < 0.001f, "scale from the old file");
        }

        private static void Clamping()
        {
            Case("load: out-of-range values clamp, junk is ignored");
            S.ResetToDefaults();
            S.ApplyLines(new[]
            {
                "monsterBarWidth=9", "monsterBarHeight=0.01", "monsterTextSize=-3",
                "npcTextSize=abc", "playerTextSize=", "selfBarWidth=0.1", "selfBarHeight=99", "selfTextSize=1e9",
            });
            Check(S.MonsterBarWidth == S.MaxBarWidth, "bar width capped");
            Check(S.MonsterBarHeight == S.MinBarHeight, "bar height floored");
            Check(S.MonsterTextSize == S.MinTextSize, "negative text size floored");
            Check(S.NpcTextSize == 1f, "junk ignored (NPC)");
            Check(S.PlayerTextSize == 1f, "empty ignored (player)");
            Check(S.SelfBarWidth == S.MinBarWidth, "self width floored");
            Check(S.SelfBarHeight == S.MaxBarHeight, "self height capped");
            Check(S.SelfTextSize == S.MaxTextSize, "self text capped");
            S.ResetToDefaults();
        }

        private static void ResetSizesOnlyTouchesSizes()
        {
            Case("Reset bar and text sizes: sizes back to 1, nothing else, saved");
            S.ResetToDefaults();
            foreach (var s in Sizes) s.Set(1.5f);
            S.Scale = 1.4f; S.MaxPlates = 7; S.SelfName = true;
            int saves = RynthCore.Engine.UI.UiBackgroundWriter.Enqueued;
            S.ResetSizes();
            foreach (var s in Sizes) Check(s.Get() == 1f, $"{s.Key} back to 1");
            Check(MathF.Abs(S.Scale - 1.4f) < 0.001f && S.MaxPlates == 7 && S.SelfName, "other settings kept");
            Check(RynthCore.Engine.UI.UiBackgroundWriter.Enqueued == saves + 1, "one save queued");
            S.ResetToDefaults();
        }

        private static void ResetAllResetsSizes()
        {
            Case("Reset all to defaults also resets the sizes");
            foreach (var s in Sizes) s.Set(0.7f);
            S.ResetToDefaults();
            foreach (var s in Sizes) Check(s.Get() == 1f, $"{s.Key} back to 1");
        }

        private static void RefactorKeepsEveryKey()
        {
            Case("write -> apply -> write is stable (the load/save split kept every key)");
            S.ResetToDefaults();
            S.Enabled = false; S.MaxDistance = 50f; S.Filter = PlateFilter.Engaged; S.SelfPlacement = SelfPlacement.Follow;
            S.SelfPosition = SelfPlatePosition.Left; S.GainTime = 2.25f; S.NumSize = 1.5f; S.SelfFixedX = 0.25f;
            S.MonsterBarHeight = 2f; S.PlayerTextSize = 0.75f;
            string[] first = Written();
            S.ResetToDefaults();
            S.ApplyLines(first);
            string[] second = Written();
            Check(first.SequenceEqual(second), "identical text after a reload");
            Check(first.Length == second.Length && first.Length > 50, $"{first.Length} lines written");
            S.ResetToDefaults();
        }

        // ── overlay opacity (colour alpha) ───────────────────────────────────

        private static readonly (string Name, uint Argb, int Pct)[] TodaysColours =
        {
            ("slopes (impassable terrain)", 0x60FF2020u, 38),
            ("water", 0x600060FFu, 38),
            ("radar ring", 0x80FFD000u, 50),
        };

        private static void OpacityDefaults()
        {
            Case("opacity: today's colours show today's opacity, untouched they never change");
            foreach (var c in TodaysColours)
            {
                float p = ArgbOpacity.Percent(c.Argb);
                Check((int)MathF.Round(p) == c.Pct, $"{c.Name} shows {c.Pct}% (got {p:0.##})");
                Check(ArgbOpacity.WithPercent(c.Argb, p) == c.Argb, $"{c.Name} unchanged when re-applied");
            }
            Check(ArgbOpacity.Percent(0x00123456) == 0f && ArgbOpacity.Percent(0xFF123456) == 100f, "0 and 100 at the ends");
        }

        private static void OpacityRoundTripsEveryAlpha()
        {
            Case("opacity: every alpha byte survives percent and back");
            int bad = 0;
            for (uint a = 0; a <= 255; a++)
            {
                uint c = (a << 24) | 0x00ABCDEF;
                if (ArgbOpacity.WithPercent(c, ArgbOpacity.Percent(c)) != c) bad++;
            }
            Check(bad == 0, $"{bad} of 256 alpha values changed");
        }

        private static void OpacitySetKeepsRgb()
        {
            Case("opacity: setting it changes only the alpha, clamped to 0..100");
            Check(ArgbOpacity.WithPercent(0x60FF2020, 100f) == 0xFFFF2020, "100% = solid, RGB kept");
            Check(ArgbOpacity.WithPercent(0x60FF2020, 0f) == 0x00FF2020, "0% = invisible, RGB kept");
            Check(ArgbOpacity.WithPercent(0x80FFD000, 25f) == 0x40FFD000, "25% = 0x40");
            Check(ArgbOpacity.WithPercent(0x80FFD000, 150f) == 0xFFFFD000, "above 100 clamps");
            Check(ArgbOpacity.WithPercent(0x80FFD000, -5f) == 0x00FFD000, "below 0 clamps");
            Check(ArgbOpacity.WithPercent(0x80FFD000, float.NaN) == 0x00FFD000, "NaN is 0");
        }

        // ── harness ─────────────────────────────────────────────────────────

        private static void Case(string name)
        {
            _case = name;
            Console.WriteLine("- " + name);
        }

        private static void Check(bool ok, string what)
        {
            _checks++;
            if (ok) return;
            _failed++;
            Console.WriteLine($"  FAIL [{_case}] {what}");
        }
    }
}
