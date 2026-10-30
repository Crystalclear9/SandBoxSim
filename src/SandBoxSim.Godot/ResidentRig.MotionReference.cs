using System;
using System.Text.Json;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed class ResidentMotionReference
{
    internal readonly record struct Sample(Vector3 LeftUpper,Vector3 LeftLower,Vector3 RightUpper,Vector3 RightLower,Quaternion Chest,Quaternion LeftWrist,Quaternion RightWrist);
    private readonly Sample[] _samples;
    internal float Duration=>(_samples.Length-1)/120f;
    internal static readonly Lazy<ResidentMotionReference> Shared=new(()=>new());
    private ResidentMotionReference()
    {
        string raw=FileAccess.GetFileAsString("res://assets/motions/cmu-143-30-stretch.json").Replace("\r\n","\n",StringComparison.Ordinal);
        string digest=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        if(digest!="77064eeab402ec0ce9552c1c9359c4a1d50f237e32dfe6bbcb57be3366a6702e")throw new InvalidOperationException("Changed CMU arm reference");
        using var json=JsonDocument.Parse(raw);var data=json.RootElement;
        if(data.GetProperty("schemaVersion").GetInt32()!=1||data.GetProperty("fps").GetInt32()!=120||data.GetProperty("frameCount").GetInt32()!=370)
            throw new InvalidOperationException("Invalid motion reference contract");
        _samples=new Sample[370];int index=0;
        foreach(var row in data.GetProperty("samples").EnumerateArray())
        {
            if(row.GetArrayLength()!=39||index>=_samples.Length)throw new InvalidOperationException("Invalid motion reference sample");
            Vector3 V(int start)=>new(row[start].GetSingle(),row[start+1].GetSingle(),row[start+2].GetSingle());
            var left=V(0);var leftLower=V(3);var right=V(6);var rightLower=V(9);var basis=new Basis(V(12),V(15),V(18));
            foreach(var vector in new[]{left,leftLower,right,rightLower})
                if(!vector.IsFinite()||MathF.Abs(vector.Length()-1)>.00001f)throw new InvalidOperationException("Invalid captured limb direction");
            if(!basis.IsFinite()||MathF.Abs(basis.Determinant()-1)>.00001f)throw new InvalidOperationException("Invalid captured chest frame");
            var leftWrist=new Basis(V(21),V(24),V(27));var rightWrist=new Basis(V(30),V(33),V(36));
            if(!leftWrist.IsFinite()||!rightWrist.IsFinite()||MathF.Abs(leftWrist.Determinant()-1)>.00001f||MathF.Abs(rightWrist.Determinant()-1)>.00001f)throw new InvalidOperationException("Invalid captured wrist frame");
            _samples[index++]=new(left,leftLower,right,rightLower,basis.Orthonormalized().GetRotationQuaternion(),leftWrist.Orthonormalized().GetRotationQuaternion(),rightWrist.Orthonormalized().GetRotationQuaternion());
        }
        if(index!=_samples.Length)throw new InvalidOperationException("Incomplete motion reference");
    }
    internal Sample At(float seconds)
    {
        float frame=Math.Clamp(seconds*120,0,_samples.Length-1);int index=Math.Min(_samples.Length-2,(int)frame);float weight=frame-index;
        var a=_samples[index];var b=_samples[index+1];
        return new(a.LeftUpper.Slerp(b.LeftUpper,weight),a.LeftLower.Slerp(b.LeftLower,weight),a.RightUpper.Slerp(b.RightUpper,weight),a.RightLower.Slerp(b.RightLower,weight),a.Chest.Slerp(b.Chest,weight),a.LeftWrist.Slerp(b.LeftWrist,weight),a.RightWrist.Slerp(b.RightWrist,weight));
    }
}

