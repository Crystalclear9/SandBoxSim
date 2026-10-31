using System;
using Godot;

namespace SandBoxSim.Client;

/// <summary>Hand anatomy, two-bone reach and tool-specific grip orientations.</summary>
internal partial class ResidentRig
{
    private float _bareBlend,_pickupBlend,_bareTime,_cargoBlend,_supportBlend,_cargoLift;
    internal Vector3 CargoSupportTarget(int side)=>BodyPoint(new(side==0?-.12f:.12f,.90f+_cargoLift,-.29f));
    private SandBoxSim.Core.Agents.ActionKind _bareAction;
    private readonly Transform3D[] _previousHands=new Transform3D[2];
    private readonly Vector3[] _previousElbows=new Vector3[2];
    private float _poseStep;
    private readonly Transform3D[] _priorArms=new Transform3D[2],_priorForearms=new Transform3D[2],_priorWrists=new Transform3D[2];
    private bool _hasArmReturn;
    private readonly bool[] _armSolvedThisStep=new bool[2],_armReturning=new bool[2];
    private Transform3D _toolReachRest;
    private readonly Vector3[] _bareVelocity=new Vector3[2],_bareAngularVelocity=new Vector3[2];
    private void BlendBareHand(int side,Vector3 target,Basis desired,float blend=-1,Vector3? preferredElbow=null)
    {
        float weight=ReachEase(blend<0?_bareBlend:blend);
        var rest=Arms[side].Transform*Elbows[side].Transform*Hands[side].Transform;
        var offset=side==1?Grip!.Position:new Vector3(0,-.035f,-.0286f);
        // Plan the palm path before solving the joints. Interpolating three
        // independent joint rotations makes the hand swing around the target.
        var orientation=rest.Basis.GetRotationQuaternion().Slerp(desired.GetRotationQuaternion(),weight);
        var previous=_previousHands[side];var priorRotation=previous.Basis.GetRotationQuaternion();
        orientation=DampRotation(priorRotation,ref _bareAngularVelocity[side],orientation,.040f,_poseStep);
        float angle=2*MathF.Acos(Math.Clamp(MathF.Abs(priorRotation.Dot(orientation)),0,1));
        if(angle>6*_poseStep&&angle>0)orientation=priorRotation.Slerp(orientation,6*_poseStep/angle);
        var goal=(rest*offset).Lerp(target,weight);var origin=previous*offset;
        var palm=origin;
        DampMotion(ref palm,ref _bareVelocity[side],goal,.035f,_poseStep);
        // A smooth speed knee preserves the incoming tangent instead of
        // snapping to a constant-speed segment at a hard clipping threshold.
        // Limit the palm before IK, so the exact elbow can follow continuously.
        if(_poseStep>0)
        {
            var step=palm-origin;float speed=step.Length()/_poseStep;
            if(speed>.65f)
            {
                float limited=.65f+.25f*(1-MathF.Exp(-(speed-.65f)/.25f));
                float scale=limited/speed;palm=origin+step*scale;_bareVelocity[side]*=scale;
            }
        }
        SolveBareHand(side,palm,new Basis(orientation),preferredElbow);
    }
    private readonly Vector3[,] _returnAngularVelocity=new Vector3[2,3];
    private readonly Vector3[] _returnShoulderVelocity=new Vector3[2];
    private static Vector3 RotationVelocity(Basis previous,Basis current,float dt)
    {
        if(dt<=0)return Vector3.Zero;
        var q=(current.GetRotationQuaternion()*previous.GetRotationQuaternion().Inverse()).Normalized();
        if(q.W<0)q=-q;
        return q.GetAngle()<.00001f?Vector3.Zero:q.GetAxis()*MathF.Min(8,q.GetAngle()/dt);
    }
    private Quaternion ReturnArmRotation(Quaternion prior,ref Vector3 velocity,Quaternion target)
    {
        var result=DampRotation(prior,ref velocity,target,.055f,_poseStep);
        float angle=2*MathF.Acos(Math.Clamp(MathF.Abs(prior.Dot(result)),0,1));
        // Carry forward the displayed angular velocity after a large gesture.
        // A spring's inherited velocity must not turn the returning arm faster
        // than its active reach simply because that arm is no longer in IK.
        float maximum=4.5f*_poseStep;
        if(angle>maximum&&angle>0)
        {
            result=prior.Slerp(result,maximum/angle);
            velocity=RotationVelocity(new Basis(prior),new Basis(result),_poseStep);
        }
        return result;
    }
    private void FinishArmReturns()
    {
        _hasArmReturn=false;
        for(int side=0;side<2;side++)
        {
            if(_armSolvedThisStep[side])
            {
                _returnAngularVelocity[side,0]=RotationVelocity(_priorArms[side].Basis,Arms[side].Basis,_poseStep);
                _returnAngularVelocity[side,1]=RotationVelocity(_priorForearms[side].Basis,Elbows[side].Basis,_poseStep);
                _returnAngularVelocity[side,2]=RotationVelocity(_priorWrists[side].Basis,Hands[side].Basis,_poseStep);
                _returnShoulderVelocity[side]=_poseStep>0?(Arms[side].Position-_priorArms[side].Origin)/_poseStep:Vector3.Zero;
                continue;
            }
            if(!_armReturning[side])continue;
            var armTarget=Arms[side].Transform;var elbowTarget=Elbows[side].Transform;var wristTarget=Hands[side].Transform;
            var position=_priorArms[side].Origin;
            DampMotion(ref position,ref _returnShoulderVelocity[side],armTarget.Origin,.055f,_poseStep);
            Arms[side].Position=position;
            Arms[side].Quaternion=ReturnArmRotation(_priorArms[side].Basis.GetRotationQuaternion(),ref _returnAngularVelocity[side,0],armTarget.Basis.GetRotationQuaternion());
            var returned=ReturnArmRotation(_priorForearms[side].Basis.GetRotationQuaternion(),ref _returnAngularVelocity[side,1],elbowTarget.Basis.GetRotationQuaternion());
            Elbows[side].Basis=HingeForearmBasis(new Basis(returned),Hands[side].Position.Normalized());
            _returnAngularVelocity[side,1]=RotationVelocity(_priorForearms[side].Basis,Elbows[side].Basis,_poseStep);
            Hands[side].Quaternion=ReturnArmRotation(_priorWrists[side].Basis.GetRotationQuaternion(),ref _returnAngularVelocity[side,2],wristTarget.Basis.GetRotationQuaternion());
            float remaining=MathF.Max(1-MathF.Abs(Arms[side].Quaternion.Dot(armTarget.Basis.GetRotationQuaternion())),
                MathF.Max(1-MathF.Abs(Elbows[side].Quaternion.Dot(elbowTarget.Basis.GetRotationQuaternion())),1-MathF.Abs(Hands[side].Quaternion.Dot(wristTarget.Basis.GetRotationQuaternion()))));
            _armReturning[side]=remaining>.000001f||position.DistanceTo(armTarget.Origin)>.0001f;
            if(!_armReturning[side])
            {Arms[side].Transform=armTarget;Elbows[side].Transform=elbowTarget;Hands[side].Transform=wristTarget;_bareVelocity[side]=_bareAngularVelocity[side]=Vector3.Zero;}
            _hasArmReturn|=_armReturning[side];
        }
    }
    // Canonical hand is left: mirrored geometry, bind roots and articulation
    // must use the same convention at every detail level.
    internal static float HandMirror(int side)=>side switch {0=>1,1=>-1,_=>throw new ArgumentOutOfRangeException(nameof(side))};
    internal static Vector3 FingerRoot(int side,int digit)=>new(HandMirror(side)*(-.022f+digit*.014f),-.04f,-.005f);
    internal static Vector3 ThumbRoot(int side)=>new(HandMirror(side)*-.034f,-.021f,.001f);
    internal static Vector3 FingerBends(int digit,float curl)=>curl*(digit switch
    {0=>new Vector3(.891f,.837f,.857f),1=>new(.880f,1.055f,.694f),2=>new(.885f,.950f,.773f),_=>new(.905f,.651f,.995f)});
    private static Vector3 IndexTipOffset(int side,float curl)
    {
        var bends=FingerBends(0,curl);
        var root=Basis.FromEuler(new(bends.X,0,HandMirror(side)*-1.5f*.025f*(1-curl)));
        return FingerRoot(side,0)+root*(new Vector3(0,-.026f*.92f,0)+new Basis(Vector3.Right,bends.Y)*
            (new Vector3(0,-.018f*.92f,0)+new Basis(Vector3.Right,bends.Z)*new Vector3(0,-.014f*.92f,0)));
    }
    internal void PoseHandReference(float curl)
    {
        for(int side=0;side<2;side++)
        {
            Hands[side].Rotation=Vector3.Zero;CurlHand(side,curl);UpdateHandBones(side);
            HandProxies[side].Mesh=curl>.5f?HandClosed[side]:HandOpen[side];
        }
    }
    private static (Vector3,Basis) CarryPose(ResidentTool kind,float stride)
    {
        // The shaft crosses the closed palm's radial axis (the grip has a
        // quarter-turn bind). Keep that bind when lowering the arm; treating
        // the tool axis as the hand's long axis folds the wrist by 90 degrees.
        float yaw=kind==ResidentTool.Hammer?.25f:1.35f;
        var basis=new Basis(Vector3.Up,yaw)*Basis.FromEuler(new Vector3(.08f+stride*.025f,0,1.42f));
        return (new(.27f,.56f,-.12f+stride*.015f),basis);
    }
    private (Vector3,Basis) WorkPose(ResidentTool kind,float phase)
    {
        float lift=WorkLift(phase);
        if(kind==ResidentTool.Hoe)return (new(.10f,.84f+lift*.16f,-.27f+lift*.06f),Basis.FromEuler(new Vector3(.25f+lift*.55f,0,.50f)));
        if(kind==ResidentTool.Spear)return (new(.10f,.99f,-.24f-lift*.12f),Basis.FromEuler(new Vector3(-1.1f,0,.45f)));
        float prepare=.48f+.055f*RelativeWorkLoad;
        float arc=phase<prepare?MathF.Sin(MathF.PI*ReachEase(phase/prepare)):0;
        // Tool X becomes the hand's long axis through the quarter-turn grip.
        // Put that axis in the forward stroke plane, rather than demanding a
        // sideways forearm and forcing the elbow out to accommodate the shaft.
        var rotation=kind==ResidentTool.Hammer
            ?new Basis(Vector3.Right,.15f+lift*.90f)*new Basis(Vector3.Up,-1.35f)*new Basis(Vector3.Right,MathF.PI)
            :Basis.FromEuler(new Vector3(.55f+lift*1.05f,arc*.08f,.45f));
        // The hammer head points back toward the character at the apex.
        // Keep its hand path in front of the chest instead of drawing the
        // head inward toward the cheek while lifting it.
        float depth=-.30f+lift*(kind==ResidentTool.Hammer?-.10f:.10f)+arc*.028f;
        // A hammer preparation raises the whole arm into a working arc.
        // Keeping the wrist below shoulder height throughout instead made
        // this a forearm-only motion, with the head passing by the cheek.
        float height=.92f+lift*(kind==ResidentTool.Hammer?.46f:.26f);
        return (new((kind==ResidentTool.Hammer?.26f:.10f)+arc*.025f,height,depth),rotation);
    }
    // Zero velocity and acceleration at both ends of an intentional reach.
    internal static float ReachEase(float t){t=Math.Clamp(t,0,1);return t*t*t*(10+t*(-15+6*t));}
    private static float ReachPulse(float t)=>t<.08f?0:t<.38f?ReachEase((t-.08f)/.30f):t<.58f?1:t<.92f?1-ReachEase((t-.58f)/.34f):0;
    private void CurlHand(int side,float curl)=>_handCurl[side]=curl;
    private void UpdateHandBones(int side)
    {
        float curl=_handCurl[side];
        if(_skinnedCurl[side]!=curl)
        {
            _skinnedCurl[side]=curl;
            for(int digit=0;digit<4;digit++)
            {
                var bends=FingerBends(digit,curl);
                Fingers[side,digit].Rotation=new(bends.X,0,HandMirror(side)*(digit-1.5f)*.025f*(1-curl));
                FingerMiddles[side,digit].Rotation=new(bends.Y,0,0);
                Fingertips[side,digit].Rotation=new(bends.Z,0,0);
                HandSkeletons[side].SetBonePoseRotation(1+digit*3,Fingers[side,digit].Basis.GetRotationQuaternion());
                HandSkeletons[side].SetBonePoseRotation(2+digit*3,FingerMiddles[side,digit].Basis.GetRotationQuaternion());
                HandSkeletons[side].SetBonePoseRotation(3+digit*3,Fingertips[side,digit].Basis.GetRotationQuaternion());
            }
            Thumbs[side].Rotation=new(curl*.25f,0,HandMirror(side)*(.15f+curl*.65f));ThumbTips[side].Rotation=new(curl*.55f,0,0);
            HandSkeletons[side].SetBonePoseRotation(13,Thumbs[side].Basis.GetRotationQuaternion());
            HandSkeletons[side].SetBonePoseRotation(14,ThumbTips[side].Basis.GetRotationQuaternion());
        }
        UpdateForearmSkin(side);
    }
    private void SolveBareHand(int side,Vector3 target,Basis desired,Vector3? preferredElbow=null)
    {
        // Solve the reach once. Feeding a clamped wrist back into the palm
        // offset moves the target and can select another elbow plane mid-frame.
        // Forearm roll is resolved by the limb frame; only the residual wrist
        // rotation is limited here, without moving the shoulder or elbow again.
        SolveHand(side,target,desired,true,0,preferredElbow);
        var relative=Hands[side].Basis.GetRotationQuaternion();
        if(relative.W<0)relative=-relative;
        float angle=relative.GetAngle();
        if(angle>.55f)
        {
            Hands[side].Basis=new Basis(Quaternion.Identity.Slerp(relative,.55f/angle));
        }
    }
    private void SolveHand(int side,Vector3 gripTarget,Basis handBasis,bool bare=false,float carry=0,Vector3? elbowHint=null)
    {
        if(!bare)_bareVelocity[side]=_bareAngularVelocity[side]=Vector3.Zero;
        _armSolvedThisStep[side]=true;_armReturning[side]=true;
        Vector3 gripOffset=side==1?Grip!.Position:new(0,-.035f,-.0286f);
        Vector3 wrist=gripTarget-handBasis*gripOffset;
        // Seed the shoulder from the displayed upper arm, or an explicit
        // elbow hint. Wrist height includes elbow flexion and must not create
        // a fresh shrug before the coupled shoulder/elbow refinement below.
        var upperHint=elbowHint.HasValue?elbowHint.Value-ShoulderRest(side):-_priorArms[side].Basis.Y;
        Vector3 shoulder=ShoulderForReach(side,wrist,upperHint);Arms[side].Position=shoulder;
        float upper=.28f,lower=Hands[side].Position.Length();
        Vector3 delta=wrist-shoulder;float distance=Math.Clamp(delta.Length(),.08f,upper+lower-.002f);var direction=delta.Normalized();
        wrist=shoulder+direction*distance;
        float along=(upper*upper-lower*lower+distance*distance)/(2*distance);
        // Keep the elbow below and slightly outside the shoulder. A tool's
        // shaft orientation must not drag the elbow up into a winged pose.
        // Pronation is resolved by the lower-arm frame, independently of pole.
        // Flexion bends the forearm forward from a hanging neutral arm;
        // the elbow itself lies behind the shoulder-to-wrist ray. Derive
        // that preference from the arm frame, not a constant world pole.
        var neutralPole=direction.Cross(PreferredArmFrame(side,direction).X);
        var supportedPole=new Vector3((side==0?-.35f:.35f)*ReachLateralScale,-.85f,-.10f);
        float dependent=1-Mathf.SmoothStep(Mathf.DegToRad(15),Mathf.DegToRad(60),MathF.Acos(Math.Clamp(direction.Dot(Vector3.Down),-1,1)));
        var relaxedPole=supportedPole.Lerp(neutralPole,dependent);
        var carryingPole=new Vector3((side==0?-.75f:.75f)*ReachLateralScale,-.45f,.15f);
        var pole=elbowHint.HasValue?elbowHint.Value-shoulder:relaxedPole.Lerp(carryingPole,carry);
        var bend=pole-direction*pole.Dot(direction);
        if(bend.LengthSquared()<.0001f){var hint=new Vector3(side==0?-1:1,.2f,.1f);bend=hint-direction*hint.Dot(direction);}
        bend=bend.Normalized();
        float radius=MathF.Sqrt(MathF.Max(0,upper*upper-along*along));
        void FitWristPole(float limit)
        {
            if(bare||radius<=.0001f)return;
            // Start from the downward elbow preference, then use only as much
            // pole rotation as needed for a comfortable wrist. This avoids
            // trading a natural elbow for a folded wrist at a fixed shaft.
            var wristPole=handBasis.Y-direction*handBasis.Y.Dot(direction);
            if(wristPole.LengthSquared()>.00001f)
            {
                wristPole=wristPole.Normalized();
                float turn=bend.SignedAngleTo(wristPole,direction);
                float Alignment(float t)=>(shoulder+direction*along+bend.Rotated(direction,turn*t)*radius-wrist).Normalized().Dot(handBasis.Y);
                if(Alignment(0)<limit&&Alignment(1)>Alignment(0))
                {
                    float low=0,high=1;
                    for(int pass=0;pass<9;pass++){float mid=(low+high)*.5f;if(Alignment(mid)>=limit)high=mid;else low=mid;}
                    bend=bend.Rotated(direction,turn*high);
                }
            }
        }
        FitWristPole(MathF.Cos(Mathf.DegToRad(43)));
        if(_poseStep>0)
        {
            var priorShoulder=_priorArms[side].Origin;
            var previousDirection=(_previousHands[side].Origin-priorShoulder).Normalized();
            var prior=_previousElbows[side]-priorShoulder;
            prior-=previousDirection*prior.Dot(previousDirection);
            if(prior.LengthSquared()<.000001f)prior=previousDirection.Cross(_priorArms[side].Basis.X);
            // Transport the previous bend plane with the moving shoulder-to-
            // wrist ray. Projecting the old elbow onto a new ray can flip the
            // plane near extension even when the hand trajectory is smooth.
            prior=new Quaternion(previousDirection,direction)*prior;
            prior-=direction*prior.Dot(direction);
            if(prior.LengthSquared()>.000001f)
            {
                prior=prior.Normalized();
                // Continue the last displayed elbow plane, not the FK rest
                // pose rebuilt at the start of this frame. Limit swivel while
                // preserving the exact two-bone lengths and wrist target.
                float turn=prior.SignedAngleTo(bend,direction);
                float limit=(bare?2:3)*_poseStep*(1-MathF.Exp(-MathF.Abs(turn)/.30f));
                bend=prior.Rotated(direction,Math.Clamp(turn,-limit,limit));
            }
        }
        Vector3 elbow=shoulder+direction*along+bend*radius;
        float wristAlignment=MathF.Min(.999f,(elbow-wrist).Normalized().Dot(handBasis.Y));
        // Support follows humeral elevation, not wrist height: bending the
        // elbow alone must not shrug the shoulder. Refine the girdle and the
        // same exact-length IK twice, keeping the displayed elbow plane and
        // wrist target. The correction is bounded to the existing girdle.
        for(int pass=0;pass<2;pass++)
        {
            shoulder=ShoulderForReach(side,wrist,elbow-shoulder);
            delta=wrist-shoulder;distance=Math.Clamp(delta.Length(),.08f,upper+lower-.002f);direction=delta.Normalized();
            wrist=shoulder+direction*distance;
            along=(upper*upper-lower*lower+distance*distance)/(2*distance);
            bend-=direction*bend.Dot(direction);
            if(bend.LengthSquared()<.000001f)
            {
                var fallback=MathF.Abs(direction.Y)<.9f?Vector3.Down:Vector3.Right;
                bend=fallback-direction*fallback.Dot(direction);
            }
            bend=bend.Normalized();
            radius=MathF.Sqrt(MathF.Max(0,upper*upper-along*along));
            // Preserve the wrist comfort achieved before moving the girdle,
            // including a transitional elbow plane; do not tighten the wrist
            // by moving the shoulder after its original pole solve.
            FitWristPole(wristAlignment);
            elbow=shoulder+direction*along+bend*radius;
        }
        Arms[side].Position=shoulder;
        Basis LimbFrame(Vector3 direction,Vector3 rightHint)
        {
            var up=-direction;var right=rightHint-up*rightHint.Dot(up);
            if(right.LengthSquared()<.0001f){var hint=MathF.Abs(up.X)<.85f?Vector3.Right:Vector3.Forward;right=hint-up*hint.Dot(up);}
            right=right.Normalized();return new Basis(right,up,right.Cross(up).Normalized());
        }
        var upperBasis=ReachUpperArmFrame(side,elbow-shoulder,wrist-elbow);
        var lowerBasis=LimbFrame((wrist-elbow).Normalized(),handBasis.X)*new Basis(new Quaternion(Hands[side].Position.Normalized(),Vector3.Down));
        if(_poseStep>0)
        {
            // A palm axis projected onto the forearm's transverse plane is
            // ill-conditioned when that axis approaches the forearm axis.
            // Transport the displayed frame instead; constrain pronation,
            // while the wrist still reaches the exact requested hand frame.
            var prior=_priorArms[side].Basis*_priorForearms[side].Basis;
            var lowerDirection=(wrist-elbow).Normalized();
            var transported=new Basis(new Quaternion(prior*Hands[side].Position.Normalized(),lowerDirection))*prior;
            float turn=transported.X.SignedAngleTo(lowerBasis.X,lowerDirection);
            lowerBasis=new Basis(lowerDirection,Math.Clamp(turn,-6*_poseStep,6*_poseStep))*transported;
        }
        Arms[side].Basis=upperBasis;Elbows[side].Basis=upperBasis.Inverse()*lowerBasis;Hands[side].Basis=lowerBasis.Inverse()*handBasis;
        if(!bare&&LimitWorkingWrist(side)&&side==1&&ToolHeld)FeedbackConstrainedTool();
    }
    private bool LimitWorkingWrist(int side)
    {
        var wrist=Hands[side].Quaternion;
        var swing=new Quaternion(Vector3.Up,Hands[side].Basis.Y);
        float angle=swing.GetAngle(),limit=Mathf.DegToRad(47);
        if(angle<=limit)return false;
        // Preserve axial orientation while projecting the local wrist swing
        // into its comfort cone. Do not kick the elbow plane to satisfy a
        // temporarily infeasible requested tool orientation.
        Hands[side].Quaternion=(Quaternion.Identity.Slerp(swing,limit/angle)*swing.Inverse()*wrist).Normalized();
        return true;
    }
    internal const float SleeveRollSampleLimit=1.65f;
    private Basis ReachUpperArmFrame(int side,Vector3 upper,Vector3 lower)
    {
        var direction=upper.Normalized();
        var hinge=direction.Cross(lower.Normalized());
        if(hinge.LengthSquared()>.00000001f)
        {
            // Pole transport controls temporal continuity. Do not lag this
            // frame independently: doing so bends the elbow outside its hinge
            // while the lower-arm frame compensates to preserve the wrist.
            hinge=hinge.Normalized();var up=-direction;
            return new Basis(hinge,up,hinge.Cross(up)).Orthonormalized();
        }
        var transport=new Quaternion(-_priorArms[side].Basis.Y,direction);
        return new Basis(transport)*_priorArms[side].Basis;
    }
    private static Basis HingeForearmBasis(Basis source,Vector3 bind)
    {
        var direction=source*bind;
        float angle=MathF.Atan2(-direction.Z,-direction.Y)-MathF.Atan2(-bind.Z,-bind.Y);
        var bend=new Basis(Vector3.Right,angle);
        var remainder=(bend.Inverse()*source).GetRotationQuaternion();
        var axial=bind*new Vector3(remainder.X,remainder.Y,remainder.Z).Dot(bind);
        var twist=new Quaternion(axial.X,axial.Y,axial.Z,remainder.W);
        if(twist.LengthSquared()<.00000001f)twist=Quaternion.Identity;else twist=twist.Normalized();
        return bend*new Basis(twist);
    }
    // Body-relative reaches adapt to the fitted shoulder span. Physical cargo
    // supports and the head's actual mouth contact remain in their own space.
    private float ReachLateralScale=>MathF.Min(1,ShoulderHalfWidth/.202f);
    private Vector3 ReachBodyPoint(Vector3 point)=>BodyPoint(new(point.X*ReachLateralScale,point.Y,point.Z));
    internal float ShoulderHalfWidth=.202f,ShoulderDrop;
    internal Vector3 ShoulderRest(int side)=>new(side==0?-ShoulderHalfWidth:ShoulderHalfWidth,BodyY(1.18f)-ShoulderDrop,0);
    private Vector3 ShoulderForReach(int side,Vector3 wrist,Vector3? upperDirection=null)
    {
        var rest=ShoulderRest(side);var reach=wrist-rest;
        if(reach.LengthSquared()<.000001f)return rest;
        return ShoulderGirdle(side,upperDirection??reach).Origin;
    }
}
