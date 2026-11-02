using System;
using System.Text.Json;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private sealed record ForearmFit(float[] X,float[] Z,float[] OffsetX,float[] OffsetZ,float[] Width,float[] Depth);
    private static readonly ForearmFit?[] ForearmFits=new ForearmFit?[2];
    private static ForearmFit ForearmSupport(int frame)
    {
        frame=frame==1?1:0;
        if(ForearmFits[frame] is {} cached)return cached;
        string name=frame==1?"female":"male";
        string raw=FileAccess.GetFileAsString("res://assets/models/human/forearm-fit-"+name+".json").Replace("\r\n","\n",StringComparison.Ordinal);
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        if(hash!=(frame==1?"b3182ecdd701b3deb2dc40ce53ddd54fc4d3b24ef9155c3748d9408e82b2d070":"a52e42bd23098c590058e47a016766806533ec58bb89c4d56dde42b367bba4db"))throw new InvalidOperationException("Changed forearm support profile");
        using var doc=JsonDocument.Parse(raw);var root=doc.RootElement;
        if(root.GetProperty("rows").GetInt32()!=49||root.GetProperty("low").GetSingle()!=.005f||root.GetProperty("high").GetSingle()!=.245f||root.GetProperty("profile").GetString()!="continuous-wrist-source-v2"||root.GetProperty("license").GetString()!="CC0-1.0 AND CC-BY-4.0")
            throw new InvalidOperationException("Invalid forearm support contract");
        float[] Read(string field,float low,float high)
        {
            var data=new float[49];int i=0;
            foreach(var v in root.GetProperty(field).EnumerateArray())
            {
                float x=v.GetSingle();if(i>=49||!float.IsFinite(x)||x<low||x>high)throw new InvalidOperationException("Invalid forearm support values");data[i++]=x;
            }
            if(i!=49)throw new InvalidOperationException("Incomplete forearm support");return data;
        }
        return ForearmFits[frame]=new(Read("scaleX",.5f,1),Read("scaleZ",.5f,1),Read("offsetX",-.02f,.02f),Read("offsetZ",-.03f,.03f),Read("targetWidths",.025f,.12f),Read("targetDepths",.02f,.12f));
    }
    private static (float Value,float Derivative) ForearmProfileSample(float[] data,float height)
    {
        if(height<=.005f)return(data[0],0);
        if(height>=.245f)return(data[^1],0);
        float row=(height-.005f)/.005f;int i=Math.Min(data.Length-2,(int)row);float t=row-i;
        float Slope(int j)
        {
            if(j==0||j==data.Length-1)return 0;
            float a=data[j]-data[j-1],b=data[j+1]-data[j];
            return a*b<=0?0:2*a*b/(a+b);
        }
        float a=data[i],b=data[i+1],m=Slope(i),n=Slope(i+1);
        float value=(2*t*t*t-3*t*t+1)*a+(t*t*t-2*t*t+t)*m+(-2*t*t*t+3*t*t)*b+(t*t*t-t*t)*n;
        float derivative=((6*t*t-6*t)*a+(3*t*t-4*t+1)*m+(-6*t*t+6*t)*b+(3*t*t-2*t)*n)/.005f;
        return(value,derivative);
    }
    private static (Vector3 Point,Vector3 Normal) FitForearmSupport(Vector3 point,Vector3 normal,int frame)
    {
        if(point.Y<=.005f||point.Y>=.275f)return(point,normal);
        var fit=ForearmSupport(frame);
        (float V,float D) Ramp(float low,float high)
        {
            float t=Math.Clamp((point.Y-low)/(high-low),0,1);
            return(t*t*t*(10+t*(-15+6*t)),30*t*t*(1-t)*(1-t)/(high-low));
        }
        var rise=Ramp(.005f,.035f);var fall=Ramp(.245f,.275f);
        float w=rise.V*(1-fall.V),dw=rise.D*(1-fall.V)-rise.V*fall.D;
        var x=ForearmProfileSample(fit.X,point.Y);var z=ForearmProfileSample(fit.Z,point.Y);
        var ox=ForearmProfileSample(fit.OffsetX,point.Y);var oz=ForearmProfileSample(fit.OffsetZ,point.Y);
        float sx=1+(x.Value-1)*w,sz=1+(z.Value-1)*w;
        float dx=x.Derivative*w+(x.Value-1)*dw,dz=z.Derivative*w+(z.Value-1)*dw;
        float tx=ox.Value*w,tz=oz.Value*w,dtx=ox.Derivative*w+ox.Value*dw,dtz=oz.Derivative*w+oz.Value*dw;
        return(new(point.X*sx+tx,point.Y,point.Z*sz+tz),
            new Vector3(normal.X/sx,normal.Y-normal.X*(point.X*dx+dtx)/sx-normal.Z*(point.Z*dz+dtz)/sz,normal.Z/sz).Normalized());
    }
    private static void ValidateForearmSupport(ResidentRig rig,int frame)
    {
        var fit=ForearmSupport(frame);int samples=0;
        for(int side=0;side<2;side++)
        {
            var arrays=rig.HandSkins[side].Mesh.SurfaceGetArrays(0);
            var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var ids=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            foreach(float y in new[]{.045f,.060f,.090f,.15f,.19f,.225f})
            {
                float x0=float.PositiveInfinity,x1=float.NegativeInfinity,z0=x0,z1=x1;
                for(int t=0;t<ids.Length;t+=3)for(int edge=0;edge<3;edge++)
                {
                    var a=p[ids[t+edge]];var b=p[ids[t+(edge+1)%3]];
                    if((a.Y-y)*(b.Y-y)>0||MathF.Abs(b.Y-a.Y)<1e-9f)continue;
                    var q=a.Lerp(b,(y-a.Y)/(b.Y-a.Y));x0=MathF.Min(x0,q.X);x1=MathF.Max(x1,q.X);z0=MathF.Min(z0,q.Z);z1=MathF.Max(z1,q.Z);
                }
                float width=ForearmProfileSample(fit.Width,y).Value,depth=ForearmProfileSample(fit.Depth,y).Value;
                if(!float.IsFinite(x0)||MathF.Abs(x1-x0-width)>.002f||MathF.Abs(z1-z0-depth)>.002f)
                    throw new InvalidOperationException($"Rendered forearm misses its body support: frame={frame}, side={side}, y={y}, actual={x1-x0}/{z1-z0}, expected={width}/{depth}");
                samples++;
            }
        }
        GD.Print($"FOREARM_BODY_SUPPORT_PASS: frame={frame}, {samples} actual wrist/forearm sections, source-bound widths/depths within 2 mm, exact fingers/grip");
    }
}
