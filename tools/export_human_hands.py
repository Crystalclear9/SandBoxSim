"""Bake licensed anatomical hands into the resident's 16-bone rest scaffold.
Blender: --background --disable-autoexec SOURCE.blend --python THIS -- --output DIRECTORY
"""
import argparse, hashlib, json, math, pathlib, sys
from collections import defaultdict
import bpy
from mathutils import Vector

SOURCE_HASH="1d9d2a6070acd6a3d0d9b676af816001310fb0f6128d07a3ab3e0318c7fdd981"
p=argparse.ArgumentParser();p.add_argument('--output',required=True)
args=p.parse_args(sys.argv[sys.argv.index('--')+1:])
if hashlib.sha256(pathlib.Path(bpy.data.filepath).read_bytes()).hexdigest()!=SOURCE_HASH:raise ValueError('Human source hash mismatch')
body=bpy.data.objects['GEO-body'];body.modifiers['Multires'].show_viewport=True;body.modifiers['Multires'].levels=1
bpy.context.view_layer.update();deps=bpy.context.evaluated_depsgraph_get()
m=body.evaluated_get(deps).to_mesh(preserve_all_data_layers=True,depsgraph=deps);m.calc_loop_triangles()
face_sets=m.attributes['.sculpt_face_set']
groups=[{49,50,78,79},{45,47,53,77},{44,48,88,89},{46,52,90,91},{51,92}]
allowed={93}|set.union(*groups)
membership=defaultdict(set);selected=[];palm_ids=set()
for tri in m.loop_triangles:
 region=face_sets.data[tri.polygon_index].value
 if region not in allowed or any(m.vertices[i].co.x<=0 for i in tri.vertices):continue
 selected.append((tri,region))
 for i in tri.vertices:
  if region==93:palm_ids.add(i)
  for digit,g in enumerate(groups):
   if region in g:membership[i].add(digit)
