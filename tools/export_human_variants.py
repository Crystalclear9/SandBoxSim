"""Export two Dan Ulrich / Blender CC0 head families for the resident rig.

Open an explicitly verified source with --disable-autoexec and pass
--kind scan|feminine --output DIRECTORY. Source files stay in .tools/sources.
"""
import argparse
import hashlib
import json
import math
from collections import Counter
from statistics import median
from pathlib import Path
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

parser = argparse.ArgumentParser()
parser.add_argument('--kind', choices=('scan', 'feminine', 'masculine'), required=True)
parser.add_argument('--output', required=True)
args = parser.parse_args(sys.argv[sys.argv.index('--')+1:])
female = args.kind == 'feminine'
masculine = args.kind == 'masculine'
body_kind = female or masculine
name = 'body-realistic-male' if masculine else 'body-realistic-female' if female else 'head-realistic-animation'
expected = ('74917ec876cdb6aae3b6d291f2f638f5a959276f4115a714cf8c5dcf6a99ce20' if female else
            '697b55a5595a756f9304d12909c85f9eddb0c0f57ec2b6c8f1edf071e00f9b30' if masculine else
            'a680ca23a0dee2a16ecc6e3921ddcfa0e039a5a7ef4262ec56214a924d08d3ca')
if hashlib.sha256(Path(bpy.data.filepath).read_bytes()).hexdigest() != expected:
    raise ValueError('Source does not match the official Blender LFS object')
body_name='GEO-body_male_realistic' if masculine else 'GEO-body_female_realistic' if female else 'GEO-head_animation_realistic'
body = bpy.data.objects[body_name]
eye_names = [body_name+'.eye.L', body_name+'.eye.R'] if body_kind else [
    'GEO-head_animation_realistic.sclera.L', 'GEO-head_animation_realistic.sclera.R']
for obj in [body]+[bpy.data.objects[n] for n in eye_names]:
    if obj.name not in bpy.context.scene.objects:
        bpy.context.scene.collection.objects.link(obj)
bpy.context.scene.render.engine = 'CYCLES'
body.modifiers['Multires'].show_viewport = True
scale = .98 if female else .99
center_x = body.matrix_world.translation.x
eye_points = []
for n in eye_names:
    obj = bpy.data.objects[n]
    world = [obj.matrix_world@v.co for v in obj.data.vertices]
    eye_points.append(Vector([(min(p[i] for p in world)+max(p[i] for p in world))/2 for i in range(3)]))
eye_z = sum(p.z for p in eye_points)/2
eye_y = sum(p.y for p in eye_points)/2
center_y = eye_y+.074/scale


def point(world):
    p = Vector((-(world.x-center_x)*scale, (world.z-eye_z)*scale+.010, (world.y-center_y)*scale))
    # Keep only the neck and head. The female body's torso never enters the
    # runtime asset; its neck root is fitted below the existing collar.
    t = min(1., max(0., (-p.y-.085)/.065))
    t = t*t*(3-2*t)
    p.x *= 1-.20*t
    p.z = p.z*(1-.10*t)-.004*t
    return p


def encode(mesh, transform, selected, lip=False):
    result = {k: [] for k in ('positions', 'normals', 'colors', 'uv', 'indices')}
    lookup = {}
    boundary=set()
    if lip:
        edges=Counter()
        for tri,_ in selected:
            a,b,c=tri.vertices
            for u,v in ((a,b),(b,c),(c,a)):edges[tuple(sorted((u,v)))]+=1
        boundary={v for edge,count in edges.items() if count==1 for v in edge}
    for tri, region in selected:
        face = []
        for vertex_id in tri.vertices:
            if vertex_id not in lookup:
                lookup[vertex_id] = len(result['positions'])//3
                v = mesh.vertices[vertex_id]
                p = point(transform@v.co)
                n = transform.to_3x3().inverted().transposed()@v.normal
                n = Vector((-n.x, n.z, n.y)).normalized()
                result['positions'].extend(round(x, 7) for x in p)
                result['normals'].extend(round(x, 7) for x in n)
                result['uv'].extend((round(.5+p.x/.177, 7), round(.5-p.y/.220, 7)))
                result['colors'].extend((1, .85, .82, 0 if vertex_id in boundary else 1) if lip else (1, .99, .98, 1))
            face.append(lookup[vertex_id])
        result['indices'].extend((face[0], face[2], face[1]))
    return result


