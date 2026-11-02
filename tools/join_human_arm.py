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
                          plane_co=(0,.075,0),plane_no=(0,1,0),clear_inner=True,clear_outer=False)
    bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS')
    bm.to_mesh(mesh);bm.free();mesh.calc_loop_triangles()
    count=len(hand['positions'])//3;hand['handVertexCount']=count
    points=[Vector(hand['positions'][i:i+3]) for i in range(0,len(hand['positions']),3)]
    hand_loop=[i for i,v in enumerate(points) if abs(v.y-.045)<1e-6]
    offset=len(points);points.extend(v.co.copy() for v in mesh.vertices)
    arm_loop=[offset+v.index for v in mesh.vertices if abs(v.co.y-.075)<1e-6]
    def angle(i):return math.atan2((points[i].z-.010)/.024,points[i].x/.027)%math.tau
    hand_loop.sort(key=angle);arm_loop.sort(key=angle)
    if len(hand_loop)<30 or len(arm_loop)<30:raise ValueError('Missing anatomical wrist loops')
    triangles=[(hand['indices'][i],hand['indices'][i+2],hand['indices'][i+1]) for i in range(0,len(hand['indices']),3)]
    triangles.extend(tuple(offset+i for i in t.vertices) for t in mesh.loop_triangles)
    neighbours={i:set() for i in hand_loop+arm_loop}
    for tri in triangles:
        for i in tri:
            if i in neighbours:neighbours[i].update(j for j in tri if j!=i)
    def tangent(i):
        delta=[points[j]-points[i] for j in neighbours[i] if abs(points[j].y-points[i].y)>1e-6]
        denominator=sum(v.y*v.y for v in delta)
        slope=sum((v*v.y for v in delta),Vector())/denominator if denominator>1e-12 else Vector((0,1,0))
        return Vector((max(-1.5,min(1.5,slope.x)),1,max(-1.5,min(1.5,slope.z))))
    tangents={i:tangent(i) for i in neighbours}
    def sample(loop,a,field):
        values=[angle(i) for i in loop]
        import bisect
        j=bisect.bisect_right(values,a);low=loop[(j-1)%len(loop)];high=loop[j%len(loop)]
        x=values[(j-1)%len(loop)];y=values[j%len(loop)]
        if j==0:x-=math.tau
        if j==len(loop):y+=math.tau
        return field[low].lerp(field[high],(a-x)/(y-x))
    # A 30 mm tangent-guided loft replaces the single 10 mm zipper band.
    # Original palm/finger indices and both shared end loops remain intact.
    slopes=[[sample(loop,math.tau*c/96,tangents) for c in range(96)] for loop in (hand_loop,arm_loop)]
    # The bisected source has irregular, very short edges. Raw one-ring
    # derivative estimates alternate around those edges and corrugate a loft.
    # Filter in uniform angular coordinates, leaving both end loops exact.
    for _ in range(16):
        slopes=[[row[c]*.5+row[(c-1)%96]*.25+row[(c+1)%96]*.25 for c in range(96)] for row in slopes]
    rings=[hand_loop]
    for row in range(1,8):
        t=row/8;ring=[]
        for col in range(96):
            a=math.tau*col/96;p=sample(hand_loop,a,points);q=sample(arm_loop,a,points)
            m=slopes[0][col]*.030;n=slopes[1][col]*.030
            v=p*(2*t**3-3*t*t+1)+m*(t**3-2*t*t+t)+q*(-2*t**3+3*t*t)+n*(t**3-t*t)
            ring.append(len(points));points.append(v)
        ring.sort(key=angle)
        rings.append(ring)
    rings.append(arm_loop)
    for lower,upper in zip(rings,rings[1:]):
        a=b=0
        while a<len(upper) or b<len(lower):
            x=upper[a%len(upper)];y=lower[b%len(lower)]
            next_a=angle(upper[(a+1)%len(upper)])+(math.tau if a+1>=len(upper) else 0)
            next_b=angle(lower[(b+1)%len(lower)])+(math.tau if b+1>=len(lower) else 0)
            if a<len(upper) and (b>=len(lower) or next_a<next_b):
                z=upper[(a+1)%len(upper)];a+=1
            else:z=lower[(b+1)%len(lower)];b+=1
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
    hand['wristTransition']={'profile':'tangent-loft-v2','low':.045,'high':.075,'intermediateRings':7,'columns':96}
    return hand
