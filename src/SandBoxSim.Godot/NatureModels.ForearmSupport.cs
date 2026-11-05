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
        if(hash!=(frame==1?"b1fdb320bc777f36fb34fbd0ea14c4018593e3b67b9edd55f59095fbed78c2cb":"ab04d2760b389d4219f27ab9910af48772fbfa321402f106294f66c97cae2004"))throw new InvalidOperationException("Changed forearm support profile");
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
        // Interpolate source rows with matching value, slope and zero
        // curvature at both ends. The former cubic was only C1: every 5 mm
        // station could change surface curvature abruptly during rotation.
        float c3=10*(b-a)-6*m-4*n,c4=-15*(b-a)+8*m+7*n,c5=6*(b-a)-3*m-3*n;
        float value=a+m*t+c3*t*t*t+c4*t*t*t*t+c5*t*t*t*t*t;
        float derivative=(m+3*c3*t*t+4*c4*t*t*t+5*c5*t*t*t*t)/.005f;
        return(value,derivative);
    }
    private static (Vector3 Point,Vector3 Normal) FitForearmSupport(Vector3 point,Vector3 normal,int frame)
    {
        if(point.Y<=.005f||point.Y>=.290f)return(point,normal);
        var fit=ForearmSupport(frame);
        (float V,float D) Ramp(float low,float high)
        {
            float t=Math.Clamp((point.Y-low)/(high-low),0,1);
            return(t*t*t*(10+t*(-15+6*t)),30*t*t*(1-t)*(1-t)/(high-low));
        }
        var rise=Ramp(.005f,.035f);var fall=Ramp(.275f,.290f);
        float w=rise.V*(1-fall.V),dw=rise.D*(1-fall.V)-rise.V*fall.D;
        var x=ForearmProfileSample(fit.X,point.Y);var z=ForearmProfileSample(fit.Z,point.Y);
        var ox=ForearmProfileSample(fit.OffsetX,point.Y);var oz=ForearmProfileSample(fit.OffsetZ,point.Y);
        float sx=1+(x.Value-1)*w,sz=1+(z.Value-1)*w;
        float dx=x.Derivative*w+(x.Value-1)*dw,dz=z.Derivative*w+(z.Value-1)*dw;
        float tx=ox.Value*w,tz=oz.Value*w,dtx=ox.Derivative*w+ox.Value*dw,dtz=oz.Derivative*w+oz.Value*dw;
        return(new(point.X*sx+tx,point.Y,point.Z*sz+tz),
            new Vector3(normal.X/sx,normal.Y-normal.X*(point.X*dx+dtx)/sx-normal.Z*(point.Z*dz+dtz)/sz,normal.Z/sz).Normalized());
    }
    private static void ValidateForearmProfileContinuity()
    {
        int samples=0;float maximumCurvatureJump=0;
        for(int frame=0;frame<2;frame++)
        {
            var fit=ForearmSupport(frame);
            foreach(var values in new[]{fit.X,fit.Z,fit.OffsetX,fit.OffsetZ})
            {
                for(int row=0;row<48;row++)for(int step=0;step<=32;step++)
                {
                    float y=.005f+(row+step/32f)*.005f;var sample=ForearmProfileSample(values,y);
                    if(!float.IsFinite(sample.Value)||!float.IsFinite(sample.Derivative)
                        ||sample.Value<MathF.Min(values[row],values[row+1])-.00001f
                        ||sample.Value>MathF.Max(values[row],values[row+1])+.00001f)
                        throw new InvalidOperationException("Forearm interpolation overshoots its source section or is nonfinite");
                    samples++;
                }
                for(int row=1;row<48;row++)
                {
                    float y=.005f+row*.005f,range=MathF.Max(MathF.Abs(values[row]-values[row-1]),MathF.Abs(values[row+1]-values[row]));
                    if(range<.000001f)continue;const float epsilon=.000001f;
                    float left=ForearmProfileSample(values,y-epsilon).Derivative,mid=ForearmProfileSample(values,y).Derivative,right=ForearmProfileSample(values,y+epsilon).Derivative;
                    float jump=MathF.Abs(left+right-2*mid)*(.005f*.005f/epsilon)/range;
                    maximumCurvatureJump=MathF.Max(maximumCurvatureJump,jump);
                }
            }
        }
        if(maximumCurvatureJump>.20f)throw new InvalidOperationException($"Forearm source sections have a curvature break: {maximumCurvatureJump}");
        GD.Print($"FOREARM_PROFILE_FLOW_PASS: {samples} source-profile samples, no overshoot, normalized curvature-limit residual={maximumCurvatureJump:F5}");
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
