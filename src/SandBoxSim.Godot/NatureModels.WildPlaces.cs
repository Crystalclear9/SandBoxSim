using Godot;
using SandBoxSim.Core.Systems;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    public void ValidateWildPlaceMeshes()
    {
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
                for(int i=0;i<7;i++)Part(node,"cylinder",new Vector3(-2.6f+i*.21f,.75f,1.7f),new Vector3(.04f,1.5f,.04f),4,new Vector3(.08f,0,.11f));
                break;
            case WildPlaceKind.Berries:
                for(int i=0;i<7;i++)
                {
                    float a=i*Mathf.Tau/7;var p=new Vector3(Mathf.Cos(a)*2,.5f,Mathf.Sin(a)*2);
                    Part(node,"foliage",p,new Vector3(1.4f,1.2f,1.3f),4);
                    if(phase!=3)for(int b=0;b<3;b++)Part(node,"sphere",p+new Vector3(-.32f+b*.3f,.5f,-.2f),new Vector3(.12f,.12f,.12f),14);
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
                for(int i=0;i<4;i++)Part(node,"foliage",new Vector3(-2.7f+i*.15f,.25f+i*.32f,-1.4f),new Vector3(.65f,.55f,.45f),4);
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
                    var p=new Vector3(Mathf.Cos(a)*r,h/2,Mathf.Sin(a)*r);
                    Part(node,"cylinder",p,new(.022f,h,.022f),4,new(.06f,0,.12f));
                    Detail(node,"seed",p+Vector3.Up*(h/2-.07f),new(.055f,.21f,.055f),"#85755a");
                    Part(node,"foliage",p-Vector3.Up*(h/2-.1f),new(.3f,.25f,.3f),4);
                }
                break;
            case WildPlaceKind.FallenWood:
                Part(node,"trunk",new(0,.23f,0),new(.7f,4.3f,.7f),0,new(0,0,1.49f));
                Part(node,"cylinder",new(1.7f,.26f,0),new(.64f,.035f,.64f),1,new(0,0,Mathf.Pi/2));
                for(int i=0;i<5;i++) Part(node,"foliage",new(-1.1f+i*.5f,.15f,.38f),new(.65f,.3f,.5f),4);
                for(int i=0;i<3;i++) Timber(node,new(-1.2f+i*.6f,.23f,0),new(-1.4f+i*.6f,.50f,.75f),.065f);
                break;
            case WildPlaceKind.Ore:
                for(int i=0;i<5;i++)Part(node,"rock",new Vector3(-2+i,1.1f+i%2*.35f,0),new Vector3(1.7f,2.2f,2.5f),i%2==0?7:6,new Vector3(.2f,i*.4f,.15f));
                break;
        }
        MergeResidentParts(node,$"wild:{kind}:{phase}:{supplies}");
        return node;
    }
}
