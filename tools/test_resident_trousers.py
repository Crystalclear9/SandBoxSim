"""Validate source clothing provenance, topology and five-bone skinning."""
from pathlib import Path
import hashlib
import json
import math
import unittest
from resident_body import source_manifest
from collections import Counter

ROOT=Path(__file__).resolve().parents[1]/'src/SandBoxSim.Godot/assets/models/human'
class TrouserTests(unittest.TestCase):
    def test_clothed_thigh_clearance_against_source_skin(self):
        for sex in ('male','female'):
            d=json.loads((ROOT/f'trousers-{sex}.mesh.json').read_text())
            self.assertEqual(d['tailoring']['legSupport'],'source-body-rays-v1')
            self.assertGreater(d['tailoring']['anatomicalSamples'],200)
            skin=d['anatomicalSupport'];self.assertEqual(len(skin['radii']),skin['rows']*skin['columns'])
            p=list(zip(*[iter(d['positions'])]*3))
            for height in (.55,.61,.67):
                segments=[]
                for tri in zip(*[iter(d['indices'])]*3):
                    hits=[]
                    for i,j in zip(tri,tri[1:]+tri[:1]):
                        a,b=p[i],p[j]
                        if (a[1]-height)*(b[1]-height)>0 or abs(b[1]-a[1])<1e-9:continue
                        u=(height-a[1])/(b[1]-a[1])
                        hits.append((a[0]+u*(b[0]-a[0])-d['hipHalfWidth'],a[2]+u*(b[2]-a[2])))
                    if len(hits)==2:segments.append(hits)
                for col in (0,16,48):
                    angle=col*math.tau/64;ray=(math.cos(angle),math.sin(angle));hits=[]
                    for a,b in segments:
                        edge=(b[0]-a[0],b[1]-a[1]);cross=ray[0]*edge[1]-ray[1]*edge[0]
                        if abs(cross)<1e-10:continue
                        r=(a[0]*edge[1]-a[1]*edge[0])/cross;t=(a[0]*ray[1]-a[1]*ray[0])/cross
                        if r>0 and 0<=t<=1:hits.append(r)
                    expected=skin['radii'][round((height-skin['low'])/skin['step'])*64+col]
                    gap=min(hits)-expected
                    self.assertGreater(gap,.0005,(sex,height,col,'cloth cuts into source skin'))
                    self.assertLess(gap,.012,(sex,height,col,'cloth duplicates anatomical volume'))

    def test_pelvis_gluteal_and_thigh_sections(self):
        data={sex:json.loads((ROOT/f'trousers-{sex}.mesh.json').read_text()) for sex in ('male','female')}
        def section(sex,y):
            points=list(zip(*(iter(data[sex]['positions']),)*3))
            rows=[p for p in points if abs(p[1]-y)<.008]
            return max(p[0] for p in rows)-min(p[0] for p in rows),max(p[2] for p in rows)
        for sex in data:
            self.assertEqual(data[sex]['physique'],'lifted-thorax-physique-v27')
            self.assertEqual(data[sex]['tailoring']['pelvis'],'source-envelope')
            points=list(zip(*(iter(data[sex]['positions']),)*3))
            self.assertTrue(all(abs(x)<.26 and .3849<=y<=.9401 and abs(z)<.20 for x,y,z in points))
            support=json.loads((ROOT/('trunk-'+('feminine' if sex=='female' else 'masculine')+'-cage.json')).read_text())
            row=round((.78-.74)/.005);radii=support['radii'];expected=radii[row*128]+radii[row*128+64]+.010
            self.assertLess(abs(section(sex,.78)[0]-expected),.018)
        male,back_m=section('male',.78);female,back_f=section('female',.78)
        self.assertGreater(female,male)
        self.assertGreater(section('female',.78)[0],section('female',.64)[0])
        self.assertEqual(data['male']['hipHalfWidth'],.085)
        self.assertEqual(data['female']['hipHalfWidth'],.085)

    def test_provenance_and_connected_clothing(self):
        manifest=source_manifest()
        for sex,sha in [('male','6ae961ac0d9439ebe668c7da488857cfa64a9d7a3ada803945554343434ba213'),('female','dd6b106a0f8229ac5c723f6ee167cc5786e000e3b76b47a22b3278ae521668e7')]:
            raw=(ROOT/f'trousers-{sex}.mesh.json').read_bytes().replace(b'\r\n',b'\n');self.assertEqual(hashlib.sha256(raw).hexdigest(),sha)
            data=json.loads(raw);count=len(data['positions'])//3
            for name,digest in data['sourceSha256'].items():self.assertEqual(digest,manifest[name]['sha256'])
            support=ROOT/('trunk-'+('feminine' if sex=='female' else 'masculine')+'-cage.json')
            self.assertEqual(data['supportSha256'],hashlib.sha256(support.read_bytes().replace(b'\r\n',b'\n')).hexdigest())
            self.assertGreater(count,2000);self.assertLessEqual(len(data['indices'])//3,10000)
            self.assertEqual(len(data['normals']),count*3);self.assertEqual(len(data['uv']),count*2)
            self.assertTrue(all(math.isfinite(v) for key in ['positions','normals','weights'] for v in data[key]))
            self.assertEqual(data['schemaVersion'],2);self.assertEqual(data['jointCount'],5)
            self.assertEqual(len(data['weights']),count*4);self.assertEqual(set(data['bones']),{0,1,2,3,4})
            for i in range(count):
                if data['positions'][i*3+1]<.40:
                    expected=3 if data['positions'][i*3]<0 else 4
                    self.assertAlmostEqual(sum(w for b,w in zip(data['bones'][i*4:i*4+4],data['weights'][i*4:i*4+4]) if b==expected),1,places=6)
            for i in range(count):self.assertAlmostEqual(sum(data['weights'][i*4:i*4+4]),1,places=5)
            adjacency=[set() for _ in range(count)];edges=Counter()
            ids=data['indices'];self.assertEqual(set(ids),set(range(count)))
            for i in range(0,len(ids),3):
                tri=ids[i:i+3]
                for a,b in zip(tri,tri[1:]+tri[:1]):
                    adjacency[a].add(b);adjacency[b].add(a);edges[tuple(sorted((a,b)))]+=1
            reached={0};stack=[0]
            while stack:
                for v in adjacency[stack.pop()]-reached:reached.add(v);stack.append(v)
            self.assertEqual(len(reached),count);self.assertTrue(all(n<=2 for n in edges.values()))
            # Only waist and the two cuffs may remain open.
            boundary={i for edge,n in edges.items() if n==1 for i in edge};rings=0
            while boundary:
                stack=[boundary.pop()];rings+=1
                while stack:
                    for v in adjacency[stack.pop()]&boundary:boundary.remove(v);stack.append(v)
            self.assertEqual(rings,3)

    def test_rendered_faces_have_area_and_consistent_normals(self):
        # A connected manifold can still contain folded sub-millimetre crotch
        # triangles. Check the shipped faces, not only export parameters.
        for sex in ('male','female'):
            data=json.loads((ROOT/f'trousers-{sex}.mesh.json').read_text())
            points=list(zip(*[iter(data['positions'])]*3))
            normals=list(zip(*[iter(data['normals'])]*3))
            for a,b,c in zip(*[iter(data['indices'])]*3):
                u=[points[b][k]-points[a][k] for k in range(3)]
                v=[points[c][k]-points[a][k] for k in range(3)]
                cross=[u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0]]
                self.assertGreater(sum(x*x for x in cross),1e-14,(sex,a,b,c))
                # Godot's front faces use clockwise winding.
                self.assertLess(sum(cross[k]*(normals[a][k]+normals[b][k]+normals[c][k]) for k in range(3)),0,(sex,a,b,c))

if __name__=='__main__':unittest.main()
