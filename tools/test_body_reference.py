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
    def test_verified_surface_and_shape_identity(self):
        manifest=json.loads((ASSETS/'reference-shape-sources.json').read_text(encoding='utf-8'))
        profile=json.loads((ASSETS/'body-profiles.json').read_text(encoding='utf-8'))
        self.assertEqual(manifest['license'],'CC0-1.0')
        self.assertEqual(manifest['source']['sha256'],'3c121505651140ceb4d69fd1d8923f7788ffadd81672f5be14845a5f2c75c137')
        for sex,geometry in [('male','4b09cfc4e2989072c7d4c525998bfeca248eafad4c51a9f4f5a445bc3207bcaa'),('female','b6290d86a6c4e6a704bf04342958270bc37507b57b5cf50fe527462b75e05566')]:
            name=f'reference-shape-{sex}.json';raw=(ASSETS/name).read_bytes();d=json.loads(raw)
            self.assertEqual(profile['frames'][sex]['targets'],{name:1.0})
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
            def mean(name):
                ids=[i for a,b in groups[name] for i in range(a,b+1)]
                return [sum(points[i][j] for i in ids)/len(ids) for j in range(3)]
            shoulder=mean('joint-l-shoulder')
            for name,expected in d['targetJoints'].items():
                p=mean(name);actual=[p[0]*scale,1.23+(p[1]-shoulder[1])*scale,-(p[2]-shoulder[2])*scale]
                for a,b in zip(actual,expected):self.assertAlmostEqual(a,b,places=6,msg=(sex,name))

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
