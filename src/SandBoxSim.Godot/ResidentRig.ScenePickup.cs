using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    private const float ScenePickupDuration=2.25f;
    private const float ScenePickupContactPose=.93f,ScenePickupContactBudget=.95f;
    private Vector3? _sceneContact;
    private float _scenePickupAge,_scenePickupContact,_scenePickupBend,_scenePickupDepth;
    private float _scenePickupWait;
    private bool _scenePickupGrasped,_scenePickupConfirmed,_plannedPickup;
    private bool _scenePickupMissed;
    private Vector3 _scenePickupGripSite;
    private bool _recoveringPickup;
    private float _pickupRecoveryAge;
    private Vector3 _pickupRecoveryStart;
    private MeshInstance3D? _pickupPiece;
    private Mesh? _pickupPieceMesh,_heldPickupMesh;
    private bool _pickupSourceVisible;
    private Transform3D _pickupPieceInHand;
    internal bool ScenePickupGrasped=>_scenePickupGrasped;
    internal bool ScenePickupPieceVisible=>_pickupPiece?.Visible==true;
    internal Transform3D? ScenePickupPiecePose=>ScenePickupPieceVisible?RigWorldSpace*_pickupPiece!.Transform:null;
    internal bool RecoveringPickup=>_recoveringPickup;
    private bool ScenePickupFinished=>_scenePickupMissed||_scenePickupAge>=ScenePickupDuration;
    private Vector3 ScenePickupSite=>_scenePickupGrasped?_scenePickupGripSite:_sceneContact!.Value;
    private string ScenePickupHandling=>!_sceneContact.HasValue?"弯身取物":_scenePickupGrasped?"握稳 / 抬起"
        :_scenePickupAge>=.80f?(_scenePickupConfirmed?"对准物件 / 等待接触":"手已就位 / 等待取用"):"伸手接近物件";

    internal void SetPickupPiece(Mesh? mesh,bool sourceVisible)
    {
        _pickupPieceMesh=mesh;
        _pickupSourceVisible=sourceVisible;
    }

    private void ResetScenePickup(bool discard=false)
    {
        if(discard){_recoveringPickup=false;_plannedPickup=false;}
        else if(!_recoveringPickup&&_scenePickupGrasped&&ScenePickupPieceVisible)
        {
            _recoveringPickup=true;
            _pickupRecoveryAge=0;
            _pickupRecoveryStart=RigWorldSpace.AffineInverse()*(HandWorldPose(1)*Grip!.Position);
        }
        _scenePickupAge=_scenePickupContact=_scenePickupWait=0;
        _scenePickupGrasped=_scenePickupMissed=false;
        _heldPickupMesh=null;
        if(!_recoveringPickup&&_pickupPiece!=null)_pickupPiece.Visible=false;
    }

    private void AdvanceScenePickup(float delta,bool active,bool reaching)
    {
        if(active&&reaching)
        {
            // Hold the reach pose until the solved palm has actually arrived.
            // Use a separate timeout so lift timing cannot consume this wait.
            if(_scenePickupGrasped)_scenePickupAge+=delta;
            else
            {
                _scenePickupAge=Mathf.Min(_scenePickupAge+delta,ScenePickupContactPose);
                if(_scenePickupConfirmed&&_scenePickupAge>=ScenePickupContactPose)
                {
                    _scenePickupWait+=delta;
                    _scenePickupMissed=_scenePickupWait>=ScenePickupContactBudget;
                }
            }
            _scenePickupBend=ReachEase(_scenePickupAge/.76f)*(1-ReachEase((_scenePickupAge-1.18f)/.96f));
            float height=(RigWorldSpace.AffineInverse()*ScenePickupSite).Y;
            _scenePickupDepth=Mathf.Clamp((.69f-height)/.60f,0,1);
        }
        else
        {
            _scenePickupContact=0;
            _scenePickupBend=Mathf.MoveToward(_scenePickupBend,0,delta/.42f);
        }
    }

    private void PoseScenePickupHand(float delta,bool reaching)
    {
        var site=ScenePickupSite;
        float lift=_scenePickupGrasped?ReachEase((_scenePickupAge-1.16f)/.88f):0;
        var actor=RigWorldSpace;
        var chest=actor*Torso.Transform;
        // Approach outside the lead knee, then close the last few centimetres
        // at the object as the body settles over the planted feet.
        float clearance=1-ReachEase((_scenePickupAge-.38f)/.43f);
        var approach=site+actor.Basis*new Vector3(.18f,.15f,-.07f)*clearance;
        var lifted=approach.Lerp(actor*new Vector3(.19f,.86f,-.20f),lift);
        var reachPoint=chest.AffineInverse()*lifted;
        float reach=ReachEase(_scenePickupAge/.58f)*(1-ReachEase((_scenePickupAge-1.81f)/.49f))*ReachEase(_bareBlend);
        var palm=Torso.Basis.Inverse()*new Basis(Vector3.Right,.55f*(1-lift));
        BlendBareHand(1,PalmMotion.Pickup,reachPoint,palm,reach,eased:true);
        if(!reaching||!_scenePickupConfirmed||_scenePickupGrasped||_scenePickupMissed||_scenePickupAge<.62f)return;
        bool touching=(HandWorldPose(1)*Grip!.Position).DistanceSquaredTo(site)<.055f*.055f;
        _scenePickupContact=touching?_scenePickupContact+delta:0;
        _scenePickupGrasped=_scenePickupContact>=.045f;
        if(_scenePickupGrasped)
        {
            _scenePickupGripSite=site;
            _heldPickupMesh=_pickupPieceMesh;
            _pickupPieceInHand=HandWorldPose(1).AffineInverse()*new Transform3D(Basis.Identity,site);
        }
    }

    private float ScenePickupCurl=>_scenePickupGrasped?.72f
        :Mathf.Lerp(.12f,.37f,Mathf.Clamp(_scenePickupContact/.045f,0,1));

    private void PosePickupRecovery(float delta)
    {
        if(!_recoveringPickup)return;
        _pickupRecoveryAge+=delta;
        var pocket=ReachBodyPoint(new Vector3(.22f,.84f,-.23f));
        var actor=RigWorldSpace;
        var chest=actor*Torso.Transform;
        var start=chest.AffineInverse()*(actor*_pickupRecoveryStart);
        var target=start.Lerp(pocket,ReachEase(_pickupRecoveryAge/.55f));
        BlendBareHand(1,PalmMotion.PickupRecovery,target,Basis.FromEuler(new(.67f,0,.13f)),1,eased:true);
        float gap=(HandWorldPose(1)*Grip!.Position).DistanceTo(chest*pocket);
        CurlHand(1,.72f);
        if(_pickupRecoveryAge>=.48f&&gap<.052f||_pickupRecoveryAge>=.96f)
        {
            _recoveringPickup=false;
            if(_pickupPiece!=null)_pickupPiece.Visible=false;
        }
    }

    private void PresentPickupPiece(bool pickup)
    {
        if(_recoveringPickup)
        {
            _pickupPiece!.Transform=RigWorldSpace.AffineInverse()*(HandWorldPose(1)*_pickupPieceInHand);
            return;
        }
        var mesh=_scenePickupGrasped?_heldPickupMesh:_pickupPieceMesh;
        if(!pickup||mesh==null||!_sceneContact.HasValue||!CompletionVisible||_scenePickupMissed
            ||_scenePickupAge>=2.04f||_pickupSourceVisible&&!_scenePickupGrasped)
        {
            if(_pickupPiece!=null)_pickupPiece.Visible=false;
            return;
        }
        if(_pickupPiece==null)
        {
            _pickupPiece=new MeshInstance3D {Name="PickedUpResource"};
            AddChild(_pickupPiece);
        }
        if(_pickupPiece.Mesh!=mesh)_pickupPiece.Mesh=mesh;
        var pose=_scenePickupGrasped?HandWorldPose(1)*_pickupPieceInHand:new Transform3D(Basis.Identity,_sceneContact.Value);
        _pickupPiece.Transform=RigWorldSpace.AffineInverse()*pose;
        _pickupPiece.Visible=true;
    }
}
