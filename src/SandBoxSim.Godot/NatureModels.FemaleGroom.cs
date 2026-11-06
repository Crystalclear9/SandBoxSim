using System;
using System.Collections.Generic;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private Mesh FemaleScalp(bool distant)
    {
        string key="female-scalp:"+_faceVariant+":"+distant;
        if(_meshes.TryGetValue(key,out var cached))return cached;
        var arrays=HumanMesh("hair").SurfaceGetArrays(0);
        var source=HairlineSource();
        var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        for(int i=0;i<points.Length;i++)
        {
            if(source.Metrics[i].X<0)continue;
            var p=points[i];var fitted=SoftenGatheringCap(p);
            if(fitted==p)continue;
            const float step=.0001f;
            var x=(SoftenGatheringCap(p+Vector3.Right*step)-SoftenGatheringCap(p-Vector3.Right*step))/(2*step);
            var y=(SoftenGatheringCap(p+Vector3.Up*step)-SoftenGatheringCap(p-Vector3.Up*step))/(2*step);
            var z=(SoftenGatheringCap(p+Vector3.Back*step)-SoftenGatheringCap(p-Vector3.Back*step))/(2*step);
            points[i]=fitted;
            normals[i]=(new Basis(x,y,z).Inverse().Transposed()*normals[i]).Normalized();
        }
        arrays[(int)Mesh.ArrayType.Vertex]=points;
        arrays[(int)Mesh.ArrayType.Normal]=normals;
        arrays[(int)Mesh.ArrayType.Index]=distant?source.CrownIndices!:source.GroomIndices!;
        var result=new ArrayMesh();
        result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);
        return _meshes[key]=result;
    }

    private static Vector3 SoftenGatheringCap(Vector3 point)
    {
        float x=MathF.Abs(point.X)*.181f,y=point.Y*.224f+.008f,z=point.Z*.200f;
        // Ease the old pointed gathering into the new low profile. Work
        // well outside the rear scalp; the temples and root edge stay put.
        float middle=Mathf.SmoothStep(.012f,.032f,y)*(1-Mathf.SmoothStep(.058f,.083f,y));
        float width=1-Mathf.SmoothStep(.015f,.035f,x);
        float shoulder=Mathf.SmoothStep(.088f,.109f,z);
        point.Z-=.008f*middle*width*shoulder/.200f;
        return point;
    }

    private float FemaleGatheringDepth()
    {
        var crown=FemaleScalp(true).SurfaceGetArrays(0);
        var p=crown[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var ids=crown[(int)Mesh.ArrayType.Index].AsInt32Array();
        float depth=GarmentDepth(p,ids,0,(.040f-.008f)/.224f,true)*.200f;
        if(!float.IsFinite(depth)||depth<.07f||depth>.13f)
            throw new InvalidOperationException("Female gathering root misses the fitted rear scalp");
        return depth;
    }

    private Material GatheredHairMaterial(string tint)
    {
        string key="gathered-hair:"+tint;
        if(_residentMaterials.TryGetValue(key,out var cached))return cached;
        return _residentMaterials[key]=new StandardMaterial3D
        {
            AlbedoColor=new Color(tint).Darkened(.10f),VertexColorUseAsAlbedo=true,
            Roughness=.74f,MetallicSpecular=.17f
        };
    }

    private Mesh FemaleGatheredHair(bool distant)
    {
        string key="female-gathered:"+_faceVariant+":"+distant;
        if(_meshes.TryGetValue(key,out var cached))return cached;
        bool knot=FaceArchetype==6;
        float side=FaceArchetype==5?-1:1;
        float scalp=FemaleGatheringDepth();
        Vector3 At(float x,float y,float depth)=>new(x,y,scalp+depth);
        var guide=knot
            ?new[]{At(0,.040f,-.012f),At(-.015f,.034f,.007f),At(-.026f,.016f,.017f),
                At(-.019f,-.010f,.024f),At(.008f,-.017f,.024f),At(.024f,-.001f,.017f),
                At(.018f,.020f,.013f),At(-.003f,.020f,.009f),At(-.005f,.005f,.004f)}
            :new[]{At(0,.040f,-.012f),At(side*.003f,.025f,.016f),At(side*.009f,-.014f,.021f),
                At(side*.017f,-.058f,.017f),At(side*.021f,FaceArchetype==5?-.114f:-.135f,.015f)};
        Vector3 Axis(float t)
        {
            float position=Math.Clamp(t,0,1)*(guide.Length-1);
            int i=Math.Min((int)position,guide.Length-2);
            return guide[i].CubicInterpolate(guide[i+1],guide[Math.Max(0,i-1)],guide[Math.Min(guide.Length-1,i+2)],position-i);
        }
        (Vector3 Right,Vector3 Depth) Frame(float t)
        {
            var tangent=(Axis(t+.001f)-Axis(t-.001f)).Normalized();
            var right=Vector3.Back.Cross(tangent).Normalized();
            return(right,tangent.Cross(right));
        }
        (float Orbit,float Radius) Width(float t)
        {
            float gathered=Mathf.SmoothStep(0,.14f,t);
            float taper=1-Mathf.SmoothStep(knot?.64f:.62f,1,t);
            float fullness=Mathf.Lerp(.88f,1,gathered);
            return((knot?.0064f:.0081f)*fullness*Mathf.Lerp(.08f,1,taper),
                (knot?.0068f:.0080f)*fullness*Mathf.Lerp(.08f,1,taper));
        }
        const int locks=3;
        int rows=distant?24:54,columns=distant?6:10;
        using var surface=new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        surface.SetSmoothGroup(0);
        for(int strand=0;strand<locks;strand++)
        {
            // A third of a turn separates the strands. Flatten the crossing
            // in depth to read as a plait across the back rather than a cable.
            Vector3 Strand(float t)
            {
                var (right,depth)=Frame(t);
                float phase=Mathf.Tau*((knot?3.3f:3.7f)*t+strand/(float)locks);
                float orbit=Width(t).Orbit;
                return Axis(t)+right*(orbit*MathF.Sin(phase))+depth*(orbit*.71f*MathF.Cos(phase));
            }
            Vector3 Point(int row,int column)
            {
                float t=row/(float)rows;
                var (right,depth)=Frame(t);
                float angle=(column%columns)*Mathf.Tau/columns;
                float radius=Width(t).Radius*(1+.038f*MathF.Cos(angle*5+t*3+strand));
                return Strand(t)+(right*MathF.Cos(angle)+depth*MathF.Sin(angle))*radius;
            }
            for(int row=0;row<rows;row++)
            {
                float t=(row+.5f)/rows;
                float dye=.81f+strand*.015f+.018f*MathF.Cos(t*19+strand);
                surface.SetColor(new Color(dye,dye*.975f,dye*.94f));
                for(int col=0;col<columns;col++)
                {
                    var a=Point(row,col);var b=Point(row+1,col);var c=Point(row,col+1);var d=Point(row+1,col+1);
                    var outward=(a+b+c+d)*.25f-Strand(t);
                    EquipmentTriangle(surface,a,b,c,outward);
                    EquipmentTriangle(surface,c,b,d,outward);
                }
            }
            foreach(int row in new[]{0,rows})
            {
                var centre=Strand(row/(float)rows);
                var outside=(row==0?Axis(0)-Axis(.01f):Axis(1)-Axis(.99f)).Normalized();
                for(int col=0;col<columns;col++)
                    EquipmentTriangle(surface,centre,Point(row,col),Point(row,col+1),outside);
            }
        }
        if(!knot)
        {
            // A short, rounded fibre tie closes the braid before its tips.
            Vector3 Tie(int row,int col)
            {
                float t=.827f+row*.013f,a=(col%columns)*Mathf.Tau/columns;
                var (right,depth)=Frame(t);
                var (orbit,radius)=Width(t);
                return Axis(t)+(right*MathF.Cos(a)+depth*MathF.Sin(a))*(orbit+radius+(row==1?.0009f:.0003f));
            }
            surface.SetColor(new Color(.78f,.65f,.48f));
            for(int row=0;row<2;row++)for(int col=0;col<columns;col++)
            {
                var a=Tie(row,col);var b=Tie(row+1,col);var c=Tie(row,col+1);var d=Tie(row+1,col+1);
                var outward=(a+b+c+d)*.25f-Axis(.827f+(row+.5f)*.013f);
                EquipmentTriangle(surface,a,b,c,outward);
                EquipmentTriangle(surface,c,b,d,outward);
            }
        }
        surface.GenerateNormals();
        surface.Index();
        return _meshes[key]=surface.Commit();
    }

    private void ValidateFemaleGatheredHair()
    {
        var near=FemaleGatheredHair(false);var far=FemaleGatheredHair(true);
        int Triangles(Mesh mesh)=>((ArrayMesh)mesh).SurfaceGetArrayIndexLen(0)/3;
        if(near!=FemaleGatheredHair(false)||far!=FemaleGatheredHair(true)||Triangles(near)>3500||Triangles(far)>1050)
            throw new InvalidOperationException("Female groom exceeds its topology budget or loses mesh sharing");
        var nearBounds=near.GetAabb();var farBounds=far.GetAabb();
        if(nearBounds.Position.DistanceTo(farBounds.Position)>.006f||nearBounds.End.DistanceTo(farBounds.End)>.006f)
            throw new InvalidOperationException("Female hairstyle changes silhouette at the distance switch");
        float rootDepth=FemaleGatheringDepth();
        if(nearBounds.Position.Z>=rootDepth-.005f||nearBounds.End.Z<rootDepth+.012f)
            throw new InvalidOperationException("Gathered hair fails to overlap the rear scalp");
        bool knot=FaceArchetype==6;
        if(knot?nearBounds.Size.Y>.085f:nearBounds.Size.Y<.135f)
            throw new InvalidOperationException("Female style loses its compact knot or hanging braid silhouette");
        var scalp=FemaleScalp(false);
        var crown=FemaleScalp(true);
        if(Triangles(crown)<600||Triangles(crown)>=Triangles(scalp)||Triangles(scalp)>=Triangles(HumanMesh("hair")))
            throw new InvalidOperationException("Female groom loses the source scalp/card separation");
        var proxy=DistantHead("#aa8b78","#42352c");
        var merged=new HashSet<Vector3>(proxy.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array());
        foreach(var point in far.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
            if(!point.IsFinite()||!merged.Contains(point))
                throw new InvalidOperationException("The actual distant resident drops its gathered hairstyle");
        GD.Print($"FEMALE_GROOM_PASS: archetype={FaceArchetype}, scalp={Triangles(scalp)}/{Triangles(crown)}, gather={Triangles(near)}/{Triangles(far)} triangles; shared scalp, root and actual distance mesh");
    }
}
