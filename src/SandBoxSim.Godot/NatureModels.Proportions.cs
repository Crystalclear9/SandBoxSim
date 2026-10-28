using System;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    internal const float HipHeight=.80f, ThighLength=.38f, ShinLength=.355f;
    // Retain the authored head, hands and arm lengths; shorten the barrel-like
    // trunk while raising the pelvis. These coordinates also drive action IK.
    internal static float BodyY(float y)=>y<.67f?y+.13f:y<1.18f?.80f+(y-.67f)*(.43f/.51f):y+.05f;
    internal static float SourceBodyY(float y)=>y<.80f?y-.13f:y<1.23f?.67f+(y-.80f)*(.51f/.43f):y-.05f;
    internal static Vector3 BodyPoint(Vector3 p)=>new(p.X,BodyY(p.Y),p.Z);
}

internal sealed partial class NatureModels
{
    private Mesh ProportionedMesh(Mesh source,Transform3D space,bool torso,float lengthScale=1)
    {
        string key="proportioned:"+source.GetInstanceId()+":"+space+":"+torso+":"+lengthScale;
        if(_meshes.TryGetValue(key,out var cached))return cached;
        var mesh=new ArrayMesh();var inverse=space.AffineInverse();
        var normalsToWorld=space.Basis.Inverse().Transposed();
        var normalsToLocal=space.Basis.Transposed();
        for(int surface=0;surface<source.GetSurfaceCount();surface++)
        {
            var arrays=source.SurfaceGetArrays(surface);var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var n=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            for(int i=0;i<p.Length;i++)
            {
                var point=space*p[i];var normal=normalsToWorld*n[i];
                float derivative=torso?(point.Y>.67f&&point.Y<1.18f?.43f/.51f:1):lengthScale;
                point.Y=torso?ResidentRig.BodyY(point.Y):point.Y*lengthScale;
                normal.Y/=derivative;p[i]=inverse*point;n[i]=(normalsToLocal*normal).Normalized();
            }
            arrays[(int)Mesh.ArrayType.Vertex]=p;arrays[(int)Mesh.ArrayType.Normal]=n;
            // A nonlinear remap changes tangent frames. Godot reconstructs them
            // from the preserved UVs when needed instead of keeping stale frames.
            arrays[(int)Mesh.ArrayType.Tangent]=default(Variant);
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);mesh.SurfaceSetMaterial(surface,source.SurfaceGetMaterial(surface));
        }
        _meshes[key]=mesh;return mesh;
    }
    private void ApplyResidentProportions(ResidentRig rig)
    {
        void Remap(Node3D parent,Transform3D space)
        {
            foreach(Node child in parent.GetChildren())
            {
                if(child is not Node3D node)continue;
                if(node==rig.Head||node==rig.Arms[0]||node==rig.Arms[1]||node==rig.Holster||node==rig.PackMount||node==rig.SatchelMount)
                {node.Position=ResidentRig.BodyPoint(node.Position);continue;}
                var transform=space*node.Transform;
                if(node is MeshInstance3D part&&part.Mesh!=null)part.Mesh=ProportionedMesh(part.Mesh,transform,true);
                else Remap(node,transform);
            }
        }
        Remap(rig.Torso,Transform3D.Identity);
        foreach(Node child in rig.GetChildren())
            if(child is MeshInstance3D part&&part.Mesh!=null)part.Mesh=ProportionedMesh(part.Mesh,part.Transform,true);
        for(int side=0;side<2;side++)
        {
            rig.Legs[side].Position=new(side==0?-.10f:.10f,ResidentRig.HipHeight,0);
            rig.Knees[side].Position=new(0,-ResidentRig.ThighLength,0);
            rig.Feet[side].Position=new(0,-ResidentRig.ShinLength,-.04f);
            foreach(Node child in rig.Legs[side].GetChildren())
                if(child is MeshInstance3D part)part.Mesh=ProportionedMesh(part.Mesh,part.Transform,false,ResidentRig.ThighLength/.31f);
            foreach(Node child in rig.Knees[side].GetChildren())
                if(child is MeshInstance3D part)part.Mesh=ProportionedMesh(part.Mesh,part.Transform,false,ResidentRig.ShinLength/.295f);
        }
    }
}
