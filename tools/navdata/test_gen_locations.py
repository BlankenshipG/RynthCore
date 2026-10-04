"""Offline tests for gen_locations.py: the coordinate conversion (checked with
towns whose map coordinates every player knows), the place classification,
and a small fake world through build(). No server needed.

Run: python tools/navdata/test_gen_locations.py
"""

import json
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_locations as g  # noqa: E402


class CoordinateTests(unittest.TestCase):
    def check(self, cell, x, y, ns, ew):
        got_ns, got_ew = g.cell_to_map(cell, x, y)
        self.assertAlmostEqual(got_ns, ns, delta=0.051, msg='NS of %08X' % cell)
        self.assertAlmostEqual(got_ew, ew, delta=0.051, msg='EW of %08X' % cell)

    def test_known_towns(self):
        # The towns' drop points (the server's points_of_interest) against their
        # well-known map coordinates.
        self.check(0xA9B40019, 84.0, 7.1, 42.1, 33.6)       # Holtburg   42.1N 33.6E
        self.check(0x7D64000D, 31.9, 104.6, -21.5, -1.8)    # Yaraq      21.5S 1.8W
        self.check(0xDA55001D, 84.8, 99.0, -33.5, 72.8)     # Shoushi    33.5S 72.8E

    def test_landblock_corners(self):
        # The map's centre line: landblock 0x7F7F at x = y = 84 is 0.0, 0.0 ... 0x7F*8 = 1016,
        # 1016 + 84/24 = 1019.5.
        self.check(0x7F7F0001, 84.0, 84.0, 0.0, 0.0)
        # The south-west corner of the world.
        self.check(0x00000001, 0.0, 0.0, -101.95, -101.95)

    def test_format(self):
        self.assertEqual(g.fmt_coord(42.08, 33.6), '42.1N, 33.6E')
        self.assertEqual(g.fmt_coord(-21.51, -1.82), '21.5S, 1.8W')

    def test_outdoor_cell(self):
        self.assertTrue(g.is_outdoor_cell(0xA9B40019))
        self.assertTrue(g.is_outdoor_cell(0x828E0000))
        self.assertFalse(g.is_outdoor_cell(0xA9B40100))
        self.assertFalse(g.is_outdoor_cell(0x016C01BC))


class PlaceTests(unittest.TestCase):
    def setUp(self):
        # Landblock A9B4 has outdoor objects whose lowest stands at z 40.
        self.world = g.World([(0xA9B4, 40.0, 60.0, 12)])

    def test_places(self):
        w = self.world
        self.assertEqual(w.place(0xA9B40019, 42.0), 'outdoor')
        self.assertEqual(w.place(0xA9B40105, 41.0), 'building')
        self.assertEqual(w.place(0xA9B40105, 10.0), 'underground')
        self.assertEqual(w.place(0x016C01BC, 0.0), 'dungeon')
        self.assertTrue(w.is_surface(0xA9B40105, 41.0))
        self.assertFalse(w.is_surface(0x016C01BC, 0.0))

    def test_dungeon_names(self):
        self.assertEqual(g.dungeon_name('Portal to Mines of Despair'), 'Mines of Despair')
        self.assertEqual(g.dungeon_name('Mines of Despair Portal'), 'Mines of Despair')
        self.assertEqual(g.dungeon_name('Four Towers Entrance'), 'Four Towers')
        self.assertEqual(g.dungeon_name('Portal'), 'Portal')

    def test_unescape(self):
        self.assertIsNone(g.unescape('NULL'))
        self.assertEqual(g.unescape('a\\tb\\nc\\\\d'), 'a\tb\nc\\d')


