using System;
using System.Text.Json;
using Godot;

namespace SandBoxSim.Client;

internal sealed class ResidentGaitCapture
{
    private const int Samples=48,SupportSamples=28,SwingSamples=Samples-SupportSamples+1;
    internal const string Version="cmu-contact-curves-v2";
    private const string Digest="23081d908777f9f69dd8fde5d0e85ec6f763b148f20ffd7dee36df5230271698";
    internal static readonly ResidentGaitCapture Shared=new();

    private sealed record Clip(Vector3[] Pelvis,Vector3[] Chest,Vector3[] WeightShift,Vector3[][] Arms,
        Vector3[][] Swing,float[][] SolePitch);
    private readonly Clip[] _clips=new Clip[2];

    private ResidentGaitCapture()
    {
        string raw=FileAccess.GetFileAsString("res://assets/motions/cmu-walk-cycles.json").Replace("\r\n","\n",StringComparison.Ordinal);
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));
        if(!hash.Equals(Digest,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Changed CMU gait asset");
        using var document=JsonDocument.Parse(raw);
        var asset=document.RootElement;
        var clips=asset.GetProperty("clips");
        if(asset.GetProperty("schemaVersion").GetInt32()!=2||asset.GetProperty("cycleSamples").GetInt32()!=Samples||
            asset.GetProperty("supportSamples").GetInt32()!=SupportSamples||clips.GetArrayLength()!=2)
            throw new InvalidOperationException("Invalid CMU gait contract");
        for(int style=0;style<_clips.Length;style++)
        {
            var source=clips[style];
            var pelvis=new Vector3[Samples];var chest=new Vector3[Samples];
            var shift=new Vector3[Samples];
            var arms=new[]{new Vector3[Samples],new Vector3[Samples]};
            var swing=new[]{new Vector3[SwingSamples],new Vector3[SwingSamples]};
            var pitch=new[]{new float[Samples],new float[Samples]};
            var body=source.GetProperty("body");var arm=source.GetProperty("arms");
            var height=source.GetProperty("swingHeight");var path=source.GetProperty("swingPath");
            var rocker=source.GetProperty("solePitch");
            for(int i=0;i<Samples;i++)
            {
                var row=body[i];
                pelvis[i]=new(row[0].GetSingle(),row[1].GetSingle(),row[2].GetSingle());
                chest[i]=new(row[3].GetSingle(),row[4].GetSingle(),row[5].GetSingle());
                shift[i]=new(row[6].GetSingle(),row[7].GetSingle(),row[8].GetSingle());
                for(int side=0;side<2;side++)
                {
                    var channel=arm[side][i];
                    arms[side][i]=new(channel[0].GetSingle(),channel[1].GetSingle(),channel[2].GetSingle());
                    pitch[side][i]=rocker[side][i].GetSingle();
                }
            }
            for(int side=0;side<2;side++)
                for(int i=0;i<SwingSamples;i++)
                    swing[side][i]=new(path[side][i][0].GetSingle(),height[side][i].GetSingle(),path[side][i][1].GetSingle());
            _clips[style]=new(pelvis,chest,shift,arms,swing,pitch);
        }
    }

    internal (Vector3 Pelvis,Vector3 Chest,Vector3 Shift) Body(float phase,float style)
    {
        var (i,weight)=Cycle(phase);
        return (Curve(_clips[0].Pelvis,i,weight).Lerp(Curve(_clips[1].Pelvis,i,weight),style),
            Curve(_clips[0].Chest,i,weight).Lerp(Curve(_clips[1].Chest,i,weight),style),
            Curve(_clips[0].WeightShift,i,weight).Lerp(Curve(_clips[1].WeightShift,i,weight),style));
    }

    internal Vector3 Arm(int side,float phase,float support,float style)
    {
        var (i,weight)=ContactPhase(phase,support);
        return Curve(_clips[0].Arms[side],i,weight).Lerp(Curve(_clips[1].Arms[side],i,weight),style);
    }

    internal float SolePitch(int side,float phase,float support,float style)
    {
        var (i,weight)=ContactPhase(phase,support);
        return Mathf.Lerp(Curve(_clips[0].SolePitch[side],i,weight),Curve(_clips[1].SolePitch[side],i,weight),style);
    }

    private static (int Index,float Weight) ContactPhase(float phase,float support)
    {
        float split=SupportSamples/(float)Samples;
        // Keep the heel-strike and toe-off samples in sync without a sharp
        // change in time derivative at the boundary.
        float shape=MathF.Sin(phase*MathF.PI)/MathF.Sin(support*MathF.PI);
        return Cycle(phase+(split-support)*shape*shape);
    }

    // X is forward progress, Y is normalized lift, Z is lateral residual.
    internal Vector3 Swing(int side,float phase,float style)
    {
        float frame=Math.Clamp(phase,0,1)*(SwingSamples-1);
        int index=Math.Min(SwingSamples-2,(int)frame);
        float weight=frame-index;
        Vector3 At(Clip clip)
        {
            var a=clip.Swing[side];
            return a[index].CubicInterpolate(a[index+1],a[Math.Max(0,index-1)],a[Math.Min(a.Length-1,index+2)],weight);
        }
        return At(_clips[0]).Lerp(At(_clips[1]),style);
    }

    private static (int Index,float Weight) Cycle(float phase)
    {
        float frame=Mathf.PosMod(phase,1)*Samples;
        int i=Math.Min(Samples-1,(int)frame);
        return (i,frame-i);
    }

    private static Vector3 Curve(Vector3[] values,int i,float weight)=>values[i].CubicInterpolate(
        values[(i+1)%Samples],values[(i+Samples-1)%Samples],values[(i+2)%Samples],weight);

    private static float Curve(float[] values,int i,float weight)=>Mathf.CubicInterpolate(values[i],
        values[(i+1)%Samples],values[(i+Samples-1)%Samples],values[(i+2)%Samples],weight);
}
