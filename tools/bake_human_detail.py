"""Bake the attributed level-two sculpt onto the runtime head's UV atlas.

Called by export_human_head.py inside Blender; no external assets or add-ons.
"""
import hashlib
import math

import bpy


def bake_head_detail(low, high, path, resolution=2048):
    def object_from_surface(name, surface):
        points = surface['positions']
        indices = surface['indices']
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata([points[i:i+3] for i in range(0, len(points), 3)], [],
                         [(indices[i], indices[i+2], indices[i+1]) for i in range(0, len(indices), 3)])
        mesh.update()
        for polygon in mesh.polygons:
            polygon.use_smooth = True
        normals = surface['normals']
        mesh.normals_split_custom_set_from_vertices([normals[i:i+3] for i in range(0, len(normals), 3)])
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
        return obj

    for obj in bpy.data.objects:
        obj.hide_render = True
    target = object_from_surface('Runtime head', low)
    sculpt = object_from_surface('Attributed detail sculpt', high)
    target.hide_render = sculpt.hide_render = False
    bpy.ops.object.select_all(action='DESELECT')
    target.select_set(True)
    bpy.context.view_layer.objects.active = target
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=.008, area_weight=1.0)
    bpy.ops.object.mode_set(mode='OBJECT')

    image = bpy.data.images.new('Human sculpt normal', width=resolution, height=resolution, alpha=False)
    image.colorspace_settings.name = 'Non-Color'
    material = bpy.data.materials.new('Normal bake target')
    material.use_nodes = True
    node = material.node_tree.nodes.new('ShaderNodeTexImage')
    node.image = image
    material.node_tree.nodes.active = node
    target.data.materials.clear()
    target.data.materials.append(material)
    sculpt.select_set(True)
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = 1
    scene.render.bake.use_selected_to_active = True
    scene.render.bake.cage_extrusion = .004
    scene.render.bake.max_ray_distance = .012
    scene.render.bake.margin = 12
    scene.render.bake.normal_space = 'TANGENT'
    # Game UVs below flip V to image coordinates; keep the tangent bitangent
    # consistent with the normal atlas rather than reversing surface relief.
    scene.render.bake.normal_g = 'NEG_Y'
    bpy.ops.object.bake(type='NORMAL')
    image.filepath_raw = str(path)
    image.file_format = 'PNG'
    image.save()

    # Preserve the sculpt's authored normals and regional color, splitting
    # vertices only at real UV seams in the packed runtime atlas.
    result = {key: [] for key in ('positions', 'normals', 'uv', 'colors', 'indices')}
    lookup = {}
    target.data.calc_loop_triangles()
    coords = target.data.uv_layers.active.data
    for triangle in target.data.loop_triangles:
        face = []
        for loop_index in triangle.loops:
            vertex = target.data.loops[loop_index].vertex_index
            uv = coords[loop_index].uv
            key = vertex, round(uv.x, 7), round(uv.y, 7)
            if key not in lookup:
                lookup[key] = len(result['positions']) // 3
                for field, stride in (('positions', 3), ('normals', 3), ('colors', 4)):
                    result[field].extend(low[field][vertex*stride:(vertex+1)*stride])
                result['uv'].extend((round(uv.x, 7), round(1-uv.y, 7)))
            face.append(lookup[key])
        result['indices'].extend((face[0], face[2], face[1]))
    return result, hashlib.sha256(path.read_bytes()).hexdigest()
