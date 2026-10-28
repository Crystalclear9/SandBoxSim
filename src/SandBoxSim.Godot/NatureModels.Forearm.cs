using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    public Node3D ForearmStudy(int side,int identity=17)
    {
        var rig=Resident(identity,false,SandBoxSim.Core.Agents.JobType.Gatherer);
        rig.SetHandDetail(true);rig.PoseHandReference(0);
        var elbow=rig.Elbows[side];elbow.GetParent().RemoveChild(elbow);
        elbow.Position=new(0,.35f,0);elbow.Rotation=Vector3.Zero;
        rig.Free();return elbow;
    }
    private Mesh AnatomicalForearm(int side)
    {
        string key="anatomical-forearm:"+side+":"+BodyFrame;
        if(_meshes.TryGetValue(key,out var cached))return cached;
        const string hash="de084f7d5717a636b4c1d5057ca07cc5941534b4b83ea9eb400059618cc77018";
        string raw=FileAccess.GetFileAsString("res://assets/models/human/human-forearm.mesh.json");
        if(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant()!=hash)
            throw new InvalidOperationException("Anatomical forearm source mismatch");
        using var doc=JsonDocument.Parse(raw);var root=doc.RootElement;
        float[] Floats(string field){var result=new List<float>();foreach(var v in root.GetProperty(field).EnumerateArray())result.Add(v.GetSingle());return result.ToArray();}
        var p=Floats("positions");var n=Floats("normals");var tex=Floats("uv");int count=p.Length/3;
        if(root.GetProperty("schemaVersion").GetInt32()!=1||count==0||p.Length!=count*3||n.Length!=p.Length||tex.Length!=count*2)
            throw new InvalidOperationException("Invalid anatomical forearm attributes");
        var points=new Vector3[count];var normals=new Vector3[count];var uv=new Vector2[count];
        float mirror=ResidentRig.HandMirror(side);
        for(int i=0;i<count;i++)
        {
            var origin=new Vector3(0,.255f,.010f);
            var fitted=RefineForearmProfile(new Vector3(p[i*3],p[i*3+1],p[i*3+2])+origin,new(n[i*3],n[i*3+1],n[i*3+2]),BodyFrame);
            var local=fitted.Point-origin;
            points[i]=new(local.X*mirror,local.Y,local.Z);normals[i]=new(fitted.Normal.X*mirror,fitted.Normal.Y,fitted.Normal.Z);uv[i]=new(tex[i*2],tex[i*2+1]);
            if(!points[i].IsFinite()||!normals[i].IsFinite()||MathF.Abs(normals[i].Length()-1)>.01f)
                throw new InvalidOperationException("Non-finite anatomical forearm");
        }
        var ids=new List<int>();foreach(var value in root.GetProperty("indices").EnumerateArray())
        {int index=value.GetInt32();if(index<0||index>=count)throw new InvalidOperationException("Invalid anatomical forearm index");ids.Add(index);}
        var indices=ids.ToArray();
        if(indices.Length%3!=0)throw new InvalidOperationException("Incomplete anatomical forearm triangles");
        if(mirror<0)for(int i=0;i<indices.Length;i+=3)(indices[i+1],indices[i+2])=(indices[i+2],indices[i+1]);
        var arrays=new Godot.Collections.Array();arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex]=points;arrays[(int)Mesh.ArrayType.Normal]=normals;arrays[(int)Mesh.ArrayType.TexUV]=uv;arrays[(int)Mesh.ArrayType.Index]=indices;
        var mesh=new ArrayMesh();mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);_meshes[key]=mesh;return mesh;
    }
    private Mesh DistantForearm(int side)
    {
        string key="distant-forearm:"+side+":"+BodyFrame;if(_meshes.TryGetValue(key,out var cached))return cached;
        var arrays=AnatomicalForearm(side).SurfaceGetArrays(0);
        var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        // Distant fingers are rigid cached poses. Retain the old buried overlap
        // below the wrist so their changing hand orientation cannot expose a cut.
        for(int i=0;i<points.Length;i++)if(points[i].Y<-.190f)
        {points[i].Y=-.190f+(points[i].Y+.190f)*2.25f;normals[i]=new Vector3(normals[i].X,normals[i].Y/2.25f,normals[i].Z).Normalized();}
        arrays[(int)Mesh.ArrayType.Vertex]=points;arrays[(int)Mesh.ArrayType.Normal]=normals;
        var mesh=new ArrayMesh();mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);_meshes[key]=mesh;return mesh;
    }
    private void ValidateForearmSurfaces()
    {
        ValidateWristContour();
        var left=AnatomicalForearm(0);var right=AnatomicalForearm(1);
        var l=left.SurfaceGetArrays(0);var r=right.SurfaceGetArrays(0);
        var p=l[(int)Mesh.ArrayType.Vertex].AsVector3Array();var q=r[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var n=l[(int)Mesh.ArrayType.Normal].AsVector3Array();var m=r[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var ids=l[(int)Mesh.ArrayType.Index].AsInt32Array();var mirrored=r[(int)Mesh.ArrayType.Index].AsInt32Array();
        if(p.Length!=q.Length||ids.Length!=mirrored.Length||AnatomicalForearm(0)!=left)
            throw new InvalidOperationException("Anatomical forearm cache/topology mismatch");
        for(int i=0;i<p.Length;i++)if(ReflectHand(p[i]).DistanceTo(q[i])>.00001f||ReflectHand(n[i]).DistanceTo(m[i])>.0001f)
            throw new InvalidOperationException("Forearms share unmirrored anatomy");
        var edges=new Dictionary<(int,int),int>();var parent=new int[p.Length];for(int i=0;i<parent.Length;i++)parent[i]=i;
        int Root(int v){while(parent[v]!=v){parent[v]=parent[parent[v]];v=parent[v];}return v;}
        for(int i=0;i<ids.Length;i+=3)
        {
            if(ids[i]!=mirrored[i]||ids[i+1]!=mirrored[i+2]||ids[i+2]!=mirrored[i+1])
                throw new InvalidOperationException("Mirrored forearm winding mismatch");
            for(int j=0;j<3;j++)
            {
                int x=ids[i+j],y=ids[i+(j+1)%3];parent[Root(x)]=Root(y);if(x>y)(x,y)=(y,x);
                edges[(x,y)]=edges.TryGetValue((x,y),out int count)?count+1:1;
            }
        }
        int wrist=0,elbow=0;
        foreach(var pair in edges)
        {
            if(pair.Value>2)throw new InvalidOperationException("Non-manifold forearm");
            if(pair.Value!=1)continue;
            foreach(int v in new[]{pair.Key.Item1,pair.Key.Item2})
            {
                if(MathF.Abs(p[v].Y+.210f)<.00001f)
                {
                    float oval=p[v].X*p[v].X/(WristHalfWidth*WristHalfWidth)+p[v].Z*p[v].Z/(WristHalfDepth*WristHalfDepth);
                    if(MathF.Abs(oval-1)>.0001f)throw new InvalidOperationException("Forearm wrist does not match the hand ring");
                    wrist++;
                }
                else if(MathF.Abs(p[v].Y-.020f)<.00001f)elbow++;
                else throw new InvalidOperationException("Exposed anatomical forearm cut");
            }
        }
        for(int i=0;i<p.Length;i++)if(Root(i)!=Root(0))throw new InvalidOperationException("Detached anatomical forearm component");
        if(wrist<30||elbow<30)throw new InvalidOperationException("Missing forearm openings");
        GD.Print("FOREARM_ANATOMY_PASS: continuous source, mirrored anatomy/normals/winding, shared cache, buried elbow and matched wrist ring");
    }

}
