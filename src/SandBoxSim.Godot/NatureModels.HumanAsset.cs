using System;
using System.Text.Json;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private sealed record HumanSurface(Vector3[] Points,Vector3[] Normals,Vector2[] Uv,Color[] Colors,int[] Indices);
    private sealed record HumanAsset(HumanSurface Head,HumanSurface Lips,HumanSurface[] Eyes,Vector3 Eye,Vector3 Mouth,
        float NoseY,int Rows,int Columns,float Low,float High,float[] Centers,float[] Radii);
    private HumanAsset? _humanAsset;
    private readonly System.Collections.Generic.Dictionary<(int Variant,string Kind,int Side),(Vector3[] Points,int[] Indices)> _humanProjectionParts=new();
    private const string HumanAssetPath="res://assets/models/human/human-head.mesh.json";
    private const string HumanAssetHash="ee5a201c695f605baa7bcff6628daa4a311648b18c41819097e1e7247e2eab01";
    private HumanAsset HumanData()
    {
        if(_humanAsset!=null)return _humanAsset;
        if(!FileAccess.FileExists(HumanAssetPath))throw new InvalidOperationException("Missing attributed human mesh asset");
        string raw=FileAccess.GetFileAsString(HumanAssetPath);
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        if(hash!=HumanAssetHash)throw new InvalidOperationException("Human mesh asset does not match the exported source");
        using var document=JsonDocument.Parse(raw);var root=document.RootElement;
        if(root.GetProperty("schemaVersion").GetInt32()!=1)throw new InvalidOperationException("Unsupported human mesh schema");
        float[] Floats(JsonElement e){var values=new float[e.GetArrayLength()];int i=0;foreach(var v in e.EnumerateArray())values[i++]=v.GetSingle();return values;}
        Vector3 Vector(JsonElement e){var f=Floats(e);return new(f[0],f[1],f[2]);}
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
            return new(points,normals,uv,colors,indices);
        }
        var scalp=root.GetProperty("scalp");var eyes=root.GetProperty("eyes");
        _humanAsset=new(Surface(root.GetProperty("head")),Surface(root.GetProperty("lips")),new[]{Surface(eyes[0]),Surface(eyes[1])},
            Vector(root.GetProperty("eyeCenter")),Vector(root.GetProperty("mouthCenter")),root.GetProperty("noseProbeY").GetSingle(),
            scalp.GetProperty("rows").GetInt32(),scalp.GetProperty("columns").GetInt32(),scalp.GetProperty("low").GetSingle(),scalp.GetProperty("high").GetSingle(),
            Floats(scalp.GetProperty("centers")),Floats(scalp.GetProperty("radii")));
        return _humanAsset;
    }
    private float HumanWidth(float y,out float derivative)
    {
        float low=1+((_faceVariant%4)-1.5f)*.028f,high=1+((_faceVariant/4)-.5f)*.040f;
        float t=Math.Clamp((y/.220f+.35f)/.55f,0,1);
        derivative=(high-low)*6*t*(1-t)/(.55f*.220f);
        return Mathf.Lerp(low,high,t*t*(3-2*t));
    }
    private float HumanDepth=>1+((_faceVariant%3)-1)*.025f;
    private Vector3 HumanPoint(Vector3 p)=>new(p.X*HumanWidth(p.Y,out _),p.Y,p.Z*HumanDepth);
    private Vector3 HumanNormal(Vector3 p,Vector3 n)
    {
        float width=HumanWidth(p.Y,out float slope);
        return new Vector3(n.X/width,n.Y-p.X*slope*n.X/width,n.Z/HumanDepth).Normalized();
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
        var asset=HumanData();var source=kind=="head"?asset.Head:kind=="mouth"?asset.Lips:asset.Eyes[side];
        var points=new Vector3[source.Points.Length];var normals=new Vector3[points.Length];var uv=(Vector2[])source.Uv.Clone();
        var origin=kind=="eye"?HumanEyeCenter(side):kind=="mouth"?HumanMouthCenter:Vector3.Zero;
        for(int i=0;i<points.Length;i++)
        {
            var p=HumanPoint(source.Points[i]);var n=HumanNormal(source.Points[i],source.Normals[i]);
            if(kind=="head")
            {
                points[i]=new(p.X/.177f,p.Y/.220f,p.Z/.195f);
                normals[i]=new Vector3(n.X*.177f,n.Y*.220f,n.Z*.195f).Normalized();
            }
            else
            {
                points[i]=p-origin;normals[i]=n;
                if(kind=="eye")uv[i]=new(.5f+(p.X-origin.X)/.028f,.5f-(p.Y-origin.Y)/.0108f);
            }
        }
        var arrays=new Godot.Collections.Array();arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex]=points;arrays[(int)Mesh.ArrayType.Normal]=normals;arrays[(int)Mesh.ArrayType.TexUV]=uv;
        arrays[(int)Mesh.ArrayType.Color]=source.Colors;arrays[(int)Mesh.ArrayType.Index]=source.Indices;
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
        float lift=.0032f+.0010f*MathF.Sin(swept*19)*crown;
        return new(MathF.Cos(angle)*(radius+lift)*HumanWidth(worldY,out _)/.181f,p.Y,
            (zCenter+MathF.Sin(angle)*(radius+lift)*HumanDepth)/.200f);
    }
    private void ValidateHumanAsset()
    {
        var data=HumanData();int previous=_faceVariant;
        if(!FileAccess.FileExists("res://assets/models/human/LICENSE.txt"))throw new InvalidOperationException("Missing human mesh attribution");
        if(data.Radii.Length!=data.Rows*data.Columns||data.Centers.Length!=data.Rows||data.High<=data.Low)
            throw new InvalidOperationException("Incomplete scalp surface lookup");
        foreach(float radius in data.Radii)if(!float.IsFinite(radius)||radius<=0)throw new InvalidOperationException("Invalid scalp sample");
        for(int variant=0;variant<8;variant++)
        {
            _faceVariant=variant;
            foreach(var mesh in new[]{HumanMesh("head"),HumanMesh("mouth"),HumanMesh("eye",0),HumanMesh("eye",1)})
            {
                var arrays=mesh.SurfaceGetArrays(0);var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                if(points.Length!=normals.Length)throw new InvalidOperationException("Human asset loses normals");
                for(int i=0;i<points.Length;i++)if(!points[i].IsFinite()||!normals[i].IsFinite()||normals[i].LengthSquared()<.8f)
                    throw new InvalidOperationException("Invalid human asset geometry or normal");
            }
        }
        _faceVariant=previous;_facePoints=null;_faceIndices=null;
        GD.Print("HUMAN_ASSET_PASS: attributed source hash, eight head/lip/eye variants, scalp lookup, finite geometry and normals");
    }
}
