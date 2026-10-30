import hashlib
import json
import math
from pathlib import Path
import unittest

from export_upper_arm_support import export

ROOT=Path(__file__).resolve().parents[1]
ASSETS=ROOT/'src/SandBoxSim.Godot/assets/models/human'


class UpperArmSupportTests(unittest.TestCase):
    def test_shipped_anatomical_sections_and_provenance(self):
        for frame in ('male','female'):
            data=json.loads((ASSETS/f'upper-arm-{frame}.json').read_text(encoding='utf-8'))
            self.assertEqual(data['license'],'CC0-1.0')
            self.assertEqual((data['rows'],data['columns']),(25,64))
            self.assertEqual(len(data['radii']),1600)
            self.assertTrue(all(math.isfinite(r) and .012<r<.105 for r in data['radii']))
            self.assertGreater(math.dist(data['sourceShoulder'],data['sourceElbow']),.20)
            self.assertEqual(data['targetLength'],.28)
            self.assertIn('base.obj',data['sourceSha256'])

    def test_deltoid_to_elbow_and_frame_specific_volume(self):
        data=[json.loads((ASSETS/f'upper-arm-{f}.json').read_text(encoding='utf-8')) for f in ('male','female')]
        for d in data:
            r=d['radii'];self.assertGreater(sum(r[4*64:5*64]),sum(r[20*64:21*64]))
            for row in range(25):
                band=r[row*64:(row+1)*64]
                self.assertGreater(max(band)-min(band),.005) # Authored asymmetry, not circular tubes.
        self.assertGreater(sum(data[0]['radii'][8*64:20*64]),sum(data[1]['radii'][8*64:20*64]))

    @unittest.skipUnless((ROOT/'.tools/sources/makehuman/base.obj').exists(),'Verified source bundle is optional for consumers')
    def test_reexport_matches_committed_source_sections(self):
        for frame in ('male','female'):
            actual=(json.dumps(export(ROOT/'.tools/sources/makehuman',frame),separators=(',',':'))+'\n').encode('utf-8')
            self.assertEqual(hashlib.sha256(actual).digest(),hashlib.sha256((ASSETS/f'upper-arm-{frame}.json').read_bytes()).digest())


if __name__=='__main__':unittest.main()
