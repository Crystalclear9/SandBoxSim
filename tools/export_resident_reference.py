"""Retarget verified CC0 Blender body surfaces onto the HM08 source topology.

Run in the project-local Blender. Produces reproducible source-shape deltas;
the game continues to use baked cloth/support meshes, not a Blender runtime.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys

import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parent))
from resident_body import body_points,body_metric_scale,read_source,limb_weights

ROOT=Path(__file__).resolve().parents[1]
BUNDLE_SHA='3c121505651140ceb4d69fd1d8923f7788ffadd81672f5be14845a5f2c75c137'
BUNDLE_URL='https://download.blender.org/demo/asset-bundles/human-base-meshes/human-base-meshes-bundle-v1.4.1.zip'


def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()


def export(source,bundle,out):
    if digest(bundle)!=BUNDLE_SHA:raise ValueError('Changed official CC0 body bundle')
    groups=json.loads(read_source(source,'basemesh_vertex_groups.json'))
    faces=[];group=''
    for line in read_source(source,'base.obj').splitlines():
        f=line.split()
        if not f:continue
        if f[0]=='g':group=f[1]
        if f[0]=='f' and group=='body':faces.append(tuple(int(t.split('/')[0])-1 for t in f[1:]))
    used=sorted({v for face in faces for v in face})
    arm_weights=limb_weights(source,('upperarm01.L','upperarm01.R'))
    torso_faces=[f for f in faces if all(arm_weights.get(i,0)<.65 for i in f)]
    adjacency={v:set() for v in used};edges=set()
    for face in faces:
        for a,b in zip(face,face[1:]+face[:1]):
            adjacency[a].add(b);adjacency[b].add(a);edges.add((min(a,b),max(a,b)))
    with bpy.data.libraries.load(str(bundle.resolve()),link=False) as (src,dst):
        dst.objects=['GEO-body_male_realistic','GEO-body_female_realistic']
    scale=body_metric_scale(source)
    manifest=dict(schemaVersion=1,license='CC0-1.0',author='Dan Ulrich / Blender Studio and community contributors',
        source=dict(url=BUNDLE_URL,archiveBytes=50643039,member='human-base-meshes-bundle-v1.4.1/human_base_meshes_bundle.blend',
                    memberBytes=49420489,memberCrc32=3540975974,sha256=BUNDLE_SHA),files={})
    for sex,ob in zip(('male','female'),dst.objects):
        mesh=ob.data;regions={}
        for face in mesh.polygons:
            label=mesh.attributes['.sculpt_face_set'].data[face.index].value
            regions.setdefault(label,set()).update(face.vertices)
        def boundary(a,b):
            ids=sorted(regions[a]&regions[b])
            if len(ids)<12:raise ValueError('Missing source anatomical region boundary')
            return np.mean([mesh.vertices[i].co[:] for i in ids],axis=0)
        # Region boundaries are retargeting guides, not medical joint markers.
        pairs={'shoulder':(1,21),'elbow':(21,12),'hand':(12,9),'upper-leg':(18,24),'knee':(24,15),'ankle':(15,14)}
        landmarks={key:boundary(*pair) for key,pair in pairs.items()}
        neck=boundary(1,17)
        s=landmarks['shoulder'];h=landmarks['upper-leg']
        reference_scale=.43/(s[2]-h[2])
        def desired(p):
            # Retarget the source's leaning shoulder/hip axis to the neutral
            # game rig. A constant shoulder depth would leave the pelvis
            # ahead of its hip joint and thin the posterior cage artificially.
            t=min(1.,max(0.,float((p[2]-h[2])/(s[2]-h[2]))))
            centre=h[1]*(1-t)+s[1]*t
            return np.array((p[0]*reference_scale,1.23+(p[2]-s[2])*reference_scale,(p[1]-centre)*reference_scale))
        target=np.array([desired(v.co) for v in mesh.vertices])
        target_faces=[tuple(f.vertices) for f in mesh.polygons]
        target_torso_faces=[tuple(f.vertices) for f in mesh.polygons if mesh.attributes['.sculpt_face_set'].data[f.index].value in (1,18,19,23,24)]
        target_bvh=BVHTree.FromPolygons([Vector(p) for p in target],target_faces,all_triangles=False)
        baseline=np.array(body_points(source,sex,baseline=True),dtype=float)
        def ids(name):return [i for a,b in groups[name] for i in range(a,b+1)]
        def joint(name):return baseline[ids(name)].mean(axis=0)
        raw_shoulder=joint('joint-l-shoulder')
        points=np.stack((baseline[:,0]*scale,1.23+(baseline[:,1]-raw_shoulder[1])*scale,-(baseline[:,2]-raw_shoulder[2])*scale),axis=1)
        control=[];goals=[];joint_goals={}
        for key,value in landmarks.items():
            for side,sign in [('l',1),('r',-1)]:
                name=f'joint-{side}-{key}';q=desired(value);q[0]=abs(q[0])*sign
                control.append(points[ids(name)].mean(axis=0));goals.append(q);joint_goals[name]=q
        for name,value in [('joint-neck',desired(neck))]:
            value[0]=0;control.append(points[ids(name)].mean(axis=0));goals.append(value);joint_goals[name]=value
        # Central controls prevent a narrowed shoulder cage from bowing the
        # spine or moving the pelvis when arm pose directions differ.
        sh=points[ids('joint-l-shoulder')].mean(axis=0);hp=points[ids('joint-l-upper-leg')].mean(axis=0)
        for t in (0,.35,.7,1):
            p=sh*(1-t)+hp*t;p[0]=0
            q=desired(s*(1-t)+h*t);q[0]=0
            control.append(p);goals.append(q)
        original_bvh=BVHTree.FromPolygons([Vector(p) for p in points],faces,all_triangles=False)
        surface_anchors=[]
        # Joint guides lie near a coronal sheet. Surface guides constrain the
        # remaining depth dimension and transfer the actual chest/waist form,
        # rather than allowing the affine tail to flatten the abdomen.
        def section_hull(vertices,polygons,height):
            points=[]
            for face in polygons:
                for i,j in zip(face,face[1:]+face[:1]):
                    a,b=vertices[i],vertices[j]
                    if (a[1]-height)*(b[1]-height)>0 or abs(b[1]-a[1])<1e-10:continue
                    p=a+(b-a)*((height-a[1])/(b[1]-a[1]));points.append((float(p[0]),float(p[2])))
            points=sorted(set(points))
            def cross(a,b,c):return (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
            def half(values):
                hull=[]
                for p in values:
                    while len(hull)>1 and cross(hull[-2],hull[-1],p)<=0:hull.pop()
                    hull.append(p)
                return hull
            hull=half(points)[:-1]+half(reversed(points))[:-1]
            def ray(direction):
                d=(direction[0],direction[2]);hits=[]
                for a,b in zip(hull,hull[1:]+hull[:1]):
                    e=(b[0]-a[0],b[1]-a[1]);den=d[0]*e[1]-d[1]*e[0]
                    if abs(den)<1e-10:continue
                    r=(a[0]*e[1]-a[1]*e[0])/den;t=(a[0]*d[1]-a[1]*d[0])/den
                    if r>0 and -.000001<=t<=1.000001:hits.append(r)
                if not hits:raise ValueError('Open source body contour guide')
                return np.array((d[0]*min(hits),height,d[1]*min(hits)))
            return ray
        for height in (.78,.82,.90,1.00,1.10,1.20):
            source_section=section_hull(points,torso_faces,height)
            reference_section=section_hull(target,target_torso_faces,height)
            for direction in (Vector((1,0,0)),Vector((-1,0,0)),Vector((0,0,1)),Vector((0,0,-1))):
                a=source_section(direction);b=reference_section(direction)
                control.append(a);goals.append(b)
                surface_anchors.append(dict(height=height,direction=list(direction),reference=[round(v,7) for v in b]))
        control=np.array(control);goals=np.array(goals)
        distances=np.linalg.norm(control[:,None,:]-control[None,:,:],axis=2)
        affine=np.column_stack((np.ones(len(control)),control))
        matrix=np.block([[distances+np.eye(len(control))*1e-8,affine],[affine.T,np.zeros((4,4))]])
        condition=float(np.linalg.cond(matrix))
        if not np.isfinite(condition) or condition>1e9:raise ValueError('Ill-conditioned body transfer cage')
        coefficients=np.linalg.solve(matrix,np.vstack((goals-control,np.zeros((4,3)))))
        def warp(p):
            distances=np.linalg.norm(p[:,None,:]-control[None,:,:],axis=2)
            return p+distances@coefficients[:len(control)]+np.column_stack((np.ones(len(p)),p))@coefficients[len(control):]
        fitted=warp(points)
        offsets=np.zeros_like(points)
        for i in used:
            hit,normal,face,distance=target_bvh.find_nearest(Vector(fitted[i]))
            if hit is None:raise ValueError('Missing target body surface')
            d=np.array(hit)-fitted[i];length=np.linalg.norm(d)
            if length>.045:d*=.045/length
            offsets[i]=d*.9
        # Fair the transferred displacement, preserving the existing topology.
        for _ in range(12):
            old=offsets.copy()
            for i in used:offsets[i]=old[i]*.65+np.mean(old[list(adjacency[i])],axis=0)*.35
        for _ in range(32):
            for a,b in edges:
                difference=offsets[a]-offsets[b];length=np.linalg.norm(difference)
                limit=np.linalg.norm(fitted[a]-fitted[b])*.40
                if length>limit and length>1e-10:
                    correction=difference*((length-limit)/length*.5);offsets[a]-=correction;offsets[b]+=correction
        body=fitted+offsets
        used_set=set(used)
        # Clothing helpers inherit the body displacement at their original
        # surface correspondence, keeping their own sewn offsets and indices.
        for i,p in enumerate(points):
            if i in used_set:continue
            hit,normal,face,distance=original_bvh.find_nearest(Vector(p))
            if hit is None or distance>.12:continue
            candidates=faces[face]
            weights=np.array([1/max(1e-6,np.linalg.norm(p-points[j]))**2 for j in candidates]);weights/=weights.sum()
            body[i]=p+sum((body[j]-points[j])*w for j,w in zip(candidates,weights))
        for name,q in joint_goals.items():
            indices=ids(name)
            if used_set.intersection(indices):raise ValueError('Joint guide unexpectedly overlaps visible skin')
            body[indices]+=q-body[indices].mean(axis=0)
        if not np.isfinite(body).all():raise ValueError('Nonfinite transferred anatomy')
        raw=np.stack((body[:,0]/scale,raw_shoulder[1]+(body[:,1]-1.23)/scale,raw_shoulder[2]-body[:,2]/scale),axis=1)
        deltas=np.round(raw-baseline,7)
        geometry_sha=hashlib.sha256(json.dumps([[list(v.co) for v in mesh.vertices],target_faces],separators=(',',':')).encode()).hexdigest()
        data=dict(schemaVersion=1,license='CC0-1.0',sourceBundleSha256=BUNDLE_SHA,sourceCollection=f'Body {sex.title()} - Realistic',
            topologyInputSha256={name:hashlib.sha256((source/name).read_bytes()).hexdigest() for name in ('base.obj','basemesh_vertex_groups.json',f'caucasian-{sex}-young.target','rig/default_weights.mhw','rig/default.mhskel')},
            sourceGeometrySha256=geometry_sha,sourceScale=round(reference_scale,9),sourceLandmarks={k:[round(float(v),7) for v in p] for k,p in landmarks.items()},
            targetJoints={k:[round(float(v),7) for v in p] for k,p in joint_goals.items()},
            topology='MakeHuman HM08',retargetPose='neutral-shoulder-pelvis-axis-v1',bodyVertices=len(used),controls=len(control),projectionLimitMetres=.045,displacementGradient=.40,
            surfaceAnchors=surface_anchors,cageCondition=round(condition,5),
            deltas=deltas.flatten().tolist())
        path=out/f'reference-shape-{sex}.json'
        path.write_text(json.dumps(data,separators=(',',':'))+'\n',encoding='utf-8',newline='\n')
        manifest['files'][path.name]=dict(bytes=path.stat().st_size,sha256=digest(path),frame=sex)
        print('REFERENCE_SHAPE_EXPORTED',sex,len(baseline),len(used),'vertices;',len(control),'controls;',digest(path),flush=True)
    (out/'reference-shape-sources.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8',newline='\n')


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source',type=Path,required=True)
    parser.add_argument('--bundle',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    a=parser.parse_args(sys.argv[sys.argv.index('--')+1:]);a.output.mkdir(parents=True,exist_ok=True)
    export(a.source,a.bundle,a.output)
