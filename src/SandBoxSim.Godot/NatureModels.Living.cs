using System;
using Godot;
using SandBoxSim.Core.Environment;
namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    private Shader? _dryingShader;
    public Node3D HouseLife(uint identity,int residents,int wear)
    {
        identity%=140;float depth=BuildingFootprint(identity).Y;
        var root=new Node3D {Name="HouseLife"};var daily=new Node3D {Name="Daily"};root.AddChild(daily);
        if(residents>0)
        {
            float z=depth/2+.30f;
            Part(daily,"box",new(.65f,.28f,z),new(.62f,.08f,.25f),1);
            foreach(float x in new[]{.41f,.89f})Part(daily,"box",new(x,.13f,z),new(.045f,.26f,.17f),0);
            for(int i=0;i<Math.Min(residents,3);i++)
            {
                float x=.35f+i*.17f;
                Pottery(daily,new(x,0,z+.25f));
            }
        }
        if(wear>0)
        {
            // Surface scars and bracing follow the actual building condition.
            foreach(float side in new[]{-1f,1f})
            {
                FineBeam(daily,new(side*.74f,.40f,depth/2+.085f),new(side*.61f,.79f,depth/2+.085f),.008f,"#51493d");
                if(wear==2)Part(daily,"box",new(side*.78f,.58f,depth/2+.15f),new(.075f,1.10f,.08f),0,new(0,0,side*.15f));
            }
        }
        MergeResidentParts(daily,$"home-daily:{identity%28}:{residents}:{wear}");
        var drying=new Node3D {Name="Drying",Position=new Vector3(-.85f,0,0)};root.AddChild(drying);
        var clothes=new Node3D {Name="Clothes"};drying.AddChild(clothes);
        if(residents>0)
        {
            float z=depth/2+.52f;
            foreach(float x in new[]{-.52f,.52f})Part(drying,"cylinder",new(x,.55f,z),new(.035f,1.10f,.035f),0);
            FineBeam(drying,new(-.52f,1.08f,z),new(.52f,1.08f,z),.005f,"#ad9d80");
            _dryingShader??=new Shader {Code=@"shader_type spatial;render_mode cull_disabled;
uniform vec4 dye:source_color;
void vertex(){VERTEX.z+=sin(TIME*1.2+VERTEX.x*2.0)*.018*UV.y*UV.y;}
void fragment(){float weave=sin(UV.x*160.0)*sin(UV.y*140.0);weave/=1.0+pow(length(fwidth(UV))*160.0,2.0);ALBEDO=dye.rgb*(.98+weave*.025);ROUGHNESS=.93;SPECULAR=.14;}"};
            for(int i=0;i<Math.Min(residents,3);i++)
            {
                var material=new ShaderMaterial {Shader=_dryingShader};material.SetShaderParameter("dye",new Color(i%2==0?"#b6b0a1":"#73877e"));
                using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
                Vector3 Point(int x,int y)=>new(-.34f+i*.33f+(x/8f-.5f)*.28f,1.075f-y/8f*(.40f+i*.035f),z+.012f*MathF.Sin(x/8f*MathF.PI)*y/8f);
                void Vertex(int x,int y){surface.SetUV(new(x/8f,y/8f));surface.AddVertex(Point(x,y));}
                for(int y=0;y<8;y++)for(int x=0;x<8;x++){Vertex(x,y);Vertex(x+1,y);Vertex(x,y+1);Vertex(x+1,y);Vertex(x+1,y+1);Vertex(x,y+1);}
                surface.GenerateNormals();surface.Index();Sculpt(clothes,surface.Commit(),Vector3.Zero,material);
                foreach(float side in new[]{-1f,1f})Part(clothes,"box",new(-.34f+i*.33f+side*.12f,1.075f,z),new(.018f,.045f,.026f),1);
            }
        }
        return root;
    }
    internal static int GroundPileTier(float amount)=>amount<8?0:amount<40?1:2;
    private static Vector3 GroundPilePiece(int index)=>new((index%3-1)*.13f,.06f+index/6*.10f,(index/3%2-.5f)*.18f);
    private static Vector3 GroundPieceGrip(ResourceKind kind)=>new(0,kind==ResourceKind.Food?.13f:kind==ResourceKind.Wood?.05f:.06f,0);
    internal static Vector3 GroundPileGrip(ResourceKind kind,int tier)
        =>GroundPilePiece(tier==2?7:1)+GroundPieceGrip(kind);

    private void AddGroundPiece(Node3D root,ResourceKind kind,Vector3 p,int index)
    {
        if(kind==ResourceKind.Wood)
        {
            Part(root,"cylinder",p,new(.10f,.48f,.10f),0,new(MathF.PI/2,0,0));
            Part(root,"cylinder",p+new Vector3(0,0,-.242f),new(.095f,.008f,.095f),1,new(MathF.PI/2,0,0));
        }
        else if(kind==ResourceKind.Food)
        {
            Part(root,"sphere",p+Vector3.Up*.035f,new(.15f,.18f,.14f),8);
            Part(root,"cylinder",p+Vector3.Up*.12f,new(.052f,.025f,.052f),0);
        }
        else Part(root,"rock",p,new(.14f,.12f,.17f),kind==ResourceKind.Iron?7:6,new(0,index*.73f,0));
    }

    internal Mesh GroundPickupPiece(ResourceKind kind,int tier)
    {
        int index=tier==2?7:1;
        string key=$"stock-piece:{kind}:{index}";
        if(_meshes.TryGetValue(key,out var mesh))return mesh;
        var root=new Node3D();
        AddGroundPiece(root,kind,-GroundPieceGrip(kind),index);
        if(root.GetChildCount()==1)
        {
            var part=root.GetChild<MeshInstance3D>(0);
            var baked=new ArrayMesh();
            AppendMergedSurface(baked,part.MaterialOverride,new() {part});
            _meshes[key]=baked;
        }
        else MergeResidentParts(root,key);
        root.Free();
        return _meshes[key];
    }

    public Node3D GroundPile(ResourceKind kind,int tier)
    {
        var root=new Node3D {Name="GroundPile"};int count=3+tier*3;
        string key=$"actual-pile:{kind}:{tier}";
        if(_meshes.TryGetValue(key,out var cached)){root.AddChild(new MeshInstance3D {Mesh=cached});return root;}
        for(int i=0;i<count;i++)AddGroundPiece(root,kind,GroundPilePiece(i),i);
        MergeResidentParts(root,key);return root;
    }
}
