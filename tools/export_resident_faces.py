"""Build textured resident heads from verified CC0 MakeHuman data in Blender.

No MakeHuman application or add-on is executed. Source OBJ/target/MHCLO files
stay in .tools/sources/makehuman; only fitted runtime surfaces are exported.
"""
import argparse
from collections import Counter
import hashlib
import json
import math
from pathlib import Path
import shutil
import sys

import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

parser = argparse.ArgumentParser()
parser.add_argument('--source', required=True)
parser.add_argument('--output', required=True)
parser.add_argument('--kind', choices=('masculine', 'neutral', 'feminine'), required=True)
args = parser.parse_args(sys.argv[sys.argv.index('--')+1:])
root, out = Path(args.source), Path(args.output)
out.mkdir(parents=True, exist_ok=True)
provenance=json.loads((Path(__file__).resolve().parents[1]/'src/SandBoxSim.Godot/assets/models/human/head-sources.json').read_text(encoding='utf-8'))
inputs = {}


def read(name, expected=None):
    path = root/name
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    expected = expected or provenance['files'][name]['sha256']
    if expected and digest != expected:
        raise ValueError('Changed source: '+name)
    inputs[name] = digest
    return path


def obj_data(path):
    points, uv, faces = [], [], []
    group = ''
    for line in path.read_text(encoding='utf-8').splitlines():
        t = line.split()
        if not t or t[0].startswith('#'):
            continue
        if t[0] == 'v': points.append(Vector(map(float, t[1:4])))
        elif t[0] == 'vt': uv.append(tuple(map(float, t[1:3])))
        elif t[0] == 'g': group = t[1]
        elif t[0] == 'f':
            corners = [tuple(int(v)-1 for v in item.split('/')[:2]) for item in t[1:]]
            faces.append((group, corners))
    return points, uv, faces


base, base_uv, base_faces = obj_data(read('base.obj', '8e761e6624b8f54536409135d1636da63b32486a90d4897f84e121d144f6fb4c'))
points = [p.copy() for p in base]
sex = 'female' if args.kind == 'feminine' else 'male'
macro_weight = .84 if args.kind == 'neutral' else 1.0
target = read('caucasian-'+sex+'-young.target',
    '118379f6e8ba9266247fdb8788a20e1df40a239f97ced0b9905bcbcc74f6e820' if sex == 'female' else
    '70e228ba7164737dae664454394536fc5935fa48d333c1a97d77e2dc6eacc5f5')
for line in target.read_text().splitlines():
    t = line.split()
    if len(t) == 4 and not line.startswith('#'):
        points[int(t[0])] += Vector(map(float, t[1:]))*macro_weight
groups = json.loads(read('basemesh_vertex_groups.json').read_text())


def landmark(group):
    ids = [i for a, b in groups[group] for i in range(a, b+1)]
    return sum((points[i] for i in ids), Vector())/len(ids)


eye = landmark('joint-l-eye')
eye_y, center_z = eye.y, eye.z-.074/.1


def point(p):
    result=Vector((p.x*.1, (p.y-eye_y)*.1+.010, -(p.z-center_z)*.1))
    # Fit the lowest cervical ring inside the existing collar, keeping the
    # jaw and upper neck untouched instead of importing a slice of trapezius.
    t=min(1.,max(0.,(-result.y-.084)/.065));t=t*t*(3-2*t)
    result.x*=1-.55*t
    result.z=result.z*(1-.22*t)-.004*t
    return result


def fit(name):
    path = read(name)
    lines = path.read_text(encoding='utf-8').splitlines()
    scales = [1., 1., 1.]
    for line in lines:
        t = line.split()
        if len(t) == 4 and t[0] in ('x_scale', 'y_scale', 'z_scale'):
            axis = 'xyz'.index(t[0][0])
            scales[axis] = abs(points[int(t[1])][axis]-points[int(t[2])][axis])/float(t[3])
    fitted = []
    started = False
    for line in lines:
        t = line.split()
        if t[:1] == ['verts']:
            started = True
            continue
        if not started or not t or t[0].startswith('#'):
            continue
        if len(t) != 9:
            if fitted: break
            continue
        ids, weights = list(map(int, t[:3])), list(map(float, t[3:6]))
        offset = Vector(float(t[6+i])*scales[i] for i in range(3))
        fitted.append(sum((points[i]*w for i, w in zip(ids, weights)), Vector())+offset)
    return fitted


closed = [p.copy() for p in points]
for side in ('left', 'right'):
    path = read('eye-'+side+'-closure.target',
        'e38cb77fd888de3d0d944efe6f3511bf3ca4076b0397fa7c840d8368f08205c2' if side == 'left' else
        '58faf2c199272bfd87478ea122cdaa4b4b8669505be0d75f777c5471527b2b15')
    for line in path.read_text().splitlines():
        t = line.split()
        if len(t) == 4 and not line.startswith('#'):
            closed[int(t[0])] += Vector(map(float, t[1:]))


