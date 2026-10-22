"""Weld the authored torso/sleeve volumes into three runtime skinned garments.

Run in the project-local Blender with --background --factory-startup --disable-autoexec.
Sources and output paths must be supplied explicitly; no network or user files.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys

import bmesh
import bpy
from mathutils import Vector
from mathutils.kdtree import KDTree

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--source',required=True)
parser.add_argument('--output',required=True)
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
source=Path(args.source);output=Path(args.output);output.mkdir(parents=True,exist_ok=True)
hashes={}
for cut in range(3):
    raw=(source/f'garment-{cut}-source.json').read_bytes();data=json.loads(raw)
    p=data['positions'];points=[Vector(p[i:i+3]) for i in range(0,len(p),3)]
    ids=data['indices'];faces=[(ids[i],ids[i+2],ids[i+1]) for i in range(0,len(ids),3)]
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    mesh=bpy.data.meshes.new('garment');mesh.from_pydata(points,[],faces);mesh.update()
    # Seal each authored volume before voxel union, then reopen neck and hem.
    bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001)
    boundary=[e for e in bm.edges if e.is_boundary]
    bmesh.ops.holes_fill(bm,edges=boundary,sides=0)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
    obj=bpy.data.objects.new('garment',mesh);bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active=obj;obj.select_set(True)
    mesh.remesh_voxel_size=.0035;mesh.use_remesh_preserve_volume=True
    bpy.ops.object.voxel_remesh()
    smooth=obj.modifiers.new('Sewn shoulder transition','SMOOTH');smooth.factor=.6;smooth.iterations=16
    bpy.ops.object.modifier_apply(modifier=smooth.name)
    bm=bmesh.new();bm.from_mesh(obj.data)
    caps=[f for f in bm.faces if (f.calc_center_median().y<data['hem']+.009 and f.normal.y<-.5)
          or (f.calc_center_median().y>1.312 and (f.calc_center_median().x/.060)**2+(f.calc_center_median().z/.069)**2<1.05)]
    bmesh.ops.delete(bm,geom=caps,context='FACES')
    # Opening the hem can leave the original inner facing as a detached sliver.
    # Keep the connected outer garment, and reject a broken sleeve rather than hiding it.
    seen=set();components=[]
    for v in bm.verts:
        if v in seen:continue
        todo=[v];seen.add(v);part=[]
        while todo:
            current=todo.pop();part.append(current)
            for edge in current.link_edges:
                other=edge.other_vert(current)
                if other not in seen:seen.add(other);todo.append(other)
        components.append(part)
    main=max(components,key=len)
    if len(main)<len(bm.verts)*.75 or min(v.co.x for v in main)>-.24 or max(v.co.x for v in main)<.24:
        raise RuntimeError('Garment union lost a connected sleeve')
    keep=set(main);bmesh.ops.delete(bm,geom=[v for v in bm.verts if v not in keep],context='VERTS')
    bm.to_mesh(obj.data);bm.free()
    triangles=sum(len(f.vertices)-2 for f in obj.data.polygons)
    dec=obj.modifiers.new('Runtime garment budget','DECIMATE');dec.ratio=min(1,10000/triangles)
    bpy.ops.object.modifier_apply(modifier=dec.name)
    bm=bmesh.new();bm.from_mesh(obj.data)
    bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS')
    bm.to_mesh(obj.data);bm.free();obj.data.calc_loop_triangles()
    # Skin transfer uses multiple nearby samples across the welded seam.
    kd=KDTree(len(points))
    for i,v in enumerate(points):kd.insert(v,i)
    kd.balance()
    result={'schemaVersion':1,'sourceSha256':hashlib.sha256(raw).hexdigest(),'positions':[],'normals':[],'uv':[],'bones':[],'weights':[],'indices':[]}
    for v in obj.data.vertices:
        result['positions'].extend(round(x,7) for x in v.co);result['normals'].extend(round(x,7) for x in v.normal.normalized())
        result['uv'].extend((round(v.co.x+v.co.z,7),round(v.co.y,7)))
        influence={}
        for co,i,d in kd.find_n(v.co,12):
            for j in range(4):
                bone=data['bones'][i*4+j];w=data['weights'][i*4+j]/(d*d+.00001)
                influence[bone]=influence.get(bone,0)+w
        chosen=sorted(influence.items(),key=lambda x:(-x[1],x[0]))[:4];total=sum(w for _,w in chosen)
        bs=[b for b,_ in chosen];ws=[round(w/total,7) for _,w in chosen]
        result['bones'].extend(bs+[0]*(4-len(bs)));result['weights'].extend(ws+[0]*(4-len(ws)))
    for tri in obj.data.loop_triangles:result['indices'].extend((tri.vertices[0],tri.vertices[2],tri.vertices[1]))
    path=output/f'garment-{cut}.skin.json';path.write_text(json.dumps(result,separators=(',',':')),encoding='utf-8')
    hashes[path.name]=hashlib.sha256(path.read_bytes()).hexdigest()
    print('GARMENT_BAKED',cut,len(obj.data.vertices),len(obj.data.loop_triangles),hashes[path.name])
(output/'manifest.json').write_text(json.dumps({'schemaVersion':1,'source':'SandBoxSim authored garment geometry','sha256':hashes},indent=2)+'\n',encoding='utf-8')
