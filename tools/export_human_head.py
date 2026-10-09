"""Blender CLI exporter for the attributed Blender Studio human base derivative.

Run with --background --disable-autoexec SOURCE.blend --python this_file --
--output DIRECTORY. No source-file scripts or materials are executed by this tool.
"""
import argparse, hashlib, json, math, pathlib, sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

SOURCE_URL = "https://studio.blender.org/training/realistic-human-research/use-of-base-meshes/"
SOURCE_SHA256 = "1d9d2a6070acd6a3d0d9b676af816001310fb0f6128d07a3ab3e0318c7fdd981"

args = argparse.ArgumentParser()
args.add_argument("--output", required=True)
opt = args.parse_args(sys.argv[sys.argv.index("--")+1:])
source = pathlib.Path(bpy.data.filepath)
if hashlib.sha256(source.read_bytes()).hexdigest() != SOURCE_SHA256:
    raise RuntimeError("Unexpected upstream human source")

output = pathlib.Path(opt.output); output.mkdir(parents=True, exist_ok=True)
body = bpy.data.objects["GEO-body"]
body.modifiers["Multires"].show_viewport = True
body.modifiers["Multires"].levels = 1
bpy.context.view_layer.update()
deps = bpy.context.evaluated_depsgraph_get()
mesh = body.evaluated_get(deps).to_mesh(preserve_all_data_layers=True, depsgraph=deps)
mesh.calc_loop_triangles()
regions = mesh.attributes[".sculpt_face_set"]
eye_object = bpy.data.objects["GEO-eye.cornea.R"]
eye_origin = eye_object.matrix_world.translation
eye_z = float(eye_origin.z)
scale_x = .93
scale_y, scale_z = .90, .80
center_z, center_y = eye_z - .010/scale_y, -.030

def point(world, neck=False):
    x, y, z = -world.x*scale_x, (world.z-center_z)*scale_y, (world.y-center_y)*scale_z
    if neck:
        t = min(1, max(0, (1.590-world.z)/.090)); t = t*t*(3-2*t)
        x *= 1-.75*t; z = z*(1-.06*t)-.022*t
    return Vector((x, y, z))

def encode(part_mesh, transform, selected):
    positions, normals, colors, uv, indices = [], [], [], [], []
    lookup = {}
    for tri, region in selected:
        corner_indices = []
        for loop_id in tri.loops:
            loop = part_mesh.loops[loop_id]; vertex = part_mesh.vertices[loop.vertex_index]
            world = transform@vertex.co
            p = point(world, region == 14)
            key = (loop.vertex_index, region if region in {22,23,38,39,40,41,42,43} else 0)
            if key not in lookup:
                lookup[key] = len(positions)//3
                positions.extend(round(v, 7) for v in p)
                # Normals account for axis conversion and normalization; keep authored smoothing.
                n = transform.to_3x3().inverted().transposed()@vertex.normal
                n = Vector((-n.x/scale_x, n.z/scale_y, n.y/scale_z)).normalized()
                normals.extend(round(v, 7) for v in n)
                uv.extend((round(.5+p.x/.177,7), round(.5-p.y/.220,7)))
                if region in {22,23}: color=(1,.86,.82,1)
                elif region in {38,39,40,41,42,43}: color=(.38,.23,.21,1)
                else: color=(1,.965 if region==21 else .99,.95 if region==21 else .98,1)
                colors.extend(color)
            corner_indices.append(lookup[key])
        # Blender CCW -> Godot clockwise after the right-handed axis conversion.
        indices.extend((corner_indices[0], corner_indices[2], corner_indices[1]))
    return {"positions":positions,"normals":normals,"colors":colors,"uv":uv,"indices":indices}

