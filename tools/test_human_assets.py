"""Check shipped head/normal pairs without requiring Blender or network access."""
import hashlib
import json
import math
from pathlib import Path
import struct
import unittest

from evaluate import validate_png

ROOT = Path(__file__).resolve().parents[1]/'src/SandBoxSim.Godot/assets/models/human'
NAMES = ('human-head', 'human-scan', 'human-feminine')


class HumanAssetTests(unittest.TestCase):
    def test_sculpt_normal_matches_mesh_and_import_contract(self):
        for name in NAMES:
            with self.subTest(family=name):
                data = json.loads((ROOT/(name+'.mesh.json')).read_text())
                detail = data['detailNormal']
                self.assertEqual(detail['file'], name+'_normal.png')
                raw = (ROOT/detail['file']).read_bytes()
                self.assertEqual(hashlib.sha256(raw).hexdigest(), detail['sha256'])
                validate_png(raw)
                self.assertEqual(struct.unpack('>II', raw[16:24]), (2048, 2048))
                settings = (ROOT/(detail['file']+'.import')).read_text(encoding='utf-8-sig')
                self.assertIn('mipmaps/generate=true', settings)
                self.assertIn('compress/normal_map=1', settings)

    def test_packed_head_atlas_retains_geometry_and_color(self):
        for name in NAMES:
            with self.subTest(family=name):
                data = json.loads((ROOT/(name+'.mesh.json')).read_text())
                surface = data['head']
                count = len(surface['positions'])//3
                self.assertEqual(len(surface['normals']), count*3)
                self.assertEqual(len(surface['uv']), count*2)
                self.assertEqual(len(surface['colors']), count*4)
                self.assertEqual(set(surface['indices']), set(range(count)))
                self.assertLessEqual(len(surface['indices'])//3, 40000)
                self.assertTrue(all(math.isfinite(x) for key in ('positions', 'normals', 'uv', 'colors') for x in surface[key]))
                self.assertTrue(all(-.000001 <= x <= 1.000001 for x in surface['uv']))
                self.assertEqual(len(data['eyes']), 2)
                self.assertGreater(len(data['lips']['positions']), 300)

    def test_independent_families_keep_their_source_attribution(self):
        sources = [json.loads((ROOT/(name+'.mesh.json')).read_text())['source'] for name in NAMES]
        self.assertEqual(len({source['sha256'] for source in sources}), 3)
        self.assertEqual(sources[0]['license'], 'CC-BY-4.0')
        self.assertEqual([source['license'] for source in sources[1:]], ['CC0-1.0', 'CC0-1.0'])
        license_text = (ROOT/'LICENSE.txt').read_text()
        self.assertIn('Dan Ulrich', license_text)
        self.assertIn('CC0', license_text)


if __name__ == '__main__':
    unittest.main()
