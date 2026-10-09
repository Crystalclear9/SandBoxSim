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
        GD.Print("LEATHER_GOODS_PASS: fitted two-sided flap, closed rim, leather body, shared geometry and bounded layer gaps");
    }
}