def fake_rows():
    """A tiny world: Holtburg and Yaraq with portals both ways, a dungeon under
    a portal near Holtburg, a lifestone, a vendor, an NPC and a monster."""
    def t(*v):
        return [None if x is None else str(x) for x in v]
    return {
        'surface': [t(0xA9B4, 40.0, 60.0, 10), t(0x7D64, 5.0, 9.0, 10), t(0xA9B3, 30.0, 50.0, 3)],
        'poi': [t('Holtburg', 42820, 0xA9B40019, 84.0, 7.1, 42.0), t('Yaraq', 42824, 0x7D64000D, 31.9, 104.6, 6.0),
                t('Marketplace', 23032, 0x016C01BC, 49.2, -31.9, 0.0)],
        'portals': [
            # guid wcid cell x y z name dcell dx dy dz minlvl maxlvl quest desc bitmask
            t(1, 100, 0xA9B40020, 90.0, 20.0, 42.0, 'Yaraq Portal', 0x7D64000D, 31.9, 104.6, 6.0, None, None, None, None, 1),
            t(2, 101, 0x7D640010, 40.0, 100.0, 6.0, 'Holtburg Portal', 0xA9B40019, 84.0, 7.1, 42.0, None, None, None, None, 1),
            t(3, 102, 0xA9B30005, 30.0, 30.0, 31.0, 'Portal to Glimmering Cave', 0x01AB0100, 10.0, -10.0, 0.0, 12, None, None,
              'A cave.', 1),
            t(4, 103, 0x01AB0105, 12.0, -12.0, 0.0, 'Holtburg', 0xA9B40019, 84.0, 7.1, 42.0, None, None, None, None, 1),
            t(5, 104, 0xA9B40021, 95.0, 25.0, 42.0, 'Quest Portal', 0x7D64000D, 31.9, 104.6, 6.0, None, None, 'flag', None, 1),
            t(6, 105, 0xA9B40022, 96.0, 26.0, 42.0, 'Sealed Portal', 0x7D64000D, 31.9, 104.6, 6.0, None, None, None, None, 0),
            t(7, 100, 0xA9B40020, 90.0, 20.0, 42.0, 'Yaraq Portal', 0x7D64000D, 31.9, 104.6, 6.0, None, None, None, None, 1),
        ],
        'places': [
            # guid wcid wtype cell x y z name template attackable
            t(10, 509, 25, 0xA9B40018, 80.0, 10.0, 42.0, 'Life Stone', None, None),
            t(11, 600, 12, 0xA9B40150, 70.0, 30.0, 44.0, 'Shopkeeper Renald', 'Shopkeeper', None),
            t(12, 601, 10, 0xA9B4001A, 60.0, 40.0, 42.0, 'Town Crier', 'Herald', 0),
            t(13, 602, 10, 0xA9B4001B, 61.0, 41.0, 42.0, 'Drudge Skulker', None, 1),
            t(14, 603, 10, 0xA9B4001C, 62.0, 42.0, 42.0, 'Invisible Event Controller', None, 0),
            t(15, 604, 10, 0x01AB0110, 20.0, -20.0, 0.0, 'Cave Hermit', None, 0),
        ],
        'recalls': [
            t(2041, 'Aerlinthe Recall', 0xBAE8001D, 84.0, 105.0, 26.0),
            t(4907, 'Celestial Hand Stronghold Recall', 0x016002A5, 80.0, -160.0, 0.0),
            t(4213, 'Colosseum Recall', 0x00AF0118, 40.0, -13.2, 0.0),
        ],
    }


