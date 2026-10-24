using Godot;
using SandBoxSim.Core.Systems;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    public void ValidateWildPlaceMeshes()
    {
        foreach(bool fruit in new[]{true,false})for(int repeat=0;repeat<2;repeat++)
        {
            var bush=BerryShrub(fruit);var mesh=bush.GetChild<MeshInstance3D>(0).Mesh;
            if(bush.GetChild<MeshInstance3D>(0).VisibilityRangeEnd!=42)throw new System.InvalidOperationException("Berry detail lacks its distance limit");
            if(mesh.GetAabb().Position.Y<-.01f||mesh.GetAabb().Size.Y<.5f)throw new System.InvalidOperationException("Berry canes are not rooted at ground level");
            for(int i=0;i<mesh.GetSurfaceCount();i++)if(mesh.SurfaceGetMaterial(i)==null)throw new System.InvalidOperationException("Cached berry shrub lost its material");
            bush.Free();
        }
        foreach (WildPlaceKind kind in System.Enum.GetValues<WildPlaceKind>())
        {
            var node = WildPlace(kind, 0, true);
            int meshCount=0;
            void Inspect(Node current)
            {
                if(current is MeshInstance3D instance)
                {
                    meshCount++;
                    for(int surface=0;surface<instance.Mesh.GetSurfaceCount();surface++)
                    {
                        var data=instance.Mesh.SurfaceGetArrays(surface);var points=data[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                        var uv=data[(int)Mesh.ArrayType.TexUV].AsVector2Array();var normals=data[(int)Mesh.ArrayType.Normal].AsVector3Array();
                        if(points.Length!=uv.Length||points.Length!=normals.Length)throw new System.InvalidOperationException("Wild place lost surface attributes: "+kind);
                        foreach(var point in points)if(!point.IsFinite())throw new System.InvalidOperationException("Wild place has invalid geometry");
                        foreach(var normal in normals)if(!normal.IsFinite())throw new System.InvalidOperationException("Wild place has invalid normals");
                    }
                }
                foreach(Node child in current.GetChildren())Inspect(child);
            }
            Inspect(node);if(meshCount==0)throw new System.InvalidOperationException("Wild place has no rendered geometry");
            node.Free();
        }
    }
    public Node3D WildPlace(WildPlaceKind kind, int phase, bool supplies)
    {
        var node = new Node3D();
        switch (kind)
        {
            case WildPlaceKind.Spring:
                for (int i=0;i<9;i++)
                { float a=i*Mathf.Tau/9; Part(node,"rock",new Vector3(Mathf.Cos(a)*2.1f,.28f,Mathf.Sin(a)*2.1f),new Vector3(.95f,.7f,.9f),6,new Vector3(.1f,a,.2f)); }
                for(int i=0;i<3;i++)Part(node,"reeds",new Vector3(-2.6f+i*.45f,0,1.7f),Vector3.One*(.8f+i*.08f),4,new Vector3(0,i*1.7f,0));
                break;
            case WildPlaceKind.Berries:
                for(int i=0;i<7;i++)
                {
                    float a=i*Mathf.Tau/7;var bush=BerryShrub(phase!=3);bush.Position=new(Mathf.Cos(a)*2,0,Mathf.Sin(a)*2);bush.Rotation=new(0,a,0);node.AddChild(bush);
                }
                break;
            case WildPlaceKind.OldGrove:
                var oldTree=Tree(false);oldTree.Scale=new(1.55f,1.55f,1.55f);node.AddChild(oldTree);
                for(int i=0;i<7;i++)
                {
                    float a=i*Mathf.Tau/7;
                    Part(node,"cylinder",new Vector3(Mathf.Cos(a)*.7f,.16f,Mathf.Sin(a)*.7f),new Vector3(.25f,2,.25f),0,new Vector3(Mathf.Sin(a)*1.4f,0,-Mathf.Cos(a)*1.4f));
                }
                break;
            case WildPlaceKind.Ruins:
                for(int row=0;row<4;row++) for(int column=0;column<8;column++)
                {
                    if(row>1 && (column+row)%4==0 || row==3 && column>4)continue;
                    Part(node,"box",new Vector3(-2.7f+column*.76f+(row%2)*.25f,.22f+row*.38f,-2),new Vector3(.72f,.35f,.55f),6);
                    if(column<5 && row<3)Part(node,"box",new Vector3(-2.7f,.22f+row*.38f,-1.6f+column*.76f),new Vector3(.55f,.35f,.72f),6);
                }
                foreach(float side in new[] {-1f,1f})
                    for(int row=0;row<6;row++)Part(node,"box",new Vector3(1.5f+side*.9f,.22f+row*.38f,.7f),new Vector3(.5f,.35f,.65f),6);
                for(int i=0;i<5;i++)Part(node,"box",new Vector3(.7f+i*.4f,2.6f-Mathf.Abs(i-2)*.16f,.7f),new Vector3(.43f,.42f,.65f),6,new Vector3(0,0,(i-2)*.17f));
                Part(node,"cylinder",new Vector3(-1,.25f,.4f),new Vector3(.22f,3.5f,.22f),0,new Vector3(0,0,1.35f));
                for(int i=0;i<4;i++)node.AddChild(new MeshInstance3D {Mesh=Shape("fern"),Position=new(-2.25f+i*.42f,0,-1.25f),Scale=Vector3.One*.9f,Rotation=new(0,i*1.7f,0)});
                if(supplies)for(int i=0;i<5;i++)Part(node,"rock",new Vector3(-.5f+i*.45f,.2f,1.4f),new Vector3(.6f,.5f,.7f),6);
                break;
            case WildPlaceKind.Meadow:
                for(int i=0;i<34;i++)
                {
                    float a=i*2.39996f,r=.45f+Mathf.Sqrt(i/34f)*2.8f;
                    var p=new Vector3(Mathf.Cos(a)*r,.19f,Mathf.Sin(a)*r);
                    Part(node,"cylinder",p,new(.015f,.38f,.015f),4);
                    if(phase<2)
                    {
                        string color=i%3==0 ? "#c4a9b3" : i%3==1 ? "#e0d5b8" : "#c9b172";
                        for(int petal=0;petal<5;petal++)
                        {
                            float angle=petal*Mathf.Tau/5;
                            Detail(node,"seed",p+new Vector3(Mathf.Cos(angle)*.06f,.19f,Mathf.Sin(angle)*.06f),new(.10f,.035f,.07f),color,new(0,-angle,0));
                        }
                    }
                }
                break;
            case WildPlaceKind.Wetland:
                for(int i=0;i<28;i++)
                {
                    float a=i*2.39996f,r=.7f+Mathf.Sqrt(i/28f)*2.3f,h=.7f+(i%5)*.12f;
                    Part(node,"reeds",new(Mathf.Cos(a)*r,0,Mathf.Sin(a)*r),Vector3.One*h,4,new(0,a,0));
                }
                break;
            case WildPlaceKind.FallenWood:
                Part(node,"trunk",new(0,.23f,0),new(.7f,4.3f,.7f),0,new(0,0,1.49f));
                Part(node,"cylinder",new(1.7f,.26f,0),new(.64f,.035f,.64f),1,new(0,0,Mathf.Pi/2));
                for(int i=0;i<5;i++) Part(node,"foliage",new(-1.1f+i*.5f,0,.38f),new(.65f,.3f,.5f),4);
                for(int i=0;i<3;i++) Timber(node,new(-1.2f+i*.6f,.23f,0),new(-1.4f+i*.6f,.50f,.75f),.065f);
                break;
            case WildPlaceKind.Ore:
                for(int i=0;i<5;i++)node.AddChild(new MeshInstance3D {Mesh=Shape("boulder"),Position=new(-2+i,-.04f,0),Scale=new(1.7f,1.7f+i%2*.3f,2.0f),Rotation=new(0,i*.4f,0),MaterialOverride=i%2==0?RockSurface(true):null});
                break;
        }
        MergeResidentParts(node,$"wild:{kind}:{phase}:{supplies}");
        return node;
    }
    internal Node3D BerryShrub(bool fruit)
    {
        var root=new Node3D();string key="berry-canes:"+fruit;
        if(_meshes.TryGetValue(key,out var cached)){root.AddChild(new MeshInstance3D {Mesh=cached,VisibilityRangeEnd=42});return root;}
        using var leaves=new SurfaceTool();leaves.Begin(Mesh.PrimitiveType.Triangles);
        for(int cane=0;cane<9;cane++)
        {
            float a=cane*2.39996f,height=.65f+(cane%3)*.10f;
            var outward=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));var across=new Vector3(-outward.Z,0,outward.X);
            Vector3 Center(float t)=>Vector3.Up*height*t+outward*(.10f*t+.26f*t*t);
            for(int row=0;row<5;row++)
            {
                float t=(row+1)/5f;BotanicalStem(leaves,Center(row/5f),Center(t),.008f*(1-t*.72f));
                if(row==0)continue;
                foreach(float side in new[]{-1f,1f})
                {
                    var origin=Center(t);var axis=(outward*.35f+across*side+Vector3.Up*.15f).Normalized();
                    BotanicalStem(leaves,origin,origin+axis*.04f,.0015f);
                    BotanicalLeaf(leaves,origin+axis*.03f,axis,outward,.12f,.035f,.008f,.9f+cane*.018f);
                    foreach(float leaflet in new[]{-1f,1f})
                    {
                        var node=origin+axis*.055f;
                        var direction=(axis*.50f+outward*leaflet*.70f+Vector3.Up*.12f).Normalized();
                        BotanicalStem(leaves,node,node+direction*.025f,.0010f);
                        BotanicalLeaf(leaves,node+direction*.020f,direction,across,.078f,.025f,.010f,.86f+cane*.012f);
                    }
                }
            }
            if(fruit)for(int berry=0;berry<3;berry++)
            {
                var stem=Center(.77f);var end=stem+across*((berry-1)*.035f)+outward*.045f-Vector3.Up*(.028f+berry*.012f);
                FineBeam(root,stem,end,.002f,"#5f6141");
                Sculpt(root,AggregateBerry(),end-Vector3.Up*.016f,ResidentMaterial("#463744",.48f));
                for(int sepal=0;sepal<5;sepal++)
                {
                    float angle=sepal*Mathf.Tau/5;
                    var axis=new Vector3(Mathf.Cos(angle),-.25f,Mathf.Sin(angle)).Normalized();
                    BotanicalLeaf(leaves,end,axis,Vector3.Up.Cross(axis).Normalized(),.010f,.003f,.001f,.76f,3);
                }
            }
        }
        leaves.GenerateNormals();leaves.Index();var leafMesh=leaves.Commit();leafMesh.SurfaceSetMaterial(0,Material(4));Sculpt(root,leafMesh,Vector3.Zero,Material(4));
        MergeResidentParts(root,key);
        // A leaf-only dormant bush has one mesh and needs the same cache path.
        var instance=root.GetChild<MeshInstance3D>(0);instance.Mesh=WithScreenLods(instance.Mesh);instance.VisibilityRangeEnd=42;
        _meshes[key]=instance.Mesh;
        return root;
    }
    private Mesh AggregateBerry()
    {
        const string key="aggregate-berry";if(_meshes.TryGetValue(key,out var cached))return cached;
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        const int rows=5,sides=10;
        for(int fruit=0;fruit<18;fruit++)
        {
            float y=1-2*(fruit+.5f)/18,angle=fruit*2.399963f,r=Mathf.Sqrt(1-y*y);
            var center=new Vector3(Mathf.Cos(angle)*r*.010f,y*.013f,Mathf.Sin(angle)*r*.010f);
            Vector3 Normal(int row,int col){float latitude=row*Mathf.Pi/rows,a=col*Mathf.Tau/sides;return new(Mathf.Sin(latitude)*Mathf.Cos(a),Mathf.Cos(latitude),Mathf.Sin(latitude)*Mathf.Sin(a));}
            void Vertex(int row,int col){var n=Normal(row,col);tool.SetNormal(n);tool.SetUV(new(col/(float)sides,row/(float)rows));tool.AddVertex(center+n*.0058f);}
            for(int row=0;row<rows;row++)for(int col=0;col<sides;col++)
            {
                if(row>0){Vertex(row,col);Vertex(row+1,col);Vertex(row,col+1);}
                if(row<rows-1){Vertex(row,col+1);Vertex(row+1,col);Vertex(row+1,col+1);}
            }
        }
        tool.Index();return _meshes[key]=tool.Commit();
    }
}
