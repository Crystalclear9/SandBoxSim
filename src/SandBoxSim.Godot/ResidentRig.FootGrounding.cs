using System;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    private readonly Vector3[] _groundContactBack=new Vector3[2];

    // Keep one flat sole frame through stance. The contacting point can roll
    // from heel to toe in that frame while the ankle and the hips move.
    internal (Vector3 Point,Vector3 Back,float Weight) GroundedFootFrame(int side,Vector3 position,Vector3 back)
    {
        float phase=Mathf.PosMod(_gaitCycle+side*.5f,1),support=GaitSupport;
        bool preparing=_departureProgress>0;
        if(!_groundContactSpeed||IsPivoting||!preparing&&(_activity<.20f||_arrivalWeight>0||phase>=support))
        {
            _groundContactValid[side]=false;
            return (position,back,0);
        }
        if(!_groundContactValid[side])
        {
            _groundContactValid[side]=true;
            _groundContact[side]=position;
            _groundContactBack[side]=back;
        }
        var pull=_groundContact[side]-position;
        pull.Y=0;
        float weight=preparing?1:ReachEase(phase/.085f)*ReachEase((support-phase)/.105f)*Mathf.SmoothStep(.20f,.74f,_activity);
        weight*=1-Mathf.SmoothStep(.075f,.168f,pull.Length());
        var heading=back.Lerp(_groundContactBack[side],weight);
        if(heading.LengthSquared()<.001f)heading=back;
        return (position+pull*weight,heading.Normalized(),weight);
    }

    internal bool FitWalkingSole(int side,Transform3D root,Transform3D inverse,Func<float,float,float> ground)
    {
        if(!_groundContactSpeed||IsPivoting||_arrivalWeight>0||_activity<.20f&&_departureProgress<=0&&_locomotionSpeed<.035f)
        {
            _groundContactValid[side]=false;
            return false;
        }

        var foot=root*DisplayedFoot(side);
        float scale=root.Basis.X.Length();
        var back=foot.Basis.Z/scale;
        var flatBack=back.Slide(Vector3.Up).Normalized();
        float pitch=Math.Clamp(MathF.Atan2(-back.Y,back.Slide(Vector3.Up).Length()),-.4f,.4f);
        var contact=SoleContact(pitch);
        var rawPivot=foot*contact;
        var flat=FlatSoleBasis(flatBack);
        var anchor=GroundedFootFrame(side,rawPivot-flat*contact*scale,flatBack);
        var pivot=anchor.Point+FlatSoleBasis(anchor.Back)*contact*scale;
        float x=pivot.X,z=pivot.Z;
        float lift=MathF.Max(0,rawPivot.Y-root.Origin.Y)*(1-anchor.Weight);
        float gradeWeight=1-Mathf.SmoothStep(.020f,.095f,lift);
        var grade=new Vector2((ground(x-.12f,z)-ground(x+.12f,z))/.24f,
            (ground(x,z-.14f)-ground(x,z+.14f))/.28f).LimitLength(.60f)*gradeWeight;
        var normal=new Vector3(grade.X,1,grade.Y).Normalized();
        var tangent=anchor.Back.Slide(normal).Normalized();
        var terrain=new Basis(normal.Cross(tangent).Normalized(),normal,tangent);
        var basis=(inverse.Basis*terrain*new Basis(Vector3.Right,pitch)).Orthonormalized();
        // The child rig scales height independently. Sample the outsole
        // with the same affine transform that the solved leg will render.
        var renderedBasis=root.Basis*basis;
        pivot.Y=ground(x,z)+lift;
        var origin=pivot-renderedBasis*contact;

        // A rounded crest can touch the outsole away from the pivot. Probe
        // several actual sole stations and retain clearance over that crest.
        float raise=SoleTerrainOverlap(origin,renderedBasis,ground);
        origin.Y+=MathF.Min(.12f,raise);
        float planeYaw=MathF.Atan2(basis.Z.X,basis.Z.Z)*.55f;
        SolveLegTo(side,inverse*origin,basis,planeYaw);
        var solved=root*DisplayedFoot(side);
        if(solved.Origin.DistanceSquaredTo(origin)>.004f*.004f)
        {
            // A leg at full reach can shift the shoe uphill of the requested
            // point. Check the displayed boot before accepting that contact.
            float overlap=SoleTerrainOverlap(solved.Origin,solved.Basis,ground);
            if(overlap>.001f)SolveLegTo(side,inverse*(solved.Origin+Vector3.Up*MathF.Min(.12f,overlap)),basis,planeYaw);
        }
        return true;
    }

    private float SoleTerrainOverlap(Vector3 origin,Basis basis,Func<float,float,float> ground)
    {
        float overlap=0;
        for(int i=0;i<BootSoleCurve.Length;i+=4)
        {
            var point=origin+basis*BootSoleCurve[i];
            overlap=MathF.Max(overlap,ground(point.X,point.Z)-point.Y);
        }
        return overlap;
    }

    private static Basis FlatSoleBasis(Vector3 back)=>new(Vector3.Up.Cross(back),Vector3.Up,back);
}
