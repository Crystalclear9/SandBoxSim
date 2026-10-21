"""Derive a small, anatomical hand from the checked-in high-detail hand.

Blender --background --python tools/export_hand_lod.py -- --source FILE --output FILE
No external downloads. Transfers UVs and joint weights from the nearest source triangle.
"""
import argparse
import hashlib
import json
import pathlib
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

parser = argparse.ArgumentParser()
parser.add_argument('--source', required=True)
parser.add_argument('--output', required=True)
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
raw = pathlib.Path(args.source).read_bytes()
if hashlib.sha256(raw).hexdigest() != 'e4ef3512eb34385e380517f79c8b76f2af3d62beea868949fc1db9cc2da821a6':
    raise ValueError('Anatomical source mismatch')
source = json.loads(raw)
points = [Vector(source['positions'][i:i+3]) for i in range(0, len(source['positions']), 3)]
# Godot uses clockwise winding; Blender uses counterclockwise winding.
faces = [tuple(reversed(source['indices'][i:i+3])) for i in range(0, len(source['indices']), 3)]
tree = BVHTree.FromPolygons(points, faces, all_triangles=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
mesh = bpy.data.meshes.new('AnatomicalHandLOD')
mesh.from_pydata(points, [], faces)
obj = bpy.data.objects.new('AnatomicalHandLOD', mesh)
bpy.context.collection.objects.link(obj)
bpy.context.view_layer.objects.active = obj
obj.select_set(True)
modifier = obj.modifiers.new('Preserve anatomical silhouette', 'DECIMATE')
modifier.ratio = 1400 / len(faces)
bpy.ops.object.modifier_apply(modifier=modifier.name)
mesh = obj.data
mesh.calc_loop_triangles()
out = dict(positions=[], normals=[], uv=[], bones=[], weights=[], indices=[], nailIndices=[])
for vertex in mesh.vertices:
    nearest, _, face, _ = tree.find_nearest(vertex.co)
    ids = faces[face]
    a, b, c = [points[i] for i in ids]
    u, v, w = b-a, c-a, nearest-a
    denominator = u.dot(u)*v.dot(v)-u.dot(v)**2
    t = (v.dot(v)*w.dot(u)-u.dot(v)*w.dot(v))/denominator
    s = (u.dot(u)*w.dot(v)-u.dot(v)*w.dot(u))/denominator
    bary = [max(0, 1-t-s), max(0, t), max(0, s)]
    total = sum(bary)
    bary = [value/total for value in bary]
    weights = {}
    normal = Vector()
    uv = Vector((0, 0))
    for i, factor in zip(ids, bary):
        normal += Vector(source['normals'][i*3:i*3+3])*factor
        uv += Vector(source['uv'][i*2:i*2+2])*factor
        for joint in range(4):
            bone = source['bones'][i*4+joint]
            weights[bone] = weights.get(bone, 0)+source['weights'][i*4+joint]*factor
    chosen = sorted(weights.items(), key=lambda item: -item[1])[:4]
    total = sum(value for _, value in chosen)
    chosen += [(0, 0)]*(4-len(chosen))
    out['positions'].extend(round(value, 7) for value in vertex.co)
    out['normals'].extend(round(value, 7) for value in normal.normalized())
    out['uv'].extend(round(value, 7) for value in uv)
    out['bones'].extend(bone for bone, _ in chosen)
    out['weights'].extend(round(value/total, 7) for _, value in chosen)
for tri in mesh.loop_triangles:
    out['indices'].extend(reversed(tri.vertices[:]))
out['sourceSha256'] = hashlib.sha256(raw).hexdigest()
out['triangles'] = len(out['indices'])//3
pathlib.Path(args.output).write_text(json.dumps(out, separators=(',', ':')), encoding='utf-8')
print(f"HAND_LOD_BAKED: {len(mesh.vertices)} vertices, {out['triangles']} triangles")
