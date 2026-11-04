using System;
using System.Text.Json;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private readonly float[][] _upperArmSupport=new float[2][];
    private float[] UpperArmRadii()
    {
        int frame=BodyFrame==1?1:0;
        if(_upperArmSupport[frame]==null)
        {
            string name=frame==0?"male":"female";
            string raw=FileAccess.GetFileAsString("res://assets/models/human/upper-arm-"+name+".json").Replace("\r\n","\n",StringComparison.Ordinal);
            string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
            string expected=frame==0?"cd0ccc6d530516c11b42e39078386a6ea8ca6f623dfaed4df70a3401962d4669":"c0e7337035b1fa727fb269456aa47f1e5ac54fd2965d94f91809c6ae4d255449";
            if(hash!=expected)throw new InvalidOperationException("Changed upper-arm anatomical support");
            using var doc=JsonDocument.Parse(raw);var root=doc.RootElement;
            if(root.GetProperty("rows").GetInt32()!=25||root.GetProperty("columns").GetInt32()!=64||root.GetProperty("license").GetString()!="CC0-1.0")
                throw new InvalidOperationException("Invalid upper-arm support contract");
            var values=new float[25*64];int index=0;
            foreach(var value in root.GetProperty("radii").EnumerateArray())
            {
                float r=value.GetSingle();if(index>=values.Length||!float.IsFinite(r)||r<.012f||r>.105f)throw new InvalidOperationException("Invalid upper-arm radius");
                values[index++]=r;
            }
            if(index!=values.Length)throw new InvalidOperationException("Incomplete upper-arm support");
            _upperArmSupport[frame]=values;
        }
        return _upperArmSupport[frame];
    }
    private readonly Vector2[] _armCuffScales=new Vector2[6];
    private Vector2 ArmCuffScale()
    {
        int key=(BodyFrame==1?3:0)+_garmentCut;
        if(_armCuffScales[key].X>0)return _armCuffScales[key];
        var radii=UpperArmRadii();float x0=float.PositiveInfinity,x1=float.NegativeInfinity,z0=x0,z1=x1;
        for(int c=0;c<64;c++)
        {
            float a=c*MathF.Tau/64,r=radii[24*64+c];
            float x=r*MathF.Cos(a),z=r*MathF.Sin(a);x0=MathF.Min(x0,x);x1=MathF.Max(x1,x);z0=MathF.Min(z0,z);z1=MathF.Max(z1,z);
        }
        var arrays=ContinuousGarment().SurfaceGetArrays(0);var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var uv=arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
        float lowX=float.PositiveInfinity,highX=float.NegativeInfinity,lowZ=lowX,highZ=highX;
        for(int i=0;i<p.Length;i++)if(uv[i].X>.2628f)
        {
            float x=MathF.Abs(p[i].X)-.202f;lowX=MathF.Min(lowX,x);highX=MathF.Max(highX,x);lowZ=MathF.Min(lowZ,p[i].Z);highZ=MathF.Max(highZ,p[i].Z);
        }
        var scale=new Vector2((x1-x0+.014f)/(highX-lowX),(z1-z0+.014f)/(highZ-lowZ));
        if(!scale.IsFinite()||scale.X<.40f||scale.X>1.15f||scale.Y<.40f||scale.Y>1.15f)
            throw new InvalidOperationException("Source elbow does not fit the sewn cuff envelope");
        return _armCuffScales[key]=scale;
    }
    private void FitUpperArmSupport(Vector3[] points,Vector2[] pattern,float[] distance)
    {
        int frame=BodyFrame==1?1:0;var radii=UpperArmRadii();float shoulder=ResidentRig.BodyY(1.18f)-(frame==1?.014f:.009f);
        for(int i=0;i<points.Length;i++)if(pattern[i].X>0)
        {
            var p=points[i];float side=p.X<0?-1:1,centre=side*.202f*ArmWidthScale;
            float x=(p.X-centre)*side,z=p.Z,radius=MathF.Sqrt(x*x+z*z);
            if(radius<.0001f)continue;
            float angle=Mathf.PosMod(MathF.Atan2(z,x),MathF.Tau),column=angle/MathF.Tau*64;
            float row=Math.Clamp((shoulder-p.Y-.025f)/(.255f-.025f)*24,0,24);
            int r=Math.Min(23,(int)row),c=(int)column%64;float u=column-MathF.Floor(column),v=row-r;
            float At(int at)=>Mathf.Lerp(radii[at*64+c],radii[at*64+(c+1)%64],u);
            float anatomical=Mathf.Lerp(At(r),At(r+1),v);
            // Cloth bridges the anatomical envelope with 7 mm ease. Preserve
            // the shared seam, rolled cuff and source's small-scale drape.
            float weight=Mathf.SmoothStep(.012f,.080f,distance[i])*(1-Mathf.SmoothStep(.215f,.263f,pattern[i].X));
            float change=Math.Clamp(anatomical+(frame==1?.005f:.007f)-radius,-.012f,.012f)*weight;
            float scale=(radius+change)/radius;
            points[i]=new(centre+(p.X-centre)*scale,p.Y,p.Z*scale);
        }
    }
}
