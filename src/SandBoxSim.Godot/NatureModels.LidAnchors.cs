using System;
using System.Collections.Generic;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private sealed record LidAttachment(Vector3 Point,Vector3 Delta,int A,int B,int C,Vector3 Bary);
    private readonly Dictionary<(int Family,int Eye,int Sample,bool Upper),LidAttachment> _lidAttachments=new();
    private readonly Dictionary<(int Family,int Eye),int[]> _lidTriangles=new();
    private LidAttachment AttachLid(int eye,int sample,bool upper)
    {
        var key=(HumanFamily,eye,sample,upper);if(_lidAttachments.TryGetValue(key,out var cached))return cached;
        var data=HumanData();var head=data.Head;var point=(upper?data.UpperLids:data.LowerLids)[eye][sample];
        var regionKey=(HumanFamily,eye);
        if(!_lidTriangles.TryGetValue(regionKey,out var candidates))
        {
            var low=point;var high=point;
            foreach(var curve in new[]{data.UpperLids[eye],data.LowerLids[eye]})foreach(var v in curve)
            {low=new(MathF.Min(low.X,v.X),MathF.Min(low.Y,v.Y),MathF.Min(low.Z,v.Z));high=new(MathF.Max(high.X,v.X),MathF.Max(high.Y,v.Y),MathF.Max(high.Z,v.Z));}
            low-=Vector3.One*.008f;high+=Vector3.One*.008f;var faces=new List<int>();
            for(int i=0;i<head.Indices.Length;i+=3)
            {
                var a=head.Points[head.Indices[i]];var b=head.Points[head.Indices[i+1]];var c=head.Points[head.Indices[i+2]];
                if(MathF.Max(a.X,MathF.Max(b.X,c.X))<low.X||MathF.Min(a.X,MathF.Min(b.X,c.X))>high.X||
                   MathF.Max(a.Y,MathF.Max(b.Y,c.Y))<low.Y||MathF.Min(a.Y,MathF.Min(b.Y,c.Y))>high.Y||
                   MathF.Max(a.Z,MathF.Max(b.Z,c.Z))<low.Z||MathF.Min(a.Z,MathF.Min(b.Z,c.Z))>high.Z)continue;
                faces.Add(i);
            }
            _lidTriangles[regionKey]=candidates=faces.ToArray();
        }
        float best=float.PositiveInfinity;LidAttachment? result=null;
        foreach(int face in candidates)
        {
            int a=head.Indices[face],b=head.Indices[face+1],c=head.Indices[face+2];
            var pa=head.Points[a];var pb=head.Points[b];var pc=head.Points[c];if((pb-pa).Cross(pc-pa).LengthSquared()<1e-18f)continue;
            var weights=ClosestBarycentric(point,pa,pb,pc);var rest=pa*weights.X+pb*weights.Y+pc*weights.Z;float distance=rest.DistanceSquaredTo(point);
            if(distance>=best)continue;best=distance;
            var delta=head.Blink![a]*weights.X+head.Blink[b]*weights.Y+head.Blink[c]*weights.Z;
            result=new(rest,delta,a,b,c,weights);
        }
        if(result==null||best>.000025f)throw new InvalidOperationException("Eye aperture has no nearby source skin triangle");
        return _lidAttachments[key]=result;
    }
    private static Vector3 ClosestBarycentric(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
    {
        var ab=b-a;var ac=c-a;var ap=p-a;float d1=ab.Dot(ap),d2=ac.Dot(ap);
        if(d1<=0&&d2<=0)return new(1,0,0);
        var bp=p-b;float d3=ab.Dot(bp),d4=ac.Dot(bp);if(d3>=0&&d4<=d3)return new(0,1,0);
        float vc=d1*d4-d3*d2;if(vc<=0&&d1>=0&&d3<=0){float v=d1/(d1-d3);return new(1-v,v,0);}
        var cp=p-c;float d5=ab.Dot(cp),d6=ac.Dot(cp);if(d6>=0&&d5<=d6)return new(0,0,1);
        float vb=d5*d2-d1*d6;if(vb<=0&&d2>=0&&d6<=0){float w=d2/(d2-d6);return new(1-w,0,w);}
        float va=d3*d6-d5*d4;if(va<=0&&d4-d3>=0&&d5-d6>=0){float w=(d4-d3)/(d4-d3+d5-d6);return new(0,1-w,w);}
        float sum=va+vb+vc;return new(va/sum,vb/sum,vc/sum);
    }
}
