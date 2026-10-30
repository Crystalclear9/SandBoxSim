"""Export CC0 anatomical shoulder surfaces using the source's authored weights.

The source A-pose is adducted into the client's downward-arm bind pose before
sampling. Unlike a convex section envelope, this retains the axillary hollow,
acromion slope and deltoid/upper-chest junction as an actual triangle surface.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
from export_upper_arm_support import add,sub,mul,dot,cross,unit


def subdivide(attributes,faces):
    """One Catmull-Clark step, interpolating source skin weights with position."""
    def average(values):return tuple(sum(v[j] for v in values)/len(values) for j in range(len(attributes[0])))
    face_points=[average([attributes[i] for i in f]) for f in faces]
    edges={};vertex_faces=[[] for _ in attributes];vertex_edges=[set() for _ in attributes]
    for fi,face in enumerate(faces):
        for a,b in zip(face,face[1:]+face[:1]):
            edge=tuple(sorted((a,b)));edges.setdefault(edge,[]).append(fi);vertex_edges[a].add(edge);vertex_edges[b].add(edge)
        for i in face:vertex_faces[i].append(fi)
    points=[]
    for i,p in enumerate(attributes):
        boundary=[e[1] if e[0]==i else e[0] for e in vertex_edges[i] if len(edges[e])==1]
        if len(boundary)==2:points.append(tuple((6*p[j]+attributes[boundary[0]][j]+attributes[boundary[1]][j])/8 for j in range(len(p))))
        elif not vertex_edges[i]:points.append(p)
        else:
            n=len(vertex_edges[i]);f=average([face_points[k] for k in vertex_faces[i]]);r=average([average([attributes[a],attributes[b]]) for a,b in vertex_edges[i]])
            points.append(tuple((f[j]+2*r[j]+(n-3)*p[j])/n for j in range(len(p))))
    edge_index={}
    for edge,neighbours in edges.items():
        a,b=edge;edge_index[edge]=len(points)
        points.append(average([attributes[a],attributes[b]]+[face_points[i] for i in neighbours]) if len(neighbours)==2 else average([attributes[a],attributes[b]]))
    result=[]
    for fi,face in enumerate(faces):
        center=len(points);points.append(face_points[fi])
        for j,a in enumerate(face):result.append((a,edge_index[tuple(sorted((a,face[(j+1)%len(face)])))],center,edge_index[tuple(sorted((face[j-1],a)))]))
    return points,result


def export(folder,sex):
    expected={'base.obj':'8e761e6624b8f54536409135d1636da63b32486a90d4897f84e121d144f6fb4c',
              'basemesh_vertex_groups.json':'8cb1417bc55ae5ec8aa99f90734e81ed5fbb556511ff6a2c00e0534b70911444',
              'caucasian-male-young.target':'70e228ba7164737dae664454394536fc5935fa48d333c1a97d77e2dc6eacc5f5',
              'rig/default.mhskel':'99f179bce0aa850b45d4191a1d0d234c5851f881c057439470ded3bddf729a24',
              'rig/default_weights.mhw':'0f3641d651ae3d00ad6b4ccee43142edb109d3bd909d27d9e4139ef1beed8625'}
    if sex=='female':expected['caucasian-female-young.target']='118379f6e8ba9266247fdb8788a20e1df40a239f97ced0b9905bcbcc74f6e820'
    for name,digest in expected.items():
        if hashlib.sha256((folder/name).read_bytes()).hexdigest()!=digest:raise ValueError('Changed source: '+name)
    rig=json.loads((folder/'rig/default.mhskel').read_text(encoding='utf-8'))
    weights=json.loads((folder/'rig/default_weights.mhw').read_text(encoding='utf-8'))
    if rig['license']!='CC0' or weights['license']!='CC0':raise ValueError('Unexpected source license')
    vertices=[];faces=[];quads=[];group=''
    for line in (folder/'base.obj').read_text(encoding='utf-8').splitlines():
        t=line.split()
        if not t:continue
        if t[0]=='v':vertices.append(tuple(map(float,t[1:4])))
        elif t[0]=='g':group=t[1]
        elif t[0]=='f' and group=='body':
            ids=[int(p.split('/')[0])-1 for p in t[1:]]
            quads.append(tuple(ids))
            faces.extend((ids[0],ids[i],ids[i+1]) for i in range(1,len(ids)-1))
    def macro(name):
        points=list(vertices)
        for line in (folder/name).read_text(encoding='utf-8').splitlines():
            t=line.split()
            if len(t)==4 and not line.startswith('#'):points[int(t[0])]=add(points[int(t[0])],tuple(map(float,t[1:])))
        return points
    groups=json.loads((folder/'basemesh_vertex_groups.json').read_text(encoding='utf-8'))
    def joint(points,name):
        ids=[i for a,b in groups['joint-l-'+name] for i in range(a,b+1)]
        return mul(tuple(sum(points[i][j] for i in ids) for j in range(3)),1/len(ids))
    reference=joint(macro('caucasian-male-young.target'),'shoulder')
    points=macro('caucasian-'+sex+'-young.target');origin=joint(points,'shoulder')
    shoulder_y=1.23-(.014 if sex=='female' else .009)
    def mapped(p):return (p[0]*.202/reference[0]*(1 if sex=='female' else 1.04),shoulder_y+(p[1]-origin[1])*.09,-(p[2]-origin[2])*.1)
    points=list(map(mapped,points));shoulder=mapped(origin);elbow=joint(points,'elbow')
    axis=unit(sub(elbow,shoulder));target=(0.,-1.,0.);normal=unit(cross(axis,target));angle=math.acos(max(-1,min(1,dot(axis,target))))
    influence=[0.]*len(points);arm=[0.]*len(points);helper=[0.]*len(points);clavicle=[0.]*len(points)
    for name,entries in weights['weights'].items():
        if not name.endswith('.L'):continue
        amount=.5 if name=='shoulder01.L' else 1. if name.startswith(('upperarm','lowerarm','wrist','finger','hand')) else 0.
        for index,weight in entries:influence[index]+=weight*amount
        for index,weight in entries:
            if name=='shoulder01.L':helper[index]+=weight
            elif name=='clavicle.L':clavicle[index]+=weight
            elif amount==1:arm[index]+=weight
    packed=[]
    for i,p in enumerate(points):
        total=max(1,arm[i]+helper[i]+clavicle[i]);packed.append((*p,max(0,1-arm[i]-helper[i]-clavicle[i])/total,arm[i]/total,helper[i]/total,clavicle[i]/total))
    packed,quads=subdivide(packed,quads)
    points=[p[:3] for p in packed];original=list(points);arm=[p[4] for p in packed];helper=[p[5] for p in packed];clavicle=[p[6] for p in packed]
    influence=[min(1,a+.5*h) for a,h in zip(arm,helper)]
    faces=[(f[0],f[i],f[i+1]) for f in quads for i in range(1,len(f)-1)]
    def rotated(p):
        local=sub(p,shoulder)
        return add(shoulder,add(add(mul(local,math.cos(angle)),mul(cross(normal,local),math.sin(angle))),mul(normal,dot(normal,local)*(1-math.cos(angle)))))
    points=[add(mul(p,1-min(1,w)),mul(rotated(p),min(1,w))) for p,w in zip(points,influence)]
    selected=[]
    for face in faces:
        center=mul(add(add(points[face[0]],points[face[1]]),points[face[2]]),1/3)
        if .045<center[0]<.36 and shoulder_y-.17<center[1]<shoulder_y+.115 and abs(center[2])<.20:selected.append(face)
    used=sorted({i for f in selected for i in f});mapping={i:n for n,i in enumerate(used)}
    if len(selected)<400:raise ValueError('Incomplete shoulder surface')
    return {'schemaVersion':2,'license':'CC0-1.0','source':'MakeHuman HM08 '+sex+' young; one Catmull-Clark level with authored weights; A-pose to hanging-arm bind',
            'sourceSha256':expected,'shoulder':shoulder,'armBind':[round(x*math.sin(angle/2),8) for x in normal]+[round(math.cos(angle/2),8)],
            'vertices':[[round(x,7) for x in points[i]] for i in used],
            'aPoseVertices':[[round(x,7) for x in original[i]] for i in used],
            'skinWeights':[[round(w/max(1,arm[i]+helper[i]+clavicle[i]),7) for w in (max(0,1-arm[i]-helper[i]-clavicle[i]),arm[i],helper[i],clavicle[i])] for i in used],
            'triangles':[[mapping[i] for i in f] for f in selected]}


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--source',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);args=parser.parse_args();args.output.mkdir(parents=True,exist_ok=True)
    for sex in ('male','female'):
        data=export(args.source,sex);path=args.output/('shoulder-surface-'+sex+'.json');path.write_text(json.dumps(data,separators=(',',':'))+'\n',encoding='utf-8',newline='\n')
        print('SHOULDER_SURFACE_EXPORTED',sex,len(data['vertices']),len(data['triangles']),hashlib.sha256(path.read_bytes()).hexdigest())
