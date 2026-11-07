using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    private enum ConsumeStage { Free, Reach, Lift, Use, Return }

    private ConsumeStage _consumeStage;
    private ActionKind _consumeAction;
    private uint? _consumeRevision;
    private float _consumeReach,_consumeTime,_consumeContactTime,_consumeReturnStart;
    private int _consumeContacts;
    internal MeshInstance3D Cup=null!,Bite=null!;
    internal static readonly Vector3 CupRim=new(0,-.075f,-.059f);
    private const float BiteCurl=.57f;
    internal static Vector3 BiteCentre=>IndexTipOffset(1,BiteCurl)+new Vector3(-.003f,-.002f,-.006f);
    private bool ConsumptionOwnsHand=>_consumeStage!=ConsumeStage.Free;
    private string ConsumptionHandling=>_consumeStage switch
    {
        ConsumeStage.Reach=>_consumeAction==ActionKind.Drink?"取杯":"取食物",
        ConsumeStage.Lift=>_consumeAction==ActionKind.Drink?"举杯":"送到嘴边",
        ConsumeStage.Use=>_consumeAction==ActionKind.Drink?(_consumeContactTime>0?"饮水":"对准杯沿"):"进食",
        _=>_consumeAction==ActionKind.Drink?"放低杯子 / 收手":"收手"
    };
    internal int ConsumeContacts=>_consumeContacts;
    internal Vector3 MouthContact=>Head.Transform*(Mouth.Position+new Vector3(.002f,.002f,-.012f));
    // Keep the displayed wrist and the held object in front of the cloth and
    // the diagonal belt tool throughout the pickup and release.
    internal Vector3 ConsumePocket=>ReachBodyPoint(new Vector3(.225f,.84f,-.227f));

    private void ClearConsumption()
    {
        if(!ConsumptionOwnsHand)return;
        _consumeStage=ConsumeStage.Free;
        _consumeReach=_consumeTime=_consumeContactTime=0;
        Cup.Visible=Bite.Visible=false;
    }

    private void ReturnConsumption()
    {
        _consumeStage=ConsumeStage.Return;
        _consumeReturnStart=_consumeReach;
        _consumeTime=_consumeContactTime=0;
    }

    private void PoseConsumption(float delta,ActionKind request,uint? revision)
    {
        if(!ConsumptionOwnsHand)
        {
            if(request==ActionKind.None)return;
            _consumeStage=ConsumeStage.Reach;_consumeAction=request;_consumeRevision=revision;
            _consumeTime=_consumeContactTime=0;
        }
        if((request!=_consumeAction||revision!=_consumeRevision)&&_consumeStage!=ConsumeStage.Return)
        {
            if(_consumeStage==ConsumeStage.Reach){ClearConsumption();return;}
            ReturnConsumption();
        }

        bool drink=_consumeAction==ActionKind.Drink;
        float closed=drink?.82f:BiteCurl;
        _consumeTime+=delta;
        _consumeReach=_consumeStage switch
        {
            ConsumeStage.Lift=>ReachEase(_consumeTime/.66f),
            ConsumeStage.Use=>1,
            ConsumeStage.Return=>_consumeReturnStart*(1-ReachEase(_consumeTime/.62f)),
            _=>0
        };
        var pocket=ConsumePocket;
        var contact=MouthContact;
        var propContact=drink?CupRim:BiteCentre;
        var prior=_previousHands[1].Basis;
        var mouthGrip=contact+prior*(Grip!.Position-propContact);
        var path=pocket.Lerp(mouthGrip,_consumeReach);
        float arc=4*_consumeReach*(1-_consumeReach);
        path+=new Vector3(.032f-.014f*Flow,.033f,-.044f)*arc;
        float handPitch=drink?Mathf.Lerp(2.32f,3.54f,_consumeReach):Mathf.Lerp(1.28f,3.20f,_consumeReach);
        BlendBareHand(1,PalmMotion.Consume,path,Basis.FromEuler(new(handPitch,0,.12f+.08f*_consumeReach)),1,eased:true);

        var hand=Arms[1].Transform*Elbows[1].Transform*Hands[1].Transform;
        float pocketGap=(hand*Grip.Position).DistanceTo(pocket);
        if(_consumeStage==ConsumeStage.Reach)
        {
            // Open around the item before closing at the pickup point.
            _consumeContactTime=pocketGap<.032f?_consumeContactTime+delta:0;
            CurlHand(1,Mathf.Lerp(.10f,closed,ReachEase(_consumeContactTime/.14f)));
            if(_consumeContactTime>=.14f)
            {
                Cup.Visible=drink;Bite.Visible=!drink;
                _consumeStage=ConsumeStage.Lift;_consumeTime=_consumeContactTime=0;
            }
        }
        else if(_consumeStage==ConsumeStage.Return&&_consumeReach<=.001f&&pocketGap<.032f)
        {
            _consumeContactTime+=delta;
            CurlHand(1,Mathf.Lerp(closed,.12f,ReachEase(_consumeContactTime/.16f)));
            if(_consumeContactTime>=.16f)ClearConsumption();
        }
        else
        {
            CurlHand(1,closed);
            float gap=(hand*propContact).DistanceTo(contact);
            if(_consumeStage==ConsumeStage.Lift&&_consumeReach>=.999f)
            {
                _consumeStage=ConsumeStage.Use;
                _consumeTime=_consumeContactTime=0;
            }
            if(_consumeStage==ConsumeStage.Use)
            {
                _consumeContactTime=gap<(drink?.020f:.024f)?_consumeContactTime+delta:0;
                if(_consumeContactTime>=(drink?.20f:.09f))
                {
                    _consumeContacts++;
                    if(!drink)Bite.Visible=false;
                    ReturnConsumption();
                }
                else if(_consumeTime>1.1f)
                {
                    ReturnConsumption();
                }
            }
        }
        if(_consumeStage==ConsumeStage.Return&&_consumeTime>=1.8f)ClearConsumption();
    }
}
