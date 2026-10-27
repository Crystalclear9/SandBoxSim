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
                d=json.loads(raw);self.assertEqual(d['construction'],'draped-sleeve-v3')
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

    def test_draped_sleeves_keep_cuffs_and_metric_details(self):
        for cut in range(3):
            with self.subTest(cut=cut):
                d=json.loads((ROOT/f'garment-{cut}.skin.json').read_text())
                points=list(zip(*[iter(d['positions'])]*3))
                detail=list(zip(*[iter(d['detailUv'])]*2))
                self.assertEqual(len(detail),len(points))
                self.assertTrue(any(a<0 for a,b in detail))
                rings=collections.defaultdict(list)
                for i,((x,y,z),(along,across)) in enumerate(zip(points,detail)):
                    self.assertTrue(math.isfinite(along) and math.isfinite(across))
                    self.assertGreaterEqual(across,0)
                    self.assertLessEqual(across,.2)
                    if along>0:rings[along].append((i,(x,y,z)))
                    if abs(along-.263)<1e-6:
                        self.assertAlmostEqual(y,1.23-.263,places=6)
                        # Original cuff ellipse, position and binding remain
                        # unchanged: the rolled cuff and forearm still meet.
                        radius=((abs(x)-.202)/.043)**2+(z/.042)**2
                        self.assertAlmostEqual(radius,1.,places=5)
                        bone=1 if x<0 else 2
                        self.assertEqual(d['weights'][i*4+bone],1.)
                self.assertEqual(len(rings),18)
                ordered=[rings[k] for k in sorted(rings)]
                self.assertEqual(len({len(r) for r in ordered}),1)
                # Do not require every underarm strand to descend: a sewn
                # armhole is a saddle. Limit any rise to a small cloth turn.
                for a,b in zip(ordered,ordered[1:]):
                    for (_,pa),(_,pb) in zip(a,b):self.assertLess(pb[1]-pa[1],.006)

    def test_shipped_shoulder_seam_does_not_form_a_ledge(self):
        for cut in range(3):
            with self.subTest(cut=cut):
                d=json.loads((ROOT/f'garment-{cut}.skin.json').read_text())
                p=list(zip(*[iter(d['positions'])]*3));uv=list(zip(*[iter(d['detailUv'])]*2))
                edge_faces=collections.defaultdict(list)
                def normal(tri):
                    a,b,c=[p[i] for i in tri]
                    u=[b[j]-a[j] for j in range(3)];v=[c[j]-a[j] for j in range(3)]
                    n=[u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0]]
                    length=math.sqrt(sum(q*q for q in n));return [q/length for q in n]
                for tri in zip(*[iter(d['indices'])]*3):
                    n=normal(tri)
                    for a,b in ((tri[0],tri[1]),(tri[1],tri[2]),(tri[2],tri[0])):
                        edge_faces[tuple(sorted((a,b)))].append(n)
                angles=[]
                for (a,b),ns in edge_faces.items():
                    if len(ns)!=2 or uv[a][0]!=0 or uv[b][0]!=0 or min(p[a][1],p[b][1])<1.25:continue
                    # Shipped Blender winding has already been recalculated;
                    # compare actual adjacent rendered triangles, not a
                    # duplicate idealized curve or averaged vertex normals.
                    dot=max(-1,min(1,sum(x*y for x,y in zip(*ns))))
                    angles.append(math.degrees(math.acos(dot)))
                self.assertGreaterEqual(len(angles),20)
                self.assertLess(max(angles),25.)
                self.assertLess(sum(angles)/len(angles),12.)

    def test_reject_old_or_incomplete_pattern(self):
        with self.assertRaises(ValueError):build_pattern({'schemaVersion':1})
        with self.assertRaises(ValueError):build_pattern({'schemaVersion':2,'rings':40,'sides':64,'positions':[]})

if __name__=='__main__':unittest.main()
