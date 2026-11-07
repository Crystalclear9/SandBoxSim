using System;
using SandBoxSim.Core.Agents;

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
    public bool CompletionVisible=>_finishConsumption||_completionRemaining>0||_placingCargo||_placementPending;

    internal void ObserveOffscreenAction(ActionKind action,ActionPhase phase,uint revision)
    {
        _observedAction=action;_observedPhase=phase;_observedRevision=revision;
        _observedExecution=_completionRemaining=0;
        _finishConsumption=_observedMouthContact=false;
        ClearConsumption();
        ClearTransientCargo();
    }

    public void PresentAction(float delta,ActionKind action,ActionPhase phase,bool paused,float identityPhase,uint? revision=null,bool hasPartner=true,bool targetAligned=true)
    {
        if(paused)return;
        bool firstSnapshot=revision.HasValue&&!_observedRevision.HasValue;
        bool newRun=action!=_observedAction||revision!=_observedRevision
            ||phase==ActionPhase.Moving&&_observedPhase!=ActionPhase.Moving
            ||phase==ActionPhase.Executing&&_observedPhase is ActionPhase.Done or ActionPhase.Idle or ActionPhase.Failed;
        if(newRun||phase!=ActionPhase.Done){_completionRemaining=0;_finishConsumption=false;}
        if(newRun){_observedExecution=0;_observedMouthContact=false;}
        bool partnerAction=RequiresPartner(action);
        if(partnerAction&&!hasPartner)_completionRemaining=0;
        bool newlyDone=!firstSnapshot&&phase==ActionPhase.Done&&(newRun||_observedPhase!=ActionPhase.Done);
        ObserveCargoPlacement(action,phase,newlyDone,newRun);
        var family=FamilyOf(action);
        if(newlyDone&&family==ActionFamily.Consume)
        {
            _completionAge=0;
            _finishConsumption=!_observedMouthContact||ConsumptionOwnsHand;
        }
        else if(newlyDone&&(!partnerAction||hasPartner)
            &&(UsesBareHands(family)||family is ActionFamily.Tool or ActionFamily.Social))
        {
            _completionAge=0;
            float minimum=family==ActionFamily.Tool?MathF.Max(.9f,WorkPeriod):1.65f;
            if(!IsPlacement(action)||_hasCargo)
                _completionRemaining=MathF.Max(0,minimum-_observedExecution);
        }
        _observedAction=action;_observedPhase=phase;_observedRevision=revision;
        var visualPhase=partnerAction&&phase==ActionPhase.Executing&&!hasPartner?ActionPhase.Failed:phase;
        AdvancePose(PresentationDelta(delta),action,visualPhase,identityPhase,true,targetAligned);
    }

    internal static bool RequiresPartner(ActionKind action)
        =>action is ActionKind.Socialize or ActionKind.ShareFood or ActionKind.Attack;

    internal static bool UsesInteractionFacing(ActionKind action)
        =>FamilyOf(action) is ActionFamily.Tool or ActionFamily.Pickup or ActionFamily.Transfer or ActionFamily.Social
            ||action==ActionKind.Drink;

    private ActionPhase DisplayedPhase(ActionPhase phase)=>phase==ActionPhase.Done&&CompletionVisible?ActionPhase.Executing:phase;

    private void PresentActionStep(float delta,ActionKind action,ActionPhase phase,float identityPhase,bool targetAligned)
    {
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
        bool active=alreadyEngaged&&GestureEngaged(action);
        // Charge time only to substeps actually spent engaged. A coarse
        // observation may contain tool stow, arrival and the first gesture.
        if(phase==ActionPhase.Executing&&active)_observedExecution+=delta;
        if(!finishing)return;
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
