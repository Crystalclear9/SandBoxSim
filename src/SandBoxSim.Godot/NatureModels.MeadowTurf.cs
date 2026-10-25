using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private ShaderMaterial? _meadowTurfMaterial;
    internal Mesh MeadowTurf()
    {
        const string key="meadow-turf";
        if(_meshes.TryGetValue(key,out var cached))return cached;
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        for(int blade=0;blade<24;blade++)
        {
            float angle=blade*2.399963f,radius=.16f*MathF.Sqrt((blade+.5f)/24);
            var forward=new Vector3(MathF.Cos(angle),0,MathF.Sin(angle));
            var across=new Vector3(-forward.Z,0,forward.X);
            var root=forward*radius;
            float height=.18f+(blade*7%17)/16f*.18f;
            float bend=.045f+(blade*11%13)/12f*.14f,width=.006f+(blade%5)*.002f;
            Vector3 Point(int row,int edge)
            {
                float t=row/4f;
                float taper=(.55f+.45f*MathF.Sin(t*MathF.PI))*(1-t);
                return root+Vector3.Up*(height*(t-.16f*t*t*t)+.002f*MathF.Sin(t*MathF.PI)*(1-MathF.Abs(edge)))
                    +forward*bend*t*t+across*width*edge*taper;
            }
            void Triangle(int ar,int ae,int br,int be,int cr,int ce)
            {
                var a=Point(ar,ae);var b=Point(br,be);var c=Point(cr,ce);
                var normal=(c-a).Cross(b-a);if(normal.LengthSquared()<1e-12f)return;
                if(normal.Dot(-forward)<0){(br,cr)=(cr,br);(be,ce)=(ce,be);normal=-normal;}
                normal=normal.Normalized();
                float dry=blade%7==0?1f:0f;
                var pigment=new Color(.115f,.205f,.061f).Lerp(new(.235f,.215f,.11f),dry*.65f);
                foreach(var v in new[]{(ar,ae),(br,be),(cr,ce)})
                {
                    float t=v.Item1/4f;
                    tool.SetNormal(normal);tool.SetUV(new((v.Item2+1)*.5f,t));
                    tool.SetColor(pigment*(.72f+.28f*t));tool.AddVertex(Point(v.Item1,v.Item2));
                }
            }
            for(int row=0;row<4;row++)foreach(int side in new[]{-1,1})
            {Triangle(row,0,row,side,row+1,0);Triangle(row,side,row+1,side,row+1,0);}
        }
        tool.Index();var mesh=tool.Commit();
        var shader=new Shader {Code=@"shader_type spatial;render_mode cull_disabled;
uniform bool soil_bound=false;uniform sampler2D soil:filter_nearest,repeat_disable;uniform vec2 map_size=vec2(1.0);
varying float blade_t;
void vertex(){
    vec3 root=(MODEL_MATRIX*vec4(0,0,0,1)).xyz;blade_t=UV.y;
    float fade=1.0-smoothstep(24.0,32.0,distance(root,CAMERA_POSITION_WORLD));
    float wear=soil_bound?texture(soil,clamp(root.xz*.5/map_size,vec2(0),vec2(1))).g:0.0;
    float scale=max(.025,fade*(1.0-wear*.85));
    vec2 bend=vec2(sin(TIME*.8+root.x*.21+root.z*.13)*.020,cos(TIME*.65+root.z*.19)*.010);
    VERTEX.xz+=bend*blade_t*blade_t*fade;VERTEX.y*=scale;
    vec2 derivative=2.0*blade_t*fade/.30*bend;
    NORMAL=normalize(vec3(NORMAL.x,(NORMAL.y-dot(derivative,NORMAL.xz))/scale,NORMAL.z));
}
vec3 display_albedo(vec3 linear_colour){
    // Compatibility decodes ALBEDO after this stage; vertex pigments are
    // authored as linear reflectance. BACKLIGHT remains linear in that path.
    return mix(linear_colour*12.92,1.055*pow(linear_colour,vec3(1.0/2.4))-.055,step(vec3(.0031308),linear_colour));
}
void fragment(){
    // GLES3 cull_disabled already performs DO_SIDE_CHECK before user code.
    if(!OUTPUT_IS_SRGB&&!FRONT_FACING)NORMAL=-NORMAL;
    float veins=cos(UV.x*25.13274)/(1.0+pow(fwidth(UV.x)*25.13274,2.0));
    vec3 leaf_linear=COLOR.rgb*(1.0+veins*.035);
    ALBEDO=OUTPUT_IS_SRGB?display_albedo(leaf_linear):leaf_linear;
    BACKLIGHT=leaf_linear*vec3(.28,.34,.15);
    ROUGHNESS=.82;SPECULAR=.22;
}"};
        _meadowTurfMaterial=new ShaderMaterial {Shader=shader};
        mesh.SurfaceSetMaterial(0,_meadowTurfMaterial);
        _meshes[key]=mesh;return mesh;
    }
    internal void BindMeadowGround(Texture2D soil,Vector2 size)
    {
        MeadowTurf();_meadowTurfMaterial!.SetShaderParameter("soil_bound",true);
        _meadowTurfMaterial.SetShaderParameter("soil",soil);_meadowTurfMaterial.SetShaderParameter("map_size",size);
    }
    private void ValidateMeadowTurf()
    {
        var mesh=MeadowTurf();var data=mesh.SurfaceGetArrays(0);
        var points=data[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=data[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var index=data[(int)Mesh.ArrayType.Index].AsInt32Array();var colours=data[(int)Mesh.ArrayType.Color].AsColorArray();
        if(index.Length/3!=336||points.Length!=normals.Length||points.Length!=colours.Length||MeadowTurf()!=mesh)
            throw new InvalidOperationException("Meadow turf lost its shared bounded geometry");
        foreach(var p in points)if(!p.IsFinite()||p.Y<0||p.Y>.37f)throw new InvalidOperationException("Meadow blade loses metric ground contact");
        for(int i=0;i<index.Length;i+=3)
        {
            int a=index[i],b=index[i+1],c=index[i+2];var area=(points[c]-points[a]).Cross(points[b]-points[a]);
            if(area.LengthSquared()<1e-12f||area.Dot(normals[a]+normals[b]+normals[c])<=0)
                throw new InvalidOperationException("Meadow blade has reversed or degenerate geometry");
        }
        GD.Print("MEADOW_TURF_PASS: 24 rooted curved folded blades, 336 triangles, finite outward attributes and shared material/cache");
    }
}