def mesh_object(name, vertices, uv, faces):
    mesh = bpy.data.meshes.new(name)
    # The source uses +Z forward; point() reflects it into Godot's -Z forward.
    # Reverse polygon winding once here so Blender computes outward normals.
    # encode() subsequently converts Blender's CCW fronts to Godot's CW fronts.
    faces=[list(reversed(f)) for f in faces]
    mesh.from_pydata(vertices, [], [[v for v, _ in f] for f in faces])
    mesh.update()
    layer = mesh.uv_layers.new(name='Source UV')
    for poly, corners in zip(mesh.polygons, faces):
        poly.use_smooth = True
        for loop, (_, vt) in zip(poly.loop_indices, corners):
            layer.data[loop].uv = uv[vt]
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return obj


# Include a lower collar margin before subdivision; clip only after subdivision.
head_faces = [f for group, f in base_faces if group == 'body' and
              min(point(points[v]).y for v, _ in f) > -.19]
body = mesh_object('CC0 resident head', [point(p) for p in points], base_uv, head_faces)
blink = mesh_object('Closed source eyelids', [point(p) for p in closed], base_uv, head_faces)
for obj in (body, blink):
    mod = obj.modifiers.new('Anatomical subdivision', 'SUBSURF')
    mod.levels = 1
deps = bpy.context.evaluated_depsgraph_get()
low = body.evaluated_get(deps).to_mesh()
shut = blink.evaluated_get(deps).to_mesh()
low.calc_loop_triangles()
# HM08's UV islands are stable across sex/identity morphs. Locate the actual
# lip fissure in that topology rather than guessing its height below the eyes.
mouth_loop=min(range(len(low.loops)),key=lambda i:
    (low.uv_layers.active.data[i].uv.x-.909)**2+
    (low.uv_layers.active.data[i].uv.y-(1-.5168))**2)
mouth_y=low.vertices[low.loops[mouth_loop].vertex_index].co.y


def encode(mesh, triangles, closed_mesh=None, lip=False):
    result = {k: [] for k in ('positions', 'normals', 'uv', 'colors', 'indices')}
    if closed_mesh is not None: result['blink'] = []
    boundary = set()
    if lip:
        edges = Counter()
        for tri in triangles:
            a, b, c = tri.vertices
            for u, v in ((a,b), (b,c), (c,a)): edges[tuple(sorted((u,v)))] += 1
        boundary = {v for edge,count in edges.items() if count == 1 for v in edge}
    lookup = {}
    for tri in triangles:
        face = []
        for loop in tri.loops:
            v = mesh.loops[loop].vertex_index
            tex = mesh.uv_layers.active.data[loop].uv
            key = v, round(tex.x,7), round(tex.y,7)
            if key not in lookup:
                lookup[key] = len(result['positions'])//3
                result['positions'].extend(round(x,7) for x in mesh.vertices[v].co)
                result['normals'].extend(round(x,7) for x in mesh.vertices[v].normal)
                result['uv'].extend((round(tex.x,7), round(1-tex.y,7)))
                result['colors'].extend((1,1,1,0 if lip and v in boundary else 1))
                if closed_mesh is not None:
                    result['blink'].extend(round(x,7) for x in closed_mesh.vertices[v].co-mesh.vertices[v].co)
            face.append(lookup[key])
        # Godot uses clockwise front faces.
        result['indices'].extend((face[0], face[2], face[1]))
    if lip:
        # UV seams split a physical source vertex. Pin both copies as well as
        # the outer face attachment so animation cannot open an atlas seam.
        edges=Counter()
        for i in range(0,len(result['indices']),3):
            a,b,c=result['indices'][i:i+3]
            for u,v in ((a,b),(b,c),(c,a)):edges[tuple(sorted((u,v)))]+=1
        for edge,count in edges.items():
            if count==1:
                for vertex in edge:result['colors'][vertex*4+3]=0
    return result


skin_triangles, lip_triangles = [], []
for tri in low.loop_triangles:
    ps = [low.vertices[v].co for v in tri.vertices]
    if min(p.y for p in ps) < -.149: continue
    p = sum(ps, Vector())/3
    if p.z < -.090 and (p.x/.034)**2+((p.y-mouth_y)/.015)**2 < 1:
        lip_triangles.append(tri)
    else: skin_triangles.append(tri)
head = encode(low, skin_triangles, shut)
lips = encode(low, lip_triangles, lip=True)
if not lip_triangles: raise ValueError('Missing visible mouth')

eye_points = fit('eyes.mhclo')
_, eye_uv, eye_faces = obj_data(read('eyes.obj'))
eyeballs = mesh_object('Fitted eyes', [point(p) for p in eye_points], eye_uv, [f for _,f in eye_faces])
eyeballs.data.calc_loop_triangles()
eyes = []
for sign in (-1, 1):
    tris = [t for t in eyeballs.data.loop_triangles if
            sum(eyeballs.data.vertices[v].co.x for v in t.vertices)*sign > 0]
    eyes.append(encode(eyeballs.data, tris))

