using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private static Vector3 AuthoredPawAnchor(Vector3 sole,bool wolf)=>new(sole.X,wolf?.095f:.10f,sole.Z+(wolf?.055f:.018f));
    private static void BindAuthoredPaws(Godot.Collections.Array arrays,bool wolf,string material)
    {
        if(material!=(wolf?"Wolf":"Body"))return;
        var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();var weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
        for(int i=0;i<p.Length;i++)
        {
            int foot=AuthoredFoot(i,bones,weights,wolf);if(foot<0||p[i].Y>.19f)continue;
            float blend=1-Mathf.SmoothStep(wolf?.12f:.13f,.24f,p[i].Y);
            var influence=new Dictionary<int,float>();float moved=0;
            for(int k=0;k<4;k++)
            {
                int b=bones[i*4+k];float w=weights[i*4+k],part=b==4+foot*2?w*blend:0;
                moved+=part;influence.TryGetValue(b,out float value);influence[b]=value+w-part;
            }
            influence[(wolf?15:14)+foot]=moved;
            var top=influence.OrderByDescending(e=>e.Value).ThenBy(e=>e.Key).Take(4).ToArray();float sum=top.Sum(e=>e.Value);
            for(int k=0;k<4;k++){bones[i*4+k]=k<top.Length?top[k].Key:0;weights[i*4+k]=k<top.Length?top[k].Value/sum:0;}
        }
        arrays[(int)Mesh.ArrayType.Bones]=bones;arrays[(int)Mesh.ArrayType.Weights]=weights;
    }
    private (Vector3 Point,Vector3 Normal) PawSurface(bool wolf,int foot,float x,float coordinate,bool vertical)
    {
        var a=AuthoredAnimalData(wolf).Mesh.SurfaceGetArrays(0);var p=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();var n=a[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var ids=a[(int)Mesh.ArrayType.Index].AsInt32Array();var bones=a[(int)Mesh.ArrayType.Bones].AsInt32Array();var weights=a[(int)Mesh.ArrayType.Weights].AsFloat32Array();
        float depth=float.NegativeInfinity;var normal=Vector3.Zero;
        Vector2 Project(Vector3 v)=>new(v.X,vertical?v.Z:v.Y);
        for(int i=0;i<ids.Length;i+=3)
        {
            int aa=ids[i],b=ids[i+1],c=ids[i+2];if(AuthoredFoot(aa,bones,weights,wolf)!=foot||MathF.Max(p[aa].Y,MathF.Max(p[b].Y,p[c].Y))>.23f)continue;
            var ab=Project(p[b])-Project(p[aa]);var ac=Project(p[c])-Project(p[aa]);float det=ab.Cross(ac);if(MathF.Abs(det)<1e-11f)continue;
            var d=new Vector2(x,coordinate)-Project(p[aa]);float u=d.Cross(ac)/det,v=ab.Cross(d)/det;if(u<0||v<0||u+v>1)continue;
            var point=p[aa]*(1-u-v)+p[b]*u+p[c]*v;float value=vertical?point.Y:point.Z;
            if(value<=depth)continue;depth=value;normal=(n[aa]*(1-u-v)+n[b]*u+n[c]*v).Normalized();
        }
        if(!float.IsFinite(depth))throw new InvalidOperationException($"Missing source paw support: {wolf}/{foot}/{x}/{coordinate}/{vertical}");
        return(vertical?new(x,depth,coordinate):new(x,coordinate,depth),normal);
    }
    private Mesh PawKeratin(bool wolf,int foot)
    {
        string key=$"paw-keratin:{wolf}:{foot}";if(_meshes.TryGetValue(key,out var cached))return cached;
        var data=AuthoredAnimalData(wolf);var frame=_authoredFootFrames[wolf?1:0]![foot];var anchor=AuthoredPawAnchor(data.Soles[foot],wolf);
        var roots=new List<Vector3>();using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        void Claw((Vector3 Point,Vector3 Normal) contact,Vector3 end,float width,float height)
        {
            roots.Add(contact.Point);var root=contact.Point-contact.Normal*.001f-anchor;
            var axis=end.Normalized();var across=Vector3.Right;var up=axis.Cross(across).Normalized();
            Vector3 Center(float t)=>root+end*t-Vector3.Up*(.002f*t*t);
            Vector3 Ring(int side,float t,float size)=>Center(t)+across*MathF.Cos(side*Mathf.Tau/6)*width*size+up*MathF.Sin(side*Mathf.Tau/6)*height*size;
            for(int side=0;side<6;side++)
            {
                for(int row=0;row<2;row++)
                {
                    float t0=row==0?0:.4f,t1=row==0?.4f:.75f,s0=row==0?1:.78f,s1=row==0?.78f:.4f;
                    var a=Ring(side,t0,s0);var b=Ring(side+1,t0,s0);var c=Ring(side,t1,s1);var d=Ring(side+1,t1,s1);
                    var outwards=(a+b+c+d)*.25f-(Center(t0)+Center(t1))*.5f;
                    EquipmentTriangle(surface,a,b,c,outwards);EquipmentTriangle(surface,b,d,c,outwards);
                }
                var last=Ring(side,.75f,.4f);var next=Ring(side+1,.75f,.4f);var tip=root+end-Vector3.Up*.002f;
                EquipmentTriangle(surface,last,next,tip,(last+next)*.5f-Center(.75f));
            }
        }
        if(wolf)
        {
            for(int toe=0;toe<4;toe++)
            {
                bool outer=toe is 0 or 3;float x=frame.CenterX+(toe-1.5f)*frame.Width*.225f;
                var contact=PawSurface(true,foot,x,frame.Minimum.Z+(outer?.020f:.012f),true);
                Claw(contact,new(0,-.009f,outer?-.015f:-.018f),.0023f,.0018f);
            }
            if(foot<2)
            {
                float x=frame.CenterX+(foot==0?.016f:-.016f);
                Claw(PawSurface(true,foot,x,.105f,false),new(0,-.018f,.006f),.0030f,.0025f);
            }
        }
        else for(int side=-1;side<=1;side+=2)
            Claw(PawSurface(false,foot,frame.CenterX+side*.012f,.115f,false),new(0,-.020f,.010f),.0045f,.0040f);
        surface.GenerateNormals();surface.Index();var mesh=surface.Commit();mesh.SetMeta("SourceRoots",roots.ToArray());
        return _meshes[key]=mesh;
    }
    private void InstallPawKeratin(AnimalRig rig,bool wolf)
    {
        string key="animal-keratin:"+wolf;
        if(!_residentMaterials.TryGetValue(key,out var material))_residentMaterials[key]=material=new StandardMaterial3D {AlbedoColor=new(wolf?"#554d42":"#514337"),Roughness=.69f,MetallicSpecular=.24f};
        for(int foot=0;foot<4;foot++)
        {
            var detail=Sculpt(rig.Paws[foot]!,PawKeratin(wolf,foot),Vector3.Zero,material);
            detail.Name="Claws";detail.VisibilityRangeEnd=14;
        }
    }
}
