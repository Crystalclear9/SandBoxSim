using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private Vector3 FramedHairPoint(Vector3 p)
    {
        if(ChildHead||HumanFamily!=2)return HumanPoint(p);
        float w=Mathf.SmoothStep(.025f,.065f,-p.Z)*Mathf.SmoothStep(.045f,.070f,p.Y)
            *(1-Mathf.SmoothStep(.115f,.145f,p.Y))*(1-Mathf.SmoothStep(.060f,.090f,MathF.Abs(p.X)));
        if(w<=0)return HumanPoint(p);
        var data=HumanData();
        (float Center,float Radius) Section(float y,float angle)
        {
            float row=Math.Clamp((y-data.Low)/(data.High-data.Low)*data.Rows-.5f,0,data.Rows-1);
            int i=(int)row,j=Math.Min(data.Rows-1,i+1);float t=row-i;
            float col=Mathf.PosMod(angle,MathF.Tau)/MathF.Tau*data.Columns;int a=(int)col%data.Columns,b=(a+1)%data.Columns;
            return(Mathf.Lerp(data.Centers[i],data.Centers[j],t),Mathf.Lerp(
                Mathf.Lerp(data.Radii[i*data.Columns+a],data.Radii[i*data.Columns+b],col-MathF.Floor(col)),
                Mathf.Lerp(data.Radii[j*data.Columns+a],data.Radii[j*data.Columns+b],col-MathF.Floor(col)),t));
        }
        var centre=Section(p.Y,0).Center;float angle=MathF.Atan2(p.Z-centre,p.X);
        var original=Section(p.Y,angle);float radius=new Vector2(p.X,p.Z-original.Center).Length();
        float height=p.Y-.005f*w;var lower=Section(height,angle);
        float fitted=lower.Radius+(radius-original.Radius);
        return HumanPoint(new(MathF.Cos(angle)*fitted,height,lower.Center+MathF.Sin(angle)*fitted));
    }
    private Vector3 FramedHairNormal(Vector3 p,Vector3 n)
    {
        const float e=.0001f;
        var x=(FramedHairPoint(p+Vector3.Right*e)-FramedHairPoint(p-Vector3.Right*e))/(2*e);
        var y=(FramedHairPoint(p+Vector3.Up*e)-FramedHairPoint(p-Vector3.Up*e))/(2*e);
        var z=(FramedHairPoint(p+Vector3.Back*e)-FramedHairPoint(p-Vector3.Back*e))/(2*e);
        return (new Basis(x,y,z).Inverse().Transposed()*n).Normalized();
    }
    private void ValidateHairFraming()
    {
        var source=HumanData().Hair;
        for(int i=0;i<source.Points.Length;i+=19)
        {
            var p=source.Points[i];var q=FramedHairPoint(p);var old=HumanPoint(p);
            if(!q.IsFinite()||q.DistanceTo(old)>.009f||!FramedHairNormal(p,source.Normals[i]).IsFinite())
                throw new InvalidOperationException("Hair framing loses its bounded source surface");
            if((ChildHead||HumanFamily!=2||p.Z>=-.025f||p.Y<=.045f)&&q!=old)
                throw new InvalidOperationException("Hair framing changes a protected source region");
            if(q.Y>old.Y+.000001f||q.Y<old.Y-.005001f)throw new InvalidOperationException("Hair framing exceeds its frontal height fit");
        }
    }
}