hair_name = 'ponytail01' if args.kind == 'feminine' else 'short02' if args.kind == 'neutral' else 'short01'
hair_folder = 'hair/'+hair_name+'/'
hair_points = fit(hair_folder+hair_name+'.mhclo')
_, hair_uv, hair_faces = obj_data(read(hair_folder+hair_name+'.obj'))
fitted_hair=[point(p) for p in hair_points]
hair_obj = mesh_object('Fitted hair cards', fitted_hair, hair_uv, [f for _,f in hair_faces])
hair_obj.data.calc_loop_triangles()
hair = encode(hair_obj.data, list(hair_obj.data.loop_triangles))

sys.path.insert(0, str(Path(__file__).parent))
from human_eyelids import eye_apertures
from human_head_lod import build_head_lod
apertures = eye_apertures(head, eyes)
lod = build_head_lod(head, lips, eyes)

# A source UV atlas is necessary for skin colour. No smart projection or
# per-feature pigment is allowed to replace the authored mapping.
file_name = {'masculine':'human-head','neutral':'human-scan','feminine':'human-feminine'}[args.kind]
skin_name = {'masculine':'young_caucasian_male/young_lightskinned_male_diffuse.png',
             'neutral':'young_caucasian_male2/young_lightskinned_male_diffuse2.png',
             'feminine':'young_caucasian_female/young_lightskinned_female_diffuse.png'}[args.kind]
albedo = out/(file_name+'_albedo.png')
shutil.copyfile(read('skins/'+skin_name), albedo)
shutil.copyfile(read('brown_eye.png'),out/'eye_albedo.png')
hair_texture = out/(hair_name+'_albedo.png')
shutil.copyfile(read(hair_folder+hair_name+'_diffuse.png'), hair_texture)
image=bpy.data.images.load(str(hair_texture),check_existing=False)
image.colorspace_settings.name='Non-Color'
rgba=np.empty(len(image.pixels),dtype=np.float32);image.pixels.foreach_get(rgba)
rgba=rgba.reshape((-1,4));rgb=rgba[rgba[:,3]>.5,:3]
hair_mean=float(np.mean(rgb@np.array([.2126,.7152,.0722])))
bpy.data.images.remove(image)

values = head['positions']
vertices = [Vector(values[i:i+3]) for i in range(0,len(values),3)]
indices = head['indices']
bvh = BVHTree.FromPolygons(vertices, [indices[i:i+3] for i in range(0,len(indices),3)], all_triangles=True)
rows, columns = 64, 128
low_y, high_y = -.038, max(p.y for p in vertices)
centers, radii = [], []
for row in range(rows):
    y = low_y+(high_y-low_y)*(row+.5)/rows
    band = sorted(vertices, key=lambda p: abs(p.y-y))[:30]
    center = (min(p.z for p in band)+max(p.z for p in band))/2
    centers.append(round(center,7))
    for col in range(columns):
        direction = Vector((math.cos(col*math.tau/columns),0,math.sin(col*math.tau/columns)))
        _,_,_,distance = bvh.ray_cast(Vector((0,y,center)), direction, .4)
        radii.append(round(max(.001,distance or .05),7))

lip_points = [Vector(lips['positions'][i:i+3]) for i in range(0,len(lips['positions']),3)]
mouth = sum(lip_points, Vector())/len(lip_points)
mouth.x = 0
actual_eye = sum((point(p) for p in eye_points if p.x>0),Vector())/sum(p.x>0 for p in eye_points)
actual_eye.x = abs(actual_eye.x)
nose = min(vertices, key=lambda p:p.z)
result = {'schemaVersion':2, 'source': {'creator':'MakeHuman Community', 'license':'CC0-1.0',
    'url':'https://github.com/makehumancommunity/makehuman', 'files':inputs,
    'configuration':{'macro':target.name,'weight':macro_weight,'subdivision':1},
    'sha256':hashlib.sha256(json.dumps(inputs,sort_keys=True).encode()+args.kind.encode()).hexdigest()},
    'head':head,'lips':lips,'eyes':eyes,'hair':hair,'eyeCenter':list(actual_eye),'mouthCenter':list(mouth),
    'mouthRegions':['source-uv-mouth'],'noseProbeY':nose.y,'eyelids':apertures,'headLod':lod,
    'scalp':{'rows':rows,'columns':columns,'low':low_y,'high':high_y,'centers':centers,'radii':radii},
    'albedo':{'file':albedo.name,'sha256':hashlib.sha256(albedo.read_bytes()).hexdigest(),'resolution':2048},
    'hairAlbedo':{'file':hair_texture.name,'sha256':hashlib.sha256(hair_texture.read_bytes()).hexdigest(),'luminance':round(hair_mean,7)},
    'eyeAlbedo':{'file':'eye_albedo.png','sha256':hashlib.sha256(read('brown_eye.png').read_bytes()).hexdigest()}}
path = out/(file_name+'.mesh.json')
path.write_text(json.dumps(result,separators=(',',':')),encoding='utf-8')
print('RESIDENT_FACE_EXPORTED', args.kind, len(head['positions'])//3, len(head['indices'])//3,
      'lip triangles',len(lips['indices'])//3,'hair triangles',len(hair['indices'])//3,
      hashlib.sha256(path.read_bytes()).hexdigest())
