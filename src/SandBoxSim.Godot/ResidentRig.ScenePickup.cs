using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    private const float ScenePickupDuration=2.25f;
    private Vector3? _sceneContact;
    private float _scenePickupAge,_scenePickupContact,_scenePickupBend,_scenePickupDepth;
    private bool _scenePickupGrasped;
    private MeshInstance3D? _pickupPiece;
    private Mesh? _pickupPieceMesh;
    private bool _pickupSourceVisible;
    private Transform3D _pickupPieceInHand;
    internal bool ScenePickupGrasped=>_scenePickupGrasped;
    internal bool ScenePickupPieceVisible=>_pickupPiece?.Visible==true;
    private string ScenePickupHandling=>!_sceneContact.HasValue?"弯身取物":_scenePickupGrasped?"握稳 / 抬起":"伸手接近物件";

    internal void SetPickupPiece(Mesh? mesh,bool sourceVisible)
    {
        _pickupPieceMesh=mesh;
        _pickupSourceVisible=sourceVisible;
    }

    private void ResetScenePickup()
    {
        _scenePickupAge=_scenePickupContact=0;
        _scenePickupGrasped=false;
        if(_pickupPiece!=null)_pickupPiece.Visible=false;
    }

    private void AdvanceScenePickup(float delta,bool active,bool reaching)
    {
        if(active&&reaching)
        {
            _scenePickupAge+=delta;
            _scenePickupBend=ReachEase(_scenePickupAge/.76f)*(1-ReachEase((_scenePickupAge-1.18f)/.96f));
            float height=(RigWorldSpace.AffineInverse()*_sceneContact!.Value).Y;
            _scenePickupDepth=Mathf.Clamp((.69f-height)/.60f,0,1);
        }
        else _scenePickupBend=Mathf.MoveToward(_scenePickupBend,0,delta/.42f);
    }

    private void PoseScenePickupHand(float delta,bool reaching)
    {
        var site=_sceneContact!.Value;
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
        BlendBareHand(1,reachPoint,palm,reach,eased:true);
        if(!reaching||_scenePickupGrasped||_scenePickupAge<.62f||_scenePickupAge>1.37f)return;
        bool touching=(HandWorldPose(1)*Grip!.Position).DistanceSquaredTo(site)<.055f*.055f;
        _scenePickupContact=touching?_scenePickupContact+delta:0;
        _scenePickupGrasped=_scenePickupContact>=.045f;
        if(_scenePickupGrasped)_pickupPieceInHand=HandWorldPose(1).AffineInverse()*new Transform3D(Basis.Identity,site);
    }

    private float ScenePickupCurl=>_scenePickupGrasped?.72f
        :Mathf.Lerp(.12f,.37f,Mathf.Clamp(_scenePickupContact/.045f,0,1));

    private void PresentPickupPiece(bool pickup)
    {
        if(!pickup||_pickupPieceMesh==null||!_sceneContact.HasValue||!CompletionVisible
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
        if(_pickupPiece.Mesh!=_pickupPieceMesh)_pickupPiece.Mesh=_pickupPieceMesh;
        var pose=_scenePickupGrasped?HandWorldPose(1)*_pickupPieceInHand:new Transform3D(Basis.Identity,_sceneContact.Value);
        _pickupPiece.Transform=RigWorldSpace.AffineInverse()*pose;
        _pickupPiece.Visible=true;
    }
}
