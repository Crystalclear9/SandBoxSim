"""Convert attributed deer/wolf sources to the game's 14-joint skin and UV surfaces.

Blender --background --factory-startup --disable-autoexec --python this_file --
  --species deer --source .tools/sources/cdmir-deer/deer1.blend --output <animal resource folder>
Use --species wolf with the pinned 0 A.D. wolf.dae and adjacent wolf-grey.png.
"""
import argparse, hashlib, json, sys
from pathlib import Path
import bpy
from mathutils import Vector

parser = argparse.ArgumentParser()
parser.add_argument('--source', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--species', choices=('deer','wolf'), default='deer')
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
source_hash = hashlib.sha256(args.source.read_bytes()).hexdigest()
wolf = args.species == 'wolf'
expected = '481ae8021e7e2624e2c20db01d2b17b89eb488a9749eb92e1952f55e4496cf3a' if wolf else '729f47b10f29e13d128aa2e0a2a5ba47be40cb6593e1e86ac31e236e93bef003'
if source_hash != expected:
    raise ValueError('Unexpected animal source; inspect its licence and geometry first')
if wolf:
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    bpy.ops.wm.collada_import(filepath=str(args.source.resolve()))
else:
    bpy.ops.wm.open_mainfile(filepath=str(args.source.resolve()), load_ui=False, use_scripts=False)
armature = bpy.data.objects['Wolf' if wolf else 'Armature']
armature.data.pose_position = 'REST'
bpy.context.view_layer.update()
args.output.mkdir(parents=True, exist_ok=True)
scale, floor, centre = (.48, -.0026873939, .10) if wolf else (.30, -.0012936592, .15)

def point(p):
    return Vector((p.x * scale, (p.z - floor) * scale, (p.y - centre) * scale))

def rounded(p):
    return [round(float(v), 7) for v in p]

def joint(name):
    return point(armature.matrix_world @ armature.data.bones[name].head_local)

rest = [Vector(), joint('Head'), joint('Tail1') if wolf else Vector((0, 1.02, .62))]
soles = []
groups = {'Hip': 0, 'Backbone': 0, 'Backbone.001': 0, 'Backbone.002': 11, 'Head': 1,
          'Head_R.001': 12, 'Head_L.001': 13}
if wolf:
    groups={'Bone':0,'Bone.005':0,'Bone.001':0,'Bone.012':0,'Neck1':11,'Neck2':11,'Neck3':1,'Head':1,'Jaw1':1,'Jaw2':1,**{f'Tail{i}':2 for i in range(1,5)}}
for leg, name in enumerate(('Frontleg_R', 'Frontleg_L', 'Backleg_R', 'Backleg_L')):
    if wolf:
        suffix='R' if leg%2==0 else 'L'
        upper=f'FrontShoulder_{suffix}' if leg<2 else f'BackLeg1_{suffix}'
        knee=f'FrontLeg2_{suffix}' if leg<2 else f'BackLeg3_{suffix}'
        rest.extend((joint(upper),joint(knee)))
        labels=(f'FrontShoulder_{suffix}',f'FrontLeg1_{suffix}') if leg<2 else (f'BackLeg1_{suffix}',f'BackLeg2_{suffix}')
        for label in labels:groups[label]=3+leg*2
        labels=(f'FrontLeg2_{suffix}',f'FrontLeg3_{suffix}',f'FrontToe_{suffix}') if leg<2 else (f'BackLeg3_{suffix}',f'BackToe_{suffix}')
        for label in labels:groups[label]=4+leg*2
        sole=joint(f'FrontToe_{suffix}' if leg<2 else f'BackToe_{suffix}')
    else:
        rest.extend((joint(name), joint(name + '.001')))
        groups[name] = 3 + leg * 2
        groups[name + '.001'] = groups[name + '.002'] = 4 + leg * 2
        sole = point(armature.matrix_world @ armature.data.bones[name + '.002'].tail_local)
    sole.y = .007
    soles.append(rounded(sole))
rest.extend((joint('Neck1'),point(Vector((-.18,-1.18,2.06))),point(Vector((.18,-1.18,2.06)))) if wolf else (joint('Backbone.002'), joint('Head_R.001'), joint('Head_L.001')))

surfaces = {}
for name in (('Wolf_mesh',) if wolf else ('Body', 'Head', 'Eyes', 'Horns')):
    obj = bpy.data.objects[name]
    # Subdivision preserves the artist's silhouette and original UV layout.
    smooth = obj.modifiers.new('Runtime silhouette', 'SUBSURF')
    smooth.levels = smooth.render_levels = 2 if wolf else 1
    evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh = evaluated.to_mesh(preserve_all_data_layers=True, depsgraph=bpy.context.evaluated_depsgraph_get())
    mesh.calc_loop_triangles()
    normal_matrix = obj.matrix_world.to_3x3().inverted().transposed()
    for tri in mesh.loop_triangles:
        material = 'Wolf' if wolf else obj.data.materials[tri.material_index].name
        surface = surfaces.setdefault(material, dict(material=material, positions=[], normals=[], uv=[], bones=[], weights=[], indices=[], lookup={}))
        # (x,z,y) reflects handedness, already producing Godot clockwise fronts.
        for loop_id in tri.loops:
            loop = mesh.loops[loop_id]
            vertex = mesh.vertices[loop.vertex_index]
            p = point(obj.matrix_world @ vertex.co)
            n = (normal_matrix @ mesh.corner_normals[loop_id].vector).normalized()
            n = Vector((n.x, n.z, n.y))
            uv = mesh.uv_layers.active.data[loop_id].uv
            influence = {}
            for entry in vertex.groups:
                label = obj.vertex_groups[entry.group].name
                if label in groups:
                    bone = groups[label]
                    influence[bone] = influence.get(bone, 0) + entry.weight
            if wolf:
                world=obj.matrix_world@vertex.co
                if world.z>2.06 and -1.55<world.y<-1.0 and abs(world.x)>.10:
                    ear=min(1,(world.z-2.06)/.08)
                    influence={b:w*(1-ear) for b,w in influence.items()}
                    influence[12 if world.x<0 else 13]=ear
            chosen = sorted(influence.items(), key=lambda pair: (-pair[1], pair[0]))[:4] or [(0, 1)]
            total = sum(w for _, w in chosen)
            bones = [b for b, _ in chosen] + [0] * (4 - len(chosen))
            weights = [round(w / total, 7) for _, w in chosen] + [0] * (4 - len(chosen))
            values = tuple(rounded(p) + rounded(n) + [round(uv.x, 7), round(1-uv.y, 7)] + bones + weights)
            if values not in surface['lookup']:
                surface['lookup'][values] = len(surface['positions']) // 3
                surface['positions'].extend(values[:3]); surface['normals'].extend(values[3:6]); surface['uv'].extend(values[6:8])
                surface['bones'].extend(bones); surface['weights'].extend(weights)
            surface['indices'].append(surface['lookup'][values])
    evaluated.to_mesh_clear()
    obj.modifiers.remove(smooth)
for surface in surfaces.values():
    surface.pop('lookup')
textures = {}
for image_name, filename in (() if wolf else (('body','deer-body.png'), ('head','deer-head.png'), ('deer-antlers.png','deer-antlers.png'),
                             ('deer-body-normal.png','deer-body-normal.png'), ('deer-head-normal.png','deer-head-normal.png'), ('deer-antlers-normal.png','deer-antlers-normal.png'))):
    image = bpy.data.images[image_name]
    if not image.packed_file:
        raise ValueError('Expected embedded source texture')
    raw = bytes(image.packed_file.data)
    (args.output / filename).write_bytes(raw)
    textures[filename] = {'sha256': hashlib.sha256(raw).hexdigest(), 'bytes': len(raw)}
if wolf:
    raw=(args.source.parent/'wolf-grey.png').read_bytes()
    if hashlib.sha256(raw).hexdigest()!='cca1ea8133ae1b51f7e2767b4c68eb7d5cd1c46668774b3e0ce72fb1c989bbb1':raise ValueError('Unexpected wolf texture')
    (args.output/'wolf-grey.png').write_bytes(raw);textures['wolf-grey.png']={'sha256':hashlib.sha256(raw).hexdigest(),'bytes':len(raw)}
data = dict(schemaVersion=1, source={'author':'Wildfire Games' if wolf else 'CDmir; collaborator TinyWorlds', 'license':'CC-BY-SA-3.0' if wolf else 'CC0-1.0',
    'url':'https://github.com/0ad/0ad/tree/61a3b9507d974084e6badb88a0826bd89a6d5b8b/binaries/data/mods/public/art' if wolf else 'https://opengameart.org/content/old-deer-male', 'sha256':source_hash},
    joints=[rounded(p) for p in rest], soles=soles, surfaces=list(surfaces.values()), textures=textures)
output = args.output / ('wolf-authored.mesh.json' if wolf else 'deer-authored.mesh.json')
output.write_text(json.dumps(data, separators=(',',':')), encoding='utf-8')
print('AUTHORED_ANIMAL_EXPORTED',args.species, sum(len(s['indices'])//3 for s in surfaces.values()), hashlib.sha256(output.read_bytes()).hexdigest())
