using Godot;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Systems;

namespace SandBoxSim.Client;

public partial class WorldView3D
{
    private bool TryActionContact(ResidentRig resident, ActionKind action, ActionSystem.ActionContact site,
        Vector3 logical, out Vector3 point, out Vector3 stand)
    {
        point=stand=logical;
        int x=site.Tile.X,y=site.Tile.Y;
        var world=Game.Sim.World;
        if(!world.IsInBounds(x,y))return false;
        var tile=PositionAt(x,y);
        if(Horizontal(tile-logical).LengthSquared()>4.01f)return false;

        Vector3 outward;
        switch(site.Surface)
        {
            case ActionSystem.ContactSurface.NaturalResource:
                if(!_sceneResourceAnchors.TryGetValue(y*world.Width+x,out var anchor)||anchor.Kind!=site.Resource)return false;
                point=anchor.Point;
                if(action!=ActionKind.GatherFood)
                {
                    stand=SceneryWorkPosition(x,y,logical,action,point);
                    return true;
                }
                outward=tile-point;
                break;
            case ActionSystem.ContactSurface.GroundStock:
                point=GroundPileOrigin(tile,site.Resource);
                if(action==ActionKind.Take)
                    point+=NatureModels.GroundPileGrip(site.Resource,NatureModels.GroundPileTier(site.StockAmount));
                outward=tile-point;
                break;
            case ActionSystem.ContactSurface.Storage:
                int building=world.TileAt(x,y).BuildingId-1;
                if(!_buildingVisuals.TryGetValue(building,out var visual)||!visual.Complete||visual.Kind!=BuildingKind.Storage
                    ||visual.Generation!=site.BuildingGeneration||Game.Sim.Buildings.GenerationOf(building)!=site.BuildingGeneration)
                    return false;
                point=visual.Root.Transform*NatureModels.StorageCrateTop(NatureModels.BuildingFootprint(visual.Identity).Y);
                outward=visual.Root.Basis.Z;
                break;
            default:
                return false;
        }
        outward.Y=0;
        if(outward.LengthSquared()<.001f)outward=Vector3.Back;
        outward=outward.Normalized();
        float reach=action==ActionKind.Deposit?.54f:site.Surface==ActionSystem.ContactSurface.Storage?.43f:.47f;
        // Only local presentation moves are allowed. Stay on dry support and
        // preserve the chosen side of the stock even as the actor turns.
        for(int i=0;i<3;i++)
        {
            float angle=i==0?0:i==1?.38f:-.38f;
            var candidate=point+outward.Rotated(Vector3.Up,angle)*(reach*resident.Scale.X);
            candidate.Y=GroundHeight(candidate.X,candidate.Z);
            if(Horizontal(candidate-logical).LengthSquared()>6.25f||!DryActorTarget(candidate)
                ||!SceneryPointClear(candidate,SceneryClearance)||!SceneryLegSupported(logical,candidate)
                ||!ScenerySegmentClear(logical,candidate))continue;
            stand=candidate;
            return true;
        }
        return false;
    }
}
