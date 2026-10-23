"""Simplify the actual anatomical head, lips and eyes for distant residents."""
import bpy


def build_head_lod(head,lips,eyes,budget=2400):
    points,faces,colors=[],[],[]
    for surface,tint in ((head,(1,.99,.98,1)),(lips,(1,.80,.77,1)),*( (eye,(.32,.30,.25,1)) for eye in eyes)):
        offset=len(points);values=surface['positions']
        points.extend([values[i:i+3] for i in range(0,len(values),3)])
        colors.extend([tint]*(len(values)//3))
        indices=surface['indices']
        faces.extend([(offset+indices[i],offset+indices[i+2],offset+indices[i+1]) for i in range(0,len(indices),3)])
    mesh=bpy.data.meshes.new('Anatomical head LOD');mesh.from_pydata(points,[],faces);mesh.update()
    color=mesh.color_attributes.new(name='HeadTint',type='FLOAT_COLOR',domain='POINT')
    for value,tint in zip(color.data,colors):value.color=tint
    for face in mesh.polygons:face.use_smooth=True
    obj=bpy.data.objects.new('Anatomical head LOD',mesh);bpy.context.collection.objects.link(obj)
    modifier=obj.modifiers.new('Preserve anatomical silhouette','DECIMATE');modifier.ratio=budget/len(faces);modifier.use_collapse_triangulate=True
    bpy.context.view_layer.update();deps=bpy.context.evaluated_depsgraph_get();result_mesh=obj.evaluated_get(deps).to_mesh(preserve_all_data_layers=True,depsgraph=deps)
    result_mesh.calc_loop_triangles()
    tint_data=result_mesh.color_attributes['HeadTint'].data
    result={k:[] for k in ('positions','normals','uv','colors','indices')}
    for i,vertex in enumerate(result_mesh.vertices):
        result['positions'].extend(round(v,7) for v in vertex.co)
        result['normals'].extend(round(v,7) for v in vertex.normal)
        result['colors'].extend(round(v,7) for v in tint_data[i].color)
        result['uv'].extend((0,0))
    for tri in result_mesh.loop_triangles:result['indices'].extend((tri.vertices[0],tri.vertices[2],tri.vertices[1]))
    if len(result['indices'])//3>budget:raise ValueError('Head LOD triangle budget exceeded')
    return result
