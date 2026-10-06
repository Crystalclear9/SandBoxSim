using System;
using Godot;

namespace SandBoxSim.Client;

/// <summary>Hand anatomy, two-bone reach and tool-specific grip orientations.</summary>
internal partial class ResidentRig
{
    private static (Vector3,Basis) WorkPose(ResidentTool kind,float time)
    {
        float period=kind==ResidentTool.Hoe?1.55f:kind==ResidentTool.Pick?1.35f:kind==ResidentTool.Axe?1.25f:1.05f;
        float t=(time%period)/period;
        // Slow preparation, fast contact and a short recovery, instead of a continuous pendulum.
        float lift=t<.55f?Smooth(t/.55f):t<.72f?1-Smooth((t-.55f)/.17f):0;
        if(kind==ResidentTool.Hoe)return (new(.10f,.84f+lift*.16f,-.27f+lift*.06f),Basis.FromEuler(new Vector3(.25f+lift*.55f,0,.50f)));
        if(kind==ResidentTool.Spear)return (new(.10f,.99f,-.24f-lift*.12f),Basis.FromEuler(new Vector3(-1.1f,0,.45f)));
        return (new(kind==ResidentTool.Hammer?.20f:.10f,.92f+lift*.26f,-.30f+lift*.10f),Basis.FromEuler(new Vector3(.55f+lift*1.05f,0,.45f)));
    }
    private void CurlHand(int side,float curl)=>_handCurl[side]=curl;
    private void UpdateHandBones(int side)
    {
        float curl=_handCurl[side];
        if(_skinnedCurl[side]!=curl)
        {
            _skinnedCurl[side]=curl;
            for(int digit=0;digit<4;digit++)
            {
                float knuckle=digit==0?.891f:digit==1?.880f:digit==2?.885f:.905f;
                float middle=digit==0?.837f:digit==1?1.055f:digit==2?.950f:.651f;
                float tip=digit==0?.857f:digit==1?.694f:digit==2?.773f:.995f;
                Fingers[side,digit].Rotation=new(curl*knuckle,0,(digit-1.5f)*.025f*(1-curl));
                FingerMiddles[side,digit].Rotation=new(curl*middle,0,0);
                Fingertips[side,digit].Rotation=new(curl*tip,0,0);
                HandSkeletons[side].SetBonePoseRotation(1+digit*3,Fingers[side,digit].Basis.GetRotationQuaternion());
                HandSkeletons[side].SetBonePoseRotation(2+digit*3,FingerMiddles[side,digit].Basis.GetRotationQuaternion());
                HandSkeletons[side].SetBonePoseRotation(3+digit*3,Fingertips[side,digit].Basis.GetRotationQuaternion());
            }
            Thumbs[side].Rotation=new(curl*.25f,0,(side==0?1:-1)*(.15f+curl*.65f));ThumbTips[side].Rotation=new(curl*.55f,0,0);
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
        // Empty hands and carried bundles do not require a prescribed tool shaft angle.
        // Let the wrist follow the forearm rather than rotating it to a world-space identity.
        for(int pass=0;pass<3;pass++)
        {
            SolveHand(side,target,desired);
            var forearm=Arms[side].Basis*Elbows[side].Basis;
            var relative=(forearm.Inverse()*desired).GetRotationQuaternion();
            float angle=relative.GetAngle();
            if(angle<=.55f)break;
            desired=forearm*new Basis(Quaternion.Identity.Slerp(relative,.55f/angle));
        }
    }
    private void SolveHand(int side,Vector3 gripTarget,Basis handBasis)
    {
        Vector3 gripOffset=side==1?Grip!.Position:new(0,-.035f,-.0286f);
        Vector3 wrist=gripTarget-handBasis*gripOffset,shoulder=Arms[side].Position;
        float upper=.28f,lower=Hands[side].Position.Length();
        Vector3 delta=wrist-shoulder;float distance=Math.Clamp(delta.Length(),.08f,upper+lower-.002f);var direction=delta.Normalized();
        wrist=shoulder+direction*distance;
        float along=(upper*upper-lower*lower+distance*distance)/(2*distance);
        // The elbow pole follows the palm, instead of forcing both forearms behind the body.
        var bend=handBasis.Y-direction*handBasis.Y.Dot(direction);
        if(bend.LengthSquared()<.0001f){var hint=new Vector3(side==0?-1:1,.2f,.1f);bend=hint-direction*hint.Dot(direction);}
        bend=bend.Normalized();
        Vector3 elbow=shoulder+direction*along+bend*MathF.Sqrt(MathF.Max(0,upper*upper-along*along));
        Basis LimbFrame(Vector3 direction,Vector3 rightHint)
        {
            var up=-direction;var right=rightHint-up*rightHint.Dot(up);
            if(right.LengthSquared()<.0001f){var hint=MathF.Abs(up.X)<.85f?Vector3.Right:Vector3.Forward;right=hint-up*hint.Dot(up);}
            right=right.Normalized();return new Basis(right,up,right.Cross(up).Normalized());
        }
        var upperBasis=new Basis(new Quaternion(Vector3.Down,(elbow-shoulder).Normalized()));
        var lowerBasis=LimbFrame((wrist-elbow).Normalized(),handBasis.X)*new Basis(new Quaternion(Hands[side].Position.Normalized(),Vector3.Down));
        Arms[side].Basis=upperBasis;Elbows[side].Basis=upperBasis.Inverse()*lowerBasis;Hands[side].Basis=lowerBasis.Inverse()*handBasis;
    }
}
