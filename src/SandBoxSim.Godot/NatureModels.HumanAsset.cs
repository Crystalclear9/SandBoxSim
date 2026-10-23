using System;
using System.Text.Json;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private int _faceVariant;
    private sealed record HumanSurface(Vector3[] Points,Vector3[] Normals,Vector2[] Uv,Color[] Colors,int[] Indices,Vector3[]? Blink=null);
    private sealed record HumanAsset(HumanSurface Head,HumanSurface Lips,HumanSurface[] Eyes,HumanSurface Lod,HumanSurface Hair,Vector3 Eye,Vector3 Mouth,
        float NoseY,Vector3[][] UpperLids,Vector3[][] LowerLids,int Rows,int Columns,float Low,float High,float[] Centers,float[] Radii,string AlbedoPath,string HairPath,float HairLuminance);
    private readonly System.Collections.Generic.Dictionary<int,HumanAsset> _humanAssets=new();
    private int FaceArchetype=>_faceVariant%8;
    private bool ChildHead=>_faceVariant>=8;
    private int HumanFamily=>FaceArchetype<2?0:FaceArchetype<5?1:2;
    private readonly System.Collections.Generic.Dictionary<(int Variant,string Kind,int Side),(Vector3[] Points,int[] Indices)> _humanProjectionParts=new();
    private static readonly string[] HumanFiles={"human-head","human-scan","human-feminine"};
    private static readonly string[] HumanHashes={
        "c4360eca573251eefa456aff4035a040a7504c5c365a6f43d91b156612a6d750",
        "2a77da0969950e1590620d67f6e1bcd81208e27a9ece88db28f2490ecef1ff01",
        "2dd7e9f888977a2d3b2a3092aa8ec1346630af9aa4a078c3d0fcd5e607bfc5b3"};
    private HumanAsset HumanData()
    {
        int family=HumanFamily;
        if(_humanAssets.TryGetValue(family,out var cached))return cached;
        string path="res://assets/models/human/"+HumanFiles[family]+".mesh.json";
        if(!FileAccess.FileExists(path))throw new InvalidOperationException("Missing attributed human mesh asset");
        string raw=FileAccess.GetFileAsString(path);
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        if(hash!=HumanHashes[family])throw new InvalidOperationException("Human mesh asset does not match the exported source");
        using var document=JsonDocument.Parse(raw);var root=document.RootElement;
        if(root.GetProperty("schemaVersion").GetInt32()!=2)throw new InvalidOperationException("Unsupported human mesh schema");
        foreach(string texture in new[]{"albedo","hairAlbedo","eyeAlbedo"})
        {
            var entry=root.GetProperty(texture);string file=entry.GetProperty("file").GetString()!;
            if(System.IO.Path.GetFileName(file)!=file)throw new InvalidOperationException("Invalid human texture path");
            string texturePath="res://assets/models/human/"+file;
            if(!ResourceLoader.Exists(texturePath))throw new InvalidOperationException("Missing human texture atlas");
            if(FileAccess.FileExists(texturePath))
            {
                string textureHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(FileAccess.GetFileAsBytes(texturePath))).ToLowerInvariant();
                if(textureHash!=entry.GetProperty("sha256").GetString())throw new InvalidOperationException("Human texture source hash mismatch");
            }
        }
        float[] Floats(JsonElement e){var values=new float[e.GetArrayLength()];int i=0;foreach(var v in e.EnumerateArray())values[i++]=v.GetSingle();return values;}
        Vector3 Vector(JsonElement e){var f=Floats(e);return new(f[0],f[1],f[2]);}
        Vector3[] Curve(JsonElement e){var curve=new Vector3[e.GetArrayLength()];int i=0;foreach(var p in e.EnumerateArray())curve[i++]=Vector(p);return curve;}
        HumanSurface Surface(JsonElement e)
        {
            var p=Floats(e.GetProperty("positions"));var n=Floats(e.GetProperty("normals"));var t=Floats(e.GetProperty("uv"));var c=Floats(e.GetProperty("colors"));
            int count=p.Length/3;
            if(p.Length%3!=0||n.Length!=p.Length||t.Length!=count*2||c.Length!=count*4)throw new InvalidOperationException("Incomplete human surface");
            var points=new Vector3[count];var normals=new Vector3[count];var uv=new Vector2[count];var colors=new Color[count];
            for(int i=0;i<count;i++){points[i]=new(p[i*3],p[i*3+1],p[i*3+2]);normals[i]=new(n[i*3],n[i*3+1],n[i*3+2]);uv[i]=new(t[i*2],t[i*2+1]);colors[i]=new(c[i*4],c[i*4+1],c[i*4+2],c[i*4+3]);}
            var index=e.GetProperty("indices");var indices=new int[index.GetArrayLength()];int k=0;
            foreach(var value in index.EnumerateArray()){int v=value.GetInt32();if(v<0||v>=count)throw new InvalidOperationException("Human surface index out of range");indices[k++]=v;}
            if(indices.Length%3!=0||count==0)throw new InvalidOperationException("Empty human surface");
            return new(points,normals,uv,colors,indices,e.TryGetProperty("blink",out var blink)?Vectors(blink):null);
        }
        Vector3[] Vectors(JsonElement e){var f=Floats(e);var result=new Vector3[f.Length/3];for(int i=0;i<result.Length;i++)result[i]=new(f[i*3],f[i*3+1],f[i*3+2]);return result;}
        var scalp=root.GetProperty("scalp");var eyes=root.GetProperty("eyes");var lids=root.GetProperty("eyelids");
        var asset=new HumanAsset(Surface(root.GetProperty("head")),Surface(root.GetProperty("lips")),new[]{Surface(eyes[0]),Surface(eyes[1])},Surface(root.GetProperty("headLod")),
            Surface(root.GetProperty("hair")),Vector(root.GetProperty("eyeCenter")),Vector(root.GetProperty("mouthCenter")),root.GetProperty("noseProbeY").GetSingle(),
            new[]{Curve(lids[0].GetProperty("upper")),Curve(lids[1].GetProperty("upper"))},
            new[]{Curve(lids[0].GetProperty("lower")),Curve(lids[1].GetProperty("lower"))},
            scalp.GetProperty("rows").GetInt32(),scalp.GetProperty("columns").GetInt32(),scalp.GetProperty("low").GetSingle(),scalp.GetProperty("high").GetSingle(),
            Floats(scalp.GetProperty("centers")),Floats(scalp.GetProperty("radii")),
            "res://assets/models/human/"+root.GetProperty("albedo").GetProperty("file").GetString(),
            "res://assets/models/human/"+root.GetProperty("hairAlbedo").GetProperty("file").GetString(),
            root.GetProperty("hairAlbedo").GetProperty("luminance").GetSingle());
        asset=asset with {Head=RepairHeadNormals(asset.Head)};
        _humanAssets[family]=asset;return asset;
    }
    private static HumanSurface RepairHeadNormals(HumanSurface source)
    {
        // UV splits must not turn a smooth face into disconnected highlights.
        // Reconstruct geometric normals across coincident atlas vertices and
        // repair authored normals that disagree with the exported surface.
        (int,int,int) Key(Vector3 p)=>((int)MathF.Round(p.X*1000000),(int)MathF.Round(p.Y*1000000),(int)MathF.Round(p.Z*1000000));
        var sums=new System.Collections.Generic.Dictionary<(int,int,int),Vector3>();
        void Add(int vertex,Vector3 normal){var key=Key(source.Points[vertex]);sums.TryGetValue(key,out var value);sums[key]=value+normal;}
        for(int i=0;i<source.Indices.Length;i+=3)
        {
            int a=source.Indices[i],b=source.Indices[i+1],c=source.Indices[i+2];
            var normal=(source.Points[b]-source.Points[a]).Cross(source.Points[c]-source.Points[a]);
            if(normal.Dot(source.Normals[a]+source.Normals[b]+source.Normals[c])<0)normal=-normal;
            Add(a,normal);Add(b,normal);Add(c,normal);
        }
        var normals=(Vector3[])source.Normals.Clone();
        for(int i=0;i<normals.Length;i++)
        {
            var normal=sums[Key(source.Points[i])];
            if(normal.LengthSquared()>1e-20f){normal=normal.Normalized();if(normal.Dot(normals[i])<.966f)normals[i]=normal;}
        }
        return source with {Normals=normals};
    }
    private float HumanWidth(float y,out float derivative)
    {
        float low=1+((FaceArchetype%4)-1.5f)*.028f,high=1+((FaceArchetype/4)-.5f)*.040f;
        float t=Math.Clamp((y/.220f+.35f)/.55f,0,1);
        derivative=(high-low)*6*t*(1-t)/(.55f*.220f);
        float childhood=ChildHead?1+.04f*MathF.Exp(-MathF.Pow((y+.03f)/.07f,2)):1;
        return Mathf.Lerp(low,high,t*t*(3-2*t))*childhood;
    }
    private float HumanDepth=>(1+((FaceArchetype%3)-1)*.025f)*(ChildHead?.98f:1);
    private Vector3 HumanPoint(Vector3 p)
    {
        // Identity varies width/depth gently; retain the source anatomy rather
        // than independently pushing nose, brow, cheeks and jaw landmarks.
        float y=p.Y;
        if(ChildHead&&y<.010f)
        {
            float blend=Math.Clamp((y+.14f)/.055f,0,1);blend=blend*blend*(3-2*blend);
            y=.010f+(y-.010f)*(1-.10f*blend);
        }
        return new(p.X*HumanWidth(p.Y,out _),y,p.Z*HumanDepth);
    }

    private Vector3 HumanNormal(Vector3 p,Vector3 n)
    {
        // All facial parts and landmarks share the same deformation. Use its
        // local Jacobian for normals; retaining the old normals darkens seams.
        const float e=.0001f;
        var dx=(HumanPoint(p+Vector3.Right*e)-HumanPoint(p-Vector3.Right*e))/(2*e);
        var dy=(HumanPoint(p+Vector3.Up*e)-HumanPoint(p-Vector3.Up*e))/(2*e);
        var dz=(HumanPoint(p+Vector3.Back*e)-HumanPoint(p-Vector3.Back*e))/(2*e);
        return (new Basis(dx,dy,dz).Inverse().Transposed()*n).Normalized();
    }
    private Vector3 HumanEyeCenter(int side)
    {
        var p=HumanData().Eye;p.X*=side==0?-1:1;return HumanPoint(p);
    }
    private Vector3 HumanMouthCenter=>HumanPoint(HumanData().Mouth);
    private float HumanNoseY=>HumanData().NoseY;
    private float HumanPartDepth(string kind,int side,float x,float y)
    {
        var key=(_faceVariant,kind,side);
        if(!_humanProjectionParts.TryGetValue(key,out var projected))
        {
            var arrays=HumanMesh(kind,side).SurfaceGetArrays(0);var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var origin=kind=="eye"?HumanEyeCenter(side):HumanMouthCenter;
            for(int i=0;i<p.Length;i++)p[i]+=origin;
            projected=(p,arrays[(int)Mesh.ArrayType.Index].AsInt32Array());_humanProjectionParts[key]=projected;
        }
        float depth=float.PositiveInfinity;
        for(int i=0;i<projected.Indices.Length;i+=3)
        {
            var a=projected.Points[projected.Indices[i]];var b=projected.Points[projected.Indices[i+1]];var c=projected.Points[projected.Indices[i+2]];
            if(x<MathF.Min(a.X,MathF.Min(b.X,c.X))-.000001f||x>MathF.Max(a.X,MathF.Max(b.X,c.X))+.000001f||
                y<MathF.Min(a.Y,MathF.Min(b.Y,c.Y))-.000001f||y>MathF.Max(a.Y,MathF.Max(b.Y,c.Y))+.000001f)continue;
            float determinant=(b.Y-c.Y)*(a.X-c.X)+(c.X-b.X)*(a.Y-c.Y);if(MathF.Abs(determinant)<.00000000001f)continue;
            float u=((b.Y-c.Y)*(x-c.X)+(c.X-b.X)*(y-c.Y))/determinant;
            float v=((c.Y-a.Y)*(x-c.X)+(a.X-c.X)*(y-c.Y))/determinant;
            if(u<0||v<0||u+v>1)continue;
            depth=MathF.Min(depth,a.Z*u+b.Z*v+c.Z*(1-u-v));
        }
        return depth;
    }
    private Mesh HumanMesh(string kind,int side=0)
    {
        string key="human-asset:"+kind+":"+side+":"+_faceVariant;
        if(_meshes.TryGetValue(key,out var cached))return cached;
        var asset=HumanData();var source=kind=="head"?asset.Head:kind=="hair"?asset.Hair:kind=="lod"?asset.Lod:kind=="mouth"?asset.Lips:asset.Eyes[side];
        var colors=(Color[])source.Colors.Clone();
        var points=new Vector3[source.Points.Length];var normals=new Vector3[points.Length];var uv=(Vector2[])source.Uv.Clone();var blinkUv=new Vector2[points.Length];
        var origin=kind=="eye"?HumanEyeCenter(side):kind=="mouth"?HumanMouthCenter:Vector3.Zero;
        for(int i=0;i<points.Length;i++)
        {
            var p=HumanPoint(source.Points[i]);var n=HumanNormal(source.Points[i],source.Normals[i]);
            if(kind=="head"||kind=="lod")
            {
                points[i]=new(p.X/.177f,p.Y/.220f,p.Z/.195f);
                normals[i]=new Vector3(n.X*.177f,n.Y*.220f,n.Z*.195f).Normalized();
                if(source.Blink!=null){var delta=HumanPoint(source.Points[i]+source.Blink[i])-p;blinkUv[i]=new(delta.Y/.220f,delta.Z/.195f);}
            }
            else if(kind=="hair")
            {
                points[i]=new(p.X/.181f,(p.Y-.008f)/.224f,p.Z/.200f);
                normals[i]=new Vector3(n.X*.181f,n.Y*.224f,n.Z*.200f).Normalized();
            }
            else
            {
                points[i]=p-origin;normals[i]=n;
            }
        }
        var arrays=new Godot.Collections.Array();arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex]=points;arrays[(int)Mesh.ArrayType.Normal]=normals;arrays[(int)Mesh.ArrayType.TexUV]=uv;
        arrays[(int)Mesh.ArrayType.Color]=colors;arrays[(int)Mesh.ArrayType.Index]=source.Indices;
        if(source.Blink!=null)arrays[(int)Mesh.ArrayType.TexUV2]=blinkUv;
        var mesh=new ArrayMesh();mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);_meshes[key]=mesh;return mesh;
    }
    private Vector3 WrapHumanHair(Vector3 p,float angle,bool center=false)
    {
        var asset=HumanData();float worldY=p.Y*.224f+.008f;
        float row=Math.Clamp((worldY-.003f-asset.Low)/(asset.High-asset.Low)*asset.Rows-.5f,0,asset.Rows-1);
        int a=(int)row,b=Math.Min(a+1,asset.Rows-1);float blend=row-a;
        float column=(angle/MathF.Tau*asset.Columns)%asset.Columns;if(column<0)column+=asset.Columns;
        int c=(int)column,d=(c+1)%asset.Columns;float t=column-c;
        float radius=Mathf.Lerp(Mathf.Lerp(asset.Radii[a*asset.Columns+c],asset.Radii[a*asset.Columns+d],t),
            Mathf.Lerp(asset.Radii[b*asset.Columns+c],asset.Radii[b*asset.Columns+d],t),blend);
        float zCenter=Mathf.Lerp(asset.Centers[a],asset.Centers[b],blend)*HumanDepth;
        if(center)return new(0,p.Y,zCenter/.200f);
        float crown=MathF.Sqrt(Math.Clamp((asset.High+.004f-worldY)/.008f,0,1));
        radius*=crown;
        float swept=angle+(_faceVariant%2==0?1:-1)*(worldY+.02f)*6;
        float direction=_faceVariant%2==0?1:-1;
        float part=MathF.Atan2(MathF.Sin(angle-direction*.55f),MathF.Cos(angle-direction*.55f));
        float locks=.5f+.5f*MathF.Sin(swept*11+MathF.Sin(angle*3)*.65f);
        float lift=.0016f+crown*.0005f*locks;
        lift-=.0004f*MathF.Exp(-MathF.Pow(part/.13f,2))*crown;
        lift=MathF.Max(.0012f,lift);
        return new(MathF.Cos(angle)*(radius+lift)*HumanWidth(worldY,out _)/.181f,p.Y,
            (zCenter+MathF.Sin(angle)*(radius+lift)*HumanDepth)/.200f);
    }
    private void ValidateHumanAsset()
    {
        var seam=new HumanSurface(new[]{Vector3.Zero,Vector3.Right,Vector3.Up,Vector3.Zero,Vector3.Up,Vector3.Left},
            new[]{Vector3.Right,Vector3.Back,Vector3.Back,Vector3.Right,Vector3.Back,Vector3.Back},
            new Vector2[6],new Color[6],new[]{0,1,2,3,4,5});
        var repaired=RepairHeadNormals(seam);
        if(repaired.Normals[0].Dot(Vector3.Back)<.999f||repaired.Normals[3]!=repaired.Normals[0]||seam.Normals[0]!=Vector3.Right)
            throw new InvalidOperationException("Head normal repair loses smooth UV seams or mutates the source");
        int previous=_faceVariant;
        if(!FileAccess.FileExists("res://assets/models/human/LICENSE.txt"))throw new InvalidOperationException("Missing human mesh attribution");
        for(int variant=0;variant<16;variant++)
        {
            _faceVariant=variant;_facePoints=null;_faceIndices=null;
            var data=HumanData();
            if(data.Radii.Length!=data.Rows*data.Columns||data.Centers.Length!=data.Rows||data.High<=data.Low)
                throw new InvalidOperationException("Incomplete scalp surface lookup");
            foreach(float radius in data.Radii)if(!float.IsFinite(radius)||radius<=0)throw new InvalidOperationException("Invalid scalp sample");
            foreach(var mesh in new[]{HumanMesh("head"),HumanMesh("mouth"),HumanMesh("eye",0),HumanMesh("eye",1),HumanMesh("lod")})
            {
                var arrays=mesh.SurfaceGetArrays(0);var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                if(points.Length!=normals.Length)throw new InvalidOperationException("Human asset loses normals");
                for(int i=0;i<points.Length;i++)if(!points[i].IsFinite()||!normals[i].IsFinite()||normals[i].LengthSquared()<.8f)
                    throw new InvalidOperationException("Invalid human asset geometry or normal");
            }
            if(data.Lod.Indices.Length>7200||HumanMesh("head").GetAabb().Position.DistanceTo(HumanMesh("lod").GetAabb().Position)>.06f
                ||HumanMesh("head").GetAabb().Size.DistanceTo(HumanMesh("lod").GetAabb().Size)>.08f)
                throw new InvalidOperationException("Distant anatomical head exceeds its budget or changes silhouette");
            var morph=data.Head.Blink;
            if(morph==null||morph.Length!=data.Head.Points.Length)throw new InvalidOperationException("Missing source eyelid morph");
            float coverage=0;
            foreach(var delta in morph){if(!delta.IsFinite()||delta.Length()>.020f)throw new InvalidOperationException("Invalid eyelid displacement");coverage=MathF.Max(coverage,-delta.Y);}
            if(coverage<.006f||coverage>.015f)throw new InvalidOperationException("Eyelid morph cannot close its eye aperture");
            for(int side=0;side<2;side++)
                if(data.UpperLids[side].Length!=49||data.LowerLids[side].Length!=49)throw new InvalidOperationException("Incomplete anatomical aperture");
            if(ChildHead&&((Texture2D)((ShaderMaterial)AnatomicalFaceMaterial("#aa8b78")).GetShaderParameter("skin_albedo").AsGodotObject()).ResourcePath!="res://assets/models/human/human-feminine_albedo.png")
                throw new InvalidOperationException("Child head inherited the adult male skin atlas");
        }
        _faceVariant=previous;_facePoints=null;_faceIndices=null;
        GD.Print("HUMAN_ASSET_PASS: three attributed anatomical heads, eight adult/eight child variants, aperture-fitted eyelid closure, child skin, scalp lookup, finite geometry and normals");
    }
}
