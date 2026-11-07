using Godot;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Systems;

namespace SandBoxSim.Client;

public partial class WorldView3D
{
    private const uint MaxContactRevisionGap=3;

    private bool TryBridgeResidentContact(ResidentRig resident,int slot,ActionKind next,ActionPhase phase,uint revision,
        Int2 origin,Int2 interest,Vector3 logical,float distanceSquared,float delta,
        out ActionSystem.ContactCompletion completion,out Vector3 point,out Vector3 stand)
    {
        completion=default;point=stand=logical;
        var sim=Game.Sim;
        if(!resident.Visible||distanceSquared>=18*18||phase==ActionPhase.Failed
            ||next is ActionKind.Flee or ActionKind.Attack or ActionKind.Hunt or ActionKind.ShareFood or ActionKind.Socialize
            ||!sim.Actions.TryGetLatestContactCompletion(slot,out completion)||completion.Origin!=origin)
            return false;
        uint revisionGap=unchecked(revision-completion.Revision);
        if(revisionGap==0||revisionGap>MaxContactRevisionGap)
            return false;
        // Consecutive work at the same site already shares a stroke in the rig.
        if(completion.Site is {} work&&next==completion.Action&&phase==ActionPhase.Executing
            &&interest==work.Tile&&ResidentRig.ToolFor(next)!=ResidentTool.None)return false;
        long age=sim.Clock-completion.Tick;
        if(!resident.CanBridgeContact(completion,age>=0&&age<=sim.Config.Clock.MaxCatchUpTicksPerFrame))return false;
        if(completion.Site is {} site)
        {
            if(!TryActionContact(resident,completion.Action,site,logical,out point,out stand)
                ||site.Surface!=ActionSystem.ContactSurface.Hunt&&Horizontal(resident.Position-stand).LengthSquared()>.20f*.20f)
                return false;
            if(site.Surface==ActionSystem.ContactSurface.Hunt
                &&(RecallHuntPoint(resident,completion.Revision,site) is not {} aim
                    ||!TrySpearSurface(resident,resident.Position,aim,out _)))return false;
            if((StrikeResource(completion.Action)!=ResourceKind.None||ResidentRig.ToolFor(completion.Action)==ResidentTool.Hammer)
                &&(!TryPlanToolSurface(completion.Action,site.Tile.X,site.Tile.Y,stand,out var strike,out _)
                    ||!ReachableToolSurface(resident,stand,strike)))
                return false;
        }
        else if(completion.Action==ActionKind.Drink&&!TryResidentWaterContact(slot,logical,out _))return false;
        // Only claim the receipt after the live scene still supports it.
        // A missing surface cannot start a playback window that outlives freshness.
        return resident.AdvanceContactBridge(completion,delta);
    }

    private Vector3? RecallHuntPoint(ResidentRig resident,uint revision,ActionSystem.ActionContact site)
        =>resident.PreparedHuntPoint(revision,site.TargetIdentity) is {} point
            &&Horizontal(point-PositionAt(site.Tile.X,site.Tile.Y)).LengthSquared()<1.50f*1.50f?point:null;

    private static ResourceKind StrikeResource(ActionKind action)=>action switch
    {
        ActionKind.GatherWood=>ResourceKind.Wood,
        ActionKind.GatherStone=>ResourceKind.Stone,
        ActionKind.GatherIron=>ResourceKind.Iron,
        _=>ResourceKind.None
    };

    private bool TrySpearSurface(ResidentRig rig,Vector3 root,Vector3 point,out ResidentRig.ToolSurface surface)
    {
        surface=default;
        var normal=Horizontal(root-point);
        float reach=normal.Length()/rig.Scale.X;
        if(reach is <.94f or >1.64f||(point.Y-root.Y)/rig.Scale.Y is <.48f or >1.36f
            ||!ScenerySegmentClear(root,point))return false;
        normal=normal.Normalized();
        surface=new(point,new Vector3(normal.X,0,normal.Y));
        return true;
    }

    private static Vector3 NearBodyPoint(Vector3 observer,Vector3 centre,float depth)
    {
        var radial=Horizontal(observer-centre).Normalized();
        return centre+new Vector3(radial.X,0,radial.Y)*depth;
    }

