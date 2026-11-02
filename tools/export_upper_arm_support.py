"""Sample the verified CC0 body's upper arm in its own shoulder/elbow frame.

Only the source envelope is exported; the client adds woven-cloth ease and
preserves its sewn armhole/cuff. Standard library only, no runtime dependency.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
from resident_body import body_points, body_provenance, body_metric_scale


def add(a,b):return tuple(x+y for x,y in zip(a,b))
def sub(a,b):return tuple(x-y for x,y in zip(a,b))
def mul(a,t):return tuple(x*t for x in a)
def dot(a,b):return sum(x*y for x,y in zip(a,b))
def unit(a):return mul(a,1/math.sqrt(dot(a,a)))
def cross(a,b):return (a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
def turn(a,b):return a[0]*b[1]-a[1]*b[0]


def export(source,frame,region='upper-arm'):
    expected={'base.obj':'8e761e6624b8f54536409135d1636da63b32486a90d4897f84e121d144f6fb4c',
              'basemesh_vertex_groups.json':'8cb1417bc55ae5ec8aa99f90734e81ed5fbb556511ff6a2c00e0534b70911444',
              'caucasian-male-young.target':'70e228ba7164737dae664454394536fc5935fa48d333c1a97d77e2dc6eacc5f5'}
    if frame=='female':expected['caucasian-female-young.target']='118379f6e8ba9266247fdb8788a20e1df40a239f97ced0b9905bcbcc74f6e820'
    for name,digest in expected.items():
        if hashlib.sha256((source/name).read_bytes()).hexdigest()!=digest:raise ValueError('Changed source: '+name)
    faces=[];group=''
    for line in (source/'base.obj').read_text(encoding='utf-8').splitlines():
        t=line.split()
        if not t:continue
        if t[0]=='g':group=t[1]
        elif t[0]=='f' and group=='body':faces.append(tuple(int(v.split('/')[0])-1 for v in t[1:]))
    groups=json.loads((source/'basemesh_vertex_groups.json').read_text(encoding='utf-8'))
    def joint(vertices,name):
        ids=[i for a,b in groups['joint-l-'+name] for i in range(a,b+1)]
        return mul(tuple(sum(vertices[i][j] for i in ids) for j in range(3)),1/len(ids))
    vertices=body_points(source,frame);raw_shoulder=joint(vertices,'shoulder')
    scale=body_metric_scale(source)
    def mapped(p):return (p[0]*scale,1.23+(p[1]-raw_shoulder[1])*scale,-(p[2]-raw_shoulder[2])*scale)
    vertices=[mapped(p) for p in vertices];shoulder=mapped(raw_shoulder);elbow=joint(vertices,'elbow')
    wrist=joint(vertices,'hand') if region=='forearm' else None
    origin,end=(elbow,wrist) if region=='forearm' else (shoulder,elbow)
    axis=unit(sub(end,origin));length=math.dist(end,origin)
    radial=unit(sub((1.,0.,0.),mul(axis,axis[0])));back=cross(axis,radial)
    local=[(dot(sub(p,origin),radial),dot(sub(p,origin),axis),dot(sub(p,origin),back)) for p in vertices]
    rows,columns,low,high=(35,64,.075,.245) if region=='forearm' else (25,64,.025,.255)
    target_length=.255 if region=='forearm' else .28
    radii=[]
    for row in range(rows):
        height=low+(high-low)*row/(rows-1)
        section=[];along=((target_length-height) if region=='forearm' else height)*length/target_length
        for face in faces:
            for ia,ib in zip(face,face[1:]+face[:1]):
                a,b=local[ia],local[ib]
                if (a[1]-along)*(b[1]-along)>0 or abs(b[1]-a[1])<1e-9:continue
                q=add(a,mul(sub(b,a),(along-a[1])/(b[1]-a[1])))
                if math.hypot(q[0],q[2])<(.075 if region=='forearm' else .105):section.append((q[0],q[2]))
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
    provenance=body_provenance(frame)
    expected=provenance.pop('sourceSha256')
    result={'schemaVersion':1,'license':'CC0-1.0','source':'MakeHuman HM08 '+frame+' young macro and source body recipe',
            'sourceSha256':expected,'shapeRecipe':provenance,'sourceShoulder':shoulder,'sourceElbow':elbow,'sourceLength':length,
            'targetLength':target_length,'low':low,'high':high,'rows':rows,'columns':columns,'radii':[round(r,7) for r in radii]}
    if region=='forearm':
        reference_path=Path(__file__).resolve().parents[1]/'src/SandBoxSim.Godot/assets/models/human/human-forearm.mesh.json'
        raw=reference_path.read_bytes();reference=json.loads(raw)
        points=[(x,y+.255,z+.010) for x,y,z in zip(*[iter(reference['positions'])]*3)]
        widths=[];depths=[];reference_widths=[];reference_depths=[];anatomical_widths=[];anatomical_depths=[]
        def smooth5(t):
            t=min(1.,max(0.,t));return t*t*t*(10+t*(-15+6*t))
        for row in range(rows):
            height=low+(high-low)*row/(rows-1);section=[]
            for tri in zip(*[iter(reference['indices'])]*3):
                for a,b in zip(tri,tri[1:]+tri[:1]):
                    p,q=points[a],points[b]
                    if (p[1]-height)*(q[1]-height)>0 or abs(q[1]-p[1])<1e-9:continue
                    t=(height-p[1])/(q[1]-p[1]);section.append((p[0]+(q[0]-p[0])*t,p[2]+(q[2]-p[2])*t))
            if not section:raise ValueError('Missing sculpted forearm reference section')
            old_weight=smooth5((height-.075)/.070)*(1-smooth5((height-.255)/.020))
            wrist_weight=smooth5((height-.018)/.020)*(1-smooth5((height-.070)/.080))
            reference_widths.append((max(p[0] for p in section)-min(p[0] for p in section))*(1-(.18 if frame=='female' else .10)*old_weight))
            reference_depths.append((max(p[1] for p in section)-min(p[1] for p in section))*(1-(1-.0185/.024)*wrist_weight)*(1-(.26 if frame=='female' else .18)*old_weight))
            ring=radii[row*columns:(row+1)*columns]
            x=[r*math.cos(c*math.tau/columns) for c,r in enumerate(ring)]
            z=[r*math.sin(c*math.tau/columns) for c,r in enumerate(ring)]
            anatomical_widths.append(max(x)-min(x));anatomical_depths.append(max(z)-min(z))
            # A boundary-constrained transfer: keep the shared 54x37 mm wrist
            # and never enlarge the existing sculpt. The 3 mm width reserve
            # prevents a source wrist of different size creating a thin notch.
            widths.append(min(reference_widths[-1],max(.057,anatomical_widths[-1])))
            depths.append(min(reference_depths[-1],max(.040,anatomical_depths[-1])))
        result.update(license='CC0-1.0 AND CC-BY-4.0',sourceWrist=wrist,
            referenceMeshSha256=hashlib.sha256(raw).hexdigest(),
            anatomicalWidths=[round(v,7) for v in anatomical_widths],anatomicalDepths=[round(v,7) for v in anatomical_depths],
            wristWidth=.054,wristDepth=.037,minimumForearmWidth=.057,
            targetWidths=[round(v,7) for v in widths],targetDepths=[round(v,7) for v in depths],
            referenceWidths=[round(v,7) for v in reference_widths],referenceDepths=[round(v,7) for v in reference_depths],
            scaleX=[round(a/b,7) for a,b in zip(widths,reference_widths)],scaleZ=[round(a/b,7) for a,b in zip(depths,reference_depths)])
    return result


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--source',type=Path,required=True);parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--region',choices=('upper-arm','forearm'),default='upper-arm')
    args=parser.parse_args();args.output.mkdir(parents=True,exist_ok=True)
    for frame in ('male','female'):
        data=export(args.source,frame,args.region);prefix='forearm-fit' if args.region=='forearm' else 'upper-arm';path=args.output/f'{prefix}-{frame}.json'
        path.write_text(json.dumps(data,separators=(',',':'))+'\n',encoding='utf-8',newline='\n')
        print('FOREARM_FIT_EXPORTED' if args.region=='forearm' else 'UPPER_ARM_SUPPORT_EXPORTED',frame,min(data['radii']),max(data['radii']),hashlib.sha256(path.read_bytes()).hexdigest())
