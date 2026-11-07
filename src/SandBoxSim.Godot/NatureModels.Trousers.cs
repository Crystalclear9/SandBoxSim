using System;
using System.Text.Json;
using Godot;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private Mesh ContinuousTrousers()
    {
        string frame=BodyFrame==1?"female":"male",key="continuous-trousers:"+frame;
        if(_meshes.TryGetValue(key,out var cached))return cached;
        string raw=FileAccess.GetFileAsString("res://assets/models/human/trousers-"+frame+".mesh.json").Replace("\r\n","\n",StringComparison.Ordinal);
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        if(hash!=(BodyFrame==1?"bbabb70f70ca50b4b450cb3b9ef7454decdee8f640c4751b0adc453418956e9e":"83ec98514683b7361dbc59dcdabfa61990c76ff13eb12481cec0e65dde81d62c"))
            throw new InvalidOperationException("Changed source trousers");
        using var doc=JsonDocument.Parse(raw);var data=doc.RootElement;
        float[] Floats(string field){var a=new float[data.GetProperty(field).GetArrayLength()];int i=0;foreach(var v in data.GetProperty(field).EnumerateArray())a[i++]=v.GetSingle();return a;}
        int[] Ints(string field){var a=new int[data.GetProperty(field).GetArrayLength()];int i=0;foreach(var v in data.GetProperty(field).EnumerateArray())a[i++]=v.GetInt32();return a;}
        var p=Floats("positions");var n=Floats("normals");var uv=Floats("uv");var b=Ints("bones");var w=Floats("weights");var ids=Ints("indices");int count=p.Length/3;
        if(data.GetProperty("schemaVersion").GetInt32()!=2||data.GetProperty("jointCount").GetInt32()!=5||count==0||n.Length!=p.Length||uv.Length!=count*2||b.Length!=count*4||w.Length!=b.Length||ids.Length==0||ids.Length>30000)
            throw new InvalidOperationException("Invalid continuous trousers");
        var points=new Vector3[count];var normals=new Vector3[count];var coords=new Vector2[count];
        for(int i=0;i<count;i++)
        {
            points[i]=new(p[i*3],p[i*3+1],p[i*3+2]);normals[i]=new(n[i*3],n[i*3+1],n[i*3+2]);coords[i]=new(uv[i*2],uv[i*2+1]);
            float total=0;for(int j=0;j<4;j++){int k=i*4+j;if(b[k]<0||b[k]>4||!float.IsFinite(w[k])||w[k]<0)throw new InvalidOperationException("Invalid trouser weight");total+=w[k];}
            if(!points[i].IsFinite()||MathF.Abs(points[i].X)>.26f||MathF.Abs(points[i].Z)>.20f||points[i].Y<.3849f||points[i].Y>.9401f||!normals[i].IsFinite()||MathF.Abs(normals[i].Length()-1)>.01f||MathF.Abs(total-1)>.0001f)
                throw new InvalidOperationException("Invalid trouser geometry");
        }
        foreach(int id in ids)if(id<0||id>=count)throw new InvalidOperationException("Invalid trouser index");
        if(BodyFrame==1)
        {
            var rest=(Vector3[])points.Clone();var freedom=new float[points.Length];
            for(int i=0;i<points.Length;i++)
                freedom[i]=Mathf.SmoothStep(.58f,.64f,points[i].Y)*(1-Mathf.SmoothStep(.80f,.87f,points[i].Y))
                    *Mathf.SmoothStep(.07f,.11f,MathF.Abs(points[i].X));
            // Paired positive/negative passes retain the source hip volume.
            // Positive-only rounding narrowed both outer sides, erasing the
            // already source-authored pelvic contrast after clothing.
            FairRestSurface(points,ids,freedom,.002f);
            TransportFairingNormals(rest,points,normals,ids,freedom);
            rest=(Vector3[])points.Clone();
            for(int i=0;i<points.Length;i++)
            {
                var point=points[i];float x=MathF.Abs(point.X);
                freedom[i]=Mathf.SmoothStep(.64f,.70f,point.Y)*(1-Mathf.SmoothStep(.79f,.84f,point.Y))
                    *Mathf.SmoothStep(.012f,.032f,x)*(1-Mathf.SmoothStep(.050f,.095f,x));
            }
            // Relieve the crease where the inseam joins the upper leg. Keep
            // the centre stitching, outer hip and lower legs on their binds.
            FairRestSurface(points,ids,freedom,.006f,true);
            TransportFairingNormals(rest,points,normals,ids,freedom);
            FitFemaleTrouserLegs(points,normals,ids);
            rest=(Vector3[])points.Clone();
            var torsoSupport=FemaleThoraxSupport();
            for(int i=0;i<points.Length;i++)
            {
                var point=rest[i];
                points[i]=FitFemaleTrouserWaist(FemaleHipTransition(FemalePelvicPoint(point)),torsoSupport);
                freedom[i]=points[i]==rest[i]?0:1;
            }
            ValidateTrouserWinding(rest,points,ids);
            TransportFairingNormals(rest,points,normals,ids,freedom);
            rest=(Vector3[])points.Clone();
            for(int i=0;i<points.Length;i++)
            {
                var point=rest[i];
                float x=MathF.Abs(point.X);
                freedom[i]=TorsoRamp(.642f,.697f,point.Y)*(1-TorsoRamp(.748f,.795f,point.Y))
                    *TorsoRamp(.014f,.040f,x)*(1-TorsoRamp(.105f,.145f,x))*TorsoRamp(.041f,.067f,-point.Z);
            }
            // Fair the joins between the shaped front rise and thigh panels
            // as one surface, before baking the pose-dependent cloth folds.
            FairRestSurface(points,ids,freedom,.006f,roundRelief:true);
            TransportFairingNormals(rest,points,normals,ids,freedom);
        }
        var arrays=new Godot.Collections.Array();arrays.Resize((int)Mesh.ArrayType.Max);
        var seam=new Vector2[count];
        var sections=BodyFrame==1?MeasureTrouserSections(points,ids):null;
        for(int i=0;i<count;i++)
        {
            var point=points[i];
            // A metric coordinate keeps the inseam continuous across the
            // angular wrap. Both seam lines follow the shaped leg in rest space.
            float seamDistance=sections==null?MathF.Atan2(point.Z,MathF.Abs(point.X)-.10f)
                :point.Z-SampleTrouserSection(sections,point.Y).Centre.Y;
            seam[i]=new(seamDistance,point.Y);
        }
        arrays[(int)Mesh.ArrayType.Vertex]=points;arrays[(int)Mesh.ArrayType.Normal]=normals;arrays[(int)Mesh.ArrayType.TexUV]=coords;arrays[(int)Mesh.ArrayType.TexUV2]=seam;
        arrays[(int)Mesh.ArrayType.Index]=ids;
        var material=(ShaderMaterial)Fabric(BodyFrame==1?"#656356":"#645e53");
        material.SetShaderParameter("trouser_details",true);
        material.SetShaderParameter("tailored_trousers",BodyFrame==1);
        var mesh=new ArrayMesh {BlendShapeMode=Mesh.BlendShapeMode.Normalized};
        var bends=new Godot.Collections.Array<Godot.Collections.Array>();
        if(BodyFrame==1)
        {
            mesh.AddBlendShape("LeftHipFlex");mesh.AddBlendShape("RightHipFlex");
            for(int side=0;side<2;side++)bends.Add(BakeTrouserHipFlex(points,normals,ids,b,w,side));
            BindTrouserKneeSupports(points,b,w);
            mesh.AddBlendShape("LeftKneeFlex");mesh.AddBlendShape("RightKneeFlex");
            for(int side=0;side<2;side++)bends.Add(BakeTrouserKneeFlex(points,normals,ids,b,w,side,false));
            mesh.AddBlendShape("LeftDeepKneeFlex");mesh.AddBlendShape("RightDeepKneeFlex");
            for(int side=0;side<2;side++)bends.Add(BakeTrouserKneeFlex(points,normals,ids,b,w,side,true));
        }
        arrays[(int)Mesh.ArrayType.Bones]=b;arrays[(int)Mesh.ArrayType.Weights]=w;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays,bends);mesh.SurfaceSetMaterial(0,material);
        return _meshes[key]=WithScreenLods(mesh);
    }

    private const float TrouserSectionBottom=.40f,TrouserSectionStep=.01f;
    private const float MaximumTrouserEdgeStretch=3f;
    private readonly record struct TrouserSection(Vector2 Min,Vector2 Max)
    {
        public Vector2 Centre=>(Min+Max)*.5f;
        public Vector2 Radius=>(Max-Min)*.5f;
    }

    private static TrouserSection[] MeasureTrouserSections(Vector3[] points,int[] ids)
    {
        var sections=new TrouserSection[54];
        for(int row=0;row<sections.Length;row++)
        {
            float height=TrouserSectionBottom+row*TrouserSectionStep;
            var min=new Vector2(float.PositiveInfinity,float.PositiveInfinity);
            var max=new Vector2(float.NegativeInfinity,float.NegativeInfinity);
            for(int t=0;t<ids.Length;t+=3)for(int edge=0;edge<3;edge++)
            {
                var a=points[ids[t+edge]];var b=points[ids[t+(edge+1)%3]];
                if((a.Y-height)*(b.Y-height)>0||MathF.Abs(b.Y-a.Y)<1e-8f)continue;
                var p=a.Lerp(b,(height-a.Y)/(b.Y-a.Y));
                var cut=new Vector2(MathF.Abs(p.X),p.Z);
                min=min.Min(cut);max=max.Max(cut);
            }
            if(!min.IsFinite()||!max.IsFinite()||MathF.Min(max.X-min.X,max.Y-min.Y)<.02f)
                throw new InvalidOperationException($"Incomplete trouser cross-section at {height:F3} m");
            sections[row]=new(min,max);
        }
        return sections;
    }

    private static TrouserSection SampleTrouserSection(TrouserSection[] sections,float height)
    {
        float row=Math.Clamp((height-TrouserSectionBottom)/TrouserSectionStep,0,sections.Length-1);
        int i=Math.Min((int)row,sections.Length-2);
        float t=row-i;
        var a=sections[Math.Max(0,i-1)];var b=sections[i];
        var c=sections[i+1];var d=sections[Math.Min(sections.Length-1,i+2)];
        return new(b.Min.CubicInterpolate(c.Min,a.Min,d.Min,t),b.Max.CubicInterpolate(c.Max,a.Max,d.Max,t));
    }

    private static void FitFemaleTrouserLegs(Vector3[] points,Vector3[] normals,int[] ids)
    {
        var rest=(Vector3[])points.Clone();
        var sections=MeasureTrouserSections(rest,ids);
        var freedom=new float[points.Length];
        for(int i=0;i<points.Length;i++)
        {
            var point=rest[i];
            float x=MathF.Abs(point.X),height=point.Y;
            float panel=TorsoRamp(.409f,.465f,height)*(1-TorsoRamp(.660f,.742f,height))*TorsoRamp(.006f,.022f,x);
            if(panel<=0)continue;
            var section=SampleTrouserSection(sections,height);
            var direction=((new Vector2(x,point.Z)-section.Centre)/section.Radius).Normalized();
            float knee=TorsoRamp(.438f,.487f,height)*(1-TorsoRamp(.510f,.577f,height));
            float thigh=TorsoRamp(.548f,.620f,height)*(1-TorsoRamp(.677f,.747f,height));
            // Leave room for the knee in front and the upper thigh toward
            // the outseam. The inseam and sides of the knee carry less ease.
            float outer=.0030f+.0045f*thigh-.0015f*knee;
            float inner=.0005f+.0012f*thigh;
            float front=.0035f+.0040f*knee+.0060f*thigh;
            float back=.0030f+.0045f*thigh;
            float width=Mathf.Lerp(inner,outer,TorsoRamp(-.45f,.55f,direction.X));
            float depth=Mathf.Lerp(front,back,TorsoRamp(-.45f,.55f,direction.Y));
            points[i]+=new Vector3(MathF.Sign(point.X)*direction.X*width,0,direction.Y*depth)*panel;
            freedom[i]=panel;
        }
        ValidateTrouserWinding(rest,points,ids);
        TransportFairingNormals(rest,points,normals,ids,freedom);
    }

    private static void ValidateTrouserWinding(Vector3[] rest,Vector3[] points,int[] ids)
    {
        for(int t=0;t<ids.Length;t+=3)
        {
            int a=ids[t],b=ids[t+1],c=ids[t+2];
            var before=(rest[b]-rest[a]).Cross(rest[c]-rest[a]);
            var after=(points[b]-points[a]).Cross(points[c]-points[a]);
            if(before.LengthSquared()>1e-20f&&before.Dot(after)<=0)
                throw new InvalidOperationException("Trouser tailoring reverses a source triangle");
        }
    }

    private static Vector3 FemaleHipTransition(Vector3 p)
    {
        float x=MathF.Abs(p.X);
        // Turn the outer panel just below the pelvis, then let it hang
        // into the fuller upper leg. Keep the crest and waistband in place.
        float outerHeight=TorsoRamp(.640f,.699f,p.Y)*(1-TorsoRamp(.720f,.775f,p.Y));
        float outer=TorsoRamp(.113f,.155f,x)
            *TorsoRamp(-.086f,-.034f,p.Z)*(1-TorsoRamp(.024f,.066f,p.Z));
        // Add length through the front rise and bridge the slope between
        // the two leg panels. Fade before the free inner-leg edges.
        float riseHeight=TorsoRamp(.689f,.725f,p.Y)*(1-TorsoRamp(.745f,.802f,p.Y));
        float rise=riseHeight*(1-TorsoRamp(.016f,.063f,x))*TorsoRamp(.007f,.062f,-p.Z);
        float frontTurn=TorsoRamp(.691f,.714f,p.Y)*(1-TorsoRamp(.733f,.775f,p.Y))
            *TorsoRamp(.036f,.062f,x)*(1-TorsoRamp(.112f,.148f,x))*TorsoRamp(.045f,.075f,-p.Z);
        // Support the rear cloth just under the seat, spreading the
        // transition over the back of the thigh.
        float rearEase=TorsoRamp(.600f,.656f,p.Y)*(1-TorsoRamp(.696f,.759f,p.Y))
            *TorsoRamp(.038f,.077f,x)*(1-TorsoRamp(.125f,.167f,x))*TorsoRamp(.025f,.067f,p.Z);
        return p+new Vector3(-MathF.Sign(p.X)*.005f*outer*outerHeight,-.009f*rise,
            .006f*rearEase-.009f*rise+.008f*frontTurn);
    }

    private static Vector3 FitFemaleTrouserWaist(Vector3 p,float[] support)
    {
        if(p.Y<=.884f||p.Z>=0)return p;
        var section=new Vector2(p.X,p.Z);
        float radius=section.Length(),angle=MathF.Atan2(p.Z,p.X);
        // Fit the hidden front waistband to the same abdomen that supports
        // the shirt. Retain the inner layer's ease and leave the side and
        // rear waist free for the pelvis; the upper shirt can bridge it.
        float front=TorsoRamp(.45f,.94f,-p.Z/radius);
        float rise=TorsoRamp(.884f,.936f,p.Y);
        float excess=radius-TrunkSample(support,p.Y,angle)-.0028f;
        var fitted=section*(1-Math.Clamp(excess,0,.006f)*front*rise/radius);
        return new(fitted.X,p.Y,fitted.Y);
    }

    private static void BindTrouserKneeSupports(Vector3[] points,int[] bones,float[] weights)
    {
        for(int i=0;i<points.Length;i++)
        {
            var p=points[i];
            int side=p.X<0?0:1,thigh=-1,knee=-1,empty=-1;
            for(int slot=i*4;slot<i*4+4;slot++)
            {
                if(weights[slot]==0)empty=slot;
                else if(bones[slot]==side+1)thigh=slot;
                else if(bones[slot]==side+3)knee=slot;
            }
            if(thigh<0||knee<0||empty<0)continue;
            float total=weights[knee]+weights[thigh];
            float balance=(weights[knee]-weights[thigh])/total;
            // Smooth a triangular weight profile near its apex. Taking equal
            // shares from thigh and shin retains their mean rotation angle.
            const float rounding=.08f;
            float middle=MathF.Sqrt(1+rounding*rounding)-MathF.Sqrt(balance*balance+rounding*rounding);
            float panel=TorsoRamp(.409f,.443f,p.Y)*(1-TorsoRamp(.515f,.555f,p.Y))
                *TorsoRamp(.036f,.061f,MathF.Abs(p.X));
            float outside=1-.82f*TorsoRamp(.012f,.056f,p.Z);
            float share=MathF.Min(MathF.Min(weights[thigh],weights[knee]),.5f*total*middle*panel*outside);
            bones[empty]=side+5;weights[empty]=share*2;
            weights[thigh]-=share;weights[knee]-=share;
        }
    }

    private static (float Knee,float Middle) TrouserKneeWeights(int[] bones,float[] weights,int vertex,int side)
    {
        float knee=0,middle=0;
        for(int j=vertex*4;j<vertex*4+4;j++)
        {
            if(bones[j]==side+3)knee+=weights[j];
            else if(bones[j]==side+5)middle+=weights[j];
        }
        return (knee,middle);
    }

    private static Basis TrouserKneeBlend(Basis bend,Basis half,float knee,float middle)=>new(Vector3.Right,
        Vector3.Up+(bend.Y-Vector3.Up)*knee+(half.Y-Vector3.Up)*middle,
        Vector3.Back+(bend.Z-Vector3.Back)*knee+(half.Z-Vector3.Back)*middle);

    private static Godot.Collections.Array BakeTrouserKneeFlex(Vector3[] rest,Vector3[] restNormals,int[] ids,int[] bones,float[] weights,int side,bool deep)
    {
        var points=(Vector3[])rest.Clone();
        var normals=(Vector3[])restNormals.Clone();
        var freedom=new float[rest.Length];
        float angle=deep?-ResidentRig.DeepTrouserKneeAngle:-ResidentRig.MidTrouserKneeAngle;
        var rotation=new Basis(Vector3.Right,angle);
        var half=new Basis(Vector3.Right,angle*.5f);
        var pivot=new Vector3((side==0?-1:1)*ResidentRig.NeutralHipHalfWidth,ResidentRig.HipHeight-ResidentRig.ThighLength,0);
        for(int i=0;i<rest.Length;i++)
        {
            var point=rest[i];
            if((point.X<0)!=(side==0))continue;
            float panel=Mathf.SmoothStep(.409f,.443f,point.Y)*(1-Mathf.SmoothStep(.500f,.555f,point.Y));
            if(panel<=0)continue;
            var (knee,middle)=TrouserKneeWeights(bones,weights,i,side);
            var lever=point-pivot;
            var linear=TrouserKneeBlend(rotation,half,knee,middle);
            var arc=new Basis(Vector3.Right,angle*(knee+.5f*middle))*lever;
            // Restore the radius lost when the straight and bent bone
            // matrices are blended. Limit this to the outside of the bend.
            float outside=1-Mathf.SmoothStep(-.012f,.038f,point.Z);
            var edit=(linear.Inverse()*arc-lever)*outside;
            float rear=Mathf.SmoothStep(.012f,.041f,point.Z);
            float fold=(point.Y-(.456f+.19f*(MathF.Abs(point.X)-.088f)))/(deep?.015f:.011f);
            edit.Z+=rear*(deep?.006f:.0045f)*MathF.Exp(-fold*fold);
            if(deep)
            {
                // Let the rear cloth turn back into the knee beside the
                // compression fold; keep the boot seam and thigh fixed.
                float tuck=(fold+1.10f)/.72f;
                edit.Z-=rear*.0025f*MathF.Exp(-tuck*tuck);
            }
            points[i]+=edit.LimitLength(deep?.016f:.010f)*panel;
            freedom[i]=panel;
        }
        TransportFairingNormals(rest,points,normals,ids,freedom);
        var shape=new Godot.Collections.Array();
        shape.Resize((int)Mesh.ArrayType.Max);
        shape[(int)Mesh.ArrayType.Vertex]=points;
        shape[(int)Mesh.ArrayType.Normal]=normals;
        return shape;
    }

    private const float TrouserHipFlexAngle=.62f;
    private static Godot.Collections.Array BakeTrouserHipFlex(Vector3[] rest,Vector3[] restNormals,int[] ids,int[] bones,float[] weights,int side)
    {
        var points=(Vector3[])rest.Clone();var normals=(Vector3[])restNormals.Clone();
        var freedom=new float[rest.Length];
        var rotation=new Basis(Vector3.Right,TrouserHipFlexAngle);
        var pivot=new Vector3((side==0?-1:1)*ResidentRig.NeutralHipHalfWidth,ResidentRig.HipHeight,0);
        for(int i=0;i<rest.Length;i++)
        {
            var p=rest[i];float x=MathF.Abs(p.X);
            if((p.X<0)!=(side==0))continue;
            float panel=Mathf.SmoothStep(.54f,.65f,p.Y)*(1-Mathf.SmoothStep(.75f,.83f,p.Y))*Mathf.SmoothStep(.028f,.080f,x);
            float front=TorsoRamp(.020f,.076f,-p.Z),rear=TorsoRamp(.017f,.084f,p.Z);
            float outerRear=TorsoRamp(.122f,.155f,x)*TorsoRamp(.014f,.057f,p.Z);
            float rearSupport=MathF.Max(rear,.55f*outerRear);
            float thighWeight=0;
            for(int j=0;j<4;j++)if(bones[i*4+j]==side+1)thighWeight+=weights[i*4+j];
            var lever=p-pivot;
            var linear=new Basis(Vector3.Right,Vector3.Up.Lerp(rotation.Y,thighWeight),Vector3.Back.Lerp(rotation.Z,thighWeight));
            var arc=new Basis(Vector3.Right,TrouserHipFlexAngle*thighWeight)*lever;
            // Restore the outside arc lost to linear skinning at the hip.
            // The shortened front panel tucks beside a shallow oblique
            // fold. Both ends fade into the rise and outseam.
            var edit=(linear.Inverse()*arc-lever)*rearSupport+Vector3.Back*HipFoldDepth(p,front);
            points[i]+=panel*edit.LimitLength(.010f);
            freedom[i]=panel*MathF.Max(front,rearSupport);
        }
        TransportFairingNormals(rest,points,normals,ids,freedom);
        var shape=new Godot.Collections.Array();shape.Resize((int)Mesh.ArrayType.Max);
        shape[(int)Mesh.ArrayType.Vertex]=points;shape[(int)Mesh.ArrayType.Normal]=normals;
        return shape;
    }

    private static float HipFoldDepth(Vector3 p,float front)
    {
        float x=MathF.Abs(p.X);
        float along=TorsoRamp(.049f,.148f,x);
        float centre=Mathf.Lerp(.761f,.712f,along);
        float offset=(p.Y-centre)/Mathf.Lerp(.019f,.023f,along);
        float tuck=(offset+.91f)/.77f;
        float coverage=front*TorsoRamp(.045f,.077f,x)*(1-TorsoRamp(.123f,.168f,x));
        return coverage*(.0036f*MathF.Exp(-tuck*tuck)-.0058f*MathF.Exp(-offset*offset));
    }
    private void AddContinuousTrousers(ResidentRig rig,FemaleBuild build)
    {
        if(rig.IsChild)return;
        var skeleton=rig.TrouserSkeleton=new Skeleton3D {Name="TrouserSkeleton"};rig.AddChild(skeleton);
        skeleton.AddBone("Waist");skeleton.SetBoneRest(0,Transform3D.Identity);
        var skin=new Skin();skin.AddBind(0,Transform3D.Identity);
        for(int side=0;side<2;side++)
        {
            int bone=side+1;skeleton.AddBone(side==0?"LeftThigh":"RightThigh");skeleton.SetBoneParent(bone,0);
            var rest=rig.Legs[side].Position;skeleton.SetBoneRest(bone,new(Basis.Identity,rest));skin.AddBind(bone,new(Basis.Identity,-rest));
        }
        for(int side=0;side<2;side++)
        {
            int bone=side+3;skeleton.AddBone(side==0?"LeftKnee":"RightKnee");skeleton.SetBoneParent(bone,side+1);
            skeleton.SetBoneRest(bone,rig.Knees[side].Transform);skin.AddBind(bone,(rig.Legs[side].Transform*rig.Knees[side].Transform).AffineInverse());
        }
        if(BodyFrame==1)for(int side=0;side<2;side++)
        {
            int bone=side+5;skeleton.AddBone(side==0?"LeftKneeSupport":"RightKneeSupport");
            skeleton.SetBoneParent(bone,side+1);
            skeleton.SetBoneRest(bone,rig.Knees[side].Transform);
            skin.AddBind(bone,skin.GetBindPose(side+3));
        }
        skeleton.ResetBonePoses();
        var trousers=ContinuousTrousers();
        if(build!=FemaleBuild.Reference)trousers=ShapeFemaleMesh(trousers,build);
        rig.Trousers=new MeshInstance3D {Name="ContinuousTrousers",Mesh=trousers,Skin=skin,Skeleton=new NodePath("../TrouserSkeleton")};rig.AddChild(rig.Trousers);
        rig.TrouserHipFlex=BodyFrame==1;
        // Fit the unloaded arm clearance to the actual clothed thigh, not
        // a sex-specific joint-spacing proxy. Reserve palm width and ease.
        var points=rig.Trousers.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        float wristHeight=rig.ShoulderRest(1).Y-.28f-rig.Hands[1].Position.Length(),half=0;
        foreach(var point in points)if(MathF.Abs(point.Y-wristHeight)<.04f)half=MathF.Max(half,MathF.Abs(point.X));
        if(half<=0)throw new InvalidOperationException("Missing actual thigh section for resting arm clearance");
        float spread=MathF.Asin(Math.Clamp((half+.034f-rig.ShoulderHalfWidth)/(.28f+rig.Hands[1].Position.Length()),0,.3f));
        rig.ArmRestClearance=MathF.Max(0,2*(spread-.040f));
        rig.SyncTrousers();
    }
    private void ValidateContinuousTrousers()
    {
        float maximum=0,maximumHipEdit=0,maximumKneeEdit=0;int samples=0;
        var peakHipWeights=new float[2];var independentHipSamples=new bool[2];
        var peakKneeWeights=new float[2];
        foreach(int identity in new[]{17,55})
        {
            var rig=Resident(identity,false,SandBoxSim.Core.Agents.JobType.None);
            ((SceneTree)Engine.GetMainLoop()).Root.AddChild(rig);
            try
            {
                var mesh=(ArrayMesh)rig.Trousers!.Mesh;var skin=rig.Trousers.Skin;var skeleton=rig.TrouserSkeleton!;
                if(ContinuousTrousers()!=mesh||mesh.GetSurfaceCount()!=1)throw new InvalidOperationException("Trouser cache or draw surface changed");
                var a=mesh.SurfaceGetArrays(0);var p=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();var b=a[(int)Mesh.ArrayType.Bones].AsInt32Array();var w=a[(int)Mesh.ArrayType.Weights].AsFloat32Array();var ids=a[(int)Mesh.ArrayType.Index].AsInt32Array();
                var shapes=mesh.SurfaceGetBlendShapeArrays(0);
                if(shapes.Count!=(BodyFrame==1?6:0)||mesh.BlendShapeMode!=Mesh.BlendShapeMode.Normalized)
                    throw new InvalidOperationException("Trouser LOD generation dropped its flex correctives");
                var targets=new Vector3[shapes.Count][];
                for(int shape=0;shape<shapes.Count;shape++)
                {
                    var target=targets[shape]=shapes[shape][(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    var normals=shapes[shape][(int)Mesh.ArrayType.Normal].AsVector3Array();
                    bool knee=shape>=2;
                    float limit=shape>=4?.01601f:.01001f;
                    int side=shape%2;
                    for(int i=0;i<p.Length;i++)
                    {
                        float displacement=p[i].DistanceTo(target[i]);
                        if(knee)maximumKneeEdit=MathF.Max(maximumKneeEdit,displacement);
                        else maximumHipEdit=MathF.Max(maximumHipEdit,displacement);
                        bool fixedPoint=(p[i].X<0)!=(side==0)||(knee
                            ?p[i].Y<=.409f||p[i].Y>=.555f
                            :MathF.Abs(p[i].X)<=.028f||p[i].Y<=.54f||p[i].Y>=.83f);
                        if(!target[i].IsFinite()||!normals[i].IsFinite()||MathF.Abs(normals[i].Length()-1)>.001f
                            ||displacement>limit||fixedPoint&&displacement>.000001f)
                            throw new InvalidOperationException("Flex corrective moves a fixed trouser panel or has invalid geometry");
                    }
                }
                var shapeWeights=new float[shapes.Count];
                var edges=new System.Collections.Generic.HashSet<(int A,int B)>();
                for(int i=0;i<ids.Length;i+=3)for(int j=0;j<3;j++){int x=ids[i+j],y=ids[i+(j+1)%3];edges.Add(x<y?(x,y):(y,x));}
                var posed=new Vector3[p.Length];
                foreach(var action in new[]{SandBoxSim.Core.Agents.ActionKind.None,SandBoxSim.Core.Agents.ActionKind.BuildHouse,SandBoxSim.Core.Agents.ActionKind.Take})
                for(int f=0;f<120;f++)
                {
                    rig.PoseAction(1f/60,action,action==SandBoxSim.Core.Agents.ActionKind.None?SandBoxSim.Core.Agents.ActionPhase.Moving:SandBoxSim.Core.Agents.ActionPhase.Executing,false,0);
                    if(f%10!=0)continue;
                    skeleton.ForceUpdateAllBoneTransforms();
                    var transforms=new Transform3D[skeleton.GetBoneCount()];
                    for(int bone=0;bone<transforms.Length;bone++)transforms[bone]=skeleton.GetBoneGlobalPose(bone)*skin.GetBindPose(bone);
                    for(int side=0;side<2;side++)if(!transforms[side+1].IsEqualApprox(rig.Legs[side].Transform*skin.GetBindPose(side+1)))
                        throw new InvalidOperationException($"Trouser skin mismatch: {transforms[side+1]} expected {rig.Legs[side].Transform*skin.GetBindPose(side+1)}");
                    for(int side=0;side<2;side++)if(!transforms[side+3].IsEqualApprox(rig.Legs[side].Transform*rig.Knees[side].Transform*skin.GetBindPose(side+3)))
                        throw new InvalidOperationException("Trouser knee skin does not follow the actual boot rim");
                    for(int shape=0;shape<shapeWeights.Length;shape++)
                        shapeWeights[shape]=rig.Trousers.GetBlendShapeValue(shape);
                    for(int side=0;side<2&&shapeWeights.Length!=0;side++)
                    {
                        peakHipWeights[side]=MathF.Max(peakHipWeights[side],shapeWeights[side]);
                        peakKneeWeights[side]=MathF.Max(peakKneeWeights[side],shapeWeights[side+2]+shapeWeights[side+4]);
                        independentHipSamples[side]|=shapeWeights[side]>.15f&&shapeWeights[1-side]<.01f;
                    }
                    for(int i=0;i<p.Length;i++)
                    {
                        var bindPoint=p[i];
                        for(int shape=0;shape<shapeWeights.Length;shape++)bindPoint+=(targets[shape][i]-p[i])*shapeWeights[shape];
                        var v=Vector3.Zero;for(int j=0;j<4;j++)v+=(transforms[b[i*4+j]]*bindPoint)*w[i*4+j];
                        if(!v.IsFinite())throw new InvalidOperationException("Invalid animated trousers");
                        if(p[i].Y>.88f&&v.DistanceTo(transforms[0]*p[i])>.00001f)throw new InvalidOperationException("Leg movement drags the trouser waist");
                        if(p[i].Y<.40f&&v.DistanceTo(transforms[p[i].X<0?3:4]*p[i])>.00001f)throw new InvalidOperationException("Trouser cuff detaches from the moving knee");
                        posed[i]=v;
                    }
                    foreach(var edge in edges)
                    {
                        float length=p[edge.A].DistanceTo(p[edge.B]);if(length<.00001f)continue;
                        float ratio=posed[edge.A].DistanceTo(posed[edge.B])/length;maximum=MathF.Max(maximum,ratio);
                        if(ratio>MaximumTrouserEdgeStretch)throw new InvalidOperationException($"Trouser crotch tears during {action}: stretch={ratio}");
                    }
                    samples++;
                }
                for(int shape=0;shape<shapeWeights.Length;shape++)shapeWeights[shape]=rig.Trousers.GetBlendShapeValue(shape);
                var frozen=skeleton.GetBonePose(1);rig.PoseAction(1,SandBoxSim.Core.Agents.ActionKind.Take,SandBoxSim.Core.Agents.ActionPhase.Executing,true,0);
                if(skeleton.GetBonePose(1)!=frozen)throw new InvalidOperationException("Paused trousers continue moving");
                for(int shape=0;shape<shapeWeights.Length;shape++)
                    if(shapeWeights[shape]!=rig.Trousers.GetBlendShapeValue(shape))throw new InvalidOperationException("Paused trouser correctives continue moving");
                if(shapeWeights.Length>0)
                {
                    ValidateFemalePelvicContour(p,ids);
                    ValidateHipCloth(p,b,w,targets);
                    ValidateKneeCloth(rig,p,b,w,targets);
                    ValidateDeepKneeCloth(rig,p,b,w,targets,edges);
                }
                rig.ValidateDistanceMeshes();if(rig.Trousers.Mesh!=mesh)throw new InvalidOperationException("Distance change replaces the bound lower garment");
            }
            finally{rig.Free();}
        }
        GD.Print($"CONTINUOUS_TROUSERS_PASS: two source frames, {samples} actual gait/work/pickup samples, thigh/knee binds, attached cuffs, stable waist, pause, distance and edge stretch={maximum:F3}");
        if(maximumHipEdit<.003f||!independentHipSamples[0]||!independentHipSamples[1])
            throw new InvalidOperationException($"Hip correctives do not independently respond to the two thighs: {peakHipWeights[0]:F3}/{peakHipWeights[1]:F3}");
        GD.Print($"FEMALE_HIP_FLEX_PASS: left/right peak={peakHipWeights[0]:F3}/{peakHipWeights[1]:F3}, maximum cloth edit={maximumHipEdit*1000:F2} mm; independent legs, fixed inseam center/waist/cuffs, normalized shapes and pause");
        if(maximumKneeEdit<.003f||peakKneeWeights[0]<.1f||peakKneeWeights[1]<.1f)
            throw new InvalidOperationException($"Knee cloth does not respond in actual resident poses: {peakKneeWeights[0]:F3}/{peakKneeWeights[1]:F3}");
        GD.Print($"FEMALE_KNEE_FLEX_PASS: left/right peak={peakKneeWeights[0]:F3}/{peakKneeWeights[1]:F3}, maximum cloth edit={maximumKneeEdit*1000:F2} mm");
    }

    private static void ValidateFemalePelvicContour(Vector3[] points,int[] indices)
    {
        using var source=JsonDocument.Parse(FileAccess.GetFileAsString("res://assets/models/human/trousers-female.mesh.json"));
        var coords=source.RootElement.GetProperty("positions");
        var original=new Vector3[coords.GetArrayLength()/3];
        for(int i=0;i<original.Length;i++)original[i]=new(coords[i*3].GetSingle(),coords[i*3+1].GetSingle(),coords[i*3+2].GetSingle());
        var sourceTopology=source.RootElement.GetProperty("indices");
        var originalIndices=new int[sourceTopology.GetArrayLength()];
        for(int i=0;i<originalIndices.Length;i++)originalIndices[i]=sourceTopology[i].GetInt32();
        float upper=GarmentDepth(original,originalIndices,.032f,.827f,true)-GarmentDepth(points,indices,.032f,.827f,true);
        float lower=GarmentDepth(points,indices,.085f,.725f,true)-GarmentDepth(original,originalIndices,.085f,.725f,true);
        if(!float.IsFinite(upper)||upper<.006f||upper>.013f||!float.IsFinite(lower)||lower<.001f||lower>.009f)
            throw new InvalidOperationException($"Female pelvic contour loses its rear depth transition: upper={upper:F4}, lower={lower:F4}");
        GD.Print($"FEMALE_PELVIC_CONTOUR_PASS: upper rear depth reduced by {upper*1000:F1} mm, rear thigh transition filled by {lower*1000:F1} mm on actual meshes");

        var sourceSections=MeasureTrouserSections(original,originalIndices);
        var fittedSections=MeasureTrouserSections(points,indices);
        var bareKnee=SampleTrouserSection(sourceSections,.50f);
        var clothedKnee=SampleTrouserSection(fittedSections,.50f);
        float kneeWidthEase=2*(clothedKnee.Radius.X-bareKnee.Radius.X);
        float kneeFrontEase=bareKnee.Min.Y-clothedKnee.Min.Y;
        float thighWidthEase=2*(SampleTrouserSection(fittedSections,.65f).Radius.X-SampleTrouserSection(sourceSections,.65f).Radius.X);
        float thighFrontEase=SampleTrouserSection(sourceSections,.65f).Min.Y-SampleTrouserSection(fittedSections,.65f).Min.Y;
        float rearEase=GarmentDepth(points,indices,.098f,.66f,true)-GarmentDepth(original,originalIndices,.098f,.66f,true);
        float rise=0;
        for(int i=0;i<original.Length;i++)
        {
            var p=original[i];
            if(MathF.Abs(p.X)<.025f&&p.Y>.720f&&p.Y<.755f&&p.Z<-.04f)
                rise=MathF.Max(rise,p.Y-points[i].Y);
            if(p.Y<=.409f&&p!=points[i])throw new InvalidOperationException("Leg tailoring moves the fixed boot overlap");
        }
        if(kneeWidthEase<.001f||kneeWidthEase>.004f||kneeFrontEase<.006f||kneeFrontEase>.009f
            ||thighWidthEase<.007f||thighWidthEase>.013f||thighFrontEase<.007f||thighFrontEase>.012f
            ||!float.IsFinite(rearEase)||rearEase<.008f||rearEase>.017f||rise<.008f||rise>.011f)
            throw new InvalidOperationException($"Female trouser ease loses its local fit: knee={kneeWidthEase:F4}/{kneeFrontEase:F4}, thigh={thighWidthEase:F4}/{thighFrontEase:F4}/{rearEase:F4}, rise={rise:F4}");
        GD.Print($"FEMALE_TROUSER_DRAPE_PASS: knee width/front ease={kneeWidthEase*1000:F2}/{kneeFrontEase*1000:F2} mm, thigh width/front/rear ease={thighWidthEase*1000:F2}/{thighFrontEase*1000:F2}/{rearEase*1000:F2} mm, rise release={rise*1000:F2} mm; fixed cuffs");
        float taper=SampleTrouserSection(fittedSections,.78f).Max.X-SampleTrouserSection(fittedSections,.69f).Max.X;
        if(taper<.004f||taper>.009f)
            throw new InvalidOperationException($"Female upper thigh loses its taper below the pelvis: {taper*1000:F2} mm per side");
        GD.Print($"FEMALE_HIP_TRANSITION_PASS: fitted upper thigh tapers {taper*1000:F2} mm per side over 90 mm");
    }

    private static void ValidateHipCloth(Vector3[] points,int[] bones,float[] weights,Vector3[][] targets)
    {
        float before=0,after=0;int samples=0;
        var rotation=new Basis(Vector3.Right,TrouserHipFlexAngle);
        for(int i=0;i<points.Length;i++)
        {
            var point=points[i];
            if(point.Y<.67f||point.Y>.77f||point.Z<.060f||MathF.Abs(point.X)<.060f)continue;
            int side=point.X<0?0:1;
            float thigh=0;
            for(int j=0;j<4;j++)if(bones[i*4+j]==side+1)thigh+=weights[i*4+j];
            if(thigh<.15f||thigh>.85f)continue;
            var axis=new Vector3(point.X,ResidentRig.HipHeight,0);
            var lever=point-axis;
            var corrected=targets[side][i]-axis;
            before+=MathF.Abs(lever.Length()-lever.Lerp(rotation*lever,thigh).Length());
            after+=MathF.Abs(lever.Length()-corrected.Lerp(rotation*corrected,thigh).Length());
            samples++;
        }
        if(samples<20||after>=.80f*before)
            throw new InvalidOperationException($"Hip correction does not preserve the rear cloth radius: {samples}, {before}/{after}");
        GD.Print($"HIP_CLOTH_RADIUS_PASS: {samples} posterior samples, mean radius loss={1000*before/samples:F2} -> {1000*after/samples:F2} mm");

        for(int side=0;side<2;side++)
        {
            float ridge=0,tuck=0;
            for(int i=0;i<points.Length;i++)
            {
                var p=points[i];float x=MathF.Abs(p.X);
                if((p.X<0)!=(side==0)||x<.055f||x>.150f||p.Z>-.065f||p.Y<.65f||p.Y>.81f)continue;
                float depth=targets[side][i].Z-p.Z;
                ridge=MathF.Max(ridge,-depth);tuck=MathF.Max(tuck,depth);
            }
            if(ridge<.004f||ridge>.006f||tuck<.001f||tuck>.003f)
                throw new InvalidOperationException($"Hip flex loses its front-panel fold and tuck: side={side}, ridge={ridge:F4}, tuck={tuck:F4}");
            GD.Print($"FEMALE_HIP_FOLD_PASS: side={side}, baked fold/tuck={ridge*1000:F2}/{tuck*1000:F2} mm");
        }
    }

    private static void ValidateKneeCloth(ResidentRig rig,Vector3[] points,int[] bones,float[] weights,Vector3[][] targets)
    {
        // Isolate each bend after the gameplay samples. Compare the engine
        // blend with the uncorrected radius on the anterior knee.
        float before=0,after=0;
        int samples=0;
        for(int side=0;side<2;side++)
        {
            rig.Knees[side].Rotation=new(-1.12f,0,0);
            rig.Knees[1-side].Rotation=Vector3.Zero;
            rig.SyncTrousers();
            if(rig.Trousers!.GetBlendShapeValue(side+2)<.999f||rig.Trousers.GetBlendShapeValue(3-side)>.00001f)
                throw new InvalidOperationException("Knee flex does not respond independently");
            var transform=new Basis(Vector3.Right,-ResidentRig.MidTrouserKneeAngle);
            var half=new Basis(Vector3.Right,-ResidentRig.MidTrouserKneeAngle*.5f);
            for(int i=0;i<points.Length;i++)
            {
                var p=points[i];
                if((p.X<0)!=(side==0)||p.Z>=-.035f||p.Y<.45f||p.Y>.52f)continue;
                var (knee,middle)=TrouserKneeWeights(bones,weights,i,side);
                float original=knee+.5f*middle;
                if(original<.15f||original>.85f)continue;
                var pivot=new Vector3(p.X,ResidentRig.HipHeight-ResidentRig.ThighLength,0);
                var lever=p-pivot;
                var corrected=targets[side+2][i]-pivot;
                before+=MathF.Abs(lever.Length()-lever.Lerp(transform*lever,original).Length());
                after+=MathF.Abs(lever.Length()-(TrouserKneeBlend(transform,half,knee,middle)*corrected).Length());
                samples++;
            }
        }
        if(samples<20||after>=before*.8f)
            throw new InvalidOperationException($"Knee correction does not preserve the front cloth radius: {samples}, {before}/{after}");
        GD.Print($"KNEE_CLOTH_RADIUS_PASS: {samples} knee-front samples at 64 degrees, mean radius loss={1000*before/samples:F2} -> {1000*after/samples:F2} mm");
    }

    private static void ValidateDeepKneeCloth(ResidentRig rig,Vector3[] points,int[] bones,float[] weights,Vector3[][] targets,
        System.Collections.Generic.HashSet<(int A,int B)> edges)
    {
        float before=0,after=0,maximumStep=0;
        int samples=0;
        float angle=-ResidentRig.DeepTrouserKneeAngle;
        var rotation=new Basis(Vector3.Right,angle);
        var half=new Basis(Vector3.Right,angle*.5f);
        var garment=rig.Trousers!;
        var skeleton=rig.TrouserSkeleton!;
        var skin=garment.Skin;
        if(skeleton.GetBoneCount()!=7||skin.GetBindCount()!=7)throw new InvalidOperationException("Missing the two trouser support bones");
        for(int side=0;side<2;side++)
        {
            rig.Knees[1-side].Rotation=Vector3.Zero;
            rig.Knees[side].Rotation=Vector3.Zero;rig.SyncTrousers();
            var previous=Vector2.Zero;
            for(int frame=1;frame<=100;frame++)
            {
                rig.Knees[side].Rotation=new(angle*frame/100,0,0);rig.SyncTrousers();
                var mixed=new Vector2(garment.GetBlendShapeValue(side+2),garment.GetBlendShapeValue(side+4));
                maximumStep=MathF.Max(maximumStep,mixed.DistanceTo(previous));previous=mixed;
                if(mixed.X+mixed.Y>1.00001f||garment.GetBlendShapeValue(5-side)>1e-6f)
                    throw new InvalidOperationException("Deep bend leaks into the other leg or doubles its correction");
            }
            if(previous.Y<.999f||previous.X>.00001f)
                throw new InvalidOperationException("A deeply bent knee still uses the walking shape");
            for(int i=0;i<points.Length;i++)
            {
                var p=points[i];
                if((p.X<0)!=(side==0))continue;
                var (knee,middle)=TrouserKneeWeights(bones,weights,i,side);
                if(middle!=0&&(p.Y<=.409f||p.Y>=.555f||MathF.Abs(p.X)<=.036f))
                    throw new InvalidOperationException("Knee support reaches into the cuff, inseam or upper thigh");
                float original=knee+.5f*middle;
                if(p.Z>=-.035f||p.Y<.45f||p.Y>.52f||original<.15f||original>.85f)continue;
                var pivot=new Vector3(p.X,ResidentRig.HipHeight-ResidentRig.ThighLength,0);
                float radius=(p-pivot).Length();
                var deep=targets[side+4][i]-pivot;
                before+=MathF.Abs(radius-(p-pivot).Lerp(rotation*(p-pivot),original).Length());
                after+=MathF.Abs(radius-(TrouserKneeBlend(rotation,half,knee,middle)*deep).Length());
                samples++;
            }
        }
        const float delta=1f/60;
        rig.ShowCargo(true,ResourceKind.Wood);
        for(int frame=0;frame<145;frame++)rig.PresentAction(delta,ActionKind.Deposit,ActionPhase.Executing,false,0);
        rig.ShowCargo(false,ResourceKind.Wood);
        float gameplay=0,maximumStretch=0;
        var transforms=new Transform3D[7];var posed=new Vector3[points.Length];
        var mix=new float[targets.Length];
        for(int frame=0;frame<145;frame++)
        {
            rig.PresentAction(delta,ActionKind.Deposit,ActionPhase.Done,false,0);
            gameplay=MathF.Max(gameplay,MathF.Min(garment.GetBlendShapeValue(4),garment.GetBlendShapeValue(5)));
            if(frame%4!=0)continue;
            for(int bone=0;bone<7;bone++)transforms[bone]=skeleton.GetBoneGlobalPose(bone)*skin.GetBindPose(bone);
            for(int shape=0;shape<mix.Length;shape++)mix[shape]=garment.GetBlendShapeValue(shape);
            for(int side=0;side<2;side++)
            {
                var joint=rig.Knees[side];
                var expected=rig.Legs[side].Transform*new Transform3D(new Basis(Vector3.Right,.5f*joint.Rotation.X),joint.Position)*skin.GetBindPose(side+3);
                if(!transforms[side+5].IsEqualApprox(expected))throw new InvalidOperationException("Knee support does not follow the displayed joint pivot");
            }
            for(int i=0;i<points.Length;i++)
            {
                var bind=points[i];
                for(int shape=0;shape<mix.Length;shape++)bind+=(targets[shape][i]-points[i])*mix[shape];
                posed[i]=Vector3.Zero;
                for(int slot=i*4;slot<i*4+4;slot++)posed[i]+=(transforms[bones[slot]]*bind)*weights[slot];
                if(!posed[i].IsFinite())throw new InvalidOperationException("Invalid knee surface during cargo placement");
            }
            foreach(var (a,b) in edges)
            {
                if(points[a].Y<.409f||points[a].Y>.555f||points[b].Y<.409f||points[b].Y>.555f)continue;
                float length=points[a].DistanceTo(points[b]);
                if(length>1e-5f)maximumStretch=MathF.Max(maximumStretch,posed[a].DistanceTo(posed[b])/length);
            }
        }
        // Apply the existing trouser mesh strain limit to the deepest pose,
        // where cloth at the front travels around the supported knee arc.
        if(samples<20||after>=before*.48f||maximumStep>.05f||gameplay<.35f||maximumStretch>MaximumTrouserEdgeStretch)
            throw new InvalidOperationException($"Deep knee fit misses its radius or gameplay pose: loss={before:F4}/{after:F4}, step={maximumStep:F4}, placement={gameplay:F3}, stretch={maximumStretch:F3}");
        GD.Print($"FEMALE_DEEP_KNEE_PASS: {samples} knee-front samples, radius loss={1000*before/samples:F2} -> {1000*after/samples:F2} mm, both knees during actual placement={gameplay:F3}, maximum local edge stretch={maximumStretch:F3}");
    }
}

internal partial class ResidentRig
{
    internal Skeleton3D? TrouserSkeleton;
    internal MeshInstance3D? Trousers;
    internal bool TrouserHipFlex;
    internal const float MidTrouserKneeAngle=1.12f,DeepTrouserKneeAngle=2.24f;
    private readonly float[] _trouserFlexWeights=new float[6];
    internal void SyncTrousers()
    {
        if(TrouserSkeleton==null)return;
        // The waistband follows the same pelvic frame as the lower shirt;
        // thigh poses remain independent in rig space through the inverse.
        var waist=LowerGarmentFrame;
        TrouserSkeleton.SetBonePose(0,waist);
        var inverse=waist.AffineInverse();
        for(int side=0;side<2;side++)
        {
            var local=inverse*Legs[side].Transform;
            TrouserSkeleton.SetBonePose(side+1,local);
            TrouserSkeleton.SetBonePose(side+3,Knees[side].Transform);
            if(!TrouserHipFlex)continue;
            SyncTrouserFlex(side,Mathf.SmoothStep(.14f,.58f,local.Basis.Y.Z));
            float bend=-Knees[side].Rotation.X;
            TrouserSkeleton.SetBonePose(side+5,new(new Basis(Vector3.Right,-.5f*bend),Knees[side].Position));
            float flex=Mathf.SmoothStep(.15f,MidTrouserKneeAngle,bend);
            float deep=Mathf.SmoothStep(MidTrouserKneeAngle,DeepTrouserKneeAngle,bend);
            SyncTrouserFlex(side+2,flex*(1-deep));
            SyncTrouserFlex(side+4,flex*deep);
        }
    }
    private void SyncTrouserFlex(int shape,float weight)
    {
        if(MathF.Abs(weight-_trouserFlexWeights[shape])<=.00001f)return;
        Trousers!.SetBlendShapeValue(shape,weight);
        _trouserFlexWeights[shape]=weight;
    }
}
