using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Systems;
using SandBoxSim.Core.Systems.Actions;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

public sealed class M7RepairTests
{
    private static Simulation World()
    {
        var config = new SimConfig();
        config.World.Width = 100;
        config.World.Height = 100;
        var sim = new Simulation(config, 100, 100, 7111);
        for (int y = 0; y < 100; y++)
            for (int x = 0; x < 100; x++)
                sim.World.SetTerrain(x, y, TerrainKind.Grass);
        return sim;
    }

    private static void Place(Simulation sim, BuildingKind kind, int x, int y)
    {
        int index = sim.Buildings.Place(sim.World, kind, x, y, 1);
        Assert.True(index >= 0);
        sim.Buildings.MarkComplete(index, 0);
    }

    private static void Group(Simulation sim, int x, int y)
    {
        sim.InterveneSpawnHumans(x, y, 8, 0);
        Place(sim, BuildingKind.House, x + 1, y + 1);
        Place(sim, BuildingKind.House, x + 3, y + 1);
        Place(sim, BuildingKind.Storage, x + 1, y + 3);
    }

    [Fact("人口已有足够住房时仍必须为缺失的仓库采集木石")]
    public void WarehouseDemandSurvivesHousingSaturation()
    {
        var sim = World();
        sim.InterveneSpawnHumans(10, 10, 40, 0);
        for (int i = 0; i < 30; i++) { Place(sim, BuildingKind.House, 30 + i, 30); }
        int slot = sim.Agents.LiveSlotsRaw(out int _)[0];
        sim.Agents.SetInventory(slot, ResourceKind.Wood, 0);
        sim.Agents.SetInventory(slot, ResourceKind.Stone, 0);
        var ctx = new ActionContext { Store = sim.Agents, Slot = slot, Buildings = sim.Buildings, GroundStocks = sim.GroundStocks };
        Assert.True(ActionSearch.BuildDemand01(in ctx, ResourceKind.Wood) > 0);
        Assert.True(ActionSearch.BuildDemand01(in ctx, ResourceKind.Stone) > 0);
        Place(sim, BuildingKind.Storage, 20, 20);
        Assert.Near(0, ActionSearch.BuildDemand01(in ctx, ResourceKind.Wood));
        Assert.Near(0, ActionSearch.BuildDemand01(in ctx, ResourceKind.Stone));
    }

    [Fact("两个相距很远的人群必须各自累积持续共处天数")]
    public void TwoGroupsFoundIndependently()
    {
        var sim = World();
        Group(sim, 10, 10);
        Group(sim, 75, 75);
        for (int day = 1; day < sim.Config.Settlement.FoundDays; day++)
        {
            sim.Settlements.TickDay(day * 1440);
            Assert.Equal(0, sim.Settlements.ActiveCount);
        }
        sim.Settlements.TickDay(sim.Config.Settlement.FoundDays * 1440);
        Assert.Equal(2, sim.Settlements.ActiveCount);
    }

    [Fact("相邻网格中的两个独立设施人群不能被网格邻接强行合并")]
    public void AdjacentGridCellsDoNotMergeSeparateCommunities()
    {
        var sim = World();
        Group(sim, 10, 10);
        Group(sim, 42, 10);
        for (int day = 1; day <= sim.Config.Settlement.FoundDays; day++)
            sim.Settlements.TickDay(day * 1440);
        Assert.Equal(2, sim.Settlements.ActiveCount);
        int counted = 0;
        for (int i = 0; i < sim.Settlements.EntityCount; i++)
            counted += sim.Settlements.At(i).Population;
        Assert.Equal(sim.Agents.LiveCount, counted);
    }

    [Fact("居民在同一共享设施附近活动不能因为质心跳动清零共处天数")]
    public void SharedFacilitiesKeepCandidateStable()
    {
        var sim = World();
        Group(sim, 30, 30);
        int[] slots = sim.Agents.LiveSlotsRaw(out int count);
        for (int day = 1; day <= sim.Config.Settlement.FoundDays; day++)
        {
            for (int i = 0; i < count; i++)
                sim.Agents.SetPosition(slots[i], day % 2 == 0 ? 36 : 24, 30);
            sim.Settlements.TickDay(day * 1440);
        }
        Assert.Equal(1, sim.Settlements.ActiveCount);
    }

