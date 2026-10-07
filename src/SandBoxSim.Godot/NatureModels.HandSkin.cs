using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private Mesh HandPalm(int side)
    {
        string key="hand-palm:"+side;if(_meshes.TryGetValue(key,out var cached))return cached;
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        const int rows=20,columns=32;
        Vector3 Point(int row,int col)
        {
            float t=row/(float)rows,y=.045f-t*.093f,a=col*MathF.Tau/columns;
            float width=.027f+.007f*MathF.Sin(t*MathF.PI*.85f),depth=.024f-.010f*t;
            float thumb=.008f*MathF.Exp(-MathF.Pow((y+.016f)/.025f,2))*MathF.Pow(MathF.Max(0,side*MathF.Cos(a)),4);
            return new(MathF.Cos(a)*(width+thumb),y,MathF.Sin(a)*depth+.010f*MathF.Pow(1-t,3));
        }
        for(int row=0;row<rows;row++)for(int col=0;col<columns;col++)
        {
            foreach(var point in new[]{Point(row,col),Point(row+1,col),Point(row,col+1),Point(row,col+1),Point(row+1,col),Point(row+1,col+1)})surface.AddVertex(point);
        }
        // Close the distal palm so curled fingers do not reveal an open cuff between their roots.
        for(int col=0;col<columns;col++)
        {
            surface.AddVertex(new(0,-.048f,0));surface.AddVertex(Point(rows,col+1));surface.AddVertex(Point(rows,col));
        }
        surface.GenerateNormals();surface.Index();var mesh=surface.Commit();_meshes[key]=mesh;return mesh;
    }
    private void AddHandSkin(ResidentRig rig,string skin)
    {
        for(int side=0;side<2;side++)
        {
            var skeleton=rig.HandSkeletons[side]=new Skeleton3D {Name="HandSkeleton"};rig.Hands[side].AddChild(skeleton);
            var nodes=new Node3D[16];nodes[0]=rig.Hands[side];
            for(int d=0;d<4;d++){nodes[1+d*3]=rig.Fingers[side,d];nodes[2+d*3]=rig.FingerMiddles[side,d];nodes[3+d*3]=rig.Fingertips[side,d];}
            nodes[13]=rig.Thumbs[side];nodes[14]=rig.ThumbTips[side];
            var binds=new Skin();var rest=new Transform3D[16];
            for(int bone=0;bone<16;bone++)
            {
                skeleton.AddBone("HandBone"+bone);
                int parent=bone==0?-1:bone==15?0:bone==14?13:bone==13||((bone-1)%3==0)?0:bone-1;
                if(parent>=0)skeleton.SetBoneParent(bone,parent);
                var local=bone is 0 or 15?Transform3D.Identity:nodes[bone].Transform;
                skeleton.SetBoneRest(bone,local);rest[bone]=parent<0?local:rest[parent]*local;
                binds.AddBind(bone,rest[bone].AffineInverse());
            }
            skeleton.ResetBonePoses();
            var mesh=SkinHandMesh(rig.HandSkins[side].Mesh,side,skin);
            rig.HandSkins[side].MaterialOverride=null;rig.HandSkins[side].Mesh=mesh;rig.HandSkins[side].Skin=binds;rig.HandSkins[side].Skeleton=new NodePath("../HandSkeleton");
        }
    }
    private Mesh SkinHandMesh(Mesh palm,int side,string skin)
    {
        string key="skinned-hand:"+side+skin;if(_meshes.TryGetValue(key,out var cached))return cached;
        var result=new ArrayMesh();var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);surface.SetMaterial(FaceSurfaceMaterial(skin));
        var arrays=palm.SurfaceGetArrays(0);var vertices=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        foreach(int index in indices){float wrist=Math.Clamp((vertices[index].Y-.005f)/.040f,0,1);surface.SetBones(new[]{0,15,0,0});surface.SetWeights(new[]{1-wrist,wrist,0f,0f});surface.SetNormal(normals[index]);surface.SetUV(new Vector2(.5f+vertices[index].X*10,.5f+vertices[index].Y*10));surface.AddVertex(vertices[index]);}
        void Digit(int baseBone,Vector3 origin,float scale,bool thumb)
        {
            const int rings=20,sides=16;float total=(thumb?.034f:.058f)*scale;
            void Vertex(int row,int column)
            {
                float distance=row/(float)rings*total,a=column*MathF.Tau/sides;
                float taper=distance/total;float radius=(thumb?.0072f:.006f)*(1-.23f*taper);
                float tip=1;
                if(distance>total-.005f){tip=MathF.Sqrt(MathF.Max(.000001f,1-MathF.Pow((distance-total+.005f)/.005f,2)));radius*=tip;}
                int bone=baseBone;float weight=0;
                float first=(thumb?.018f:.026f)*scale,second=.044f*scale;
                // Small knuckle volumes break the uniform tube silhouette without moving the rig.
                radius*=1+.065f*MathF.Exp(-MathF.Pow((distance-first)/(.0045f*scale),2))+(thumb?0:.05f*MathF.Exp(-MathF.Pow((distance-second)/(.0035f*scale),2)));
                if(distance>first-.006f*scale){weight=Math.Clamp((distance-first+.006f*scale)/(.012f*scale),0,1);}
                if(!thumb&&distance>second-.004f*scale){bone++;weight=Math.Clamp((distance-second+.004f*scale)/(.008f*scale),0,1);}
                surface.SetBones(new[]{bone,bone+1,0,0});surface.SetWeights(new[]{1-weight,weight,0f,0f});
                surface.SetNormal(new Vector3(MathF.Cos(a)*tip,-(1-tip),MathF.Sin(a)*tip).Normalized());surface.SetUV(new(column/(float)sides,taper));
                surface.AddVertex(origin+new Vector3(MathF.Cos(a)*radius,-distance,MathF.Sin(a)*radius));
            }
            for(int row=0;row<rings;row++)for(int col=0;col<sides;col++)
            {Vertex(row,col);Vertex(row+1,col);Vertex(row,col+1);Vertex(row,col+1);Vertex(row+1,col);Vertex(row+1,col+1);}
        }
        for(int d=0;d<4;d++)Digit(1+d*3,new(-.022f+d*.014f,-.04f,-.005f),d==0?.92f:d==1?1:d==2?.96f:.86f,false);
        Digit(13,new(side==0?-.034f:.034f,-.021f,.001f),1,true);
        surface.GenerateNormals();surface.Index();surface.Commit(result);
        var nails=new SurfaceTool();nails.Begin(Mesh.PrimitiveType.Triangles);
        nails.SetMaterial(new StandardMaterial3D {AlbedoColor=new Color(skin).Lightened(.12f),Roughness=.63f,MetallicSpecular=.14f});
        for(int digit=0;digit<5;digit++)
        {
            bool thumb=digit==4;float scale=thumb?1:digit==0?.92f:digit==1?1:digit==2?.96f:.86f;
            void Vertex(int column,int row)
            {
                float u=column/8f*2-1,v=row/8f*2-1;
                float arc=MathF.Sqrt(MathF.Max(0,1-v*v));
                nails.SetBones(new[]{thumb?14:3+digit*3,0,0,0});nails.SetWeights(new[]{1f,0f,0f,0f});nails.SetNormal(Vector3.Back);nails.SetUV(new(column/8f,row/8f));
                nails.AddVertex(new((thumb?(side==0?-.034f:.034f):-.022f+digit*.014f)+u*(thumb?.0038f:.0035f)*arc,(thumb?-.047f:-.04f-.051f*scale)+v*.005f*scale,(thumb?.0067f:-.00005f)+.00018f*MathF.Sqrt(MathF.Max(0,1-u*u))*arc));
            }
            for(int row=0;row<8;row++)for(int col=0;col<8;col++){Vertex(col,row);Vertex(col,row+1);Vertex(col+1,row);Vertex(col+1,row);Vertex(col,row+1);Vertex(col+1,row+1);}
        }
        nails.Index();nails.Commit(result);_meshes[key]=result;return result;
    }
}
