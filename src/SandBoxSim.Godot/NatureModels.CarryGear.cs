using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private readonly record struct StrapStation(Vector3 Centre,Vector3 Outward,float Pelvis);

    private void AddFemaleCarryGear(ResidentRig rig,JobType job)
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
            var harness=new MeshInstance3D {Name="FittedPackHarness",Mesh=FemalePackHarness(pack.Position,points,indices),
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

    private Mesh FemalePackHarness(Vector3 pack,Vector3[] points,int[] indices)
    {
        string key="female-pack-harness:"+_garmentCut;
        if(_meshes.TryGetValue(key,out var mesh))return mesh;
        using var surface=new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        surface.SetMaterial(LeatherSurface("#51443a"));
        var rotated=new Vector3[points.Length];
        Vector3 Contact(Vector3 origin,Vector3 normal)
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
            float depth=GarmentDepth(rotated,indices,0,0,true);
            if(!float.IsFinite(depth)||depth<=0)throw new InvalidOperationException($"Shoulder strap misses the fitted garment at {origin}/{normal}");
            return origin+normal*(depth+.004f);
        }
        foreach(float side in new[]{-1f,1f})
        {
            var path=new List<StrapStation>();
            void Add(Vector3 origin,Vector3 outward)
            {
                var centre=Contact(origin,outward);
                path.Add(new(centre,outward,ResidentRig.PelvicClothWeight(centre.Y)));
            }
            float x=side*.077f;
            float packWeight=ResidentRig.PelvicClothWeight(pack.Y);
            path.Add(new(pack+new Vector3(x,.086f,-.023f),Vector3.Back,packWeight));
            for(int i=0;i<=24;i++)
            {
                float angle=i*MathF.PI/24;
                Add(new(side*(.077f+.010f*MathF.Sin(angle)),1.181f,0),new(0,MathF.Sin(angle),MathF.Cos(angle)));
            }
            for(int i=1;i<=20;i++)
            {
                float t=i/20f;
                Add(new(side*Mathf.Lerp(.077f,.091f,t),Mathf.Lerp(1.181f,1.032f,t),0),Vector3.Forward);
            }
            float from=MathF.Atan2(path[^1].Centre.Z,MathF.Abs(path[^1].Centre.X));
            for(int i=1;i<=20;i++)
            {
                float angle=Mathf.Lerp(from,.80f,i/20f);
                Add(new(0,1.032f,0),new(side*MathF.Cos(angle),0,MathF.Sin(angle)));
            }
            path.Add(new(pack+new Vector3(side*.082f,-.108f,-.027f),Vector3.Back,packWeight));
            var edges=new Vector3[path.Count];
            for(int i=0;i<path.Count;i++)
            {
                var along=path[Math.Min(i+1,path.Count-1)].Centre-path[Math.Max(0,i-1)].Centre;
                edges[i]=along.Cross(path[i].Outward).Normalized()*.009f;
            }
            Vector3 At(int row,int edge,bool inside)=>path[row].Centre+edge*edges[row]-(inside?path[row].Outward*.0025f:Vector3.Zero);
            void Triangle((int Row,int Edge,bool Inside) a,(int Row,int Edge,bool Inside) b,(int Row,int Edge,bool Inside) c,Vector3 normal)
            {
                var p=At(a.Row,a.Edge,a.Inside);var q=At(b.Row,b.Edge,b.Inside);var r=At(c.Row,c.Edge,c.Inside);
                if((q-p).Cross(r-p).Dot(normal)>0)(b,c)=(c,b);
                foreach(var v in new[]{a,b,c})
                {
                    float pelvis=path[v.Row].Pelvis;
                    surface.SetBones(new[]{0,3,0,0});surface.SetWeights(new[]{1-pelvis,pelvis,0f,0f});
                    surface.SetUV(new((v.Edge+1)*.5f,v.Row*.15f));
                    surface.AddVertex(At(v.Row,v.Edge,v.Inside));
                }
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
        surface.GenerateNormals();surface.Index();
        return _meshes[key]=surface.Commit();
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
}
