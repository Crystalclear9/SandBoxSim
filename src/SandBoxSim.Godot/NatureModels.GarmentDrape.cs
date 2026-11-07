using System;
using System.Collections.Generic;
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
        bool exteriorHem=BodyFrame==1;
        int key=_garmentCut+(exteriorHem?3:0);
        if(_sewnFrameReference[key]!=null)return _sewnFrameReference[key];
        var samples=(float[])GarmentFrameReference().Clone();
        var arrays=ContinuousGarment().SurfaceGetArrays(0);
        var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var ids=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        var detail=arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
        var firstSection=new int[TrunkColumns];
        Array.Fill(firstSection,-1);
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
                if(exteriorHem&&Math.Max(a,Math.Max(b,c))>=p.Length-2*SewnPanelColumns)continue;
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
                if(float.IsFinite(radius))
                {
                    if(firstSection[col]<0)firstSection[col]=row;
                    samples[row*TrunkColumns+col]=radius;
                }
            }
        }
        if(exteriorHem)
            for(int col=0;col<TrunkColumns;col++)
            {
                // The lowest edge rises around the coat vents. Extend the
                // measured outer panel into its last interpolation cell.
                int row=firstSection[col];
                if(row<0)throw new InvalidOperationException("Missing exterior garment meridian");
                float radius=samples[row*TrunkColumns+col];
                float slope=Math.Clamp(samples[(row+1)*TrunkColumns+col]-radius,-.003f,.003f);
                for(int lower=row-1;lower>=0;lower--)
                    samples[lower*TrunkColumns+col]=radius+slope*Math.Max(lower-row,-2);
            }
        return _sewnFrameReference[key]=samples;
    }

    private int[] FindFemaleHem(Vector3[] input)
    {
        var source=ContinuousGarment().SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var indices=new System.Collections.Generic.Dictionary<Vector3,int>(input.Length);
        for(int i=0;i<input.Length;i++)indices.TryAdd(input[i],i);
        // The source baker emits the first two exterior rings and the last
        // two facing rings in pattern order. SurfaceTool reindexes them.
        var hem=new int[4*SewnPanelColumns];
        int facing=source.Length-2*SewnPanelColumns;
        for(int i=0;i<2*SewnPanelColumns;i++)
        {
            hem[i]=indices[source[i]];
            hem[i+2*SewnPanelColumns]=indices[source[facing+i]];
        }
        return hem;
    }

    private static void FitFemaleHemFacing(Vector3[] points,int[] hem)
    {
        for(int col=0;col<SewnPanelColumns;col++)
        {
            var rim=points[hem[col]];
            var above=points[hem[col+SewnPanelColumns]];
            var radial=new Vector3(rim.X,0,rim.Z).Normalized();
            // Turn the existing rings around a slightly rounded edge. Keep
            // the two facing rings inside the actual exterior as it flares.
            points[hem[col]]=rim+radial*.0012f;
            points[hem[2*SewnPanelColumns+col]]=rim.Lerp(above,.085f)-radial*.0028f;
            points[hem[3*SewnPanelColumns+col]]=rim.Lerp(above,.48f)-radial*.0039f;
        }
    }

    private float FemaleOuterLayer(float cloth,float height,float angle,float front,out float trousers)
    {
        // Continue the waistband's last supported ring into the waist.
        // Slow the front sample smoothly before it reaches the end.
        float band=Math.Clamp((height-.924f)/.022f,0,1);
        float tapered=MathF.Min(height,.924f)+.022f*band*(1-.5f*band);
        float station=Mathf.Lerp(MathF.Min(height,.935f),tapered,TorsoRamp(.50f,.93f,front));
        trousers=TrouserLayerRadius(station,angle);
        return Mathf.Lerp(BlendOuterLayer(cloth,trousers),cloth,TorsoRamp(.928f,.990f,height));
    }

    private float FairFemalePanelFall(float[] support,float radius,float trousers,float height,float angle,float front)
    {
        float hem=FinishedGarmentHem+FemaleHemOffset(MathF.Sin(angle));
        float active=TorsoRamp(hem-.006f,hem+.033f,height)*(1-TorsoRamp(.914f,.967f,height));
        if(active==0)return radius;
        // A hanging panel bridges small changes in the cross-section
        // underneath it. Use two supports on each side along the grain, with
        // the real trouser envelope as the lower bound at this station.
        float Supported(float y)
        {
            float cloth=ClothEnvelope(support,MathF.Max(TrunkLow,y),angle)
                +.003f*front*(1-Mathf.SmoothStep(.80f,.94f,y))+GarmentDrapeDetail(y,angle);
            return FemaleOuterLayer(cloth,y,angle,front,out _);
        }
        float fall=(Supported(height-.032f)+4*Supported(height-.016f)+6*radius
            +4*Supported(height+.016f)+Supported(height+.032f))/16;
        float clearance=MathF.Min(radius,trousers-.0015f);
        return MathF.Max(clearance,radius+Math.Clamp(fall-radius,-.005f,.004f)*active);
    }

    private float FemaleLowerPanelEase(float height,float angle)
    {
        float depth=MathF.Sin(angle),side=MathF.Abs(MathF.Cos(angle));
        float belt=FinishedBeltHeight(angle),hem=FinishedGarmentHem+FemaleHemOffset(depth);
        float t=Math.Clamp((belt-height)/(belt-hem),0,1);
        float release=TorsoRamp(.13f,.84f,t);
        float outseam=side*side*side;
        float frontQuarter=TorsoRamp(.12f,.48f,-depth)*(1-TorsoRamp(.71f,.97f,-depth));
        float backQuarter=TorsoRamp(.16f,.56f,depth)*(1-TorsoRamp(.83f,1,depth));
        float towardWaist=TorsoRamp(.887f,.923f,height);
        float fall=_garmentCut switch
        {
            0=>.0012f+.0024f*outseam+.0019f*frontQuarter+.0022f*backQuarter,
            1=>.0014f+.0061f*outseam+.0008f*frontQuarter+.0045f*backQuarter,
            _=>.0028f+.0056f*outseam+.0013f*frontQuarter+.0085f*backQuarter
        };
        // Release the two folds from the back of the belt. Their crests
        // diverge toward the hip and fade before the turned lower edge.
        float rear=TorsoRamp(.56f,.88f,depth);
        float separation=Mathf.Lerp(.14f,.31f,release);
        float shape=(side-separation)/(_garmentCut==2?.16f:.20f);
        float fold=(_garmentCut switch {0=>.0022f,1=>.0068f,_=>.015f})
            *MathF.Exp(-shape*shape)*rear*TorsoRamp(.24f,.68f,t)*(1-TorsoRamp(.86f,1.02f,t));
        float waistAllowance=(1-TorsoRamp(.80f,.935f,height))*(_garmentCut==2?.004f+.010f*side*side:0);
        return Mathf.Lerp(release*fall+fold,waistAllowance,towardWaist);
    }

    private static Color[] FemaleHemFinishCoordinates(Vector3[] points,int[] triangles,int[] hem)
    {
        // Measure the sewn edge on the finished surface, including the
        // turned facing. A vertical distance made the line drift around a
        // curved hem or through a fitted lower panel.
        const float finishWidth=.024f;
        var neighbours=new HashSet<int>[points.Length];
        for(int i=0;i<points.Length;i++)neighbours[i]=new();
        for(int i=0;i<triangles.Length;i+=3)for(int j=0;j<3;j++)
        {
            int a=triangles[i+j],b=triangles[i+(j+1)%3];
            neighbours[a].Add(b);neighbours[b].Add(a);
        }
        var distances=new float[points.Length];Array.Fill(distances,finishWidth);
        var pending=new PriorityQueue<int,float>();
        for(int col=0;col<SewnPanelColumns;col++)
        {
            int rim=hem[col];
            distances[rim]=0;pending.Enqueue(rim,0);
        }
        while(pending.TryDequeue(out int point,out float distance))
        {
            if(distance>distances[point])continue;
            foreach(int next in neighbours[point])
            {
                float candidate=distance+points[point].DistanceTo(points[next]);
                if(candidate>=distances[next])continue;
                distances[next]=candidate;pending.Enqueue(next,candidate);
            }
        }
        var chart=new Color[points.Length];
        for(int i=0;i<points.Length;i++)
            chart[i]=new(1,1,1,distances[i]/finishWidth);
        return chart;
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
            // Short, broad gathers belong where the belt supports the linen.
            // Keep the front-side grain continuous through the lower ribs.
            float waistLine=MathF.Abs(x)-(.51f+.42f*above);
            float broadGather=(.0019f*Bell(waistLine,.19f)-.00065f*Bell(waistLine-.18f,.14f))
                *Bell(above-.032f,.056f)*front*front;
            float span=Mathf.SmoothStep(1.04f,1.09f,height)*(1-Mathf.SmoothStep(1.17f,1.22f,height))*front*front;
            gathering=Mathf.Lerp(gathering*(1-.8f*span),broadGather,TorsoRamp(1.006f,1.052f,height));
            axilla*=1-.8f*span;
        }
        float backPleat=.0028f*Bell(MathF.Abs(x)-.32f,.10f)*back*back
            *Bell(height-(belt+.12f),.17f);
        // Under the belt the narrow gathers open into long, shallow flutes.
        // Their support is the waistband, so they fade out above it.
        float below=1-Mathf.SmoothStep(belt-.015f,belt+.025f,height);
        float hem=.0020f*MathF.Cos(angle*8+.25f)*below;
        if(FittedFigure)
        {
            backPleat*=.65f;
            // Keep the two broad hanging folds distinct from the structural
            // ease of each cut. Stop before they can scallop the turned hem.
            float edge=FinishedGarmentHem+FemaleHemOffset(MathF.Sin(angle));
            float release=TorsoRamp(.025f,.105f,-above)*TorsoRamp(.002f,.030f,height-edge);
            float flank=MathF.Abs(x),grain=Mathf.Lerp(.48f,.66f,release);
            float fall=Bell(flank-grain,.15f)*front*front+Bell(flank-.52f,.20f)*back*back;
            float edgeFold=(_garmentCut==2?.0035f:.0024f)*release*fall;
            float waistRelease=TorsoRamp(.023f,.120f,-above)*TorsoRamp(.760f,.835f,height);
            float waistFold=(_garmentCut==2?.0045f:_garmentCut==1?.0027f:.0020f)*waistRelease
                *(Bell(flank-Mathf.Lerp(.43f,.62f,waistRelease),.135f)*front*front
                    +Bell(flank-(.56f-.11f*waistRelease),.17f)*back*back);
            hem=Mathf.Lerp(edgeFold,waistFold,TorsoRamp(.887f,.923f,height));
        }
        float clearNeck=1-Mathf.SmoothStep(1.18f,1.22f,height);
        float sideSeam=1-Mathf.SmoothStep(.90f,1,MathF.Abs(x));
        return Math.Clamp((gathering+axilla+backPleat+hem)*clearNeck*sideSeam,-.003f,.0045f);
    }
}
