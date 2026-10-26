using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private sealed record HairlineData(Vector2[] Metrics,int RootEdges,int CapVertices);
    private readonly Dictionary<int,HairlineData> _hairlineSources=new();
    private HairlineData HairlineSource()
    {
        if(_hairlineSources.TryGetValue(HumanFamily,out var cached))return cached;
        var source=HumanData().Hair;
        var physical=new List<Vector3>();var welded=new Dictionary<(int,int,int),int>();var ids=new int[source.Points.Length];
        for(int i=0;i<ids.Length;i++)
        {
            var p=source.Points[i];var key=((int)MathF.Round(p.X*1000000),(int)MathF.Round(p.Y*1000000),(int)MathF.Round(p.Z*1000000));
            if(!welded.TryGetValue(key,out int index)){index=physical.Count;welded.Add(key,index);physical.Add(p);}ids[i]=index;
        }
        var neighbours=physical.Select(_=>new HashSet<int>()).ToArray();var counts=new Dictionary<(int,int),int>();
        for(int i=0;i<source.Indices.Length;i+=3)
        for(int edge=0;edge<3;edge++)
        {
            int a=ids[source.Indices[i+edge]],b=ids[source.Indices[i+(edge+1)%3]];if(a==b)continue;
            neighbours[a].Add(b);neighbours[b].Add(a);var key=a<b?(a,b):(b,a);
            counts.TryGetValue(key,out int count);counts[key]=count+1;
        }
        // Weld only positional UV duplicates before finding open edges. UV
        // seams must not become artificial hairlines across the crown.
        var border=physical.Select(_=>new HashSet<int>()).ToArray();
        foreach(var pair in counts)if(pair.Value==1){border[pair.Key.Item1].Add(pair.Key.Item2);border[pair.Key.Item2].Add(pair.Key.Item1);}
        var visited=new HashSet<int>();List<int>? root=null;
        for(int start=0;start<border.Length;start++)
        {
            if(border[start].Count==0||visited.Contains(start))continue;
            var component=new List<int>();var pending=new Stack<int>();pending.Push(start);
            while(pending.Count>0)
            {
                int v=pending.Pop();if(!visited.Add(v))continue;component.Add(v);foreach(int next in border[v])pending.Push(next);
            }
            var low=physical[component[0]];var high=low;
            foreach(int v in component)
            {
                var p=physical[v];low=new(MathF.Min(low.X,p.X),MathF.Min(low.Y,p.Y),MathF.Min(low.Z,p.Z));high=new(MathF.Max(high.X,p.X),MathF.Max(high.Y,p.Y),MathF.Max(high.Z,p.Z));
            }
            // The scalp's root encircles both temples and the back of the
            // head. Isolated flyaway cards and the ponytail fail this extent.
            if(high.X-low.X>.12f&&low.Z<-.075f&&high.Z>.025f)
            {
                if(root!=null)throw new InvalidOperationException("Hair source has ambiguous scalp root boundaries");root=component;
            }
        }
        if(root==null||root.Count<30||root.Any(v=>border[v].Count!=2))throw new InvalidOperationException("Hair source has no closed physical scalp root");
        // Start the arc at the back; the circular coordinate seam stays away
        // from the exposed forehead and temples.
        int first=root.OrderByDescending(v=>physical[v].Z).ThenBy(v=>physical[v].X).First(),previous=-1,current=first;
        var ordered=new List<int>();
        do
        {
            ordered.Add(current);int next=border[current].Where(v=>v!=previous).OrderBy(v=>v).First();previous=current;current=next;
            if(ordered.Count>root.Count)throw new InvalidOperationException("Hair root is not a single closed loop");
        }while(current!=first);
        if(ordered.Count!=root.Count)throw new InvalidOperationException("Hair root traversal omits source edges");
        var cap=new HashSet<int>();var queue=new Stack<int>();queue.Push(first);
        while(queue.Count>0){int v=queue.Pop();if(!cap.Add(v))continue;foreach(int next in neighbours[v])queue.Push(next);}
        var arc=new float[ordered.Count+1];
        for(int edge=0;edge<ordered.Count;edge++)arc[edge+1]=arc[edge]+physical[ordered[edge]].DistanceTo(physical[ordered[(edge+1)%ordered.Count]]);
        var metric=new Vector2[source.Points.Length];int capVertices=0;
        for(int i=0;i<metric.Length;i++)
        {
            if(!cap.Contains(ids[i])){metric[i]=new(-1,0);continue;}capVertices++;
            var point=source.Points[i];float best=float.PositiveInfinity,at=0;
            for(int edge=0;edge<ordered.Count;edge++)
            {
                var a=physical[ordered[edge]];var delta=physical[ordered[(edge+1)%ordered.Count]]-a;
                float t=Math.Clamp((point-a).Dot(delta)/delta.LengthSquared(),0,1),distance=point.DistanceSquaredTo(a+delta*t);
                if(distance>=best)continue;best=distance;at=arc[edge]+delta.Length()*t;
            }
            metric[i]=new(MathF.Sqrt(best),at);
        }
        return _hairlineSources[HumanFamily]=new(metric,ordered.Count,capVertices);
    }

    private void ValidateHairlineSource()
    {
        var source=HumanData().Hair;var data=HairlineSource();var mesh=HumanMesh("hair");var arrays=mesh.SurfaceGetArrays(0);
        var metric=arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();var uv=arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        if(!ReferenceEquals(data,HairlineSource())||metric.Length!=source.Points.Length||indices.Length!=source.Indices.Length)throw new InvalidOperationException("Hairline loses source cache or topology");
        int boundary=0,excluded=0,interior=0;
        for(int i=0;i<metric.Length;i++)
        {
            if(metric[i]!=data.Metrics[i]||!metric[i].IsFinite()||uv[i]!=source.Uv[i])throw new InvalidOperationException("Hairline modifies source UV or loses metric attributes");
            if(metric[i].X<0){if(metric[i]!=new Vector2(-1,0))throw new InvalidOperationException("Invalid non-cap hair sentinel");excluded++;}
            else if(metric[i].X<.000002f)boundary++;
            else if(metric[i].X>.004f)interior++;
        }
        for(int i=0;i<indices.Length;i++)if(indices[i]!=source.Indices[i])throw new InvalidOperationException("Hairline replaces the original hair cards");
        if(boundary<data.RootEdges||interior==0||data.CapVertices+excluded!=metric.Length||(HumanFamily!=1&&excluded==0))
            throw new InvalidOperationException("Hairline loses its boundary, full-opacity interior or excluded flyaway/ponytail cards");
        GD.Print($"HAIRLINE_SOURCE_PASS: family={HumanFamily}, root={data.RootEdges} edges, cap={data.CapVertices}, excluded={excluded}; source UV/topology, metric units and family cache");
    }
}
