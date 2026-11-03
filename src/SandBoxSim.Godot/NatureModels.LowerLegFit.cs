using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private void FitFemaleLowerLegs(ResidentRig rig)
    {
        if(BodyFrame!=1||rig.IsChild)return;
        Mesh Fit(Mesh source,Transform3D space,bool shoe)
        {
            string key="female-lower-leg-v1:"+source.GetInstanceId()+":"+space+":"+shoe;
            if(_meshes.TryGetValue(key,out var cached))return cached;
            var mesh=new ArrayMesh();var inverse=space.AffineInverse();
            Vector3 Map(Vector3 p)
            {
                // Preserve the actual trouser cuff and knee rim. Taper only
                // below their overlap; ankle/sole height and foot length stay.
                float w=shoe?1:Mathf.SmoothStep(.045f,.115f,-p.Y);
                return new(p.X*(1-(shoe?.10f:.08f)*w),p.Y,p.Z*(1-(shoe?0:.05f)*w));
            }
            for(int s=0;s<source.GetSurfaceCount();s++)
            {
                var arrays=source.SurfaceGetArrays(s);var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var n=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                for(int i=0;i<p.Length;i++)
                {
                    var q=space*p[i];const float h=.0001f;
                    var jacobian=new Basis((Map(q+Vector3.Right*h)-Map(q-Vector3.Right*h))/(2*h),
                        (Map(q+Vector3.Up*h)-Map(q-Vector3.Up*h))/(2*h),(Map(q+Vector3.Back*h)-Map(q-Vector3.Back*h))/(2*h));
                    if(jacobian.Determinant()<.80f)throw new InvalidOperationException("Lower-leg tailoring folds its source");
                    var normal=space.Basis.Inverse().Transposed()*n[i];
                    n[i]=(space.Basis.Transposed()*jacobian.Inverse().Transposed()*normal).Normalized();p[i]=inverse*Map(q);
                    if(!p[i].IsFinite()||!n[i].IsFinite())throw new InvalidOperationException("Invalid lower-leg tailoring");
                }
                arrays[(int)Mesh.ArrayType.Vertex]=p;arrays[(int)Mesh.ArrayType.Normal]=n;arrays[(int)Mesh.ArrayType.Tangent]=default(Variant);
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);mesh.SurfaceSetMaterial(s,source.SurfaceGetMaterial(s));
            }
            return _meshes[key]=WithScreenLods(mesh);
        }
        for(int side=0;side<2;side++)
        {
            foreach(Node child in rig.Knees[side].GetChildren())if(child is MeshInstance3D part&&part.Mesh!=null)part.Mesh=Fit(part.Mesh,part.Transform,false);
            foreach(Node child in rig.Feet[side].GetChildren())if(child is MeshInstance3D part&&part.Mesh!=null)part.Mesh=Fit(part.Mesh,part.Transform,true);
        }
    }
}
