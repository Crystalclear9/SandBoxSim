"""Run with Blender --background --disable-autoexec --python this_file -- --source-root ... --output-root ..."""
import argparse,hashlib,json,sys
from pathlib import Path
import bpy,bmesh
from mathutils import Vector

args=argparse.ArgumentParser();args.add_argument('--source-root',type=Path,required=True);args.add_argument('--output-root',type=Path,required=True)
opts=args.parse_args(sys.argv[sys.argv.index('--')+1:])
assets=[('tree_small_02','tree_small_02_LOD1',5.5,28000),('fir_sapling','fir_sapling_b',5.4,18000),('fern_02','fern_02_b',.43,0),('grass_medium_01','grass_medium_01_small_b_LOD1',.34,0),('shrub_sorrel_01','shrub_sorrel_01_d',.8,0)]
for asset,name,height,budget in assets:
 source=opts.source_root/asset/(asset+'_1k.blend');sha=hashlib.sha256(source.read_bytes()).hexdigest()
 bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False,use_scripts=False)
 obj=bpy.data.objects.get(name)
 if obj is None or obj.type!='MESH':raise ValueError('Required source object missing: '+name)
 source_mesh=obj.data;coordinates=[obj.matrix_world@v.co for v in source_mesh.vertices]
 low=Vector(tuple(min(p[i] for p in coordinates) for i in range(3)));high=Vector(tuple(max(p[i] for p in coordinates) for i in range(3)))
 center=Vector(((low.x+high.x)*.5,(low.y+high.y)*.5,low.z));scale=height/(high.z-low.z)
 if asset=='shrub_sorrel_01':center.z=(low.z+high.z)*.5
 normal_matrix=obj.matrix_world.to_3x3().inverted().transposed();surfaces={}
 for slot,mat in enumerate(source_mesh.materials):
  material=mat.name;copy=obj.copy();copy.data=source_mesh.copy();bpy.context.collection.objects.link(copy)
  parent=list(range(len(source_mesh.vertices)))
  def find(v):
   while parent[v]!=v:parent[v]=parent[parent[v]];v=parent[v]
   return v
  leaf=asset in ('tree_small_02','fir_sapling') and ('leaves' in material or 'twigs' in material)
  if leaf:
   positions={}
   for f in source_mesh.polygons:
    if f.material_index!=slot:continue
    for v in f.vertices:
     key=tuple(round(float(x),6) for x in source_mesh.vertices[v].co)
     if key in positions:parent[find(v)]=find(positions[key])
     else:positions[key]=v
   for f in source_mesh.polygons:
    if f.material_index!=slot:continue
    for v in f.vertices[1:]:parent[find(v)]=find(f.vertices[0])
  modulus=3 if asset=='tree_small_02' else 1
  mesh_bm=bmesh.new();mesh_bm.from_mesh(copy.data);mesh_bm.faces.ensure_lookup_table()
  remove=[]
  for f in source_mesh.polygons:
   if f.material_index!=slot or (leaf and ((find(f.vertices[0])*2654435761)&0xffffffff)%modulus!=0):remove.append(mesh_bm.faces[f.index])
  bmesh.ops.delete(mesh_bm,geom=remove,context='FACES')
  if leaf:
   bmesh.ops.remove_doubles(mesh_bm,verts=list(mesh_bm.verts),dist=1e-6)
   for edge in mesh_bm.edges:edge.smooth=True
   for face in mesh_bm.faces:face.smooth=True
  mesh_bm.to_mesh(copy.data);mesh_bm.free()
  if leaf:
   for polygon in copy.data.polygons:polygon.use_smooth=True
   copy.data.normals_split_custom_set([(0,0,0)]*len(copy.data.loops))
   copy.data.update()
  copy.data.calc_loop_triangles();before=len(copy.data.loop_triangles)
  target=3000 if 'branches' in material else 1500
  if budget and not leaf and before>target:
   modifier=copy.modifiers.new('SandBoxSim wood budget','DECIMATE');modifier.ratio=target/before;modifier.use_collapse_triangulate=True
  evaluated=copy.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=evaluated.to_mesh();mesh.calc_loop_triangles()
  coordinates=[copy.matrix_world@v.co for v in mesh.vertices]
  surface=surfaces.setdefault(material,{'material':material,'positions':[],'normals':[],'uv':[],'indices':[],'lookup':{}})
  layer=mesh.uv_layers[1 if material=='tree_small_02_branches' and len(mesh.uv_layers)>1 else 0]
  for tri in mesh.loop_triangles:
   a,b,c=(coordinates[v] for v in tri.vertices);face_normal=(b-a).cross(c-a)
   if face_normal.length_squared<1e-20:continue
   face_normal.normalize()
   for loop_id in (tri.loops[0],tri.loops[2],tri.loops[1]):
    loop=mesh.loops[loop_id];p=(coordinates[loop.vertex_index]-center)*scale;n=(normal_matrix@mesh.corner_normals[loop_id].vector).normalized();uv=layer.data[loop_id].uv
    if n.length_squared<.8:n=face_normal
    values=tuple(round(float(x),6) for x in (p.x,p.z,-p.y,n.x,n.z,-n.y,uv.x,1-uv.y))
    if values not in surface['lookup']:
     surface['lookup'][values]=len(surface['positions'])//3;surface['positions'].extend(values[:3]);surface['normals'].extend(values[3:6]);surface['uv'].extend(values[6:])
    surface['indices'].append(surface['lookup'][values])
  evaluated.to_mesh_clear();data=copy.data;bpy.data.objects.remove(copy,do_unlink=True);bpy.data.meshes.remove(data)
 output=opts.output_root/asset;output.mkdir(exist_ok=True,parents=True)
 for surface in surfaces.values():surface.pop('lookup')
 data={'schemaVersion':1,'asset':asset,'source':{'url':'https://polyhaven.com/a/'+asset,'license':'CC0-1.0','sha256':sha,'object':name},'height':height,'surfaces':list(surfaces.values())}
 path=output/(asset+'.mesh.json');path.write_text(json.dumps(data,separators=(',',':')),encoding='utf-8')
 print('ENVIRONMENT_EXPORTED',asset,'triangles',sum(len(s['indices'])//3 for s in surfaces.values()),'vertices',sum(len(s['positions'])//3 for s in surfaces.values()),'sha256',hashlib.sha256(path.read_bytes()).hexdigest())