class BuildTests(unittest.TestCase):
    def setUp(self):
        self.locs, self.recalls, self.route = g.build(fake_rows())
        self.by_name = {}
        for l in self.locs:
            self.by_name.setdefault((l['type'], l['name']), l)

    def test_towns(self):
        h = self.by_name[('Town', 'Holtburg')]
        self.assertAlmostEqual(h['ns'], 42.08, delta=0.01)
        self.assertAlmostEqual(h['ew'], 33.6, delta=0.01)
        m = self.by_name[('Landmark', 'Marketplace')]
        self.assertNotIn('ns', m)   # a dungeon: no map position

    def test_portals_and_dungeons(self):
        p = self.by_name[('Portal', 'Yaraq Portal')]
        self.assertEqual(p['dest']['place'], 'outdoor')
        self.assertEqual(sum(1 for l in self.locs if l['name'] == 'Yaraq Portal'), 1)   # duplicate dropped
        d = self.by_name[('Dungeon', 'Glimmering Cave')]
        self.assertEqual(d['lb'], '01AB')
        self.assertEqual(d['minLevel'], 12)
        cave = self.by_name[('Portal', 'Portal to Glimmering Cave')]
        self.assertEqual(cave['dest']['name'], 'Glimmering Cave')
        exit_ = self.by_name[('Portal', 'Holtburg')]
        self.assertNotIn('ns', exit_)
        self.assertIn('Glimmering Cave', exit_['desc'])
        self.assertTrue(self.by_name[('Portal', 'Quest Portal')]['quest'])
        self.assertTrue(self.by_name[('Portal', 'Sealed Portal')]['closed'])

    def test_places(self):
        self.assertIn(('Lifestone', 'Life Stone'), self.by_name)
        v = self.by_name[('Vendor', 'Shopkeeper Renald')]
        self.assertEqual(v['place'], 'building')
        self.assertNotIn(('NPC', 'Drudge Skulker'), self.by_name)              # attackable: a monster
        self.assertNotIn(('NPC', 'Invisible Event Controller'), self.by_name)  # not a person
        self.assertEqual(self.by_name[('NPC', 'Town Crier')]['desc'], 'Herald.')
        self.assertIn('Glimmering Cave', self.by_name[('NPC', 'Cave Hermit')]['desc'])

    def test_recalls(self):
        spells = {r['spell']: r for r in self.recalls}
        self.assertIn(2041, spells)
        self.assertNotIn(4907, spells)   # placeholder destination
        self.assertEqual(spells[4213]['place'], 'dungeon')
        self.assertAlmostEqual(spells[2041]['ns'], 84.09, delta=0.01)

    def test_routing_list(self):
        names = [r[4] for r in self.route]
        # Surface to surface only; no quest-locked, closed, dungeon or duplicate portals.
        self.assertEqual(sorted(names), ['Holtburg Portal', 'Yaraq Portal'])
        y = [r for r in self.route if r[4] == 'Yaraq Portal'][0]
        self.assertAlmostEqual(y[2], -21.51, delta=0.01)
        self.assertAlmostEqual(y[3], -1.82, delta=0.01)

    def test_outputs(self):
        import tempfile
        with tempfile.TemporaryDirectory() as d:
            g.write_outputs(d, self.locs, self.recalls, self.route, 'test')
            doc = json.load(open(os.path.join(d, 'locations.json'), encoding='utf-8'))
            self.assertEqual(doc['version'], 1)
            self.assertEqual(len(doc['locations']), len(self.locs))
            lines = open(os.path.join(d, 'portals.tsv'), encoding='utf-8').read().splitlines()
            self.assertEqual(len(lines), 2)
            self.assertEqual(len(lines[0].split('\t')), 5)


class GeneratedDataTests(unittest.TestCase):
    """Checks the committed output (tools/navdata/NavData), when it is there."""
    PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'NavData', 'locations.json')

    def test_known_towns_in_data(self):
        if not os.path.exists(self.PATH):
            self.skipTest('no generated locations.json')
        doc = json.load(open(self.PATH, encoding='utf-8'))
        towns = {l['name']: l for l in doc['locations'] if l['type'] == 'Town'}
        for name, ns, ew in (('Holtburg', 42.1, 33.6), ('Yaraq', -21.5, -1.8), ('Shoushi', -33.5, 72.8)):
            self.assertIn(name, towns)
            self.assertAlmostEqual(towns[name]['ns'], ns, delta=0.1, msg=name)
            self.assertAlmostEqual(towns[name]['ew'], ew, delta=0.1, msg=name)


if __name__ == '__main__':
    unittest.main(verbosity=1)