    [Fact("已开工的仓库和住房必须计入缺口，不能重复开建")]
    public void PendingBuildingsCloseDemand()
    {
        var sim = World();
        sim.InterveneSpawnHumans(10, 10, 8, 0);
        int slot = sim.Agents.LiveSlotsRaw(out int _)[0];
        var ctx = new ActionContext { Store = sim.Agents, Slot = slot, Buildings = sim.Buildings, GroundStocks = sim.GroundStocks };
        Assert.True(ActionSearch.StorageDemand01(in ctx) > 0);
        int storage = sim.Buildings.Place(sim.World, BuildingKind.Storage, 20, 20, 100);
        Assert.True(storage >= 0);
        Assert.Near(0, ActionSearch.StorageDemand01(in ctx));
        int house = sim.Buildings.Place(sim.World, BuildingKind.House, 25, 20, 100);
        Assert.True(house >= 0);
        Assert.Equal(BuildingRegistry.Of(BuildingKind.House).Beds, ActionSearch.PlannedBeds(sim.Buildings));
    }

    [Fact("已选建造动作在执行前需求被满足时不得继续重复开工")]
    public void BuilderRechecksDemandBeforeStarting()
    {
        var sim = World();
        Group(sim, 10, 10);
        int slot = sim.Agents.LiveSlotsRaw(out int _)[0];
        sim.Agents.SetInventory(slot, ResourceKind.Wood, 100);
        sim.Agents.SetInventory(slot, ResourceKind.Stone, 20);
        sim.Agents.SetAction(slot, ActionKind.BuildStorage, ActionPhase.Executing);
        sim.Agents.SetTarget(slot, 20, 20);
        sim.Agents.SetPhase(slot, ActionPhase.Executing);
        sim.Actions.Tick(1);
        Assert.Equal(0L, sim.Actions.BuildsStarted);
        Assert.Near(100, sim.Agents.InventoryOf(slot, ResourceKind.Wood));
        Assert.Near(20, sim.Agents.InventoryOf(slot, ResourceKind.Stone));
        Assert.Equal(ActionPhase.Failed, sim.Agents.PhaseOf(slot));
    }

    [Fact("建造选址必须找到材料可达的工地，并按同一坐标扣料")]
    public void FundedSiteMatchesActualConstruction()
    {
        var sim = World();
        sim.InterveneSpawnHumans(10, 10, 8, 0);
        sim.World.SetTerrain(10, 12, TerrainKind.Water);
        sim.Config.GroundStocks.SearchRadius = 12;
        int slot = sim.Agents.LiveSlotsRaw(out int _)[0];
        sim.Agents.SetInventory(slot, ResourceKind.Wood, 0);
        sim.GroundStocks.Deposit(24, 10, ResourceKind.Wood, 20, sim.Config.GroundStocks);
        var ctx = new ActionContext { World = sim.World, Store = sim.Agents, Slot = slot, X = 10, Y = 10,
            Config = sim.Config, Ai = sim.Config.Ai, Buildings = sim.Buildings, GroundStocks = sim.GroundStocks, Storage = sim.Storage };
        Assert.True(BuildAction.SelectSite(in ctx, BuildingKind.House, out Int2 site, out float _));
        Assert.True(site.X >= 12, "最近空地材料不可达时必须继续寻找已备料的工地");
        Assert.True(sim.BuildingSystem.TryStartBuilding(sim.Agents, slot, BuildingKind.House, site.X, site.Y, out int _, out string _));
        Assert.Near(0, sim.GroundStocks.TotalOf(ResourceKind.Wood));
    }

    [Fact("地图上有材料但所有候选工地都够不到时不能产生建造效用")]
    public void UnreachableMaterialsCannotDriveBuildLoop()
    {
        var sim = World();
        sim.InterveneSpawnHumans(10, 10, 8, 0);
        sim.World.SetTerrain(10, 12, TerrainKind.Water);
        int slot = sim.Agents.LiveSlotsRaw(out int _)[0];
        sim.Agents.SetInventory(slot, ResourceKind.Wood, 0);
        sim.GroundStocks.Deposit(80, 80, ResourceKind.Wood, 20, sim.Config.GroundStocks);
        var ctx = new ActionContext { World = sim.World, Store = sim.Agents, Slot = slot, X = 10, Y = 10,
            Config = sim.Config, Ai = sim.Config.Ai, Buildings = sim.Buildings, GroundStocks = sim.GroundStocks, Storage = sim.Storage };
        Assert.False(BuildAction.SelectSite(in ctx, BuildingKind.House, out Int2 _, out float _));
        Assert.Near(0, BuildHouseAction.Evaluate(in ctx).Utility);
        Assert.Near(20, sim.GroundStocks.TotalOf(ResourceKind.Wood));
    }

