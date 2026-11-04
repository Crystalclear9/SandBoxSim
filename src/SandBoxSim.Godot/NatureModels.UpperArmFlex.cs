using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private static float UpperArmFlexFreedom(Vector2 pattern)=>pattern.X<=0?0:Mathf.SmoothStep(.055f,.085f,pattern.X)*(1-Mathf.SmoothStep(.195f,.225f,pattern.X));
    private void AddUpperArmFlexShapes(Godot.Collections.Array<Godot.Collections.Array> shapes,Godot.Collections.Array arrays)
    {
        var source=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var uv=arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();var ids=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        var restNormals=ClothSurfaceNormals(source,ids);var area=new float[source.Length];
        var edges=new System.Collections.Generic.HashSet<(int A,int B)>();
        for(int t=0;t<ids.Length;t+=3)
        {
            int a=ids[t],b=ids[t+1],c=ids[t+2];float value=(source[b]-source[a]).Cross(source[c]-source[a]).Length()/6;
            area[a]+=value;area[b]+=value;area[c]+=value;
            for(int k=0;k<3;k++){int x=ids[t+k],y=ids[t+(k+1)%3];edges.Add((Math.Min(x,y),Math.Max(x,y)));}
        }
        for(int side=0;side<2;side++)
        {
            var points=(Vector3[])source.Clone();var radial=new Vector3[source.Length];var freedom=new float[source.Length];var offset=new Vector3[source.Length];
            double flux=0,capacity=0;
            for(int i=0;i<source.Length;i++)if((source[i].X<0)==(side==0))
            {
                float free=freedom[i]=UpperArmFlexFreedom(uv[i]);if(free==0)continue;
                float centre=(side==0?-1:1)*.202f*ArmWidthScale;
                var r=new Vector3(source[i].X-centre,0,source[i].Z);float radius=r.Length();if(radius<.0001f)continue;
                radial[i]=r/radius;float front=Math.Clamp(-radial[i].Z,0,1);
                float along=ResidentRig.BodyY(1.18f)-(BodyFrame==1?.014f:.009f)-source[i].Y;
                float proximal=(along-.115f)/.040f,distal=(along-.180f)/.040f;
                float relief=(MathF.Exp(-proximal*proximal)-.85f*MathF.Exp(-distal*distal))*(.2f+.8f*front*front);
                offset[i]=radial[i]*(MathF.Min(.003f,radius*.055f)*free*relief);
                flux+=area[i]*restNormals[i].Dot(offset[i]);capacity+=area[i]*free*restNormals[i].Dot(radial[i]);
            }
            if(capacity<=0)throw new InvalidOperationException("Upper-arm flex region has no outward surface support");
            float mean=(float)(flux/capacity);
            for(int i=0;i<points.Length;i++)
            {
                offset[i]-=radial[i]*(mean*freedom[i]);
                if(offset[i].Length()>.0035f)throw new InvalidOperationException("Upper-arm flex exceeds its cloth relief bound");
            }
            // A sewn fold can have much shorter edges than its pattern chart.
            // Limit neighbouring edit differences with exact pinned endpoints.
            for(int pass=0;pass<64;pass++)foreach(var (a,b) in edges)
            {
                float wa=freedom[a]>0?1:0,wb=freedom[b]>0?1:0;if(wa+wb==0)continue;
                var d=offset[a]-offset[b];float length=d.Length(),limit=source[a].DistanceTo(source[b])*.10f;
                if(length<=limit||length<1e-10f)continue;
                var change=d*((length-limit)/length/(wa+wb));offset[a]-=change*wa;offset[b]+=change*wb;
            }
            // Projection can alter the signed flux. Remove that residual before
            // uniform backtracking, which preserves its zero first-order sum.
            flux=0;for(int i=0;i<source.Length;i++)flux+=area[i]*restNormals[i].Dot(offset[i]);
            mean=(float)(flux/capacity);
            for(int i=0;i<points.Length;i++)offset[i]-=radial[i]*(mean*freedom[i]);
            float scale=1;
            for(int attempt=0;attempt<20;attempt++)
            {
                for(int i=0;i<points.Length;i++)points[i]=source[i]+offset[i]*scale;
                bool valid=true;
                for(int t=0;t<ids.Length;t+=3)
                {
                    int a=ids[t],b=ids[t+1],c=ids[t+2];var before=(source[c]-source[a]).Cross(source[b]-source[a]);var after=(points[c]-points[a]).Cross(points[b]-points[a]);
                    if(before.Dot(after)<.85f*before.LengthSquared()){valid=false;break;}
                }
                if(valid)break;
                if(attempt==19)throw new InvalidOperationException("Upper-arm flex cannot retain sewn triangle orientation");scale*=.5f;
            }
            var geometry=ClothSurfaceNormals(points,ids);var corrected=(Vector3[])normals.Clone();
            for(int i=0;i<points.Length;i++)if(freedom[i]>0)
                corrected[i]=(new Quaternion(restNormals[i],geometry[i])*normals[i]).Normalized();
            var shape=new Godot.Collections.Array();shape.Resize((int)Mesh.ArrayType.Max);
            shape[(int)Mesh.ArrayType.Vertex]=points;shape[(int)Mesh.ArrayType.Normal]=corrected;shapes.Add(shape);
        }
    }
}
