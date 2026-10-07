using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Pathing;
using SandBoxSim.Core.Systems;
using SandBoxSim.Core.Systems.Actions;
using SandBoxSim.Tests.Framework;
namespace SandBoxSim.Tests;
public sealed class ResponsivenessTests
{
    private static Simulation Flat(int width=40,int height=24)
    {
        var config=new SimConfig();config.Ai.BatchCount=1;
        var sim=new Simulation(config,width,height,17);
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {sim.World.SetTerrain(x,y,TerrainKind.Grass);sim.World.SetResource(x,y,new ResourceNode());}
        return sim;
    }
    private static ActionContext Context(Simulation sim,int x,int y)=>new() {World=sim.World,Store=sim.Agents,X=x,Y=y,Config=sim.Config,Ai=sim.Config.Ai,Reachability=sim.Pathfinder};
    private static void Wood(Simulation sim,int x,int y)=>sim.World.SetResource(x,y,new ResourceNode {Kind=ResourceKind.Wood,Capacity=10,Amount=10});
    [Fact("Resource search continues past a stocked but unusable chunk")]
    public void SearchesBeyondUnusableChunk()
    {
        var sim=Flat();Wood(sim,4,12);sim.World.SetWalkable(4,12,false);Wood(sim,17,12);
        var ctx=Context(sim,15,12);Assert.True(ActionSearchProbe.TryFindResource(in ctx,ResourceKind.Wood,out var target));Assert.Equal(new Int2(17,12),target);
    }
    [Fact("Resource targeting skips islands and refreshes after a passage opens")]
    public void AvoidsDisconnectedResources()
    {
        var sim=Flat();for(int y=0;y<sim.World.Height;y++)sim.World.SetWalkable(5,y,false);
        Wood(sim,6,4);Wood(sim,3,8);var ctx=Context(sim,4,4);
        Assert.True(ActionSearchProbe.TryFindResource(in ctx,ResourceKind.Wood,out var target));Assert.Equal(new Int2(3,8),target);
        sim.World.SetWalkable(5,4,true);
        Assert.True(ActionSearchProbe.TryFindResource(in ctx,ResourceKind.Wood,out target));Assert.Equal(new Int2(6,4),target);
    }
    [Fact("Reachability agrees with blocked diagonal corners and handles topology edits")]
    public void DoesNotReachThroughCorners()
    {
        var sim=Flat(8,8);for(int y=0;y<8;y++)for(int x=0;x<8;x++)sim.World.SetWalkable(x,y,false);
        sim.World.SetWalkable(1,1,true);sim.World.SetWalkable(2,2,true);
        Assert.False(sim.Pathfinder.CanReach(1,1,2,2));Assert.False(sim.Pathfinder.FindPath(1,1,2,2,new Int2[64]).Success);
        sim.World.SetWalkable(1,2,true);Assert.True(sim.Pathfinder.CanReach(1,1,2,2));Assert.True(sim.Pathfinder.FindPath(1,1,2,2,new Int2[64]).Success);
    }
    private static int Worker(Simulation sim)
    {
        sim.InterveneSpawnHumans(4,4,1,0);int slot=-1;foreach(int alive in sim.Agents.AliveSlots()){slot=alive;break;}Assert.True(slot>=0);
        sim.Agents.SetHunger(slot,.9f);sim.Agents.SetInventory(slot,ResourceKind.Food,3);
        sim.Agents.SetAction(slot,ActionKind.GatherWood,ActionPhase.Moving);sim.Agents.SetTarget(slot,18,4);sim.Agents.SetNextDecisionTick(slot,1000);return slot;
    }
    [Fact("A starving worker with carried food interrupts work without waiting for cooldown")]
    public void EatsBeforeContinuingWork()
    {
        var sim=Flat();int slot=Worker(sim);sim.Ai.Tick(1,false);
        Assert.Equal(ActionKind.Eat,sim.Agents.ActionOf(slot));Assert.Equal(ActionPhase.Executing,sim.Agents.PhaseOf(slot));Assert.False(sim.Agents.HasTarget(slot));
    }
    [Fact("Normal work and escape are not interrupted by the emergency meal rule")]
    public void PreservesWorkAndEscape()
    {
        var sim=Flat();int slot=Worker(sim);sim.Agents.SetHunger(slot,.3f);sim.Ai.Tick(1,false);Assert.Equal(ActionKind.GatherWood,sim.Agents.ActionOf(slot));
        sim.Agents.SetHunger(slot,.9f);sim.Agents.SetAction(slot,ActionKind.Flee,ActionPhase.Moving);sim.Ai.Tick(2,false);Assert.Equal(ActionKind.Flee,sim.Agents.ActionOf(slot));
        sim.Agents.SetAction(slot,ActionKind.GatherWood,ActionPhase.Executing);sim.Ai.Tick(3,false);Assert.Equal(ActionKind.GatherWood,sim.Agents.ActionOf(slot));
    }
}