    private bool TryPlanToolSurface(ActionKind action,int x,int y,Vector3 stand,out ResidentRig.ToolSurface contact,out bool requiresSurface)
    {
        contact=default;
        requiresSurface=false;
        var world=Game.Sim.World;
        if(!world.IsInBounds(x,y))return false;
        var resource=StrikeResource(action);
        if(resource!=ResourceKind.None)
        {
            requiresSurface=true;
            if(!_sceneResourceAnchors.TryGetValue(y*world.Width+x,out var anchor)||anchor.Kind!=resource||anchor.WorkShape is not {} shape)
                return false;
            float height=shape switch {"conifer-tree"=>.48f,"broad-tree"=>.85f,_=>.49f};
            return SceneryPointClear(stand,SceneryClearance)&&_models.TryWorkSurface(shape,anchor.Placement,stand,height,out contact);
        }
        if(ResidentRig.ToolFor(action)!=ResidentTool.Hammer)return false;
        int index=world.TileAt(x,y).BuildingId-1;
        if(!_buildingVisuals.TryGetValue(index,out var structure)||structure.Complete||!Game.Sim.Buildings.IsAlive(index)
            ||structure.Generation!=Game.Sim.Buildings.GenerationOf(index))return false;
        contact=new(structure.Root.Transform*NatureModels.ConstructionPostFace(structure.Identity),structure.Root.Basis.Z.Normalized());
        return true;
    }

    private static bool ReachableToolSurface(ResidentRig rig,Vector3 stand,ResidentRig.ToolSurface surface)
    {
        var offset=(surface.Point-stand)/rig.Scale.X;
        return offset.Y is >=.22f and <=1.42f&&Horizontal(offset).LengthSquared() is >=.25f*.25f and <=.84f*.84f;
    }

    private float CultivationSurfaceHeight(Vector3 point,int x,int y)
    {
        float terrain=GroundHeight(point.X,point.Z);
        var world=Game.Sim.World;
        if(!world.IsInBounds(x,y))return terrain;
        int index=world.TileAt(x,y).BuildingId-1;
        if(!_buildingVisuals.TryGetValue(index,out var farm)||!farm.Complete||farm.Kind!=BuildingKind.Farm)return terrain;
        var local=farm.Root.Transform.AffineInverse()*point;
        if(Mathf.Abs(local.X)>NatureModels.FarmBedHalfWidth||Mathf.Abs(local.Z)>NatureModels.FarmBedHalfWidth)return terrain;
        return Mathf.Max(terrain,(farm.Root.Transform*new Vector3(local.X,NatureModels.FarmBedHeight,local.Z)).Y);
    }

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
            case ActionSystem.ContactSurface.Hunt:
                point=tile;
                return true;
            case ActionSystem.ContactSurface.BuildingWork:
                int workplace=world.TileAt(x,y).BuildingId-1;
                if(!Game.Sim.Buildings.IsAlive(workplace)||Game.Sim.Buildings.GenerationOf(workplace)!=site.BuildingGeneration)
                    return false;
                if(action==ActionKind.Farm)
                {
                    // Furrow probes still sample the actual ground from the
                    // resident's feet, along the farm's rendered row direction.
                    var forward=_buildingVisuals.TryGetValue(workplace,out var farm)?-farm.Root.Basis.Z.Normalized():Vector3.Forward;
                    for(int side=0;side<2;side++,forward=-forward)
                    {
                        var far=logical+forward*(-ResidentRig.FurrowStart*resident.Scale.X);
                        var near=logical+forward*(-ResidentRig.FurrowEnd*resident.Scale.X);
                        float high= CultivationSurfaceHeight(far,x,y)-logical.Y;
                        float low= CultivationSurfaceHeight(near,x,y)-logical.Y;
                        if(Mathf.Abs(high)>.17f||Mathf.Abs(low)>.17f)continue;
                        point=logical+forward*.68f;
                        return true;
                    }
                    return false;
                }
                if(Game.Sim.Buildings.StateOf(workplace)==BuildingState.Complete)return false;
                if(!_buildingVisuals.TryGetValue(workplace,out var construction)||construction.Generation!=site.BuildingGeneration)
                {
                    point=tile;
                    return true;
                }
                point=construction.Root.Transform*NatureModels.ConstructionPostFace(construction.Identity);
                outward=construction.Root.Basis.Z;
                break;
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
