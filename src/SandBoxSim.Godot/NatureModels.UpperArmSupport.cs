using System;
using System.Text.Json;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private readonly float[][] _upperArmSupport=new float[2][];
    private void FitUpperArmSupport(Vector3[] points,Vector2[] pattern,float[] distance)
    {
        int frame=BodyFrame==1?1:0;
        if(_upperArmSupport[frame]==null)
        {
            string name=frame==0?"male":"female";
            string raw=FileAccess.GetFileAsString("res://assets/models/human/upper-arm-"+name+".json").Replace("\r\n","\n",StringComparison.Ordinal);
            string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
            string expected=frame==0?"c8cfe4424a6c4510b62f645595089f29b4a8cb153b719fb8b0c4d9dae8179a0a":"9714ed80fad5832dc17a11870558e1494e1899e3f5817ef2439c8ba3fbe0f4e6";
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
        var radii=_upperArmSupport[frame];float shoulder=ResidentRig.BodyY(1.18f)-(frame==1?.014f:.009f);
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
            float change=Math.Clamp(anatomical+.007f-radius,-.012f,.012f)*weight;
            float scale=(radius+change)/radius;
            points[i]=new(centre+(p.X-centre)*scale,p.Y,p.Z*scale);
        }
    }
}
