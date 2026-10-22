"""Join the authored forearm and hand with a shared, skinned wrist transition.

Used inside Blender by export_human_hands.py. Keeps the original hand vertex
indices stable for nail partitions and for deriving the hand-only distant mesh.
"""
import hashlib
import json
import math
import bmesh
import bpy
from mathutils import Vector


def join_forearm(hand,path):
    raw=path.read_bytes()
    if hashlib.sha256(raw).hexdigest()!='de084f7d5717a636b4c1d5057ca07cc5941534b4b83ea9eb400059618cc77018':
        raise ValueError('Unexpected forearm source for wrist join')
    arm=json.loads(raw);p=arm['positions'];ids=arm['indices']
    mesh=bpy.data.meshes.new('forearm-wrist-transition')
    mesh.from_pydata([(p[i],p[i+1]+.255,p[i+2]+.010) for i in range(0,len(p),3)],[],
                     [(ids[i],ids[i+2],ids[i+1]) for i in range(0,len(ids),3)])
    bm=bmesh.new();bm.from_mesh(mesh)
    bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),dist=1e-7,
                          plane_co=(0,.055,0),plane_no=(0,1,0),clear_inner=True,clear_outer=False)
    bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS')
    bm.to_mesh(mesh);bm.free();mesh.calc_loop_triangles()
    count=len(hand['positions'])//3;hand['handVertexCount']=count
    points=[Vector(hand['positions'][i:i+3]) for i in range(0,len(hand['positions']),3)]
    hand_loop=[i for i,v in enumerate(points) if abs(v.y-.045)<1e-6]
    offset=len(points);points.extend(v.co.copy() for v in mesh.vertices)
    arm_loop=[offset+v.index for v in mesh.vertices if abs(v.co.y-.055)<1e-6]
    def angle(i):return math.atan2((points[i].z-.010)/.024,points[i].x/.027)%math.tau
    hand_loop.sort(key=angle);arm_loop.sort(key=angle)
    if len(hand_loop)<30 or len(arm_loop)<30:raise ValueError('Missing anatomical wrist loops')
    triangles=[(hand['indices'][i],hand['indices'][i+2],hand['indices'][i+1]) for i in range(0,len(hand['indices']),3)]
    triangles.extend(tuple(offset+i for i in t.vertices) for t in mesh.loop_triangles)
    # Zipper the two convex wrist loops in angular order. Shared indices produce
    # a single manifold surface, even though the two authored rings differ in size.
    a=b=0
    while a<len(arm_loop) or b<len(hand_loop):
        x=arm_loop[a%len(arm_loop)];y=hand_loop[b%len(hand_loop)]
        next_a=angle(arm_loop[(a+1)%len(arm_loop)])+(math.tau if a+1>=len(arm_loop) else 0)
        next_b=angle(hand_loop[(b+1)%len(hand_loop)])+(math.tau if b+1>=len(hand_loop) else 0)
        if a<len(arm_loop) and (b>=len(hand_loop) or next_a<next_b):
            z=arm_loop[(a+1)%len(arm_loop)];a+=1
        else:z=hand_loop[(b+1)%len(hand_loop)];b+=1
        outward=(points[x]+points[y]+points[z])/3;outward.y=0;outward.z-=.010
        if (points[y]-points[x]).cross(points[z]-points[x]).dot(outward)<0:y,z=z,y
        triangles.append((x,y,z))
    joined=bpy.data.meshes.new('continuous-arm-and-hand');joined.from_pydata(points,[],triangles);joined.update()
    hand['positions']=[];hand['normals']=[]
    for v in joined.vertices:
        hand['positions'].extend(round(x,7) for x in v.co)
        hand['normals'].extend(round(x,7) for x in v.normal.normalized())
        if v.index>=count:
            hand['uv'].extend((round(v.co.x*4+.5,7),round(v.co.y*4,7)))
            hand['bones'].extend((15,0,0,0));hand['weights'].extend((1.,0.,0.,0.))
    hand['indices']=[i for t in triangles for i in (t[0],t[2],t[1])]
    hand['forearmSha256']=hashlib.sha256(raw).hexdigest()
    return hand
