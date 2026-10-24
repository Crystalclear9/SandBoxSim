using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private Vector3[]? _satchelPoints;
    private int[]? _satchelIndices;
    private readonly System.Collections.Generic.Dictionary<(float X,float Y),float> _satchelDepths=new();
    private Mesh SatchelBody()=>Loft("satchel-body",new[]{
        new Vector4(-.098f,0,.035f,.020f),new(-.079f,0,.060f,.039f),new(-.025f,0,.074f,.045f),
        new(.045f,0,.072f,.041f),new(.087f,0,.060f,.029f),new(.108f,0,.025f,.010f)});
    private Mesh PackBody()=>Loft("leather-pack",new[]{new Vector4(-.145f,0,.10f,.045f),new(-.12f,0,.13f,.065f),new(.10f,0,.12f,.060f),new(.15f,0,.09f,.047f)});
    private Vector3[]? _packPoints;
    private int[]? _packIndices;
    private readonly System.Collections.Generic.Dictionary<(float X,float Y),float> _packDepths=new();
    private float PackDepth(float x,float y)
    {
        if(_packDepths.TryGetValue((x,y),out var cached))return cached;
        if(_packPoints==null)
        {
            var data=PackBody().SurfaceGetArrays(0);_packPoints=data[(int)Mesh.ArrayType.Vertex].AsVector3Array();_packIndices=data[(int)Mesh.ArrayType.Index].AsInt32Array();
            var rotation=new Basis(Vector3.Right,-MathF.PI/2);for(int i=0;i<_packPoints.Length;i++)_packPoints[i]=rotation*_packPoints[i];
        }
        float depth=float.NegativeInfinity;
        for(int i=0;i<_packIndices!.Length;i+=3)
        {
            var a=_packPoints[_packIndices[i]];var b=_packPoints[_packIndices[i+1]];var c=_packPoints[_packIndices[i+2]];
            float divisor=(b.Y-c.Y)*(a.X-c.X)+(c.X-b.X)*(a.Y-c.Y);if(MathF.Abs(divisor)<.0000001f)continue;
            float u=((b.Y-c.Y)*(x-c.X)+(c.X-b.X)*(y-c.Y))/divisor,v=((c.Y-a.Y)*(x-c.X)+(a.X-c.X)*(y-c.Y))/divisor;
            if(u<-.000001f||v<-.000001f||u+v>1.000001f)continue;
            depth=MathF.Max(depth,a.Z*u+b.Z*v+c.Z*(1-u-v));
        }
        if(!float.IsFinite(depth))throw new InvalidOperationException("Backpack layer outside actual body");
        _packDepths[(x,y)]=depth;return depth;
    }
    private Mesh PackFlap()
    {
        const string key="pack-flap";if(_meshes.TryGetValue(key,out var cached))return cached;
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);tool.SetSmoothGroup(0);
        // A folded leather sheet follows the top/front of the rounded pack.
        Vector3 Point(int row,int col,bool inside)
        {
            float v=row/12f,u=col/16f*2-1;
            float y=.142f-v*.140f;
            float width=Mathf.Lerp(.086f,.114f,Mathf.SmoothStep(0,.5f,v));
            float x=u*width;
            return new(x,y,PackDepth(x,y)+(inside?.001f:.0025f));
        }
        foreach(bool inside in new[]{false,true})for(int r=0;r<12;r++)for(int c=0;c<16;c++)
        {
            var a=Point(r,c,inside);var b=Point(r,c+1,inside);var d=Point(r+1,c,inside);var e=Point(r+1,c+1,inside);
            EquipmentTriangle(tool,a,b,d,inside?Vector3.Forward:Vector3.Back);EquipmentTriangle(tool,b,e,d,inside?Vector3.Forward:Vector3.Back);
        }
        for(int c=0;c<16;c++)
        {
            var a=Point(12,c,false);var b=Point(12,c+1,false);var d=Point(12,c,true);var e=Point(12,c+1,true);
            EquipmentTriangle(tool,a,d,b,Vector3.Down);EquipmentTriangle(tool,b,d,e,Vector3.Down);
        }
        foreach(int c in new[]{0,16})for(int r=0;r<12;r++)
        {
            var a=Point(r,c,false);var b=Point(r+1,c,false);var d=Point(r,c,true);var e=Point(r+1,c,true);
            EquipmentTriangle(tool,a,b,d,c==0?Vector3.Left:Vector3.Right);EquipmentTriangle(tool,b,e,d,c==0?Vector3.Left:Vector3.Right);
        }
        tool.GenerateNormals();tool.Index();var mesh=tool.Commit();_meshes[key]=mesh;return mesh;
    }
    private Mesh PackStrap(int side)
    {
        string key="pack-strap:"+side;if(_meshes.TryGetValue(key,out var cached))return cached;
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);tool.SetSmoothGroup(0);
        Vector3 Point(int row,int col,bool inside)
        {
            float x=side*.065f+(col-.5f)*.018f,y=.12f-row/24f*.232f;
            float offset=(y>.002f?.0045f:.002f)-(inside?.0015f:0);
            return new(x,y,PackDepth(x,y)+offset);
        }
        foreach(bool inside in new[]{false,true})for(int row=0;row<24;row++)
        {
            var a=Point(row,0,inside);var b=Point(row,1,inside);var c=Point(row+1,0,inside);var d=Point(row+1,1,inside);
            EquipmentTriangle(tool,a,b,c,inside?Vector3.Forward:Vector3.Back);EquipmentTriangle(tool,b,d,c,inside?Vector3.Forward:Vector3.Back);
        }
        foreach(int col in new[]{0,1})for(int row=0;row<24;row++)
        {
            var a=Point(row,col,false);var b=Point(row+1,col,false);var c=Point(row,col,true);var d=Point(row+1,col,true);
            EquipmentTriangle(tool,a,c,b,col==0?Vector3.Left:Vector3.Right);EquipmentTriangle(tool,b,c,d,col==0?Vector3.Left:Vector3.Right);
        }
        tool.GenerateNormals();tool.Index();var mesh=tool.Commit();_meshes[key]=mesh;return mesh;
    }
    private float SatchelDepth(float x,float y)
    {
        if(_satchelDepths.TryGetValue((x,y),out var cached))return cached;
        if(_satchelPoints==null)
        {
            var data=SatchelBody().SurfaceGetArrays(0);_satchelPoints=data[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            _satchelIndices=data[(int)Mesh.ArrayType.Index].AsInt32Array();
            var rotation=new Basis(Vector3.Right,-MathF.PI/2);
            for(int i=0;i<_satchelPoints.Length;i++)_satchelPoints[i]=rotation*_satchelPoints[i];
        }
        float depth=float.PositiveInfinity;
        for(int i=0;i<_satchelIndices!.Length;i+=3)
        {
            var a=_satchelPoints[_satchelIndices[i]];var b=_satchelPoints[_satchelIndices[i+1]];var c=_satchelPoints[_satchelIndices[i+2]];
            if(x<MathF.Min(a.X,MathF.Min(b.X,c.X))-.000001f||x>MathF.Max(a.X,MathF.Max(b.X,c.X))+.000001f||
                y<MathF.Min(a.Y,MathF.Min(b.Y,c.Y))-.000001f||y>MathF.Max(a.Y,MathF.Max(b.Y,c.Y))+.000001f)continue;
            float divisor=(b.Y-c.Y)*(a.X-c.X)+(c.X-b.X)*(a.Y-c.Y);
            if(MathF.Abs(divisor)<.0000001f)continue;
            float u=((b.Y-c.Y)*(x-c.X)+(c.X-b.X)*(y-c.Y))/divisor;
            float v=((c.Y-a.Y)*(x-c.X)+(a.X-c.X)*(y-c.Y))/divisor;
            if(u<-.000001f||v<-.000001f||u+v>1.000001f)continue;
            depth=MathF.Min(depth,a.Z*u+b.Z*v+c.Z*(1-u-v));
        }
        if(!float.IsFinite(depth))throw new InvalidOperationException("Satchel flap escapes its leather body");
        _satchelDepths[(x,y)]=depth;return depth;
    }
    private Mesh SatchelFlap()
    {
        const string key="satchel-flap";if(_meshes.TryGetValue(key,out var cached))return cached;
        const int rows=10,columns=16;
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);tool.SetSmoothGroup(0);
        Vector3 Point(int row,int column,bool back)
        {
            float v=row/(float)rows,u=column/(float)columns;
            float x=(u*2-1)*Mathf.Lerp(.053f,.063f,v),y=.086f-.062f*v-.003f*MathF.Sin(u*MathF.PI)*v;
            return new(x,y,SatchelDepth(x,y)-(back?.0010f:.0025f));
        }
        foreach(bool back in new[]{false,true})for(int row=0;row<rows;row++)for(int col=0;col<columns;col++)
        {
            var a=Point(row,col,back);var b=Point(row,col+1,back);var c=Point(row+1,col,back);var d=Point(row+1,col+1,back);
            EquipmentTriangle(tool,a,b,c,back?Vector3.Back:Vector3.Forward);EquipmentTriangle(tool,b,d,c,back?Vector3.Back:Vector3.Forward);
        }
        foreach(int row in new[]{0,rows})for(int col=0;col<columns;col++)
        {
            var a=Point(row,col,false);var b=Point(row,col+1,false);var c=Point(row,col,true);var d=Point(row,col+1,true);
            EquipmentTriangle(tool,a,b,c,row==0?Vector3.Up:Vector3.Down);EquipmentTriangle(tool,b,d,c,row==0?Vector3.Up:Vector3.Down);
        }
        foreach(int col in new[]{0,columns})for(int row=0;row<rows;row++)
        {
            var a=Point(row,col,false);var b=Point(row+1,col,false);var c=Point(row,col,true);var d=Point(row+1,col,true);
            EquipmentTriangle(tool,a,b,c,col==0?Vector3.Left:Vector3.Right);EquipmentTriangle(tool,b,d,c,col==0?Vector3.Left:Vector3.Right);
        }
        tool.GenerateNormals();tool.Index();var mesh=tool.Commit();_meshes[key]=mesh;return mesh;
    }
    private void ValidateLeatherGoods()
    {
        var mesh=SatchelFlap();var data=mesh.SurfaceGetArrays(0);
        var points=data[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=data[(int)Mesh.ArrayType.Normal].AsVector3Array();
        for(int i=0;i<points.Length;i++)
        {
            var p=points[i];float gap=SatchelDepth(p.X,p.Y)-p.Z;
            if(!p.IsFinite()||!normals[i].IsFinite()||gap<.0008f||gap>.0028f)
                throw new InvalidOperationException("Leather flap floats or cuts through the body");
        }
        if(mesh!=SatchelFlap()||SatchelBody()!=SatchelBody())throw new InvalidOperationException("Leather goods do not share geometry");
        foreach(var layer in new[]{PackFlap(),PackStrap(-1),PackStrap(1)})
        {
            var arrays=layer.SurfaceGetArrays(0);var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var n=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            for(int i=0;i<p.Length;i++){float gap=p[i].Z-PackDepth(p[i].X,p[i].Y);if(!p[i].IsFinite()||!n[i].IsFinite()||gap<.0004f||gap>.0046f)throw new InvalidOperationException("Backpack flap/strap leaves actual body");}
        }
        GD.Print("LEATHER_GOODS_PASS: fitted two-sided flap, closed rim, leather body, shared geometry and bounded layer gaps");
    }
}
