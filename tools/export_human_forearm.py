"""Extract the licensed continuous elbow/forearm and fit the resident wrist.

Blender --background --disable-autoexec SOURCE.blend --python THIS -- --output DIR
The source anatomy supplies the olecranon, muscle bellies and wrist tendons;
only the centreline, length and wrist opening are retargeted to the game rig.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys
import bmesh
import bpy
from mathutils import Vector

SOURCE_HASH='1d9d2a6070acd6a3d0d9b676af816001310fb0f6128d07a3ab3e0318c7fdd981'
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output',required=True)
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
if hashlib.sha256(Path(bpy.data.filepath).read_bytes()).hexdigest()!=SOURCE_HASH:
    raise ValueError('Unexpected licensed human source')
body=bpy.data.objects['GEO-body'];body.modifiers['Multires'].show_viewport=True;body.modifiers['Multires'].levels=1
bpy.context.view_layer.update();deps=bpy.context.evaluated_depsgraph_get()
source=body.evaluated_get(deps).to_mesh(preserve_all_data_layers=True,depsgraph=deps);source.calc_loop_triangles()
regions=source.attributes['.sculpt_face_set']
selected=[t for t in source.loop_triangles if regions.data[t.polygon_index].value in {12,102}
          and all((body.matrix_world@source.vertices[i].co).x>.22 for i in t.vertices)]
ids=sorted({i for t in selected for i in t.vertices});lookup={v:i for i,v in enumerate(ids)}
mesh=bpy.data.meshes.new('licensed-forearm')
mesh.from_pydata([body.matrix_world@source.vertices[i].co for i in ids],[],[tuple(lookup[i] for i in t.vertices) for t in selected]);mesh.update()
# Cut across the continuous authored surface, not separate region end caps.
low,high=.967,1.247
bm=bmesh.new();bm.from_mesh(mesh)
for z,normal in ((low,(0,0,-1)),(high,(0,0,1))):
    bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),dist=1e-7,
                          plane_co=(0,0,z),plane_no=normal,clear_outer=True,clear_inner=False)
bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-6)
bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS')
bm.to_mesh(mesh);bm.free()
edges={}
for poly in mesh.polygons:
    for edge in poly.edge_keys:edges[edge]=edges.get(edge,0)+1
wrist={i for edge,count in edges.items() if count==1 for i in edge if mesh.vertices[i].co.z<1.08}
points=[v.co.copy() for v in mesh.vertices]
# Smooth the source's relaxed angled centreline; preserve the radial authored
# asymmetry instead of replacing it with elliptical cross sections.
rows=65;centers=[]
for row in range(rows):
    z=low+(high-low)*row/(rows-1)
    band=sorted(points,key=lambda p:abs(p.z-z))[:max(24,len(points)//35)]
    centers.append(Vector(((min(p.x for p in band)+max(p.x for p in band))*.5,
                           (min(p.y for p in band)+max(p.y for p in band))*.5,0)))
for _ in range(4):
    centers=[sum(centers[max(0,i-2):min(rows,i+3)],Vector())/len(centers[max(0,i-2):min(rows,i+3)]) for i in range(rows)]
for v in mesh.vertices:
    p=v.co.copy();t=max(0,min(1,(p.z-low)/(high-low)));sample=t*(rows-1);i=min(rows-2,int(sample));center=centers[i].lerp(centers[i+1],sample-i)
    q=Vector(((p.y-center.y)*.82,-.210+t*.230,(p.x-center.x)*.82))
    blend=max(0,min(1,(.13-t)/.13));blend=blend*blend*(3-2*blend)
    # Match the hand's open wrist ring at hand.Y + .045 = -.210.
    radial=Vector((q.x/.027,0,q.z/.024));radial.normalize()
    if v.index in wrist:q.y=-.210;blend=1
    target=Vector((radial.x*.027,q.y,radial.z*.024))
    v.co=q.lerp(target,blend)
mesh.update()
bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free();mesh.calc_loop_triangles()
result={'schemaVersion':1,'sourceSha256':SOURCE_HASH,'regions':[12,102],'positions':[],'normals':[],'uv':[],'indices':[]}
for v in mesh.vertices:
    result['positions'].extend(round(x,7) for x in v.co);result['normals'].extend(round(x,7) for x in v.normal.normalized())
    result['uv'].extend((round(v.co.x*4+.5,7),round(-v.co.y*4,7)))
for tri in mesh.loop_triangles:result['indices'].extend((tri.vertices[0],tri.vertices[2],tri.vertices[1]))
out=Path(args.output);out.mkdir(parents=True,exist_ok=True)
target=out/'human-forearm.mesh.json';target.write_text(json.dumps(result,separators=(',',':')),encoding='utf-8')
print('HUMAN_FOREARM_EXPORTED',len(mesh.vertices),len(mesh.loop_triangles),hashlib.sha256(target.read_bytes()).hexdigest())
