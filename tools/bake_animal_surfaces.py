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
 raw=(source/(name+'-source.json')).read_bytes();data=json.loads(raw)
 p=data['positions'];points=[Vector(p[i:i+3]) for i in range(0,len(p),3)]
 indices=data['indices'];faces=[(indices[i],indices[i+2],indices[i+1]) for i in range(0,len(indices),3)]
 bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
 mesh=bpy.data.meshes.new(name);mesh.from_pydata(points,[],faces);mesh.update()
 obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj);bpy.context.view_layer.objects.active=obj;obj.select_set(True)
 mesh.remesh_voxel_size=.008;mesh.use_remesh_preserve_volume=True
 bpy.ops.object.voxel_remesh()
 smooth=obj.modifiers.new('Continuous musculature','SMOOTH');smooth.factor=.45;smooth.iterations=3;bpy.ops.object.modifier_apply(modifier=smooth.name)
 # Anatomy is baked into the shared skin, not overlaid as intersecting muscle balls.
 # Centimetre-scale scapula, rib barrel and haunch relief survives the runtime LOD.
 import math
 back=.72 if name=='wolf' else 1.03
 def bell(value,center,width):return math.exp(-((value-center)/width)**2)
 for v in obj.data.vertices:
  x,y,z=v.co;flank=min(1,abs(x)/.12)
  shoulder=bell(z,-.38,.17)*bell(y,back+.055,.20)
  haunch=bell(z,.40,.13)*bell(y,back-.025,.17)
  tuck=bell(z,.14,.17)*bell(y,back-.07,.12)
  relief=(shoulder*.012+haunch*.008-tuck*.005)*flank
  v.co+=v.normal*relief
 obj.data.update()
 target=18000;triangles=sum(len(f.vertices)-2 for f in obj.data.polygons)
 if triangles>target:
  decimate=obj.modifiers.new('Runtime surface budget','DECIMATE');decimate.ratio=target/triangles;bpy.ops.object.modifier_apply(modifier=decimate.name)
 obj.data.calc_loop_triangles()
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
 print('ANIMAL_SURFACE_BAKED',name,len(obj.data.vertices),len(obj.data.loop_triangles),hashlib.sha256(path.read_bytes()).hexdigest())
