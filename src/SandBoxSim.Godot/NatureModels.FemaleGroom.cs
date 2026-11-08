using System;
using System.Collections.Generic;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private readonly Dictionary<int,(Vector3[] Crown,int[] CrownIndices,Vector3[] Head,int[] HeadIndices)> _gatheringSupports=new();

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

    private float FemaleGatheringDepth(float x=0,float y=.040f)
    {
        if(!_gatheringSupports.TryGetValue(_faceVariant,out var support))
        {
            static (Vector3[] Points,int[] Indices) InHeadSpace(Mesh mesh,Vector3 scale,Vector3 offset)
            {
                var arrays=mesh.SurfaceGetArrays(0);
                var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                for(int i=0;i<points.Length;i++)points[i]=points[i]*scale+offset;
                return (points,arrays[(int)Mesh.ArrayType.Index].AsInt32Array());
            }
            var crown=InHeadSpace(FemaleScalp(true),new(.181f,.224f,.200f),new(0,.008f,0));
            var head=InHeadSpace(HumanMesh("head"),new(.177f,.220f,.195f),Vector3.Zero);
            _gatheringSupports[_faceVariant]=support=(crown.Points,crown.Indices,head.Points,head.Indices);
        }
        float crownDepth=GarmentDepth(support.Crown,support.CrownIndices,x,y,true);
        float skinDepth=GarmentDepth(support.Head,support.HeadIndices,x,y,true);
        // The original cap is open where its rear hair cards were gathered.
        // Use the underlying scalp there to keep the new root against the head.
        float depth=MathF.Max(crownDepth,skinDepth+.002f);
        if(!float.IsFinite(depth)||depth<.065f||depth>.145f)
            throw new InvalidOperationException($"Female gathering misses the rear scalp at {x:F3}/{y:F3}: crown={crownDepth:F4}, skin={skinDepth:F4}");
        return depth;
    }

    private static Color GatheredHairDye(string tint)=>new Color(tint).Darkened(.12f);

    private Material GatheredHairMaterial(string tint)
    {
        string key="gathered-hair:"+tint;
        if(_residentMaterials.TryGetValue(key,out var cached))return cached;
        return _residentMaterials[key]=new StandardMaterial3D
        {
            AlbedoColor=GatheredHairDye(tint),VertexColorUseAsAlbedo=true,
            Roughness=.79f,MetallicSpecular=.15f
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
            ?new[]{At(0,.040f,-.012f),At(-.015f,.029f,.012f),At(-.024f,.008f,.020f),
                At(-.012f,-.013f,.026f),At(.012f,-.014f,.026f),At(.024f,.003f,.024f),
                At(.015f,.018f,.021f),At(-.004f,.019f,.019f),At(-.009f,.005f,.011f)}
            :new[]{At(0,.040f,-.012f),At(side*.004f,.029f,.016f),At(side*.010f,-.007f,.018f),
                At(side*.018f,-.057f,.006f),At(side*.025f,FaceArchetype==5?-.114f:-.135f,-.009f)};
        // Equal-length intervals keep the plait pitch and taper independent
        // of the placement of the hand-authored curve control points.
        using var path=new Curve3D {BakeInterval=.0015f};
        for(int i=0;i<guide.Length;i++)
        {
            var handle=(guide[Math.Min(i+1,guide.Length-1)]-guide[Math.Max(i-1,0)])/6;
            path.AddPoint(guide[i],-handle,handle);
        }
        float length=path.GetBakedLength();
        Vector3 Axis(float t)=>path.SampleBaked(Math.Clamp(t,0,1)*length,true);
        (Vector3 Right,Vector3 Depth) Frame(float t)
        {
            var tangent=(Axis(t+.003f)-Axis(t-.003f)).Normalized();
            if(knot)
            {
                var depth=(Vector3.Back-tangent*tangent.Z).Normalized();
                return(depth.Cross(tangent),depth);
            }
            var right=(Vector3.Right-tangent*tangent.X).Normalized();
            return(right,tangent.Cross(right));
        }
        (float Orbit,float Radius) Width(float t)
        {
            float gathered=Mathf.SmoothStep(.05f,.23f,t);
            float taper=1-Mathf.SmoothStep(knot?.75f:.65f,1,t);
            float fullness=Mathf.Lerp(.74f,1,gathered);
            float tied=knot?0:Mathf.SmoothStep(.793f,.827f,t)*(1-Mathf.SmoothStep(.853f,.907f,t));
            return((knot?.0055f:.0076f)*gathered*Mathf.Lerp(.08f,1,taper)*(1-.70f*tied),
                (knot?.0080f:.0090f)*fullness*Mathf.Lerp(.08f,1,taper)*(1-.14f*tied));
        }
        const int locks=3;
        int rows=distant?24:52,columns=distant?6:10;
        using var surface=new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        surface.SetSmoothGroup(0);
        float Station(float row)=>Mathf.Lerp(.075f,1,row/rows);
        for(int strand=0;strand<locks;strand++)
        {
            // Use alternating over/under crossings in the back-facing
            // profile, with a compact twist for the coiled style.
            Vector3 Strand(float t)
            {
                var (right,depth)=Frame(t);
                float phase=Mathf.Tau*((knot?2.8f:3.5f)*t+strand/(float)locks);
                float orbit=Width(t).Orbit;
                float crossing=knot?MathF.Cos(phase):.55f*MathF.Sin(phase*2)+.45f*MathF.Cos(phase);
                return Axis(t)+right*(orbit*MathF.Sin(phase))+depth*(orbit*.73f*crossing);
            }
            Vector3 Point(int row,int column)
            {
                float t=Station(row);
                var (axisRight,_)=Frame(t);
                var tangent=(Strand(MathF.Min(t+.003f,1))-Strand(MathF.Max(t-.003f,0))).Normalized();
                var right=(axisRight-tangent*axisRight.Dot(tangent)).Normalized();
                var depth=tangent.Cross(right);
                float angle=(column%columns)*Mathf.Tau/columns;
                float radius=Width(t).Radius*(1+.038f*MathF.Cos(angle*5+t*3+strand));
                return Strand(t)+(right*MathF.Cos(angle)+depth*(MathF.Sin(angle)*(knot?.78f:.84f)))*radius;
            }
            for(int row=0;row<rows;row++)
            {
                float t=Station(row+.5f);
                float dye=.71f+strand*.033f+.022f*MathF.Cos(t*13+strand);
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
                float t=Station(row);
                var centre=Strand(t);
                var outside=(row==0?Axis(t)-Axis(t+.01f):Axis(t)-Axis(t-.01f)).Normalized();
                for(int col=0;col<columns;col++)
                    EquipmentTriangle(surface,centre,Point(row,col),Point(row,col+1),outside);
            }
        }
        // Start the gathering on measured crown triangles around the
        // original opening. Bury its wide ring in the scalp, then sweep
        // the hair into the guide before the three locks spread apart.
        const int rootRows=5;
        const float join=.21f;
        var root=new Vector3[columns];
        for(int col=0;col<columns;col++)
        {
            float angle=col*Mathf.Tau/columns;
            float x=.021f*MathF.Cos(angle),rise=MathF.Sin(angle);
            float y=.043f+rise*(rise>0?.024f:.018f);
            root[col]=new(x,y,FemaleGatheringDepth(x,y)-.0025f);
        }
        Vector3 Gather(int row,int col)
        {
            int column=col%columns;
            float t=row/(float)rootRows,angle=column*Mathf.Tau/columns;
            var end=Axis(join)+new Vector3(.007f*MathF.Cos(angle),.006f*MathF.Sin(angle),0);
            var shoulder=Vector3.Back*(.012f*MathF.Sin(t*Mathf.Pi));
            return root[column].Lerp(end,t)+shoulder;
        }
        for(int row=0;row<rootRows;row++)for(int col=0;col<columns;col++)
        {
            float shade=.60f+.10f*Mathf.SmoothStep(0,rootRows,row)+.037f*MathF.Cos((col+.5f)*Mathf.Tau/columns*4);
            surface.SetColor(new Color(shade,shade*.973f,shade*.94f));
            var a=Gather(row,col);var b=Gather(row+1,col);var c=Gather(row,col+1);var d=Gather(row+1,col+1);
            var outward=new Vector3((a+b+c+d).X*.25f,(a+b+c+d).Y*.25f-.040f,.014f);
            EquipmentTriangle(surface,a,b,c,outward);
            EquipmentTriangle(surface,c,b,d,outward);
        }
        for(int col=0;col<columns;col++)
            EquipmentTriangle(surface,Axis(join),Gather(rootRows,col),Gather(rootRows,col+1),Vector3.Back);
        if(!knot)
        {
            // A short, rounded fibre tie closes the braid before its tips.
            Vector3 Tie(int row,int col)
            {
                float t=.827f+row*.013f,a=(col%columns)*Mathf.Tau/columns;
                var (right,depth)=Frame(t);
                var (orbit,radius)=Width(t);
                float edge=row==1?.0009f:.0003f;
                return Axis(t)+right*(MathF.Cos(a)*(orbit+radius+edge))
                    +depth*(MathF.Sin(a)*(.68f*orbit+.84f*radius+edge));
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
        if(knot?nearBounds.Position.Y<-.030f:nearBounds.Size.Y<.135f)
            throw new InvalidOperationException("Female style loses its compact knot or hanging braid silhouette");
        float minimumRootCover=float.PositiveInfinity;
        foreach(var mesh in new[]{near,far})
        {
            var arrays=mesh.SurfaceGetArrays(0);
            var vertices=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            for(int i=0;i<vertices.Length;i++)
                if(!vertices[i].IsFinite()||!normals[i].IsFinite()||normals[i].LengthSquared()<.8f)
                    throw new InvalidOperationException("Female gathered hair contains a collapsed bundle surface");
            for(int row=-2;row<=4;row++)for(int col=-2;col<=2;col++)
            {
                float x=col*.004f,y=.040f+row*.004f;
                float cover=GarmentDepth(vertices,indices,x,y,true)-FemaleGatheringDepth(x,y);
                if(!float.IsFinite(cover)||cover<.0015f)
                    throw new InvalidOperationException($"Gathered hair exposes its scalp join: archetype={FaceArchetype}, x={x:F3}, y={y:F3}, cover={cover:F4}");
                minimumRootCover=MathF.Min(minimumRootCover,cover);
            }
        }
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
        GD.Print($"FEMALE_GROOM_JOIN_PASS: archetype={FaceArchetype}, 70 actual scalp rays, minimum overlapping hair depth={minimumRootCover*1000:F2} mm");
    }
}
