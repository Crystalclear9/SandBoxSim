using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;
internal enum ResidentTool { None, Hammer, Axe, Pick, Hoe, Spear }
internal sealed partial class NatureModels
{
    private ShaderMaterial? _handleMaterial;
    private Material HandleMaterial()=>_handleMaterial??=new ShaderMaterial {Shader=new Shader {Code=@"shader_type spatial;
varying vec3 p;void vertex(){p=VERTEX;}
void fragment(){
float fibre=sin(p.x*3400.0+p.z*1700.0+sin(p.y*19.0)*.35);
fibre/=1.0+pow(max(length(dFdx(p)),length(dFdy(p)))*2900.0,2.0);
float grain=sin(p.x*690.0+p.z*430.0+sin(p.y*8.0)*.6);
ALBEDO=mix(vec3(.20,.135,.071),vec3(.31,.226,.133),.55+grain*.10+fibre*.065);
ROUGHNESS=.64;SPECULAR=.24;
}"}};
    private void AddHandTools(ResidentRig rig,JobType job)
    {
        rig.Grip=new Node3D {Name="HandGrip",Position=new(0,-.035f,-.0286f),Rotation=new(0,0,MathF.PI/2)};rig.Hands[1].AddChild(rig.Grip);
        rig.Holster=new Node3D {Name="BeltToolLoop",Position=TorsoSurface(.145f,.79f+BeltRise,false)+new Vector3(0,0,-.022f),Rotation=new(0,0,.65f)};rig.Torso.AddChild(rig.Holster);
        if(!rig.IsChild&&job!=JobType.None)
        {
            // Two closed leather loops surround the shaft. The supporting strap
            // reaches the belt; it is no longer a solid block behind empty air.
            var mount=rig.Holster.Transform;
            foreach(float y in new[]{-.045f,.045f})for(int edge=0;edge<24;edge++)
            {
                float a=edge*MathF.Tau/24,b=(edge+1)*MathF.Tau/24;
                FineBeam(rig.Torso,mount*new Vector3(MathF.Cos(a)*.018f,y,MathF.Sin(a)*.018f),mount*new Vector3(MathF.Cos(b)*.018f,y,MathF.Sin(b)*.018f),.009f,"#514237");
            }
            foreach(float y in new[]{-.045f,.045f})
            {
                var start=TorsoSurface(.145f,.79f+BeltRise+y,false);var end=mount*new Vector3(0,y,.018f);var direction=end-start;
                var strap=Detail(rig.Torso,"masonry",(start+end)*.5f,new(.025f,direction.Length(),.004f),"#514237");
                strap.Quaternion=new Quaternion(Vector3.Up,direction.Normalized());
            }
        }
        rig.ToolMesh=new MeshInstance3D {Name="Tool",Visible=false};rig.Holster.AddChild(rig.ToolMesh);
        foreach(ResidentTool tool in Enum.GetValues<ResidentTool>()) if(tool!=ResidentTool.None) rig.ToolMeshes[tool]=HandTool(tool);
        rig.DefaultTool=rig.IsChild?ResidentTool.None:job switch {
            JobType.Farmer=>ResidentTool.Hoe, JobType.Miner=>ResidentTool.Pick,
            JobType.Builder or JobType.Craftsman=>ResidentTool.Hammer, JobType.Soldier=>ResidentTool.Spear,_=>ResidentTool.None };
        rig.SelectTool(rig.DefaultTool);
        rig.CargoMesh=new MeshInstance3D {Name="CarriedResources",Position=new(-.12f,-.106f,-.025f),Visible=false};rig.Hands[1].AddChild(rig.CargoMesh);
        foreach(var resource in new[]{ResourceKind.Food,ResourceKind.Wood,ResourceKind.Stone,ResourceKind.Iron})rig.CargoMeshes[resource]=CargoBundle(resource);
    }
    private Mesh CargoBundle(ResourceKind resource)
    {
        string key="cargo:"+resource;if(_meshes.TryGetValue(key,out var mesh))return mesh;
        var root=new Node3D();
        if(resource==ResourceKind.Wood)
        {
            for(int i=0;i<3;i++)Detail(root,"cylinder",new(0,-.025f,i*.035f-.035f),new(.041f,.25f,.041f),"#8b6c46",new(0,0,MathF.PI/2));
            // Closed bindings wrap the bundle instead of two diagonal strokes.
            foreach(float side in new[]{-1f,1f})for(int segment=0;segment<32;segment++)
            {
                float a=segment*MathF.Tau/32,b=(segment+1)*MathF.Tau/32;
                FineBeam(root,new(side*.075f,-.025f+MathF.Cos(a)*.023f,MathF.Sin(a)*.058f),new(side*.075f,-.025f+MathF.Cos(b)*.023f,MathF.Sin(b)*.058f),.0035f,"#a18c68");
            }
            for(int log=0;log<3;log++)foreach(float side in new[]{-1f,1f})
            {
                Detail(root,"cylinder",new(side*.1255f,-.025f,log*.035f-.035f),new(.035f,.0015f,.035f),"#b59a70",new(0,0,MathF.PI/2));
                for(int segment=0;segment<20;segment++)
                {
                    float a=segment*MathF.Tau/20,b=(segment+1)*MathF.Tau/20;
                    FineBeam(root,new(side*.1265f,-.025f+MathF.Cos(a)*.011f,log*.035f-.035f+MathF.Sin(a)*.011f),new(side*.1265f,-.025f+MathF.Cos(b)*.011f,log*.035f-.035f+MathF.Sin(b)*.011f),.0008f,"#836444");
                }
            }
        }
        else
        {
            Detail(root,"seed",new(0,-.04f,0),new(.24f,.17f,.14f),resource==ResourceKind.Food?"#a8937d":"#645e53");
            for(int i=0;i<3;i++)FineBeam(root,new(-.08f+i*.08f,.025f,-.025f),new(-.08f+i*.08f,.025f,.025f),.004f,"#514237");
        }
        MergeResidentParts(root,key);mesh=root.GetChild<MeshInstance3D>(0).Mesh;_meshes[key]=mesh;root.Free();return mesh;
    }
    private Mesh FingerProxy(string skin,bool closed,int side)
    {
        string key="finger-proxy:"+skin+closed+":"+side+":"+BodyFrame;
        if(_meshes.TryGetValue(key,out var mesh))return mesh;
        if(ResidentRig.HandMirror(side)<0)
        {
            var canonical=FingerProxy(skin,closed,0);var mirrored=new ArrayMesh();
            for(int surface=0;surface<canonical.GetSurfaceCount();surface++)
            {
                var arrays=canonical.SurfaceGetArrays(surface);
                var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                for(int i=0;i<points.Length;i++){points[i].X=-points[i].X;normals[i].X=-normals[i].X;}
                var tangents=arrays[(int)Mesh.ArrayType.Tangent].AsFloat32Array();
                for(int i=0;i<tangents.Length;i+=4){tangents[i]=-tangents[i];tangents[i+3]=-tangents[i+3];}
                var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                if(indices.Length==0){indices=new int[points.Length];for(int i=0;i<indices.Length;i++)indices[i]=i;}
                for(int i=0;i<indices.Length;i+=3)(indices[i+1],indices[i+2])=(indices[i+2],indices[i+1]);
                arrays[(int)Mesh.ArrayType.Vertex]=points;arrays[(int)Mesh.ArrayType.Normal]=normals;arrays[(int)Mesh.ArrayType.Index]=indices;
                if(tangents.Length>0)arrays[(int)Mesh.ArrayType.Tangent]=tangents;
                mirrored.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);mirrored.SurfaceSetMaterial(surface,canonical.SurfaceGetMaterial(surface));
            }
            _meshes[key]=mirrored;return mirrored;
        }
        // A static bake of the same anatomy and joint poses as the close-up hand.
        // No per-frame skeleton work is required for distant residents.
        var source=AnatomicalHandMesh(0,skin,true);var result=new ArrayMesh();
        var rest=new Transform3D[ResidentRig.HandBoneCount];var posed=new Transform3D[ResidentRig.HandBoneCount];
        Array.Fill(rest,Transform3D.Identity);Array.Fill(posed,Transform3D.Identity);
        rest[0]=posed[0]=rest[15]=posed[15]=Transform3D.Identity;
        float curl=closed?1:0;
        for(int digit=0;digit<4;digit++)
        {
            float length=digit==0?.92f:digit==1?1:digit==2?.96f:.86f;
            var bends=ResidentRig.FingerBends(digit,curl);
            for(int joint=0;joint<3;joint++)
            {
                int bone=1+digit*3+joint,parent=joint==0?0:bone-1;
                var origin=joint==0?ResidentRig.FingerRoot(0,digit):new Vector3(0,-(joint==1?.026f:.018f)*length,0);
                var rotation=joint==0?new Vector3(bends.X,0,(digit-1.5f)*.025f*(1-curl)):new Vector3(joint==1?bends.Y:bends.Z,0,0);
                rest[bone]=rest[parent]*new Transform3D(Basis.Identity,origin);
                posed[bone]=posed[parent]*new Transform3D(Basis.FromEuler(rotation),origin);
            }
        }
        rest[13]=new(Basis.Identity,ResidentRig.ThumbRoot(0));rest[14]=rest[13]*new Transform3D(Basis.Identity,new(0,-.018f,0));
        posed[13]=new(Basis.FromEuler(new(curl*.25f,0,.15f+curl*.65f)),ResidentRig.ThumbRoot(0));
        posed[14]=posed[13]*new Transform3D(Basis.FromEuler(new(curl*.55f,0,0)),new(0,-.018f,0));
        var palette=new Transform3D[ResidentRig.HandBoneCount];for(int i=0;i<palette.Length;i++)palette[i]=posed[i]*rest[i].AffineInverse();
        for(int surface=0;surface<source.GetSurfaceCount();surface++)
        {
            var arrays=source.SurfaceGetArrays(surface);var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();var weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            for(int i=0;i<points.Length;i++)
            {
                Vector3 point=Vector3.Zero,normal=Vector3.Zero;
                for(int j=0;j<4;j++){var transform=palette[bones[i*4+j]];point+=(transform*points[i])*weights[i*4+j];normal+=(transform.Basis*normals[i])*weights[i*4+j];}
                points[i]=point;normals[i]=normal.Normalized();
            }
            arrays[(int)Mesh.ArrayType.Vertex]=points;arrays[(int)Mesh.ArrayType.Normal]=normals;
            arrays[(int)Mesh.ArrayType.Bones]=default(Variant);arrays[(int)Mesh.ArrayType.Weights]=default(Variant);
            result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);result.SurfaceSetMaterial(surface,source.SurfaceGetMaterial(surface));
        }
        _meshes[key]=result;return result;
    }