def extract(level):
    body.modifiers['Multires'].levels = level
    bpy.context.view_layer.update()
    deps = bpy.context.evaluated_depsgraph_get()
    mesh = body.evaluated_get(deps).to_mesh(preserve_all_data_layers=True, depsgraph=deps)
    mesh.calc_loop_triangles()
    regions = mesh.attributes['.sculpt_face_set']
    head, lips, scalp = [], [], []
    for tri in mesh.loop_triangles:
        region = regions.data[tri.polygon_index].value
        # Region 1 includes the anterior neck, not just the trunk. Omitting it
        # leaves an open strip below the jaw even when the back neck survives.
        if body_kind and region not in {1, 2, 3, 4, 5, 7, 8, 17, 22}:
            continue
        positions = [point(body.matrix_world@mesh.vertices[i].co) for i in tri.vertices]
        if min(p.y for p in positions) < -.149:
            continue
        if region == 7 or body_kind and region == 8:
            lips.append((tri, region))
        else:
            head.append((tri, region))
            if region not in ({4, 5} if body_kind else {3, 4}):
                scalp.append((tri, region))
    return (encode(mesh, body.matrix_world, head), encode(mesh, body.matrix_world, lips, True),
            encode(mesh, body.matrix_world, scalp))


head, lips, scalp = extract(1)
high_level = 2 if body_kind else 1
high, _, _ = extract(high_level)
eyes = []
for n in sorted(eye_names, key=lambda n: point(eye_points[eye_names.index(n)]).x):
    obj = bpy.data.objects[n]
    mesh = obj.data
    mesh.calc_loop_triangles()
    eyes.append(encode(mesh, obj.matrix_world, [(t, 0) for t in mesh.loop_triangles]))

p = scalp['positions']
vertices = [Vector(p[i:i+3]) for i in range(0, len(p), 3)]
indices = scalp['indices']
bvh = BVHTree.FromPolygons(vertices, [indices[i:i+3] for i in range(0, len(indices), 3)], all_triangles=True)
rows, columns = 64, 128
low, high_y = -.038, max(p.y for p in vertices)
centers, radii = [], []
for row in range(rows):
    y = low+(high_y-low)*(row+.5)/rows
    band = [p for p in vertices if abs(p.y-y) < (high_y-low)/rows]
    if not band:
        band = sorted(vertices, key=lambda p: abs(p.y-y))[:12]
    center = (min(p.z for p in band)+max(p.z for p in band))/2
    centers.append(round(center, 7))
    origin = Vector((0, y, center))
    for col in range(columns):
        angle = col*math.tau/columns
        direction = Vector((math.cos(angle), 0, math.sin(angle)))
        hit, normal, face, distance = bvh.ray_cast(origin, direction, .4)
        if hit is None:
            best = min(band, key=lambda p: abs(math.atan2(math.sin(math.atan2(p.z-center, p.x)-angle),
                                                        math.cos(math.atan2(p.z-center, p.x)-angle))))
            distance = max(.001, (best-origin).dot(direction))
        radii.append(round(max(.001, distance), 7))

out = Path(args.output)
out.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(Path(__file__).parent))
from bake_human_detail import bake_head_detail
from human_eyelids import eye_apertures
from human_head_lod import build_head_lod
apertures=eye_apertures(head,eyes)
head_lod=build_head_lod(head,lips,eyes)
file_name='human-head' if masculine else 'human-'+args.kind
normal_path = out/(file_name+'_normal.png')
head, normal_hash = bake_head_detail(head, high, normal_path)
mouth_points = [Vector(lips['positions'][i:i+3]) for i in range(0, len(lips['positions']), 3)]
mouth = sum(mouth_points, Vector())/len(mouth_points)
mouth.x = 0
front=min(p.z for p in mouth_points)
visible_lip=[p for p in mouth_points if p.z<front+.005 and abs(p.x)<.022]
mouth.y=median(p.y for p in visible_lip);mouth.z=median(p.z for p in visible_lip)
eye_center = sum((point(p) for p in eye_points), Vector())/2
eye_center.x = sum(abs(point(p).x) for p in eye_points)/2
points = head['positions']
nose = min((Vector(points[i:i+3]) for i in range(0, len(points), 3)), key=lambda p: p.z)
result = {'schemaVersion': 1, 'source': {'creator': 'Dan Ulrich / Blender', 'license': 'CC0-1.0',
    'url': 'https://projects.blender.org/blender/blender-assets/src/branch/main/publish/online/base_meshes/human_base_meshes__'+name+'.blend',
    'sha256': expected}, 'head': head, 'lips': lips, 'eyes': eyes, 'eyeCenter': list(eye_center),
    'mouthCenter': list(mouth),'mouthRegions':[7,8] if body_kind else [7], 'noseProbeY': nose.y,'eyelids':apertures,'headLod':head_lod,
    'scalp': {'rows': rows, 'columns': columns, 'low': low, 'high': high_y, 'centers': centers, 'radii': radii},
    'detailNormal': {'file': normal_path.name, 'sha256': normal_hash, 'resolution': 2048,
                     'sourceSubdivisionLevel': high_level, 'runtimeSubdivisionLevel': 1}}
path = out/(file_name+'.mesh.json')
path.write_text(json.dumps(result, separators=(',', ':')), encoding='utf-8')
print('HEAD_FAMILY_EXPORTED', args.kind, len(head['positions'])//3, len(head['indices'])//3,
      hashlib.sha256(path.read_bytes()).hexdigest())
