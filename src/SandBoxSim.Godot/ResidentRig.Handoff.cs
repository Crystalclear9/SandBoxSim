using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    private int _handoffHand=-1;
    private Vector3? _handoffPalm;
    private float _handoffCurl;
    private bool _handoffRecovery;
    private string _handoffReadout="";
    internal bool HandoffReserved=>_handoffHand>=0;
    internal bool HandoffHandReady {get;private set;}
    internal uint? LastSharePresentation;

    internal Transform3D RigWorldSpace=>IsInsideTree()?GlobalTransform:Transform;
    internal Transform3D HandWorldPose(int side)
        =>RigWorldSpace*Torso.Transform*Arms[side].Transform*Elbows[side].Transform*Hands[side].Transform;
    internal Vector3 HandoffWaist(int side)
        =>RigWorldSpace*new Vector3(side==0?-.19f:.19f,.84f,-.235f);

    internal void PlanHandoff(int side,Vector3? palm,float curl,string readout,bool recovery=false)
    {
        _handoffHand=side;_handoffPalm=palm;_handoffCurl=curl;_handoffReadout=readout;_handoffRecovery=recovery;
        HandoffHandReady=false;
    }

    private void PoseHandoff(bool ready)
    {
        HandoffHandReady=ready&&(InteractionSettled||_handoffRecovery)&&_handoffHand>=0&&_handoffPalm.HasValue;
        if(!HandoffHandReady)return;
        var local=(RigWorldSpace*Torso.Transform).AffineInverse()*_handoffPalm!.Value;
        var target=local+CargoPalmBasis*(Grip!.Position-CargoPalmContact);
        BlendBareHand(_handoffHand,target,CargoPalmBasis,1,eased:true);
        CurlHand(_handoffHand,_handoffCurl);
    }
}
