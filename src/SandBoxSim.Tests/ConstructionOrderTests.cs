using System.Linq;
using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Save;
using SandBoxSim.Core.Systems;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;
public sealed class ConstructionOrderTests
{
    [Fact("住房选址可重复，靠近道路而不占用道路，不再固定扫描方向")]
    public void SitePreferenceIsDeterministic()
    {
        var sim = World(); sim.Config.Buildings.OrganicHousing = true; int slot = sim.Agents.AliveSlots().First();
        sim.InterveneSetTerrain(14, 12, TerrainKind.Road);
        var ctx = new ActionContext { World = sim.World, Store = sim.Agents, Slot = slot, X = 12, Y = 12,
            Config = sim.Config, Ai = sim.Config.Ai, Buildings = sim.Buildings, Storage = sim.Storage, GroundStocks = sim.GroundStocks };
        string before = StateHash.ComputeDigest(sim);
        Assert.True(SandBoxSim.Core.Systems.Actions.BuildAction.SelectSite(in ctx, BuildingKind.House, out var first, out float _));
        Assert.True(SandBoxSim.Core.Systems.Actions.BuildAction.SelectSite(in ctx, BuildingKind.House, out var second, out float _));
        Assert.Equal(first, second); Assert.Equal(13, first.X); Assert.Equal(12, first.Y);
        Assert.Equal(before, StateHash.ComputeDigest(sim));
    }
    private static Simulation World()
    {
        var sim = new Simulation(new SimConfig(), 24, 24, 913);
        for (int y = 0; y < 24; y++) for (int x = 0; x < 24; x++) sim.InterveneSetTerrain(x, y, TerrainKind.Grass);
        sim.InterveneSetTerrain(10, 9, TerrainKind.Water);
        sim.InterveneSpawnHumans(12, 12, 1, 0);
        int slot = sim.Agents.AliveSlots().First(); sim.Agents.SetLifeStage(slot, LifeStage.Adult);
        sim.Agents.SetInventory(slot, ResourceKind.Wood, 100); sim.Agents.SetInventory(slot, ResourceKind.Stone, 100);
        return sim;
    }
    [Fact("建造委托扣除真实材料，产生工地，完成后提供床位")]
    public void RealMaterialsAndBeds()
    {
        var sim = World(); int slot = sim.Agents.AliveSlots().First();
        Assert.True(ConstructionOrders.TryStart(sim, BuildingKind.House, 10, 10, out int index, out string _));
        Assert.Equal(100 - BuildingRegistry.Of(BuildingKind.House).WoodCost, sim.Agents.InventoryOf(slot, ResourceKind.Wood));
        Assert.Equal(BuildingState.UnderConstruction, sim.Buildings.StateOf(index));
        for (int tick = 0; tick < 1000; tick += 10) sim.BuildingSystem.TickFast(tick);
        Assert.Equal(BuildingState.Complete, sim.Buildings.StateOf(index));
        Assert.True(sim.Buildings.TotalBeds > 0);
        var restored = Simulation.CreateForRestore(sim.Config, 24, 24, 913);
        Assert.True(SaveLoader.Load(restored, SaveFile.Encode(sim)).Success);
        Assert.Equal(StateHash.ComputeDigest(sim), StateHash.ComputeDigest(restored));
    }
    [Fact("没有材料、成年居民、合适地形时委托没有副作用")]
    public void RejectedOrdersArePure()
    {
        foreach (int variant in new[] { 0, 1, 2, 3 })
        {
            var sim = World(); int slot = sim.Agents.AliveSlots().First();
            if (variant == 0) { sim.Agents.SetInventory(slot, ResourceKind.Wood, 0); sim.Agents.SetInventory(slot, ResourceKind.Stone, 0); }
            if (variant == 1) sim.Agents.SetLifeStage(slot, LifeStage.Child);
            if (variant == 2) sim.InterveneSetTerrain(10, 10, TerrainKind.Water);
            string before = StateHash.ComputeDigest(sim);
            Assert.False(ConstructionOrders.TryStart(sim, BuildingKind.House, variant == 3 ? 23 : 10, variant == 3 ? 23 : 10, out int _, out string reason));
            Assert.True(reason.Length > 0); Assert.Equal(before, StateHash.ComputeDigest(sim));
        }
    }
    [Fact("隔水工地必须可达，农田必须邻水，失败不会扣材料")]
    public void AccessAndIrrigation()
    {
        var sim = World();
        Assert.True(ConstructionOrders.TryStart(sim, BuildingKind.Farm, 11, 9, out int _, out string _));
        string before = StateHash.ComputeDigest(sim);
        Assert.False(ConstructionOrders.TryStart(sim, BuildingKind.Farm, 15, 15, out int _, out string _));
        Assert.Equal(before, StateHash.ComputeDigest(sim));
        var island = World();
        for (int y = 9; y <= 11; y++) for (int x = 9; x <= 11; x++) if (x != 10 || y != 10) island.InterveneSetTerrain(x, y, TerrainKind.Water);
        before = StateHash.ComputeDigest(island);
        Assert.False(ConstructionOrders.TryStart(island, BuildingKind.House, 10, 10, out int _, out string reason));
        Assert.True(reason.Contains("到达")); Assert.Equal(before, StateHash.ComputeDigest(island));
    }
}
