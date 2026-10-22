"""Regression checks on shipped shirt topology and its independent pattern builder."""
import collections
import hashlib
import json
import math
from pathlib import Path
import unittest
from garment_pattern import build_pattern

ROOT=Path(__file__).resolve().parents[1]/'src/SandBoxSim.Godot/assets/models/garments'

class GarmentPatternTests(unittest.TestCase):
    def test_shipped_openings_and_skin(self):
        manifest=json.loads((ROOT/'manifest.json').read_text(encoding='utf-8'))
        for name,digest in manifest['sha256'].items():
            with self.subTest(cut=name):
                raw=(ROOT/name).read_bytes();self.assertEqual(hashlib.sha256(raw).hexdigest(),digest)
                d=json.loads(raw);self.assertEqual(d['construction'],'sewn-panel-v2')
                points=list(zip(*[iter(d['positions'])]*3));ids=d['indices']
                self.assertLessEqual(len(ids)//3,10000)
                self.assertEqual(set(ids),set(range(len(points))))
                edges=collections.Counter()
                for a,b,c in zip(*[iter(ids)]*3):
                    self.assertEqual(len({a,b,c}),3)
                    ab=[points[b][i]-points[a][i] for i in range(3)]
                    ac=[points[c][i]-points[a][i] for i in range(3)]
                    area=sum((ab[(i+1)%3]*ac[(i+2)%3]-ab[(i+2)%3]*ac[(i+1)%3])**2 for i in range(3))
                    self.assertGreater(area,1e-18)
                    edges.update(tuple(sorted(e)) for e in ((a,b),(b,c),(c,a)))
                self.assertLessEqual(max(edges.values()),2)
                boundary=collections.defaultdict(set)
                for (a,b),n in edges.items():
                    if n==1:boundary[a].add(b);boundary[b].add(a)
                self.assertEqual({len(v) for v in boundary.values()},{2})
                loops=[];remaining=set(boundary)
                while remaining:
                    todo=[remaining.pop()];loop=[]
                    while todo:
                        a=todo.pop();loop.append(a)
                        for b in boundary[a]:
                            if b in remaining:remaining.remove(b);todo.append(b)
                    loops.append(loop)
                # The neck and skirt are root-bound; the two open cuffs follow
                # distinct left/right bones, not a single mirrored binding.
                self.assertEqual(len(loops),4)
                cuff_bones=[]
                for loop in loops:
                    if abs(sum(points[i][0] for i in loop)/len(loop))>.15:
                        bs={d['bones'][i*4+j] for i in loop for j in range(4) if d['weights'][i*4+j]>.99}
                        self.assertEqual(len(bs),1);cuff_bones.extend(bs)
                self.assertEqual(sorted(cuff_bones),[1,2])
                for i in range(len(points)):
                    w=d['weights'][i*4:i*4+4]
                    self.assertTrue(all(math.isfinite(x) and 0<=x<=1 for x in w))
                    self.assertAlmostEqual(sum(w),1,places=5)

    def test_reject_old_or_incomplete_pattern(self):
        with self.assertRaises(ValueError):build_pattern({'schemaVersion':1})
        with self.assertRaises(ValueError):build_pattern({'schemaVersion':2,'rings':40,'sides':64,'positions':[]})

if __name__=='__main__':unittest.main()
