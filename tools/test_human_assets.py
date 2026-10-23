"""Check shipped head/normal pairs without requiring Blender or network access."""
import hashlib
import json
import math
from pathlib import Path
import struct
import unittest
from collections import Counter

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
        self.assertEqual([source['license'] for source in sources], ['CC0-1.0']*3)
        license_text = (ROOT/'LICENSE.txt').read_text()
        self.assertIn('Dan Ulrich', license_text)
        self.assertIn('CC0', license_text)

    def test_eyelids_follow_each_source_aperture(self):
        for name in NAMES:
            data=json.loads((ROOT/(name+'.mesh.json')).read_text())
            self.assertEqual(len(data['eyelids']),2)
            for eye in data['eyelids']:
                upper,lower=eye['upper'],eye['lower']
                self.assertEqual(len(upper),49)
                self.assertEqual(len(lower),49)
                self.assertTrue(all(math.isfinite(v) for curve in (upper,lower) for p in curve for v in p))
                self.assertTrue(all(u[0]==l[0] and u[1]>=l[1] for u,l in zip(upper,lower)))
                self.assertTrue(all(upper[i][0]<upper[i+1][0] for i in range(48)))
                self.assertGreater(upper[-1][0]-upper[0][0],.018)
                self.assertLess(upper[-1][0]-upper[0][0],.030)
                self.assertGreater(max(u[1]-l[1] for u,l in zip(upper,lower)),.004)
                self.assertLess(max(u[1]-l[1] for u,l in zip(upper,lower)),.014)
                self.assertEqual(upper[0],lower[0]);self.assertEqual(upper[-1],lower[-1])

    def test_neck_has_no_exposed_upper_boundary(self):
        for name in NAMES:
            surface=json.loads((ROOT/(name+'.mesh.json')).read_text())['head']
            values=surface['positions'];points=[tuple(round(x,6) for x in values[i:i+3]) for i in range(0,len(values),3)]
            edges=Counter()
            for i in range(0,len(surface['indices']),3):
                a,b,c=[points[j] for j in surface['indices'][i:i+3]]
                for u,v in ((a,b),(b,c),(c,a)):edges[tuple(sorted((u,v)))]+=1
            neck=[p for edge,count in edges.items() if count==1 for p in edge if p[1]<-.09]
            self.assertTrue(neck,name)
            # Cropping keeps whole triangles: the edge may sit up to one
            # neck-ring spacing above the -.149 m cut, but not at the jaw.
            self.assertLess(max(p[1] for p in neck),-.135,name)

    def test_distant_head_is_a_bounded_anatomical_derivative(self):
        for name in NAMES:
            data=json.loads((ROOT/(name+'.mesh.json')).read_text());lod=data['headLod']
            self.assertGreater(len(lod['indices'])//3,1500)
            self.assertLessEqual(len(lod['indices'])//3,2400)
            self.assertTrue(all(math.isfinite(v) for field in ('positions','normals','colors') for v in lod[field]))
            self.assertEqual(set(lod['indices']),set(range(len(lod['positions'])//3)))
            for axis in range(3):
                near=[v for surface in (data['head'],data['lips'],*data['eyes']) for v in surface['positions'][axis::3]]
                far=lod['positions'][axis::3]
                self.assertLess(abs(min(near)-min(far)),.007,name)
                self.assertLess(abs(max(near)-max(far)),.007,name)

    def test_lip_animation_pins_the_face_boundary(self):
        for name in NAMES:
            data=json.loads((ROOT/(name+'.mesh.json')).read_text());surface=data['lips'];edges=Counter()
            for i in range(0,len(surface['indices']),3):
                a,b,c=surface['indices'][i:i+3]
                for u,v in ((a,b),(b,c),(c,a)):edges[tuple(sorted((u,v)))]+=1
            boundary={v for edge,count in edges.items() if count==1 for v in edge}
            self.assertTrue(boundary,name)
            self.assertTrue(all(surface['colors'][i*4+3]==0 for i in boundary),name)
            self.assertGreater(sum(surface['colors'][3::4]),100,name)
            self.assertEqual(data['mouthRegions'],[7] if name=='human-scan' else [7,8])


if __name__ == '__main__':
    unittest.main()
