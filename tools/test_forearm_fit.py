import hashlib
import json
import math
from pathlib import Path
import unittest

from export_upper_arm_support import export

ROOT=Path(__file__).resolve().parents[1]
ASSETS=ROOT/'src/SandBoxSim.Godot/assets/models/human'


class ForearmFitTests(unittest.TestCase):
    def test_source_recipe_and_noninflating_profile(self):
        for sex,label in (('male','masculine'),('female','feminine')):
            d=json.loads((ASSETS/f'forearm-fit-{sex}.json').read_text(encoding='utf-8'))
            self.assertEqual((d['rows'],d['low'],d['high']),(35,.075,.245))
            self.assertEqual(d['license'],'CC0-1.0 AND CC-BY-4.0')
            self.assertEqual(d['referenceMeshSha256'],hashlib.sha256((ASSETS/'human-forearm.mesh.json').read_bytes()).hexdigest())
            trunk=json.loads((ASSETS/f'trunk-{label}-cage.json').read_text(encoding='utf-8'))
            self.assertEqual(d['shapeRecipe'],trunk['shapeRecipe'])
            for axis,width,reference in [('scaleX','targetWidths','referenceWidths'),('scaleZ','targetDepths','referenceDepths')]:
                self.assertEqual(len(d[axis]),35)
                for scale,target,original in zip(d[axis],d[width],d[reference]):
                    self.assertTrue(math.isfinite(scale) and .5<scale<=1)
                    self.assertLessEqual(target,original+.000001)
                    self.assertAlmostEqual(scale*original,target,places=6)
            # This fitted frame retains a shared wrist; the source reference
            # supplies the muscle-belly shape without cutting a wrist notch.
            self.assertTrue(all(w>=d['wristWidth'] for w in d['targetWidths'][8:]))

    @unittest.skipUnless((ROOT/'.tools/sources/makehuman/base.obj').exists(),'Optional source cache')
    def test_exact_source_reexport(self):
        for sex in ('male','female'):
            raw=(json.dumps(export(ROOT/'.tools/sources/makehuman',sex,'forearm'),separators=(',',':'))+'\n').encode()
            self.assertEqual(raw,(ASSETS/f'forearm-fit-{sex}.json').read_bytes())


if __name__=='__main__':unittest.main()
