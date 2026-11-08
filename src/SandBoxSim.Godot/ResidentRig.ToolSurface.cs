using System;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    internal readonly record struct ToolSurface(Vector3 Point,Vector3 Normal);
    internal static bool ContinuesSpearAim(ToolSurface previous,ToolSurface next)
        =>previous.Point.DistanceSquaredTo(next.Point)<=.28f*.28f&&previous.Normal.Dot(next.Normal)>=.90f;
    // These centres lie on the exposed ends of the authored tool meshes.
    internal static Vector3 StrikingPoint(ResidentTool tool)=>tool switch
    {
        ResidentTool.Axe=>new(0,-.28f,-.10f),
        ResidentTool.Pick=>new(0,-.342f,-.17f),
        ResidentTool.Hammer=>new(-.0665f,-.30f,0),
        ResidentTool.Spear=>new(0,1.11f,0),
        _=>Vector3.Zero
    };

    private ToolSurface? _toolSurface;
    private bool _hadToolSurface,_surfaceTouched;
    private bool MeasuresWorkSurface=>_toolSurface.HasValue||CurrentTool==ResidentTool.Hoe&&_hasFurrowSurface;
    private float _surfacePhase,_workSurfaceBend;
    internal int PresentedSurfaceContacts {get;private set;}
    internal Vector3 WorldToolTip=>HandWorldPose(1)*Grip!.Transform*StrikingPoint(CurrentTool);

    private bool ObserveToolSurface(ToolSurface? surface,bool reset)
    {
        if(reset){_hadToolSurface=false;_surfaceTouched=false;}
        _toolSurface=surface;
        _hadToolSurface|=surface.HasValue;
        return _hadToolSurface&&!surface.HasValue;
    }

    private float WorkSurfaceWeight()
    {
        float impact=ToolImpactPhase(CurrentTool);
        return ReachEase((_strokePhase-(impact-.32f))/.25f)
            *(1-ReachEase((_strokePhase-impact-.05f)/(1-impact-.05f)));
    }

    private float WorkSurfaceBend()
    {
        if(CurrentTool is not (ResidentTool.Pick or ResidentTool.Axe)||_toolSurface is not {} surface)return 0;
        float height=(RigWorldSpace.AffineInverse()*surface.Point).Y;
        return Mathf.Clamp((.76f-height)*.42f,0,.15f)*WorkSurfaceWeight();
    }

    private float AdvanceWorkSurfaceBend(float delta,bool working,float blend)
    {
        float target=working?WorkSurfaceBend()*blend:0;
        if(target==0&&_workSurfaceBend<.0001f)return _workSurfaceBend=0;
        _workSurfaceBend=Mathf.Lerp(_workSurfaceBend,target,1-MathF.Exp(-18*delta));
        return _workSurfaceBend;
    }

    private (Vector3 Grip,Basis Orientation) GuideWorkTool(Vector3 grip,Basis basis,float blend)
    {
        if(_toolSurface is not {} surface||_returning||_draw<.99f||blend<=0)return (grip,basis);
        var tip=StrikingPoint(CurrentTool);
        float weight=WorkSurfaceWeight()*blend;
        if(tip==Vector3.Zero||weight<=0)return (grip,basis);
        if(CurrentTool==ResidentTool.Pick)
        {
            var downward=Basis.FromEuler(new(.26f,0,.30f));
            basis=new Basis(basis.GetRotationQuaternion().Slerp(downward.GetRotationQuaternion(),weight));
        }
        var chest=RigWorldSpace*Torso.Transform;
        var inverse=chest.AffineInverse();
        var contact=surface.Point+surface.Normal*.010f;
        if(CurrentTool==ResidentTool.Spear)
        {
            // Steer the shaft from the guard during extension. Leaving a
            // live aim point unfiltered would move the tip with the target.
            var aim=(inverse*contact-grip).Normalized();
            if(aim.LengthSquared()>.5f)
            {
                var pointed=new Quaternion(basis.Y,aim)*basis.GetRotationQuaternion();
                basis=new Basis(basis.GetRotationQuaternion().Slerp(pointed,weight));
            }
        }
        var wanted=inverse*contact-basis*tip;
        return (grip+(wanted-grip).LimitLength(.42f)*weight,basis);
    }

    private void MeasureWorkContact(bool working)
    {
        if(_strokePhase<_surfacePhase)_surfaceTouched=false;
        _surfacePhase=_strokePhase;
        if(!working||!_strikeWindowOpen||_surfaceTouched||_draw<.99f)return;
        if(CurrentTool==ResidentTool.Hoe)
        {
            if(_strokePhase>.54f||!HoeTouchesFurrow())return;
        }
        else
        {
            if(_toolSurface is not {} surface||CurrentTool==ResidentTool.Spear&&_awaitToolCommit
                ||Mathf.Abs(_strokePhase-ToolImpactPhase(CurrentTool))>.105f)return;
            var offset=WorldToolTip-surface.Point;
            if(offset.LengthSquared()>.060f*.060f||offset.Dot(surface.Normal)<-.035f)return;
        }
        _surfaceTouched=true;
        PresentedSurfaceContacts++;
        PresentedToolImpacts++;
    }

    private Vector3 GuideSpearTip(Vector3 grip,Basis filteredBasis)
    {
        if(_toolSurface is not {} surface||_awaitToolCommit||_draw<.99f)return grip;
        float weight=ReachEase((_strokePhase-.55f)/.12f)*(1-ReachEase((_strokePhase-.70f)/.17f));
        if(weight<=0)return grip;
        // Use the filtered shaft direction when locating the grip. At the
        // end of a long lever a few degrees of lag can miss the surface.
        var target=(RigWorldSpace*Torso.Transform).AffineInverse()*(surface.Point+surface.Normal*.010f);
        var correction=target-(grip+filteredBasis*StrikingPoint(ResidentTool.Spear));
        return grip+correction.LimitLength(.14f)*weight;
    }
}
