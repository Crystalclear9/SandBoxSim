using Godot;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    private void RoofCourses(Node3D model,float width,float depth,float height,int material)
    {
        float half=(width+.4f)/2;
        foreach(float side in new[]{-1f,1f})
        {
            for(int row=0;row<5;row++) for(int col=0;col<8;col++)
            {
                float x=(row+.5f)*half/5, z=-(depth+.4f)/2+(col+.5f)*(depth+.4f)/8;
                Part(model,"box",new(side*x,height+half*.48f-x*.495f+.13f+(4-row)*.012f,z),
                    new(half/5*1.18f,.035f,(depth+.4f)/8*.965f),material,new(0,0,-side*.46f));
            }
            foreach(float z in new[]{-(depth+.43f)/2,(depth+.43f)/2})
                Part(model,"box",new(side*half/2,height+half*.25f,z),new(half*1.15f,.075f,.085f),0,new(0,0,-side*.46f));
            Part(model,"box",new(side*half,height-.018f,0),new(.085f,.10f,depth+.5f),0);
        }
    }
    private void LeanToDetail(Node3D model,float width,float depth,float height)
    {
        string key=$"lean-wall:{width}:{depth}:{height}";
        if(!_meshes.TryGetValue(key,out var mesh))
        {
            using var tool=new SurfaceTool(); tool.Begin(Mesh.PrimitiveType.Triangles);
            foreach(float side in new[]{-1f,1f})
            {
                float z=side*depth/2;
                Vector3 a=new(-width/2,height+.17f,z),b=new(width/2,height+.17f,z),c=new(-width/2,height+.4f-width*.09f,z),d=new(width/2,height+.4f+width*.09f,z);
                foreach(var v in new[]{a,b,c,b,d,c,c,b,a,c,d,b}) { tool.SetNormal(new(0,0,side)); tool.SetUV(new(v.X/width+.5f,v.Y-height)); tool.AddVertex(v); }
            }
            mesh=tool.Commit(); _meshes[key]=mesh;
        }
        Sculpt(model,mesh,Vector3.Zero,Material(3));
        for(int row=0;row<7;row++) for(int col=0;col<8;col++)
        {
            float x=-(width+.4f)/2+(row+.5f)*(width+.4f)/7,z=-(depth+.4f)/2+(col+.5f)*(depth+.4f)/8;
            Part(model,"box",new(x,height+.54f+x*.182f,z),new((width+.4f)/7*.945f,.025f,(depth+.4f)/8*.945f),2,new(0,0,.18f));
        }
    }
    private Node3D FinishArchitecture(Node3D model,BuildingKind kind,bool complete,uint identity,float width,float depth)
    {
        if(!complete)
        {
            foreach(float side in new[]{-1f,1f}) Timber(model,new(side*width/2,.18f,-depth/2),new(-side*width/2,1.5f,-depth/2),.055f);
        }
        else if(kind==BuildingKind.House || kind==BuildingKind.Storage)
        {
            int variant=(int)(identity%5);
            float height=kind==BuildingKind.Storage ? 1.4f : variant==3 ? 2.6f : variant==0 ? 1.8f : 2.15f;
            float z=depth/2+.1f, doorX=kind==BuildingKind.Storage ? 0 : variant%2==0 ? -.28f : .28f;
            float doorHeight=kind==BuildingKind.Storage ? 1.2f : 1.55f, centerY=kind==BuildingKind.Storage ? .6f : .95f;
            float doorWidth=kind==BuildingKind.Storage ? .85f : .62f;
            // Individual planks, lintel, jambs, iron straps and latch.
            for(int i=0;i<5;i++) Part(model,"box",new(doorX-doorWidth/2+(i+.5f)*doorWidth/5,centerY,z+.015f),new(doorWidth/5-.009f,doorHeight-.04f,.025f),1);
            foreach(float side in new[]{-1f,1f}) Part(model,"box",new(doorX+side*(doorWidth/2+.035f),centerY,z),new(.07f,doorHeight+.13f,.10f),0);
            Part(model,"box",new(doorX,centerY+doorHeight/2+.055f,z),new(doorWidth+.22f,.10f,.12f),0);
            foreach(float y in new[]{centerY-doorHeight*.29f,centerY+doorHeight*.29f})
                Part(model,"box",new(doorX,y,z+.04f),new(doorWidth*.85f,.026f,.018f),11);
            Part(model,"sphere",new(doorX+doorWidth*.3f,centerY,z+.067f),new(.033f,.033f,.025f),11);
            // Foundation masonry has visible joints; side framing supplies depth from every orbit angle.
            for(int row=0;row<2;row++) for(int i=0;i<6;i++)
            {
                float x=-width/2+(i+.5f)*width/6;
                foreach(float side in new[]{-1f,1f})
                    Part(model,"box",new(x,.045f+row*.095f,side*(depth/2+.015f)),new(width/6-.014f,.084f,.075f),6);
            }
            foreach(float side in new[]{-1f,1f})
            {
                float x=side*(width/2+.027f);
                Part(model,"box",new(x,height+.15f,0),new(.07f,.09f,depth),0);
                Part(model,"box",new(x,.3f,0),new(.08f,.09f,depth),0);
                foreach(float end in new[]{-1f,1f})
                    Part(model,"box",new(x,height/2+.18f,end*(depth/2-.07f)),new(.075f,height,.075f),0);
                Timber(model,new(x,.4f,-depth*.4f),new(x,height+.08f,depth*.4f),.045f);
            }
            if(variant==0 && kind==BuildingKind.House)
                for(int row=0;row<12;row++) foreach(float side in new[]{-1f,1f})
                    Part(model,"cylinder",new(side*width/2,.27f+row*.138f,0),new(.14f,depth+.12f,.14f),0,new(Godot.Mathf.Pi/2,0,0));
            if(variant==2 && kind==BuildingKind.House)
                for(int row=0;row<11;row++) for(int col=0;col<7;col++)
                {
                    float x=-width/2+(col+.5f)*width/7;
                    foreach(float side in new[]{-1f,1f})
                        Part(model,"box",new(x,.18f+(row+.5f)*height/11,side*(depth/2+.026f)),new(width/7-.012f,height/11-.012f,.07f),6);
                    float stoneZ=-depth/2+(col+.5f)*depth/7;
                    foreach(float side in new[]{-1f,1f})
                        Part(model,"box",new(side*(width/2+.026f),.18f+(row+.5f)*height/11,stoneZ),new(.07f,height/11-.012f,depth/7-.012f),6);
                }
            if(variant==4 && kind==BuildingKind.House) LeanToDetail(model,width,depth,height);
            // Stacked firewood, a braced barrel and rain-darkened chimney cap.
            for(int i=0;i<5;i++) Part(model,"cylinder",new(width/2+.12f,.16f+(i/3)*.12f,-depth/2+.28f+(i%3)*.11f),new(.09f,.40f,.09f),0,new(0,0,Godot.Mathf.Pi/2));
            foreach(float y in new[]{.10f,.30f}) Part(model,"cylinder",new(width/2-.13f,y,-depth/2-.07f),new(.266f,.024f,.266f),11);
            if((variant==1 || variant==3) && kind==BuildingKind.House)
            {
                for(int i=0;i<5;i++) Part(model,"box",new(-.5f,height+.2f+i*.12f,-.4f),new(.29f,.025f,.31f),7);
                Part(model,"box",new(-.5f,height+.88f,-.4f),new(.34f,.06f,.37f),6);
                Part(model,"box",new(-.5f,height+.912f,-.4f),new(.19f,.013f,.22f),10);
            }
        }
        else if(kind==BuildingKind.Farm)
        {
            foreach(float side in new[]{-1f,1f})
            {
                for(int i=0;i<5;i++) Part(model,"box",new(side*1.05f,.28f,-1+i*.5f),new(.045f,.52f,.045f),0);
                Part(model,"box",new(side*1.05f,.45f,0),new(.045f,.045f,2.15f),0);
            }
            for(int row=0;row<4;row++) for(int col=0;col<5;col++)
            {
                var root=new Vector3(-.8f+row*.5f,.30f,-.8f+col*.4f);
                foreach(float side in new[]{-1f,1f})
                    Part(model,"sphere",root+new Vector3(side*.065f,-.06f,0),new(.18f,.018f,.05f),4,new(0,0,side*.5f));
            }
        }
        else if(kind==BuildingKind.Mine)
        {
            for(int i=0;i<6;i++)
            {
                float z=.8f+i*.20f;
                foreach(float side in new[]{-1f,1f}) Part(model,"box",new(side*.24f,.17f,z),new(.028f,.026f,.22f),11);
            }
            Part(model,"box",new(.66f,.30f,1.25f),new(.45f,.30f,.56f),1);
            foreach(float x in new[]{.45f,.88f}) foreach(float z in new[]{1.04f,1.46f})
                Part(model,"cylinder",new(x,.12f,z),new(.15f,.045f,.15f),11,new(0,0,Godot.Mathf.Pi/2));
        }
        MergeResidentParts(model,$"architecture:{kind}:{complete}:{identity}"); return model;
    }
}
