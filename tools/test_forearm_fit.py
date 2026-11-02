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
            self.assertEqual((d['rows'],d['low'],d['high']),(49,.005,.245))
            self.assertEqual(d['license'],'CC0-1.0 AND CC-BY-4.0')
            self.assertEqual(d['referenceMeshSha256'],hashlib.sha256((ASSETS/'human-hand.mesh.json').read_bytes()).hexdigest())
            self.assertEqual(d['profile'],'continuous-wrist-source-v2')
            trunk=json.loads((ASSETS/f'trunk-{label}-cage.json').read_text(encoding='utf-8'))
            self.assertEqual(d['shapeRecipe'],trunk['shapeRecipe'])
            for axis,width,reference in [('scaleX','targetWidths','referenceWidths'),('scaleZ','targetDepths','referenceDepths')]:
                self.assertEqual(len(d[axis]),49)
                for scale,target,original in zip(d[axis],d[width],d[reference]):
                    self.assertTrue(math.isfinite(scale) and .5<scale<=1)
                    self.assertLessEqual(target,original+.000001)
                    self.assertAlmostEqual(scale*original,target,places=6)
            for actual,anatomy,original in zip(d['targetWidths'],d['anatomicalWidths'],d['referenceWidths']):
                self.assertAlmostEqual(actual,min(anatomy,original),places=6)
            self.assertTrue(all(math.isfinite(v) and abs(v)<.03 for v in d['offsetZ']))
        male=json.loads((ASSETS/'forearm-fit-male.json').read_text())
        female=json.loads((ASSETS/'forearm-fit-female.json').read_text())
        self.assertLess(female['targetWidths'][24],male['targetWidths'][24]-.006)

    def test_wrist_loft_is_watertight_and_has_no_long_zipper_or_corrugation(self):
        from collections import Counter
        d=json.loads((ASSETS/'human-hand.mesh.json').read_text())
        self.assertEqual(d['wristTransition']['intermediateRings'],7)
        p=list(zip(*[iter(d['positions'])]*3));edges=Counter();directions=Counter()
        for tri in zip(*[iter(d['indices'])]*3):
            for a,b in zip(tri,tri[1:]+tri[:1]):
                key=tuple(sorted((a,b)));edges[key]+=1;directions[key]+=1 if a<b else -1
                if .04499<=p[a][1]<=.07501 and .04499<=p[b][1]<=.07501:
                    self.assertLess(math.dist(p[a],p[b]),.010)
        self.assertTrue(all(n<=2 for n in edges.values()))
        self.assertTrue(all(directions[e]==0 for e,n in edges.items() if n==2))
        self.assertTrue(all(p[i][1]>.27 for e,n in edges.items() if n==1 for i in e))
        for row in range(1,8):
            y=.045+.030*row/8;ring=[v for v in p if abs(v[1]-y)<1e-6]
            self.assertEqual(len(ring),96)
            ring.sort(key=lambda v:math.atan2(v[2]-.010,v[0]));r=[math.hypot(v[0],v[2]-.010) for v in ring]
            self.assertLess(max(abs(r[i-1]-2*r[i]+r[(i+1)%96]) for i in range(96)),.0008)

    @unittest.skipUnless((ROOT/'.tools/sources/makehuman/base.obj').exists(),'Optional source cache')
    def test_exact_source_reexport(self):
        for sex in ('male','female'):
            raw=(json.dumps(export(ROOT/'.tools/sources/makehuman',sex,'forearm'),separators=(',',':'))+'\n').encode()
            self.assertEqual(raw,(ASSETS/f'forearm-fit-{sex}.json').read_bytes())


if __name__=='__main__':unittest.main()
