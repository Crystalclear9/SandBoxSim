using System;
using System.Collections.Generic;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private readonly float[][] _clothEnvelopes=new float[6][];

    // A woven panel spans hollows between its supports. Use the convex section
    // of the attributed body, rather than reproducing every muscular groove.
    // This is a cached bind-space tailoring operation, not cloth simulation.
    private float ClothEnvelope(float[] support,float y,float angle)
    {
        int frame=BodyFrame==1?1:0;
        int key=frame*3+_garmentCut;
        if(_clothEnvelopes[key]==null)
        {
            var data=new float[TrunkRows*TrunkColumns];
            static float Cross(Vector2 a,Vector2 b,Vector2 c)=>(b-a).Cross(c-a);
            for(int row=0;row<TrunkRows;row++)
            {
                float height=TrunkLow+row*.005f;
                var ring=new List<Vector2>(TrunkColumns);
                for(int col=0;col<TrunkColumns;col++)
                {
                    float a=col*Mathf.Tau/TrunkColumns;
                    float local=BodyFrame==1?Mathf.SmoothStep(1.08f,1.12f,height)*(1-Mathf.SmoothStep(1.18f,1.23f,height))*MathF.Pow(MathF.Max(0,-MathF.Sin(a)),2):0;
                    float step=Mathf.Lerp(.015f,.0075f,local);
                    float r=(TrunkSample(support,height-2*step,a)+4*TrunkSample(support,height-step,a)+
                        6*TrunkSample(support,height,a)+4*TrunkSample(support,height+step,a)+TrunkSample(support,height+2*step,a))/16;
                    ring.Add(new(MathF.Cos(a)*r,MathF.Sin(a)*r));
                }
                var skinRing=ring.ToArray();
                ring.Sort((a,b)=>a.X==b.X?a.Y.CompareTo(b.Y):a.X.CompareTo(b.X));
                var hull=new List<Vector2>(TrunkColumns*2);
                foreach(var p in ring){while(hull.Count>=2&&Cross(hull[^2],hull[^1],p)<=0)hull.RemoveAt(hull.Count-1);hull.Add(p);}
                int lower=hull.Count;
                for(int i=ring.Count-2;i>=0;i--){var p=ring[i];while(hull.Count>lower&&Cross(hull[^2],hull[^1],p)<=0)hull.RemoveAt(hull.Count-1);hull.Add(p);}
                hull.RemoveAt(hull.Count-1);
                for(int col=0;col<TrunkColumns;col++)
                {
                    float a=col*Mathf.Tau/TrunkColumns;var ray=new Vector2(MathF.Cos(a),MathF.Sin(a));
                    float radius=float.PositiveInfinity,incidence=1;
                    for(int i=0;i<hull.Count;i++)
                    {
                        var p=hull[i];var edge=hull[(i+1)%hull.Count]-p;
                        float divisor=ray.Cross(edge);if(MathF.Abs(divisor)<1e-7f)continue;
                        float t=p.Cross(edge)/divisor,u=p.Cross(ray)/divisor;
                        if(t>0&&u>=-.00001f&&u<=1.00001f&&t<radius)
                        {
                            radius=t;
                            incidence=MathF.Abs(new Vector2(edge.Y,-edge.X).Normalized().Dot(ray));
                        }
                    }
                    if(!float.IsFinite(radius))throw new InvalidOperationException("Incomplete convex cloth section");
                    if(BodyFrame==1)
                    {
                        // Preserve a shallow, source-driven anterior relief.
                        // The cloth still bridges the deeper skin concavity,
                        // but no longer replaces the whole thorax with a bar.
                        float span=Mathf.SmoothStep(1.07f,1.11f,height)*(1-Mathf.SmoothStep(1.18f,1.22f,height))
                            *MathF.Pow(MathF.Max(0,-ray.Y),4);
                        radius-=MathF.Min(MathF.Max(0,radius-skinRing[col].Length()),.002f*span);
                    }
                    // A tailoring allowance is measured normal to the cloth,
                    // rather than adding the same radial volume to a sternum,
                    // flank and lumbar hollow. Gather the shell at its belt;
                    // keep a little more freedom across the scapular panel.
                    float side=MathF.Pow(MathF.Abs(ray.X),2),back=MathF.Max(0,ray.Y);
                    float belt=FinishedBeltHeight(a);
                    float gather=MathF.Exp(-MathF.Pow((height-belt)/.045f,2));
                    float ease=.0055f+.0015f*side+.001f*back-.001f*gather;
                    if(BodyFrame==1)
                    {
                        // Tailor the outer anterior quadrant independently of
                        // the back and flank. Incidence compensation otherwise
                        // amplifies ease into a lateral shelf at the bust root.
                        float anterior=Mathf.SmoothStep(.20f,.65f,-ray.Y);
                        float chest=Mathf.SmoothStep(1.07f,1.11f,height)*(1-Mathf.SmoothStep(1.18f,1.23f,height));
                        ease-=.002f*chest*anterior*Mathf.SmoothStep(.25f,.70f,MathF.Abs(ray.X));
                    }
                    data[row*TrunkColumns+col]=radius+ease/MathF.Max(.55f,incidence);
                }
            }
            RelaxClothMeridians(data);
            if(BodyFrame==1)RelaxClothHoops(data);
            // Cloth hangs from higher supports instead of following every
            // inward turn of the abdomen. A bounded one-sided envelope bridges
            // the hollows below the chest without enlarging the source pelvis.
            // The belt gradually gathers this ease at its actual fitted height.
            var skin=(float[])data.Clone();
            for(int row=0;row<TrunkRows;row++)for(int col=0;col<TrunkColumns;col++)
            {
                float height=TrunkLow+row*.005f;
                float sampleAngle=col*Mathf.Tau/TrunkColumns;
                float belt=FinishedBeltHeight(sampleAngle);
                float free=Mathf.SmoothStep(belt+.018f,belt+.11f,height)*(1-Mathf.SmoothStep(1.16f,1.21f,height));
                free*=1-MathF.Pow(MathF.Abs(MathF.Cos(sampleAngle)),4);
                float radius=skin[row*TrunkColumns+col],hanging=radius;
                for(int step=1;step<=24&&row+step<TrunkRows;step++)
                    hanging=MathF.Max(hanging,skin[(row+step)*TrunkColumns+col]-step*.005f*.22f);
                data[row*TrunkColumns+col]=radius+MathF.Min(.007f,hanging-radius)*free;
            }
            _clothEnvelopes[key]=data;
        }
        return TrunkSample(_clothEnvelopes[key],y,angle);
    }

    // Round the transverse span as well as its vertical fibres. A convex hull
    // alone has a flat bridge with sharp changes at the two anterior supports;
    // sewing that section directly reads as separate moulded cups. This small
    // outward-only bend keeps the source clearance and a shared front panel.
    private static void RelaxClothHoops(float[] data)
    {
        var source=(float[])data.Clone();var freedom=new float[data.Length];
        for(int row=0;row<TrunkRows;row++)for(int col=0;col<TrunkColumns;col++)
        {
            float y=TrunkLow+row*.005f,a=col*Mathf.Tau/TrunkColumns;
            freedom[row*TrunkColumns+col]=Mathf.SmoothStep(1.05f,1.10f,y)
                *(1-Mathf.SmoothStep(1.18f,1.23f,y))*MathF.Pow(MathF.Max(0,-MathF.Sin(a)),4);
        }
        const float bending=8,limit=.001f;
        for(int pass=0;pass<96;pass++)for(int sweep=0;sweep<2;sweep++)
        for(int row=0;row<TrunkRows;row++)for(int c=0;c<TrunkColumns;c++)
        {
            int col=sweep==0?c:TrunkColumns-1-c,i=row*TrunkColumns+col;
            if(freedom[i]<=0)continue;
            float At(int offset)=>data[row*TrunkColumns+(col+offset+TrunkColumns)%TrunkColumns];
            float candidate=(source[i]+bending*(4*(At(-1)+At(1))-At(-2)-At(2)))/(1+6*bending);
            data[i]=Math.Clamp(candidate,source[i],source[i]+limit*freedom[i]);
        }
        for(int i=0;i<data.Length;i++)
            if(!float.IsFinite(data[i])||data[i]<source[i]||data[i]>source[i]+limit*freedom[i]+.000001f)
                throw new InvalidOperationException("Transverse cloth bend loses source clearance or its fixed border");
    }

    // Minimise bending along the fabric's vertical fibres, subject to the
    // source skin and fixed collar/belt. Independent convex rings otherwise
    // reproduce abrupt under-bust and lumbar turns as moulded body armour.
    private void RelaxClothMeridians(float[] data)
    {
        var source=(float[])data.Clone();var freedom=new float[data.Length];var allowance=new float[data.Length];
        for(int row=0;row<TrunkRows;row++)for(int col=0;col<TrunkColumns;col++)
        {
            float y=TrunkLow+row*.005f,a=col*Mathf.Tau/TrunkColumns;
            float pin=Mathf.SmoothStep(.015f,.050f,MathF.Abs(y-FinishedBeltHeight(a)));
            freedom[row*TrunkColumns+col]=pin*Mathf.SmoothStep(.83f,.90f,y)*(1-Mathf.SmoothStep(1.17f,1.23f,y));
            float side=MathF.Abs(MathF.Cos(a));
            float limit=Mathf.Lerp(.016f,.006f,side*side);
            if(BodyFrame==1)
            {
                float chest=Mathf.SmoothStep(1.04f,1.07f,y)*(1-Mathf.SmoothStep(1.18f,1.22f,y))*MathF.Pow(MathF.Max(0,-MathF.Sin(a)),2);
                // Let one panel span below both supports; pulling this whole
                // region to a 4 mm skin offset moulded two separate cups.
                float frontAllowance=.004f+.009f*(1-Mathf.SmoothStep(1.085f,1.15f,y));
                limit=Mathf.Lerp(limit,frontAllowance,chest);
            }
            allowance[row*TrunkColumns+col]=limit*freedom[row*TrunkColumns+col];
        }
        float bending=BodyFrame==1?100:12;
        for(int pass=0;pass<96;pass++)for(int sweep=0;sweep<2;sweep++)
        for(int r=2;r<TrunkRows-2;r++)for(int col=0;col<TrunkColumns;col++)
        {
            int row=sweep==0?r:TrunkRows-1-r,i=row*TrunkColumns+col;
            if(freedom[i]<=0)continue;
            float candidate=(source[i]+bending*(4*(data[i-TrunkColumns]+data[i+TrunkColumns])
                -data[i-2*TrunkColumns]-data[i+2*TrunkColumns]))/(1+6*bending);
            data[i]=Math.Clamp(candidate,source[i],source[i]+allowance[i]);
        }
        for(int i=0;i<data.Length;i++)
            if(!float.IsFinite(data[i])||data[i]<source[i]||data[i]>source[i]+.016001f
                ||(freedom[i]==0&&data[i]!=source[i]))
                throw new InvalidOperationException("Cloth meridian loses skin clearance or a fixed support");
    }

    private static Vector3[] ClothSurfaceNormals(Vector3[] points,int[] indices)
    {
        var normals=new Vector3[points.Length];
        for(int i=0;i<indices.Length;i+=3)
        {
            int a=indices[i],b=indices[i+1],c=indices[i+2];
            // Godot's front faces use clockwise winding.
            var n=(points[c]-points[a]).Cross(points[b]-points[a]);
            normals[a]+=n;normals[b]+=n;normals[c]+=n;
        }
        for(int i=0;i<normals.Length;i++)
        {
            if(!normals[i].IsFinite()||normals[i].LengthSquared()<1e-16f)
                throw new InvalidOperationException("Degenerate fitted cloth normal");
            normals[i]=normals[i].Normalized();
        }
        return normals;
    }
}
