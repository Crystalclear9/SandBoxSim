using System;
using System.Text.Json;
using Godot;

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
        }
        var arrays=new Godot.Collections.Array();arrays.Resize((int)Mesh.ArrayType.Max);
        var seam=new Vector2[count];
        for(int i=0;i<count;i++)seam[i]=new(MathF.Atan2(points[i].Z,MathF.Abs(points[i].X)-.10f),points[i].Y);
        arrays[(int)Mesh.ArrayType.Vertex]=points;arrays[(int)Mesh.ArrayType.Normal]=normals;arrays[(int)Mesh.ArrayType.TexUV]=coords;arrays[(int)Mesh.ArrayType.TexUV2]=seam;
        arrays[(int)Mesh.ArrayType.Bones]=b;arrays[(int)Mesh.ArrayType.Weights]=w;arrays[(int)Mesh.ArrayType.Index]=ids;
        var material=(ShaderMaterial)Fabric(BodyFrame==1?"#656356":"#645e53");
        material.SetShaderParameter("trouser_details",true);
        material.SetShaderParameter("tailored_trousers",BodyFrame==1);
        var mesh=new ArrayMesh {BlendShapeMode=Mesh.BlendShapeMode.Normalized};
        var bends=new Godot.Collections.Array<Godot.Collections.Array>();
        if(BodyFrame==1)
        {
            mesh.AddBlendShape("LeftHipFlex");mesh.AddBlendShape("RightHipFlex");
            for(int side=0;side<2;side++)bends.Add(BakeTrouserHipFlex(points,normals,ids,side));
            mesh.AddBlendShape("LeftKneeFlex");mesh.AddBlendShape("RightKneeFlex");
            for(int side=0;side<2;side++)bends.Add(BakeTrouserKneeFlex(points,normals,ids,b,w,side));
        }
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays,bends);mesh.SurfaceSetMaterial(0,material);
        return _meshes[key]=WithScreenLods(mesh);
    }

    private static void FitFemaleTrouserLegs(Vector3[] points,Vector3[] normals,int[] ids)
    {
        var rest=(Vector3[])points.Clone();
        var freedom=new float[points.Length];
        for(int i=0;i<points.Length;i++)
        {
            var point=rest[i];
            float side=MathF.Sign(point.X);
            // Each leg hangs from its own cloth column, which sits slightly
            // inward of the bone near the knee. Pin both ends of the panel.
            var radial=new Vector3(point.X-side*.087f,0,point.Z).Normalized();
            float panel=Mathf.SmoothStep(.405f,.464f,point.Y)*(1-Mathf.SmoothStep(.615f,.725f,point.Y));
            float outer=Mathf.SmoothStep(-.75f,.65f,side*radial.X);
            float aboveKnee=Mathf.SmoothStep(.454f,.490f,point.Y)*(1-Mathf.SmoothStep(.535f,.590f,point.Y));
            float ease=panel*(.0075f+.0025f*aboveKnee)*Mathf.Lerp(.56f,1,outer);
            points[i]+=new Vector3(radial.X*.88f,0,radial.Z)*ease;
            freedom[i]=panel;
        }
        TransportFairingNormals(rest,points,normals,ids,freedom);
    }

    private static Godot.Collections.Array BakeTrouserKneeFlex(Vector3[] rest,Vector3[] restNormals,int[] ids,int[] bones,float[] weights,int side)
    {
        var points=(Vector3[])rest.Clone();
        var normals=(Vector3[])restNormals.Clone();
        var freedom=new float[rest.Length];
        const float angle=-1.12f;
        var rotation=new Basis(Vector3.Right,angle);
        var pivot=new Vector3((side==0?-1:1)*ResidentRig.NeutralHipHalfWidth,ResidentRig.HipHeight-ResidentRig.ThighLength,0);
        for(int i=0;i<rest.Length;i++)
        {
            var point=rest[i];
            if((point.X<0)!=(side==0))continue;
            float panel=Mathf.SmoothStep(.409f,.443f,point.Y)*(1-Mathf.SmoothStep(.500f,.555f,point.Y));
            if(panel<=0)continue;
            float kneeWeight=0;
            for(int j=0;j<4;j++)if(bones[i*4+j]==side+3)kneeWeight+=weights[i*4+j];
            var lever=point-pivot;
            var linear=new Basis(Vector3.Right,Vector3.Up.Lerp(rotation.Y,kneeWeight),Vector3.Back.Lerp(rotation.Z,kneeWeight));
            var arc=new Basis(Vector3.Right,angle*kneeWeight)*lever;
            // Restore the radius lost when the straight and bent bone
            // matrices are blended. Limit this to the outside of the bend.
            float outside=1-Mathf.SmoothStep(-.012f,.038f,point.Z);
            var edit=(linear.Inverse()*arc-lever)*outside;
            float rear=Mathf.SmoothStep(.012f,.041f,point.Z);
            float fold=(point.Y-(.456f+.19f*(MathF.Abs(point.X)-.088f)))/.011f;
            edit.Z+=rear*.0045f*MathF.Exp(-fold*fold);
            points[i]+=edit.LimitLength(.010f)*panel;
            freedom[i]=panel;
        }
        TransportFairingNormals(rest,points,normals,ids,freedom);
        var shape=new Godot.Collections.Array();
        shape.Resize((int)Mesh.ArrayType.Max);
        shape[(int)Mesh.ArrayType.Vertex]=points;
        shape[(int)Mesh.ArrayType.Normal]=normals;
        return shape;
    }

    private static Godot.Collections.Array BakeTrouserHipFlex(Vector3[] rest,Vector3[] restNormals,int[] ids,int side)
    {
        var points=(Vector3[])rest.Clone();var normals=(Vector3[])restNormals.Clone();
        var freedom=new float[rest.Length];
        for(int i=0;i<rest.Length;i++)
        {
            var p=rest[i];float x=MathF.Abs(p.X);
            if((p.X<0)!=(side==0))continue;
            float panel=Mathf.SmoothStep(.54f,.65f,p.Y)*(1-Mathf.SmoothStep(.75f,.83f,p.Y))*Mathf.SmoothStep(.028f,.080f,x);
            float front=Mathf.SmoothStep(.014f,.071f,-p.Z),rear=Mathf.SmoothStep(.017f,.084f,p.Z);
            float fold=(p.Y-(.723f-.27f*(x-.09f)))/.034f;
            float diagonal=.010f*MathF.Exp(-fold*fold)*front;
            // The raised leg compresses the front cloth into a broad diagonal
            // fold and retains a rounded transition behind the pelvis.
            points[i].Z+=panel*(.007f*rear-diagonal);
            freedom[i]=panel*(front+rear);
        }
        TransportFairingNormals(rest,points,normals,ids,freedom);
        var shape=new Godot.Collections.Array();shape.Resize((int)Mesh.ArrayType.Max);
        shape[(int)Mesh.ArrayType.Vertex]=points;shape[(int)Mesh.ArrayType.Normal]=normals;
        return shape;
    }
    private void AddContinuousTrousers(ResidentRig rig)
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
        skeleton.ResetBonePoses();
        rig.Trousers=new MeshInstance3D {Name="ContinuousTrousers",Mesh=ContinuousTrousers(),Skin=skin,Skeleton=new NodePath("../TrouserSkeleton")};rig.AddChild(rig.Trousers);
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
                if(shapes.Count!=(BodyFrame==1?4:0)||mesh.BlendShapeMode!=Mesh.BlendShapeMode.Normalized)
                    throw new InvalidOperationException("Trouser LOD generation dropped its flex correctives");
                var targets=new Vector3[shapes.Count][];
                for(int shape=0;shape<shapes.Count;shape++)
                {
                    var target=targets[shape]=shapes[shape][(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    var normals=shapes[shape][(int)Mesh.ArrayType.Normal].AsVector3Array();
                    bool knee=shape>=2;
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
                            ||displacement>.01001f||fixedPoint&&displacement>.000001f)
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
                    var transforms=new Transform3D[5];
                    for(int bone=0;bone<5;bone++)transforms[bone]=skeleton.GetBoneGlobalPose(bone)*skin.GetBindPose(bone);
                    for(int side=0;side<2;side++)if(!transforms[side+1].IsEqualApprox(rig.Legs[side].Transform*skin.GetBindPose(side+1)))
                        throw new InvalidOperationException($"Trouser skin mismatch: {transforms[side+1]} expected {rig.Legs[side].Transform*skin.GetBindPose(side+1)}");
                    for(int side=0;side<2;side++)if(!transforms[side+3].IsEqualApprox(rig.Legs[side].Transform*rig.Knees[side].Transform*skin.GetBindPose(side+3)))
                        throw new InvalidOperationException("Trouser knee skin does not follow the actual boot rim");
                    for(int shape=0;shape<shapeWeights.Length;shape++)
                        shapeWeights[shape]=rig.Trousers.GetBlendShapeValue(shape);
                    for(int side=0;side<shapeWeights.Length/2;side++)
                    {
                        peakHipWeights[side]=MathF.Max(peakHipWeights[side],shapeWeights[side]);
                        peakKneeWeights[side]=MathF.Max(peakKneeWeights[side],shapeWeights[side+2]);
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
                        if(ratio>3)throw new InvalidOperationException($"Trouser crotch tears during {action}: stretch={ratio}");
                    }
                    samples++;
                }
                for(int shape=0;shape<shapeWeights.Length;shape++)shapeWeights[shape]=rig.Trousers.GetBlendShapeValue(shape);
                var frozen=skeleton.GetBonePose(1);rig.PoseAction(1,SandBoxSim.Core.Agents.ActionKind.Take,SandBoxSim.Core.Agents.ActionPhase.Executing,true,0);
                if(skeleton.GetBonePose(1)!=frozen)throw new InvalidOperationException("Paused trousers continue moving");
                for(int shape=0;shape<shapeWeights.Length;shape++)
                    if(shapeWeights[shape]!=rig.Trousers.GetBlendShapeValue(shape))throw new InvalidOperationException("Paused trouser correctives continue moving");
                if(shapeWeights.Length>0)ValidateKneeCloth(rig,p,b,w,targets);
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
            var transform=new Basis(Vector3.Right,-1.12f);
            for(int i=0;i<points.Length;i++)
            {
                var p=points[i];
                if((p.X<0)!=(side==0)||p.Z>=-.035f||p.Y<.45f||p.Y>.52f)continue;
                float knee=0;
                for(int j=0;j<4;j++)if(bones[i*4+j]==side+3)knee+=weights[i*4+j];
                if(knee<.15f||knee>.85f)continue;
                var pivot=new Vector3(p.X,ResidentRig.HipHeight-ResidentRig.ThighLength,0);
                var lever=p-pivot;
                var corrected=targets[side+2][i]-pivot;
                before+=MathF.Abs(lever.Length()-lever.Lerp(transform*lever,knee).Length());
                after+=MathF.Abs(lever.Length()-corrected.Lerp(transform*corrected,knee).Length());
                samples++;
            }
        }
        if(samples<20||after>=before*.8f)
            throw new InvalidOperationException($"Knee correction does not preserve the front cloth radius: {samples}, {before}/{after}");
        GD.Print($"KNEE_CLOTH_RADIUS_PASS: {samples} knee-front samples at 64 degrees, mean radius loss={1000*before/samples:F2} -> {1000*after/samples:F2} mm");
    }
}

internal partial class ResidentRig
{
    internal Skeleton3D? TrouserSkeleton;
    internal MeshInstance3D? Trousers;
    internal bool TrouserHipFlex;
    private readonly float[] _trouserFlexWeights=new float[4];
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
            SyncTrouserFlex(side+2,Mathf.SmoothStep(.15f,1.12f,-Knees[side].Rotation.X));
        }
    }
    private void SyncTrouserFlex(int shape,float weight)
    {
        if(MathF.Abs(weight-_trouserFlexWeights[shape])<=.00001f)return;
        Trousers!.SetBlendShapeValue(shape,weight);
        _trouserFlexWeights[shape]=weight;
    }
}
