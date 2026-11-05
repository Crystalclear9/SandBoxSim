using System;
using Godot;
using SandBoxSim.Core;

namespace SandBoxSim.Client;

public partial class WorldView3D
{
    private bool AnimalBankSupport(Vector3 point,float radius)
    {
        if(!DryActorTarget(point)||GroundHeight(point.X,point.Z)<=.035f)return false;
        for(int i=0;i<24;i++)
        {
            float angle=i*Mathf.Tau/24;
            float x=point.X+MathF.Cos(angle)*radius,z=point.Z+MathF.Sin(angle)*radius;
            if(GroundHeight(x,z)<=.035f)return false;
            if(i%3==0&&GroundHeight((point.X+x)*.5f,(point.Z+z)*.5f)<=.035f)return false;
        }
        return true;
    }
    private Vector3 AnimalShoreTarget(AnimalRig rig,Vector3 logical)
    {
        long ground=_terrainSignature^_wearSignature;
        if(rig.BankPlacement is { } cached&&cached.Logical==logical&&cached.Ground==ground
            &&cached.Scenery==_sceneryPlacementRevision)return cached.Point;
        var target=ResolveAnimalShoreTarget(logical,rig.BankSupportRadius);
        rig.BankPlacement=(logical,ground,_sceneryPlacementRevision,target);return target;
    }
    private Vector3 ResolveAnimalShoreTarget(Vector3 logical,float radius)
    {
        if(!ActorNearShore(logical)||AnimalBankSupport(logical,radius))return logical;
        // Keep the simulator's tile and choose the smallest supported offset.
        // This is not a route through another tile or a replacement for A*.
        int tx=(int)MathF.Floor(logical.X*.5f),ty=(int)MathF.Floor(logical.Z*.5f);
        for(int ring=1;ring<=4;ring++)for(int i=0;i<8;i++)
        {
            float angle=i*Mathf.Tau/8;
            var point=logical+new Vector3(MathF.Cos(angle),0,MathF.Sin(angle))*(ring*.20f);
            if((int)MathF.Floor(point.X*.5f)!=tx||(int)MathF.Floor(point.Z*.5f)!=ty)continue;
            point.Y=GroundHeight(point.X,point.Z);
            if(SceneryPointClear(point,SceneryClearance)&&AnimalBankSupport(point,radius))return point;
        }
        // Narrow banks may genuinely lack enough room. Never move into an
        // unrelated tile or falsify the simulator's location to hide that case.
        return logical;
    }
    private void ValidateAnimalBanks()
    {
        string digest=StateHash.ComputeDigest(Game.Sim);int repaired=0,unresolved=0,samples=0;
        foreach(bool wolf in new[]{false,true})
        {
            var rig=(AnimalRig)_models.Animal(wolf);
            try
            {
                float radius=rig.BankSupportRadius;
                if(!float.IsFinite(radius)||radius<=.28f)throw new InvalidOperationException("Animal support ignores authored sole span");
                var world=Game.Sim.World;
                for(int y=0;y<world.Height;y++)for(int x=0;x<world.Width;x++)
                {
                    var logical=PositionAt(x,y);
                    if(!DryActorTarget(logical)||!ActorNearShore(logical))continue;
                    var target=AnimalShoreTarget(rig,logical);
                    if(target.DistanceTo(AnimalShoreTarget(rig,logical))>.000001f
                        ||(int)MathF.Floor(target.X*.5f)!=x||(int)MathF.Floor(target.Z*.5f)!=y)
                        throw new InvalidOperationException("Animal bank cache changes tile or placement");
                    if(!AnimalBankSupport(target,radius)){unresolved++;continue;}
                    if(target!=logical)repaired++;
                    for(int heading=0;heading<24;heading++)for(int foot=0;foot<4;foot++)
                    {
                        var sole=rig.Legs[foot].Position+rig.Knees[foot].Position+rig.RestSoles![foot];
                        for(int step=-1;step<=1;step++)
                        {
                            var offset=new Basis(Vector3.Up,heading*Mathf.Tau/24)*(sole+new Vector3(0,0,step*.14f));
                            if(GroundHeight(target.X+offset.X,target.Z+offset.Z)<=.021f)
                                throw new InvalidOperationException("Supported animal bank leaves an actual sole over water");
                            samples++;
                        }
                    }
                }
                var plain=new Vector3(1,0,1);
                rig.BankPlacement=(plain,long.MinValue,_sceneryPlacementRevision,new(100,0,100));
                if(AnimalShoreTarget(rig,plain)==new Vector3(100,0,100))throw new InvalidOperationException("Animal bank placement survives ground-version change");
            }
            finally{rig.Free();}
        }
        if(repaired==0||samples==0||StateHash.ComputeDigest(Game.Sim)!=digest)
            throw new InvalidOperationException("Animal bank checks lack repairs or change simulation");
        GD.Print($"ANIMAL_BANK_PASS: {repaired} corrected bank targets, {samples} actual sole/heading/stride samples; {unresolved} narrow-bank cases retain logical position, cache/version and unchanged simulation");
    }
}
