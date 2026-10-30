"""Sample the verified CC0 body's upper arm in its own shoulder/elbow frame.

Only the source envelope is exported; the client adds woven-cloth ease and
preserves its sewn armhole/cuff. Standard library only, no runtime dependency.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path


def add(a,b):return tuple(x+y for x,y in zip(a,b))
def sub(a,b):return tuple(x-y for x,y in zip(a,b))
def mul(a,t):return tuple(x*t for x in a)
def dot(a,b):return sum(x*y for x,y in zip(a,b))
def unit(a):return mul(a,1/math.sqrt(dot(a,a)))
def cross(a,b):return (a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
def turn(a,b):return a[0]*b[1]-a[1]*b[0]


def export(source,frame):
    expected={'base.obj':'8e761e6624b8f54536409135d1636da63b32486a90d4897f84e121d144f6fb4c',
              'basemesh_vertex_groups.json':'8cb1417bc55ae5ec8aa99f90734e81ed5fbb556511ff6a2c00e0534b70911444',
              'caucasian-male-young.target':'70e228ba7164737dae664454394536fc5935fa48d333c1a97d77e2dc6eacc5f5'}
    if frame=='female':expected['caucasian-female-young.target']='118379f6e8ba9266247fdb8788a20e1df40a239f97ced0b9905bcbcc74f6e820'
    for name,digest in expected.items():
        if hashlib.sha256((source/name).read_bytes()).hexdigest()!=digest:raise ValueError('Changed source: '+name)
    points=[];faces=[];group=''
    for line in (source/'base.obj').read_text(encoding='utf-8').splitlines():
        t=line.split()
        if not t:continue
        if t[0]=='v':points.append(tuple(map(float,t[1:4])))
        elif t[0]=='g':group=t[1]
        elif t[0]=='f' and group=='body':faces.append(tuple(int(v.split('/')[0])-1 for v in t[1:]))
    def macro(name):
        result=list(points)
        for line in (source/name).read_text(encoding='utf-8').splitlines():
            t=line.split()
            if len(t)==4 and not line.startswith('#'):result[int(t[0])]=add(result[int(t[0])],tuple(map(float,t[1:])))
        return result
    groups=json.loads((source/'basemesh_vertex_groups.json').read_text(encoding='utf-8'))
    def joint(vertices,name):
        ids=[i for a,b in groups['joint-l-'+name] for i in range(a,b+1)]
        return mul(tuple(sum(vertices[i][j] for i in ids) for j in range(3)),1/len(ids))
    reference=joint(macro('caucasian-male-young.target'),'shoulder')
    vertices=macro('caucasian-'+frame+'-young.target');raw_shoulder=joint(vertices,'shoulder')
    def mapped(p):return (p[0]*.202/reference[0],1.23+(p[1]-raw_shoulder[1])*.09,-(p[2]-raw_shoulder[2])*.1)
    vertices=[mapped(p) for p in vertices];shoulder=mapped(raw_shoulder);elbow=joint(vertices,'elbow')
    axis=unit(sub(elbow,shoulder));length=math.dist(elbow,shoulder)
    radial=unit(sub((1.,0.,0.),mul(axis,axis[0])));back=cross(axis,radial)
    local=[(dot(sub(p,shoulder),radial),dot(sub(p,shoulder),axis),dot(sub(p,shoulder),back)) for p in vertices]
    rows,columns,low,high=25,64,.025,.255;radii=[]
    for row in range(rows):
        section=[];along=(low+(high-low)*row/(rows-1))*length/.28
        for face in faces:
            for ia,ib in zip(face,face[1:]+face[:1]):
                a,b=local[ia],local[ib]
                if (a[1]-along)*(b[1]-along)>0 or abs(b[1]-a[1])<1e-9:continue
                q=add(a,mul(sub(b,a),(along-a[1])/(b[1]-a[1])))
                if math.hypot(q[0],q[2])<.105:section.append((q[0],q[2]))
        section=sorted(set(section))
        def half(values):
            hull=[]
            for p in values:
                while len(hull)>1 and turn(sub(hull[-1],hull[-2]),sub(p,hull[-1]))<=0:hull.pop()
                hull.append(p)
            return hull
        hull=half(section)[:-1]+half(reversed(section))[:-1]
        if len(hull)<8:raise ValueError('Incomplete arm section')
        for col in range(columns):
            direction=(math.cos(col*math.tau/columns),math.sin(col*math.tau/columns));hits=[]
            for a,b in zip(hull,hull[1:]+hull[:1]):
                edge=sub(b,a);den=turn(direction,edge)
                if abs(den)<1e-10:continue
                radius=turn(a,edge)/den;t=turn(a,direction)/den
                if radius>0 and -.000001<=t<=1.000001:hits.append(radius)
            if not hits or not .012<min(hits)<.105:raise ValueError('Invalid upper-arm envelope')
            radii.append(min(hits))
    for _ in range(3):
        old=radii
        radii=[sum(old[min(rows-1,max(0,r+dr))*columns+(c+dc)%columns]*w for dr,dc,w in ((0,0,.5),(-1,0,.15),(1,0,.15),(0,-1,.1),(0,1,.1))) for r in range(rows) for c in range(columns)]
    return {'schemaVersion':1,'license':'CC0-1.0','source':'MakeHuman HM08 '+frame+' young macro; existing verified source bundle',
            'sourceSha256':expected,'sourceShoulder':shoulder,'sourceElbow':elbow,'sourceLength':length,
            'targetLength':.28,'low':low,'high':high,'rows':rows,'columns':columns,'radii':[round(r,7) for r in radii]}


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--source',type=Path,required=True);parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args();args.output.mkdir(parents=True,exist_ok=True)
    for frame in ('male','female'):
        data=export(args.source,frame);path=args.output/f'upper-arm-{frame}.json'
        path.write_text(json.dumps(data,separators=(',',':'))+'\n',encoding='utf-8',newline='\n')
        print('UPPER_ARM_SUPPORT_EXPORTED',frame,min(data['radii']),max(data['radii']),hashlib.sha256(path.read_bytes()).hexdigest())
