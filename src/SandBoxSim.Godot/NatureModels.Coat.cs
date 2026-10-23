using System;
using System.Collections.Generic;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    // Close-range coat geometry grows from the actual continuous skin, with
    // barycentrically transferred weights. It is shared by all animals of a kind.
    private Mesh GroomAnimalCoat(Mesh skin,bool wolf)
    {
        string key="groomed-coat:"+wolf;
        if(_meshes.TryGetValue(key,out var cached))return cached;
        var a=skin.SurfaceGetArrays(0);
        var p=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var n=a[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var ix=a[(int)Mesh.ArrayType.Index].AsInt32Array();
        var bs=a[(int)Mesh.ArrayType.Bones].AsInt32Array();
        var ws=a[(int)Mesh.ArrayType.Weights].AsFloat32Array();
        var cumulative=new List<float>();var eligible=new List<int>();float area=0;
        for(int t=0;t<ix.Length;t+=3)
        {
            var center=(p[ix[t]]+p[ix[t+1]]+p[ix[t+2]])/3;
            // Keep muzzle/eyes, paws and hooves readable. Ribbons are for coat,
            // not a replacement for the face or the underlying anatomy.
            if(center.Y<.18f||center.Z<-.88f)continue;
            float weight=(p[ix[t+1]]-p[ix[t]]).Cross(p[ix[t+2]]-p[ix[t]]).Length();
            if(weight<.000001f)continue;
            area+=weight;cumulative.Add(area);eligible.Add(t);
        }
        if(area<=0)throw new InvalidOperationException("Empty animal grooming surface");
        uint seed=wolf?76123u:19871u;
        float Random(){seed^=seed<<13;seed^=seed>>17;seed^=seed<<5;return (seed&0xFFFFFF)/16777216f;}
        using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        var coat=new ShaderMaterial {Shader=new Shader {Code=((ShaderMaterial)Fur(wolf)).Shader.Code.Replace("shader_type spatial;","shader_type spatial;render_mode cull_disabled;").Replace("void fragment(){","void fragment(){if(!FRONT_FACING){NORMAL=-NORMAL;}")}};
        foreach(string field in new[]{"coat","pale","predator"})coat.SetShaderParameter(field,((ShaderMaterial)Fur(wolf)).GetShaderParameter(field));
        surface.SetMaterial(coat);
        int strands=wolf?3000:1800;
        for(int strand=0;strand<strands;strand++)
        {
            float tone=.80f+Random()*.34f;
            surface.SetColor(new Color(tone,tone,tone));
            int selected=cumulative.BinarySearch(Random()*area);if(selected<0)selected=~selected;
            int t=eligible[Math.Min(selected,eligible.Count-1)];
            float r=MathF.Sqrt(Random()),s=Random();float[] bary={1-r,r*(1-s),r*s};
            var root=Vector3.Zero;var normal=Vector3.Zero;var influence=new float[12];
            for(int k=0;k<3;k++)
            {
                int v=ix[t+k];root+=p[v]*bary[k];normal+=n[v]*bary[k];
                for(int j=0;j<4;j++)influence[bs[v*4+j]]+=ws[v*4+j]*bary[k];
            }
            normal=normal.Normalized();
            var order=new int[12];for(int k=0;k<12;k++)order[k]=k;
            Array.Sort(order,(x,y)=>influence[y].CompareTo(influence[x]));
            var bones=new[]{order[0],order[1],order[2],order[3]};
            float total=0;for(int k=0;k<4;k++)total+=influence[bones[k]];
            var weights=new float[4];for(int k=0;k<4;k++)weights[k]=influence[bones[k]]/total;
            // Coat lays toward the rump and down the flanks. Shoulder guard
            // hairs stand slightly longer; the deer keeps a close summer coat.
            var flow=root.Z<-.48f?new Vector3(0,-.85f,.25f):root.Y<.50f?Vector3.Down:new Vector3(0,-.35f,.9f);flow=(flow-normal*flow.Dot(normal)).Normalized();
            if(flow.LengthSquared()<.5f)flow=normal.Cross(Vector3.Right).Normalized();
            var across=normal.Cross(flow).Normalized();
            float ruff=wolf?MathF.Exp(-MathF.Pow((root.Z+.4f)/.25f,2)):0;
            float length=(wolf?.018f:.004f)*(0.65f+Random()*.6f)*(1+ruff*.9f);
            float width=wolf?.00042f:.00028f;
            for(int plane=0;plane<2;plane++)
            {
                var spread=plane==0?across:(across+normal*.5f).Normalized();
                // Keep the entire ribbon above its sampled triangle. Burying
                // roots and lifting the middle by less than a millimetre caused
                // bright, flickering intersections on the smoothed shoulders.
                var basePoint=root+normal*.0008f;
                var middle=root+normal*(.001f+length*(.12f+ruff*.25f))+flow*length*.45f;
                var tip=root+normal*(.001f+length*(.18f+ruff*.30f))+flow*length;
                void V(Vector3 point){surface.SetBones(bones);surface.SetWeights(weights);surface.SetNormal(normal);surface.SetUV(Vector2.Zero);surface.AddVertex(point);}
                void Tri(Vector3 x,Vector3 y,Vector3 z){if((y-x).Cross(z-x).Dot(normal)>0)(y,z)=(z,y);V(x);V(y);V(z);}
                Tri(basePoint-spread*width,basePoint+spread*width,middle-spread*width*.65f);
                Tri(basePoint+spread*width,middle+spread*width*.65f,middle-spread*width*.65f);
                Tri(middle-spread*width*.65f,middle+spread*width*.65f,tip);
            }
        }
        surface.Index();var mesh=surface.Commit();_meshes[key]=mesh;return mesh;
    }
}
