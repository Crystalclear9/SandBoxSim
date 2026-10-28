using System;
using System.Text.Json;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private float[]? _torsoRadii;
    private Vector3 AnatomicalGarmentPoint(Vector3 original,float angle,float fold)
    {
        float y=ResidentRig.BodyY(original.Y);
        float blend=Mathf.SmoothStep(1.04f,1.115f,y)*(1-Mathf.SmoothStep(1.285f,1.315f,y));
        if(blend==0)return original;
        if(_torsoRadii==null)
        {
            string raw=FileAccess.GetFileAsString("res://assets/models/human/torso-cage.json");
            string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
            if(hash!="10bb1a49edc0ad2b21da16048dcd650662d61b680c4b7d4b4b9be5e2cc741b58")throw new InvalidOperationException("Changed anatomical torso support cage");
            using var doc=JsonDocument.Parse(raw);var data=doc.RootElement;
            if(data.GetProperty("schemaVersion").GetInt32()!=1||data.GetProperty("rows").GetInt32()!=65||data.GetProperty("columns").GetInt32()!=128)
                throw new InvalidOperationException("Invalid anatomical torso support cage");
            _torsoRadii=new float[65*128];int i=0;
            foreach(var value in data.GetProperty("radii").EnumerateArray())
            {
                float sample=value.GetSingle();
                if(i>=_torsoRadii.Length||!float.IsFinite(sample)||sample<.025f||sample>.34f)throw new InvalidOperationException("Invalid anatomical chest radius");
                _torsoRadii[i++]=sample;
            }
            if(i!=_torsoRadii.Length)throw new InvalidOperationException("Incomplete anatomical torso support cage");
        }
        float row=Math.Clamp((y-1.02f)/.005f,0,64),column=Mathf.PosMod(angle,MathF.Tau)/MathF.Tau*128;
        int r=Math.Min(63,(int)row),c=(int)column%128;
        float Sample(int h)=>Mathf.Lerp(_torsoRadii[h*128+c],_torsoRadii[h*128+(c+1)%128],column-MathF.Floor(column));
        float radius=Mathf.Lerp(Sample(r),Sample(r+1),row-r)+.007f+(_garmentCut==2?.004f:0);
        radius*=1+(fold-1)*.35f;
        var supported=new Vector3(MathF.Cos(angle)*radius,original.Y,MathF.Sin(angle)*radius);
        if(supported.Z<0)supported.Z=Mathf.Lerp(supported.Z,MathF.Min(supported.Z,original.Z),Mathf.SmoothStep(1.22f,1.28f,y));
        return original.Lerp(supported,blend);
    }
}
