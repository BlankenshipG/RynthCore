using RynthCore.Engine.UI.Data;
using RynthCore.Engine.UI.Panels;

namespace RynthCore.Engine.UI
{
    /// <summary>Stub of the engine's background writer: runs the write at once.</summary>
    internal static class UiBackgroundWriter
    {
        public static void Enqueue(string label, Action work) => work();
    }
}

namespace RadarSettingsTests
{
    // Offline tests for the radar's show boxes. See the csproj.
    internal static class Program
    {
        private static int _fails, _asserts;
        private static void Check(bool cond, string msg)
        {
            _asserts++;
            if (!cond) { _fails++; Console.WriteLine($"  [FAIL] {msg}"); }
        }

        private static readonly string[] FileKeys =
        {
            "showMonsters", "showNpcs", "showPortals", "showDoors", "showPlayers", "showFellows",
            "showPets", "showVendors", "showCorpses", "showOwnCorpses", "showLifestones", "showGroundItems",
        };

        private static int Main()
        {
            string root = Path.Combine(Path.GetTempPath(), "rynth-radar-settings-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                Run("KindNumbersMatchThePlugin", KindNumbersMatchThePlugin);
                Run("EachKindHasItsOwnBox", EachKindHasItsOwnBox);
                Run("CorpsesOffHidesYourCorpses", CorpsesOffHidesYourCorpses);
                Run("UnknownKindsHidden", UnknownKindsHidden);
                Run("SnapshotFiltersPerBox", SnapshotFiltersPerBox);
                Run("FileRoundTrip", () => FileRoundTrip(root));
            }
            finally
            {
                Environment.SetEnvironmentVariable("RYNTHCORE_RADAR_SETTINGS", null);
                try { Directory.Delete(root, true); } catch { }
            }
            Console.WriteLine();
            Console.WriteLine(_fails == 0 && _asserts > 0 ? $"PASS ({_asserts} checks)" : $"FAIL ({_fails} of {_asserts} checks)");
            return _fails == 0 && _asserts > 0 ? 0 : 1;
        }

        private static void Run(string name, Action body)
        {
            Console.WriteLine(name);
            SetAll(true);
            body();
        }

        private static void SetAll(bool on)
        {
            for (byte k = 0; k < RadarKind.Count; k++) RadarSettingsStore.ShowFlag(k) = on;
        }

        // RynthAi's RynthRadarUi (KindMonster = 0 ... KindGroundItem = 11) sends these numbers;
        // the legend's labels are bound to them.
        private static void KindNumbersMatchThePlugin()
        {
            Check(RadarKind.Monster == 0 && RadarKind.Npc == 1 && RadarKind.Portal == 2 && RadarKind.Door == 3, "kinds 0-3");
            Check(RadarKind.Player == 4 && RadarKind.Fellow == 5 && RadarKind.Pet == 6 && RadarKind.Vendor == 7, "kinds 4-7");
            Check(RadarKind.Corpse == 8 && RadarKind.OwnCorpse == 9 && RadarKind.Lifestone == 10 && RadarKind.GroundItem == 11, "kinds 8-11");
            Check(RadarKind.Count == 12, "12 kinds");
        }

        // Turning one box off hides that kind and nothing else (your corpses aside: they sit under Corpses).
        private static void EachKindHasItsOwnBox()
        {
            for (byte off = 0; off < RadarKind.Count; off++)
            {
                SetAll(true);
                RadarSettingsStore.ShowFlag(off) = false;
                for (byte k = 0; k < RadarKind.Count; k++)
                {
                    bool expected = k != off && !(off == RadarKind.Corpse && k == RadarKind.OwnCorpse);
                    Check(RadarSettingsStore.ShowKind(k) == expected,
                        $"{RadarKind.Name(off)} off: {RadarKind.Name(k)} shown={RadarSettingsStore.ShowKind(k)}, expected {expected}");
                }
                // and back on shows it again
                RadarSettingsStore.ShowFlag(off) = true;
                Check(RadarSettingsStore.ShowKind(off), $"{RadarKind.Name(off)} back on");
            }
        }

