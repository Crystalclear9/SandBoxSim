using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private void ValidateLumbarClothing()
    {
        int probes=0;float excursion=0,backYoke=0,lumbarSlope=0;
        foreach(int identity in new[]{17,55})
        foreach(var job in new[]{JobType.Farmer,JobType.Gatherer,JobType.Trader})
        {
            var rig=Resident(identity,false,job);
            try
            {
                if(rig.ShoulderSkeleton.GetBoneCount()!=(rig.HasShoulderSupports?ResidentRig.SupportedBodyBones:4)||rig.DetailedBodySkin.GetBindCount()!=rig.ShoulderSkeleton.GetBoneCount())
                    throw new InvalidOperationException("Adult garment lacks its pelvic cloth binding");
                var cloth=rig.DetailedBodyMesh.SurfaceGetArrays(0);var p=cloth[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var bones=cloth[(int)Mesh.ArrayType.Bones].AsInt32Array();var w=cloth[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                var ids=cloth[(int)Mesh.ArrayType.Index].AsInt32Array();var posed=new Vector3[p.Length];
                if(FittedFigure)
                {
                    backYoke=MathF.Max(backYoke,MeasureRearCollarProjection(p,ids));
                    lumbarSlope=MathF.Max(lumbarSlope,MeasureLumbarSlope(p,ids));
                    ValidateFemaleHemCut(p);
                }
                var pants=rig.Trousers!.Mesh.SurfaceGetArrays(0);var pp=pants[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                float hem=float.PositiveInfinity;foreach(var point in p)hem=MathF.Min(hem,point.Y);
                var pb=pants[(int)Mesh.ArrayType.Bones].AsInt32Array();var pw=pants[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                float pickupPitch=0,priorPitch=0;
                foreach(var action in new[]{ActionKind.Take,ActionKind.BuildHouse,ActionKind.None})
                for(int f=0;f<120;f++)
                {
                    rig.PoseAction(1f/60,action,action==ActionKind.None?ActionPhase.Moving:ActionPhase.Executing,false,0);
                    if(action==ActionKind.Take)
                    {
                        float pitch=-rig.LowerGarmentFrame.Basis.GetEuler().X;
                        if(pitch<-.001f||pitch>MathF.Max(0,-rig.Torso.Rotation.X)+.015f||MathF.Abs(pitch-priorPitch)>.020f)
                            throw new InvalidOperationException("Pickup pelvis leads the chest, reverses or snaps between frames");
                        pickupPitch=MathF.Max(pickupPitch,pitch);
                        priorPitch=pitch;
                    }
                    if(f%30!=29)continue;
                    if(!rig.ShoulderSkeleton.GetBonePose(3).IsEqualApprox(rig.PelvicClothPose)||
                        (action!=ActionKind.Take&&!rig.LowerGarmentFrame.Basis.IsEqualApprox(action==ActionKind.None?rig.GaitPelvisBasis:Basis.Identity)))
                        throw new InvalidOperationException("Lower cloth inherits the rigid chest rotation");
                    // Sample the overlapping layers in the pelvis frame. A
                    // world-Z ray through a tilted open hem can leave through
                    // the opening before reaching its opposite cloth panel.
                    var toPelvis=rig.LowerGarmentFrame.AffineInverse();
                    var chest=toPelvis*rig.Torso.Transform;
                    var transforms=new Transform3D[rig.ShoulderSkeleton.GetBoneCount()];
                    for(int b=0;b<transforms.Length;b++)transforms[b]=chest*rig.ShoulderSkeleton.GetBonePose(b)*rig.DetailedBodySkin.GetBindPose(b);
                    for(int i=0;i<p.Length;i++)
                    {
                        var v=Vector3.Zero;for(int j=0;j<4;j++)v+=transforms[bones[i*4+j]]*p[i]*w[i*4+j];posed[i]=v;
                        if(!v.IsFinite())throw new InvalidOperationException("Non-finite lumbar skin deformation");
                        if(p[i].Y<.84f)
                        {
                            if(w[i*4+3]<.999f)throw new InvalidOperationException("The shirt hem is still bound to the rigid chest");
                            excursion=MathF.Max(excursion,v.DistanceTo(chest*p[i]));
                        }
                    }
                    if(MathF.Abs(rig.Holster.Basis.Determinant()-1)>.0001f)throw new InvalidOperationException("Lumbar bending stretches the mounted tool");
                    var trouserTransforms=new[]{rig.LowerGarmentFrame,rig.Legs[0].Transform*rig.Trousers.Skin.GetBindPose(1),rig.Legs[1].Transform*rig.Trousers.Skin.GetBindPose(2),
                        rig.Legs[0].Transform*rig.Knees[0].Transform*rig.Trousers.Skin.GetBindPose(3),rig.Legs[1].Transform*rig.Knees[1].Transform*rig.Trousers.Skin.GetBindPose(4)};
                    var trouserPoints=pp;
                    if(rig.TrouserHipFlex)
                    {
                        using var baked=rig.Trousers.BakeMeshFromCurrentBlendShapeMix();
                        trouserPoints=baked.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    }
                    for(int i=0;i<pp.Length;i+=9)
                    {
                        // Check the whole overlapping panel, including the
                        // travelling coat below the hip. The curved side of a
                        // short shirt begins above its front/back low points.
                        float localHem=FittedFigure
                            ?FinishedGarmentHem+FemaleHemOffset(MathF.Sin(MathF.Atan2(pp[i].Z,pp[i].X))):hem;
                        // Probe closer to the revised female hem so the
                        // exterior/facing fit is exercised during motion too.
                        float edgeMargin=FittedFigure?.013f:.020f;
                        if(pp[i].Y<localHem+edgeMargin||pp[i].Y>.91f)continue;
                        var point=Vector3.Zero;for(int j=0;j<4;j++)point+=trouserTransforms[pb[i*4+j]]*trouserPoints[i]*pw[i*4+j];
                        point=toPelvis*point;
                        // Below the front hem, a rear-tail slice is an open
                        // flap. Its outer back face must cover the trousers,
                        // but a second, front-facing wall cannot be required.
                        bool rearTail=FittedFigure&&_garmentCut==2&&pp[i].Z>0
                            &&pp[i].Y<FinishedGarmentHem+FemaleHemOffset(-1);
                        float front=float.PositiveInfinity,back=float.NegativeInfinity;
                        for(int t=0;t<ids.Length;t+=3)
                        {
                            var a=posed[ids[t]];var b=posed[ids[t+1]];var c=posed[ids[t+2]];
                            var ab=new Vector2(b.X-a.X,b.Y-a.Y);var ac=new Vector2(c.X-a.X,c.Y-a.Y);float det=ab.Cross(ac);
                            if(MathF.Abs(det)<1e-10f)continue;var hit=new Vector2(point.X-a.X,point.Y-a.Y);
                            // Inverse skin transforms introduce sub-micrometre
                            // roundoff at shared triangle edges. Include those
                            // edges without relaxing the millimetre clearance.
                            float u=hit.Cross(ac)/det,v=ab.Cross(hit)/det;if(u<-.000001f||v<-.000001f||u+v>1.000001f)continue;
                            float z=a.Z*(1-u-v)+b.Z*u+c.Z*v;
                            front=MathF.Min(front,z);
                            if(!rearTail||det<0)back=MathF.Max(back,z);
                        }
                        if(rearTail&&!float.IsFinite(back))
                        {
                            // A bent trouser leg can move below the open rear
                            // edge. Use the posed edge at this lateral station
                            // to distinguish exposure from a missing panel.
                            float lowerEdge=PosedRearEdgeAt(p,posed,ids,point.X);
                            if(float.IsFinite(lowerEdge)&&point.Y<lowerEdge)continue;
                        }
                        if(!float.IsFinite(back)||(!rearTail&&point.Z<front-.001f)||point.Z>back+.001f)
                            throw new InvalidOperationException($"Trousers pierce the lower shirt: {identity}/{job}/{action}/{f}, source={pp[i]}, point={point}, cloth={front}/{back}, hem={hem}, torso={rig.Torso.Rotation}, pelvis={rig.LowerGarmentFrame}");
                        probes++;
                    }
                }
                if(pickupPitch<(FittedFigure?.16f:.10f)||pickupPitch>.22f)
                    throw new InvalidOperationException($"Pickup loses its supported hip flexion: {identity}/{job}, pitch={pickupPitch}");
                GD.Print($"PICKUP_HIP_PASS: {identity}/{job}, peak {Mathf.RadToDeg(pickupPitch):F1} degrees, continuous 60 Hz samples");
                var frozen=rig.PelvicClothPose;var mount=rig.Holster.Transform;
                rig.PoseAction(1,ActionKind.Take,ActionPhase.Executing,true,0);
                if(rig.PelvicClothPose!=frozen||rig.Holster.Transform!=mount)throw new InvalidOperationException("Paused lower cloth or equipment moves");
            }
            finally{rig.Free();}
        }
        if(excursion<.015f||probes==0)throw new InvalidOperationException("Lumbar clothing remains a rigid plank");
        float sway=0,knee=0,entryDrift=0;
        foreach(int identity in new[]{17,55})
        {
            var rig=Resident(identity,false,JobType.None);
            try
            {
                var anchors=new[]{InTorso(rig.Feet[0],rig).Origin,InTorso(rig.Feet[1],rig).Origin};
                for(int f=0;f<240;f++)
                {
                    rig.PoseAction(1f/60,ActionKind.None,ActionPhase.Idle,false,.31f);
                    for(int side=0;side<2;side++)
                    {
                        var foot=InTorso(rig.Feet[side],rig);
                        float drift=foot.Origin.DistanceTo(anchors[side]);if(f<21)entryDrift=MathF.Max(entryDrift,drift);
                        if(drift>(f<21?.004f:.0001f)||foot.Basis.Y.Dot(Vector3.Up)<.99999f)
                            throw new InvalidOperationException($"Relaxed stance slides the feet or tilts the soles: {identity}/{f}/{side}, ankle={foot.Origin}, target={anchors[side]}, up={foot.Basis.Y}, hip={rig.Legs[side].Position}");
                        if(MathF.Abs(rig.Knees[side].Position.Length()-ResidentRig.ThighLength)>.000001f)
                            throw new InvalidOperationException("Relaxed stance changes the thigh length");
                        sway=MathF.Max(sway,MathF.Abs(rig.Legs[side].Position.X-(side==0?-rig.HipHalfWidth:rig.HipHalfWidth)));
                        knee=MathF.Max(knee,MathF.Abs(rig.Knees[side].Rotation.X));
                    }
                }
                var leg=rig.Legs[0].Transform;rig.PoseAction(1,ActionKind.None,ActionPhase.Idle,true,0);
                if(leg!=rig.Legs[0].Transform)throw new InvalidOperationException("Paused relaxed stance moves");
            }
            finally{rig.Free();}
        }
        if(sway<.005f||knee<.05f||knee>.12f)throw new InvalidOperationException("Relaxed stance locks the knees or crouches from the shoe-origin offset");
        if(backYoke<=0||backYoke>.012f)
            throw new InvalidOperationException($"Female rear yoke retains a protruding collar shelf: {backYoke*1000:F2} mm");
        if(lumbarSlope<=0||lumbarSlope>.655f)
            throw new InvalidOperationException($"Female rear waist returns too steeply from the upper seat: {lumbarSlope:F3} m/m");
        GD.Print($"FEMALE_BACK_YOKE_PASS: actual exterior depth, 3 cuts and 3 rear meridians, maximum collar-relative projection={backYoke*1000:F2} mm");
        GD.Print($"FEMALE_LUMBAR_PROFILE_PASS: 3 cuts and 3 rear meridians, steepest waist transition={lumbarSlope:F3} m/m");
        GD.Print($"LUMBAR_CLOTH_PASS: 2 frames/3 cuts, 3 actions, {probes} actual posed trouser-clearance rays; pelvic/chest excursion={excursion:F3} m, rigid mounts and pause");
        GD.Print($"RELAXED_STANCE_PASS: 2 frames/480 steps, planted level soles after 0.35s entry, entry drift={entryDrift:F4} m, fixed thigh lengths, sway={sway:F3} m, knee={knee:F3} rad and pause");
    }

    private static float MeasureLumbarSlope(Vector3[] points,int[] indices)
    {
        const float step=.010f;
        float maximum=0;
        foreach(float x in new[]{-.035f,0,.035f})
        {
            float previous=GarmentDepth(points,indices,x,.92f,true);
            for(int row=1;row<=10;row++)
            {
                float current=GarmentDepth(points,indices,x,.92f+step*row,true);
                float slope=(previous-current)/step;
                if(!float.IsFinite(slope))
                    throw new InvalidOperationException("Female lumbar meridian loses its continuous surface");
                maximum=MathF.Max(maximum,slope);
                previous=current;
            }
        }
        return maximum;
    }

    private void ValidateFemaleHemCut(Vector3[] points)
    {
        var (left,right,front,back)=HemExtents(points);
        float sideRise=MathF.Min(left,right)-MathF.Max(front,back);
        float rearDrop=front-back;
        bool correct=_garmentCut switch
        {
            0=>sideRise>=.004f&&sideRise<=.014f&&MathF.Abs(rearDrop)<.005f,
            1=>sideRise>=.020f&&sideRise<=.036f,
            _=>rearDrop>=.033f&&rearDrop<=.047f
        };
        if(!correct||MathF.Abs(left-right)>.003f)
            throw new InvalidOperationException($"Female cut {_garmentCut} loses its authored hem profile: side={sideRise}, rear={rearDrop}");
        GD.Print($"FEMALE_HEM_CUT_PASS: cut={_garmentCut}, actual side rise={sideRise*1000:F1} mm, rear drop={rearDrop*1000:F1} mm");
    }

    private static (float Left,float Right,float Front,float Back) HemExtents(Vector3[] points)
    {
        float left=float.PositiveInfinity,right=left,front=left,back=left;
        foreach(var p in points)
        {
            float radius=new Vector2(p.X,p.Z).Length();
            if(radius<.05f)continue;
            if(p.X<-.95f*radius)left=MathF.Min(left,p.Y);
            if(p.X>.95f*radius)right=MathF.Min(right,p.Y);
            if(p.Z<-.97f*radius)front=MathF.Min(front,p.Y);
            if(p.Z>.97f*radius)back=MathF.Min(back,p.Y);
        }
        if(!float.IsFinite(left)||!float.IsFinite(right)||!float.IsFinite(front)||!float.IsFinite(back))
            throw new InvalidOperationException("Missing garment hem sector");
        return(left,right,front,back);
    }

    private static float PosedRearEdgeAt(Vector3[] rest,Vector3[] posed,int[] indices,float x)
    {
        float lowest=float.PositiveInfinity;
        for(int t=0;t<indices.Length;t+=3)
        for(int edge=0;edge<3;edge++)
        {
            int i=indices[t+edge],j=indices[t+(edge+1)%3];
            if(rest[i].Z<=0||rest[j].Z<=0||rest[i].Y>.9f||rest[j].Y>.9f)continue;
            var a=posed[i];var b=posed[j];
            if(x<MathF.Min(a.X,b.X)||x>MathF.Max(a.X,b.X)||a.X==b.X)continue;
            lowest=MathF.Min(lowest,Mathf.Lerp(a.Y,b.Y,(x-a.X)/(b.X-a.X)));
        }
        return lowest;
    }

    private static float MeasureRearCollarProjection(Vector3[] points,int[] indices)
    {
        var opening=NeckOpeningEdges(points,indices);
        float maximum=0;
        foreach(float x in new[]{-.025f,0,.025f})
        {
            float rimHeight=float.NegativeInfinity;
            foreach(var (from,to) in opening)
            {
                var a=points[from];var b=points[to];
                if(a.Z<=0||b.Z<=0||(a.X-x)*(b.X-x)>0||MathF.Abs(b.X-a.X)<1e-8f)continue;
                rimHeight=MathF.Max(rimHeight,Mathf.Lerp(a.Y,b.Y,(x-a.X)/(b.X-a.X)));
            }
            // Read the rear collar from the real rim rather than a fixed Y
            // plane that sat above the lowered standing collar.
            float collar=GarmentDepth(points,indices,x,rimHeight+FemaleNeckFacingDepth-.008f,true);
            if(!float.IsFinite(collar))throw new InvalidOperationException("Female rear collar loses its continuous surface");
            for(int mm=1235;mm<=1285;mm+=5)
            {
                float depth=GarmentDepth(points,indices,x,mm*.001f,true);
                if(!float.IsFinite(depth))throw new InvalidOperationException("Female rear yoke loses its continuous surface");
                maximum=MathF.Max(maximum,depth-collar);
            }
        }
        return maximum;
    }
}
