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
from mathutils.bvhtree import BVHTree
sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parent))
from resident_clothing import trouser_fold
from resident_body import body_points, body_provenance, body_metric_scale

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
base=[];faces=[];skin_faces=[];group=''
for line in read('base.obj').splitlines():
    t=line.split()
    if not t:continue
    if t[0]=='v':base.append(Vector(map(float,t[1:4])))
    elif t[0]=='g':group=t[1]
    elif t[0]=='f' and group=='helper-tights':faces.append(tuple(int(c.split('/')[0])-1 for c in t[1:]))
    elif t[0]=='f' and group=='body':skin_faces.append(tuple(int(c.split('/')[0])-1 for c in t[1:]))
groups=json.loads(read('basemesh_vertex_groups.json'))
def body(sex):
    return [Vector(p) for p in body_points(root,sex)]
def joint(points,name):
    ids=[i for a,b in groups[name] for i in range(a,b+1)]
    return sum((points[i] for i in ids),Vector())/len(ids)
metric_scale=body_metric_scale(root)
for sex in ('male','female'):
    points=body(sex);hip=joint(points,'joint-l-upper-leg');knee=joint(points,'joint-l-knee')
    anatomy=BVHTree.FromPolygons(points,skin_faces,all_triangles=False)
    anatomical_samples=0
    half=.100 # Shared neutral stance; source pelvis volume remains independent.
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
        x=p.x*metric_scale
        # Remove source A-pose leg splay, preserving the radial thigh form.
        influence=min(1.,abs(x)/.055);influence=influence*influence*(3-2*influence)
        x+=(1 if x>=0 else -1)*(half-centre*metric_scale)*influence
        y=.80+(p.y-hip.y)*.38/(hip.y-knee.y)
        z=-(p.z-(hip.z*(1-t)+knee.z*t))*metric_scale
        def smooth(a,b,value):
            u=min(1.,max(0.,(value-a)/(b-a)));return u*u*(3-2*u)
        anatomical_weight=smooth(.43,.49,y)*(1-smooth(.69,.77,y))*smooth(.03,.065,abs(x))
        if anatomical_weight>0:
            side=1 if p.x>=0 else -1
            origin=Vector((side*centre,p.y,hip.z*(1-t)+knee.z*t))
            direction=Vector((p.x-origin.x,0,p.z-origin.z))
            if direction.length<1e-8:raise ValueError('Missing anatomical thigh direction')
            hit,normal,index,distance=anatomy.ray_cast(origin,direction.normalized(),4.)
            if hit is None:raise ValueError('Anatomical thigh section is open')
            radial=Vector(((hit.x-origin.x)*metric_scale,0,-(hit.z-origin.z)*metric_scale))
            if not .025<radial.length<.20:raise ValueError(f'Anatomical thigh radius escaped its source region: {sex}/{i}, radius={radial.length}')
            radial+=radial.normalized()*.006
            fitted=Vector((side*half+radial.x,y,radial.z))
            x=x*(1-anatomical_weight)+fitted.x*anatomical_weight
            z=z*(1-anatomical_weight)+fitted.z*anatomical_weight
            anatomical_samples+=1
        r=math.hypot(x,z);blend=min(1,max(0,(y-.70)/.07));blend=blend*blend*(3-2*blend)
        if r>1e-8:
            fit=min(1.30,max(.04,radius(y,math.atan2(z,x))+.007)/r)
            factor=1+(fit-1)*blend;x*=factor;z*=factor
        # Bury open knee cuffs inside the existing boot rim.
        cuff=1-min(1,max(0,(y-.40)/.09));cuff=cuff*cuff*(3-2*cuff)
        centre_x=(1 if x>=0 else -1)*half
        length=math.hypot((x-centre_x)/.045,z/.041)
        if length>1e-8:
            x=x*(1-cuff)+(centre_x+(x-centre_x)/length)*cuff
            z=z*(1-cuff)+(z/length)*cuff
        # Dress the source thigh with a small cloth clearance. Keep the
        # inseam and buried boot opening clear; the source pelvic/gluteal
        # form needs no additional sex-dependent volume displacement.
        centre_x=(1 if x>=0 else -1)*half
        dx=x-centre_x;radial=math.hypot(dx,z)
        edge=min(1.,max(0.,(abs(x)-.035)/.045));edge=edge*edge*(3-2*edge)
        lower=1-min(1.,max(0.,(y-.71)/.12));lower=lower*lower*(3-2*lower)
        clearance=.007*edge*lower*(1-cuff)*(1-anatomical_weight)
        if radial>1e-8:
            # Compression folds terminate near the hip/boot supports, rather
            # than adding a periodic ripple to the whole leg.
            front=max(0.,-z/radial)**2
            hip_fold=.0025*math.exp(-math.pow((y-(.735-.35*abs(dx)))/.013,2))
            knee_fold=.0020*math.exp(-math.pow((y-(.475+.18*dx))/.012,2))
            fold=front*(hip_fold-knee_fold)*(1-cuff)
            x+=dx/radial*(clearance+fold);z+=z/radial*(clearance+fold)
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
    # Resolve folds on the subdivided cloth, so narrow support creases have
    # enough samples without changing the rig, topology or triangle budget.
    for vertex in result.vertices:
        p=vertex.co;centre_x=(1 if p.x>=0 else -1)*half
        radial=Vector((p.x-centre_x,0,p.z))
        if radial.length>1e-9:p+=radial.normalized()*trouser_fold(p.x,p.y,p.z,half)
    bm=bmesh.new();bm.from_mesh(result)
    # Relax the tiny folded medial join without welding opposite crotch
    # sheets into a non-manifold edge. Preserve every index and boundary.
    medial=[v for v in bm.verts if abs(v.co.x)<.004 and .70<v.co.y<.74]
    for _ in range(6):
        bmesh.ops.smooth_vert(bm,verts=medial,factor=.5,use_axis_x=True,use_axis_y=True,use_axis_z=True)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(result);bm.free()
    result.update();result.calc_loop_triangles()
    for triangle in result.loop_triangles:
        a,b,c=(result.vertices[i] for i in triangle.vertices)
        cross=(b.co-a.co).cross(c.co-a.co)
        if cross.length_squared<=1e-14 or cross.dot(a.normal+b.normal+c.normal)<=0:
            raise ValueError('Folded or degenerate trouser triangle')
    skin_support=[]
    for row in range(21):
        height=.49+row*.01;t=(.80-height)/.38
        origin=hip.lerp(knee,t)
        for col in range(64):
            angle=col*math.tau/64
            direction=Vector((math.cos(angle),0,-math.sin(angle)))
            hit,normal,index,distance=anatomy.ray_cast(origin,direction,4.)
            if hit is None:raise ValueError('Incomplete anatomical leg reference')
            skin_support.append(round(math.hypot((hit.x-origin.x)*metric_scale,(hit.z-origin.z)*metric_scale),7))
    provenance=body_provenance(sex)
    source_hashes=provenance.pop('sourceSha256')
    data=dict(schemaVersion=2,source='MakeHuman CC0 HM08 helper topology and anatomical body sections',frame=sex,hipHalfWidth=half,jointCount=5,cuffSkin='knee-transition-v1',physique='metric-physique-v20',shapeRecipe=provenance,tailoring=dict(thighEaseMetres=.006,maximumFoldMetres=.0085,pelvis='source-envelope',bootOpening='pinned',detail='sewn-support-folds-v1',legSupport='source-body-rays-v1',anatomicalSamples=anatomical_samples),
        sourceSha256=source_hashes,
        supportSha256=hashlib.sha256(support_path.read_bytes().replace(b'\r\n',b'\n')).hexdigest(),anatomicalSupport=dict(low=.49,step=.01,rows=21,columns=64,radii=skin_support),positions=[],normals=[],uv=[],bones=[],weights=[],indices=[])
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
    if any(abs(v.co.x)>.26 or abs(v.co.z)>.20 or not .3849<=v.co.y<=.9401 for v in result.vertices):raise ValueError('Trouser deformation escaped the metric body envelope')
    for tri in result.loop_triangles:data['indices']+= [tri.vertices[0],tri.vertices[2],tri.vertices[1]]
    if not data['indices'] or len(data['indices'])//3>10000:raise ValueError('Trouser budget exceeded')
    target=out/f'trousers-{sex}.mesh.json'
    target.write_text(json.dumps(data,separators=(',',':'))+'\n',encoding='utf-8',newline='\n')
    print('TROUSERS_EXPORTED',sex,len(result.vertices),len(data['indices'])//3,hashlib.sha256(target.read_bytes()).hexdigest())
    bpy.data.objects.remove(obj,do_unlink=True)