        private static void CorpsesOffHidesYourCorpses()
        {
            RadarSettingsStore.ShowCorpses = false;
            Check(!RadarSettingsStore.ShowKind(RadarKind.Corpse), "Corpses off hides corpses");
            Check(!RadarSettingsStore.ShowKind(RadarKind.OwnCorpse), "Corpses off hides your corpses");
            RadarSettingsStore.ShowCorpses = true;
            RadarSettingsStore.ShowOwnCorpses = false;
            Check(RadarSettingsStore.ShowKind(RadarKind.Corpse), "Your corpses off keeps other corpses");
            Check(!RadarSettingsStore.ShowKind(RadarKind.OwnCorpse), "Your corpses off hides your corpses");
        }

        private static void UnknownKindsHidden()
        {
            foreach (byte k in new byte[] { 12, 13, 100, 255 })
                Check(!RadarSettingsStore.ShowKind(k), $"kind {k} hidden");
        }

        // One marker of each kind, as RynthAi writes the snapshot: the shown ones follow the boxes.
        private static void SnapshotFiltersPerBox()
        {
            var marks = string.Join(",", Enumerable.Range(0, RadarKind.Count)
                .Select(k => $"{{\"kind\":{k},\"x\":{k},\"y\":0,\"z\":0,\"label\":\"m{k}\",\"id\":{1000 + k}}}"));
            RadarSnapshot? snap = RadarSnapshot.Parse("{\"mapVersion\":1,\"markers\":[" + marks + "]}");
            Check(snap != null && snap.Markers.Count == RadarKind.Count, "snapshot parsed");
            if (snap == null) return;
            for (int k = 0; k < RadarKind.Count; k++)
                Check(snap.Markers[k].Kind == k && snap.Markers[k].Id == 1000 + k, $"marker {k} kind/id");

            RadarSettingsStore.ShowMonsters = false;
            RadarSettingsStore.ShowPlayers = false;
            RadarSettingsStore.ShowDoors = false;
            var shown = snap.Markers.Where(m => RadarSettingsStore.ShowKind(m.Kind)).Select(m => m.Kind).ToHashSet();
            Check(!shown.Contains(RadarKind.Monster) && !shown.Contains(RadarKind.Player) && !shown.Contains(RadarKind.Door),
                "monsters, players and doors filtered out");
            Check(shown.Contains(RadarKind.Fellow), "fellows are their own box: still shown with Players off");
            Check(shown.Count == RadarKind.Count - 3, $"the other kinds still shown ({shown.Count})");
        }

        // Every box loads from its key and saves back under it.
        private static void FileRoundTrip(string root)
        {
            string seed = Path.Combine(root, "seed.txt");
            var lines = new List<string> { "# test" };
            for (int k = 0; k < RadarKind.Count; k++)
                lines.Add($"{FileKeys[k]}={(k % 2 == 0 ? 0 : 1)}");   // even kinds off
            File.WriteAllLines(seed, lines);
            Environment.SetEnvironmentVariable("RYNTHCORE_RADAR_SETTINGS", seed);
            RadarSettingsStore.Load();
            for (byte k = 0; k < RadarKind.Count; k++)
                Check(RadarSettingsStore.ShowFlag(k) == (k % 2 == 1), $"loaded {FileKeys[k]}");

            string saved = Path.Combine(root, "saved.txt");
            Environment.SetEnvironmentVariable("RYNTHCORE_RADAR_SETTINGS", saved);
            SetAll(false);
            RadarSettingsStore.ShowFlag(RadarKind.Fellow) = true;
            RadarSettingsStore.Save();
            string text = File.Exists(saved) ? File.ReadAllText(saved) : "";
            for (int k = 0; k < RadarKind.Count; k++)
            {
                string want = $"{FileKeys[k]}={(k == RadarKind.Fellow ? 1 : 0)}";
                Check(text.Contains(want + "\n") || text.Contains(want + "\r\n"), $"saved {want}");
            }
        }
    }
}
