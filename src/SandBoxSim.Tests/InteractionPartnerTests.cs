using System.Linq;
using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

public sealed class InteractionPartnerTests
{
    [Fact("Interaction candidate agrees with execution selection and remains read-only")]
    public void CandidatesFollowActionAndLiveState()
    {
        var config=new SimConfig();config.World.Width=config.World.Height=32;
        var sim=new Simulation(config,32,32,1234);
        for(int y=0;y<32;y++)for(int x=0;x<32;x++)sim.World.SetTerrain(x,y,TerrainKind.Grass);
        sim.InterveneSpawnHumans(12,12,3,2);
        var slots=sim.Agents.AliveSlots().ToArray();Assert.Equal(3,slots.Length);
        int actor=slots[0],near=slots[1],hungry=slots[2];
        sim.Agents.SetPosition(actor,12,12);sim.Agents.SetPosition(near,13,12);sim.Agents.SetPosition(hungry,14,12);
        sim.Agents.SetHunger(near,.1f);sim.Agents.SetHunger(hungry,.9f);
        sim.Agents.SetAction(actor,ActionKind.Socialize,ActionPhase.Executing);
        string before=sim.StateDigestString();
        Assert.True(sim.Actions.TryGetInteractionPartner(actor,out int partner));Assert.Equal(near,partner);
        Assert.Equal(before,sim.StateDigestString());
        sim.Agents.SetAction(actor,ActionKind.ShareFood,ActionPhase.Executing);
        Assert.False(sim.Actions.TryGetInteractionPartner(actor,out partner));
        sim.Agents.AddInventory(actor,ResourceKind.Food,config.Relationship.ShareFoodAmount*3);
        Assert.True(sim.Actions.TryGetInteractionPartner(actor,out partner));Assert.Equal(hungry,partner);
        sim.Agents.SetPosition(hungry,31,31);
        Assert.True(sim.Actions.TryGetInteractionPartner(actor,out partner));Assert.Equal(near,partner);
        sim.Agents.SetPhase(actor,ActionPhase.Moving);
        Assert.False(sim.Actions.TryGetInteractionPartner(actor,out partner));Assert.Equal(-1,partner);
        sim.Agents.SetAction(actor,ActionKind.Eat,ActionPhase.Executing);
        Assert.False(sim.Actions.TryGetInteractionPartner(actor,out partner));
        Assert.False(sim.Actions.TryGetInteractionPartner(-1,out partner));
        sim.Config.Rules.PeaceMode=true;sim.Agents.SetAction(actor,ActionKind.Attack,ActionPhase.Executing);
        Assert.False(sim.Actions.TryGetInteractionPartner(actor,out partner));
    }
}
