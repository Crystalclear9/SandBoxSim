"""Blender voxel union and smooth skin transfer for SandBoxSim quadrupeds.
Run in a factory background Blender process; all inputs/outputs are explicit.
"""
import argparse,hashlib,json,pathlib,sys
import bpy
from mathutils import Vector
from mathutils.kdtree import KDTree
args=argparse.ArgumentParser();args.add_argument('--source',required=True);args.add_argument('--output',required=True)
a=args.parse_args(sys.argv[sys.argv.index('--')+1:]);source=pathlib.Path(a.source);out=pathlib.Path(a.output);out.mkdir(parents=True,exist_ok=True)
for name in ('deer','wolf'):
 print('ANIMAL_BAKE_START',name,flush=True)
 raw=(source/(name+'-source.json')).read_bytes();data=json.loads(raw)
 p=data['positions'];points=[Vector(p[i:i+3]) for i in range(0,len(p),3)]
 indices=data['indices'];faces=[(indices[i],indices[i+2],indices[i+1]) for i in range(0,len(indices),3)]
 bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
 mesh=bpy.data.meshes.new(name);mesh.from_pydata(points,[],faces);mesh.update()
 obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj);bpy.context.view_layer.objects.active=obj;obj.select_set(True)
 mesh.remesh_voxel_size=.0045;mesh.use_remesh_preserve_volume=True
 bpy.ops.object.voxel_remesh()
 smooth=obj.modifiers.new('Continuous musculature','SMOOTH');smooth.factor=.55;smooth.iterations=5;bpy.ops.object.modifier_apply(modifier=smooth.name)
 # Anatomy is baked into the shared skin, not overlaid as intersecting muscle balls.
 # Centimetre-scale scapula, rib barrel and haunch relief survives the runtime LOD.
 import math
 back=.72 if name=='wolf' else 1.03
 def bell(value,center,width):return math.exp(-((value-center)/width)**2)
 # Reading v.normal after each coordinate write can recompute the entire
 # mesh repeatedly. Freeze this displacement pass's input normal field.
 rest_normals=[v.normal.copy() for v in obj.data.vertices]
 for v,normal in zip(obj.data.vertices,rest_normals):
  x,y,z=v.co;flank=min(1,abs(x)/.12)
  shoulder=bell(z,-.38,.17)*bell(y,back+.055,.20)
  haunch=bell(z,.40,.13)*bell(y,back-.025,.17)
  tuck=bell(z,.14,.17)*bell(y,back-.07,.12)
  relief=(shoulder*.012+haunch*.008-tuck*.005)*flank
  v.co+=normal*relief
 obj.data.update()
 # Musculature displacement can fold tiny triangles where the shoulder meets
 # a limb. Re-union the displaced surface before reducing it; smoothing alone
 # leaves back-facing slivers which flash when the joint moves.
 obj.data.remesh_voxel_size=.0045
 obj.data.use_remesh_preserve_volume=True
 bpy.ops.object.voxel_remesh()
 relax=obj.modifiers.new('Joint surface relaxation','SMOOTH');relax.factor=.45;relax.iterations=3;bpy.ops.object.modifier_apply(modifier=relax.name)
 target=24000;triangles=sum(len(f.vertices)-2 for f in obj.data.polygons)
 if triangles>target:
  decimate=obj.modifiers.new('Runtime surface budget','DECIMATE');decimate.ratio=target/triangles;bpy.ops.object.modifier_apply(modifier=decimate.name)
 obj.data.calc_loop_triangles()
 folded=sum(1 for t in obj.data.loop_triangles if t.normal.dot(sum((obj.data.vertices[i].normal for i in t.vertices),Vector()))<0)
 if folded>8:raise RuntimeError(f'{name}: {folded} folded triangles after remeshing')
 kd=KDTree(len(points))
 for i,v in enumerate(points):kd.insert(v,i)
 kd.balance()
 result={'schemaVersion':1,'sourceSha256':hashlib.sha256(raw).hexdigest(),'positions':[],'normals':[],'uv':[],'bones':[],'weights':[],'indices':[]}
 for vertex in obj.data.vertices:
  result['positions'].extend(round(v,7) for v in vertex.co);result['normals'].extend(round(v,7) for v in vertex.normal)
  result['uv'].extend((round(vertex.co.x*.7+.5,7),round(vertex.co.z*.5+.5,7)))
  influence={}
  for co,i,distance in kd.find_n(vertex.co,16):
   bone=data['bones'][i];influence[bone]=influence.get(bone,0)+1/(distance*distance+.00002)
  chosen=sorted(influence.items(),key=lambda item:(-item[1],item[0]))[:4];total=sum(w for b,w in chosen)
  bs=[b for b,w in chosen];ws=[round(w/total,7) for b,w in chosen]
  bs.extend([0]*(4-len(bs)));ws.extend([0]*(4-len(ws)));result['bones'].extend(bs);result['weights'].extend(ws)
 for triangle in obj.data.loop_triangles:
  i=triangle.vertices;result['indices'].extend((i[0],i[2],i[1]))
 path=out/(name+'.skin.json');path.write_text(json.dumps(result,separators=(',',':')),encoding='utf-8')
 print('ANIMAL_SURFACE_BAKED',name,len(obj.data.vertices),len(obj.data.loop_triangles),hashlib.sha256(path.read_bytes()).hexdigest(),flush=True)
