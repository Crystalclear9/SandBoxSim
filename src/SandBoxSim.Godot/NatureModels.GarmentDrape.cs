using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private readonly float[][] _sourceHemHeight=new float[3][];
    private float SourceHemHeight(float angle)
    {
        if(_sourceHemHeight[_garmentCut]==null)
        {
            var a=ContinuousGarment().SurfaceGetArrays(0);var p=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var ids=a[(int)Mesh.ArrayType.Index].AsInt32Array();var uv=a[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
            var edges=new System.Collections.Generic.Dictionary<(int A,int B),int>();
            for(int t=0;t<ids.Length;t+=3)for(int j=0;j<3;j++)
            {
                int x=ids[t+j],y=ids[t+(j+1)%3];var key=x<y?(x,y):(y,x);edges.TryGetValue(key,out int count);edges[key]=count+1;
            }
            var hem=new float[TrunkColumns];
            for(int col=0;col<TrunkColumns;col++)
            {
                float theta=col*Mathf.Tau/TrunkColumns;var ray=new Vector2(MathF.Cos(theta),MathF.Sin(theta));float hit=float.PositiveInfinity;
                foreach(var (edge,count) in edges)
                {
                    if(count!=1||uv[edge.A].X>=0||uv[edge.B].X>=0||p[edge.A].Y>.95f||p[edge.B].Y>.95f)continue;
                    var u=p[edge.A];var v=p[edge.B];var start=new Vector2(u.X,u.Z);var span=new Vector2(v.X-u.X,v.Z-u.Z);
                    float cross=ray.Cross(span);if(MathF.Abs(cross)<1e-9f)continue;
                    float distance=start.Cross(span)/cross,t=start.Cross(ray)/cross;
                    if(distance>0&&t>=-.00001f&&t<=1.00001f)hit=MathF.Min(hit,Mathf.Lerp(u.Y,v.Y,t)-.010f);
                }
                // The open boundary is the inner facing, exactly 10 mm
                // above the visible source hem (garment_pattern.py).
                if(!float.IsFinite(hit))throw new InvalidOperationException("Missing sewn garment hem boundary");hem[col]=hit;
            }
            _sourceHemHeight[_garmentCut]=hem;
        }
        float column=Mathf.PosMod(angle,Mathf.Tau)/Mathf.Tau*TrunkColumns;
        // A negative angle very close to zero can round to exactly Tau.
        // Wrap both table indices, while retaining the fractional parameter.
        int c=(int)column%TrunkColumns;
        return Mathf.Lerp(_sourceHemHeight[_garmentCut][c],_sourceHemHeight[_garmentCut][(c+1)%TrunkColumns],column-MathF.Floor(column));
    }

    private readonly float[][] _trouserClothEnvelope=new float[2][];
    private float TrouserLayerRadius(float height,float angle)
    {
        int frame=BodyFrame==1?1:0;
        if(_trouserClothEnvelope[frame]==null)
        {
            var result=new float[TrunkRows*TrunkColumns];
            var a=ContinuousTrousers().SurfaceGetArrays(0);
            var p=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();var ids=a[(int)Mesh.ArrayType.Index].AsInt32Array();
            for(int row=0;row<TrunkRows;row++)
            {
                float y=TrunkLow+row*.005f;if(y>=.94f)continue;
                var ring=new System.Collections.Generic.List<Vector2>();
                for(int t=0;t<ids.Length;t+=3)for(int edge=0;edge<3;edge++)
                {
                    var u=p[ids[t+edge]];var v=p[ids[t+(edge+1)%3]];
                    if((u.Y-y)*(v.Y-y)>0||MathF.Abs(u.Y-v.Y)<1e-8f)continue;
                    var q=u.Lerp(v,(y-u.Y)/(v.Y-u.Y));ring.Add(new(q.X,q.Z));
                }
                ring.Sort((u,v)=>u.X==v.X?u.Y.CompareTo(v.Y):u.X.CompareTo(v.X));
                var hull=new System.Collections.Generic.List<Vector2>();
                static float Cross(Vector2 x,Vector2 b,Vector2 c)=>(b-x).Cross(c-x);
                foreach(var q in ring){while(hull.Count>=2&&Cross(hull[^2],hull[^1],q)<=0)hull.RemoveAt(hull.Count-1);hull.Add(q);}
                int lower=hull.Count;
                for(int i=ring.Count-2;i>=0;i--){var q=ring[i];while(hull.Count>lower&&Cross(hull[^2],hull[^1],q)<=0)hull.RemoveAt(hull.Count-1);hull.Add(q);}
                if(hull.Count<4)throw new InvalidOperationException("Missing actual trouser section for outer garment");
                hull.RemoveAt(hull.Count-1);
                for(int col=0;col<TrunkColumns;col++)
                {
                    float theta=col*Mathf.Tau/TrunkColumns;var ray=new Vector2(MathF.Cos(theta),MathF.Sin(theta));float radius=float.PositiveInfinity;
                    for(int i=0;i<hull.Count;i++)
                    {
                        var u=hull[i];var edge=hull[(i+1)%hull.Count]-u;float cross=ray.Cross(edge);if(MathF.Abs(cross)<1e-9f)continue;
                        float distance=u.Cross(edge)/cross,t=u.Cross(ray)/cross;
                        if(distance>0&&t>=-.00001f&&t<=1.00001f)radius=MathF.Min(radius,distance);
                    }
                    if(!float.IsFinite(radius))throw new InvalidOperationException("Open actual trouser envelope");
                    result[row*TrunkColumns+col]=radius+.006f;
                }
            }
            _trouserClothEnvelope[frame]=result;
        }
        return TrunkSample(_trouserClothEnvelope[frame],height,angle);
    }

    private static void ValidateRenderedBelt(ResidentRig rig)
    {
        var mesh=rig.DetailedBodyMesh;var cloth=mesh.SurfaceGetArrays(0);
        var p=cloth[(int)Mesh.ArrayType.Vertex].AsVector3Array();var ids=cloth[(int)Mesh.ArrayType.Index].AsInt32Array();
        float Surface(float x,float y,bool back)
        {
            float z=back?float.NegativeInfinity:float.PositiveInfinity;
            for(int t=0;t<ids.Length;t+=3)
            {
                var a=p[ids[t]];var b=p[ids[t+1]];var c=p[ids[t+2]];
                var ab=new Vector2(b.X-a.X,b.Y-a.Y);var ac=new Vector2(c.X-a.X,c.Y-a.Y);
                float determinant=ab.Cross(ac);if(MathF.Abs(determinant)<1e-10f)continue;
                var q=new Vector2(x-a.X,y-a.Y);float v=q.Cross(ac)/determinant,w=ab.Cross(q)/determinant;
                if(v<-.00001f||w<-.00001f||v+w>1.00001f)continue;
                float hit=a.Z*(1-v-w)+b.Z*v+c.Z*w;z=back?MathF.Max(z,hit):MathF.Min(z,hit);
            }
            return z;
        }
        int samples=0;
        for(int s=1;s<mesh.GetSurfaceCount();s++)
        {
            if(mesh.SurfaceGetMaterial(s) is not StandardMaterial3D material||!material.AlbedoColor.IsEqualApprox(new Color("#514237")))continue;
            foreach(var point in mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
            {
                // Central front/back belt vertices, excluding buttons above
                // the waist and the tool loop on the lateral hip.
                if(MathF.Abs(point.X)>.001f||point.Y<.86f||point.Y>1.08f)continue;
                bool back=point.Z>0;float clothZ=Surface(point.X,point.Y,back);
                float gap=back?point.Z-clothZ:clothZ-point.Z;
                if(!float.IsFinite(gap)||gap<-.001f||gap>.012f)
                    throw new InvalidOperationException($"Rendered belt is buried in or floats off the fitted shirt: {gap} m");
                samples++;
            }
        }
        if(samples<4)throw new InvalidOperationException("Missing actual front/back belt samples");
        GD.Print($"RENDERED_BELT_PASS: {samples} actual front/back samples, shirt triangle contact within -1/+12 mm");
    }

    private float[] SewnFrameReference()
    {
        if(_sewnFrameReference[_garmentCut]!=null)return _sewnFrameReference[_garmentCut];
        var samples=(float[])GarmentFrameReference().Clone();
        var arrays=ContinuousGarment().SurfaceGetArrays(0);
        var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var ids=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        var detail=arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
        // The retopologized sewn shirt is not identical to its analytic
        // construction cage. Fit its actual planar sections; using the cage
        // as a denominator preserved old chest inflation and a scalloped hem.
        for(int row=0;row<GarmentReferenceRows;row++)
        {
            float y=GarmentReferenceLow+row*.005f;
            var sections=new System.Collections.Generic.List<(Vector2 A,Vector2 B)>();
            for(int t=0;t<ids.Length;t+=3)
            {
                int a=ids[t],b=ids[t+1],c=ids[t+2];
                if(detail[a].X>0||detail[b].X>0||detail[c].X>0)continue;
                Vector2 first=default,second=default;int count=0;
                void Edge(int i,int j)
                {
                    var u=p[i];var v=p[j];
                    if((u.Y-y)*(v.Y-y)>0||MathF.Abs(u.Y-v.Y)<1e-8f)return;
                    var hit=u.Lerp(v,(y-u.Y)/(v.Y-u.Y));var q=new Vector2(hit.X,hit.Z);
                    if(count==0){first=q;count=1;}else if(q.DistanceSquaredTo(first)>1e-12f){second=q;count=2;}
                }
                Edge(a,b);Edge(b,c);Edge(c,a);
                if(count==2)sections.Add((first,second));
            }
            for(int col=0;col<TrunkColumns;col++)
            {
                float angle=col*Mathf.Tau/TrunkColumns;var ray=new Vector2(MathF.Cos(angle),MathF.Sin(angle));
                float radius=float.PositiveInfinity;
                foreach(var (a,b) in sections)
                {
                    var edge=b-a;float cross=ray.Cross(edge);if(MathF.Abs(cross)<1e-9f)continue;
                    float distance=a.Cross(edge)/cross,along=a.Cross(ray)/cross;
                    if(distance>0&&along>=-.00001f&&along<=1.00001f)radius=MathF.Min(radius,distance);
                }
                // Above the axilla a torso section can be open at its sleeve.
                // Preserve the construction reference only in those openings.
                if(float.IsFinite(radius))samples[row*TrunkColumns+col]=radius;
            }
        }
        return _sewnFrameReference[_garmentCut]=samples;
    }

    // Bind-space sewn folds. They follow the same fit as buttons, ribbons and
    // mounts; neither shader noise nor displaced floating accessory meshes.
    private float GarmentDrapeDetail(float height,float angle)
    {
        static float Bell(float distance,float width)=>MathF.Exp(-MathF.Pow(distance/width,2));
        float x=MathF.Cos(angle),front=MathF.Max(0,-MathF.Sin(angle)),back=MathF.Max(0,MathF.Sin(angle));
        float belt=FinishedBeltHeight(angle);
        float above=height-belt;
        // Narrow trough beside a broader crest: a folded panel, not a sine
        // wave passing through the entire chest. Leave the button stand flat.
        float line=MathF.Abs(x)-(.48f+.65f*above);
        float gathering=(.0035f*Bell(line,.10f)-.0016f*Bell(line-.12f,.065f))
            *Bell(above-.09f,.13f)*front*front;
        float axilla=(.0025f*Bell(MathF.Abs(x)-.77f+(height-1.12f)*1.7f,.09f)
            -.0011f*Bell(MathF.Abs(x)-.88f+(height-1.12f)*1.7f,.06f))
            *Bell(height-1.105f,.09f)*front;
        if(FittedFigure)
        {
            // Short gathers belong near the waist/armhole. Repeating their
            // two ridges across the breast apex cuts one panel into two lobes.
            float span=Mathf.SmoothStep(1.04f,1.09f,height)*(1-Mathf.SmoothStep(1.17f,1.22f,height))*front*front;
            gathering*=1-.8f*span;axilla*=1-.8f*span;
        }
        float backPleat=.0028f*Bell(MathF.Abs(x)-.32f,.10f)*back*back
            *Bell(height-(belt+.12f),.17f);
        // Under the belt the narrow gathers open into long, shallow flutes.
        // Their support is the waistband, so they fade out above it.
        float below=1-Mathf.SmoothStep(belt-.015f,belt+.025f,height);
        float hem=.0020f*MathF.Cos(angle*8+.25f)*below;
        if(FittedFigure){hem*=.45f;backPleat*=.65f;}
        float clearNeck=1-Mathf.SmoothStep(1.18f,1.22f,height);
        float sideSeam=1-Mathf.SmoothStep(.90f,1,MathF.Abs(x));
        return Math.Clamp((gathering+axilla+backPleat+hem)*clearNeck*sideSeam,-.003f,.0045f);
    }
}