head_regions = {14,18,19,20,21,22,23,24,25,30,31,32,33,36,37,38,39,40,41,42,43,103,104,105}
selected, lip_triangles, scalp_triangles = [], [], []
for tri in mesh.loop_triangles:
    region = regions.data[tri.polygon_index].value
    if region not in head_regions: continue
    world = [body.matrix_world@mesh.vertices[i].co for i in tri.vertices]
    if min(p.z for p in world) < 1.500: continue
    if region in {22,23}: lip_triangles.append((tri,region))
    else: selected.append((tri,region))
    if region == 25: scalp_triangles.append((tri,region))

head = encode(mesh,body.matrix_world,selected)
lips = encode(mesh,body.matrix_world,lip_triangles)
eyes = []
eye_mesh = eye_object.evaluated_get(deps).to_mesh(); eye_mesh.calc_loop_triangles()
for sign in (-1,1):
    triangles=[]
    for tri in eye_mesh.loop_triangles:
        center=sum((point(eye_object.matrix_world@eye_mesh.vertices[i].co) for i in tri.vertices),Vector())/3
        if center.x*sign>0:triangles.append((tri,0))
    eyes.append(encode(eye_mesh,eye_object.matrix_world,triangles))

scalp = encode(mesh,body.matrix_world,scalp_triangles)
vertices=[Vector(scalp['positions'][i:i+3]) for i in range(0,len(scalp['positions']),3)]
polygons=[scalp['indices'][i:i+3] for i in range(0,len(scalp['indices']),3)]
bvh=BVHTree.FromPolygons(vertices,polygons,all_triangles=True)
rows,columns=64,128
low,high=min(p.y for p in vertices),max(p.y for p in vertices)
centers,radii=[],[]
for row in range(rows):
    y=low+(high-low)*(row+.5)/rows
    band=[p for p in vertices if abs(p.y-y)<(high-low)/rows]
    if not band:band=sorted(vertices,key=lambda p:abs(p.y-y))[:12]
    center=(min(p.z for p in band)+max(p.z for p in band))*.5
    centers.append(round(center,7))
    origin=Vector((0,y,center))
    for col in range(columns):
        angle=col*math.tau/columns; direction=Vector((math.cos(angle),0,math.sin(angle)))
        hit,normal,index,distance=bvh.ray_cast(origin,direction,.4)
        if hit is None:
            aligned=sorted(band,key=lambda p:math.atan2(p.z-center,p.x))
            best=min(aligned,key=lambda p:abs(math.atan2(math.sin(math.atan2(p.z-center,p.x)-angle),math.cos(math.atan2(p.z-center,p.x)-angle))))
            distance=max(.001,(best-origin).dot(direction))
        radii.append(round(max(.001,distance),7))

eye_center=point(eye_origin)
mouth_vertices=[Vector(lips['positions'][i:i+3]) for i in range(0,len(lips['positions']),3)]
mouth=sum(mouth_vertices,Vector())/len(mouth_vertices);mouth.x=0
nose_candidates=[point(body.matrix_world@mesh.vertices[i].co) for tri,region in selected if region==20 for i in tri.vertices]
nose=min(nose_candidates,key=lambda p:p.z)
result={"schemaVersion":1,"source":{"creator":"Julien Kaspar / Blender Studio","url":SOURCE_URL,"license":"CC-BY-4.0","sha256":SOURCE_SHA256},
        "head":head,"lips":lips,"eyes":eyes,
        "eyeCenter":[abs(float(eye_center.x)),float(eye_center.y),float(eye_center.z)],
        "mouthCenter":list(mouth),"noseProbeY":float(nose.y),
        "scalp":{"rows":rows,"columns":columns,"low":low,"high":high,"centers":centers,"radii":radii}}
target=output/'human-head.mesh.json';target.write_text(json.dumps(result,separators=(',',':')),encoding='utf-8')
print('HUMAN_ASSET_EXPORTED',target,{'headVertices':len(head['positions'])//3,'headTriangles':len(head['indices'])//3,'mouthCenter':list(mouth),'eyeCenter':list(eye_center),'noseProbeY':nose.y})
