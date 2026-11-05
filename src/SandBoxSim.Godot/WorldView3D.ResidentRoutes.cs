using System;
using Godot;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Client;

public partial class WorldView3D
{
    internal struct ResidentRoute
    {
        internal (Int2 Tile,Int2 Destination,int Revision,int SearchLimit)? Key;
        internal Int2 Next,Following;
        internal int LegCount;
    }

    private bool TryProjectResidentRoute(ResidentRig rig,int slot,Int2 tile,Int2 destination,
        float tickFraction,ref int searches,out Vector3 sample,out float remaining,out float turn,out float speedLimit)
    {
        sample=default;remaining=turn=speedLimit=0;
        var sim=Game.Sim;
        float step=sim.Config.Ai.MoveSpeedPerTick;
        if(tile==destination||step<=0||sim.Civilizations.IsTransporting(slot))return false;

        // The same A* rules as the simulator, on the observer's own search buffers.
        // One query per tile/goal/topology combination, even when the route fails.
        ref var route=ref rig.Route;
        var key=(tile,destination,sim.World.Revision,sim.Pathfinder.MaxExpandedNodes);
        if(route.Key!=key)
        {
            if(searches==0)return false;
            searches--;
            _observerPaths.MaxExpandedNodes=key.MaxExpandedNodes;
            var result=_observerPaths.FindPath(tile.X,tile.Y,destination.X,destination.Y,_observedRoute);
            route.Key=key;
            route.LegCount=result.Success?Math.Min(2,result.Length-1):0;
            if(route.LegCount>0)route.Next=_observedRoute[1];
            if(route.LegCount>1)route.Following=_observedRoute[2];
            if(route.LegCount>0&&!ResidentRouteLegClear(tile,route.Next))route.LegCount=0;
            if(route.LegCount>1&&!ResidentRouteLegClear(route.Next,route.Following))route.LegCount=1;
        }
        if(route.LegCount==0)return false;

        var origin=PositionAt(tile.X,tile.Y);
        var next=PositionAt(route.Next.X,route.Next.Y);
        float progress=sim.Actions.MoveProgressOf(slot)+tickFraction*step;
        var first=Horizontal(next-origin);
        sample=origin.Lerp(next,Math.Clamp(progress,0,1));
        remaining=Horizontal(next-rig.Position).Length();
        float metresPerTile=first.Length();
        if(route.LegCount==2)
        {
            var following=PositionAt(route.Following.X,route.Following.Y);
            var second=Horizontal(following-next);
            if(progress>1)
            {
                // A tick can complete this leg and retain a fractional step.
                // Keep that fraction continuous on the adjoining leg.
                sample=next.Lerp(following,Math.Clamp(progress-1,0,.5f));
                metresPerTile=second.Length();
            }
            else turn=.72f*Mathf.AngleDifference(HeadingYaw(first),HeadingYaw(second))
                *Mathf.SmoothStep(.44f,.92f,progress);
            remaining+=second.Length();
        }
        speedLimit=MathF.Min(ResidentRig.MaximumGaitSpeed,(float)Game.ObservationTickRate*step*metresPerTile+.30f);
        var current=rig.Position;
        return Horizontal(sample-current).LengthSquared()<MaximumAdjacentPresentationStep*MaximumAdjacentPresentationStep
            &&ScenerySegmentClear(current,sample)&&SceneryLegSupported(current,sample);
    }

    private bool ResidentRouteLegClear(Int2 start,Int2 end)
    {
        var from=PositionAt(start.X,start.Y);var to=PositionAt(end.X,end.Y);
        return ScenerySegmentClear(from,to)&&SceneryLegSupported(from,to);
    }
}
