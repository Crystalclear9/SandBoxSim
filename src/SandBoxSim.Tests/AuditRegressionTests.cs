using SandBoxSim.Core;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Systems;
using SandBoxSim.Core.Systems.Actions;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

public sealed class AuditRegressionTests
{
    private static Simulation World()
    {
        var config = new SimConfig();
        config.Buildings.DecayGraceDays = 0;
        config.Buildings.DecayPerDay = 2;
        var sim = new Simulation(config, 32, 32, 4242);
        for (int y = 1; y < 31; y++)
            for (int x = 1; x < 31; x++)
                sim.World.SetTerrain(x, y, TerrainKind.Grass);
        sim.World.SetTerrain(10, 11, TerrainKind.Water);
        return sim;
    }

    private static int Complete(Simulation sim, BuildingKind kind, int x, int y)
    {
        int index = sim.Buildings.Place(sim.World, kind, x, y, 1);
        Assert.True(index >= 0);
        sim.BuildingSystem.TickFast(10);
        return index;
    }

    [Fact("仓库真实完工流程必须启用库存容量")]
    public void CompletedWarehouseAcceptsResources()
    {
        var sim = World();
        int index = Complete(sim, BuildingKind.Storage, 12, 10);
        Assert.Equal(BuildingRegistry.Of(BuildingKind.Storage).StorageCapacity, sim.Storage.CapacityOf(index));
        Assert.Near(10, sim.Storage.Deposit(index, ResourceKind.Food, 10));
    }

    [Fact("当天耕种的农田不应被当成无人维护拆除")]
    public void WorkedFarmSurvivesDailyMaintenance()
    {
        var sim = World();
        int farm = Complete(sim, BuildingKind.Farm, 10, 10);
        sim.Buildings.AddLabor(farm, 10);
        sim.BuildingSystem.TickDay(1440);
        Assert.True(sim.Buildings.IsAlive(farm));
        Assert.Near(0, sim.Buildings.LaborOf(farm));
        sim.BuildingSystem.TickDay(2880);
        Assert.False(sim.Buildings.IsAlive(farm), "随后无人耕种仍应发生衰减");
    }

    [Fact("仓库满后农田产出溢出到地面，不能凭空消失")]
    public void FarmYieldOverflowsFullWarehouse()
    {
        var sim = World();
        sim.Config.Buildings.DecayPerDay = 0;
        int storage = Complete(sim, BuildingKind.Storage, 12, 10);
        sim.Storage.SetCapacity(storage, 10);
        sim.Storage.Deposit(storage, ResourceKind.Food, 10);
        int farm = Complete(sim, BuildingKind.Farm, 10, 10);
        sim.Buildings.AddLabor(farm, 10);
        sim.BuildingSystem.TickDay(1440);
        Assert.Greater(sim.GroundStocks.TotalOf(ResourceKind.Food), 0f);
        Assert.Near(sim.BuildingSystem.FoodProducedThisDay, sim.GroundStocks.TotalOf(ResourceKind.Food));
    }

    [Fact("倒塌仓库不能保留幽灵库存")]
    public void DemolishedWarehouseLosesInventory()
    {
        var sim = World();
        int storage = Complete(sim, BuildingKind.Storage, 12, 10);
        sim.Storage.SetCapacity(storage, 10);
        sim.Storage.Deposit(storage, ResourceKind.Food, 10);
        sim.BuildingSystem.TickDay(1440);
        Assert.False(sim.Buildings.IsAlive(storage));
        Assert.Near(0, sim.Storage.AmountOf(storage, ResourceKind.Food));
        Assert.Near(0, sim.Storage.CapacityOf(storage));
    }

    [Fact("重建世界后不得沿用上一局的出生和日历观测计数")]
    public void RegenerationResetsObservationCounters()
    {
        var sim = World();
        sim.Tick(1440);
        sim.Births.RestoreCounters(5);
        sim.RegenerateWorld(4242);
        Assert.Equal(0, sim.Births.TotalBirths);
        Assert.Equal(0, sim.HourEventsFired);
        Assert.Equal(0, sim.DayEventsFired);
        Assert.Equal(0L, sim.Trade.TotalQuotes);
    }

    [Fact("数值断言必须拒绝 NaN，否则模拟数值崩坏会被误报通过")]
    public void NumericAssertionsRejectNaN()
    {
        Assert.Throws<Assert.AssertionException>(() => Assert.Near(1, double.NaN));
        Assert.Throws<Assert.AssertionException>(() => Assert.InRange(double.NaN, 0, 1));
    }

    [Fact("居民能选中仓库食物并真正取回，库存总量守恒")]
    public void ResidentsCanTakeFoodFromWarehouse()
    {
        var sim = World();
        int warehouse = Complete(sim, BuildingKind.Storage, 12, 10);
        sim.Storage.Deposit(warehouse, ResourceKind.Food, 40);
        sim.InterveneSpawnHumans(12, 10, 1, 1);
        int slot = sim.Agents.LiveSlotsRaw(out int _)[0];
        sim.Agents.SetPosition(slot, 12, 10);
        sim.Agents.SetInventory(slot, ResourceKind.Food, 0);
        var ctx = new ActionContext { World = sim.World, Store = sim.Agents, Slot = slot,
            X = 12, Y = 10, Config = sim.Config, Ai = sim.Config.Ai,
            GroundStocks = sim.GroundStocks, Buildings = sim.Buildings, Storage = sim.Storage };
        Int2? target = TakeAction.SelectTarget(in ctx, sim.Pathfinder);
        Assert.True(target.HasValue);
        Assert.Equal(new Int2(12, 10), target!.Value);
        sim.Agents.SetAction(slot, ActionKind.Take, ActionPhase.Executing);
        sim.Agents.SetTarget(slot, 12, 10);
        sim.Actions.Tick(1);
        Assert.Greater(sim.Agents.InventoryOf(slot, ResourceKind.Food), 0f);
        Assert.Near(40, sim.Agents.InventoryOf(slot, ResourceKind.Food)
            + sim.Storage.AmountOf(warehouse, ResourceKind.Food));
    }
}