    [Fact("携带建材不能降低缺粮者的采食效用，仓库造价必须容得下")]
    public void MaterialsCannotBlockFoodOrWarehousePreparation()
    {
        var sim = World();
        sim.InterveneSpawnHumans(10, 10, 8, 0);
        int slot = sim.Agents.LiveSlotsRaw(out int _)[0];
        sim.Agents.SetInventory(slot, ResourceKind.Food, 0);
        sim.Agents.SetInventory(slot, ResourceKind.Wood, 0);
        sim.Agents.SetInventory(slot, ResourceKind.Stone, 0);
        var ctx = new ActionContext { World = sim.World, Store = sim.Agents, Slot = slot, X = 10, Y = 10,
            Config = sim.Config, Ai = sim.Config.Ai, Buildings = sim.Buildings, GroundStocks = sim.GroundStocks, Storage = sim.Storage };
        float foodBefore = GatherFoodAction.Evaluate(in ctx).Utility;
        BuildingRecipe recipe = BuildingRegistry.Of(BuildingKind.Storage);
        sim.Agents.SetInventory(slot, ResourceKind.Wood, recipe.WoodCost);
        sim.Agents.SetInventory(slot, ResourceKind.Stone, recipe.StoneCost);
        Assert.Near(foodBefore, GatherFoodAction.Evaluate(in ctx).Utility);
        Assert.Near(0, ActionSearch.Overstock01(in ctx));
    }

    [Fact("实际减少人口到滞回带时保持聚落，降至解散阈值时只解散一次")]
    public void HysteresisUsesActualPopulation()
    {
        var sim = World();
        Group(sim, 10, 10);
        for (int day = 1; day <= 5; day++) { sim.Settlements.TickDay(day * 1440); }
        var slots = new System.Collections.Generic.List<int>(sim.Agents.AliveSlots());
        for (int i = 0; i < 4; i++) { sim.Agents.MarkDead(slots[i], DeathCause.Starvation, 7200); }
        Assert.Equal(4, sim.Agents.LiveCount);
        for (int day = 6; day <= 10; day++) { sim.Settlements.TickDay(day * 1440); }
        Assert.Equal(1, sim.Settlements.ActiveCount);
        Assert.Equal(1, sim.Settlements.TotalFounded);
        Assert.Equal(0, sim.Settlements.TotalDissolved);
        sim.Agents.MarkDead(slots[4], DeathCause.Starvation, 14400);
        sim.Settlements.TickDay(15840);
        sim.Settlements.TickDay(17280);
        Assert.Equal(0, sim.Settlements.ActiveCount);
        Assert.Equal(1, sim.Settlements.TotalDissolved);
    }

    [Fact("持续共处被打断后不能把间隔前的天数继续累加")]
    public void BrokenCohabitationResetsCandidate()
    {
        var sim = World();
        Group(sim, 10, 10);
        sim.Settlements.TickDay(1440);
        sim.Settlements.TickDay(2880);
        sim.Settlements.TickDay(4320);
        int[] slots = sim.Agents.LiveSlotsRaw(out int count);
        for (int i = 0; i < count; i++) { sim.Agents.SetPosition(slots[i], 75, 75); }
        sim.Settlements.TickDay(5760);
        for (int i = 0; i < count; i++) { sim.Agents.SetPosition(slots[i], 10, 10); }
        sim.Settlements.TickDay(7200);
        sim.Settlements.TickDay(8640);
        Assert.Equal(0, sim.Settlements.ActiveCount);
    }

    [Fact("居民全部离开原聚落后不能永久保留幽灵聚落")]
    public void EmptySettlementDissolves()
    {
        var sim = World();
        Group(sim, 10, 10);
        for (int day = 1; day <= sim.Config.Settlement.FoundDays; day++) { sim.Settlements.TickDay(day * 1440); }
        Assert.Equal(1, sim.Settlements.ActiveCount);
        int[] slots = sim.Agents.LiveSlotsRaw(out int count);
        for (int i = 0; i < count; i++) { sim.Agents.SetPosition(slots[i], 75, 75); }
        sim.Settlements.TickDay(8640);
        Assert.Equal(0, sim.Settlements.ActiveCount);
        Assert.Equal(1, sim.Settlements.TotalDissolved);
    }

    [Fact("多个聚落候选的持续性状态必须完整存档并保持续跑一致")]
    public void MultipleCandidatesSurviveSaveLoad()
    {
        var sim = World();
        Group(sim, 10, 10);
        Group(sim, 75, 75);
        sim.Settlements.TickDay(1440);
        sim.Settlements.TickDay(2880);
        var restored = Simulation.CreateForRestore(sim.Config, 100, 100, 9999);
        Assert.True(restored.LoadFromText(sim.SaveToText()).Success);
        Assert.Equal(sim.StateDigestString(), restored.StateDigestString());
        for (int day = 3; day <= 5; day++)
        {
            sim.Settlements.TickDay(day * 1440);
            restored.Settlements.TickDay(day * 1440);
            Assert.Equal(sim.StateDigestString(), restored.StateDigestString());
        }
        Assert.Equal(2, restored.Settlements.ActiveCount);
    }
}
