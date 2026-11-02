"""Check shipped anatomical heads and original UV atlases without requiring Blender or network access."""
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
    def test_body_recipe_is_shared_by_all_anatomical_supports(self):
        raw=(ROOT/'body-profiles.json').read_bytes().replace(b'\r\n',b'\n')
        profile=json.loads(raw);sha=hashlib.sha256(raw).hexdigest()
        self.assertEqual(profile['profile'],'lean-limb-physique-v21')
        self.assertEqual(profile['license'],'CC0-1.0')
        manifest=json.loads((ROOT/'head-sources.json').read_text(encoding='utf-8'))['files']
        for sex,label in (('male','masculine'),('female','feminine')):
            shapes=[]
            for file in (f'trunk-{label}-cage.json',f'upper-arm-{sex}.json',f'shoulder-surface-{sex}.json',f'trousers-{sex}.mesh.json',f'forearm-fit-{sex}.json'):
                asset=json.loads((ROOT/file).read_text(encoding='utf-8'))
                recipe=asset['shapeRecipe'];shapes.append(recipe)
                self.assertEqual(recipe['profileSha256'],sha,file)
                self.assertEqual(recipe['targets'],profile['frames'][sex]['targets'],file)
                for name in recipe['targets']:
                    self.assertEqual(asset['sourceSha256'][name],manifest[name]['sha256'],file)
            self.assertTrue(all(shape==shapes[0] for shape in shapes))
        m=json.loads((ROOT/'trunk-masculine-cage.json').read_text(encoding='utf-8'))
        f=json.loads((ROOT/'trunk-feminine-cage.json').read_text(encoding='utf-8'))
        for sex,data in (('male',m),('female',f)):
            self.assertEqual(data['scale'],[data['scale'][0]]*3)
            self.assertEqual(data['shapeRecipe']['coordinateFrame'],'uniform-baseline-shoulder-hip-v1')
            arm=json.loads((ROOT/f'upper-arm-{sex}.json').read_text(encoding='utf-8'))
            shoulder=json.loads((ROOT/f'shoulder-surface-{sex}.json').read_text(encoding='utf-8'))
            expected=data['sourceShoulder'][0]*data['scale'][0]
            self.assertAlmostEqual(arm['sourceShoulder'][0],expected,places=6)
            self.assertAlmostEqual(shoulder['shoulder'][0],expected,places=6)
        # The upper trunk must exclude each source's A-pose arm. A common
        # male-width clip previously made the female upper section as broad.
        self.assertLess(f['armholeHalfWidth'],m['armholeHalfWidth']-.025)
        row=round((1.18-.74)/.005)*128
        self.assertLess(f['radii'][row]+f['radii'][row+64],m['radii'][row]+m['radii'][row+64]-.04)

    def test_complete_trunk_sources_and_shape_sections(self):
        expected=('250d268b435d96439c7e2b98c5b44bcb97c4e9ebd99dc722ec92369e1987f714',
                  'bc6cc6ed312c17227c42a8bed7655ff0fe2d908b3112f4e0ff7d7f262f39237d')
        sources=json.loads((ROOT/'head-sources.json').read_text())['files']
        frames=[]
        for frame,digest in zip(('masculine','feminine'),expected):
            raw=(ROOT/f'trunk-{frame}-cage.json').read_bytes().replace(b'\r\n',b'\n')
            self.assertEqual(hashlib.sha256(raw).hexdigest(),digest)
            data=json.loads(raw);frames.append(data)
            self.assertEqual((data['low'],data['high'],data['rows'],data['columns']),(.74,1.30,113,128))
            self.assertEqual(data['region'],'trunk-plane-sections')
            self.assertEqual(len(data['radii']),113*128)
            self.assertTrue(all(math.isfinite(x) and .025<x<.34 for x in data['radii']))
            for name,sha in data['sourceSha256'].items():self.assertEqual(sha,sources[name]['sha256'])
        def width(data,y):
            row=round((y-.74)/.005)
            x=[r*math.cos(i*math.tau/128) for i,r in enumerate(data['radii'][row*128:(row+1)*128])]
            return max(x)-min(x)
        male,female=frames
        self.assertLess(width(female,1.04)/width(female,.82),width(male,1.04)/width(male,.82)-.15)
        self.assertGreater(width(female,.82),width(male,.82))
        # A fixed centimetre gap encourages widening a lean pelvis. Preserve
        # the authored shoulder/pelvis relationship instead of adding volume.
        shoulder=lambda d:2*d['sourceShoulder'][0]*d['scale'][0]
        self.assertGreater(width(female,.82)/shoulder(female),width(male,.82)/shoulder(male)+.20)

    def test_feminine_torso_support_and_provenance(self):
        raw=(ROOT/'torso-feminine-cage.json').read_bytes().replace(b'\r\n',b'\n')
        self.assertEqual(hashlib.sha256(raw).hexdigest(),'043ce8aff6766d2691d3c58c2c2baa2d110fb041cf91d41f4001986dc3617181')
        data=json.loads(raw);base=json.loads((ROOT/'torso-cage.json').read_text())
        self.assertEqual((data['rows'],data['columns']),(65,128))
        self.assertEqual(len(data['radii']),65*128)
        self.assertTrue(all(math.isfinite(v) and .025<=v<=.34 for v in data['radii']))
        self.assertGreater(max(abs(a-b) for a,b in zip(data['radii'],base['radii'])),.01)
        sources=json.loads((ROOT/'head-sources.json').read_text())['files']
        for name,digest in data['sourceSha256'].items():
            self.assertEqual(digest,sources[name]['sha256'])

    def test_torso_cage_transport_and_source_contract(self):
        raw=(ROOT/'torso-cage.json').read_bytes().replace(b'\r\n',b'\n')
        self.assertEqual(hashlib.sha256(raw).hexdigest(),
                         'ecd8b6f42e68931caae38d48a76758f5b071b8c556ccaed0d5b58c92aa67d1db')
        data=json.loads(raw)
        self.assertEqual((data['schemaVersion'],data['rows'],data['columns']),(1,65,128))
        self.assertEqual(len(data['radii']),65*128)
        self.assertTrue(all(math.isfinite(v) and .025<=v<=.34 for v in data['radii']))
        sources=json.loads((ROOT/'head-sources.json').read_text())['files']
        for name,digest in data['sourceSha256'].items():
            self.assertEqual(digest,sources[name]['sha256'])

    def test_atlases_match_mesh_and_import_contract(self):
        for name in NAMES:
            data=json.loads((ROOT/(name+'.mesh.json')).read_text())
            self.assertEqual(data['schemaVersion'],2)
            self.assertFalse((ROOT/(name+'_normal.png')).exists())
            for field,size in (('albedo',2048),('hairAlbedo',2048),('eyeAlbedo',1024)):
                entry=data[field];raw=(ROOT/entry['file']).read_bytes()
                self.assertEqual(hashlib.sha256(raw).hexdigest(),entry['sha256'])
                validate_png(raw)
                self.assertEqual(struct.unpack('>II',raw[16:24]),(size,size))
                settings=(ROOT/(entry['file']+'.import')).read_text(encoding='utf-8-sig')
                self.assertIn('mipmaps/generate=true',settings)

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
        self.assertIn('MakeHuman', license_text)
        source_manifest=json.loads((ROOT/'head-sources.json').read_text())
        for source in sources:
            for name,digest in source['files'].items():
                self.assertEqual(digest,source_manifest['files'][name]['sha256'])
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
            self.assertEqual(data['mouthRegions'],['source-uv-mouth'])


    def test_source_winding_faces_outward_in_godot(self):
        for name in NAMES:
            data=json.loads((ROOT/(name+'.mesh.json')).read_text())
            for part in ('head','lips','hair'):
                surface=data[part];p=surface['positions'];n=surface['normals'];indices=surface['indices']
                wrong=total=0
                for j in range(0,len(indices),3):
                    a,b,c=[p[i*3:i*3+3] for i in indices[j:j+3]]
                    u=[b[i]-a[i] for i in range(3)];v=[c[i]-a[i] for i in range(3)]
                    cross=[u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0]]
                    if sum(x*x for x in cross)<1e-20:continue
                    normal=[sum(n[index*3+i] for index in indices[j:j+3]) for i in range(3)]
                    wrong+=sum(x*y for x,y in zip(cross,normal))>0
                    total+=1
                self.assertLess(wrong/max(1,total),.02,(name,part))

    def test_blink_moves_the_original_lids_without_moving_the_neck(self):
        for name in NAMES:
            data=json.loads((ROOT/(name+'.mesh.json')).read_text());head=data['head']
            self.assertEqual(len(head['blink']),len(head['positions']))
            displacements=head['blink'];moved=0
            for i in range(len(displacements)//3):
                delta=displacements[i*3:i*3+3];point=head['positions'][i*3:i*3+3]
                self.assertTrue(all(math.isfinite(x) for x in delta))
                self.assertLess(sum(x*x for x in delta),.02**2)
                if sum(x*x for x in delta)>1e-12:
                    moved+=1
                    self.assertGreater(point[1],-.04)
                    self.assertLess(point[1],.07)
            self.assertGreater(moved,100)
            self.assertGreater(-min(displacements[1::3]),.006)

if __name__ == '__main__':
    unittest.main()
