using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    // One cached CPU refinement of curved skull edges. Original vertices,
    // apertures and articulation boundaries stay fixed; this is not a new rig.
    private static void RefineAnimalHead(Godot.Collections.Array arrays,bool wolf,string material)
    {
        if(material is not ("Wolf" or "Head"))return;
        var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().ToList();
        var n=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array().ToList();
        var uv=arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array().ToList();
        var uv2=arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array().ToList();
        var colour=arrays[(int)Mesh.ArrayType.Color].AsColorArray().ToList();
        var bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array().ToList();
        var weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array().ToList();
        var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();int original=p.Count;
        var positionIds=new Dictionary<(int,int,int),int>();var ids=new int[original];var normals=new List<Vector3>();
        for(int i=0;i<original;i++)
        {
            var key=((int)MathF.Round(p[i].X*100000),(int)MathF.Round(p[i].Y*100000),(int)MathF.Round(p[i].Z*100000));
            if(!positionIds.TryGetValue(key,out int id)){id=normals.Count;positionIds.Add(key,id);normals.Add(Vector3.Zero);}
            ids[i]=id;normals[id]+=n[i];
        }
        for(int i=0;i<normals.Count;i++)normals[i]=normals[i].Normalized();
        (int,int) Edge(int a,int b)=>a<b?(a,b):(b,a);
        bool Skull(int i){float weight=0;for(int b=0;b<4;b++)if(bones[i*4+b]==1)weight+=weights[i*4+b];return weight>.9999f;}
        bool Aperture(Vector3 point)
        {
            var eye=wolf?new Vector3(.061f,.901f,-.785f):new Vector3(.070f,1.421f,-.854f);
            var nose=wolf?new Vector3(0,.826f,-.908f):new Vector3(0,1.370f,-1.106f);
            point.X=MathF.Abs(point.X);return point.DistanceTo(eye)<.022f||point.DistanceTo(nose)<.024f;
        }
        var scores=new Dictionary<(int,int),float>();
        var limits=new Dictionary<(int,int),float>();
        for(int i=0;i<indices.Length;i+=3)foreach(var pair in new[]{(indices[i],indices[i+1],indices[i+2]),(indices[i+1],indices[i+2],indices[i]),(indices[i+2],indices[i],indices[i+1])})
        {
            int a=pair.Item1,b=pair.Item2;float length=p[a].DistanceTo(p[b]),dot=normals[ids[a]].Dot(normals[ids[b]]);
            var edge=Edge(ids[a],ids[b]);float altitude=(p[pair.Item3]-p[a]).Cross(p[b]-p[a]).Length()/MathF.Max(.000001f,length);
            float limit=MathF.Min(.0015f,altitude*.12f);limits.TryGetValue(edge,out float existing);
            limits[edge]=existing>0?MathF.Min(existing,limit):limit;
            if(!Skull(a)||!Skull(b)||Aperture(p[a])||Aperture(p[b])||length<.014f||dot<.35f||dot>.9999f)continue;
            scores[Edge(ids[a],ids[b])]=length*(1-dot);
        }
        // Physical edge groups include duplicated atlas vertices, so UV seams
        // receive the same split and geometric projection on both sides.
        var selected=scores.OrderByDescending(e=>e.Value).ThenBy(e=>e.Key).Take(400).Select(e=>e.Key).ToHashSet();
        var midpoints=new Dictionary<(int,int),int>();
        int Mid(int a,int b)
        {
            if(!selected.Contains(Edge(ids[a],ids[b])))return -1;
            var key=Edge(a,b);if(midpoints.TryGetValue(key,out int cached))return cached;
            var linear=(p[a]+p[b])*.5f;var na=normals[ids[a]];var nb=normals[ids[b]];
            var projected=((linear-na*(linear-p[a]).Dot(na))+(linear-nb*(linear-p[b]).Dot(nb)))*.5f;
            var shift=(projected-linear)*.55f;float limit=limits[Edge(ids[a],ids[b])];if(shift.Length()>limit)shift=shift.Normalized()*limit;
            int index=p.Count;p.Add(linear+shift);n.Add((n[a]+n[b]).Normalized());uv.Add((uv[a]+uv[b])*.5f);uv2.Add((uv2[a]+uv2[b])*.5f);colour.Add(colour[a].Lerp(colour[b],.5f));
            var blended=new Dictionary<int,float>();
            foreach(int vertex in new[]{a,b})for(int k=0;k<4;k++)
            {int bone=bones[vertex*4+k];blended.TryGetValue(bone,out float value);blended[bone]=value+weights[vertex*4+k]*.5f;}
            var top=blended.OrderByDescending(e=>e.Value).ThenBy(e=>e.Key).Take(4).ToArray();float sum=top.Sum(e=>e.Value);
            for(int k=0;k<4;k++){bones.Add(k<top.Length?top[k].Key:0);weights.Add(k<top.Length?top[k].Value/sum:0);}
            midpoints.Add(key,index);return index;
        }
        var refined=new List<int>(indices.Length+selected.Count*12);
        void Triangle(int a,int b,int c){refined.Add(a);refined.Add(b);refined.Add(c);}
        for(int i=0;i<indices.Length;i+=3)
        {
            int a=indices[i],b=indices[i+1],c=indices[i+2],ab=Mid(a,b),bc=Mid(b,c),ca=Mid(c,a);
            int count=(ab>=0?1:0)+(bc>=0?1:0)+(ca>=0?1:0);
            if(count==0){Triangle(a,b,c);continue;}
            if(count==3){Triangle(a,ab,ca);Triangle(ab,b,bc);Triangle(ca,bc,c);Triangle(ab,bc,ca);continue;}
            if(count==1)
            {
                if(bc>=0){(a,b,c)=(b,c,a);ab=bc;}else if(ca>=0){(a,b,c)=(c,a,b);ab=ca;}
                Triangle(a,ab,c);Triangle(ab,b,c);
            }
            else
            {
                if(ab<0){(a,b,c)=(b,c,a);(ab,bc,ca)=(bc,ca,ab);}else if(bc<0){(a,b,c)=(c,a,b);(ab,bc,ca)=(ca,ab,bc);}
                Triangle(b,bc,ab);Triangle(a,ab,c);Triangle(ab,bc,c);
            }
        }
        if(refined.Count<indices.Length||refined.Count-indices.Length>7200)throw new InvalidOperationException("Skull refinement exceeds its index budget");
        var seamPoints=new Dictionary<(int,int),Vector3>();
        foreach(var pair in midpoints)
        {
            var physical=Edge(ids[pair.Key.Item1],ids[pair.Key.Item2]);var point=p[pair.Value];
            if(seamPoints.TryGetValue(physical,out var other)&&other.DistanceTo(point)>.00002f)throw new InvalidOperationException("Skull refinement separates an atlas seam");
            seamPoints[physical]=point;
        }
        for(int i=0;i<refined.Count;i+=3)
        {
            int a=refined[i],b=refined[i+1],c=refined[i+2];if(a<original&&b<original&&c<original)continue;
            if((p[c]-p[a]).Cross(p[b]-p[a]).Dot(n[a]+n[b]+n[c])<=0)throw new InvalidOperationException("Skull refinement reverses a surface");
        }
        GD.Print($"SKULL_REFINEMENT: {(wolf?"wolf":"deer")}, {indices.Length/3} → {refined.Count/3} triangles, {selected.Count} physical edges; bounded displacement and atlas seam checks");
        arrays[(int)Mesh.ArrayType.Vertex]=p.ToArray();arrays[(int)Mesh.ArrayType.Normal]=n.ToArray();arrays[(int)Mesh.ArrayType.TexUV]=uv.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV2]=uv2.ToArray();arrays[(int)Mesh.ArrayType.Color]=colour.ToArray();arrays[(int)Mesh.ArrayType.Bones]=bones.ToArray();
        arrays[(int)Mesh.ArrayType.Weights]=weights.ToArray();arrays[(int)Mesh.ArrayType.Index]=refined.ToArray();
    }
}
