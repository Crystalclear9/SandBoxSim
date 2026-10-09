using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    // A shoe last has a broad toe, narrow waist, heel and rising instep. The sole
    // follows the same outline, rather than intersecting two unrelated ellipsoids.
    private Mesh BootLast(bool sole)
    {
        string key="boot-last:"+sole;
        if(_meshes.TryGetValue(key,out var cached))return cached;
        const int rings=24,sides=40;
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Point(int row,int column)
        {
            float t=row/(float)rings,a=column*MathF.Tau/sides;
            float z=Mathf.Lerp(-.139f,.083f,t);
            float taper=MathF.Sqrt(MathF.Max(0,MathF.Sin(t*MathF.PI)));
            float width=(.056f+.016f*MathF.Exp(-MathF.Pow((t-.25f)*5,2)))*taper;
            float arch=.018f*MathF.Exp(-MathF.Pow((t-.64f)*6,2));
            float bottom=-.034f+arch;
            float top=.016f+.050f*MathF.Exp(-MathF.Pow((t-.66f)*4,2));
            if(sole){bottom-=.012f;top=bottom+.012f;width+=.002f*taper;}
            float height=(top-bottom)*.5f*taper;
            return new(MathF.Cos(a)*width,(top+bottom)*.5f+MathF.Sin(a)*height,z);
        }
        void Vertex(int r,int c){tool.SetUV(new(c/(float)sides,r/(float)rings));tool.AddVertex(Point(r,c));}
        for(int r=0;r<rings;r++)for(int c=0;c<sides;c++)
        {Vertex(r,c);Vertex(r+1,c);Vertex(r,c+1);Vertex(r,c+1);Vertex(r+1,c);Vertex(r+1,c+1);}
        tool.GenerateNormals();tool.Index();var mesh=tool.Commit();_meshes[key]=mesh;return mesh;
    }

    private Shader? _leatherShader;
    private Material LeatherSurface(string color)
    {
        string key="leather:"+color;if(_residentMaterials.TryGetValue(key,out var cached))return cached;
        _leatherShader??=new Shader {Code=@"shader_type spatial;
uniform vec4 dye:source_color;
varying vec3 point;
void vertex(){point=VERTEX;}
void fragment(){
float pores=sin(point.x*1230.0+point.z*470.0)*sin(point.y*970.0+point.z*730.0);
pores/=1.0+pow(max(length(dFdx(point)),length(dFdy(point)))*1100.0,2.0);
float wear=sin(point.z*57.0+point.x*21.0)*0.018;
ALBEDO=dye.rgb*(0.97+pores*0.025+wear);ROUGHNESS=0.69+pores*0.035;SPECULAR=0.26;
}"};
        var material=new ShaderMaterial {Shader=_leatherShader};material.SetShaderParameter("dye",new Color(color));
        _residentMaterials[key]=material;return material;
    }

    // Both sides of a cupped ear share the rim. Its inset is part of the surface,
    // so the pale inner ear never floats in front of a separate primitive.
    private Mesh AnimalEar(bool wolf)
    {
        string key="sculpted-ear:"+wolf;if(_meshes.TryGetValue(key,out var cached))return cached;
        const int rows=20,columns=24;
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Point(int row,int column,bool rear)
        {
            float t=row/(float)rows,u=column/(float)columns*2-1;
            float width=wolf?.063f*(1-t)*MathF.Min(1,t/.12f):.057f*MathF.Pow(MathF.Max(0,MathF.Sin(t*MathF.PI)),.6f);
            float cup=(1-u*u)*MathF.Sin(t*MathF.PI);
            return new(u*width,t*(wolf?.17f:.21f),rear?.019f*cup+.005f*MathF.Sin(t*MathF.PI):-.015f*cup);
        }
        void Vertex(int r,int c,bool rear)
        {
            float t=r/(float)rows,u=c/(float)columns*2-1;
            float inset=rear?0:MathF.Pow(MathF.Max(0,1-u*u),.8f)*MathF.Sin(t*MathF.PI);
            tool.SetColor(new Color(wolf?"#747b7b":"#8c6445").Lerp(new Color("#b9a08a"),inset*.75f));
            tool.SetUV(new(c/(float)columns,t));tool.AddVertex(Point(r,c,rear));
        }
        foreach(bool rear in new[]{false,true})for(int r=0;r<rows;r++)for(int c=0;c<columns;c++)
        {
            if(rear){Vertex(r,c,rear);Vertex(r+1,c,rear);Vertex(r,c+1,rear);Vertex(r,c+1,rear);Vertex(r+1,c,rear);Vertex(r+1,c+1,rear);}
            else{Vertex(r,c,rear);Vertex(r,c+1,rear);Vertex(r+1,c,rear);Vertex(r,c+1,rear);Vertex(r+1,c+1,rear);Vertex(r+1,c,rear);}
        }
        // Seal both narrow rims; front and rear faces meet at their endpoints.
        foreach(int c in new[]{0,columns})for(int r=0;r<rows;r++)
        {
            if(c==0){Vertex(r,c,false);Vertex(r+1,c,false);Vertex(r,c,true);Vertex(r,c,true);Vertex(r+1,c,false);Vertex(r+1,c,true);}
            else{Vertex(r,c,false);Vertex(r,c,true);Vertex(r+1,c,false);Vertex(r,c,true);Vertex(r+1,c,true);Vertex(r+1,c,false);}
        }
        tool.GenerateNormals();tool.Index();var mesh=tool.Commit();_meshes[key]=mesh;return mesh;
    }

    // A curved, tapered tube with a parallel reference axis: curved antlers have
    // no cylinder end discs or abrupt changes of thickness at the branch tips.
    private Mesh AntlerBranch(string key,Vector3[] controls,float radius)
    {
        if(_meshes.TryGetValue(key,out var cached))return cached;
        const int segments=24,sides=12;
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Center(float t)
        {
            float value=Math.Clamp(t,0,1)*(controls.Length-1);
            int i=Math.Min((int)value,controls.Length-2);float u=value-i;
            var a=controls[Math.Max(0,i-1)];var b=controls[i];var c=controls[i+1];var d=controls[Math.Min(controls.Length-1,i+2)];
            return .5f*(2*b+(c-a)*u+(2*a-5*b+4*c-d)*u*u+(-a+3*b-3*c+d)*u*u*u);
        }
        void Vertex(int row,int col)
        {
            float t=row/(float)segments,a=col*MathF.Tau/sides;
            var tangent=(Center(t+.002f)-Center(t-.002f)).Normalized();
            var right=tangent.Cross(Vector3.Back).Normalized();var up=tangent.Cross(right).Normalized();
            float r=radius*MathF.Pow(1-t,.72f);
            r*=1+.045f*MathF.Sin(t*90+a*3);
            tool.SetUV(new(col/(float)sides,t));tool.AddVertex(Center(t)+(right*MathF.Cos(a)+up*MathF.Sin(a))*r);
        }
        for(int r=0;r<segments;r++)for(int c=0;c<sides;c++)
        {Vertex(r,c);Vertex(r+1,c);Vertex(r,c+1);Vertex(r,c+1);Vertex(r+1,c);Vertex(r+1,c+1);}
        tool.GenerateNormals();tool.Index();var mesh=tool.Commit();_meshes[key]=mesh;return mesh;
    }
    private readonly StandardMaterial3D _earMaterial=new() {VertexColorUseAsAlbedo=true,Roughness=.88f,MetallicSpecular=.18f};

    private void ValidateOrganicDetails()
    {
        foreach(var mesh in new[]{BootLast(false),BootLast(true),AnimalEar(false),AnimalEar(true),
            AntlerBranch("antler-check",new[]{Vector3.Zero,new Vector3(.08f,.3f,.02f),new Vector3(.2f,.5f,.08f)},.018f)})
        {
            var data=mesh.SurfaceGetArrays(0);var vertices=data[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals=data[(int)Mesh.ArrayType.Normal].AsVector3Array();
            if(vertices.Length!=normals.Length)throw new InvalidOperationException("Organic detail lost normals");
            for(int i=0;i<vertices.Length;i++)if(!vertices[i].IsFinite()||!normals[i].IsFinite())throw new InvalidOperationException("Invalid organic detail geometry");
        }
        if(BootLast(false)!=BootLast(false)||AnimalEar(true)!=AnimalEar(true))throw new InvalidOperationException("Organic detail cache misses");
        foreach(bool sole in new[]{false,true})
        {
            var arrays=BootLast(sole).SurfaceGetArrays(0);var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();float facing=0;
            for(int i=0;i<points.Length;i++)facing+=points[i].X*normals[i].X;
            if(facing<=0)throw new InvalidOperationException("Shoe last faces inward");
        }
        GD.Print("ORGANIC_DETAILS_PASS: shoe lasts, soles, cupped ears, tapered antlers, finite geometry and shared meshes");
    }
}
