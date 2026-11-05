using System;
using System.Text.Json;
using Godot;

namespace SandBoxSim.Client;

internal sealed class ResidentGaitCapture
{
    private const int Samples=48, SupportSamples=28;
    internal const string Version="cmu-contact-curves-v1";
    private const string Digest="39ee450b9533359b5ea8d48c460f48157b26cd8ad142741d178fa1d2376b4a5e";
    internal static readonly ResidentGaitCapture Shared=new();

    private sealed record Clip(Vector3[] Pelvis,Vector3[] Chest,Vector2[][] Arms,float[][] Lift);
    private readonly Clip[] _clips=new Clip[2];

    private ResidentGaitCapture()
    {
        string raw=FileAccess.GetFileAsString("res://assets/motions/cmu-walk-cycles.json").Replace("\r\n","\n",StringComparison.Ordinal);
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));
        if(!hash.Equals(Digest,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Changed CMU gait asset");
        using var document=JsonDocument.Parse(raw);
        var asset=document.RootElement;
        var clips=asset.GetProperty("clips");
        if(asset.GetProperty("schemaVersion").GetInt32()!=1||asset.GetProperty("cycleSamples").GetInt32()!=Samples||
            asset.GetProperty("supportSamples").GetInt32()!=SupportSamples||clips.GetArrayLength()!=2)
            throw new InvalidOperationException("Invalid CMU gait contract");
        for(int style=0;style<_clips.Length;style++)
        {
            var source=clips[style];
            var pelvis=new Vector3[Samples];var chest=new Vector3[Samples];
            var arms=new[]{new Vector2[Samples],new Vector2[Samples]};
            var lift=new[]{new float[Samples-SupportSamples+1],new float[Samples-SupportSamples+1]};
            var body=source.GetProperty("body");var arm=source.GetProperty("arms");var swing=source.GetProperty("swingHeight");
            for(int i=0;i<Samples;i++)
            {
                var row=body[i];
                pelvis[i]=new(row[0].GetSingle(),row[1].GetSingle(),row[2].GetSingle());
                chest[i]=new(row[3].GetSingle(),row[4].GetSingle(),row[5].GetSingle());
                for(int side=0;side<2;side++)arms[side][i]=new(arm[side][i][0].GetSingle(),arm[side][i][1].GetSingle());
            }
            for(int side=0;side<2;side++)
                for(int i=0;i<lift[side].Length;i++)lift[side][i]=swing[side][i].GetSingle();
            _clips[style]=new(pelvis,chest,arms,lift);
        }
    }

    internal (Vector3 Pelvis,Vector3 Chest) Body(float phase,float style)
    {
        var (i,weight)=Cycle(phase);
        return (Curve(_clips[0].Pelvis,i,weight).Lerp(Curve(_clips[1].Pelvis,i,weight),style),
            Curve(_clips[0].Chest,i,weight).Lerp(Curve(_clips[1].Chest,i,weight),style));
    }

    internal Vector2 Arm(int side,float phase,float support,float style)
    {
        float split=SupportSamples/(float)Samples;
        // Move the toe-off sample to the actual support boundary without
        // changing the phase velocity abruptly at either contact.
        float shape=MathF.Sin(phase*MathF.PI)/MathF.Sin(support*MathF.PI);
        float time=phase+(split-support)*shape*shape;
        var (i,weight)=Cycle(time);
        Vector2 At(Clip clip)
        {
            var a=clip.Arms[side];
            return a[i].CubicInterpolate(a[(i+1)%Samples],a[(i+Samples-1)%Samples],a[(i+2)%Samples],weight);
        }
        return At(_clips[0]).Lerp(At(_clips[1]),style);
    }

    internal float Lift(int side,float phase,float style)
    {
        float frame=phase*(Samples-SupportSamples);
        int index=Math.Min(Samples-SupportSamples-1,(int)frame);
        float weight=frame-index;
        float At(Clip clip)
        {
            var a=clip.Lift[side];
            return Mathf.CubicInterpolate(a[index],a[index+1],a[Math.Max(0,index-1)],a[Math.Min(a.Length-1,index+2)],weight);
        }
        return Math.Clamp(Mathf.Lerp(At(_clips[0]),At(_clips[1]),style),0,1);
    }

    private static (int Index,float Weight) Cycle(float phase)
    {
        float frame=Mathf.PosMod(phase,1)*Samples;
        int i=Math.Min(Samples-1,(int)frame);
        return (i,frame-i);
    }

    private static Vector3 Curve(Vector3[] values,int i,float weight)=>values[i].CubicInterpolate(
        values[(i+1)%Samples],values[(i+Samples-1)%Samples],values[(i+2)%Samples],weight);
}
