using System;
using System.Collections.Generic;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    // Rest-detail-preserving correction in deformed space. Subtracting the
    // transported rest residual makes a rigid pose an exact fixed point.
    private static float ForearmShapeFreedom(Vector3 p)=>Mathf.SmoothStep(.075f,.115f,p.Y)*(1-Mathf.SmoothStep(.240f,.260f,p.Y));
    private static List<(int Index,float Weight)>[] ForearmNeighbours(Vector3[] rest,int[] indices)
    {
        var edges=new HashSet<int>[rest.Length];var result=new List<(int,float)>[rest.Length];
        for(int i=0;i<rest.Length;i++)edges[i]=new();
        for(int t=0;t<indices.Length;t+=3)for(int k=0;k<3;k++)
        {
            int a=indices[t+k],b=indices[t+(k+1)%3];edges[a].Add(b);edges[b].Add(a);
        }
        for(int i=0;i<rest.Length;i++)
        {
            result[i]=new();if(ForearmShapeFreedom(rest[i])==0)continue;
            float sum=0;foreach(int j in edges[i])sum+=1/MathF.Max(.0005f,rest[i].DistanceTo(rest[j]));
            foreach(int j in edges[i])result[i].Add((j,(1/MathF.Max(.0005f,rest[i].DistanceTo(rest[j])))/sum));
        }
        return result;
    }
    private static Vector3[] SmoothForearm(Vector3[] source,Vector3[] rest,List<(int Index,float Weight)>[] neighbours)
    {
        var points=(Vector3[])source.Clone();
        for(int pass=0;pass<12;pass++)
        {
            var prior=points;points=(Vector3[])prior.Clone();
            for(int i=0;i<points.Length;i++)if(neighbours[i].Count>0)
            {
                Vector3 average=Vector3.Zero;foreach(var edge in neighbours[i])average+=prior[edge.Index]*edge.Weight;
                points[i]=prior[i].Lerp(average,.46f*ForearmShapeFreedom(rest[i]));
            }
        }
        return points;
    }
    private static Vector3[] CorrectForearmShape(Vector3[] rest,Vector3[] posed,Basis[] transforms,int[] indices,List<(int Index,float Weight)>[] neighbours,Vector3[] smoothRest)
    {
        var smooth=SmoothForearm(posed,rest,neighbours);var normals=SkinSurfaceNormals(posed,indices,rest);var result=(Vector3[])posed.Clone();
        for(int i=0;i<rest.Length;i++)
        {
            float freedom=ForearmShapeFreedom(rest[i]);if(freedom==0)continue;
            var residual=smooth[i]-posed[i]-transforms[i]*(smoothRest[i]-rest[i]);
            // Only the deformation-induced surface-normal residual is removed;
            // do not slide anatomical landmarks along the limb or add a bulge.
            float amount=Math.Clamp(residual.Dot(normals[i]),-.002f*freedom,.002f*freedom);
            result[i]+=normals[i]*amount;
        }
        // A source edge is never allowed to reverse under the corrective.
        for(int attempt=0;attempt<12;attempt++)
        {
            bool valid=true;
            for(int t=0;t<indices.Length;t+=3)
            {
                int a=indices[t],b=indices[t+1],c=indices[t+2];
                var before=(posed[c]-posed[a]).Cross(posed[b]-posed[a]);var after=(result[c]-result[a]).Cross(result[b]-result[a]);
                if(before.Dot(after)<.75f*before.LengthSquared()){valid=false;break;}
            }
            if(valid)return result;
            for(int i=0;i<result.Length;i++)result[i]=posed[i].Lerp(result[i],.5f);
        }
        throw new InvalidOperationException("Forearm corrective loses its source triangle orientation");
    }
    private readonly Dictionary<ulong,Vector3[][]> _validationForearmShapes=new();
    private Vector3[] CorrectedForearmSource(ResidentRig rig,int side,Vector3[] rest)
    {
        var mesh=(ArrayMesh)rig.HandSkins[side].Mesh;
        if(!_validationForearmShapes.TryGetValue(mesh.GetInstanceId(),out var shapes))
        {
            var arrays=mesh.SurfaceGetBlendShapeArrays(0);shapes=new Vector3[arrays.Count][];
            for(int i=0;i<shapes.Length;i++)shapes[i]=arrays[i][(int)Mesh.ArrayType.Vertex].AsVector3Array();
            _validationForearmShapes[mesh.GetInstanceId()]=shapes;
        }
        var result=(Vector3[])rest.Clone();
        for(int k=0;k<shapes.Length;k++)
        {
            float w=rig.HandSkins[side].GetBlendShapeValue(k);if(w==0)continue;
            for(int i=0;i<rest.Length;i++)result[i]+=(shapes[k][i]-rest[i])*w;
        }
        return result;
    }
}
