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
            BootSection(t,out float width,out float bottom,out float top,out float taper);
            float z=Mathf.Lerp(-.170f,.090f,t);
            if(sole)return new(MathF.Cos(a)*(width+.0015f*taper),bottom+.001f-.012f*MathF.Pow(MathF.Max(0,-MathF.Sin(a)),.4f),z);
            return new(MathF.Cos(a)*width,bottom+(top-bottom)*taper*MathF.Pow(MathF.Max(0,MathF.Sin(a)),.70f),z);
        }
        void Vertex(int r,int c){tool.SetUV(new(c/(float)sides,r/(float)rings));tool.AddVertex(Point(r,c));}
        for(int r=0;r<rings;r++)for(int c=0;c<sides;c++)
        {Vertex(r,c);Vertex(r+1,c);Vertex(r,c+1);Vertex(r,c+1);Vertex(r+1,c);Vertex(r+1,c+1);}
        tool.GenerateNormals();tool.Index();var mesh=tool.Commit();_meshes[key]=mesh;return mesh;
    }

    private static void BootSection(float t,out float width,out float bottom,out float top,out float taper)
    {
        taper=MathF.Sqrt(MathF.Max(0,MathF.Sin(t*MathF.PI)));
        width=(.051f+.007f*MathF.Exp(-MathF.Pow((t-.25f)*5,2)))*taper;
        bottom=-.053f+.018f*MathF.Exp(-MathF.Pow((t-.64f)*6,2));
        top=.016f+.050f*MathF.Exp(-MathF.Pow((t-.66f)*4,2));
    }
    private static float BootUpperHeight(float x,float z)
    {
        float t=Math.Clamp((z+.170f)/.260f,0,1);
        BootSection(t,out float width,out float bottom,out float top,out float taper);
        float across=Math.Clamp(x/MathF.Max(.0001f,width),-1,1);
        return bottom+(top-bottom)*taper*MathF.Pow(MathF.Sqrt(MathF.Max(0,1-across*across)),.70f);
    }
    private Mesh BootLaces()
    {
        if(_meshes.TryGetValue("boot-laces",out var cached))return cached;
        using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        for(int row=0;row<4;row++)foreach(float side in new[]{-1f,1f})
        {
            Vector3 Point(int step)
            {
                float t=step/8f,x=Mathf.Lerp(side*-.022f,side*.022f,t),z=.010f-row*.023f-t*.018f;
                return new(x,BootUpperHeight(x,z)+.0013f,z);
            }
            for(int segment=0;segment<8;segment++)
            {
                var start=Point(segment);var end=Point(segment+1);var axis=(end-start).Normalized();
                var right=axis.Cross(Vector3.Up).Normalized();var up=axis.Cross(right).Normalized();
                for(int edge=0;edge<6;edge++)
                {
                    float a=edge*MathF.Tau/6,b=(edge+1)*MathF.Tau/6;
                    var n=right*MathF.Cos(a)+up*MathF.Sin(a);var m=right*MathF.Cos(b)+up*MathF.Sin(b);
                    var x=start+n*.0008f;var y=start+m*.0008f;var z=end+n*.0008f;var w=end+m*.0008f;
                    EquipmentTriangle(surface,x,y,z,n+m);EquipmentTriangle(surface,y,w,z,n+m);
                }
            }
        }
        surface.GenerateNormals();surface.Index();var mesh=surface.Commit();_meshes["boot-laces"]=mesh;return mesh;
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

    private Mesh AnimalEyelid(bool wolf,float side)
    {
        string key=$"animal-lid:{wolf}:{side}";if(_meshes.TryGetValue(key,out var cached))return cached;
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        const int ring=48,sides=6;
        Vector3 Point(int row,int edge)
        {
            float t=row*MathF.Tau/ring,a=edge*MathF.Tau/sides;
            float y=.038f+MathF.Sin(t)*(.009f+.0025f*MathF.Max(0,MathF.Sin(t)));
            float z=-.105f+MathF.Cos(t)*.026f;
            float x=(wolf?.113f:.104f)+MathF.Sin(t)*.0015f;
            return new(side*(x+MathF.Cos(a)*.0022f),y+MathF.Sin(a)*MathF.Sin(t)*.0022f,z+MathF.Sin(a)*MathF.Cos(t)*.0022f);
        }
        for(int r=0;r<ring;r++)for(int e=0;e<sides;e++)
        {
            var a=Point(r,e);var b=Point(r+1,e);var c=Point(r,e+1);var d=Point(r+1,e+1);
            float t=(r+.5f)*MathF.Tau/ring,section=(e+.5f)*MathF.Tau/sides;
            var outward=new Vector3(side*MathF.Cos(section),MathF.Sin(section)*MathF.Sin(t),MathF.Sin(section)*MathF.Cos(t));
            EquipmentTriangle(tool,a,b,c,outward);EquipmentTriangle(tool,c,b,d,outward);
        }
        tool.GenerateNormals();tool.Index();var mesh=tool.Commit();_meshes[key]=mesh;return mesh;
    }

    // Both sides of a cupped ear share the rim. Its inset is part of the surface,
    // so the pale inner ear never floats in front of a separate primitive.
    private Mesh AnimalEar(bool wolf)
    {
        string key="sculpted-ear:"+wolf;if(_meshes.TryGetValue(key,out var cached))return cached;
        const int rows=28,columns=32;
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Point(int row,int column,bool rear)
        {
            float t=row/(float)rows,u=column/(float)columns*2-1;
            float leaf=MathF.Pow(MathF.Max(0,MathF.Sin(t*MathF.PI)),wolf?.78f:.68f);
            float width=(wolf?.059f:.060f)*leaf*(wolf?1.20f-.55f*t:1+.09f*MathF.Sin(t*MathF.PI));
            float cup=(1-u*u)*MathF.Sin(t*MathF.PI);
            float rim=.004f*MathF.Exp(-MathF.Pow((MathF.Abs(u)-.84f)/.14f,2))*MathF.Sin(t*MathF.PI);
            float root=.006f*MathF.Exp(-MathF.Pow((t-.18f)/.13f,2))*(1-u*u);
            // A curled, thick cartilage rim surrounds a recessed concha; the
            // tip bends backwards instead of ending in a flat triangular plane.
            float bend=.016f*t*t*t;
            return new(u*width,t*(wolf?.145f:.21f),bend+(rear?.012f*cup+.004f*MathF.Sin(t*MathF.PI):.007f*cup-rim-root));
        }
        void Vertex(int r,int c,bool rear)
        {
            float t=r/(float)rows,u=c/(float)columns*2-1;
            float inset=rear?0:MathF.Pow(MathF.Max(0,1-u*u),.8f)*MathF.Sin(t*MathF.PI);
            float baseShade=MathF.Exp(-MathF.Pow((t-.23f)/.19f,2))*(1-u*u);
            tool.SetColor(new Color(wolf?"#797b71":"#8c6445").Lerp(new Color("#bcb09c"),inset*.7f).Darkened(rear?0:baseShade*.13f));
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
        float HeightAt(Mesh mesh,float x,float z,bool highest)
        {
            var arrays=mesh.SurfaceGetArrays(0);var vertices=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();float result=highest?float.NegativeInfinity:float.PositiveInfinity;
            for(int i=0;i<indices.Length;i+=3)
            {
                var a=vertices[indices[i]];var b=vertices[indices[i+1]];var c=vertices[indices[i+2]];
                float det=(b.Z-c.Z)*(a.X-c.X)+(c.X-b.X)*(a.Z-c.Z);if(MathF.Abs(det)<1e-10f)continue;
                float u=((b.Z-c.Z)*(x-c.X)+(c.X-b.X)*(z-c.Z))/det;
                float v=((c.Z-a.Z)*(x-c.X)+(a.X-c.X)*(z-c.Z))/det;
                if(u<-.00001f||v<-.00001f||u+v>1.00001f)continue;
                float y=u*a.Y+v*b.Y+(1-u-v)*c.Y;result=highest?MathF.Max(result,y):MathF.Min(result,y);
            }
            return result;
        }
        foreach(float z in new[]{-.115f,-.085f,-.045f,0f,.04f,.065f})foreach(float x in new[]{-.025f,0f,.025f})
        {
            float upperBottom=HeightAt(BootLast(false),x,z,false),soleTop=HeightAt(BootLast(true),x,z,true);
            if(!float.IsFinite(upperBottom)||!float.IsFinite(soleTop)||upperBottom-soleTop>.0015f||soleTop-upperBottom>.003f)
                throw new InvalidOperationException("Shoe upper separates from outsole at toe, waist or heel");
        }
        var laces=BootLaces().SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        foreach(var point in laces)
        {
            float gap=point.Y-HeightAt(BootLast(false),point.X,point.Z,true);
            if(!float.IsFinite(gap)||gap<-.0008f||gap>.004f)
                throw new InvalidOperationException("Lace leaves or penetrates the actual shoe upper: "+gap);
        }
        var soleBounds=BootLast(true).GetAabb();
        float footY=ResidentRig.HipHeight-ResidentRig.ThighLength-ResidentRig.ShinLength;
        if(MathF.Abs(footY+soleBounds.Position.Y)>.002f||soleBounds.Size.Z<.25f)
            throw new InvalidOperationException("Boot sole floats in the rest pose or loses anatomical length");
        GD.Print("FOOTWEAR_CONTACT_PASS: actual upper/outsole/lace triangle contact, rest ground contact and anatomical length");
        GD.Print("ORGANIC_DETAILS_PASS: shoe lasts, soles, cupped ears, tapered antlers, finite geometry and shared meshes");
    }
}
