using System;
using Godot;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Systems;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    private enum ActionFamily { Idle, Travel, Rest, Tool, Pickup, Transfer, Consume, Social }

    private static ActionFamily FamilyOf(ActionKind action)=>action switch
    {
        ActionKind.Wander or ActionKind.Explore or ActionKind.Migrate or ActionKind.Flee=>ActionFamily.Travel,
        ActionKind.Sleep=>ActionFamily.Rest,
        ActionKind.Take or ActionKind.GatherFood=>ActionFamily.Pickup,
        ActionKind.Deposit or ActionKind.StoreInBuilding or ActionKind.ShareFood or ActionKind.Trade=>ActionFamily.Transfer,
        ActionKind.Eat or ActionKind.Drink=>ActionFamily.Consume,
        ActionKind.Socialize=>ActionFamily.Social,
        _=>ToolFor(action)==ResidentTool.None?ActionFamily.Idle:ActionFamily.Tool
    };

    private static bool UsesBareHands(ActionFamily family)=>family is ActionFamily.Pickup or ActionFamily.Transfer or ActionFamily.Consume;
    internal static bool CompatibleBareActions(ActionKind a,ActionKind b)
    {
        var family=FamilyOf(a);
        return UsesBareHands(family)&&family==FamilyOf(b);
    }

    private ActionKind _observedAction;
    private ActionPhase _observedPhase;
    private uint? _observedRevision;
    private float _observedExecution,_completionRemaining,_completionAge;
    private bool _finishConsumption,_observedMouthContact;
    internal readonly record struct InteractionTarget(Vector2I? Tile,long Partner,ActionSystem.ActionContact? Pickup,
        ActionSystem.ActionContact? Worksite=null);
    private InteractionTarget _interactionTarget;
    private bool _observedContactReady;
    private float _retargetSettle,_contactWait;
    private bool _awaitingContact;
    private uint? _bridgedRevision;
    private float _bridgeAge;
    public bool CompletionVisible=>_finishConsumption||_finishToolStroke||_completionRemaining>0||_placingCargo||_placementPending||_awaitingContact;

    internal Vector3? PreparedHuntPoint(uint revision,long prey)
        =>_observedAction==ActionKind.Hunt&&_observedRevision==revision&&_interactionTarget.Partner==prey
            ?_toolSurface?.Point:null;

    internal bool ShowsCompletion(ActionKind action,uint revision)
        =>_observedRevision.HasValue&&(revision!=_observedRevision||action!=_observedAction
            ||_observedPhase!=ActionPhase.Done||CompletionVisible);

    internal bool BridgeContact(ActionSystem.ContactCompletion completion,float delta,bool recent)
    {
        if(_observedRevision!=completion.Revision||_observedAction!=completion.Action||HandoffReserved)return false;
        var family=FamilyOf(completion.Action);
        bool placement=IsPlacement(completion.Action);
        if(completion.Site is {} site&&(_interactionTarget.Tile!=new Vector2I(site.Tile.X,site.Tile.Y)
            ||family==ActionFamily.Pickup&&_interactionTarget.Pickup!=site with {StockAmount=0}))return false;
        bool ready=_observedPhase switch
        {
            ActionPhase.Executing=>InteractionSettled&&(family switch
            {
                ActionFamily.Pickup=>_observedContactReady&&_scenePickupAge>=.62f,
                ActionFamily.Transfer=>placement&&SupportedCargo==completion.Site?.Resource,
                ActionFamily.Consume=>_observedContactReady&&(_consumeStage==ConsumeStage.Use
                    ||_consumeStage==ConsumeStage.Lift&&_consumeReach>.45f),
                ActionFamily.Tool=>_observedContactReady&&GestureEngaged(completion.Action),
                _=>false
            }),
            ActionPhase.Done=>CompletionVisible&&!(placement&&ReleasedCargo),
            _=>false
        };
        if(!ready)return false;
        if(_bridgedRevision!=completion.Revision)
        {
            if(!recent)return false;
            _bridgedRevision=completion.Revision;_bridgeAge=0;
        }
        // Finish the nearby physical contact. Longer follow-through belongs
        // to the normal pose transitions into the new action.
        _bridgeAge+=PresentationDelta(delta);
        return _bridgeAge<=(placement?1.60f:.64f);
    }

    internal void ObserveOffscreenAction(ActionKind action,ActionPhase phase,uint revision)
    {
        _poseStep=0;
        ReleasingSupport=false;
        _bridgedRevision=null;
        Array.Clear(_palmMotion);
        _observedAction=action;_observedPhase=phase;_observedRevision=revision;
        _observedExecution=_completionRemaining=0;
        _waitingForContact=false;
        _observedContactReady=false;
        _awaitingContact=false;
        _conversation=null;
        ConversationNod=0;
        _mouthAmount=0;
        _finishConsumption=_observedMouthContact=false;
        ClearConsumption();
        ClearTransientCargo();
        ResetToolPhrase(retainPose:false);
        ObserveToolSurface(null,reset:true);
        _workSurfaceBend=0;
        _interactionTarget=default;_retargetSettle=0;
        _sceneContact=null;
        ResetScenePickup(discard:true);_scenePickupBend=0;
    }

    public void PresentAction(float delta,ActionKind action,ActionPhase phase,bool paused,float identityPhase,uint? revision=null,bool hasPartner=true,bool targetAligned=true,
        Vector3? sceneContact=null,ResourceKind contactResource=ResourceKind.None,InteractionTarget target=default,
        bool completeToolStroke=true,bool awaitToolCommit=false,ToolSurface? toolSurface=null,bool contactReady=true,bool deferContact=false,
        ResourceKind? inventoryCargo=null,ConversationBeat? conversation=null)
    {
        if(paused)return;
        _conversation=conversation;
        bool firstSnapshot=revision.HasValue&&!_observedRevision.HasValue;
        bool newRun=action!=_observedAction||revision!=_observedRevision
            ||phase==ActionPhase.Moving&&_observedPhase!=ActionPhase.Moving
            ||phase==ActionPhase.Executing&&_observedPhase is ActionPhase.Done or ActionPhase.Idle or ActionPhase.Failed;
        bool sameExecution=!newRun&&_observedPhase==ActionPhase.Executing&&phase is ActionPhase.Executing or ActionPhase.Done;
        // The active hunt tracks a live animal. Its committed receipt keeps
        // the last position after that animal leaves the live render set.
        bool changedPartner=(RequiresPartner(action)||action==ActionKind.Hunt)
            &&target.Partner!=_interactionTarget.Partner;
        // A moving animal or person does not become a new target on a tile boundary.
        bool trackingIdentity=action==ActionKind.Hunt?toolSurface.HasValue:target.Partner!=0&&RequiresPartner(action);
        bool changedTile=!trackingIdentity&&target.Tile.HasValue&&_interactionTarget.Tile.HasValue&&target.Tile!=_interactionTarget.Tile;
        bool changedPickup=target.Pickup!=_interactionTarget.Pickup;
        bool changedWorksite=target.Worksite.HasValue&&_interactionTarget.Worksite.HasValue
            &&target.Worksite!=_interactionTarget.Worksite;
        bool changedSurface=!trackingIdentity&&_toolSurface is {} previousSurface&&toolSurface is {} nextSurface
            &&(previousSurface.Point.DistanceSquaredTo(nextSurface.Point)>.09f*.09f
                ||previousSurface.Normal.Dot(nextSurface.Normal)<.80f);
        bool changedWorkContact=changedPartner||changedTile||changedWorksite||changedSurface;
        bool replacedCommittedWork=!newRun&&phase==ActionPhase.Done&&_observedPhase==ActionPhase.Done&&_finishToolStroke
            &&changedWorkContact;
        bool retargeted=replacedCommittedWork||sameExecution&&(changedWorkContact||changedPickup);
        var family=FamilyOf(action);
        bool continuesWork=newRun&&_observedRevision.HasValue&&revision.HasValue&&revision!=_observedRevision
            &&action==_observedAction&&family==ActionFamily.Tool&&CurrentTool!=ResidentTool.Spear
            &&(phase==ActionPhase.Executing||phase==ActionPhase.Done&&completeToolStroke)
            &&_observedContactReady&&contactReady&&targetAligned&&_strokePhase<1&&GestureEngaged(action)
            &&SameWorkContact(target,toolSurface,sceneContact);
        contactReady&=!ObserveToolSurface(toolSurface,(newRun&&!continuesWork)||retargeted);
        contactReady&=!replacedCommittedWork;
        completeToolStroke&=contactReady;
        bool lostToolContact=sameExecution&&phase==ActionPhase.Executing&&family==ActionFamily.Tool
            &&_observedContactReady&&!contactReady;
        // Keep the visible stroke and its contact marker, but give the
        // incoming revision its own impact. Recovery belongs to the old one.
        if(continuesWork)
            _toolImpactSeen=false;
        else if(newRun||retargeted||lostToolContact)
            ResetToolPhrase(retainPose:family==ActionFamily.Tool&&ToolFor(action)==ToolFor(_observedAction),
                keepImpact:lostToolContact&&!retargeted);
        if(newRun)_retargetSettle=0;
        if(retargeted)
        {
            _retargetSettle=.13f;
            _observedExecution=0;
        }
        if(newRun||phase!=ActionPhase.Done){_completionRemaining=0;_finishConsumption=_finishToolStroke=false;}
        if(newRun){_observedExecution=0;_observedMouthContact=false;}
        bool partnerAction=RequiresPartner(action);
        if(!contactReady||partnerAction&&!hasPartner)
        {
            _completionRemaining=0;
            _finishConsumption=_finishToolStroke=false;
        }
        if(!completeToolStroke)_finishToolStroke=false;
        _awaitToolCommit=awaitToolCommit&&phase==ActionPhase.Executing;
        bool observedCompletion=!firstSnapshot&&phase==ActionPhase.Done&&(newRun||_observedPhase!=ActionPhase.Done);
        bool pendingCompletion=!newRun&&phase==ActionPhase.Done&&_awaitingContact&&_contactWait<.80f;
        if(observedCompletion)_contactWait=0;
        _awaitingContact=(observedCompletion||pendingCompletion)&&deferContact&&!contactReady&&_contactWait<.80f;
        if(_awaitingContact)_contactWait+=PresentationDelta(delta);
        bool newlyDone=observedCompletion||pendingCompletion&&contactReady;
        var nextContact=phase==ActionPhase.Done&&!firstSnapshot||phase==ActionPhase.Executing&&target.Pickup.HasValue?sceneContact:null;
        if(family==ActionFamily.Pickup&&!contactReady)nextContact=null;
        if(newRun||changedPickup||_sceneContact.HasValue!=nextContact.HasValue)
            ResetScenePickup();
        else if(family==ActionFamily.Pickup&&!_scenePickupGrasped&&_sceneContact is {} prior&&nextContact is {} next
            &&prior.DistanceSquaredTo(next)>.022f*.022f)
            _scenePickupContact=0;
        if(newRun)_plannedPickup=false;
        _plannedPickup|=target.Pickup.HasValue;
        _sceneContact=nextContact;
        _scenePickupConfirmed=phase==ActionPhase.Done;
        _interactionTarget=target;
        ObserveCargoPlacement(action,phase,newlyDone&&contactReady,newRun,contactResource,inventoryCargo);
        if(newlyDone&&contactReady&&family==ActionFamily.Consume)
        {
            _completionAge=0;
            _finishConsumption=!_observedMouthContact||ConsumptionOwnsHand;
        }
        else if(newlyDone&&family==ActionFamily.Tool&&completeToolStroke&&(!partnerAction||hasPartner)&&!IsChild)
        {
            _completionAge=0;
            // A confirmed action can finish its first stroke or the recovery
            // of an impact already shown. A spare wind-up must not hit again.
            _finishToolStroke=!_toolImpactSeen||_strokePhase>=ToolImpactPhase(ToolFor(action))&&_strokePhase<1;
        }
        else if(newlyDone&&contactReady&&(!partnerAction||hasPartner)&&(UsesBareHands(family)||family==ActionFamily.Social&&!conversation.HasValue))
        {
            _completionAge=0;
            float minimum=family==ActionFamily.Pickup&&_sceneContact.HasValue?2.5f:1.65f;
            if(!IsPlacement(action)||_hasCargo&&!_placementPending)
                _completionRemaining=MathF.Max(0,minimum-(family==ActionFamily.Pickup&&_sceneContact.HasValue?0:_observedExecution));
        }
        _observedAction=action;_observedPhase=phase;_observedRevision=revision;_observedContactReady=contactReady;
        var visualPhase=partnerAction&&phase==ActionPhase.Executing&&!hasPartner?ActionPhase.Failed:phase;
        _waitingForContact=phase==ActionPhase.Executing&&!contactReady||_awaitingContact;
        AdvancePose(PresentationDelta(delta),action,visualPhase,identityPhase,true,targetAligned&&contactReady);
        PresentPickupPiece(family==ActionFamily.Pickup);
    }

    private bool SameWorkContact(InteractionTarget target,ToolSurface? surface,Vector3? point)
    {
        if(!target.Tile.HasValue||target.Tile!=_interactionTarget.Tile||target.Worksite!=_interactionTarget.Worksite)
            return false;
        const float toleranceSquared=.025f*.025f;
        if(surface is {} current&&_toolSurface is {} previous)
            return current.Point.DistanceSquaredTo(previous.Point)<=toleranceSquared&&current.Normal.Dot(previous.Normal)>=.96f;
        return CurrentTool==ResidentTool.Hoe&&target.Worksite.HasValue&&point is {} soil&&_sceneContact is {} priorSoil
            &&soil.DistanceSquaredTo(priorSoil)<=toleranceSquared;
    }

    internal static bool RequiresPartner(ActionKind action)
        =>action is ActionKind.Socialize or ActionKind.ShareFood or ActionKind.Attack;

    internal static bool UsesWorksiteReceipt(ActionKind action)
        =>action is ActionKind.Farm or ActionKind.BuildHouse or ActionKind.BuildStorage or ActionKind.BuildFarm or ActionKind.BuildMine or ActionKind.Hunt;

    internal static bool UsesInteractionFacing(ActionKind action)
        =>FamilyOf(action) is ActionFamily.Tool or ActionFamily.Pickup or ActionFamily.Transfer or ActionFamily.Social
            ||action==ActionKind.Drink;

    private ActionPhase DisplayedPhase(ActionPhase phase)=>phase==ActionPhase.Done&&CompletionVisible&&!_awaitingContact?ActionPhase.Executing:phase;

    private void PresentActionStep(float delta,ActionKind action,ActionPhase phase,float identityPhase,bool targetAligned)
    {
        _retargetSettle=MathF.Max(0,_retargetSettle-delta);
        targetAligned&=_retargetSettle<=0;
        AdvancePendingPlacement(delta,targetAligned);
        bool finishing=_completionRemaining>0;
        var displayed=DisplayedPhase(phase);
        bool alreadyEngaged=targetAligned&&displayed==ActionPhase.Executing&&GestureEngaged(action);
        int previousContacts=_consumeContacts;
        PoseActionStep(delta,action,displayed,identityPhase,targetAligned,_observedRevision);
        if(_consumeContacts!=previousContacts&&_consumeAction==action&&_consumeRevision==_observedRevision)_observedMouthContact=true;
        if(_finishConsumption)
        {
            _completionAge+=delta;
            if(_observedMouthContact&&!ConsumptionOwnsHand||_completionAge>=6)_finishConsumption=false;
        }
        if(_finishToolStroke)
        {
            _completionAge+=delta;
            if(_completionAge>=6)_finishToolStroke=false;
        }
        bool active=alreadyEngaged&&GestureEngaged(action);
        // Charge time only to substeps actually spent engaged. A coarse
        // observation may contain tool stow, arrival and the first gesture.
        if(phase==ActionPhase.Executing&&active)_observedExecution+=delta;
        if(!finishing)return;
        if(_sceneContact.HasValue&&FamilyOf(action)==ActionFamily.Pickup)
        {
            if(ScenePickupFinished)_completionRemaining=0;
        }
        else if(active)_completionRemaining=MathF.Max(0,_completionRemaining-delta);
        _completionAge+=delta;
        if(_completionAge>=6)_completionRemaining=0;
    }

    internal bool GestureEngaged(ActionKind action)
    {
        if(!InteractionSettled||_lastAction!=action||_lastPhase!=ActionPhase.Executing)return false;
        var family=FamilyOf(action);
        return family switch
        {
            ActionFamily.Tool=>ToolHeld&&CurrentTool==ToolFor(action)&&!_returning&&!_rearmToolStroke
                &&_draw>=.99f&&_workBlend>.9f&&(!UsesStagedStance||WorkStanceSettled),
            ActionFamily.Social=>_socialBlend>.9f||_hasCargo&&CargoMesh.Visible&&!ToolHeld,
            ActionFamily.Rest=>_restPose.X>.9f,
            _=>UsesBareHands(family)&&_bareAction==action&&_bareBlend>.9f
        };
    }
}
