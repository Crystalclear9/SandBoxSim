"""Export continuous CC0 MakeHuman tights helpers as clothed resident trousers.

Run in project-local Blender, with -- --source DIRECTORY --output DIRECTORY.
The original head and body assets are never modified.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import sys
import bpy
import bmesh
from mathutils import Vector

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--source',required=True)
parser.add_argument('--output',required=True)
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
root,out=Path(args.source),Path(args.output)
out.mkdir(parents=True,exist_ok=True)
manifest=json.loads((Path(__file__).resolve().parents[1]/'src/SandBoxSim.Godot/assets/models/human/head-sources.json').read_text())['files']
def read(name):
    raw=(root/name).read_bytes()
    if hashlib.sha256(raw).hexdigest()!=manifest[name]['sha256']:raise ValueError('Changed source '+name)
    return raw.decode('utf-8')
base=[];faces=[];group=''
for line in read('base.obj').splitlines():
    t=line.split()
    if not t:continue
    if t[0]=='v':base.append(Vector(map(float,t[1:4])))
    elif t[0]=='g':group=t[1]
    elif t[0]=='f' and group=='helper-tights':faces.append(tuple(int(c.split('/')[0])-1 for c in t[1:]))
groups=json.loads(read('basemesh_vertex_groups.json'))
def body(sex):
    points=[v.copy() for v in base]
    for line in read(f'caucasian-{sex}-young.target').splitlines():
        t=line.split()
        if len(t)==4 and not line.startswith('#'):points[int(t[0])]+=Vector(map(float,t[1:]))
    return points
def joint(points,name):
    ids=[i for a,b in groups[name] for i in range(a,b+1)]
    return sum((points[i] for i in ids),Vector())/len(ids)
reference=joint(body('male'),'joint-l-shoulder').x
for sex in ('male','female'):
    points=body(sex);hip=joint(points,'joint-l-upper-leg');knee=joint(points,'joint-l-knee')
    half=.122 if sex=='female' else .100
    used=sorted({i for face in faces for i in face});lookup={v:i for i,v in enumerate(used)}
    support_path=Path(__file__).resolve().parents[1]/('src/SandBoxSim.Godot/assets/models/human/trunk-'+('feminine' if sex=='female' else 'masculine')+'-cage.json')
    support=json.loads(support_path.read_text());radii=support['radii']
    def radius(y,angle):
        row=min(112,max(0,(y-.74)/.005));col=(angle%math.tau)/math.tau*128
        a=min(111,int(row));c=int(col)%128;u=col-int(col);v=row-a
        def at(r):return radii[r*128+c]*(1-u)+radii[r*128+(c+1)%128]*u
        return at(a)*(1-v)+at(a+1)*v
    mapped=[]
    for i in used:
        p=points[i];t=min(1.,max(0.,(hip.y-p.y)/(hip.y-knee.y)))
        centre=hip.x*(1-t)+knee.x*t
        x=p.x*.202/reference
        # Remove source A-pose leg splay, preserving the radial thigh form.
        influence=min(1.,abs(x)/.055);influence=influence*influence*(3-2*influence)
        x+=(1 if x>=0 else -1)*(half-centre*.202/reference)*influence
        y=.80+(p.y-hip.y)*.38/(hip.y-knee.y)
        z=-(p.z-(hip.z*(1-t)+knee.z*t))*.1
        r=math.hypot(x,z);blend=min(1,max(0,(y-.70)/.07));blend=blend*blend*(3-2*blend)
        if r>1e-8:
            fit=min(1,max(.04,radius(y,math.atan2(z,x))-.010)/r)
            factor=1+(fit-1)*blend;x*=factor;z*=factor
        # Bury open knee cuffs inside the existing boot rim.
        cuff=1-min(1,max(0,(y-.40)/.09));cuff=cuff*cuff*(3-2*cuff)
        centre_x=(1 if x>=0 else -1)*half
        length=math.hypot((x-centre_x)/.045,z/.041)
        if length>1e-8:
            x=x*(1-cuff)+(centre_x+(x-centre_x)/length)*cuff
            z=z*(1-cuff)+(z/length)*cuff
        # Keep the CC0 topology and anatomical surface, but restore a continuous
        # iliac/gluteal envelope instead of a pair of similarly thick thigh tubes.
        # These are clothing/art parameters, not population measurements.
        upper=math.exp(-((y-.63)/.105)**2)
        centre_x=(1 if x>=0 else -1)*half
        x=centre_x+(x-centre_x)*(1-(.11 if sex=='female' else .025)*upper)
        if z<0:z*=1-(.105 if sex=='female' else .015)*upper
        if sex=='female':
            pelvis=math.exp(-((y-.775)/.105)**2)
            x+=.034*math.tanh(x/.065)*pelvis
            back=min(1.,max(0.,z/.055));back=back*back*(3-2*back)
            z+=.033*back*pelvis
        else:
            back=min(1.,max(0.,z/.055));back=back*back*(3-2*back)
            z+=.005*back*math.exp(-((y-.775)/.105)**2)
            front=min(1.,max(0.,-z/.06));front=front*front*(3-2*front)
            z-=.004*front*upper*(1-cuff)
        mapped.append((x,y,z))
    mesh=bpy.data.meshes.new('trousers');mesh.from_pydata(mapped,[],[tuple(lookup[i] for i in f) for f in faces]);mesh.update()
    bm=bmesh.new();bm.from_mesh(mesh)
    for y,normal in ((.94,(0,1,0)),(.385,(0,-1,0))):
        bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),dist=1e-7,
            plane_co=(0,y,0),plane_no=normal,clear_outer=True,clear_inner=False)
    bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS')
    # The helper also includes upper-body sleeves. After plane cropping,
    # retain only the connected lower garment, never detached arm fragments.
    pending=set(bm.verts);components=[]
    while pending:
        stack=[pending.pop()];component=set(stack)
        while stack:
            for edge in stack.pop().link_edges:
                for v in edge.verts:
                    if v in pending:pending.remove(v);component.add(v);stack.append(v)
        components.append(component)
    main=max(components,key=len)
    bmesh.ops.delete(bm,geom=[v for v in bm.verts if v not in main],context='VERTS')
    bm.to_mesh(mesh);bm.free()
    obj=bpy.data.objects.new('clothed-trousers',mesh);bpy.context.collection.objects.link(obj)
    modifier=obj.modifiers.new('Cloth surface','SUBSURF');modifier.levels=1
    dg=bpy.context.evaluated_depsgraph_get();evaluated=obj.evaluated_get(dg)
    result=bpy.data.meshes.new_from_object(evaluated)
    bm=bmesh.new();bm.from_mesh(result);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(result);bm.free()
    result.update();result.calc_loop_triangles()
    data=dict(schemaVersion=2,source='MakeHuman CC0 HM08 helper-tights',frame=sex,hipHalfWidth=half,jointCount=5,cuffSkin='knee-transition-v1',physique='adult-proportions-v13',
        sourceSha256={name:manifest[name]['sha256'] for name in ('base.obj','basemesh_vertex_groups.json',f'caucasian-{sex}-young.target','caucasian-male-young.target')},
        supportSha256=hashlib.sha256(support_path.read_bytes().replace(b'\r\n',b'\n')).hexdigest(),positions=[],normals=[],uv=[],bones=[],weights=[],indices=[])
    for v in result.vertices:
        p=v.co;n=v.normal.normalized()
        data['positions']+= [round(x,7) for x in p];data['normals']+= [round(x,7) for x in n]
        data['uv']+= [round(p.x*3+.5,7),round(p.y*3,7)]
        t=min(1.,max(0.,(.84-p.y)/.18));t=t*t*(3-2*t)
        side=min(1.,max(0.,(p.x+.035)/.070));side=side*side*(3-2*side)
        knee=1-min(1.,max(0.,(p.y-.40)/.13));knee=knee*knee*(3-2*knee)
        if knee>0:
            data['bones']+=[1,2,3,4]
            data['weights']+=[round((1-knee)*(1-side),7),round((1-knee)*side,7),round(knee*(1-side),7),round(knee*side,7)]
        else:
            data['bones']+=[0,1,2,0];data['weights']+=[round(1-t,7),round(t*(1-side),7),round(t*side,7),0]
    for tri in result.loop_triangles:data['indices']+= [tri.vertices[0],tri.vertices[2],tri.vertices[1]]
    if not data['indices'] or len(data['indices'])//3>10000:raise ValueError('Trouser budget exceeded')
    target=out/f'trousers-{sex}.mesh.json'
    target.write_text(json.dumps(data,separators=(',',':'))+'\n',encoding='utf-8',newline='\n')
    print('TROUSERS_EXPORTED',sex,len(result.vertices),len(data['indices'])//3,hashlib.sha256(target.read_bytes()).hexdigest())
    bpy.data.objects.remove(obj,do_unlink=True)