internal partial class ResidentRig
{
    internal bool AmbientMotionEnabled=true;
    private bool _referenceStudy,_referencePlaying;
    private float _referenceStudyTime,_referenceTime,_referenceWeight,_idleAge;
    private ResidentMotionReference.Sample _referenceSample;
    private Vector3 _referenceChest;
    private void AdvanceMotionReference(float delta,ActionKind action,ActionPhase phase,float identity)
    {
        bool eligible=AmbientMotionEnabled&&_detailedHands&&!IsChild&&action==ActionKind.None&&phase==ActionPhase.Idle&&!ToolHeld&&!_hasCargo&&_reach==0&&_releaseTime==0&&_activity<.001f&&(!_worldLocomotion||_locomotionSpeed<.01f);
        if(_referenceStudy)
        {
            var clip=ResidentMotionReference.Shared.Value;float cycle=Mathf.PosMod(_referenceStudyTime,clip.Duration+2);
            _referenceTime=MathF.Min(cycle,clip.Duration);_referenceWeight=cycle<=clip.Duration?1:0;
            eligible=true;
        }
        else if(eligible)
        {
            if(!_referencePlaying)
            {
                _idleAge+=delta;
                if(_idleAge>=35+Mathf.PosMod(identity*17.17f,22)){_referencePlaying=true;_referenceTime=0;_idleAge=0;}
            }
            else
            {
                _referenceTime+=delta;
                if(_referenceTime>=ResidentMotionReference.Shared.Value.Duration){_referencePlaying=false;_idleAge=0;}
            }
            _referenceWeight=Mathf.MoveToward(_referenceWeight,_referencePlaying?1:0,delta/.25f);
        }
        else{_referencePlaying=false;_idleAge=0;_referenceWeight=0;}
        _referenceChest=Vector3.Zero;
        if(!eligible||_referenceWeight<=0)return;
        var reference=ResidentMotionReference.Shared.Value;
        _referenceSample=reference.At(_referenceTime);
        // Retarget the measured torso motion conservatively; the character's
        // pelvis, legs and root stay under the existing grounded controller.
        var angles=new Basis(_referenceSample.Chest).GetEuler();
        _referenceChest=new Vector3(Math.Clamp(angles.X,-.13f,.13f),Math.Clamp(angles.Y,-.13f,.13f),Math.Clamp(angles.Z,-.10f,.10f))*_referenceWeight;
    }
    private void ApplyMotionReference()
    {
        if(_referenceWeight<=0||IsChild)return;
        for(int side=0;side<2;side++)
        {
            var upper=side==0?_referenceSample.LeftUpper:_referenceSample.RightUpper;
            var lower=side==0?_referenceSample.LeftLower:_referenceSample.RightLower;
            var baseUpper=-Arms[side].Basis.Y;var baseLower=(Arms[side].Basis*Elbows[side].Basis*Hands[side].Position).Normalized();
            float blend=ReachEase(_referenceWeight);upper=baseUpper.Slerp(upper,blend);lower=baseLower.Slerp(lower,blend);
            var restHand=Arms[side].Basis*Elbows[side].Basis*Hands[side].Basis;
            var wristRotation=restHand.GetRotationQuaternion().Slerp(side==0?_referenceSample.LeftWrist:_referenceSample.RightWrist,blend);
            var right=new Basis(wristRotation).X;
            right=(right-lower*right.Dot(lower)).Normalized();var up=-lower;
            // Keep captured wrist pronation. CMU fingers/thumbs are not
            // captured; their relaxed articulation remains authored here.
            var palm=new Basis(right,up,right.Cross(up));
            // Unconstrained captured motion uses its measured limb directions
            // directly. Feeding it through a target/pole speed limiter would
            // flatten the reach and replace the captured timing with IK lag.
            var arm=ReachUpperArmFrame(side,upper,lower);var lowerBasis=palm*new Basis(new Quaternion(Hands[side].Position.Normalized(),Vector3.Down));
            Arms[side].Basis=arm;Arms[side].Position=ShoulderGirdle(side,upper).Origin;
            Elbows[side].Basis=arm.Inverse()*lowerBasis;Hands[side].Basis=lowerBasis.Inverse()*palm;
            _armSolvedThisStep[side]=true;_armReturning[side]=true;
            var offset=side==1?Grip!.Position:new Vector3(0,-.035f,-.0286f);
            var hand=Arms[side].Transform*Elbows[side].Transform*Hands[side].Transform;
            if(_poseStep>0)_bareVelocity[side]=(hand*offset-_previousHands[side]*offset)/_poseStep;
            _bareAngularVelocity[side]=RotationVelocity(_previousHands[side].Basis,hand.Basis,_poseStep);CurlHand(side,.22f);
        }
    }
    internal void StudyCapturedStretch(float delta,float seconds)
    {
        _referenceStudy=true;_referenceStudyTime=seconds;
        try{PoseAction(delta,ActionKind.None,ActionPhase.Idle,false,0);}
        finally{_referenceStudy=false;}
    }
}
