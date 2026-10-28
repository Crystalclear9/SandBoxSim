using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private Shader? _eyeShader,_lashShader;
    private Mesh Eyelashes()
    {
        string key="anatomical-lashes:"+_faceVariant;if(_meshes.TryGetValue(key,out var cached))return cached;
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        var data=HumanData();
        for(int eye=0;eye<2;eye++)for(int hair=0;hair<26;hair++)
        {
            float u=(hair+.7f)/27;
            var upper=data.UpperLids[eye];
            float sample=u*(upper.Length-1);int i=Math.Min((int)sample,upper.Length-2);float t=sample-i;
            var anchorA=AttachLid(eye,i,true);var anchorB=AttachLid(eye,i+1,true);
            var rest=anchorA.Point.Lerp(anchorB.Point,t);var delta=anchorA.Delta.Lerp(anchorB.Delta,t);
            var root=HumanPoint(rest);var closed=HumanPoint(rest+delta);
            var closure=new Vector2(closed.Y-root.Y,closed.Z-root.Z);
            float length=(.0011f+.0010f*MathF.Sin(u*MathF.PI))*(ChildHead?.8f:1);
            Vector3 Point(float v,float side)=>root+new Vector3(side*.00012f*(1-v),length*(.20f*v+.23f*v*v),-.00018f-length*v);
            void Vertex(float v,float side){tool.SetNormal(Vector3.Forward);tool.SetUV(new(u,v));tool.SetUV2(closure);tool.AddVertex(Point(v,side));}
            for(int row=0;row<3;row++)
            {
                float a=row/3f,b=(row+1)/3f;
                Vertex(a,-1);Vertex(b,-1);Vertex(a,1);Vertex(a,1);Vertex(b,-1);Vertex(b,1);
            }
        }
        tool.Index();return _meshes[key]=tool.Commit();
    }
    private Mesh TearMeniscus()
    {
        string key="anatomical-tearline:"+_faceVariant;if(_meshes.TryGetValue(key,out var cached))return cached;
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);var data=HumanData();
        for(int eye=0;eye<2;eye++)
        {
            var lower=data.LowerLids[eye];
            void Vertex(int i,int section)
            {
                float u=i/(float)(lower.Length-1),angle=section*Mathf.Pi/4;
                var anchor=AttachLid(eye,i,false);var root=HumanPoint(anchor.Point);var closed=HumanPoint(anchor.Point+anchor.Delta);
                var normal=Vector3.Forward*Mathf.Sin(angle)+Vector3.Up*Mathf.Cos(angle);
                float radius=.00018f*MathF.Sqrt(MathF.Max(.02f,MathF.Sin(u*Mathf.Pi)));
                tool.SetNormal(normal);tool.SetUV(new(u,eye));tool.SetUV2(new(closed.Y-root.Y,closed.Z-root.Z));
                tool.AddVertex(root+Vector3.Forward*.00010f+normal*radius);
            }
            for(int i=0;i<lower.Length-1;i++)for(int section=0;section<4;section++)
            {
                // The left and right aperture arrays can run in opposite directions.
                // Build indexed vertices first, then orient against their own normals.
                Vertex(i,section);Vertex(i+1,section);Vertex(i,section+1);
                Vertex(i,section+1);Vertex(i+1,section);Vertex(i+1,section+1);
            }
        }
        using var mesh=tool.Commit();var arrays=mesh.SurfaceGetArrays(0);
        var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var n=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var indices=new int[p.Length];for(int i=0;i<indices.Length;i+=3)
        {indices[i]=i;indices[i+1]=i+1;indices[i+2]=i+2;if((p[i+2]-p[i]).Cross(p[i+1]-p[i]).Dot(n[i]+n[i+1]+n[i+2])<0)(indices[i+1],indices[i+2])=(indices[i+2],indices[i+1]);}
        arrays[(int)Mesh.ArrayType.Index]=indices;var result=new ArrayMesh();result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);
        return _meshes[key]=result;
    }
    private Vector3[]? _facePoints;
    private int[]? _faceIndices;
    private readonly System.Collections.Generic.Dictionary<(int Variant,float X,float Y),float> _faceDepthSamples = new();
    private float FaceDepth(float x,float y)
    {
        var key=(_faceVariant,x,y);
        if(_faceDepthSamples.TryGetValue(key,out float sampled))return sampled;
        if(_facePoints==null)
        {
            var arrays=Shape("face").SurfaceGetArrays(0);_facePoints=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();_faceIndices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        }
        float localX=x,localY=y;
        x/=.177f;y/=.220f;float depth=0;
        for(int i=0;i<_faceIndices!.Length;i+=3)
        {
            var a=_facePoints[_faceIndices[i]];var b=_facePoints[_faceIndices[i+1]];var c=_facePoints[_faceIndices[i+2]];
            if(x<MathF.Min(a.X,MathF.Min(b.X,c.X))-.000001f||x>MathF.Max(a.X,MathF.Max(b.X,c.X))+.000001f||
                y<MathF.Min(a.Y,MathF.Min(b.Y,c.Y))-.000001f||y>MathF.Max(a.Y,MathF.Max(b.Y,c.Y))+.000001f)continue;
            float determinant=(b.Y-c.Y)*(a.X-c.X)+(c.X-b.X)*(a.Y-c.Y);if(MathF.Abs(determinant)<.000001f)continue;
            float u=((b.Y-c.Y)*(x-c.X)+(c.X-b.X)*(y-c.Y))/determinant;
            float v=((c.Y-a.Y)*(x-c.X)+(a.X-c.X)*(y-c.Y))/determinant;
            if(u<0||v<0||u+v>1)continue;
            depth=MathF.Min(depth,(a.Z*u+b.Z*v+c.Z*(1-u-v))*.195f);
        }
        foreach(int side in new[]{0,1})
        {
            var center=HumanEyeCenter(side);
            if(MathF.Abs(localX-center.X)<.019f&&MathF.Abs(localY-center.Y)<.017f)
                depth=MathF.Min(depth,HumanPartDepth("eye",side,localX,localY));
        }
        if(MathF.Abs(localX)<.039f&&MathF.Abs(localY-HumanMouthCenter.Y)<.014f)
            depth=MathF.Min(depth,HumanPartDepth("mouth",0,localX,localY));
        _faceDepthSamples[key]=depth;return depth;
    }
    private void AddResidentFace(ResidentRig rig,string skin)
    {
        // Blink deforms the authored eyelid vertices on the skin itself. Each
        // actor owns its surface override while geometry remains shared.
        for(int surface=0;surface<rig.HeadSkin.Mesh.GetSurfaceCount();surface++)
        {
            var material=rig.HeadSkin.Mesh.SurfaceGetMaterial(surface);
            if(!material.HasMeta("UsesSkinAtlas"))continue;
            rig.LidMaterial=(ShaderMaterial)material.Duplicate();
            rig.HeadSkin.SetSurfaceOverrideMaterial(surface,rig.LidMaterial);
        }
        _eyeShader??=new Shader {Code=@"shader_type spatial;
uniform sampler2D eye_albedo:source_color,filter_linear_mipmap_anisotropic;
void fragment(){
vec4 eye=texture(eye_albedo,UV);
float iris=1.0-smoothstep(.16,.45,dot(eye.rgb,vec3(.2126,.7152,.0722)));
ALBEDO=eye.rgb*.95;ALPHA=eye.a;ALPHA_SCISSOR_THRESHOLD=.5;ROUGHNESS=mix(.25,.11,iris);SPECULAR=.50;
CLEARCOAT=.65;CLEARCOAT_ROUGHNESS=.12;
}"};
        rig.EyeMaterial=new ShaderMaterial {Shader=_eyeShader};
        rig.HasAnatomicalEyes=true;
        _lashShader??=new Shader {Code=@"shader_type spatial;render_mode cull_disabled;
uniform float blink=0.0;
void vertex(){VERTEX.yz+=UV2*blink;}
void fragment(){ALBEDO=vec3(.027,.022,.017);ROUGHNESS=.83;SPECULAR=.16;}"};
        rig.LashMaterial=new ShaderMaterial {Shader=_lashShader};rig.LashMaterial.SetShaderParameter("blink",0f);
        rig.Lashes=Sculpt(rig.Head,Eyelashes(),Vector3.Zero,rig.LashMaterial);
        rig.Lashes.Name="AnatomicalLashes";rig.Lashes.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
        rig.TearMaterial=new ShaderMaterial {Shader=new Shader {Code=@"shader_type spatial;
uniform float blink=0.0;uniform vec4 tone:source_color;
void vertex(){VERTEX.yz+=UV2*blink;}
void fragment(){ALBEDO=mix(tone.rgb,vec3(.25,.075,.055),.32);ROUGHNESS=.13;SPECULAR=.42;}"}};
        rig.TearMaterial.SetShaderParameter("tone",new Color(skin));rig.TearMaterial.SetShaderParameter("blink",0f);
        rig.TearLine=Sculpt(rig.Head,TearMeniscus(),Vector3.Zero,rig.TearMaterial);rig.TearLine.Name="AnatomicalTearMeniscus";
        rig.TearLine.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
        rig.EyeMaterial.SetShaderParameter("eye_albedo",GD.Load<Texture2D>("res://assets/models/human/eye_albedo.png"));
        for(int eye=0;eye<2;eye++)
        {
            var center=HumanEyeCenter(eye);
            var eyeball=rig.Eyes[eye]=new Node3D {Name="Eye"+eye,Position=center};rig.Head.AddChild(eyeball);
            eyeball.AddChild(new MeshInstance3D {Mesh=HumanMesh("eye",eye),MaterialOverride=rig.EyeMaterial});
            rig.Eyelids[eye]=new Node3D {Name="EyelidAnchor"+eye,Position=center};rig.Head.AddChild(rig.Eyelids[eye]);
        }
        rig.Mouth=new Node3D {Name="Mouth",Position=HumanMouthCenter};rig.Head.AddChild(rig.Mouth);
        rig.MouthMaterial=(ShaderMaterial)LipMaterial(skin).Duplicate();
        rig.Mouth.AddChild(new MeshInstance3D {Mesh=HumanMesh("mouth"),MaterialOverride=rig.MouthMaterial});rig.MouthRest=rig.Mouth.Position;
    }
    private Mesh Brows()
    {
        string key="brows:"+_faceVariant;if(_meshes.TryGetValue(key,out var cached))return cached;
        using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        foreach(float side in new[]{-1f,1f})
        {
            var eye=HumanEyeCenter(side<0?0:1);
            // Separate tapered hairs grow outward from the brow ridge. Both
            // ends are sampled on the actual face; no straight floating ribbon.
            for(int hair=0;hair<72;hair++)
            {
                float u=(hair+.5f)/72,fullness=MathF.Pow(MathF.Sin(u*MathF.PI),.55f);
                float x=eye.X+side*(-.016f+u*.035f),y=eye.Y+.015f+MathF.Sin(u*MathF.PI*.95f)*.004f-u*.003f;
                float length=(.0012f+.0021f*fullness)*(1+(hair%3-1)*.12f)*(BodyFrame==1?.84f:1.04f);
                Vector3 Point(float t,float across)
                {
                    float px=x+side*t*(.0006f+.0015f*u),py=y+length*(t-.55f);
                    px+=across*.00040f*fullness*MathF.Sin(MathF.PI*t);
                    return new(px,py,FaceDepth(px,py)-.00035f-.00025f*MathF.Sin(MathF.PI*t));
                }
                for(int row=0;row<3;row++)
                {
                    float t=row/3f,v=(row+1)/3f;
                    surface.SetColor(new Color(.76f+(hair%5)*.045f,.76f+(hair%5)*.045f,.76f+(hair%5)*.045f));
                    EquipmentTriangle(surface,Point(t,-1),Point(v,-1),Point(t,1),Vector3.Forward);
                    EquipmentTriangle(surface,Point(t,1),Point(v,-1),Point(v,1),Vector3.Forward);
                }
            }
        }
        surface.GenerateNormals();surface.Index();var mesh=surface.Commit();_meshes[key]=mesh;return mesh;
    }
}
