using System;
using System.Text.Json;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private float[]? _torsoRadii;
    private static string VerifiedTorsoText(string raw)
    {
        // Git checks text assets out as LF; older Windows exports used CRLF.
        // Ignore only that transport difference, retaining the content digest.
        string normalized=raw.Replace("\r\n","\n",StringComparison.Ordinal);
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        if(hash!="ecd8b6f42e68931caae38d48a76758f5b071b8c556ccaed0d5b58c92aa67d1db")
            throw new InvalidOperationException($"Changed anatomical torso support cage: SHA-256 {hash}");
        return normalized;
    }
    private static void ValidateTorsoTextContract()
    {
        string raw=FileAccess.GetFileAsString("res://assets/models/human/torso-cage.json");
        string lf=VerifiedTorsoText(raw),crlf=VerifiedTorsoText(lf.Replace("\n","\r\n",StringComparison.Ordinal));
        if(lf!=crlf)throw new InvalidOperationException("Torso cage depends on checkout line endings");
        // A syntactically valid geometry change must still fail the digest.
        using var document=JsonDocument.Parse(lf);
        string radius=document.RootElement.GetProperty("radii")[0].GetRawText();
        int start=lf.IndexOf("\"radii\":[",StringComparison.Ordinal)+"\"radii\":[".Length;
        string changed=lf.Remove(start,radius.Length).Insert(start,"0.1234567");
        bool rejected=false;
        try{VerifiedTorsoText(changed);}catch(InvalidOperationException){rejected=true;}
        if(!rejected)throw new InvalidOperationException("Modified torso geometry passed integrity validation");
        GD.Print("TORSO_TEXT_PASS: LF/CRLF equivalence, changed geometry rejected by the runtime digest");
    }
    private Vector3 AnatomicalGarmentPoint(Vector3 original,float angle,float fold)
    {
        float y=ResidentRig.BodyY(original.Y);
        float blend=Mathf.SmoothStep(.98f,1.09f,y)*(1-Mathf.SmoothStep(1.285f,1.315f,y));
        if(blend==0)return original;
        if(_torsoRadii==null)
        {
            string raw=VerifiedTorsoText(FileAccess.GetFileAsString("res://assets/models/human/torso-cage.json"));
            using var doc=JsonDocument.Parse(raw);var data=doc.RootElement;
            if(data.GetProperty("schemaVersion").GetInt32()!=1||data.GetProperty("rows").GetInt32()!=65||data.GetProperty("columns").GetInt32()!=128)
                throw new InvalidOperationException("Invalid anatomical torso support cage");
            var radii=new float[65*128];int i=0;
            foreach(var value in data.GetProperty("radii").EnumerateArray())
            {
                float sample=value.GetSingle();
                if(i>=radii.Length||!float.IsFinite(sample)||sample<.025f||sample>.34f)throw new InvalidOperationException("Invalid anatomical chest radius");
                radii[i++]=sample;
            }
            if(i!=radii.Length)throw new InvalidOperationException("Incomplete anatomical torso support cage");
            _torsoRadii=radii;
        }
        float row=Math.Clamp((y-1.02f)/.005f,0,64),column=Mathf.PosMod(angle,MathF.Tau)/MathF.Tau*128;
        int r=Math.Min(63,(int)row),c=(int)column%128;
        float Sample(int h)=>Mathf.Lerp(_torsoRadii[h*128+c],_torsoRadii[h*128+(c+1)%128],column-MathF.Floor(column));
        // Woven cloth hangs away from the abdomen. Ease continues below the
        // pectorals rather than collapsing back onto the old elliptical waist.
        float abdominalEase=.006f*Mathf.SmoothStep(.98f,1.04f,y)*(1-Mathf.SmoothStep(1.13f,1.20f,y));
        float radius=Mathf.Lerp(Sample(r),Sample(r+1),row-r)+.008f+abdominalEase+(_garmentCut==2?.004f:0);
        radius*=1+(fold-1)*.35f;
        var supported=new Vector3(MathF.Cos(angle)*radius,original.Y,MathF.Sin(angle)*radius);
        if(supported.Z<0)supported.Z=Mathf.Lerp(supported.Z,MathF.Min(supported.Z,original.Z),Mathf.SmoothStep(1.22f,1.28f,y));
        return original.Lerp(supported,blend);
    }
}
