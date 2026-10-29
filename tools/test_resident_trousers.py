"""Validate source clothing provenance, topology and five-bone skinning."""
from pathlib import Path
import hashlib
import json
import math
import unittest
from collections import Counter

ROOT=Path(__file__).resolve().parents[1]/'src/SandBoxSim.Godot/assets/models/human'
class TrouserTests(unittest.TestCase):
    def test_pelvis_gluteal_and_thigh_sections(self):
        data={sex:json.loads((ROOT/f'trousers-{sex}.mesh.json').read_text()) for sex in ('male','female')}
        def section(sex,y):
            points=list(zip(*(iter(data[sex]['positions']),)*3))
            rows=[p for p in points if abs(p[1]-y)<.008]
            return max(p[0] for p in rows)-min(p[0] for p in rows),max(p[2] for p in rows)
        for sex in data:self.assertEqual(data[sex]['physique'],'adult-proportions-v13')
        male,back_m=section('male',.78);female,back_f=section('female',.78)
        self.assertGreater(female/male,1.20)
        self.assertGreater(back_f-back_m,.012)
        self.assertGreater(section('female',.78)[0],section('female',.64)[0])
        self.assertEqual(data['male']['hipHalfWidth'],.10)
        self.assertEqual(data['female']['hipHalfWidth'],.122)

    def test_provenance_and_connected_clothing(self):
        manifest=json.loads((ROOT/'head-sources.json').read_text())['files']
        for sex,sha in [('male','ca7bae401aad82e94016ea190e472e17fc7934a85a3a727dd455e4d0a9c27658'),('female','3159ec553da44d982f32f19bda62eed5a881e86bb2c8fafa614f28431d49cc6a')]:
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

if __name__=='__main__':unittest.main()
