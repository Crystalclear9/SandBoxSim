using System;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    internal static readonly Vector3 HoeBladeMount=new(0,-.795f,-.045f);
    internal static readonly Vector3 HoeCuttingEdge=HoeBladeMount+new Basis(Vector3.Right,.25f)*new Vector3(0,.012f,.05f);
    internal const float FurrowStart=-.75f,FurrowEnd=-.47f;
    private float _furrowFarHeight,_furrowNearHeight;
    private bool _hasFurrowSurface;

    internal void SetFurrowSurface(float farHeight,float nearHeight)
    {
        _furrowFarHeight=Math.Clamp(farHeight,-.15f,.17f);
        _furrowNearHeight=Math.Clamp(nearHeight,-.15f,.17f);
        _hasFurrowSurface=true;
    }

    private float FurrowHeight(float depth)=>Mathf.Lerp(_furrowFarHeight,_furrowNearHeight,
        Math.Clamp((depth-FurrowStart)/(FurrowEnd-FurrowStart),0,1));

    private bool HoeTouchesFurrow()
    {
        var edge=RigWorldSpace.AffineInverse()*(HandWorldPose(1)*Grip!.Transform*HoeCuttingEdge);
        return edge.Z is >=FurrowStart-.08f and <=FurrowEnd+.08f
            &&MathF.Abs(edge.Y-FurrowHeight(edge.Z))<.040f;
    }

    private (Vector3, Basis) CultivationGrip(float phase)
    {
        // The blade advances to the far end, pulls along the furrow, then
        // clears the soil before returning. Hand targets derive from the blade.
        float entry=ReachEase(phase/.29f);
        float pull=ReachEase((phase-.29f)/.25f);
        float lift=ReachEase((phase-.54f)/.24f);
        float recover=ReachEase((phase-.78f)/.22f);
        float depth=-.60f+(FurrowStart+.60f)*entry+(FurrowEnd-FurrowStart)*pull-.042f*lift-.088f*recover;
        float height=.17f*(1-entry)+.27f*lift-.10f*recover;
        var blade=new Vector3(-.045f+.045f*pull-.045f*recover,FurrowHeight(depth)+height+.008f,depth);
        var shaft=Basis.FromEuler(new(.36f+.12f*lift-.12f*recover,.02f-.08f*pull+.08f*recover,-.22f+.12f*pull-.12f*recover));
        var inverse=Torso.Transform.AffineInverse();
        return (inverse*(blade-shaft*HoeCuttingEdge),inverse.Basis*shaft);
    }

    private Vector3 GuideCultivationEdge(Vector3 grip,Basis orientation,float phase,float weight)
    {
        // Reconcile the tip with the sampled surface using the *filtered*
        // shaft orientation. Filtering the grip and shaft independently would
        // otherwise move the cutting edge up or down through the surface.
        float contact=ReachEase((phase-.18f)/.11f)*(1-ReachEase((phase-.54f)/.12f))*weight;
        if(contact<=0)return grip;
        var edge=Torso.Transform*(grip+orientation*HoeCuttingEdge);
        float correction=Math.Clamp(FurrowHeight(edge.Z)+.008f-edge.Y,-.12f,.12f)*contact;
        return grip+Torso.Basis.Inverse()*Vector3.Up*correction;
    }

    private Basis AlignHoeSupport(Basis palm,Vector3 shaft)
    {
        // The upper hand can turn around the round shaft while keeping its
        // finger axis across it. Aim its long axis toward the actual forearm.
        var forearm=(_previousElbows[0]-_previousHands[0].Origin).Slide(shaft);
        if(forearm.LengthSquared()<.001f)return palm;
        float turn=palm.Y.SignedAngleTo(forearm.Normalized(),shaft);
        return new Basis(shaft,Math.Clamp(turn,-.52f,.52f))*palm;
    }
}
