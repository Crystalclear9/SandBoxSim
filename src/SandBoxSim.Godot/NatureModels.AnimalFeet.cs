using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private readonly record struct AnimalFootFrame(Vector3 Minimum,Vector3 Maximum)
    {
        public float CenterX=>(Minimum.X+Maximum.X)*.5f;
        public float Width=>Maximum.X-Minimum.X;
    }
    private readonly AnimalFootFrame[]?[] _authoredFootFrames=new AnimalFootFrame[2][];

    private static int AuthoredFoot(int vertex,int[] bones,float[] weights)
    {
        int result=-1;float best=.5f;
        for(int k=0;k<4;k++)
        {
            int bone=bones[vertex*4+k];float weight=weights[vertex*4+k];
            if(bone is 4 or 6 or 8 or 10&&weight>best){result=(bone-4)/2;best=weight;}
        }
        return result;
    }
    private static float FootToeInset(Vector3 point,AnimalFootFrame frame,bool wolf)
    {
        float x=point.X-frame.CenterX;
        float narrow=wolf?.0038f:.0065f;
        float Cleft(float center)=>MathF.Exp(-MathF.Pow((x-center)/narrow,2));
        float cleft=wolf?Cleft(-frame.Width*.25f)+Cleft(0)+Cleft(frame.Width*.25f):Cleft(0);
        float front=1-Mathf.SmoothStep(frame.Minimum.Z+.025f,frame.Minimum.Z+(wolf?.080f:.065f),point.Z);
        float low=1-Mathf.SmoothStep(.035f,wolf?.085f:.090f,point.Y);
        return cleft*front*low*(wolf?.008f:.012f);
    }
    private static void SplitHindHoofCrests(Godot.Collections.Array arrays,AnimalFootFrame[] frames)
    {
        var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().ToList();var n=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array().ToList();
        var uv=arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array().ToList();var uv2=arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array().ToList();
        var colours=arrays[(int)Mesh.ArrayType.Color].AsColorArray().ToList();var bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array().ToList();
        var weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array().ToList();var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        var sourceBones=bones.ToArray();var sourceWeights=weights.ToArray();var cuts=new Dictionary<(int,int),int>();
        int Cut(int a,int b)
        {
            var key=a<b?(a,b):(b,a);if(cuts.TryGetValue(key,out int cached))return cached;
            int foot=AuthoredFoot(a,sourceBones,sourceWeights);
            if(foot<2||foot!=AuthoredFoot(b,sourceBones,sourceWeights))return -1;
            var frame=frames[foot];var pa=p[a];var pb=p[b];
            if(pa.Y>.045f||pb.Y>.045f||MathF.Max(pa.Z,pb.Z)>frame.Minimum.Z+.035f||(pa.X-frame.CenterX)*(pb.X-frame.CenterX)>=0)return -1;
            float t=(frame.CenterX-pa.X)/(pb.X-pa.X);var hit=pa.Lerp(pb,t);
            if(t<.05f||t>.95f||hit.Y<.008f||hit.Y>.034f||hit.Z>frame.Minimum.Z+.020f)return -1;
            int index=p.Count;p.Add(hit);n.Add(n[a].Lerp(n[b],t).Normalized());uv.Add(uv[a].Lerp(uv[b],t));uv2.Add(uv2[a].Lerp(uv2[b],t));colours.Add(colours[a].Lerp(colours[b],t));
            var influence=new Dictionary<int,float>();
            foreach(var (v,amount) in new[]{(a,1-t),(b,t)})for(int k=0;k<4;k++)
            {int bone=sourceBones[v*4+k];influence.TryGetValue(bone,out float value);influence[bone]=value+sourceWeights[v*4+k]*amount;}
            var top=influence.OrderByDescending(pair=>pair.Value).ThenBy(pair=>pair.Key).Take(4).ToArray();float total=top.Sum(pair=>pair.Value);
            for(int k=0;k<4;k++){bones.Add(k<top.Length?top[k].Key:0);weights.Add(k<top.Length?top[k].Value/total:0);}
            cuts.Add(key,index);return index;
        }
        var result=new List<int>(indices.Length+96);
        void Triangle(int a,int b,int c){result.Add(a);result.Add(b);result.Add(c);}
        for(int i=0;i<indices.Length;i+=3)
        {
            int a=indices[i],b=indices[i+1],c=indices[i+2],ab=Cut(a,b),bc=Cut(b,c),ca=Cut(c,a);
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
        int added=(result.Count-indices.Length)/3;
        if(added>32||added<8)throw new InvalidOperationException("Hind hoof support topology exceeds its complete-animal budget: "+added);
        arrays[(int)Mesh.ArrayType.Vertex]=p.ToArray();arrays[(int)Mesh.ArrayType.Normal]=n.ToArray();arrays[(int)Mesh.ArrayType.TexUV]=uv.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV2]=uv2.ToArray();arrays[(int)Mesh.ArrayType.Color]=colours.ToArray();arrays[(int)Mesh.ArrayType.Bones]=bones.ToArray();
        arrays[(int)Mesh.ArrayType.Weights]=weights.ToArray();arrays[(int)Mesh.ArrayType.Index]=result.ToArray();
        GD.Print($"HOOF_CONTOUR_SUPPORT: {cuts.Count} source-edge samples, +{added} triangles, existing UV seams and lower-leg weights retained");
    }
    private void RefineAuthoredFeet(Godot.Collections.Array arrays,bool wolf,string material)
    {
        if(material!=(wolf?"Wolf":"Body"))return;
        var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var n=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var uv2=arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();var colour=arrays[(int)Mesh.ArrayType.Color].AsColorArray();
        var bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();var weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
        var low=new Vector3[4];var high=new Vector3[4];var count=new int[4];
        for(int foot=0;foot<4;foot++){low[foot]=Vector3.One*float.PositiveInfinity;high[foot]=Vector3.One*float.NegativeInfinity;}
        for(int i=0;i<p.Length;i++)
        {
            int foot=AuthoredFoot(i,bones,weights);if(foot<0||p[i].Y>.045f)continue;
            low[foot]=low[foot].Min(p[i]);high[foot]=high[foot].Max(p[i]);count[foot]++;
        }
        var frames=new AnimalFootFrame[4];
        for(int foot=0;foot<4;foot++)
        {
            frames[foot]=new(low[foot],high[foot]);
            if(count[foot]<30||frames[foot].Width<.04f||frames[foot].Width>.13f)throw new InvalidOperationException("Authored foot has no reliable source contour");
        }
        if(!wolf)
        {
            SplitHindHoofCrests(arrays,frames);
            p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();n=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            uv2=arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();colour=arrays[(int)Mesh.ArrayType.Color].AsColorArray();
            bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
        }
        for(int i=0;i<p.Length;i++)
        {
            int foot=AuthoredFoot(i,bones,weights);if(foot<0||p[i].Y>.12f)continue;
            var source=p[i];var frame=frames[foot];float inset=FootToeInset(source,frame,wolf);
            const float step=.00005f;
            float dx=(FootToeInset(source+Vector3.Right*step,frame,wolf)-FootToeInset(source-Vector3.Right*step,frame,wolf))/(2*step);
            float dy=(FootToeInset(source+Vector3.Up*step,frame,wolf)-FootToeInset(source-Vector3.Up*step,frame,wolf))/(2*step);
            float dz=1+(FootToeInset(source+Vector3.Back*step,frame,wolf)-FootToeInset(source-Vector3.Back*step,frame,wolf))/(2*step);
            if(!float.IsFinite(inset)||inset<0||inset>(wolf?.0081f:.0121f)||dz<.50f)
                throw new InvalidOperationException("Toe contour folds back or exceeds source displacement budget");
            p[i].Z+=inset;
            n[i]=new Vector3(n[i].X-dx*n[i].Z/dz,n[i].Y-dy*n[i].Z/dz,n[i].Z/dz).Normalized();
            // This opaque shader never uses vertex alpha as transparency.
            // RGB remains the nose/hoof/ear contract. Only foot UV2 is changed;
            // deer eye UV2 and all non-foot material anchors stay untouched.
            colour[i].A=Mathf.SmoothStep(.040f,.090f,source.Y);
            uv2[i]=new(source.X-frame.CenterX,source.Z-frame.Minimum.Z);
        }
        arrays[(int)Mesh.ArrayType.Vertex]=p;arrays[(int)Mesh.ArrayType.Normal]=n;
        arrays[(int)Mesh.ArrayType.TexUV2]=uv2;arrays[(int)Mesh.ArrayType.Color]=colour;
        _authoredFootFrames[wolf?1:0]=frames;
    }

    private void ValidateAuthoredFeet(AnimalRig rig,bool wolf)
    {
        var frames=_authoredFootFrames[wolf?1:0]??throw new InvalidOperationException("Missing authored foot contours");
        var mesh=rig.FurSkin!.Mesh;var a=mesh.SurfaceGetArrays(0);var p=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var n=a[(int)Mesh.ArrayType.Normal].AsVector3Array();var uv2=a[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
        var colour=a[(int)Mesh.ArrayType.Color].AsColorArray();var bones=a[(int)Mesh.ArrayType.Bones].AsInt32Array();
        var weights=a[(int)Mesh.ArrayType.Weights].AsFloat32Array();var indices=a[(int)Mesh.ArrayType.Index].AsInt32Array();
        var lowest=new[]{float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity};
        var coverage=new int[4];float largest=0;
        for(int i=0;i<p.Length;i++)
        {
            int foot=AuthoredFoot(i,bones,weights);if(foot<0||p[i].Y>.09f)continue;
            var frame=frames[foot];var original=new Vector3(frame.CenterX+uv2[i].X,p[i].Y,frame.Minimum.Z+uv2[i].Y);
            var expected=original+Vector3.Back*FootToeInset(original,frame,wolf);
            if(p[i].DistanceTo(expected)>.000025f||!n[i].IsFinite()||MathF.Abs(n[i].Length()-1)>.01f)
                throw new InvalidOperationException("Final skinned foot loses source anchors or transformed normals");
            if(MathF.Abs(colour[i].A-Mathf.SmoothStep(.040f,.090f,p[i].Y))>.0041f)
                throw new InvalidOperationException("Final foot loses its opaque surface mask");
            lowest[foot]=MathF.Min(lowest[foot],p[i].Y);coverage[foot]++;largest=MathF.Max(largest,p[i].Z-original.Z);
        }
        int rays=0;float minimumCleft=float.PositiveInfinity;
        for(int foot=0;foot<4;foot++)
        {
            var frame=frames[foot];
            if(coverage[foot]<30||MathF.Abs(lowest[foot]-frame.Minimum.Y)>.000002f)
                throw new InvalidOperationException("Foot refinement changes source sole contact or loses a limb");
            float Front(float x)
            {
                const float y=.018f;float front=float.PositiveInfinity;
                for(int k=0;k<indices.Length;k+=3)
                {
                    var v=p[indices[k]];var b=p[indices[k+1]];var c=p[indices[k+2]];
                    if(v.X<frame.Minimum.X-.01f||v.X>frame.Maximum.X+.01f||v.Y>.10f)continue;
                    var ab=new Vector2(b.X-v.X,b.Y-v.Y);var ac=new Vector2(c.X-v.X,c.Y-v.Y);float determinant=ab.Cross(ac);
                    if(MathF.Abs(determinant)<1e-10f)continue;
                    var delta=new Vector2(x-v.X,y-v.Y);float s=delta.Cross(ac)/determinant,t=ab.Cross(delta)/determinant;
                    if(s<-.00001f||t<-.00001f||s+t>1.00001f)continue;
                    float z=v.Z+(b.Z-v.Z)*s+(c.Z-v.Z)*t;
                    if(z>=frame.Minimum.Z-.02f&&z<=frame.Minimum.Z+.10f)front=MathF.Min(front,z);
                }
                rays++;if(!float.IsFinite(front))throw new InvalidOperationException("Final foot contour ray misses an authored toe");return front;
            }
            foreach(float offset in wolf?new[]{-.25f,0f,.25f}:new[]{0f})
            {
                float center=frame.CenterX+frame.Width*offset;
                float half=frame.Width*(wolf?.125f:.24f);
                float cleft=Front(center)-(Front(center-half)+Front(center+half))*.5f;
                if(cleft<.0010f)throw new InvalidOperationException($"Final foot has no readable cleft: wolf={wolf}, foot={foot}, offset={offset}, depth={cleft}");
                minimumCleft=MathF.Min(minimumCleft,cleft);
            }
        }
        // GenerateLods changes index buffers only. The cached full vertex
        // buffer already contains these deformed points, UVs and weights;
        // there is no undeformed far-foot mesh to swap back to.
        GD.Print($"ANIMAL_FEET_PASS: {(wolf?"wolf":"deer")}, four source-bound feet, {rays} final mesh contour rays, minimum cleft {minimumCleft*1000:0.0} mm, maximum local inset {largest*1000:0.0} mm; source sole heights, opaque masks and index-only LOD contract retained");
    }
}
