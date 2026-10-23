using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private Shader? _eyeShader;
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
uniform vec2 gaze=vec2(0.0);
uniform sampler2D eye_albedo:source_color,filter_linear_mipmap_anisotropic;
void fragment(){
vec4 eye=texture(eye_albedo,UV+gaze*.035);
ALBEDO=eye.rgb*.85;ALPHA=eye.a;ALPHA_SCISSOR_THRESHOLD=.5;ROUGHNESS=.20;SPECULAR=.45;
}"};
        rig.EyeMaterial=new ShaderMaterial {Shader=_eyeShader};
        rig.HasAnatomicalEyes=true;
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
                float length=(.0012f+.0021f*fullness)*(1+(hair%3-1)*.12f);
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
