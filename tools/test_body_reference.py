"""Verify source identity and downstream anatomical guide transfer."""
import hashlib
import json
import math
from pathlib import Path
import unittest
from resident_body import body_points,body_metric_scale,limb_weights,source_manifest

ROOT=Path(__file__).resolve().parents[1]
ASSETS=ROOT/'src/SandBoxSim.Godot/assets/models/human'
SOURCE=ROOT/'.tools/sources/makehuman'


class BodyReferenceTests(unittest.TestCase):
    def test_anterior_skin_is_retained_separately_from_cloth_hull(self):
        d=json.loads((ASSETS/'trunk-feminine-cage.json').read_text(encoding='utf-8'))
        self.assertEqual(d['anatomicalSampling'],'source-anterior-skin-rays-v1')
        self.assertGreater(d['skinSampleCount'],500)
        self.assertEqual(len(d['skinRadii']),113*128)
        self.assertTrue(all(math.isfinite(v) and .025<v<.34 for v in d['skinRadii']))
        row=round((1.15-.74)/.005)
        z=lambda c:math.sin(c*math.tau/128)*d['skinRadii'][row*128+c]
        self.assertGreater(z(96)-.5*(z(88)+z(104)),.005)
        self.assertGreater(d['radii'][row*128+96]-d['skinRadii'][row*128+96],.005)

    def test_baked_thorax_has_supported_apex_and_lower_transition(self):
        d=json.loads((ASSETS/'trunk-feminine-cage.json').read_text(encoding='utf-8'))
        samples=[(.74+r*.005,d['radii'][r*128+96]) for r in range(113) if 1.09<=.74+r*.005<=1.20]
        height,radius=max(samples,key=lambda v:v[1])
        self.assertGreaterEqual(height,1.145);self.assertLessEqual(height,1.18)
        lower=d['radii'][round((1.08-.74)/.005)*128+96]
        self.assertGreater(radius-lower,.009)

    def test_soft_contours_preserve_orientation_symmetry_and_source_budget(self):
        from resident_shape import soft_contour
        maximum=0
        for x in (.0,.025,.045,.065,.085,.10,.125,.145,.18):
            for y in (.53,.635,.72,.78,.80,.86,.90,.96,1.015,1.075,1.10,1.135,1.18,1.22,1.25):
                for z in (-.15,-.10,-.09,-.06,-.04,-.03,0,.015,.03,.045,.065,.09):
                    p=(x,y,z);q=soft_contour(p);other=soft_contour((-x,y,z))
                    self.assertAlmostEqual(q[0],-other[0]);self.assertAlmostEqual(q[2],other[2]);self.assertAlmostEqual(q[1],other[1]);self.assertGreaterEqual(q[1],y)
                    maximum=max(maximum,math.dist(p,q));h=1e-5
                    dx=[(a-b)/(2*h) for a,b in zip(soft_contour((x+h,y,z)),soft_contour((x-h,y,z)))]
                    dz=[(a-b)/(2*h) for a,b in zip(soft_contour((x,y,z+h)),soft_contour((x,y,z-h)))]
                    dy=[(a-b)/(2*h) for a,b in zip(soft_contour((x,y+h,z)),soft_contour((x,y-h,z)))]
                    determinant=dx[0]*(dy[1]*dz[2]-dy[2]*dz[1])-dy[0]*(dx[1]*dz[2]-dx[2]*dz[1])+dz[0]*(dx[1]*dy[2]-dx[2]*dy[1])
                    self.assertGreater(determinant,.75);self.assertLess(determinant,1.25)
        self.assertGreater(maximum,.012);self.assertLess(maximum,.018)

    def test_verified_surface_and_shape_identity(self):
        manifest=json.loads((ASSETS/'reference-shape-sources.json').read_text(encoding='utf-8'))
        profile=json.loads((ASSETS/'body-profiles.json').read_text(encoding='utf-8'))
        self.assertEqual(manifest['license'],'CC0-1.0')
        self.assertEqual(manifest['source']['sha256'],'3c121505651140ceb4d69fd1d8923f7788ffadd81672f5be14845a5f2c75c137')
        for sex,geometry in [('male','4b09cfc4e2989072c7d4c525998bfeca248eafad4c51a9f4f5a445bc3207bcaa'),('female','b6290d86a6c4e6a704bf04342958270bc37507b57b5cf50fe527462b75e05566')]:
            name=f'reference-shape-{sex}.json';raw=(ASSETS/name).read_bytes();d=json.loads(raw)
            self.assertEqual(profile['frames'][sex]['targets'],{name:.75 if sex=='female' else 1.0})
            self.assertEqual((len(raw),hashlib.sha256(raw).hexdigest()),(manifest['files'][name]['bytes'],manifest['files'][name]['sha256']))
            self.assertEqual(d['sourceGeometrySha256'],geometry)
            self.assertEqual(d['sourceBundleSha256'],manifest['source']['sha256'])
            self.assertEqual(d['retargetPose'],'neutral-shoulder-pelvis-axis-v1')
            self.assertEqual(len(d['deltas']),19158*3)
            self.assertTrue(all(math.isfinite(v) for v in d['deltas']))
            self.assertEqual(d['bodyVertices'],13380)
            self.assertEqual((d['controls'],len(d['surfaceAnchors'])),(41,24))
            self.assertLess(d['cageCondition'],1e6)
            for name,sha in d['topologyInputSha256'].items():self.assertEqual(sha,source_manifest()[name]['sha256'])
            self.assertAlmostEqual(d['targetJoints']['joint-l-shoulder'][1]-d['targetJoints']['joint-l-upper-leg'][1],.43,places=6)
            self.assertAlmostEqual(d['targetJoints']['joint-l-upper-leg'][2],0,places=6)

    @unittest.skipUnless((SOURCE/'base.obj').is_file(),'Optional HM08 source cache')
    def test_actual_body_joint_guides_match_reference(self):
        groups=json.loads((SOURCE/'basemesh_vertex_groups.json').read_text(encoding='utf-8'))
        scale=body_metric_scale(SOURCE)
        for sex in ('male','female'):
            d=json.loads((ASSETS/f'reference-shape-{sex}.json').read_text(encoding='utf-8'));points=body_points(SOURCE,sex)
            baseline=body_points(SOURCE,sex,baseline=True)
            weight=json.loads((ASSETS/'body-profiles.json').read_text())['frames'][sex]['targets'][f'reference-shape-{sex}.json']
            def mean(name,vertices):
                ids=[i for a,b in groups[name] for i in range(a,b+1)]
                return [sum(vertices[i][j] for i in ids)/len(ids) for j in range(3)]
            shoulder=mean('joint-l-shoulder',points);original_shoulder=mean('joint-l-shoulder',baseline)
            for name,expected in d['targetJoints'].items():
                p=mean(name,points);actual=[p[0]*scale,1.23+(p[1]-shoulder[1])*scale,-(p[2]-shoulder[2])*scale]
                q=mean(name,baseline);original=[q[0]*scale,1.23+(q[1]-original_shoulder[1])*scale,-(q[2]-original_shoulder[2])*scale]
                for a,b,c in zip(actual,expected,original):self.assertAlmostEqual(a,b*weight+c*(1-weight),places=6,msg=(sex,name))

    @unittest.skipUnless((SOURCE/'rig/default_weights.mhw').is_file(),'Optional source skeleton cache')
    def test_arm_region_includes_palm_and_excludes_leg(self):
        weights=json.loads((SOURCE/'rig/default_weights.mhw').read_text(encoding='utf-8'))['weights']
        arm=limb_weights(SOURCE,('upperarm01.L','upperarm01.R'))
        for side in ('L','R'):
            vertex,weight=max(weights[f'metacarpal1.{side}'],key=lambda pair:pair[1])
            self.assertGreaterEqual(arm.get(vertex,0)+1e-6,weight)
            vertex,weight=max(weights[f'lowerleg01.{side}'],key=lambda pair:pair[1])
            self.assertLess(arm.get(vertex,0),.1)


if __name__=='__main__':unittest.main()
