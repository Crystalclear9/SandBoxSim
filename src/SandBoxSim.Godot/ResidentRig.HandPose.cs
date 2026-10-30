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
    private void BlendBareHand(int side,Vector3 target,Basis desired,float blend=-1)
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
        // Preserve incoming velocity at target changes. Keep the existing
        // metre/second safety bound, including large target resynchronisations.
        if(palm.DistanceTo(origin)>_poseStep&&_poseStep>0)
        {palm=origin.MoveToward(palm,_poseStep);_bareVelocity[side]=(palm-origin)/_poseStep;}
        SolveBareHand(side,palm,new Basis(orientation));
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
            Arms[side].Quaternion=DampRotation(_priorArms[side].Basis.GetRotationQuaternion(),ref _returnAngularVelocity[side,0],armTarget.Basis.GetRotationQuaternion(),.055f,_poseStep);
            Elbows[side].Quaternion=DampRotation(_priorForearms[side].Basis.GetRotationQuaternion(),ref _returnAngularVelocity[side,1],elbowTarget.Basis.GetRotationQuaternion(),.055f,_poseStep);
            Hands[side].Quaternion=DampRotation(_priorWrists[side].Basis.GetRotationQuaternion(),ref _returnAngularVelocity[side,2],wristTarget.Basis.GetRotationQuaternion(),.055f,_poseStep);
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
        float depth=-.30f+lift*(kind==ResidentTool.Hammer?-.06f:.10f)+arc*.028f;
        return (new((kind==ResidentTool.Hammer?.20f:.10f)+arc*.025f,.92f+lift*.26f,depth),rotation);
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
        var wrist=Hands[side].Basis.Inverse().GetRotationQuaternion();
        if(MathF.Abs(wrist.Dot(_wristSkinRotation[side]))<.999999f)
        {
            _wristSkinRotation[side]=wrist;HandSkeletons[side].SetBonePoseRotation(15,wrist);
        }
    }
    private void SolveBareHand(int side,Vector3 target,Basis desired)
    {
        var elbowHint=_previousElbows[side];
        // Solve the reach once. Feeding a clamped wrist back into the palm
        // offset moves the target and can select another elbow plane mid-frame.
        // Forearm roll is resolved by the limb frame; only the residual wrist
        // rotation is limited here, without moving the shoulder or elbow again.
        SolveHand(side,target,desired,true,0,elbowHint);
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
        Vector3 shoulder=ShoulderForReach(side,wrist);Arms[side].Position=shoulder;
        float upper=.28f,lower=Hands[side].Position.Length();
        Vector3 delta=wrist-shoulder;float distance=Math.Clamp(delta.Length(),.08f,upper+lower-.002f);var direction=delta.Normalized();
        wrist=shoulder+direction*distance;
        float along=(upper*upper-lower*lower+distance*distance)/(2*distance);
        // Keep the elbow below and slightly outside the shoulder. A tool's
        // shaft orientation must not drag the elbow up into a winged pose.
        // Pronation is resolved by the lower-arm frame, independently of pole.
        var relaxedPole=new Vector3((side==0?-.35f:.35f)*ReachLateralScale,-.85f,-.10f);
        var carryingPole=new Vector3((side==0?-.75f:.75f)*ReachLateralScale,-.45f,.15f);
        var pole=bare?relaxedPole:relaxedPole.Lerp(handBasis.Y,.12f).Lerp(carryingPole,carry);
        var bend=pole-direction*pole.Dot(direction);
        if(bend.LengthSquared()<.0001f){var hint=new Vector3(side==0?-1:1,.2f,.1f);bend=hint-direction*hint.Dot(direction);}
        bend=bend.Normalized();
        float radius=MathF.Sqrt(MathF.Max(0,upper*upper-along*along));
        if(!bare&&radius>.0001f)
        {
            // Start from the downward elbow preference, then use only as much
            // pole rotation as needed for a comfortable wrist. This avoids
            // trading a natural elbow for a folded wrist at a fixed shaft.
            var wristPole=handBasis.Y-direction*handBasis.Y.Dot(direction);
            if(wristPole.LengthSquared()>.00001f)
            {
                wristPole=wristPole.Normalized();
                float turn=bend.SignedAngleTo(wristPole,direction),limit=MathF.Cos(Mathf.DegToRad(43));
                float Alignment(float t)=>(shoulder+direction*along+bend.Rotated(direction,turn*t)*radius-wrist).Normalized().Dot(handBasis.Y);
                if(Alignment(0)<limit&&Alignment(1)>Alignment(0))
                {
                    float low=0,high=1;
                    for(int pass=0;pass<9;pass++){float mid=(low+high)*.5f;if(Alignment(mid)>=limit)high=mid;else low=mid;}
                    bend=bend.Rotated(direction,turn*high);
                }
            }
        }
        if(_poseStep>0)
        {
            var prior=(elbowHint??_previousElbows[side])-shoulder;
            prior-=direction*prior.Dot(direction);
            if(prior.LengthSquared()>.000001f)
            {
                prior=prior.Normalized();
                // Continue the last displayed elbow plane, not the FK rest
                // pose rebuilt at the start of this frame. Limit swivel while
                // preserving the exact two-bone lengths and wrist target.
                float turn=prior.SignedAngleTo(bend,direction);
                float limit=3*_poseStep*(1-MathF.Exp(-MathF.Abs(turn)/.30f));
                bend=prior.Rotated(direction,Math.Clamp(turn,-limit,limit));
            }
        }
        Vector3 elbow=shoulder+direction*along+bend*radius;
        Basis LimbFrame(Vector3 direction,Vector3 rightHint)
        {
            var up=-direction;var right=rightHint-up*rightHint.Dot(up);
            if(right.LengthSquared()<.0001f){var hint=MathF.Abs(up.X)<.85f?Vector3.Right:Vector3.Forward;right=hint-up*hint.Dot(up);}
            right=right.Normalized();return new Basis(right,up,right.Cross(up).Normalized());
        }
        var upperBasis=ReachUpperArmFrame(side,elbow-shoulder,wrist-elbow);
        var lowerBasis=LimbFrame((wrist-elbow).Normalized(),handBasis.X)*new Basis(new Quaternion(Hands[side].Position.Normalized(),Vector3.Down));
        Arms[side].Basis=upperBasis;Elbows[side].Basis=upperBasis.Inverse()*lowerBasis;Hands[side].Basis=lowerBasis.Inverse()*handBasis;
    }
    private Basis ReachUpperArmFrame(int side,Vector3 upper,Vector3 lower)
    {
        var direction=upper.Normalized();
        var swing=new Basis(new Quaternion(Vector3.Down,direction));
        var hinge=direction.Cross(lower.Normalized());
        float bend=hinge.Length(),wanted=0;
        if(bend>.0001f)
        {
            hinge/=bend;
            // The plane normal is an unsigned axis. Choosing its opposite
            // sign must never turn the humerus and sleeve through half a turn.
            if(hinge.Dot(swing.X)<0)hinge=-hinge;
            wanted=Math.Clamp(swing.X.SignedAngleTo(hinge,direction),-.80f,.80f)
                *Mathf.SmoothStep(.08f,.35f,bend);
        }
        // Transport the displayed frame onto the new upper-arm direction.
        // Near extension the elbow plane is ill-conditioned, so recover the
        // neutral frame gradually instead of following the raw cross product.
        var previous=_priorArms[side].Basis.X;
        previous-=direction*previous.Dot(direction);
        float prior=previous.LengthSquared()>.0001f?swing.X.SignedAngleTo(previous.Normalized(),direction):0;
        float difference=Mathf.Wrap(wanted-prior,-MathF.PI,MathF.PI);
        float limit=2.6f*_poseStep*(1-MathF.Exp(-MathF.Abs(difference)/.20f));
        float twist=prior+Math.Clamp(difference,-limit,limit);
        return new Basis(direction,twist)*swing;
    }
    // Body-relative reaches adapt to the fitted shoulder span. Physical cargo
    // supports and the head's actual mouth contact remain in their own space.
    private float ReachLateralScale=>MathF.Min(1,ShoulderHalfWidth/.202f);
    private Vector3 ReachBodyPoint(Vector3 point)=>BodyPoint(new(point.X*ReachLateralScale,point.Y,point.Z));
    internal float ShoulderHalfWidth=.202f,ShoulderDrop;
    internal Vector3 ShoulderRest(int side)=>new(side==0?-ShoulderHalfWidth:ShoulderHalfWidth,BodyY(1.18f)-ShoulderDrop,0);
    private Vector3 ShoulderForReach(int side,Vector3 wrist)
    {
        var rest=ShoulderRest(side);var reach=wrist-rest;
        if(reach.LengthSquared()<.000001f)return rest;
        float elevation=MathF.Acos(Math.Clamp(reach.Normalized().Dot(Vector3.Down),-1,1));
        // Bounded shoulder-girdle support, not a clinical scapular solver.
        // Below 30 degrees the humerus carries most motion; reaching higher
        // lifts the shoulder slightly, while forward reach protracts it.
        float lift=Mathf.SmoothStep(Mathf.DegToRad(30),Mathf.DegToRad(105),elevation);
        float forward=Mathf.SmoothStep(.10f,.40f,-reach.Z);
        return rest+new Vector3((side==0?-1:1)*.004f*lift,.020f*lift,-.014f*forward);
    }
}
