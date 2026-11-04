using System;
using System.Text.Json;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private float[]? _trunkMale,_trunkFeminine,_trunkFeminineSkin;
    private readonly float[][] _garmentFrameReference=new float[3][];
    private readonly float[][] _sewnFrameReference=new float[3][];
    private const int TrunkRows=113,TrunkColumns=128;
    private const string MaleTrunkHash="b6006b48444c898845ce45762277e73639ac5527b9f6e29998de5f6ee7ffdcf0";
    private const string FemaleTrunkHash="266821065b0bcb0bdc90bccac4fd2d46a466754b2db5606dec128de5363ada9c";
    private const float TrunkLow=.74f,TrunkHigh=1.30f;
    private const float GarmentReferenceLow=.64f;
    private const int GarmentReferenceRows=133;
    private const float SleeveAxialBlendDistance=.06f;
    private static float[] FrameRadii(string file,string expected,string field="radii")
    {
        string raw=FileAccess.GetFileAsString("res://assets/models/human/"+file).Replace("\r\n","\n",StringComparison.Ordinal);
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        if(hash!=expected)throw new InvalidOperationException("Changed body frame support: "+file);
        using var doc=JsonDocument.Parse(raw);var root=doc.RootElement;
        if(root.GetProperty("schemaVersion").GetInt32()!=1||root.GetProperty("rows").GetInt32()!=TrunkRows||root.GetProperty("columns").GetInt32()!=TrunkColumns)
            throw new InvalidOperationException("Invalid body frame grid");
        if(root.GetProperty("low").GetSingle()!=TrunkLow||root.GetProperty("high").GetSingle()!=TrunkHigh)throw new InvalidOperationException("Invalid trunk sample bounds");
        var radii=new float[TrunkRows*TrunkColumns];int i=0;
        foreach(var value in root.GetProperty(field).EnumerateArray())
        {
            float r=value.GetSingle();
            if(i>=radii.Length||!float.IsFinite(r)||r<.025f||r>.34f)throw new InvalidOperationException("Invalid body frame radius");
            radii[i++]=r;
        }
        if(i!=radii.Length)throw new InvalidOperationException("Incomplete body frame support");
        return radii;
    }
    private static float TrunkSample(float[] data,float y,float angle)
    {
        float row=Math.Clamp((y-TrunkLow)/.005f,0,TrunkRows-1),column=Mathf.PosMod(angle,Mathf.Tau)/Mathf.Tau*TrunkColumns;
        int r=Math.Min(TrunkRows-2,(int)row),c=(int)column%TrunkColumns;float u=column-MathF.Floor(column),v=row-r;
        float At(int h)=>Mathf.Lerp(data[h*TrunkColumns+c],data[h*TrunkColumns+(c+1)%TrunkColumns],u);
        return Mathf.Lerp(At(r),At(r+1),v);
    }
    private static float TrunkSectionWidth(float[] data,float y)
    {
        float low=float.PositiveInfinity,high=float.NegativeInfinity;
        for(int col=0;col<TrunkColumns;col++)
        {
            float angle=col*Mathf.Tau/TrunkColumns;
            float x=MathF.Cos(angle)*TrunkSample(data,y,angle);
            low=MathF.Min(low,x);high=MathF.Max(high,x);
        }
        // Opposing centre rays measure a chord, not the breadth of a torso
        // whose widest lateral points lie forward/backward of the origin.
        return high-low;
    }
    private float[] GarmentFrameReference()
    {
        if(_garmentFrameReference[_garmentCut]!=null)return _garmentFrameReference[_garmentCut];
        var samples=new float[GarmentReferenceRows*TrunkColumns];
        for(int row=0;row<GarmentReferenceRows;row++)for(int col=0;col<TrunkColumns;col++)
        {
            float y=GarmentReferenceLow+row*.005f;
            float fraction=Math.Clamp((ResidentRig.SourceBodyY(y)-GarmentBottom)/(1.28f-GarmentBottom),0,1);
            var p=GarmentPoint(fraction,col*Mathf.Tau/TrunkColumns);
            samples[row*TrunkColumns+col]=new Vector2(p.X,p.Z).Length();
        }
        return _garmentFrameReference[_garmentCut]=samples;
    }
    private static float GarmentReferenceSample(float[] data,float y,float angle)
    {
        float row=Math.Clamp((y-GarmentReferenceLow)/.005f,0,GarmentReferenceRows-1);
        float column=Mathf.PosMod(angle,Mathf.Tau)/Mathf.Tau*TrunkColumns;
        int r=Math.Min(GarmentReferenceRows-2,(int)row),c=(int)column%TrunkColumns;float u=column-MathF.Floor(column),v=row-r;
        float At(int h)=>Mathf.Lerp(data[h*TrunkColumns+c],data[h*TrunkColumns+(c+1)%TrunkColumns],u);
        return Mathf.Lerp(At(r),At(r+1),v);
    }
    private Vector3 FramePoint(Vector3 point,float sleeve=0,bool tailored=false)
    {
        bool feminine=BodyFrame==1;
        // Carry the source thorax into the central clavicular panel without
        // radially shrinking the acromion or the open neck boundary. Their
        // support is the three-dimensional shoulder surface, not a torso ray.
        float clavicle=(1-Mathf.SmoothStep(.070f,.130f,MathF.Abs(point.X)))*(1-Mathf.SmoothStep(1.245f,1.29f,point.Y));
        float neck=Mathf.SmoothStep(1.19f,1.27f,point.Y)*(1-.35f*clavicle);
        float upper=MathF.Max(sleeve,neck);
        float panelHeight=tailored?Mathf.Lerp(point.Y,TailoredPanelHeight(point),1-upper):point.Y;
        // Coverage is defined in finished-clothing space. A long coat's
        // construction hem starts below .70 m before its panel is remapped.
        float active=Mathf.SmoothStep(.70f,.735f,panelHeight)*(1-neck);
        var fitted=point;
        if(active>0&&point.X*point.X+point.Z*point.Z>.00001f)
        {
            _trunkMale??=FrameRadii("trunk-masculine-cage.json",MaleTrunkHash);
            _trunkFeminine??=FrameRadii("trunk-feminine-cage.json",FemaleTrunkHash);
            _trunkFeminineSkin??=FrameRadii("trunk-feminine-cage.json",FemaleTrunkHash,"skinRadii");
            float angle=MathF.Atan2(point.Z,point.X),front=MathF.Max(0,-MathF.Sin(angle));
            // Fit the source skin's complete thorax/lumbar/pelvic envelope.
            // Millimetre-scale ease bridges the skin, rather than shrinking
            // every body part with one sex-dependent actor scale.
            var support=feminine?FemaleThoraxSupport():_trunkMale;
            // Sample the anatomy where the finished panel actually lands.
            // Using its pre-shortening height inflated the upper hip/hem and
            // hid the natural waist-to-pelvis transition under a straight box.
            float supportY=MathF.Max(TrunkLow,panelHeight);
            float target=ClothEnvelope(support,supportY,angle);
            // Fit one clothing envelope. The finished trousers below provide
            // the outer-layer clearance; adding another lumbar pad here
            // counted that same movement allowance twice.
            // The source already contains the thorax, waist and pelvic form.
            // Garment ease is added by ClothEnvelope; applying another chest
            // bulge or carved waist here duplicates those anatomical features.
            target+=.003f*front*(1-Mathf.SmoothStep(.80f,.94f,panelHeight));
            target+=GarmentDrapeDetail(panelHeight,angle);
            // The shirt is outside the actual finished trousers, not only
            // outside the unclothed source. This includes a coat's lower skirt.
            if(panelHeight<.94f)target=MathF.Max(target,TrouserLayerRadius(panelHeight,angle));
            // Clothing, belts and mounts must share the actual sewn reference.
            // Mixing it with the analytic construction cage buries accessories
            // when the torso's retopology changes its radial section.
            var reference=SewnFrameReference();
            float ratio=Math.Clamp(target/GarmentReferenceSample(reference,point.Y,angle),.50f,1.35f);
            ratio=Mathf.Lerp(1,ratio,active);fitted=new(point.X*ratio,point.Y,point.Z*ratio);
        }
        // The arm root, sewn sleeve and inverse bind use the same lateral
        // fit. Authored pattern coordinates keep trunk fitting off the cuffs.
        var cuff=ArmCuffScale();float along=ResidentRig.BodyY(1.18f)-point.Y;
        float taper=Mathf.SmoothStep(.04f,.24f,along);
        float girthX=Mathf.Lerp(feminine?.90f:1,cuff.X,taper),girthZ=Mathf.Lerp(feminine?.90f:1,cuff.Y,taper);
        float armX=(point.X<0?-1:1)*(AdultShoulderWidth+(MathF.Abs(point.X)-.202f)*girthX);
        float lateral=Mathf.Lerp(ShoulderPanelX(point.X),armX,sleeve);
        var sleevePoint=new Vector3(lateral,point.Y,point.Z*Mathf.Lerp(1,girthZ,sleeve));
        var result=fitted.Lerp(sleevePoint,upper);
        // A continuous neck-to-acromion slope, carried through the entire
        // sleeve. The joint rest and inverse bind receive the same drop.
        result.Y-=(feminine?.014f:.009f)*Mathf.SmoothStep(.065f,.18f,MathF.Abs(point.X))*MathF.Max(sleeve,Mathf.SmoothStep(1.08f,1.23f,point.Y));
        // Fit the garment's lower panel independently of the pelvis/trousers.
        // The raised hem still overlaps the trouser waist; sleeves and rigid
        // equipment retain their original length and attachment transforms.
        if(tailored)
        {
            result.Y+=panelHeight-point.Y;
        }
        if(feminine)
        {
            // Carry the opening, its facing and placket through one shared
            // map. The attributed skin neck remains underneath the lowered arc.
            float frontNeck=Mathf.SmoothStep(.020f,.055f,-point.Z)*(1-Mathf.SmoothStep(.035f,.075f,MathF.Abs(point.X)));
            result.Y-=.012f*frontNeck*Mathf.SmoothStep(1.24f,1.32f,point.Y);
            // A lower rounded opening reveals the existing continuous neck.
            // Carry the shirt edge, facing and placket through one map.
            float opening=(1-Mathf.SmoothStep(.065f,.100f,MathF.Abs(point.X)))
                *Mathf.SmoothStep(1.21f,1.28f,point.Y);
            result.Y-=.014f*opening*(1-.35f*Mathf.SmoothStep(0,.06f,point.Z));
            result.Z+=.006f*opening*frontNeck;
            result=SoftShoulderPoint(result);
        }
        return result;
    }
    private Mesh FrameMesh(Mesh source,Transform3D space,bool tailored=false)
    {
        string key="body-frame:"+BodyFrame+":"+source.GetInstanceId()+":"+space+":"+tailored;
        if(_meshes.TryGetValue(key,out var cached))return cached;
        var result=new ArrayMesh();var inverse=space.AffineInverse();
        for(int s=0;s<source.GetSurfaceCount();s++)
        {
            var arrays=source.SurfaceGetArrays(s);var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var pattern=arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
            for(int i=0;i<points.Length;i++)
            {
                var p=space*points[i];const float h=.0001f;
                float along=pattern.Length==points.Length?pattern[i].X:-1;
                // Source pattern metres distinguish sleeves from a torso at
                // the same XYZ neighbourhood. Include d(along)/dY in normals;
                // a spatial X mask compresses the axilla and can fold it.
                Vector3 Fit(Vector3 sample)=>FramePoint(sample,along>=0?Mathf.SmoothStep(0,.06f,along-(sample.Y-p.Y)):0,tailored);
                var dx=(Fit(p+Vector3.Right*h)-Fit(p-Vector3.Right*h))/(2*h);
                var dy=(Fit(p+Vector3.Up*h)-Fit(p-Vector3.Up*h))/(2*h);
                var dz=(Fit(p+Vector3.Back*h)-Fit(p-Vector3.Back*h))/(2*h);
                var jacobian=new Basis(dx,dy,dz);
                if(!float.IsFinite(jacobian.Determinant())||jacobian.Determinant()<=.15f)
                    throw new InvalidOperationException($"Trunk fit folds the garment or limb surface: frame={BodyFrame}, cut={_garmentCut}, p={p}, determinant={jacobian.Determinant()}");
                var normal=space.Basis.Inverse().Transposed()*normals[i];
                normals[i]=(space.Basis.Transposed()*(jacobian.Inverse().Transposed()*normal)).Normalized();
                points[i]=inverse*Fit(p);
                if(!points[i].IsFinite()||!normals[i].IsFinite())throw new InvalidOperationException("Invalid frame geometry");
            }
            var bones=arrays[(int)Mesh.ArrayType.Bones].VariantType==Variant.Type.Nil?Array.Empty<int>():arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
            var weights=arrays[(int)Mesh.ArrayType.Weights].VariantType==Variant.Type.Nil?Array.Empty<float>():arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            var sleeveDistance=bones.Length==points.Length*4&&pattern.Length==points.Length
                ?SleeveDistances(points,arrays[(int)Mesh.ArrayType.Index].AsInt32Array(),pattern):Array.Empty<float>();
            if(tailored&&sleeveDistance.Length==points.Length)
            {
                FitUpperArmSupport(points,pattern,sleeveDistance);
                FitShoulderSurface(points,arrays[(int)Mesh.ArrayType.Index].AsInt32Array(),pattern,sleeveDistance);
                FairGarmentRest(points,arrays[(int)Mesh.ArrayType.Index].AsInt32Array(),pattern,source.SurfaceGetMaterial(s)?.HasMeta("FittedBodice")==true);
                RelaxArmholeSurface(points,arrays[(int)Mesh.ArrayType.Index].AsInt32Array(),pattern);
                sleeveDistance=SleeveDistances(points,arrays[(int)Mesh.ArrayType.Index].AsInt32Array(),pattern);
            }
            var girdle=tailored&&sleeveDistance.Length==points.Length
                ?GirdleWeights(points,arrays[(int)Mesh.ArrayType.Index].AsInt32Array(),pattern):Array.Empty<float>();
            var sleeveInfluence=girdle.Length==points.Length
                ?SleeveInfluences(points,arrays[(int)Mesh.ArrayType.Index].AsInt32Array(),pattern,sleeveDistance):Array.Empty<float>();
            if(bones.Length==points.Length*4)
                for(int i=0;i<points.Length;i++)
                {
                    if(girdle.Length==points.Length&&pattern[i].X<=0)
                    {
                        // Chest panels follow the shoulder platform, never
                        // the humerus. Carrying old arm weights into this
                        // region adds humeral swing to scapular rotation and
                        // pulls the lower axilla into a wing.
                        float g=girdle[i];bones[i*4]=0;bones[i*4+1]=ResidentRig.GirdleBone(points[i].X<0?0:1);
                        bones[i*4+2]=bones[i*4+3]=0;weights[i*4]=1-g;weights[i*4+1]=g;weights[i*4+2]=weights[i*4+3]=0;
                        continue;
                    }
                    float arm=0;for(int j=0;j<4;j++)if(bones[i*4+j] is 1 or 2)arm+=weights[i*4+j];
                    if(sleeveInfluence.Length==points.Length)arm=sleeveInfluence[i];
                    if(arm<=0)
                    {
                        if(girdle.Length==points.Length)
                        {
                            float g=girdle[i];bones[i*4]=0;bones[i*4+1]=ResidentRig.GirdleBone(points[i].X<0?0:1);
                            bones[i*4+2]=bones[i*4+3]=0;weights[i*4]=1-g;weights[i*4+1]=g;weights[i*4+2]=weights[i*4+3]=0;
                        }
                        continue;
                    }
                    arm=Math.Clamp(arm,0,1);
                    int side=points[i].X<0?0:1;
                    if(arm>=.999999f)
                    {
                        // One exact cuff influence, not two duplicate bones
                        // whose 16-bit decoded weights can sum below one.
                        bones[i*4]=side+1;bones[i*4+1]=bones[i*4+2]=bones[i*4+3]=0;
                        weights[i*4]=1;weights[i*4+1]=weights[i*4+2]=weights[i*4+3]=0;continue;
                    }
                    float scaled=ResidentRig.SleeveSpans*arm;
                    int segment=Math.Min(ResidentRig.SleeveSpans-1,(int)scaled);
                    float t=scaled-segment;
                    int lower=segment==0?ResidentRig.GirdleBone(side):ResidentRig.SleeveSupportBone(side,segment);
                    int upper=segment==ResidentRig.SleeveSpans-1?side+1:ResidentRig.SleeveSupportBone(side,segment+1);
                    bones[i*4]=0;bones[i*4+1]=lower;bones[i*4+2]=upper;bones[i*4+3]=0;
                    weights[i*4]=0;weights[i*4+1]=1-t;
                    weights[i*4+2]=t;weights[i*4+3]=0;
                    if(pattern.Length==points.Length&&pattern[i].X>0)
                    {
                        // Centre translation is an axial station, not a cloth
                        // geodesic. Around the armhole the surface can travel
                        // several centimetres while remaining at the pivot's
                        // height. Using that distance lifts a false cap horn.
                        // With d=(pivotY-y), Rf*(radial-down*d) plus
                        // (R-Rf)*down*d keeps the centre on the humeral axis.
                        float shoulderY=ResidentRig.BodyY(1.18f)-(BodyFrame==1?.014f:.009f);
                        // The sewn boundary remains on its scapular support;
                        // Spread centreline transport over the shoulder cap.
                        // A four-centimetre ramp produced a steep translation
                        // gradient across short edges of shaped armholes.
                        float gather=Math.Clamp((shoulderY-points[i].Y)/.263f,0,1)*Mathf.SmoothStep(0,SleeveAxialBlendDistance,sleeveDistance[i]);
                        int lowerCap=segment==0?ResidentRig.GirdleBone(side):ResidentRig.SleeveSupportBone(side,segment,true);
                        int upperCap=segment==ResidentRig.SleeveSpans-1?side+1:ResidentRig.SleeveSupportBone(side,segment+1,true);
                        bones[i*4]=lower;bones[i*4+1]=upper;bones[i*4+2]=lowerCap;bones[i*4+3]=upperCap;
                        weights[i*4]=(1-t)*(1-gather);weights[i*4+1]=t*(1-gather);
                        weights[i*4+2]=(1-t)*gather;weights[i*4+3]=t*gather;
                    }
                    else if(segment==0)
                    {
                        // Only the torso's residual fraction blends from the
                        // fixed chest to its shoulder platform. Sleeve roots
                        // use the same platform as the shared armhole.
                        float g=girdle.Length==points.Length?girdle[i]:0;
                        weights[i*4]=(1-t)*(1-g);weights[i*4+1]=(1-t)*g;
                    }
                }
            if(tailored&&bones.Length==points.Length*4)
                for(int i=0;i<points.Length;i++)
                {
                    if(pattern.Length==points.Length&&pattern[i].X>=0)continue;
                    float lower=ResidentRig.PelvicClothWeight(points[i].Y);
                    // The fourth slot carries the lower trunk; the first
                    // three retain the torso/arm/intermediate sleeve blend.
                    float amount=weights[i*4]*lower;weights[i*4]-=amount;bones[i*4+3]=3;weights[i*4+3]=amount;
                }
            if(bones.Length>0){arrays[(int)Mesh.ArrayType.Bones]=bones;arrays[(int)Mesh.ArrayType.Weights]=weights;}
            // Pattern coordinates are a surface parameter, not a spatial Y
            // function. Rebuild the sewn panel normals from final triangles;
            // otherwise its sleeve/torso seam receives mismatched gradients.
            if(tailored)normals=ClothSurfaceNormals(points,arrays[(int)Mesh.ArrayType.Index].AsInt32Array());
            // Preserve metric rest coordinates for the torso's side seams and
            // back yoke. Negative X remains the torso sentinel everywhere in
            // the rig; sleeve geodesic coordinates retain their exact contract.
            if(tailored&&pattern.Length==points.Length)
            {
                for(int i=0;i<points.Length;i++)if(pattern[i].X<0)
                    pattern[i]=new(-2-MathF.Atan2(points[i].Z,points[i].X)/Mathf.Tau,points[i].Y);
                arrays[(int)Mesh.ArrayType.TexUV2]=pattern;
                // The geometric pattern's negative torso sentinel is not a
                // continuous texture coordinate across an armhole triangle.
                // Keep rig coordinates in UV2; a separate rest chart in UV
                // supplies continuous torso colour coordinates on both panels.
                var colourChart=new Vector2[points.Length];
                for(int i=0;i<points.Length;i++)colourChart[i]=new(-2-MathF.Atan2(points[i].Z,points[i].X)/Mathf.Tau,points[i].Y);
                if(source.SurfaceGetMaterial(s)!=_fittedDistantMaterial&&source.SurfaceGetMaterial(s)!=_distantColorMaterial)
                    arrays[(int)Mesh.ArrayType.TexUV]=colourChart;
            }
            arrays[(int)Mesh.ArrayType.Vertex]=points;arrays[(int)Mesh.ArrayType.Normal]=normals;arrays[(int)Mesh.ArrayType.Tangent]=default(Variant);
            result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);result.SurfaceSetMaterial(s,source.SurfaceGetMaterial(s));
        }
        var fitted=WithScreenLods(result);fitted.SetMeta("BodyFrameFit",true);
        return _meshes[key]=fitted;
    }
    private void ApplyBodyFrame(ResidentRig rig)
    {
        if(ChildHead)return;
        void Walk(Node3D parent,Transform3D space)
        {
            foreach(Node child in parent.GetChildren())
            {
                if(child is not Node3D node||node==rig.Head||node==rig.Arms[0]||node==rig.Arms[1])continue;
                if(node==rig.Holster||node==rig.PackMount||node==rig.SatchelMount)
                {
                    var origin=space*node.Position;var fitted=FramePoint(origin,0,true);
                    if(node==rig.PackMount)fitted.Z+=FramePoint(new Vector3(0,1.15f,origin.Z)).Z-origin.Z;
                    node.Position=space.AffineInverse()*fitted;
                    continue; // Mounted tools and packs retain their rigid shape.
                }
                var transform=space*node.Transform;
                if(node is MeshInstance3D mesh&&mesh.Mesh!=null)
                {
                    mesh.Mesh=FrameMesh(mesh.Mesh,transform,mesh==rig.BodySkin);
                    if(mesh!=rig.BodySkin&&mesh.HasMeta("DistantMesh"))
                        mesh.SetMeta("DistantMesh",FrameMesh((Mesh)mesh.GetMeta("DistantMesh").AsGodotObject(),transform));
                }
                else Walk(node,transform);
            }
        }
        Walk(rig,Transform3D.Identity);
        rig.DetailedBodyMesh=rig.BodySkin.Mesh;
        // Derive the static proxy from the fitted exterior. The old distant
        // template retained a separate torso and primitive shoulder caps.
        rig.DistantBodyMesh=DistantColors(rig.DetailedBodyMesh);
        rig.DistantBodyMesh.SetMeta("BodyFrameFit",true);
        rig.HipHalfWidth=ResidentRig.NeutralHipHalfWidth;
        rig.ArmRestClearance=0;
        for(int side=0;side<2;side++)rig.Legs[side].Position=new(side==0?-rig.HipHalfWidth:rig.HipHalfWidth,ResidentRig.HipHeight,0);
        // The fitted sleeve vertices, joint rest and inverse bind must move
        // together. Scaling the mesh alone leaves a male-width moving rig.
        rig.ShoulderHalfWidth=.202f*ArmWidthScale;
        rig.ClavicleRest=AdultClavicleRest;
        rig.ShoulderDrop=BodyFrame==1?.014f:.009f;
        rig.ShoulderSkeleton.AddBone("PelvicCloth");rig.ShoulderSkeleton.SetBoneParent(3,0);rig.ShoulderSkeleton.SetBoneRest(3,Transform3D.Identity);
        var skin=new Skin();skin.AddBind(0,Transform3D.Identity);
        for(int side=0;side<2;side++)
        {
            var rest=rig.ShoulderRest(side);rig.Arms[side].Position=rest;
            rig.ShoulderSkeleton.SetBoneRest(side+1,new Transform3D(Basis.Identity,rest));
            skin.AddBind(side+1,new Transform3D(Basis.Identity,-rest));
        }
        skin.AddBind(3,Transform3D.Identity);
        if(!rig.IsChild)
        {
            rig.HasShoulderSupports=true;
            for(int family=0;family<2;family++)for(int span=1;span<ResidentRig.SleeveSpans;span++)for(int side=0;side<2;side++)
            {
                int bone=ResidentRig.SleeveSupportBone(side,span,family==1);var rest=rig.ShoulderRest(side);
                rig.ShoulderSkeleton.AddBone((side==0?"Left":"Right")+(family==0?"SleeveSupport":"SleeveCap")+span);
                rig.ShoulderSkeleton.SetBoneParent(bone,0);rig.ShoulderSkeleton.SetBoneRest(bone,new Transform3D(Basis.Identity,rest));
                skin.AddBind(bone,new Transform3D(Basis.Identity,-rest));
            }
            for(int side=0;side<2;side++)
            {
                int bone=ResidentRig.GirdleBone(side);var rest=rig.ShoulderRest(side);
                rig.ShoulderSkeleton.AddBone((side==0?"Left":"Right")+"ShoulderGirdle");
                rig.ShoulderSkeleton.SetBoneParent(bone,0);rig.ShoulderSkeleton.SetBoneRest(bone,new Transform3D(Basis.Identity,rest));
                skin.AddBind(bone,new Transform3D(Basis.Identity,-rest));
            }
        }
        rig.ShoulderSkeleton.ResetBonePoses();rig.DetailedBodySkin=skin;rig.BodySkin.Skin=skin;
        FitArmClothing(rig);
        rig.CaptureBodyMounts();
    }
    public void ValidateBodyFrames()
    {
        ValidateSoftShoulderField();
        ValidateMedialThorax();
        ValidateBalancedTorso();
        ValidateChestVolume();
        int previous=_faceVariant;
        var male=Resident(17,false,SandBoxSim.Core.Agents.JobType.Gatherer);
        var female=Resident(42,false,SandBoxSim.Core.Agents.JobType.Gatherer);
        try
        {
            ValidateAdultContourSections(male,female);
            foreach(var rig in new[]{male,female})
            {
                var far=new System.Collections.Generic.HashSet<Vector3>(rig.DistantBodyMesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array());
                foreach(var point in rig.DetailedBodyMesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                    if(!far.Contains(point))throw new InvalidOperationException("Distant adult clothing loses the repaired near surface");
            }
            GD.Print("BODY_LOD_SURFACE_PASS: both adult frames, static distant cloth shares the repaired near rest positions");
            var headMesh=female.Head.GetChild<MeshInstance3D>(0);
            var headSpace=InTorso(headMesh,female.Torso);
            float neckBase=float.PositiveInfinity;
            foreach(var point in headMesh.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
            {
                var p=headSpace*point;
                if(MathF.Abs(p.X)<.03f)neckBase=MathF.Min(neckBase,p.Y);
            }
            float openingY=FramePoint(ResidentRig.BodyPoint(GarmentPoint(1,-MathF.PI/2))).Y;
            if(!float.IsFinite(neckBase)||neckBase>openingY-.001f)
                throw new InvalidOperationException($"Lowered female neckline exposes the source neck boundary: skin={neckBase}, opening={openingY}");
            GD.Print($"NECK_COVERAGE_PASS: actual central neck extends below rounded opening by {(openingY-neckBase)*1000:F2} mm");
            ValidateForearmSupport(male,0);ValidateForearmSupport(female,1);
            foreach(var rig in new[]{male,female})
            {
                ValidateArmClothingFit(rig);
                ValidateRenderedBelt(rig);
                var a=rig.DetailedBodyMesh.SurfaceGetArrays(0);
                var vertices=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var detail=a[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
                for(int side=0;side<2;side++)
                {
                    float y=0;int count=0;
                    for(int v=0;v<vertices.Length;v++)if(detail[v].X>.2628f&&(vertices[v].X<0)==(side==0))
                    {y+=vertices[v].Y;count++;}
                    float elbow=rig.Arms[side].Position.Y-.28f;
                    if(count==0||MathF.Abs(y/count-elbow)>.028f)
                        throw new InvalidOperationException("Shortened torso panel detaches the sleeve from the actual elbow");
                }
            }
            float Hem(Mesh mesh)
            {
                float y=float.PositiveInfinity;
                foreach(var p in mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array())y=MathF.Min(y,p.Y);
                return y;
            }
            float hemRise=Hem(female.DetailedBodyMesh)-Hem(male.DetailedBodyMesh);
            if(MathF.Abs(hemRise-.023f)>.003f||MathF.Abs(Hem(female.DetailedBodyMesh)-.848f)>.002f
                ||MathF.Abs(Hem(male.DetailedBodyMesh)-.825f)>.002f)
                throw new InvalidOperationException("Tailored hem loses trouser coverage or retains the common long cut");
            GD.Print($"BODY_CUT_PASS: raised garment hem={hemRise:F4} m, preserved trouser overlap and separate lower-body geometry");
            if(MathF.Abs(female.HipHalfWidth-ResidentRig.NeutralHipHalfWidth)>.00001f||MathF.Abs(male.HipHalfWidth-ResidentRig.NeutralHipHalfWidth)>.00001f)
                throw new InvalidOperationException("Neutral stance does not match the fitted clothing");
            foreach(var rig in new[]{male,female})
            {
                float gap=float.PositiveInfinity;
                foreach(var point in rig.DetailedBodyMesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                    gap=MathF.Min(gap,point.DistanceTo(rig.Holster.Position));
                if(gap>.055f)throw new InvalidOperationException($"Belt-mounted tool loop separates from the actual shirt: {gap}");
            }
            if(male.DetailedBodyMesh==female.DetailedBodyMesh||male.DistantBodyMesh==female.DistantBodyMesh)
                throw new InvalidOperationException("Adult frames share their fitted body cache");
            float SourceShoulder(string file)
            {
                using var doc=JsonDocument.Parse(FileAccess.GetFileAsString("res://assets/models/human/"+file));
                return doc.RootElement.GetProperty("sourceShoulder")[0].GetSingle()*doc.RootElement.GetProperty("scale")[0].GetSingle();
            }
            // Check the selected source guides, not a fixed sex-gap that
            // pressures art direction into ever narrower female shoulders.
            if(MathF.Abs(male.ShoulderHalfWidth-SourceShoulder("trunk-masculine-cage.json"))>.000001f
                ||MathF.Abs(female.ShoulderHalfWidth-SourceShoulder("trunk-feminine-cage.json"))>.000001f
                ||female.ShoulderHalfWidth>=male.ShoulderHalfWidth||female.DetailedBodySkin==male.DetailedBodySkin)
                throw new InvalidOperationException("Adult shoulder fit retains the common width or inverse binds");
            for(int side=0;side<2;side++)
                if(!(female.ShoulderSkeleton.GetBoneRest(side+1)*female.DetailedBodySkin.GetBindPose(side+1)).IsEqualApprox(Transform3D.Identity))
                    throw new InvalidOperationException("Fitted shoulder rest and inverse bind disagree");
            if(male.SatchelMount==null||female.SatchelMount==null||male.SatchelMount.Scale!=Vector3.One||female.SatchelMount.Scale!=Vector3.One||
                male.SatchelMount.GetChild<MeshInstance3D>(0).Mesh.GetAabb()!=female.SatchelMount.GetChild<MeshInstance3D>(0).Mesh.GetAabb())
                throw new InvalidOperationException("Body fitting stretches the rigid satchel instead of adapting its mount");
            foreach(var rig in new[]{male,female})
                if(MathF.Abs(rig.Scale.X-rig.Scale.Y)>.00001f||MathF.Abs(rig.Scale.X-rig.Scale.Z)>.00001f||rig.Head.Scale.X<.98f||rig.Head.Scale.X>1.04f)
                    throw new InvalidOperationException("Stature scaling distorts the adult body or head proportions");
            female.ValidateDistanceMeshes();
            void CheckFrameCaches(Node3D parent)
            {
                foreach(Node child in parent.GetChildren())
                {
                    if(child is not Node3D node||node==female.Head||node==female.Arms[0]||node==female.Arms[1]||node==female.Holster||node==female.PackMount||node==female.SatchelMount)continue;
                    if(node is MeshInstance3D mesh&&mesh!=female.BodySkin&&mesh.HasMeta("DistantMesh"))
                    {
                        var far=(Mesh)mesh.GetMeta("DistantMesh").AsGodotObject();
                        if(!mesh.Mesh.HasMeta("BodyFrameFit")||!far.HasMeta("BodyFrameFit"))
                            throw new InvalidOperationException("Adult accessory reverts to an unfitted distance mesh");
                    }
                    CheckFrameCaches(node);
                }
            }
            CheckFrameCaches(female);
            // A single construction point can lie on a newly authored body.
            // Check the map across thorax/waist sections; actual paired-frame
            // silhouette differences are checked on the final meshes above.
            float maximumFrameFit=0;
            foreach(float height in new[]{1.00f,1.08f,1.135f,1.16f})
            foreach(float angle in new[]{-.5f,-1.0f,-1.5f})
            {
                var sample=new Vector3(.15f*MathF.Cos(angle),height,.15f*MathF.Sin(angle));
                maximumFrameFit=MathF.Max(maximumFrameFit,FramePoint(sample).DistanceTo(sample));
            }
            if(maximumFrameFit<.002f)throw new InvalidOperationException("Source body map retains the unmodified construction cage");
            foreach(float y in new[]{.035f,.075f,.10f,.145f,.20f,.255f,.265f,.275f})
            foreach(int frame in new[]{0,1})
            {
                var point=new Vector3(.032f,y,.035f);var normal=new Vector3(.3f,.4f,.8660254f).Normalized();
                var fitted=RefineForearmProfile(point,normal,frame);const float h=.00001f;
                var dx=(RefineForearmProfile(point+Vector3.Right*h,normal,frame).Point-RefineForearmProfile(point-Vector3.Right*h,normal,frame).Point)/(2*h);
                var dy=(RefineForearmProfile(point+Vector3.Up*h,normal,frame).Point-RefineForearmProfile(point-Vector3.Up*h,normal,frame).Point)/(2*h);
                var dz=(RefineForearmProfile(point+Vector3.Back*h,normal,frame).Point-RefineForearmProfile(point-Vector3.Back*h,normal,frame).Point)/(2*h);
                var reference=(new Basis(dx,dy,dz).Inverse().Transposed()*normal).Normalized();
                if(reference.DistanceTo(fitted.Normal)>.001f)throw new InvalidOperationException("Forearm volume normals miss their composite deformation");
                if(y<=.005f&&fitted.Point!=point)
                    throw new InvalidOperationException("Muscle fit changes the grip or wrist attachment");
            }
            var rim=new System.Collections.Generic.List<Vector3>();
            float hem=Hem(female.DetailedBodyMesh);
            foreach(var point in female.DetailedBodyMesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                if(point.Y<=hem+.025f)rim.Add(point);
            foreach(var rig in new[]{male,female})
            foreach(var action in new[]{SandBoxSim.Core.Agents.ActionKind.Eat,SandBoxSim.Core.Agents.ActionKind.BuildHouse,SandBoxSim.Core.Agents.ActionKind.Take})
            for(int f=0;f<150;f++)
            {
                rig.PoseAction(1f/60,action,SandBoxSim.Core.Agents.ActionPhase.Executing,false,0);
                if(rig==female)foreach(var point in rim)
                    if((rig.Torso.Transform*point.Lerp(rig.PelvicClothPose*point,ResidentRig.PelvicClothWeight(point.Y))).Y>.895f)
                        throw new InvalidOperationException("Raised shirt hem exposes a gap above the trouser waist during an action");
                for(int side=0;side<2;side++)
                {
                    var elbow=InTorso(rig.Elbows[side],rig.Torso).Origin;
                    var wrist=InTorso(rig.Hands[side],rig.Torso).Origin;
                    if(MathF.Abs(elbow.DistanceTo(rig.Arms[side].Position)-.28f)>.0001f||MathF.Abs(elbow.DistanceTo(wrist)-rig.Hands[side].Position.Length())>.0001f)
                        throw new InvalidOperationException("Adult frame detaches an animated arm segment");
                }
            }
        }
        finally{male.Free();female.Free();_faceVariant=previous;_facePoints=null;_faceIndices=null;}
        GD.Print("BODY_CUT_POSE_PASS: raised hem retains trouser overlap through eating, work and pickup transitions");
        GD.Print("BODY_FRAMES_PASS: source chest/waist/hip presentation, separate near/far caches, bounded forearm fit/Jacobian, exact wrist and animated limb lengths");
    }
    private static float ClothSectionWidth(Mesh mesh,float height,Color? cloth=null,bool allowEmpty=false)
    {
        var arrays=mesh.SurfaceGetArrays(0);var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        var detail=arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
        var colours=arrays[(int)Mesh.ArrayType.Color].AsColorArray();
        float low=float.PositiveInfinity,high=float.NegativeInfinity;
        for(int i=0;i<indices.Length;i+=3)
        {
            bool body=true;
            for(int j=0;j<3;j++)
            {
                int v=indices[i+j];
                // Exclude sleeves using their authored pattern coordinates;
                // distant batching preserves dye in vertex colour instead.
                if(detail.Length==points.Length&&detail[v].X>0)body=false;
                if(cloth.HasValue&&(colours.Length!=points.Length||!colours[v].IsEqualApprox(cloth.Value)))body=false;
            }
            if(!body)continue;
            for(int edge=0;edge<3;edge++)
            {
                var a=points[indices[i+edge]];var b=points[indices[i+(edge+1)%3]];
                if((a.Y-height)*(b.Y-height)>0||MathF.Abs(b.Y-a.Y)<1e-8f)continue;
                float x=Mathf.Lerp(a.X,b.X,(height-a.Y)/(b.Y-a.Y));
                low=MathF.Min(low,x);high=MathF.Max(high,x);
            }
        }
        if(!float.IsFinite(low)||!float.IsFinite(high))
        {if(allowEmpty)return 0;throw new InvalidOperationException("Adult garment has no measured cross-section");}
        return high-low;
    }
    private void ValidateAdultContourSections(ResidentRig male,ResidentRig female)
    {
        // Measure the final render meshes in world units, after all fitting and
        // outer scale. A wider nominal pelvis is insufficient if actor scaling
        // erases it, or the LOD silently restores the common source shape.
        foreach(bool distant in new[]{false,true})
        {
            Mesh m=distant?male.DistantBodyMesh:male.DetailedBodyMesh,f=distant?female.DistantBodyMesh:female.DetailedBodyMesh;
            Color? maleDye=distant?((ShaderMaterial)male.DetailedBodyMesh.SurfaceGetMaterial(0)).GetShaderParameter("dye").AsColor():null;
            Color? femaleDye=distant?((ShaderMaterial)female.DetailedBodyMesh.SurfaceGetMaterial(0)).GetShaderParameter("dye").AsColor():null;
            float maleHip=MathF.Max(ClothSectionWidth(m,.78f,maleDye,true),ClothSectionWidth(male.Trousers!.Mesh,.78f))*male.Scale.X;
            float femaleHip=MathF.Max(ClothSectionWidth(f,.78f,femaleDye,true),ClothSectionWidth(female.Trousers!.Mesh,.78f))*female.Scale.X;
            float maleWaist=ClothSectionWidth(m,1.00f,maleDye)*male.Scale.X,femaleWaist=ClothSectionWidth(f,1.00f,femaleDye)*female.Scale.X;
            float maleShoulder=male.ShoulderHalfWidth*2*male.Scale.X,femaleShoulder=female.ShoulderHalfWidth*2*female.Scale.X;
            // Compare clothing with the same source body instead of forcing
            // a fixed sex-specific hourglass ratio onto every character.
            var sourceMale=FrameRadii("trunk-masculine-cage.json",MaleTrunkHash);
            var sourceFemale=FrameRadii("trunk-feminine-cage.json",FemaleTrunkHash);
            float Width(float[] source,float y)=>TrunkSectionWidth(source,y);
            float maximumWidthAllowance=0;
            void Check(ResidentRig rig,Mesh body,Color? dye,float[] source)
            {
                foreach(float y in new[]{.97f,1.00f,1.04f,1.08f,1.10f,1.135f})
                {
                    float actual=ClothSectionWidth(body,y,dye),skin=Width(source,y);
                    maximumWidthAllowance=MathF.Max(maximumWidthAllowance,actual-skin);
                    if(actual<skin+.008f||actual>skin+.040f)
                        throw new InvalidOperationException($"Clothing duplicates or cuts through the source trunk at {y}: {actual}/{skin}");
                }
                float hip=ClothSectionWidth(rig.Trousers!.Mesh,.78f),expected=Width(source,.78f)+.010f;
                if(MathF.Abs(hip-expected)>.018f)
                    throw new InvalidOperationException($"Trouser pelvis departs from its source envelope: {hip}/{expected}");
            }
            Check(male,m,maleDye,sourceMale);Check(female,f,femaleDye,sourceFemale);
            GD.Print($"TAILORED_VOLUME_PASS: {(distant?"far":"near")} final cloth section width allowance <=40 mm, measured max={maximumWidthAllowance*1000:F2} mm, source-bound support");
            // Preserve the authored body references after clothing, outer
            // scale and distant batching, rather than checking only a sign.
            float sourceHipDifference=(Width(sourceFemale,.78f)-Width(sourceMale,.78f));
            float actualHipDifference=ClothSectionWidth(female.Trousers!.Mesh,.78f)-ClothSectionWidth(male.Trousers!.Mesh,.78f);
            if(actualHipDifference<.014f)
                throw new InvalidOperationException("Current adult designs lose their intended clothed pelvic silhouette contrast");
            float sourceChestDifference=Width(sourceMale,1.135f)-Width(sourceFemale,1.135f);
            float actualChestDifference=ClothSectionWidth(m,1.135f,maleDye)-ClothSectionWidth(f,1.135f,femaleDye);
            if(MathF.Abs(actualHipDifference-sourceHipDifference)>.015f||MathF.Abs(actualChestDifference-sourceChestDifference)>.015f)
                throw new InvalidOperationException($"Clothing or LOD erases the authored body contrast: hip={actualHipDifference}/{sourceHipDifference}, chest={actualChestDifference}/{sourceChestDifference}");
            if(femaleShoulder>=maleShoulder||ClothSectionWidth(female.Trousers!.Mesh,.78f)<=ClothSectionWidth(male.Trousers!.Mesh,.78f))
                throw new InvalidOperationException("Source shoulder/pelvis contrast was lost during clothing or LOD conversion");
            GD.Print($"ADULT_CONTOUR_SECTION: {(distant?"far":"near")} shoulder/hip={maleShoulder/maleHip:F3}/{femaleShoulder/femaleHip:F3}, waist/hip={maleWaist/maleHip:F3}/{femaleWaist/femaleHip:F3}, hip width={maleHip:F3}/{femaleHip:F3} m; source-bound clothing");
        }
        float Front(Mesh mesh,float y,float sampleX=0,bool fittedOnly=false)
        {
            var a=mesh.SurfaceGetArrays(0);var p=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();var ids=a[(int)Mesh.ArrayType.Index].AsInt32Array();
            var uv=a[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            float front=float.PositiveInfinity;
            for(int i=0;i<ids.Length;i+=3)
            {
                // The distant batch also contains leather straps and trim.
                // Compare cloth with cloth, using its preserved material flag.
                if(fittedOnly&&(uv.Length!=p.Length||uv[ids[i]].X<.5f||uv[ids[i+1]].X<.5f||uv[ids[i+2]].X<.5f))continue;
                var x=p[ids[i]];var b=p[ids[i+1]];var c=p[ids[i+2]];
                var ab=new Vector2(b.X-x.X,b.Y-x.Y);var ac=new Vector2(c.X-x.X,c.Y-x.Y);float determinant=ab.Cross(ac);
                if(MathF.Abs(determinant)<1e-10f)continue;
                var hit=new Vector2(sampleX-x.X,y-x.Y);float v=hit.Cross(ac)/determinant,w=ab.Cross(hit)/determinant;
                if(v>=0&&w>=0&&v+w<=1)front=MathF.Min(front,x.Z*(1-v-w)+b.Z*v+c.Z*w);
            }
            if(!float.IsFinite(front))throw new InvalidOperationException("Missing final garment profile sample");
            return front;
        }
        (float Projection,float Apex) ThoraxProfile(Mesh mesh)
        {
            float minimum=float.PositiveInfinity,apex=0;
            for(int row=0;row<=22;row++)
            {
                float y=1.09f+row*.005f,z=Front(mesh,y);
                for(int col=-6;col<=6;col++)z=MathF.Min(z,Front(mesh,y,col*.01f));
                if(z<minimum){minimum=z;apex=y;}
            }
            return(Front(mesh,1.00f)-minimum,apex);
        }
        var maleThorax=ThoraxProfile(male.DetailedBodyMesh);var femaleThorax=ThoraxProfile(female.DetailedBodyMesh);
        // Inspect the rendered front panel, not the source cage or a shader
        // label. A central trough must not split the shared cloth span, and
        // distant batching must retain the same surface across that span.
        var skinSupport=FemaleThoraxSupport();
        float SkinFront(float y,float x)
        {
            float z=float.PositiveInfinity;
            for(int col=0;col<TrunkColumns;col++)
            {
                float a=col*Mathf.Tau/TrunkColumns,b=(col+1)*Mathf.Tau/TrunkColumns;
                float ra=TrunkSample(skinSupport,y,a),rb=TrunkSample(skinSupport,y,b);
                float ax=MathF.Cos(a)*ra,bx=MathF.Cos(b)*rb;
                if((x-ax)*(x-bx)>0||MathF.Abs(bx-ax)<1e-8f)continue;
                z=MathF.Min(z,Mathf.Lerp(MathF.Sin(a)*ra,MathF.Sin(b)*rb,(x-ax)/(bx-ax)));
            }
            if(!float.IsFinite(z))throw new InvalidOperationException("Missing anterior anatomical section");
            return z;
        }
        float maximumRecess=0,maximumLodGap=0;Vector2 worstLodSample=Vector2.Zero;
        for(int row=0;row<=8;row++)
        {
            float y=1.10f+row*.01f;
            float recess=Front(female.DetailedBodyMesh,y)-.5f*(Front(female.DetailedBodyMesh,y,-.05f)+Front(female.DetailedBodyMesh,y,.05f));
            float skinRecess=SkinFront(y,0)-.5f*(SkinFront(y,-.05f)+SkinFront(y,.05f));
            if(recess>MathF.Min(.005f,MathF.Max(0,skinRecess)+.0015f))
                throw new InvalidOperationException($"Cloth carves a recess beyond its anatomical support: {y}/{recess}/{skinRecess}");
            maximumRecess=MathF.Max(maximumRecess,recess);
            for(int col=-7;col<=7;col++)
            {
                float x=col*.01f;
                if(Front(female.DetailedBodyMesh,y,x)>SkinFront(y,x)+.001f)
                    throw new InvalidOperationException($"Front cloth enters its source skin: {x}/{y}");
                float gap=MathF.Abs(Front(female.DetailedBodyMesh,y,x)-Front(female.DistantBodyMesh,y,x,true));
                if(gap>maximumLodGap){maximumLodGap=gap;worstLodSample=new(x,y);}
            }
        }
        if(maximumLodGap>.001f)
            throw new InvalidOperationException($"Front cloth span has a central trough or inconsistent LOD: {maximumRecess}/{maximumLodGap}, at {worstLodSample}");
        GD.Print($"CHEST_PANEL_PASS: source-bound anterior relief <=5 mm, skin clearance and near/far deviation <=1 mm; measured {maximumRecess*1000:F2}/{maximumLodGap*1000:F2} mm");
        float PairedFront(float y)=>.5f*(Front(female.DetailedBodyMesh,y,-.045f)+Front(female.DetailedBodyMesh,y,.045f));
        float chestRelief=.5f*(PairedFront(1.085f)+PairedFront(1.205f))-PairedFront(1.15f);
        if(chestRelief<.012f)throw new InvalidOperationException($"Final cloth erases the adult anterior chest volume: {chestRelief}");
        GD.Print($"CHEST_VOLUME_PASS: final paired cloth profile relief={chestRelief*1000:F2} mm, continuous upper/lower supports; existing sternum, skin and LOD constraints retained");
        float maximumChestBend=0;
        foreach(float x in new[]{0f,.045f,.075f,.09f})
            for(int row=1;row<26;row++)
            {
                float y=1.075f+row*.005f;
                float bend=MathF.Abs(Front(female.DetailedBodyMesh,y-.005f,x)-2*Front(female.DetailedBodyMesh,y,x)+Front(female.DetailedBodyMesh,y+.005f,x));
                maximumChestBend=MathF.Max(maximumChestBend,bend);
            }
        if(maximumChestBend>.003f)throw new InvalidOperationException($"Final chest retains an abrupt meridian tangent break: {maximumChestBend}");
        GD.Print($"CHEST_CURVE_PASS: final four anterior meridians, 5 mm sampling, maximum second depth difference={maximumChestBend*1000:F2} mm <=3 mm; source contact and LOD checks retained");
        // Measure the outer front contour independently of full trunk breadth:
        // the latter can stay unchanged even when anterior volume splays out.
        static float AnteriorWidth(float[] support,float y,float depth)
        {
            float low=float.PositiveInfinity,high=float.NegativeInfinity;
            for(int col=0;col<TrunkColumns;col++)
            {
                float a=col*Mathf.Tau/TrunkColumns,b=(col+1)*Mathf.Tau/TrunkColumns;
                float ra=TrunkSample(support,y,a),rb=TrunkSample(support,y,b);
                float za=MathF.Sin(a)*ra,zb=MathF.Sin(b)*rb;
                if((depth-za)*(depth-zb)>0||MathF.Abs(zb-za)<1e-8f)continue;
                float x=Mathf.Lerp(MathF.Cos(a)*ra,MathF.Cos(b)*rb,(depth-za)/(zb-za));
                low=MathF.Min(low,x);high=MathF.Max(high,x);
            }
            if(!float.IsFinite(low)||!float.IsFinite(high))throw new InvalidOperationException("Missing outer anterior support section");
            return high-low;
        }
        var originalSkin=FrameRadii("trunk-feminine-cage.json",FemaleTrunkHash,"skinRadii");
        float minimumReduction=float.PositiveInfinity,maximumOuterAllowance=0,maximumOuterLod=0;
        foreach(float y in new[]{1.13f,1.15f,1.17f})
        {
            float original=AnteriorWidth(originalSkin,y,-.11f),support=AnteriorWidth(skinSupport,y,-.11f);
            minimumReduction=MathF.Min(minimumReduction,original-support);
            // The finished edge is where the forward garment crosses z=-.11.
            float Edge(Mesh mesh,int side,bool far)
            {
                float inside=.07f,outside=.115f;
                if(Front(mesh,y,side*inside,far)>-.11f||Front(mesh,y,side*outside,far)<-.11f)
                    throw new InvalidOperationException("Missing final anterior cloth contour bracket");
                for(int i=0;i<12;i++)
                {
                    float x=(inside+outside)*.5f;
                    if(Front(mesh,y,side*x,far)<-.11f)inside=x;else outside=x;
                }
                return (inside+outside)*.5f;
            }
            float near=Edge(female.DetailedBodyMesh,-1,false)+Edge(female.DetailedBodyMesh,1,false);
            float far=Edge(female.DistantBodyMesh,-1,true)+Edge(female.DistantBodyMesh,1,true);
            maximumOuterAllowance=MathF.Max(maximumOuterAllowance,near-support);
            maximumOuterLod=MathF.Max(maximumOuterLod,MathF.Abs(near-far));
            if(original-support<.010f||near<support||near-support>.025f||MathF.Abs(near-far)>.002f)
                throw new InvalidOperationException($"Anterior cloth restores excessive lateral volume: y={y}, original/support/near/far={original}/{support}/{near}/{far}");
        }
        GD.Print($"CHEST_LATERAL_PASS: three final anterior sections; support breadth reduction >=10 mm, measured {minimumReduction*1000:F2} mm; total cloth width allowance={maximumOuterAllowance*1000:F2} mm, near/far edge sum difference={maximumOuterLod*1000:F2} mm");
        float maleProjection=maleThorax.Projection,femaleProjection=femaleThorax.Projection;
        if(femaleThorax.Apex<1.145f||femaleProjection<.008f)
            throw new InvalidOperationException("Fitted female thoracic profile loses its authored supported apex");
        GD.Print($"FRAME_READABILITY_PASS: clothed pelvic contrast retained; female thoracic apex={femaleThorax.Apex:F3} m, projection={femaleProjection*1000:F2} mm; design regression, not identity classification");
        var sourceM=FrameRadii("trunk-masculine-cage.json",MaleTrunkHash);
        var sourceF=FrameRadii("trunk-feminine-cage.json",FemaleTrunkHash);
        float ExpectedProjection(float[] source)
        {
            float peak=0;for(int row=0;row<=22;row++)peak=MathF.Max(peak,TrunkSample(source,1.09f+row*.005f,-MathF.PI/2));
            return peak-TrunkSample(source,1.00f,-MathF.PI/2);
        }
        if(MathF.Abs(maleProjection-ExpectedProjection(sourceM))>.016f||MathF.Abs(femaleProjection-ExpectedProjection(sourceF))>.016f)
            throw new InvalidOperationException($"Garment adds an artificial chest/waist projection: {maleProjection:F4}/{femaleProjection:F4}");
        GD.Print($"TRUNK_PROFILE_PASS: source-bound chest-to-waist projection={maleProjection:F4}/{femaleProjection:F4} m, fitted mounts and distinct source pelvis");
        // All eight adult identities use the same smooth mapping for normals
        // and attached facial parts. Test local invertibility across the actual
        // authored skin, rather than only checking a few designed landmarks.
        int previous=_faceVariant;
        try
        {
            for(_faceVariant=0;_faceVariant<8;_faceVariant++)
            {
                var source=HumanData().Head.Points;
                for(int i=0;i<source.Length;i+=11)
                {
                    var p=source[i];const float h=.0001f;
                    var dx=(HumanPoint(p+Vector3.Right*h)-HumanPoint(p-Vector3.Right*h))/(2*h);
                    var dy=(HumanPoint(p+Vector3.Up*h)-HumanPoint(p-Vector3.Up*h))/(2*h);
                    var dz=(HumanPoint(p+Vector3.Back*h)-HumanPoint(p-Vector3.Back*h))/(2*h);
                    float determinant=new Basis(dx,dy,dz).Determinant();
                    if(!float.IsFinite(determinant)||determinant<.65f||determinant>1.45f)
                        throw new InvalidOperationException("Adult regional face fit folds or over-stretches the source surface");
                }
            }
        }
        finally{_faceVariant=previous;}
        GD.Print("ADULT_CONTOURS_PASS: final near/far clothed sections retain shoulder/pelvis and waist/hip differences; eight adult face maps remain locally invertible");
    }
}