world={i:body.matrix_world@m.vertices[i].co for tri,r in selected for i in tri.vertices}
# Source cross-section centres straighten the authored, relaxed fingers before binding.
profiles=[]
for digit,g in enumerate(groups):
 ids={i for tri,r in selected if r in g for i in tri.vertices}
 ps=[world[i] for i in ids];low=min(v.z for v in ps);high=max(v.z for v in ps)
 rows=[]
 for j in range(25):
  z=low+(high-low)*j/24
  band=sorted(ps,key=lambda v:abs(v.z-z))[:max(12,len(ps)//16)]
  rows.append(((min(v.x for v in band)+max(v.x for v in band))/2,(min(v.y for v in band)+max(v.y for v in band))/2))
 profiles.append((low,high,rows))
scales=[.92,1,.96,.86,1]
def smooth(v):v=max(0,min(1,v));return v*v*(3-2*v)
def finger_point(p,digit):
 low,high,rows=profiles[digit];sample=max(0,min(24,(p.z-low)/(high-low)*24));i=min(23,int(sample));t=sample-i
 cx=rows[i][0]*(1-t)+rows[i+1][0]*t;cy=rows[i][1]*(1-t)+rows[i+1][1]*t
 thumb=digit==4;total=(.034 if thumb else .058)*scales[digit]
 origin=Vector((-.034 if thumb else -.022+digit*.014,-.021 if thumb else -.04,.001 if thumb else -.005))
 return origin+Vector(((p.y-cy)*.55,-(high-p.z)/(high-low)*total,(p.x-cx)*.64))
positions={};digit_for={}
for i,p in world.items():
 base=Vector(((p.y+.083)*.55,(p.z-.918)*.69,(p.x-.425)*.64))
 if membership[i]:
  d=min(membership[i]);positions[i]=finger_point(p,d);digit_for[i]=d
 else:
  # Blend the palm into each MCP root while keeping the authored webbing continuous.
  targets=[]
  for d,(low,high,rows) in enumerate(profiles):
   cx,cy=rows[-1];distance=((p.y-cy)/.018)**2+((p.z-high)/.024)**2
   weight=math.exp(-distance)*smooth((high+.025-p.z)/.025)
   if weight>.0001:targets.append((d,weight,finger_point(p,d)))
  total=sum(w for d,w,p in targets)
  mix=min(.95,total)
  if total:base=base*(1-mix)+sum((target*w for d,w,target in targets),Vector())*(mix/total)
  positions[i]=base
  if targets and total>.5:digit_for[i]=max(targets,key=lambda item:item[1])[0]
# Smooth the retargeting displacement around the MCP/web junctions. Keeping the
# authored differential detail avoids both a pinched web and an artificial flat palm.
neighbors=defaultdict(set)
for tri,region in selected:
 for i in tri.vertices:neighbors[i].update(j for j in tri.vertices if j!=i)
affine={i:Vector(((p.y+.083)*.55,(p.z-.918)*.69,(p.x-.425)*.64)) for i,p in world.items()}
offsets={i:positions[i]-affine[i] for i in world}
for iteration in range(16):
 updated={}
 for i,p in world.items():
  amount=.5 if i in palm_ids and p.z<.944 else 0
  if i not in palm_ids and i in digit_for:
   d=digit_for[i];origin=-.021 if d==4 else -.04
   amount=.32*(1-smooth((origin-positions[i].y)/.018))
  if amount>0:
   average=sum((offsets[j] for j in neighbors[i]),Vector())/len(neighbors[i])
   updated[i]=offsets[i].lerp(average,amount)
  else:updated[i]=offsets[i]
 offsets=updated
positions={i:affine[i]+offsets[i] for i in world}
for i in palm_ids:
 if positions[i].y<-.04:positions[i].y=-.04-.005*math.tanh((-positions[i].y-.04)/.005)
# Retarget the open wrist ring into the forearm's exact cross section. The source
# wrist is oblique; leaving that cut unmodified produces visible pointed corners.
edges=defaultdict(int)
for tri,region in selected:
 for a,b in zip(tri.vertices,tuple(tri.vertices[1:])+tuple(tri.vertices[:1])):edges[tuple(sorted((a,b)))]+=1
boundary={i for edge,count in edges.items() if count==1 for i in edge}
if not boundary or min(world[i].z for i in boundary)<.94:raise ValueError('Unexpected hand boundary outside wrist')
cx=(min(world[i].x for i in boundary)+max(world[i].x for i in boundary))/2
cy=(min(world[i].y for i in boundary)+max(world[i].y for i in boundary))/2
for i,p in world.items():
 blend=smooth((p.z-.938)/.030)
 if blend<=0:continue
 angle=math.atan2((p.x-cx)*.64,(p.y-cy)*.55)
 target=Vector((math.cos(angle)*.027,positions[i].y,.010+math.sin(angle)*.024))
 if i in boundary:target.y=.045;blend=1
 positions[i]=positions[i]*(1-blend)+target*blend
# Preserve the single connected source surface and compute normals after retargeting.
ids=sorted(world);lookup={v:i for i,v in enumerate(ids)}
verts=[positions[i] for i in ids];triangles=[tuple(lookup[i] for i in tri.vertices) for tri,r in selected]
baked=bpy.data.meshes.new('SandBoxSim_anatomical_hand');baked.from_pydata(verts,[],triangles);baked.update()
result={'schemaVersion':1,'sourceSha256':SOURCE_HASH,'positions':[],'normals':[],'uv':[],'bones':[],'weights':[],'indices':[],'nailIndices':[]}
for v,i in zip(baked.vertices,ids):
 point=v.co;result['positions'].extend(round(c,7) for c in point);result['normals'].extend(round(c,7) for c in v.normal)
 result['uv'].extend((round(.5+point.x*6,7),round(.5+point.y*6,7)))
 if i in palm_ids:
  wrist=max(0,min(1,(point.y-.005)/.040))
  near=sorted(range(5),key=lambda d:(point.x-(-.034 if d==4 else -.022+d*.014))**2+(point.y-(-.021 if d==4 else -.04))**2)[:2]
  influence=[1/((point.x-(-.034 if d==4 else -.022+d*.014))**2+.00004) for d in near]
  total=sum(influence);curl=.25*smooth((-point.y-.006)/.040)*(1-wrist)
  result['bones'].extend((0,13 if near[0]==4 else 1+near[0]*3,13 if near[1]==4 else 1+near[1]*3,15))
  result['weights'].extend((round(1-wrist-curl,7),round(curl*influence[0]/total,7),round(curl*influence[1]/total,7),round(wrist,7)))
 elif i in digit_for:
  d=digit_for[i];thumb=d==4;bone=13 if thumb else 1+d*3
  distance=max(0,(-.021 if thumb else -.04)-point.y);scale=scales[d]
  first=(.018 if thumb else .026)*scale;second=.044*scale
  weight=max(0,min(1,(distance-first+.006*scale)/(.012*scale)))
  if not thumb and distance>second-.004*scale:bone+=1;weight=max(0,min(1,(distance-second+.004*scale)/(.008*scale)))
  palm=1-smooth((((-.021 if thumb else -.04)+.012)-point.y)/(.025 if thumb else .035))
  result['bones'].extend((0,bone,bone+1,0));result['weights'].extend((round(palm,7),round((1-palm)*(1-weight),7),round((1-palm)*weight,7),0))
 else:
  wrist=max(0,min(1,(point.y-.005)/.040));result['bones'].extend((0,15,0,0));result['weights'].extend((round(1-wrist,7),round(wrist,7),0,0))
for (tri,region),indices in zip(selected,triangles):
 result['indices'].extend((indices[0],indices[2],indices[1]))
 digit=next((d for d,g in enumerate(groups) if region in g),None)
 if digit is not None:
  center=sum((baked.vertices[i].co for i in indices),Vector())/3
  normal=sum((baked.vertices[i].normal for i in indices),Vector())/3
  thumb=digit==4;scale=scales[digit];origin_x=-.034 if thumb else -.022+digit*.014
  distance=(-.021 if thumb else -.04)-center.y;total=(.034 if thumb else .058)*scale
  ellipse=((center.x-origin_x)/(.0043 if thumb else .0038))**2+((distance-(total-.007*scale))/(.006*scale))**2
  if ellipse<1 and normal.z>.5:result['nailIndices'].extend((indices[0],indices[2],indices[1]))
out=pathlib.Path(args.output);out.mkdir(parents=True,exist_ok=True)
target=out/'human-hand.mesh.json';target.write_text(json.dumps(result,separators=(',',':')),encoding='utf-8')
print('HUMAN_HAND_EXPORTED',len(verts),len(triangles),hashlib.sha256(target.read_bytes()).hexdigest())
