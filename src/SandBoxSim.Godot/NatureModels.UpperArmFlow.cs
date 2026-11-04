using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    // A positive, periodic cubic B-spline keeps the source's anatomical shape
    // while removing the tangent breaks of bilinear radius sampling. The
    // endpoint remap joins the clamped source rows with zero slope/curvature.
    private static float UpperArmRadius(float[] radii,float row,float column)
    {
        static float End(float t)=>t*t*t*(6+t*(-8+3*t));
        row=Math.Clamp(row,0,24);
        if(row<1)row=End(row);else if(row>23)row=24-End(24-row);
        column=Mathf.PosMod(column,64);
        int r=(int)MathF.Floor(row),c=(int)MathF.Floor(column);
        Span<float> rw=stackalloc float[4],cw=stackalloc float[4];
        static void Weights(float t,Span<float> w)
        {
            float s=1-t;
            w[0]=s*s*s/6;w[1]=(3*t*t*t-6*t*t+4)/6;
            w[2]=(-3*t*t*t+3*t*t+3*t+1)/6;w[3]=t*t*t/6;
        }
        Weights(row-r,rw);Weights(column-c,cw);
        float result=0;
        for(int i=0;i<4;i++)for(int j=0;j<4;j++)
            result+=radii[Math.Clamp(r+i-1,0,24)*64+(c+j+63)%64]*rw[i]*cw[j];
        return result;
    }

    private void ValidateUpperArmFlow()
    {
        var radii=UpperArmRadii();float low=float.PositiveInfinity,high=0,maxTangent=0;int samples=0;
        foreach(float r in radii){low=MathF.Min(low,r);high=MathF.Max(high,r);}
        const float h=.001f;
        for(int row=0;row<=24;row++)for(int column=0;column<64;column++)
        {
            float At(float r,float c)=>UpperArmRadius(radii,r,c);
            float centre=At(row,column);
            float rowBreak=MathF.Abs((At(row+h,column)-centre)/h-(centre-At(row-h,column))/h);
            float colBreak=MathF.Abs((At(row,column+h)-centre)/h-(centre-At(row,column-h))/h);
            maxTangent=MathF.Max(maxTangent,MathF.Max(rowBreak,colBreak));
            if(!float.IsFinite(centre)||centre<low-.000001f||centre>high+.000001f
                ||MathF.Abs(At(row,column)-At(row,column+64))>.000001f)
                throw new InvalidOperationException("Upper-arm source flow overshoots or loses its periodic seam");
            samples++;
        }
        if(maxTangent>.00015f)throw new InvalidOperationException("Upper-arm source flow has a tangent discontinuity");
        GD.Print($"UPPER_ARM_SOURCE_FLOW_PASS: frame={BodyFrame}, samples={samples}, maximum tangent residual={maxTangent:F8} m/grid unit; bounded periodic source, fixed cuff fit");
    }
}
