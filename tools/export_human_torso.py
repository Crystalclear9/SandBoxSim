"""Sample a cloth support cage from the verified CC0 MakeHuman body in Blender.

The cage supplies chest, scapular and cervical cross sections, not muscle
normal-map decoration. Garment seams and cloth ease are fitted by the client.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import sys
from mathutils import Vector
from mathutils.bvhtree import BVHTree

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--source',required=True)
parser.add_argument('--output',required=True)
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
root=Path(args.source)
expected={'base.obj':'8e761e6624b8f54536409135d1636da63b32486a90d4897f84e121d144f6fb4c',
          'caucasian-male-young.target':'70e228ba7164737dae664454394536fc5935fa48d333c1a97d77e2dc6eacc5f5',
          'basemesh_vertex_groups.json':'8cb1417bc55ae5ec8aa99f90734e81ed5fbb556511ff6a2c00e0534b70911444'}
for name,digest in expected.items():
    if hashlib.sha256((root/name).read_bytes()).hexdigest()!=digest:raise ValueError('Changed source: '+name)
points=[];faces=[];group=''
for line in (root/'base.obj').read_text(encoding='utf-8').splitlines():
    t=line.split()
    if not t:continue
    if t[0]=='v':points.append(Vector(map(float,t[1:4])))
    elif t[0]=='g':group=t[1]
    elif t[0]=='f' and group=='body':faces.append(tuple(int(c.split('/')[0])-1 for c in t[1:]))
for line in (root/'caucasian-male-young.target').read_text().splitlines():
    t=line.split()
    if len(t)==4 and not line.startswith('#'):points[int(t[0])]+=Vector(map(float,t[1:]))
groups=json.loads((root/'basemesh_vertex_groups.json').read_text())
ids=[i for a,b in groups['joint-l-shoulder'] for i in range(a,b+1)]
shoulder=sum((points[i] for i in ids),Vector())/len(ids)
mapped=[Vector((p.x*.202/shoulder.x,1.23+(p.y-shoulder.y)*.09,-(p.z-shoulder.z)*.1)) for p in points]
bvh=BVHTree.FromPolygons(mapped,faces,all_triangles=False)
low,high,rows,columns=1.02,1.34,65,128
radii=[]
for row in range(rows):
    y=low+(high-low)*row/(rows-1)
    for col in range(columns):
        angle=col*math.tau/columns;direction=Vector((math.cos(angle),0,math.sin(angle)))
        hit,normal,index,distance=bvh.ray_cast(Vector((0,y,0)),direction,.4)
        if hit is None or not .025<distance<.34:raise ValueError(f'Invalid torso ray {row}/{col}: {distance}')
        # Cut off the A-pose upper arm at the medial sleeve seam. Otherwise a
        # horizontal ray above the axilla follows the arm all the way outward.
        distance=min(distance,.185/max(abs(direction.x),.001))
        radii.append(distance)
# Woven cloth bridges the sternum/pectoral hollows and neck-to-deltoid
# concavities. Sample each section's convex envelope rather than imprinting
# every muscle depression (or an A-pose ray's branch change) onto the shirt.
def cross(a,b):return a[0]*b[1]-a[1]*b[0]
def sub(a,b):return (a[0]-b[0],a[1]-b[1])
for row in range(rows):
    section=sorted((radii[row*columns+c]*math.cos(c*math.tau/columns),radii[row*columns+c]*math.sin(c*math.tau/columns)) for c in range(columns))
    def half(values):
        hull=[]
        for p in values:
            while len(hull)>1 and cross(sub(hull[-1],hull[-2]),sub(p,hull[-1]))<=0:hull.pop()
            hull.append(p)
        return hull
    hull=half(section)[:-1]+half(reversed(section))[:-1]
    for col in range(columns):
        direction=(math.cos(col*math.tau/columns),math.sin(col*math.tau/columns))
        hits=[]
        for a,b in zip(hull,hull[1:]+hull[:1]):
            edge=sub(b,a);den=cross(direction,edge)
            if abs(den)<1e-10:continue
            distance=cross(a,edge)/den;along=cross(a,direction)/den
            if distance>0 and -.000001<=along<=1.000001:hits.append(distance)
        if not hits:raise ValueError('Open cloth support envelope')
        radii[row*columns+col]=min(hits)
for _ in range(4):
    old=radii
    radii=[sum(old[min(rows-1,max(0,r+dr))*columns+(c+dc)%columns]*weight for dr,dc,weight in ((0,0,.4),(-1,0,.15),(1,0,.15),(0,-1,.15),(0,1,.15))) for r in range(rows) for c in range(columns)]
radii=[round(v,7) for v in radii]
result={'schemaVersion':1,'source':'MakeHuman CC0 body; male young macro, existing source bundle',
        'sourceSha256':expected,'low':low,'high':high,'rows':rows,'columns':columns,
        'shoulder':[.202,1.23,0],'armholeHalfWidth':.185,'sourceShoulder':[round(v,7) for v in shoulder],
        'scale':[round(.202/shoulder.x,7),.09,.1],'clothEnvelope':'convex-sections-four-pass-filter','radii':radii}
output=Path(args.output);output.parent.mkdir(parents=True,exist_ok=True)
output.write_text(json.dumps(result,separators=(',',':'))+'\n',encoding='utf-8',newline='\n')
print('TORSO_CAGE_EXPORTED',len(faces),'source faces;',len(radii),'radial samples;',hashlib.sha256(output.read_bytes()).hexdigest())
