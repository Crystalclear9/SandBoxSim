using System;
using System.Text.Json;
using Godot;

namespace SandBoxSim.Client;

internal sealed class ResidentActionCapture
{
    internal const string Version="cmu-standing-phrases-v1";
    internal const string TaskVersion="cmu-turn-and-dig-v1";
    private const int Samples=65;
    internal static readonly ResidentActionCapture Shared=new();
    private readonly float[][][] _pickup,_social,_drink,_turn,_dig;

    internal readonly record struct PickupPose(float Bend,float Drop,float Shift,float Reach,float Balance,float Head,float Turn);
    internal readonly record struct SocialPose(Vector3 Chest,float Head,Vector3 Left,Vector3 Right);
    internal readonly record struct TurnPose(float Look,float Chest,float Roll,float Left,float Right);
    internal readonly record struct DigPose(float Bend,float Turn,float Head,float Shift);

    private ResidentActionCapture()
    {
        string raw=FileAccess.GetFileAsString("res://assets/motions/cmu-action-phrases.json").Replace("\r\n","\n",StringComparison.Ordinal);
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));
        if(!hash.Equals("de7af3e9314facccddace436bf3649aa80208a3259fbcfdf0112edb258e08911",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Changed CMU action asset");
        using var document=JsonDocument.Parse(raw);
        var root=document.RootElement;
        if(root.GetProperty("schemaVersion").GetInt32()!=1||root.GetProperty("samples").GetInt32()!=Samples)
            throw new InvalidOperationException("Invalid CMU action schema");
        _pickup=Read(root,"pickup",2,7);
        _social=Read(root,"social",2,10);
        _drink=Read(root,"drink",1,2);
        string tasks=FileAccess.GetFileAsString("res://assets/motions/cmu-task-phrases.json").Replace("\r\n","\n",StringComparison.Ordinal);
        string taskHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(tasks)));
        if(!taskHash.Equals("57c9acaa69a9494a2ab622d08de5c7ceef9864b7d73f618b7cae2aa890df7c81",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Changed CMU task motion asset");
        using var taskDocument=JsonDocument.Parse(tasks);
        var task=taskDocument.RootElement;
        if(task.GetProperty("schemaVersion").GetInt32()!=1)throw new InvalidOperationException("Invalid CMU task schema");
        _turn=Read(task,"turn",1,5);
        _dig=Read(task,"dig",2,4);
    }

    private static float[][][] Read(JsonElement root,string name,int count,int channels)
    {
        var curves=root.GetProperty(name).GetProperty("curves");
        if(curves.GetArrayLength()!=count)throw new InvalidOperationException($"Invalid {name} phrase count");
        var result=new float[count][][];
        for(int clip=0;clip<count;clip++)
        {
            var rows=curves[clip];
            if(rows.GetArrayLength()!=Samples)throw new InvalidOperationException($"Invalid {name} phrase length");
            result[clip]=new float[Samples][];
            for(int frame=0;frame<Samples;frame++)
            {
                var row=rows[frame];
                if(row.GetArrayLength()!=channels)throw new InvalidOperationException($"Invalid {name} channel count");
                result[clip][frame]=new float[channels];
                for(int channel=0;channel<channels;channel++)result[clip][frame][channel]=row[channel].GetSingle();
            }
        }
        return result;
    }

    private readonly struct Sample
    {
        private readonly float[][][] _curves;
        private readonly int _i;
        private readonly float _t,_style;
        internal Sample(float[][][] curves,float phase,float style)
        {
            _curves=curves;
            float index=Math.Clamp(phase,0,1)*(Samples-1);
            _i=Math.Min(Samples-2,(int)index);_t=index-_i;_style=style;
        }
        private float At(int clip,int channel)
        {
            var c=_curves[clip];
            return Mathf.CubicInterpolate(c[_i][channel],c[_i+1][channel],c[Math.Max(0,_i-1)][channel],
                c[Math.Min(Samples-1,_i+2)][channel],_t);
        }
        internal float Channel(int i)=>_curves.Length==1?At(0,i):Mathf.Lerp(At(0,i),At(1,i),_style);
        internal float Positive(int i)=>Math.Clamp(Channel(i),0,1);
        internal Vector3 Vector(int i)=>new(Channel(i),Channel(i+1),Channel(i+2));
    }

    internal PickupPose Pickup(float phase,float style)
    {
        var sample=new Sample(_pickup,phase,style);
        return new(sample.Positive(0),sample.Positive(1),sample.Channel(2),sample.Positive(3),
            sample.Positive(4),sample.Channel(5),sample.Channel(6));
    }

    internal SocialPose Social(float phase,float style)
    {
        var sample=new Sample(_social,phase,style);
        return new(sample.Vector(0),sample.Channel(3),sample.Vector(4),sample.Vector(7));
    }

    internal Vector2 Drink(float phase)
    {
        var sample=new Sample(_drink,phase,0);
        return new(sample.Channel(0),sample.Channel(1));
    }

    internal TurnPose Turn(float phase)
    {
        var sample=new Sample(_turn,phase,0);
        return new(sample.Channel(0),sample.Channel(1),sample.Channel(2),sample.Channel(3),sample.Channel(4));
    }

    internal DigPose Dig(float phase,float style)
    {
        var sample=new Sample(_dig,phase,style);
        return new(sample.Channel(0),sample.Channel(1),sample.Channel(2),sample.Channel(3));
    }
}
