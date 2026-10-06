using System;
using Godot;
namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    private Shader? _lidShader,_shoulderFabric;
    private Skin? _shoulderSkin;
    private void AddShoulderBridges(ResidentRig rig,string cloth,string skin)
    {
        var skeleton=rig.ShoulderSkeleton=new Skeleton3D {Name="ShoulderSkeleton"};rig.Torso.AddChild(skeleton);
        skeleton.AddBone("Torso");skeleton.SetBoneRest(0,Transform3D.Identity);
        for(int side=0;side<2;side++){skeleton.AddBone(side==0?"LeftShoulder":"RightShoulder");skeleton.SetBoneParent(side+1,0);skeleton.SetBoneRest(side+1,new Transform3D(Basis.Identity,rig.Arms[side].Position));}
        skeleton.ResetBonePoses();
        if(_shoulderSkin==null)
        {
            _shoulderSkin=new Skin();_shoulderSkin.AddBind(0,Transform3D.Identity);
            for(int side=0;side<2;side++)_shoulderSkin.AddBind(side+1,new Transform3D(Basis.Identity,-rig.Arms[side].Position));
        }
        string key="shoulder-fabric:"+cloth;
        if(!_residentMaterials.TryGetValue(key,out var material))
        {
            _shoulderFabric??=new Shader {Code=((ShaderMaterial)ResidentMaterial(cloth)).Shader.Code.Replace("shader_type spatial;","shader_type spatial; render_mode cull_disabled;").Replace("ROUGHNESS = 0.94;","if(!FRONT_FACING){NORMAL=-NORMAL;} ROUGHNESS = 0.94;")};
            var fabric=new ShaderMaterial {Shader=_shoulderFabric};fabric.SetShaderParameter("dye",new Color(cloth));material=fabric;_residentMaterials[key]=material;
        }
        rig.ShoulderGeometry=JoinedShoulders();
        rig.DistantBodyMesh=DistantBody(rig.BodySkin.Mesh);rig.DetailedBodyMesh=SkinnedBody(rig.BodySkin.Mesh,material);rig.DetailedBodySkin=_shoulderSkin;
        rig.BodySkin.Mesh=rig.DetailedBodyMesh;rig.BodySkin.Skin=_shoulderSkin;rig.BodySkin.Skeleton=new NodePath("../ShoulderSkeleton");rig.ShoulderBridge=rig.BodySkin;
        for(int side=0;side<2;side++)rig.ShoulderFlesh[side]=Detail(rig.Arms[side],"sphere",Vector3.Zero,new(.082f,.082f,.074f),skin);
    }
    private Mesh DistantBody(Mesh body)
    {
        string key="distant-body:"+body.GetInstanceId();if(_meshes.TryGetValue(key,out var mesh))return mesh;
        var result=new ArrayMesh();
        for(int s=0;s<body.GetSurfaceCount();s++)
        {
            var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);surface.SetMaterial(body.SurfaceGetMaterial(s));
            void Append(Mesh part,int partSurface,Transform3D transform)
            {
                var arrays=part.SurfaceGetArrays(partSurface);var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();var uv=arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();var normalBasis=transform.Basis.Inverse().Transposed();
                for(int i=0;i<indices.Length;i++){int v=indices[i];surface.SetNormal((normalBasis*normals[v]).Normalized());surface.SetUV(uv[v]);surface.AddVertex(transform*points[v]);}
            }
            Append(body,s,Transform3D.Identity);
            if(s==0)foreach(float side in new[]{-1f,1f})Append(Shape("finger-low"),0,new Transform3D(Basis.Identity.Scaled(new(.13f,.14f,.124f)),new(side*.218f,1.18f,0)));
            surface.Index();surface.Commit(result);
        }
        mesh=result;_meshes[key]=mesh;return mesh;
    }
    private Mesh SkinnedBody(Mesh body,Material cloth)
    {
        string key="skinned-body:"+body.GetInstanceId();if(_meshes.TryGetValue(key,out var mesh))return mesh;
        var result=new ArrayMesh();
        for(int s=0;s<body.GetSurfaceCount();s++)
        {
            var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);surface.SetMaterial(s==0?cloth:body.SurfaceGetMaterial(s));
            void Append(Godot.Collections.Array arrays,bool shoulder)
            {
                var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();var uv=arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();var colors=shoulder?arrays[(int)Mesh.ArrayType.Color].AsColorArray():Array.Empty<Color>();
                for(int i=0;i<indices.Length;i++)
                {
                    int v=indices[i];float t=shoulder?colors[v].R:0;int bone=shoulder?(colors[v].G<.5f?1:2):0;
                    surface.SetBones(new[]{0,bone,0,0});surface.SetWeights(new[]{1-t,t,0f,0f});surface.SetUV(uv[v]);surface.SetNormal(normals[v]);surface.AddVertex(points[v]);
                }
            }
            Append(body.SurfaceGetArrays(s),false);if(s==0)Append(JoinedShoulders().SurfaceGetArrays(0),true);
            surface.Index();surface.Commit(result);
        }
        mesh=result;_meshes[key]=mesh;return mesh;
    }
    private Mesh JoinedShoulders()
    {
        if(_meshes.TryGetValue("joined-shoulders",out var mesh))return mesh;
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        for(int side=0;side<2;side++)
        {
            var arrays=ShoulderBridge(side==0?-1:1).SurfaceGetArrays(0);
            var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();var colors=arrays[(int)Mesh.ArrayType.Color].AsColorArray();var uv=arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            for(int i=0;i<indices.Length;i++){int index=indices[i];surface.SetBones(new[]{0,side+1,0,0});surface.SetWeights(new[]{1-colors[index].R,colors[index].R,0f,0f});surface.SetColor(new(colors[index].R,side,0,1));surface.SetNormal(normals[index]);surface.SetUV(uv[index]);surface.AddVertex(points[index]+new Vector3(side==0?-.218f:.218f,1.18f,0));}
        }
        surface.Index();mesh=surface.Commit();_meshes["joined-shoulders"]=mesh;return mesh;
    }
    private Mesh ShoulderBridge(int side)
    {
        string key="shoulder-bridge:"+side;if(_meshes.TryGetValue(key,out var mesh))return mesh;
        const int rings=5,sides=24;
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);surface.SetSmoothGroup(0);
        Vector3 Point(int ring,int column)
        {
            float t=(float)ring/rings,a=column*MathF.Tau/sides;
            var center=new Vector3(side*-.060f*(1-t)*(1-t),.04f*MathF.Sin(MathF.PI*t)-.077f*t*t,0);
            var basis=new Basis(Quaternion.Identity.Slerp(new Quaternion(new Vector3(side,0,0),Vector3.Down),t));
            var radial=new Vector3(0,MathF.Cos(a)*Mathf.Lerp(.064f,.066f,t),MathF.Sin(a)*Mathf.Lerp(.061f,.061f,t));
            return center+basis*radial;
        }
        void Vertex(Vector3 point,int ring,int column){float t=(float)ring/rings;surface.SetColor(new Color(t,t,t,1));surface.SetUV(new((float)column/sides,t));surface.AddVertex(point);}
        Vector3 Center(int ring){float t=(float)ring/rings;return new(side*-.060f*(1-t)*(1-t),.04f*MathF.Sin(MathF.PI*t)-.077f*t*t,0);}
        void Triangle(int ra,int ca,int rb,int cb,int rc,int cc)
        {
            var a=Point(ra,ca);var b=Point(rb,cb);var c=Point(rc,cc);
            var outward=a-Center(ra)+b-Center(rb)+c-Center(rc);
            if((b-a).Cross(c-a).Dot(outward)>0){(rb,rc)=(rc,rb);(cb,cc)=(cc,cb);(b,c)=(c,b);}
            Vertex(a,ra,ca);Vertex(b,rb,cb);Vertex(c,rc,cc);
        }
        for(int r=0;r<rings;r++)for(int c=0;c<sides;c++){Triangle(r,c,r,c+1,r+1,c);Triangle(r,c+1,r+1,c+1,r+1,c);}
        surface.GenerateNormals();surface.Index();mesh=surface.Commit();_meshes[key]=mesh;return mesh;
    }
    private void AddResidentFace(ResidentRig rig,string skin)
    {
        _lidShader??=new Shader {Code=@"shader_type spatial;
uniform vec4 dye:source_color;uniform float blink=0.0;
void vertex(){ float x=UV.x*2.0-1.0;float arc=sqrt(max(0.0,1.0-x*x));VERTEX.y=arc*(0.005-UV.y*(0.001+blink*0.010)); }
void fragment(){ALBEDO=dye.rgb;ROUGHNESS=0.76;SPECULAR=0.18;}"};
        rig.LidMaterial=new ShaderMaterial {Shader=_lidShader};rig.LidMaterial.SetShaderParameter("dye",new Color(skin));
        for(int eye=0;eye<2;eye++)
        {
            float side=eye==0?-1:1;
            var eyeball=rig.Eyes[eye]=new Node3D {Name="Eye"+eye,Position=new(side*.034f,.019f,-.083f)};rig.Head.AddChild(eyeball);
            Detail(eyeball,"sphere",Vector3.Zero,new(.024f,.010f,.007f),"#cec4b4");
            Detail(eyeball,"sphere",new(0,0,-.004f),new(.009f,.008f,.004f),"#4d463a");
            Detail(eyeball,"sphere",new(.001f,.001f,-.007f),Vector3.One*.0018f,"#eee4d3");
            MergeResidentParts(eyeball,"animated-eye:"+eye);
            var lid=rig.Eyelids[eye]=new MeshInstance3D {Name="Eyelid"+eye,Mesh=EyeLid(),Position=eyeball.Position,MaterialOverride=rig.LidMaterial};rig.Head.AddChild(lid);
        }
        rig.Mouth=new Node3D {Name="Mouth",Position=new(0,-.058f,-.092f)};rig.Head.AddChild(rig.Mouth);
        Detail(rig.Mouth,"seed",new(0,.003f,0),new(.032f,.003f,.005f),"#976d5d");
        Detail(rig.Mouth,"seed",new(0,-.002f,0),new(.029f,.004f,.004f),"#aa7f6b");
        FineBeam(rig.Mouth,new(-.012f,0,-.003f),new(.012f,0,-.003f),.001f,"#785b4c");
        MergeResidentParts(rig.Mouth,"animated-mouth");
    }
    private Mesh EyeLid()
    {
        if(_meshes.TryGetValue("eye-lid",out var mesh))return mesh;
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        void Vertex(int col,int row)
        {
            float u=col/16f,t=row/4f,x=u*2-1,arc=MathF.Sqrt(MathF.Max(0,1-x*x));
            surface.SetNormal(Vector3.Forward);surface.SetUV(new(u,t));surface.AddVertex(new(x*.0125f,arc*(.005f-t*.001f),-.0085f*arc));
        }
        for(int row=0;row<4;row++)for(int col=0;col<16;col++){Vertex(col,row);Vertex(col,row+1);Vertex(col+1,row);Vertex(col+1,row);Vertex(col,row+1);Vertex(col+1,row+1);}
        surface.Index();mesh=surface.Commit();_meshes["eye-lid"]=mesh;return mesh;
    }
}
