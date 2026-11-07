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
    internal readonly record struct InteractionTarget(Vector2I? Tile,long Partner,ActionSystem.ActionContact? Pickup);
    private InteractionTarget _interactionTarget;
    private float _retargetSettle,_contactWait;
    private bool _awaitingContact;
    public bool CompletionVisible=>_finishConsumption||_finishToolStroke||_completionRemaining>0||_placingCargo||_placementPending||_awaitingContact;

    internal bool ShowsCompletion(ActionKind action,uint revision)
        =>_observedRevision.HasValue&&(revision!=_observedRevision||action!=_observedAction
            ||_observedPhase!=ActionPhase.Done||CompletionVisible);

    internal void ObserveOffscreenAction(ActionKind action,ActionPhase phase,uint revision)
    {
        _observedAction=action;_observedPhase=phase;_observedRevision=revision;
        _observedExecution=_completionRemaining=0;
        _waitingForContact=false;
        _awaitingContact=false;
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
        bool completeToolStroke=true,bool awaitToolCommit=false,ToolSurface? toolSurface=null,bool contactReady=true,bool deferContact=false)
    {
        if(paused)return;
        bool firstSnapshot=revision.HasValue&&!_observedRevision.HasValue;
        bool newRun=action!=_observedAction||revision!=_observedRevision
            ||phase==ActionPhase.Moving&&_observedPhase!=ActionPhase.Moving
            ||phase==ActionPhase.Executing&&_observedPhase is ActionPhase.Done or ActionPhase.Idle or ActionPhase.Failed;
        bool sameExecution=!newRun&&_observedPhase==ActionPhase.Executing&&phase is ActionPhase.Executing or ActionPhase.Done;
        // The active hunt tracks a live animal. Its committed receipt keeps
        // the last position after that animal leaves the live render set.
        bool changedPartner=(RequiresPartner(action)||action==ActionKind.Hunt&&phase==ActionPhase.Executing)
            &&target.Partner!=_interactionTarget.Partner;
        bool changedTile=target.Tile.HasValue&&_interactionTarget.Tile.HasValue&&target.Tile!=_interactionTarget.Tile;
        bool changedPickup=target.Pickup!=_interactionTarget.Pickup;
        bool changedSurface=_toolSurface is {} previousSurface&&toolSurface is {} nextSurface
            &&(previousSurface.Point.DistanceSquaredTo(nextSurface.Point)>.09f*.09f
                ||previousSurface.Normal.Dot(nextSurface.Normal)<.80f);
        bool retargeted=sameExecution&&(changedPartner||changedTile||changedPickup||changedSurface);
        var family=FamilyOf(action);
        contactReady&=!ObserveToolSurface(toolSurface,newRun||retargeted);
        completeToolStroke&=contactReady;
        if(newRun||retargeted)
            ResetToolPhrase(retainPose:family==ActionFamily.Tool&&ToolFor(action)==ToolFor(_observedAction));
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
        if(newRun||changedPickup||_sceneContact.HasValue!=nextContact.HasValue)
            ResetScenePickup();
        if(newRun)_plannedPickup=false;
        _plannedPickup|=target.Pickup.HasValue;
        _sceneContact=nextContact;
        _scenePickupConfirmed=phase==ActionPhase.Done;
        _interactionTarget=target;
        ObserveCargoPlacement(action,phase,newlyDone&&contactReady,newRun,contactResource);
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
        else if(newlyDone&&contactReady&&(!partnerAction||hasPartner)&&(UsesBareHands(family)||family==ActionFamily.Social))
        {
            _completionAge=0;
            float minimum=family==ActionFamily.Pickup&&_sceneContact.HasValue?2.5f:1.65f;
            if(!IsPlacement(action)||_hasCargo&&!_placementPending)
                _completionRemaining=MathF.Max(0,minimum-(family==ActionFamily.Pickup&&_sceneContact.HasValue?0:_observedExecution));
        }
        _observedAction=action;_observedPhase=phase;_observedRevision=revision;
        var visualPhase=partnerAction&&phase==ActionPhase.Executing&&!hasPartner?ActionPhase.Failed:phase;
        _waitingForContact=phase==ActionPhase.Executing&&!contactReady||_awaitingContact;
        AdvancePose(PresentationDelta(delta),action,visualPhase,identityPhase,true,targetAligned&&contactReady);
        PresentPickupPiece(family==ActionFamily.Pickup);
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
        if(_sceneContact.HasValue&&FamilyOf(action)==ActionFamily.Pickup&&_scenePickupAge>=ScenePickupDuration)
        {
            _completionRemaining=0;
            return;
        }
        if(active)_completionRemaining=MathF.Max(0,_completionRemaining-delta);
        _completionAge+=delta;
        if(_completionAge>=6)_completionRemaining=0;
    }

    internal bool GestureEngaged(ActionKind action)
    {
        if(!InteractionSettled||_lastAction!=action||_lastPhase!=ActionPhase.Executing)return false;
        var family=FamilyOf(action);
        return family switch
        {
            ActionFamily.Tool=>ToolHeld&&CurrentTool==ToolFor(action)&&!_returning&&_draw>=.99f&&_workBlend>.9f&&(!UsesStagedStance||WorkStanceSettled),
            ActionFamily.Social=>_socialBlend>.9f||_hasCargo&&CargoMesh.Visible&&!ToolHeld,
            ActionFamily.Rest=>_restPose.X>.9f,
            _=>UsesBareHands(family)&&_bareAction==action&&_bareBlend>.9f
        };
    }
}