    private Mesh HandTool(ResidentTool kind)
    {
        string key="hand-tool:"+kind;
        if(_meshes.TryGetValue(key,out var mesh))return mesh;
        var root=new Node3D();
        float length=kind==ResidentTool.Hoe?.61f:kind==ResidentTool.Spear?1.38f:.39f;
        float center=kind==ResidentTool.Spear?.28f:-.11f;
        // Taper outside the grip while keeping its 24 mm contact diameter.
        Sculpt(root,Loft("tool-handle:"+kind,new[]{
            new Vector4(-length/2,0,.010f,.010f),new(-length*.36f,0,.012f,.012f),
            new(length*.20f,0,.012f,.012f),new(length/2,0,.0105f,.0105f)},24),
            new(0,center,0),HandleMaterial(),new(-MathF.PI/2,0,0));
        float socket=kind==ResidentTool.Spear?.97f:kind==ResidentTool.Hoe?-.385f:-.27f;
        var ferrule=Detail(root,"cylinder",new(0,socket,0),new(.031f,.045f,.031f),"#68716f");ferrule.MaterialOverride=Material(11);
        foreach(float side in new[]{-1f,1f})Detail(root,"sphere",new(side*.016f,socket,0),new(.004f,.006f,.006f),"#98968a");
        if(kind==ResidentTool.Hammer)
        {
            // Separate striking faces and a narrower central eye housing;
            // metric chamfers keep millimetre edges independent of aspect ratio.
            Sculpt(root,MetricBoard(new(.09f,.040f,.040f),.002f),new(0,-.30f,0),ResidentMaterial("#626865"));
            foreach(float end in new[]{-1f,1f})
                Sculpt(root,MetricBoard(new(.023f,.046f,.046f),.0015f),new(end*.055f,-.30f,0),ResidentMaterial("#94988f"));
            Detail(root,"cylinder",new(0,-.275f,0),new(.028f,.027f,.028f),"#514237");
        }
        else if(kind==ResidentTool.Pick)
        {
            Sculpt(root,Loft("pick-head",new[]{new Vector4(-.17f,-.035f,.004f,.004f),new(-.08f,-.005f,.014f,.015f),new(0,0,.022f,.024f),new(.08f,-.01f,.013f,.014f),new(.17f,-.042f,.003f,.003f)}),new(0,-.30f,0),ResidentMaterial("#68716f"),new(0,MathF.PI/2,0));
        }
        else if(kind==ResidentTool.Axe)
        {
            Sculpt(root,Loft("axe-blade",new[]{new Vector4(-.055f,0,.022f,.025f),new(0,0,.020f,.030f),new(.07f,-.010f,.010f,.065f),new(.10f,-.010f,.003f,.071f)}),new(0,-.27f,0),ResidentMaterial("#68716f"),new(0,MathF.PI/2,0));
        }
        else if(kind==ResidentTool.Hoe)
            Sculpt(root,HoeBlade(),new(0,-.415f,-.045f),ResidentMaterial("#68716f"),new(.25f,0,0));
        else Detail(root,"cone",new(0,1.035f,0),new(.046f,.15f,.029f),"#7d8581");
        MergeResidentParts(root,key);
        mesh=root.GetChild<MeshInstance3D>(0).Mesh;_meshes[key]=mesh;root.Free();return mesh;
    }
    private Mesh HoeBlade()
    {
        const string key="forged-hoe-blade";if(_meshes.TryGetValue(key,out var cached))return cached;
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        const int rows=12,columns=8;
        Vector3 Point(int row,int column,int side)
        {
            float t=row/(float)rows,u=column/(float)columns*2-1;
            float width=Mathf.Lerp(.040f,.068f,SmoothBlade(t));
            float thickness=Mathf.Lerp(.007f,.0015f,t*t);
            return new(u*width,.012f*t*t+side*thickness*.5f,-.05f+.10f*t-.005f*u*u*t);
        }
        void Triangle(Vector3 a,Vector3 b,Vector3 c)
        {
            (b,c)=(c,b); // Godot uses clockwise front faces; keep the closed blade facing outward.
            var normal=(c-a).Cross(b-a).Normalized();
            foreach(var p in new[]{a,b,c}){tool.SetNormal(normal);tool.SetUV(new((p.X+.07f)/.14f,(p.Z+.06f)/.12f));tool.AddVertex(p);}
        }
        void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d){Triangle(a,b,c);Triangle(a,c,d);}
        foreach(int side in new[]{-1,1})for(int r=0;r<rows;r++)for(int c=0;c<columns;c++)
        {var a=Point(r,c,side);var b=Point(r+1,c,side);var x=Point(r+1,c+1,side);var d=Point(r,c+1,side);if(side==1)Quad(a,b,x,d);else Quad(d,x,b,a);}
        for(int r=0;r<rows;r++){Quad(Point(r,0,-1),Point(r+1,0,-1),Point(r+1,0,1),Point(r,0,1));Quad(Point(r,columns,1),Point(r+1,columns,1),Point(r+1,columns,-1),Point(r,columns,-1));}
        for(int c=0;c<columns;c++){Quad(Point(0,c,1),Point(0,c+1,1),Point(0,c+1,-1),Point(0,c,-1));Quad(Point(rows,c,-1),Point(rows,c+1,-1),Point(rows,c+1,1),Point(rows,c,1));}
        tool.Index();return _meshes[key]=tool.Commit();
    }
    private static float SmoothBlade(float t)=>t*t*(3-2*t);
}
