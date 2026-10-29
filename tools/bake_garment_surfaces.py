"""Bake the sampled sewn garment pattern with project-local Blender.

No voxel remesh: shared armhole vertices preserve the designed cloth silhouette.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys
import bmesh
import bpy
sys.path.insert(0,str(Path(__file__).resolve().parent))
from garment_pattern import build_pattern

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--source',required=True)
parser.add_argument('--output',required=True)
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
source=Path(args.source);output=Path(args.output);output.mkdir(parents=True,exist_ok=True)
hashes={}
for cut in range(3):
    raw=(source/f'garment-{cut}-source.json').read_bytes();data=json.loads(raw)
    points,faces,weights,details=build_pattern(data,include_details=True)
    # Mirror the quad diagonals with the body. Unconstrained triangulation can
    # pick different diagonals at the two non-planar scapular sleeve corners.
    faces=[tri for f in faces for tri in (((f[0],f[1],f[2]),(f[0],f[2],f[3])) if sum(points[i][0] for i in f)<0 else ((f[0],f[1],f[3]),(f[1],f[2],f[3])))]
    mesh=bpy.data.meshes.new('sewn-shirt');mesh.from_pydata(points,[],faces);mesh.update()
    # Recalculate winding across both mirrored sleeves from the shared topology.
    bm=bmesh.new();bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
    mesh.calc_loop_triangles()
    if len(mesh.loop_triangles)>10000:raise RuntimeError('Garment triangle budget exceeded')
    result={'schemaVersion':1,'construction':'faired-panel-v8','sourceSha256':hashlib.sha256(raw).hexdigest(),'positions':[],'normals':[],'uv':[],'detailUv':[],'bones':[],'weights':[],'indices':[]}
    for v,w,detail in zip(mesh.vertices,weights,details):
        result['positions'].extend(round(x,7) for x in v.co)
        result['normals'].extend(round(x,7) for x in v.normal.normalized())
        result['uv'].extend((round(v.co.x+v.co.z,7),round(v.co.y,7)))
        result['detailUv'].extend(round(x,7) for x in detail)
        result['bones'].extend((0,1,2,0));result['weights'].extend((*[round(x,7) for x in w],0.))
    for tri in mesh.loop_triangles:result['indices'].extend((tri.vertices[0],tri.vertices[2],tri.vertices[1]))
    path=output/f'garment-{cut}.skin.json';path.write_text(json.dumps(result,separators=(',',':')),encoding='utf-8')
    hashes[path.name]=hashlib.sha256(path.read_bytes()).hexdigest()
    print('GARMENT_BAKED',cut,len(mesh.vertices),len(mesh.loop_triangles),hashes[path.name])
(output/'manifest.json').write_text(json.dumps({'schemaVersion':1,'source':'SandBoxSim sewn panel garment pattern','sha256':hashes},indent=2)+'\n',encoding='utf-8')
