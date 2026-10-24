using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private void InstallThatchMaterial()
    {
        var shader=new Shader {Code=@"shader_type spatial;
uniform sampler2D straw_diffuse:source_color,filter_linear_mipmap_anisotropic,repeat_enable;
uniform sampler2D straw_nr:filter_linear_mipmap_anisotropic,repeat_enable;
varying vec3 p;varying vec3 face_normal;
void vertex(){p=VERTEX;face_normal=NORMAL;}
void fragment(){
    // Keep the enlarged roof in the reed-only strip of the source atlas.
    // Wrapping into its upper band would paint timber battens on the eaves.
    vec2 uv=vec2(p.z/2.5,.32+abs(p.x)/3.5*.48);
    vec3 packed=texture(straw_nr,uv).rgb;
    ALBEDO=texture(straw_diffuse,uv).rgb;
    vec3 base=normalize(face_normal),axis=abs(base.z)>.9?vec3(1,0,0):vec3(0,0,1);
    vec3 tangent=normalize(axis-base*dot(axis,base)),bitangent=normalize(cross(base,tangent));
    vec2 grain=packed.rg*2.0-1.0;
    vec3 n=normalize(base+tangent*grain.x*.22+bitangent*grain.y*.22);
    NORMAL=normalize(mat3(VIEW_MATRIX*MODEL_MATRIX)*n);
    ROUGHNESS=clamp(packed.b,.78,.99);SPECULAR=.16;
}"};
        var material=new ShaderMaterial {Shader=shader};
        material.SetShaderParameter("straw_diffuse",ScannedSurfaces.Map("reed_roof_03","diffuse"));
        material.SetShaderParameter("straw_nr",ScannedSurfaces.NormalRoughness(ScannedSurfaces.Map("reed_roof_03","normal"),ScannedSurfaces.Map("reed_roof_03","roughness")));
        _materials[15]=material;
    }
    private Mesh ThatchPanel(float width,float depth,float height,float side)
    {
        string key=$"thatch-panel:{width}:{depth}:{height}:{side}:{_fineArchitecture}";
        if(_meshes.TryGetValue(key,out var cached))return cached;
        int rows=_fineArchitecture?12:4,columns=_fineArchitecture?96:8;float half=(width+.4f)/2;
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);tool.SetSmoothGroup(0);
        Vector3 Point(int r,int c,bool top)
        {
            float u=r/(float)rows,z=(c/(float)columns-.5f)*(depth+.43f);
            float irregular=(MathF.Sin(z*19+.8f)*.014f+MathF.Sin(z*43)*.006f)*MathF.Pow(u,8);
            float x=u*half+irregular;
            float fibres=_fineArchitecture?(MathF.Sin(z*46+x*.4f)*.0002f+MathF.Sin(z*21+x)*.0003f):0;
            float y=height+half*.48f-x*.495f+(top?.23f+MathF.Sin(u*MathF.PI)*.035f+fibres:.055f);
            return new(side*x,y,z);
        }
        void Triangle(Vector3 a,Vector3 b,Vector3 c,Vector3 outward)
        {EquipmentTriangle(tool,a,b,c,outward);}
        foreach(bool top in new[]{false,true})for(int r=0;r<rows;r++)for(int c=0;c<columns;c++)
        {
            var a=Point(r,c,top);var b=Point(r+1,c,top);var d=Point(r,c+1,top);var e=Point(r+1,c+1,top);
            var normal=top?new Vector3(side*.495f,1,0):new Vector3(-side*.495f,-1,0);
            Triangle(a,b,d,normal);Triangle(b,e,d,normal);
        }
        foreach(int r in new[]{0,rows})for(int c=0;c<columns;c++)
        {
            var a=Point(r,c,true);var b=Point(r,c+1,true);var d=Point(r,c,false);var e=Point(r,c+1,false);
            var n=new Vector3((r==0?-1:1)*side,0,0);Triangle(a,b,d,n);Triangle(b,e,d,n);
        }
        foreach(int c in new[]{0,columns})for(int r=0;r<rows;r++)
        {
            var a=Point(r,c,true);var b=Point(r+1,c,true);var d=Point(r,c,false);var e=Point(r+1,c,false);
            var n=c==0?Vector3.Forward:Vector3.Back;Triangle(a,b,d,n);Triangle(b,e,d,n);
        }
        if(_fineArchitecture)for(int c=0;c<columns;c++)
        {
            var root=Point(rows,c,true);float length=.018f+(c*17%13)*.002f;
            var tip=root+new Vector3(side*length,-length*.65f,MathF.Sin(c*2.4f)*.005f);
            var left=root+Vector3.Forward*.0012f;var right=root+Vector3.Back*.0012f;
            Triangle(left,tip,right,Vector3.Up);Triangle(left,right,tip,Vector3.Down);
        }
        tool.GenerateNormals();tool.Index();var mesh=tool.Commit();_meshes[key]=mesh;return mesh;
    }
    private static Vector3 ThatchRidgePoint(float width,float height,float angle,float z,bool inside)
    {
        float baseY=height+(width+.4f)*.24f+.23f;
        return new(MathF.Sin(angle)*.28f,baseY-.135f+MathF.Cos(angle)*.23f-(inside?.045f:0),z);
    }
    private Mesh ThatchRidge(float width,float depth,float height)
    {
        string key=$"thatch-ridge:{width}:{depth}:{height}:{_fineArchitecture}";
        if(_meshes.TryGetValue(key,out var cached))return cached;
        int sides=_fineArchitecture?32:12,rows=_fineArchitecture?12:3;
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);tool.SetSmoothGroup(0);
        Vector3 Point(int c,int r,bool inside)=>ThatchRidgePoint(width,height,-MathF.PI*.5f+c/(float)sides*MathF.PI,(r/(float)rows-.5f)*(depth+.43f),inside);
        foreach(bool inside in new[]{false,true})for(int c=0;c<sides;c++)for(int r=0;r<rows;r++)
        {
            var a=Point(c,r,inside);var b=Point(c+1,r,inside);var d=Point(c,r+1,inside);var e=Point(c+1,r+1,inside);
            var outward=(inside?-1:1)*new Vector3((a.X+b.X)*.5f,MathF.Max(.03f,a.Y-height-(width+.4f)*.24f),0);
            EquipmentTriangle(tool,a,b,d,outward);EquipmentTriangle(tool,b,e,d,outward);
        }
        foreach(int r in new[]{0,rows})for(int c=0;c<sides;c++)
        {
            var a=Point(c,r,false);var b=Point(c+1,r,false);var d=Point(c,r,true);var e=Point(c+1,r,true);
            var outward=r==0?Vector3.Forward:Vector3.Back;
            EquipmentTriangle(tool,a,d,b,outward);EquipmentTriangle(tool,b,d,e,outward);
        }
        foreach(int c in new[]{0,sides})for(int r=0;r<rows;r++)
        {
            var a=Point(c,r,false);var b=Point(c,r+1,false);var d=Point(c,r,true);var e=Point(c,r+1,true);
            var outward=c==0?Vector3.Left:Vector3.Right;
            EquipmentTriangle(tool,a,b,d,outward);EquipmentTriangle(tool,b,e,d,outward);
        }
        tool.GenerateNormals();tool.Index();var mesh=tool.Commit();_meshes[key]=mesh;return mesh;
    }
    private void ValidateThatch()
    {
        bool previous=_fineArchitecture;
        try
        {
            foreach(bool fine in new[]{false,true})foreach(float width in new[]{3.65f,4.13f})
            {
                _fineArchitecture=fine;var mesh=ThatchRidge(width,3.4f,2.23f);
                if(mesh!=ThatchRidge(width,3.4f,2.23f)||mesh.GetAabb().Size.Y<.26f)throw new InvalidOperationException("Thatch ridge loses thickness or cache");
                foreach(var part in new[]{mesh,ThatchPanel(width,3.4f,2.23f,1),ThatchPanel(width,3.4f,2.23f,-1)})
                {
                    var a=part.SurfaceGetArrays(0);var p=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();var n=a[(int)Mesh.ArrayType.Normal].AsVector3Array();
                    for(int i=0;i<p.Length;i++)if(!p[i].IsFinite()||!n[i].IsFinite()||MathF.Abs(n[i].Length()-1)>.015f)throw new InvalidOperationException("Invalid thick thatch geometry");
                }
                foreach(float side in new[]{-1f,1f})
                {
                    float edge=ThatchRidgePoint(width,2.23f,side*MathF.PI*.5f,0,false).Y;
                    float panel=2.23f+(width+.4f)*.24f-.28f*.495f+.23f+MathF.Sin(.28f/((width+.4f)*.5f)*MathF.PI)*.035f;
                    if(edge>panel||panel-edge>.04f)throw new InvalidOperationException("Thatch ridge edge detaches from roof");
                }
            }
        }
        finally {_fineArchitecture=previous;}
        GD.Print("THATCH_JOINERY_PASS: thick near/far surfaces, enclosed ridge, finite normals, cache and embedded edge contact");
    }
}
