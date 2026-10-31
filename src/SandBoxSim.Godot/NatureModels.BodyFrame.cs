using System;
using System.Text.Json;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private float[]? _trunkMale,_trunkFeminine;
    private readonly float[][] _garmentFrameReference=new float[3][];
    private const int TrunkRows=113,TrunkColumns=128;
    private const float TrunkLow=.74f,TrunkHigh=1.30f;
    private static float[] FrameRadii(string file,string expected)
    {
        string raw=FileAccess.GetFileAsString("res://assets/models/human/"+file).Replace("\r\n","\n",StringComparison.Ordinal);
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        if(hash!=expected)throw new InvalidOperationException("Changed body frame support: "+file);
        using var doc=JsonDocument.Parse(raw);var root=doc.RootElement;
        if(root.GetProperty("schemaVersion").GetInt32()!=1||root.GetProperty("rows").GetInt32()!=TrunkRows||root.GetProperty("columns").GetInt32()!=TrunkColumns)
            throw new InvalidOperationException("Invalid body frame grid");
        if(root.GetProperty("low").GetSingle()!=TrunkLow||root.GetProperty("high").GetSingle()!=TrunkHigh)throw new InvalidOperationException("Invalid trunk sample bounds");
        var radii=new float[TrunkRows*TrunkColumns];int i=0;
        foreach(var value in root.GetProperty("radii").EnumerateArray())
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
        int r=Math.Min(TrunkRows-2,(int)row),c=(int)column;float u=column-c,v=row-r;
        float At(int h)=>Mathf.Lerp(data[h*TrunkColumns+c],data[h*TrunkColumns+(c+1)%TrunkColumns],u);
        return Mathf.Lerp(At(r),At(r+1),v);
    }
    private float[] GarmentFrameReference()
    {
        if(_garmentFrameReference[_garmentCut]!=null)return _garmentFrameReference[_garmentCut];
        var samples=new float[TrunkRows*TrunkColumns];
        for(int row=0;row<TrunkRows;row++)for(int col=0;col<TrunkColumns;col++)
        {
            float y=TrunkLow+row*.005f;
            float fraction=Math.Clamp((ResidentRig.SourceBodyY(y)-GarmentBottom)/(1.28f-GarmentBottom),0,1);
            var p=GarmentPoint(fraction,col*Mathf.Tau/TrunkColumns);
            samples[row*TrunkColumns+col]=new Vector2(p.X,p.Z).Length();
        }
        return _garmentFrameReference[_garmentCut]=samples;
    }
    private Vector3 FramePoint(Vector3 point,float sleeve=0,bool tailored=false)
    {
        bool feminine=BodyFrame==1;
        float upper=MathF.Max(sleeve,Mathf.SmoothStep(1.19f,1.27f,point.Y));
        float panelHeight=feminine&&tailored?Mathf.Lerp(point.Y,TailoredPanelHeight(point),1-upper):point.Y;
        float active=Mathf.SmoothStep(.70f,.735f,point.Y)*(1-Mathf.SmoothStep(1.19f,1.27f,point.Y));
        var fitted=point;
        if(active>0&&point.X*point.X+point.Z*point.Z>.00001f)
        {
            _trunkMale??=FrameRadii("trunk-masculine-cage.json","27836b278e247921af480fca63edca7c4d65f11655cb1272a19fb3909e43821c");
            _trunkFeminine??=FrameRadii("trunk-feminine-cage.json","0868f6499f700995e3fb0eaea1ed7aea623406fbbfa5486b38a35e5d74d3fcad");
            float angle=MathF.Atan2(point.Z,point.X),front=MathF.Max(0,-MathF.Sin(angle));
            // Fit the source skin's complete thorax/lumbar/pelvic envelope.
            // Millimetre-scale ease bridges the skin, rather than shrinking
            // every body part with one sex-dependent actor scale.
            var support=feminine?_trunkFeminine:_trunkMale;
            // Sample the anatomy where the finished panel actually lands.
            // Using its pre-shortening height inflated the upper hip/hem and
            // hid the natural waist-to-pelvis transition under a straight box.
            float supportY=MathF.Max(.80f,panelHeight);
            float target=ClothEnvelope(support,supportY,angle);
            // The overlapping shirt is the outer layer. Reserve common
            // movement ease over the trouser waistband through lumbar flexion,
            // independently of body sex and its anatomical proportions.
            target+=.008f*(1-Mathf.SmoothStep(.94f,1.06f,panelHeight));
            // The source already contains the thorax, waist and pelvic form.
            // Garment ease is added by ClothEnvelope; applying another chest
            // bulge or carved waist here duplicates those anatomical features.
            target+=.020f*front*(1-Mathf.SmoothStep(.80f,.94f,panelHeight));
            float ratio=Math.Clamp(target/TrunkSample(GarmentFrameReference(),point.Y,angle),.65f,1.35f);
            ratio=Mathf.Lerp(1,ratio,active);fitted=new(point.X*ratio,point.Y,point.Z*ratio);
        }
        // The arm root, sewn sleeve and inverse bind use the same lateral
        // fit. Authored pattern coordinates keep trunk fitting off the cuffs.
        var sleevePoint=new Vector3(point.X*ArmWidthScale,point.Y,point.Z*(feminine?.90f:1));
        var result=fitted.Lerp(sleevePoint,upper);
        // A continuous neck-to-acromion slope, carried through the entire
        // sleeve. The joint rest and inverse bind receive the same drop.
        result.Y-=(feminine?.014f:.009f)*Mathf.SmoothStep(.065f,.18f,MathF.Abs(point.X))*MathF.Max(sleeve,Mathf.SmoothStep(1.08f,1.23f,point.Y));
        // Fit the garment's lower panel independently of the pelvis/trousers.
        // The raised hem still overlaps the trouser waist; sleeves and rigid
        // equipment retain their original length and attachment transforms.
        if(tailored)
        {
            if(feminine)
            {
                // A fitted blouse ends above the fullest hip instead of hiding
                // the pelvis under the same bell-shaped tunic as every worker.
                result.Y+=panelHeight-point.Y;
            }
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
                        // introduce the axial correction over four centimetres
                        // of cloth before requiring a free sleeve centreline.
                        float gather=Math.Clamp((shoulderY-points[i].Y)/.263f,0,1)*Mathf.SmoothStep(0,.04f,sleeveDistance[i]);
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
        rig.DistantBodyMesh=FrameMesh(rig.DistantBodyMesh,Transform3D.Identity,true);
        rig.HipHalfWidth=.10f;
        rig.ArmRestClearance=0;
        for(int side=0;side<2;side++)rig.Legs[side].Position=new(side==0?-rig.HipHalfWidth:rig.HipHalfWidth,ResidentRig.HipHeight,0);
        // The fitted sleeve vertices, joint rest and inverse bind must move
        // together. Scaling the mesh alone leaves a male-width moving rig.
        rig.ShoulderHalfWidth=.202f*ArmWidthScale;
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
    private void ValidateBodyFrames()
    {
        int previous=_faceVariant;
        var male=Resident(17,false,SandBoxSim.Core.Agents.JobType.Gatherer);
        var female=Resident(42,false,SandBoxSim.Core.Agents.JobType.Gatherer);
        try
        {
            ValidateAdultContourSections(male,female);
            foreach(var rig in new[]{male,female})
            {
                ValidateArmClothingFit(rig);
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
            if(hemRise<.085f||hemRise>.110f||MathF.Abs(Hem(female.DetailedBodyMesh)-.852f)>.002f)
                throw new InvalidOperationException("Tailored hem loses trouser coverage or retains the common long cut");
            GD.Print($"BODY_CUT_PASS: raised garment hem={hemRise:F4} m, preserved trouser overlap and separate lower-body geometry");
            if(MathF.Abs(female.HipHalfWidth-.10f)>.00001f||MathF.Abs(male.HipHalfWidth-.10f)>.00001f||female.Holster.Position.Y<male.Holster.Position.Y+.10f)
                throw new InvalidOperationException("Neutral stance or waist-mounted tool does not match the fitted clothing");
            if(male.DetailedBodyMesh==female.DetailedBodyMesh||male.DistantBodyMesh==female.DistantBodyMesh)
                throw new InvalidOperationException("Adult frames share their fitted body cache");
            if(female.ShoulderHalfWidth>=male.ShoulderHalfWidth-.01f||female.DetailedBodySkin==male.DetailedBodySkin)
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
            var p=new Vector3(.08f,1.16f,-.13f);
            if(FramePoint(p).DistanceTo(p)<.002f)throw new InvalidOperationException("Source female frame has no chest contour difference");
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
                if(y<=.075f&&fitted.Point!=RefineWristContour(point,normal).Point)
                    throw new InvalidOperationException("Muscle fit changes the grip or wrist attachment");
            }
            var rim=new System.Collections.Generic.List<Vector3>();
            float hem=Hem(female.DetailedBodyMesh);
            foreach(var point in female.DetailedBodyMesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                if(point.Y<=hem+.009f)rim.Add(point);
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
            var sourceMale=FrameRadii("trunk-masculine-cage.json","27836b278e247921af480fca63edca7c4d65f11655cb1272a19fb3909e43821c");
            var sourceFemale=FrameRadii("trunk-feminine-cage.json","0868f6499f700995e3fb0eaea1ed7aea623406fbbfa5486b38a35e5d74d3fcad");
            float Width(float[] source,float y)=>TrunkSample(source,y,0)+TrunkSample(source,y,MathF.PI);
            void Check(ResidentRig rig,Mesh body,Color? dye,float[] source)
            {
                foreach(float y in new[]{1.00f,1.135f})
                {
                    float actual=ClothSectionWidth(body,y,dye),skin=Width(source,y);
                    if(actual<skin+.008f||actual>skin+.056f)
                        throw new InvalidOperationException($"Clothing duplicates or cuts through the source trunk at {y}: {actual}/{skin}");
                }
                float hip=ClothSectionWidth(rig.Trousers!.Mesh,.78f),expected=Width(source,.78f)+.014f;
                if(MathF.Abs(hip-expected)>.018f)
                    throw new InvalidOperationException($"Trouser pelvis departs from its source envelope: {hip}/{expected}");
            }
            Check(male,m,maleDye,sourceMale);Check(female,f,femaleDye,sourceFemale);
            if(femaleShoulder>=maleShoulder||ClothSectionWidth(female.Trousers!.Mesh,.78f)<=ClothSectionWidth(male.Trousers!.Mesh,.78f))
                throw new InvalidOperationException("Source shoulder/pelvis contrast was lost during clothing or LOD conversion");
            GD.Print($"ADULT_CONTOUR_SECTION: {(distant?"far":"near")} shoulder/hip={maleShoulder/maleHip:F3}/{femaleShoulder/femaleHip:F3}, waist/hip={maleWaist/maleHip:F3}/{femaleWaist/femaleHip:F3}, hip width={maleHip:F3}/{femaleHip:F3} m; source-bound clothing");
        }
        float Front(Mesh mesh,float y)
        {
            var a=mesh.SurfaceGetArrays(0);var p=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();var ids=a[(int)Mesh.ArrayType.Index].AsInt32Array();
            float front=float.PositiveInfinity;
            for(int i=0;i<ids.Length;i+=3)
            {
                var x=p[ids[i]];var b=p[ids[i+1]];var c=p[ids[i+2]];
                var ab=new Vector2(b.X-x.X,b.Y-x.Y);var ac=new Vector2(c.X-x.X,c.Y-x.Y);float determinant=ab.Cross(ac);
                if(MathF.Abs(determinant)<1e-10f)continue;
                var hit=new Vector2(-x.X,y-x.Y);float v=hit.Cross(ac)/determinant,w=ab.Cross(hit)/determinant;
                if(v>=0&&w>=0&&v+w<=1)front=MathF.Min(front,x.Z*(1-v-w)+b.Z*v+c.Z*w);
            }
            if(!float.IsFinite(front))throw new InvalidOperationException("Missing final garment profile sample");
            return front;
        }
        float maleProjection=Front(male.DetailedBodyMesh,1.00f)-Front(male.DetailedBodyMesh,1.135f);
        float femaleProjection=Front(female.DetailedBodyMesh,1.00f)-Front(female.DetailedBodyMesh,1.135f);
        var sourceM=FrameRadii("trunk-masculine-cage.json","27836b278e247921af480fca63edca7c4d65f11655cb1272a19fb3909e43821c");
        var sourceF=FrameRadii("trunk-feminine-cage.json","0868f6499f700995e3fb0eaea1ed7aea623406fbbfa5486b38a35e5d74d3fcad");
        float ExpectedProjection(float[] source)=>TrunkSample(source,1.135f,-MathF.PI/2)-TrunkSample(source,1.00f,-MathF.PI/2);
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
