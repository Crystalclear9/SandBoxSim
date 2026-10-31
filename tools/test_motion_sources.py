import hashlib
import json
import math
from pathlib import Path
import unittest

from export_cmu_arm_motion import export,parse_asf,parse_amc,forward
from export_shoulder_surface import export as export_shoulder,bind_shoulder

ROOT=Path(__file__).resolve().parents[1]
MOTION=ROOT/'src/SandBoxSim.Godot/assets/motions/cmu-143-30-stretch.json'
HUMAN=ROOT/'src/SandBoxSim.Godot/assets/models/human'


class MotionSourceTests(unittest.TestCase):
    def test_authored_bind_preserves_pivot_radius(self):
        pivot=(.2,1.2,0);point=(.27,1.21,-.04)
        before=math.dist(point,pivot)
        for angle in (0,.5,1.2):
            for weights in ((1,0,0,0),(0,1,0,0),(.2,.4,.3,.1),(0,0,1,0)):
                posed=bind_shoulder(point,pivot,(0,0,1),angle,weights)
                self.assertAlmostEqual(math.dist(posed,pivot),before,places=12)
        self.assertEqual(bind_shoulder(point,pivot,(0,0,1),1.2,(1,0,0,0)),point)

    def test_asf_axis_basis_and_parent_rotation(self):
        text=':bonedata\nbegin\nname arm\ndirection 1 0 0\nlength 2\naxis 0 0 0 XYZ\ndof rz\nend\n:hierarchy\nbegin\nroot arm\nend'
        bones,children=parse_asf(text);frames=parse_amc(':DEGREES\n1\nroot 0 0 0 0 0 90\narm 90')
        rotations,points=forward(bones,children,frames[0]);self.assertAlmostEqual(points['arm'][0],-2);self.assertAlmostEqual(points['arm'][1],0,places=6)

    def test_motion_directions_wrist_axes_and_non_symmetric_capture(self):
        data=json.loads(MOTION.read_text(encoding='utf-8'));samples=data['samples']
        self.assertEqual((data['fps'],len(samples)),(120,370));self.assertEqual(data['license'],'LicenseRef-CMU-Mocap')
        self.assertTrue(all(len(v)==39 and all(math.isfinite(x) for x in v) for v in samples))
        for v in samples:
            for offset in (0,3,6,9):self.assertAlmostEqual(sum(x*x for x in v[offset:offset+3]),1,places=5)
            for lower,palm_y in ((3,24),(9,33)):self.assertAlmostEqual(-sum(v[lower+i]*v[palm_y+i] for i in range(3)),1,places=5)
        self.assertTrue(any(abs(v[0]+v[6])>.1 for v in samples))
        self.assertGreater(max(v[1] for v in samples),.05)

    def test_shoulder_surface_is_weighted_anatomy_with_valid_topology(self):
        for sex in ('male','female'):
            data=json.loads((HUMAN/f'shoulder-surface-{sex}.json').read_text(encoding='utf-8'));points=data['vertices'];faces=data['triangles']
            self.assertEqual(data['license'],'CC0-1.0');self.assertGreater(len(points),600);self.assertGreater(len(faces),1100)
            self.assertIn('rig/default_weights.mhw',data['sourceSha256'])
            self.assertEqual(len(data['aPoseVertices']),len(points));self.assertEqual(len(data['skinWeights']),len(points))
            self.assertTrue(all(min(w)>=0 and abs(sum(w)-1)<1e-5 for w in data['skinWeights']))
            self.assertTrue(all(len(p)==3 and all(math.isfinite(x) for x in p) for p in points))
            self.assertTrue(all(len(set(f))==3 and all(0<=i<len(points) for i in f) for f in faces))

    @unittest.skipUnless((ROOT/'.tools/sources/mocap/cmu-143/143.asf').exists(),'Optional original capture bundle')
    def test_motion_reexport_matches_and_ignores_root_travel(self):
        source=ROOT/'.tools/sources/mocap/cmu-143';data=export(source)
        encoded=(json.dumps(data,separators=(',',':'))+'\n').encode();self.assertEqual(hashlib.sha256(encoded).digest(),hashlib.sha256(MOTION.read_bytes()).digest())
        bones,children=parse_asf((source/'143.asf').read_text(encoding='utf-8'));frame=parse_amc((source/'143_30.amc').read_text(encoding='utf-8'))[0]
        r,a=forward(bones,children,frame);frame['root'][0]+=123;r,b=forward(bones,children,frame)
        self.assertAlmostEqual(a['lhumerus'][0]-a['lclavicle'][0],b['lhumerus'][0]-b['lclavicle'][0])

    @unittest.skipUnless((ROOT/'.tools/sources/makehuman/rig/default_weights.mhw').exists(),'Optional original anatomy bundle')
    def test_shoulder_reexport_matches(self):
        for sex in ('male','female'):
            encoded=(json.dumps(export_shoulder(ROOT/'.tools/sources/makehuman',sex),separators=(',',':'))+'\n').encode()
            self.assertEqual(hashlib.sha256(encoded).digest(),hashlib.sha256((HUMAN/f'shoulder-surface-{sex}.json').read_bytes()).digest())


if __name__=='__main__':unittest.main()
