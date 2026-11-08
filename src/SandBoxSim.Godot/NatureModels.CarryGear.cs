using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;
using MeshArrays = Godot.Collections.Array;
using MeshShapes = Godot.Collections.Array<Godot.Collections.Array>;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private readonly record struct StrapContact(Vector3 Point,Vector3 Outward,int Triangle,Vector3 Weights,bool Shoulder);
    private const float StrapStandOff=.001f,StrapThickness=.0028f;

    private void AddFemaleCarryGear(ResidentRig rig,JobType job,FemaleBuild build)
    {
        if(rig.PackMount==null&&job!=JobType.Trader)return;
        var cloth=rig.DetailedBodyMesh.SurfaceGetArrays(0);
        var points=cloth[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var indices=cloth[(int)Mesh.ArrayType.Index].AsInt32Array();
        if(rig.PackMount is {} pack)
        {
            // Keep the rigid pack in the chest frame. Both shoulder loops
            // terminate at that same pack rather than across the front belt.
            pack.Position=new Vector3(0,1.114f,pack.Position.Z);
            var shell=pack.GetChild<MeshInstance3D>(0);
            float back=float.NegativeInfinity;
            foreach(var vertex in shell.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
            {
                var local=shell.Transform*vertex;
                float garment=GarmentDepth(points,indices,local.X,pack.Position.Y+local.Y,true);
                if(float.IsFinite(garment))back=MathF.Max(back,garment-local.Z);
            }
            if(!float.IsFinite(back))throw new InvalidOperationException("Gatherer's pack misses the fitted back panel");
            pack.Position=new(0,pack.Position.Y,back+.004f);
            var harness=rig.PackHarness=new MeshInstance3D {Name="FittedPackHarness",Mesh=FemalePackHarness(pack.Position,(ArrayMesh)rig.DetailedBodyMesh,cloth,points,indices,build),
                Skin=rig.DetailedBodySkin,Skeleton=new NodePath("../ShoulderSkeleton")};
            rig.Torso.AddChild(harness);
        }
        else
        {
            const float x=-.053f,y=.952f;
            float belt=GarmentDepth(points,indices,x,1.010f,true);
            float panel=GarmentDepth(points,indices,x,y,true);
            if(!float.IsFinite(belt)||!float.IsFinite(panel))throw new InvalidOperationException("Trader's pouch misses the fitted waist");
            var pouch=rig.SatchelMount=new Node3D {Name="BeltPouch",Position=new(x,y,MathF.Max(panel,belt)+.025f),Rotation=new(0,MathF.PI,0)};
            rig.Torso.AddChild(pouch);
            var leather=new Node3D {Name="LeatherCase",Scale=new Vector3(.71f,.62f,.53f)};
            pouch.AddChild(leather);
            Sculpt(leather,SatchelBody(),Vector3.Zero,LeatherSurface("#6e503b"),new(-MathF.PI/2,0,0));
            Sculpt(leather,SatchelFlap(),Vector3.Zero,LeatherSurface("#806048"));
            Detail(leather,"sphere",new(0,.030f,SatchelDepth(0,.030f)-.005f),new(.010f,.010f,.008f),"#9d8254");
            foreach(float side in new[]{-1f,1f})
            {
                // Two folded tabs run from the back of the pouch across the
                // physical belt and turn down inside it.
                float offset=side*.024f;
                float clothDepth=pouch.Position.Z-GarmentDepth(points,indices,x-offset,1.010f,true);
                float leatherDepth=clothDepth-.006f;
                FineBeam(pouch,new(offset,.035f,.015f),new(offset,.069f,leatherDepth),.010f,"#5e4636");
                FineBeam(pouch,new(offset,.069f,leatherDepth),new(offset,.069f,clothDepth+.001f),.010f,"#5e4636");
                FineBeam(pouch,new(offset,.069f,clothDepth+.001f),new(offset,.050f,clothDepth+.001f),.010f,"#5e4636");
            }
        }
    }

    private Mesh FemalePackHarness(Vector3 pack,ArrayMesh garment,MeshArrays cloth,Vector3[] points,int[] indices,FemaleBuild build)
    {
        string key="female-pack-harness:"+_garmentCut+":"+build;
        if(_meshes.TryGetValue(key,out var mesh))return mesh;
        var vertices=new List<Vector3>();
        var contacts=new List<StrapContact>();
        var triangles=new List<int>();
        var uv=new List<Vector2>();
        var pattern=cloth[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
        var panelIndices=new List<int>();
        var panelTriangles=new List<int>();
        var yokeIndices=new List<int>();
        var yokeTriangles=new List<int>();
        for(int t=0;t<indices.Length;t+=3)
        {
            float sleeve=MathF.Max(pattern[indices[t]].X,MathF.Max(pattern[indices[t+1]].X,pattern[indices[t+2]].X));
            if(sleeve<=.055f)
            {
                yokeTriangles.Add(t);
                yokeIndices.Add(indices[t]);yokeIndices.Add(indices[t+1]);yokeIndices.Add(indices[t+2]);
            }
            if(sleeve>=0)continue;
            panelTriangles.Add(t);
            panelIndices.Add(indices[t]);panelIndices.Add(indices[t+1]);panelIndices.Add(indices[t+2]);
        }
        // Side rays must pass through the sleeve and find the torso panel
        // beneath it. The sleeve never provides a harness attachment.
        var panel=panelIndices.ToArray();
        var yoke=yokeIndices.ToArray();
        var rotated=new Vector3[points.Length];
        StrapContact Contact(Vector3 origin,Vector3 normal,bool shoulder)
        {
            var across=Vector3.Up.Cross(normal);
            if(across.LengthSquared()<.001f)across=Vector3.Right;
            across=across.Normalized();
            var tangent=normal.Cross(across);
            for(int i=0;i<points.Length;i++)
            {
                var p=points[i]-origin;
                rotated[i]=new(p.Dot(across),p.Dot(tangent),p.Dot(normal));
            }
            float depth=GarmentDepth(rotated,shoulder?yoke:panel,0,0,true,out int triangle,out var weights);
            if(!float.IsFinite(depth)||depth<=0)throw new InvalidOperationException($"Shoulder strap misses the fitted garment at {origin}/{normal}");
            return new(origin+normal*depth,normal,(shoulder?yokeTriangles:panelTriangles)[triangle/3],weights,shoulder);
        }
        foreach(float side in new[]{-1f,1f})
        {
            var path=new List<StrapContact>();
            void Add(Vector3 origin,Vector3 outward,bool shoulder=false)=>path.Add(Contact(origin,outward,shoulder));
            float x=side*.077f;
            path.Add(new(pack+new Vector3(x,.086f,-.023f),Vector3.Back,-1,Vector3.Zero,false));
            for(int i=0;i<=24;i++)
            {
                float angle=i*MathF.PI/24;
                Add(new(side*(.077f+.010f*MathF.Sin(angle)),1.181f,0),new(0,MathF.Sin(angle),MathF.Cos(angle)),true);
            }
            for(int i=1;i<=20;i++)
            {
                float t=i/20f;
                Add(new(side*Mathf.Lerp(.077f,.089f,t),Mathf.Lerp(1.181f,1.062f,t),0),Vector3.Forward);
            }
            // Round the transition from the descending front strap to its
            // side return. A flat horizontal turn creates a right-angle kink.
            float from=MathF.Atan2(path[^1].Point.Z,MathF.Abs(path[^1].Point.X));
            for(int i=1;i<=32;i++)
            {
                float t=i/32f;
                float angle=Mathf.Lerp(from,.80f,TorsoRamp(0,1,t));
                Add(new(0,1.062f-.038f*(1-(1-t)*(1-t)),0),new(side*MathF.Cos(angle),0,MathF.Sin(angle)));
            }
            path.Add(new(pack+new Vector3(side*.082f,-.108f,-.027f),Vector3.Back,-1,Vector3.Zero,false));
            int start=vertices.Count;
            var edges=new Vector3[path.Count];
            for(int i=0;i<path.Count;i++)
            {
                var station=path[i];
                var along=path[Math.Min(i+1,path.Count-1)].Point-path[Math.Max(0,i-1)].Point;
                edges[i]=along.Cross(station.Outward).Normalized()*.009f;
                for(int edge=-1;edge<=1;edge+=2)
                {
                    var centre=station.Point+edge*edges[i];
                    // Project both leather edges. Centre-only projection
                    // leaves a flat ribbon over the rounded shoulder/chest.
                    var contact=station.Triangle<0?station with {Point=centre}
                        :Contact(centre-station.Outward*.22f,station.Outward,station.Shoulder);
                    contacts.Add(contact);
                    contacts.Add(contact);
                    float lift=contact.Triangle<0?0:StrapStandOff;
                    vertices.Add(contact.Point+contact.Outward*(lift+StrapThickness));
                    vertices.Add(contact.Point+contact.Outward*lift);
                    var texCoord=new Vector2((edge+1)*.5f,i*.15f);
                    uv.Add(texCoord);
                    uv.Add(texCoord);
                }
            }
            int At(int row,int edge,bool inside)=>start+row*4+(edge+1)+(inside?1:0);
            void Triangle((int Row,int Edge,bool Inside) a,(int Row,int Edge,bool Inside) b,(int Row,int Edge,bool Inside) c,Vector3 normal)
            {
                int ia=At(a.Row,a.Edge,a.Inside),ib=At(b.Row,b.Edge,b.Inside),ic=At(c.Row,c.Edge,c.Inside);
                if((vertices[ib]-vertices[ia]).Cross(vertices[ic]-vertices[ia]).Dot(normal)>0)(ib,ic)=(ic,ib);
                triangles.Add(ia);triangles.Add(ib);triangles.Add(ic);
            }
            for(int i=0;i<path.Count-1;i++)
            {
                foreach(bool inside in new[]{false,true})
                {
                    var normal=path[i].Outward*(inside?-1:1);
                    Triangle((i,-1,inside),(i,1,inside),(i+1,-1,inside),normal);
                    Triangle((i,1,inside),(i+1,1,inside),(i+1,-1,inside),normal);
                }
                foreach(int edge in new[]{-1,1})
                {
                    Triangle((i,edge,false),(i+1,edge,false),(i,edge,true),edge*edges[i]);
                    Triangle((i,edge,true),(i+1,edge,false),(i+1,edge,true),edge*edges[i]);
                }
            }
        }
        var rest=vertices.ToArray();
        var faces=triangles.ToArray();
        var bones=new int[rest.Length*4];
        var weights=new float[bones.Length];
        var garmentBones=cloth[(int)Mesh.ArrayType.Bones].AsInt32Array();
        var garmentWeights=cloth[(int)Mesh.ArrayType.Weights].AsFloat32Array();
        var influences=new float[ResidentRig.SupportedBodyBones];
        for(int i=0;i<contacts.Count;i++)
        {
            var contact=contacts[i];
            if(contact.Triangle<0){weights[i*4]=1;continue;}
            Array.Clear(influences);
            for(int corner=0;corner<3;corner++)
            {
                int vertex=indices[contact.Triangle+corner];
                for(int j=vertex*4;j<vertex*4+4;j++)
                    influences[garmentBones[j]]+=garmentWeights[j]*contact.Weights[corner];
            }
            float total=0;
            for(int slot=0;slot<4;slot++)
            {
                int bone=0;
                for(int candidate=1;candidate<influences.Length;candidate++)
                    if(influences[candidate]>influences[bone])bone=candidate;
                bones[i*4+slot]=bone;
                weights[i*4+slot]=influences[bone];
                total+=influences[bone];
                influences[bone]=0;
            }
            for(int slot=0;slot<4;slot++)weights[i*4+slot]/=total;
        }

        MeshArrays Surface(Vector3[] surfacePoints)
        {
            var normals=new Vector3[surfacePoints.Length];
            for(int t=0;t<faces.Length;t+=3)
            {
                int a=faces[t],b=faces[t+1],c=faces[t+2];
                var normal=(surfacePoints[c]-surfacePoints[a]).Cross(surfacePoints[b]-surfacePoints[a]);
                normals[a]+=normal;normals[b]+=normal;normals[c]+=normal;
            }
            for(int i=0;i<normals.Length;i++)normals[i]=normals[i].Normalized();
            var arrays=new MeshArrays();arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex]=surfacePoints;
            arrays[(int)Mesh.ArrayType.Normal]=normals;
            return arrays;
        }
        var fitted=new ArrayMesh {BlendShapeMode=garment.BlendShapeMode};
        var targets=new MeshShapes();
        foreach(var pose in garment.SurfaceGetBlendShapeArrays(0))
        {
            fitted.AddBlendShape(garment.GetBlendShapeName(targets.Count));
            var posed=pose[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var target=(Vector3[])rest.Clone();
            for(int i=0;i<target.Length;i++)
            {
                var contact=contacts[i];
                if(contact.Triangle<0)continue;
                for(int corner=0;corner<3;corner++)
                {
                    int vertex=indices[contact.Triangle+corner];
                    target[i]+=(posed[vertex]-points[vertex])*contact.Weights[corner];
                }
            }
            targets.Add(Surface(target));
        }
        var arrays=Surface(rest);
        arrays[(int)Mesh.ArrayType.Bones]=bones;
        arrays[(int)Mesh.ArrayType.Weights]=weights;
        arrays[(int)Mesh.ArrayType.TexUV]=uv.ToArray();
        arrays[(int)Mesh.ArrayType.Index]=faces;
        fitted.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays,targets);
        fitted.SurfaceSetMaterial(0,LeatherSurface("#51443a"));
        return _meshes[key]=fitted;
    }

    private void ValidateFemaleCarryGear(ResidentRig gatherer)
    {
        var pack=gatherer.PackMount??throw new InvalidOperationException("Missing fitted gathering pack");
        var harness=gatherer.Torso.GetNode<MeshInstance3D>("FittedPackHarness");
        var straps=harness.Mesh.SurfaceGetArrays(0);
        var strapPoints=straps[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var skinWeights=straps[(int)Mesh.ArrayType.Weights].AsFloat32Array();
        foreach(float side in new[]{-1f,1f})
        {
            var top=pack.Position+new Vector3(side*.077f,.086f,-.023f);
            var bottom=pack.Position+new Vector3(side*.082f,-.108f,-.027f);
            foreach(var anchor in new[]{top,bottom})
            {
                float distance=float.PositiveInfinity;
                foreach(var p in strapPoints)distance=MathF.Min(distance,p.DistanceTo(anchor));
                if(distance>.011f)throw new InvalidOperationException("Shoulder harness separates from a pack anchor");
            }
        }
        if(harness.Skin!=gatherer.DetailedBodySkin||skinWeights.Length!=strapPoints.Length*4)
            throw new InvalidOperationException("Gathering straps lose their cloth pose");
        ValidatePosedPackHarness(gatherer,harness,straps,strapPoints);

        int previous=_faceVariant,cut=_garmentCut;
        var trader=Resident(42,false,JobType.Trader);
        try
        {
            var pouch=trader.SatchelMount??throw new InvalidOperationException("Missing trader's belt pouch");
            if(trader.PackMount!=null||pouch.Scale!=Vector3.One||pouch.GetChildCount()!=7)
                throw new InvalidOperationException("Trading gear loses its rigid pouch and folded belt tabs");
            var body=pouch.GetNode<Node3D>("LeatherCase").GetChild<MeshInstance3D>(0);
            var local=InTorso(body,pouch);
            var points=body.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var bounds=new Aabb(local*points[0],Vector3.Zero);
            foreach(var p in points)bounds=bounds.Expand(local*p);
            bounds=bounds.Grow(.012f);
            int probes=0;
            foreach(var action in new[]{ActionKind.Wander,ActionKind.Take,ActionKind.Trade,ActionKind.Socialize,ActionKind.Eat})
                for(int tick=0;tick<120;tick++)
                {
                    trader.PoseAction(1f/60,action,action==ActionKind.Wander?ActionPhase.Moving:ActionPhase.Executing,false,0);
                    if(tick%10!=0)continue;
                    for(int side=0;side<2;side++)
                    {
                        var upper=InTorso(trader.Arms[side],trader.Torso).Origin;
                        var elbow=InTorso(trader.Elbows[side],trader.Torso).Origin;
                        var wrist=InTorso(trader.Hands[side],trader.Torso).Origin;
                        var palm=InTorso(trader.Hands[side],trader.Torso)*new Vector3(0,-.06f,0);
                        var inverse=pouch.Transform.AffineInverse();
                        for(int step=0;step<=12;step++)
                        {
                            float t=step/12f;
                            if(bounds.HasPoint(inverse*upper.Lerp(elbow,t))||bounds.HasPoint(inverse*elbow.Lerp(wrist,t))||bounds.HasPoint(inverse*wrist.Lerp(palm,t)))
                                throw new InvalidOperationException($"Trader's waist pouch enters the arm path during {action}, frame={tick}, side={side}, station={step}");
                            probes+=3;
                        }
                    }
                }
            GD.Print($"FEMALE_CARRY_GEAR_PASS: four pack strap anchors, skinned harness, two belt tabs and {probes} trader arm clearance samples");
        }
        finally{trader.Free();_faceVariant=previous;_facePoints=null;_faceIndices=null;SelectGarmentCut(cut);}
    }

    private void ValidatePosedPackHarness(ResidentRig rig,MeshInstance3D harness,MeshArrays straps,Vector3[] rest)
    {
        var cloth=rig.DetailedBodyMesh.SurfaceGetArrays(0);
        var points=cloth[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var pattern=cloth[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
        var indices=cloth[(int)Mesh.ArrayType.Index].AsInt32Array();
        var clothBones=cloth[(int)Mesh.ArrayType.Bones].AsInt32Array();
        var clothWeights=cloth[(int)Mesh.ArrayType.Weights].AsFloat32Array();
        var strapBones=straps[(int)Mesh.ArrayType.Bones].AsInt32Array();
        var strapWeights=straps[(int)Mesh.ArrayType.Weights].AsFloat32Array();
        var poses=harness.Mesh.SurfaceGetBlendShapeArrays(0);
        var transforms=new Transform3D[ResidentRig.SupportedBodyBones];
        var probes=new List<(int Strap,int Face,Vector3 Coordinates)>();
        // Sample the inner faces across the crown of both shoulder straps.
        // Find each actual sewn triangle independently of the harness builder.
        for(int i=1;i<rest.Length;i+=2)
        {
            var point=rest[i];
            if(point.Y<1.177f||MathF.Abs(point.Z)>.044f)continue;
            int face=-1;float nearest=.002f*.002f;var coordinates=Vector3.Zero;
            for(int t=0;t<indices.Length;t+=3)
            {
                int a=indices[t],b=indices[t+1],c=indices[t+2];
                if(pattern[a].X>.055f||pattern[b].X>.055f||pattern[c].X>.055f||
                    (points[a]-point).LengthSquared()>.045f*.045f)continue;
                var w=Geometry3D.GetTriangleBarycentricCoords(point,points[a],points[b],points[c]);
                if(!w.IsFinite()||MathF.Min(w.X,MathF.Min(w.Y,w.Z))<0)continue;
                float distance=(points[a]*w.X+points[b]*w.Y+points[c]*w.Z-point).LengthSquared();
                if(distance>=nearest)continue;
                face=t;nearest=distance;coordinates=w;
            }
            if(face>=0)probes.Add((i,face,coordinates));
        }
        if(probes.Count<16)throw new InvalidOperationException("Too few shoulder-strap edge contacts on the sewn garment");
        float maximum=0,minimum=float.PositiveInfinity,motion=0;
        Vector3 SkinPoint(Vector3 point,int vertex,int[] bones,float[] weights)
        {
            var result=Vector3.Zero;
            for(int i=vertex*4;i<vertex*4+4;i++)result+=(transforms[bones[i]]*point)*weights[i];
            return result;
        }
        foreach(int side in new[]{0,1})
        foreach(int angle in new[]{45,90,135})
        {
            for(int frame=0;frame<90;frame++)rig.StudyArmElevation(1f/60,Mathf.DegToRad(angle),.52f,side);
            var clothPose=CorrectedSleeveSource(rig,points);
            var strapPose=(Vector3[])rest.Clone();
            for(int shape=0;shape<poses.Count;shape++)
            {
                float weight=harness.GetBlendShapeValue(shape);
                if(weight!=rig.BodySkin.GetBlendShapeValue(shape))throw new InvalidOperationException("Shoulder strap pose falls out of sync with the sewn panel");
                if(weight==0)continue;
                var target=poses[shape][(int)Mesh.ArrayType.Vertex].AsVector3Array();
                foreach(var probe in probes)strapPose[probe.Strap]+=(target[probe.Strap]-rest[probe.Strap])*weight;
            }
            for(int b=0;b<transforms.Length;b++)transforms[b]=rig.ShoulderSkeleton.GetBonePose(b)*rig.DetailedBodySkin.GetBindPose(b);
            foreach(var (i,face,w) in probes)
            {
                int a=indices[face],b=indices[face+1],c=indices[face+2];
                var pa=SkinPoint(clothPose[a],a,clothBones,clothWeights);
                var pb=SkinPoint(clothPose[b],b,clothBones,clothWeights);
                var pc=SkinPoint(clothPose[c],c,clothBones,clothWeights);
                var normal=(pc-pa).Cross(pb-pa).Normalized();
                var strap=SkinPoint(strapPose[i],i,strapBones,strapWeights);
                var gap=strap-(pa*w.X+pb*w.Y+pc*w.Z);
                maximum=MathF.Max(maximum,gap.Length());
                minimum=MathF.Min(minimum,normal.Dot(gap));
                motion=MathF.Max(motion,rest[i].DistanceTo(strap));
            }
        }
        if(maximum>.006f||minimum<-.003f||motion<.003f)
            throw new InvalidOperationException($"Shoulder harness loses surface contact: gap={maximum:F4}, signed={minimum:F4}, movement={motion:F4}");
        rig.SetHandDetail(false);
        for(int shape=0;shape<poses.Count;shape++)
            if(harness.GetBlendShapeValue(shape)!=0)throw new InvalidOperationException("Distant straps retain a close-up pose");
        if(harness.Skin!=null||!harness.Skeleton.IsEmpty)throw new InvalidOperationException("Distant straps retain close-up skin binding");
        rig.SetHandDetail(true);
        for(int shape=0;shape<poses.Count;shape++)
            if(harness.GetBlendShapeValue(shape)!=rig.BodySkin.GetBlendShapeValue(shape))
                throw new InvalidOperationException("Shoulder strap fails to rebind at close range");
        for(int side=0;side<2;side++)for(int frame=0;frame<90;frame++)rig.StudyArmElevation(1f/60,0,.52f,side);
        GD.Print($"POSED_PACK_HARNESS_PASS: {6*probes.Count} shoulder contacts, max gap={maximum*1000:F2} mm, minimum signed gap={minimum*1000:F2} mm; near/far binding");
    }
}
