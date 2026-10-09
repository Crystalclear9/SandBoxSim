using System;
using Godot;

namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    // Clockwise folded leaf, with a shared midrib, tapered apex and leaf-local UV.
    private static void BotanicalLeaf(SurfaceTool tool,Vector3 root,Vector3 axis,Vector3 across,float length,float width,float cup,float tone)
    {
        var normal=across.Cross(axis).Normalized();if(normal.Y<0)normal=-normal;
        Vector3 Point(int row,int edge)
        {
            float t=row/3f,w=width*MathF.Pow(MathF.Max(0,MathF.Sin(t*MathF.PI)),.8f);
            return root+axis*length*t+across*w*edge+normal*(cup*MathF.Sin(t*MathF.PI)*(1-.70f*MathF.Abs(edge)));
        }
        void Triangle(int ar,int ae,int br,int be,int cr,int ce)
        {
            var a=Point(ar,ae);var b=Point(br,be);var c=Point(cr,ce);
            var n=(c-a).Cross(b-a);if(n.LengthSquared()<1e-12f)return;
            if(n.Dot(normal)<0){(br,cr)=(cr,br);(be,ce)=(ce,be);n=-n;}
            n=n.Normalized();
            foreach(var v in new[]{(ar,ae),(br,be),(cr,ce)})
            {
                tool.SetNormal(n);tool.SetUV(new((v.Item2+1)*.5f,v.Item1/3f));
                float shade=tone*(.83f+.17f*v.Item1/3f);tool.SetColor(new(shade,shade,shade,1));tool.AddVertex(Point(v.Item1,v.Item2));
            }
        }
        for(int row=0;row<3;row++)foreach(int side in new[]{-1,1})
        {Triangle(row,0,row,side,row+1,0);Triangle(row,side,row+1,side,row+1,0);}
    }
    private static void BotanicalStem(SurfaceTool tool,Vector3 start,Vector3 end,float radius)
    {
        var axis=(end-start).Normalized();var across=axis.Cross(MathF.Abs(axis.Y)>.95f?Vector3.Right:Vector3.Up).Normalized();var up=axis.Cross(across).Normalized();
        void Vertex(int ring,int side)
        {
            float a=side*MathF.Tau/6;var normal=across*MathF.Cos(a)+up*MathF.Sin(a);
            tool.SetColor(new(1.35f,.65f,.40f,1));tool.SetNormal(normal);tool.SetUV(new(side/6f,ring));
            tool.AddVertex((ring==0?start:end)+normal*radius*(ring==0?1:.72f));
        }
        for(int side=0;side<6;side++){Vertex(0,side);Vertex(1,side);Vertex(0,side+1);Vertex(0,side+1);Vertex(1,side);Vertex(1,side+1);}
    }
    private Mesh Fern()
    {
        if(_meshes.TryGetValue("fern",out var cached))return cached;
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        for(int frond=0;frond<9;frond++)
        {
            float a=frond*2.399963f,length=.34f+(frond%3)*.06f;
            var axis=new Vector3(MathF.Cos(a),.5f,MathF.Sin(a)).Normalized();var across=new Vector3(-axis.Z,0,axis.X).Normalized();
            Vector3 Center(float t)=>axis*length*t+Vector3.Up*.16f*MathF.Sin(t*MathF.PI);
            for(int stem=0;stem<12;stem++)BotanicalStem(tool,Center(stem/12f),Center((stem+1)/12f),.0018f);
            for(int row=0;row<10;row++)foreach(int sign in new[]{-1,1})
            {
                float t=(row+.5f)/10;var root=axis*length*t+Vector3.Up*.16f*MathF.Sin(t*MathF.PI);
                var direction=(axis*.28f+across*sign).Normalized();
                BotanicalLeaf(tool,root,direction,axis,(.085f+.024f*MathF.Sin(t*MathF.PI))*(1-t*.80f),.012f*(1-t*.65f),.007f,.86f+.035f*(frond%4));
            }
        }
        tool.GenerateNormals();tool.Index();var mesh=WithScreenLods(tool.Commit());_meshes["fern"]=mesh;return mesh;
    }
    private void ValidateBotanicalModels()
    {
        foreach(string kind in new[]{"foliage","pine","fern","grass","rock"})
        {
            var mesh=Shape(kind);if(mesh!=Shape(kind))throw new InvalidOperationException("Botanical mesh cache is unstable");
            var data=mesh.SurfaceGetArrays(0);var points=data[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals=data[(int)Mesh.ArrayType.Normal].AsVector3Array();var uv=data[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            var index=data[(int)Mesh.ArrayType.Index].AsInt32Array();
            if(points.Length<30||points.Length>30000||points.Length!=normals.Length||points.Length!=uv.Length)throw new InvalidOperationException("Botanical mesh outside budget or missing attributes: "+kind);
            foreach(var p in points)if(!p.IsFinite())throw new InvalidOperationException("Botanical vertex is not finite");
            foreach(var n in normals)if(!n.IsFinite()||n.LengthSquared()<.8f)throw new InvalidOperationException("Botanical normal is degenerate");
            for(int i=0;i<index.Length;i+=3)
            {
                int a=index[i],b=index[i+1],c=index[i+2];var area=(points[c]-points[a]).Cross(points[b]-points[a]);
                if(area.LengthSquared()>1e-12f&&area.Dot(normals[a]+normals[b]+normals[c])<-.000001f)
                    throw new InvalidOperationException("Botanical triangle winding disagrees with its normal: "+kind);
            }
        }
        if(Shape("fern").GetAabb().Position.Y<-.005f||Shape("fern").GetAabb().Size.Y<.15f)throw new InvalidOperationException("Fern roots or frond height are invalid");
        GD.Print("BOTANICAL_MODELS_PASS: curved leaves/connected stems, fern/grass/rock, outward normals, finite attributes, cache and geometry budgets");
    }
    private void InstallVegetationMaterials()
    {
        var shader=new Shader {Code=@"shader_type spatial;
render_mode cull_disabled;
uniform vec4 pigment:source_color;
varying vec3 leaf_position;
void vertex(){leaf_position=VERTEX;VERTEX.x+=sin(TIME*.9+VERTEX.x*5.0+VERTEX.z*4.0)*.003*max(0.0,VERTEX.y+.5);}
void fragment(){
if(!FRONT_FACING){NORMAL=-NORMAL;}
float ridge=exp(-pow((UV.x-.5)*44.0,2.0));
float veins=cos((UV.y-abs(UV.x-.5)*.5)*74.0)/(1.0+pow(fwidth(UV.y)*74.0,2.0));
float edge=pow(abs(UV.x*2.0-1.0),5.0);
ALBEDO=pigment.rgb*COLOR.rgb*(1.02+ridge*.065+veins*.025-edge*.04);
ROUGHNESS=.72+edge*.08;SPECULAR=.24;
}"};
        foreach(var pair in new[]{(4,"#466649"),(5,"#345a4b")})
        {var material=new ShaderMaterial {Shader=shader};material.SetShaderParameter("pigment",new Color(pair.Item2));_materials[pair.Item1]=material;}
    }
}
