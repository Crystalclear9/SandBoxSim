using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private static Vector3 BrambleCane(int cane,float t)
    {
        float angle=cane*2.399963f,height=.64f+(cane%4)*.055f;
        var outward=new Vector3(MathF.Cos(angle),0,MathF.Sin(angle));
        // Woody canes arch away from their common crown, rather than standing
        // as seven identical vertical sticks. Tips remain above this young clump.
        return outward*(.025f*t+.44f*t*t)+Vector3.Up*(height*MathF.Sin(t*MathF.PI*.73f));
    }
    private static void BrambleLeaf(SurfaceTool tool,Vector3 root,Vector3 direction,float length,float width,float tone,float roll)
    {
        var axis=direction.Normalized();var across=Vector3.Up.Cross(axis).Normalized().Rotated(axis,roll);var normal=across.Cross(axis).Normalized();
        if(normal.Y<0)normal=-normal;
        const int rows=20,columns=4;
        Vector3 Point(int row,int column)
        {
            float t=row/(float)rows,u=column/(float)columns*2-1;
            float edge=MathF.Pow(MathF.Max(0,MathF.Sin(MathF.PI*t)),.72f);
            // Alternating margin samples give actual serrated silhouettes;
            // the central lamina stays cupped instead of a flat triangle card.
            float tooth=row%2==0?.94f:1.055f;
            float radius=width*edge*(1+(tooth-1)*MathF.Pow(MathF.Abs(u),4));
            return root+axis*(length*t)+across*(u*radius+width*.06f*MathF.Sin(MathF.PI*t))
                +normal*(length*.055f*MathF.Sin(MathF.PI*t)*(1-.70f*u*u)-length*.11f*t*t*t);
        }
        void Triangle(int ar,int ac,int br,int bc,int cr,int cc)
        {
            var a=Point(ar,ac);var b=Point(br,bc);var c=Point(cr,cc);var n=(c-a).Cross(b-a);
            if(n.LengthSquared()<1e-13f)return;
            if(n.Dot(normal)<0){(br,cr)=(cr,br);(bc,cc)=(cc,bc);n=-n;}n=n.Normalized();
            foreach(var v in new[]{(ar,ac),(br,bc),(cr,cc)})
            {
                float t=v.Item1/(float)rows;
                tool.SetColor(new Color(tone*(.90f+.10f*t),tone,tone*.96f,1));
                tool.SetUV(new(v.Item2/(float)columns,t));tool.SetNormal(n);tool.AddVertex(Point(v.Item1,v.Item2));
            }
        }
        for(int r=0;r<rows;r++)for(int c=0;c<columns;c++)
        {Triangle(r,c,r+1,c,r,c+1);Triangle(r,c+1,r+1,c,r+1,c+1);}
    }
    private Node3D BrambleShrub(bool fruit)
    {
        var root=new Node3D();string key="arched-bramble:"+fruit;
        if(_meshes.TryGetValue(key,out var cached)){root.AddChild(new MeshInstance3D {Mesh=cached,VisibilityRangeEnd=42});return root;}
        using var leaves=new SurfaceTool();leaves.Begin(Mesh.PrimitiveType.Triangles);leaves.SetSmoothGroup(0);
        for(int cane=0;cane<7;cane++)
        {
            float angle=cane*2.399963f;var outward=new Vector3(MathF.Cos(angle),0,MathF.Sin(angle));var across=new Vector3(-outward.Z,0,outward.X);
            for(int segment=0;segment<12;segment++)
            {
                float a=segment/12f,b=(segment+1)/12f,ra=.0068f*(1-a*.84f),rb=.0068f*(1-b*.84f);
                BotanicalStem(leaves,BrambleCane(cane,a),BrambleCane(cane,b),ra,rb/ra);
            }
            for(int node=0;node<4;node++)
            {
                float t=.28f+node*.16f+(cane%2)*.015f,side=node%2==0?-1:1;
                var origin=BrambleCane(cane,t);var axis=(outward*.62f+across*side*.58f+Vector3.Up*(.25f-node*.09f)).Normalized();
                if(node>=2){var lateral=origin+axis*.045f;BotanicalStem(leaves,origin,lateral,.0028f,.70f);origin=lateral;}
                var hub=origin+axis*.055f;BotanicalStem(leaves,origin,hub,.0017f,.60f);
                BrambleLeaf(leaves,hub,axis,.104f+(cane%3)*.006f,.028f,.86f+(cane%4)*.055f,(cane%3-1)*.35f+side*.22f);
                int pairs=node==2&&cane%2==1?2:1;
                for(int pair=0;pair<pairs;pair++)foreach(float sign in new[]{-1f,1f})
                {
                    var junction=origin+axis*(.035f-pair*.018f);
                    var direction=(axis*.36f+across*sign*.78f+Vector3.Up*(.08f-pair*.08f)).Normalized();
                    var petiole=junction+direction*.018f;BotanicalStem(leaves,junction,petiole,.0011f,.6f);
                    BrambleLeaf(leaves,petiole,direction,.075f-pair*.012f,.022f-pair*.003f,.86f+(cane%3)*.055f,sign*(.45f+pair*.12f)+(cane%2)*.15f);
                }
                // Small recurved prickles grow from a cane, not detached from it.
                var prickleRoot=origin+outward*(.0068f*(1-t*.84f));
                var bend=prickleRoot+outward*.006f-Vector3.Up*.002f;
                BotanicalStem(leaves,prickleRoot,bend,.0018f,.55f);
                BotanicalStem(leaves,bend,bend+outward*.001f-Vector3.Up*.006f,.0010f,.10f);
            }
            if(fruit)for(int berry=0;berry<2;berry++)
            {
                var origin=BrambleCane(cane,.62f+berry*.16f);
                var fork=origin+outward*.044f+across*(berry==0?-.022f:.022f);
                var end=fork-Vector3.Up*(.029f+berry*.009f);
                BotanicalStem(leaves,origin,fork,.0015f,.80f);BotanicalStem(leaves,fork,end,.0012f,.72f);
                Sculpt(root,AggregateBerry(),end-Vector3.Up*.0125f,BrambleFruitMaterial());
                for(int sepal=0;sepal<5;sepal++)
                {
                    float a=sepal*MathF.Tau/5;var direction=new Vector3(MathF.Cos(a),-.22f,MathF.Sin(a)).Normalized();
                    BotanicalLeaf(leaves,end,direction,Vector3.Up.Cross(direction).Normalized(),.009f,.0025f,.001f,.78f,3);
                }
            }
        }
        leaves.GenerateNormals();leaves.Index();var leafMesh=leaves.Commit();leafMesh.SurfaceSetMaterial(0,Material(4));Sculpt(root,leafMesh,Vector3.Zero,Material(4));
        MergeResidentParts(root,key);var instance=root.GetChild<MeshInstance3D>(0);
        instance.Mesh=WithScreenLods(instance.Mesh);instance.VisibilityRangeEnd=42;_meshes[key]=instance.Mesh;return root;
    }
    private Mesh AggregateBerry()
    {
        const string key="aggregate-berry";if(_meshes.TryGetValue(key,out var cached))return cached;
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        const int rows=4,sides=8,lobes=21;
        for(int fruit=0;fruit<lobes;fruit++)
        {
            float y=1-2*(fruit+.5f)/lobes,angle=fruit*2.399963f,r=MathF.Sqrt(1-y*y);
            var center=new Vector3(MathF.Cos(angle)*r*.0068f,y*.0097f,MathF.Sin(angle)*r*.0068f);
            Vector3 Normal(int row,int col){float latitude=row*MathF.PI/rows,a=col*MathF.Tau/sides;return new(MathF.Sin(latitude)*MathF.Cos(a),MathF.Cos(latitude),MathF.Sin(latitude)*MathF.Sin(a));}
            void Vertex(int row,int col){var n=Normal(row,col);tool.SetNormal(n);tool.SetUV(new(col/(float)sides,row/(float)rows));tool.AddVertex(center+n*.0038f);}
            for(int row=0;row<rows;row++)for(int col=0;col<sides;col++)
            {
                if(row>0){Vertex(row,col);Vertex(row+1,col);Vertex(row,col+1);}
                if(row<rows-1){Vertex(row,col+1);Vertex(row+1,col);Vertex(row+1,col+1);}
            }
        }
        tool.Index();return _meshes[key]=tool.Commit();
    }
    private Material BrambleFruitMaterial()
    {
        const string key="bramble-fruit";if(_residentMaterials.TryGetValue(key,out var cached))return cached;
        // Fruit and pedicels share the same displacement after static merging;
        // otherwise the waving branch would separate from a stationary berry.
        var material=new ShaderMaterial {Shader=new Shader {Code=@"shader_type spatial;
uniform vec4 pigment:source_color;
"+BotanicalWindFunction+@"
void vertex(){VERTEX+=botanical_wind(VERTEX);}
void fragment(){ALBEDO=pigment.rgb;ROUGHNESS=.43;SPECULAR=.26;CLEARCOAT=.10;CLEARCOAT_ROUGHNESS=.28;}"}};
        material.SetShaderParameter("pigment",new Color("#463744"));return _residentMaterials[key]=material;
    }
    private void ValidateBrambleGeometry(Mesh mesh,bool fruit)
    {
        int triangles=0;
        for(int surface=0;surface<mesh.GetSurfaceCount();surface++)
        {
            var arrays=mesh.SurfaceGetArrays(surface);var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var n=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();var uv=arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();triangles+=indices.Length/3;
            if(p.Length!=n.Length||p.Length!=uv.Length)throw new InvalidOperationException("Bramble attributes disagree");
            for(int i=0;i<p.Length;i++)if(!p[i].IsFinite()||!uv[i].IsFinite()||!n[i].IsFinite()||n[i].LengthSquared()<.90f)throw new InvalidOperationException("Bramble has invalid geometry");
        }
        var bounds=mesh.GetAabb();
        if(triangles>(fruit?32000:17000)||bounds.Size.X<.85f||bounds.Size.Z<.85f||bounds.Position.Y<-.01f||bounds.Size.Y>1.05f)
            throw new InvalidOperationException($"Arched bramble lost its rooted crown, spread or geometry budget: fruit={fruit}, triangles={triangles}, bounds={bounds}");
        var berry=AggregateBerry().GetAabb();
        if(berry.Size.X>.023f||berry.Size.Y>.028f||MathF.Abs(berry.End.Y-.0125f)>.002f)
            throw new InvalidOperationException("Aggregate fruit no longer joins the pedicel at metric scale");
        GD.Print($"BRAMBLE_GEOMETRY_PASS: fruit={fruit}, triangles={triangles}, arched rooted canes, compound serrated leaves, joined aggregate fruit and finite cached attributes");
    }
}
