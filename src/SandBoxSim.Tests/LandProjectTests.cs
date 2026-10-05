using System.Linq;
using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Save;
using SandBoxSim.Core.Systems;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

public sealed class LandProjectTests
{
    [Fact("没有有效土地时拒绝工程，不改变历史和资源")]
    public void InvalidGroundIsRejected()
    {
        var sim = World(); var projects = new LandProjects();
        for (int y = 0; y < 20; y++) for (int x = 0; x < 24; x++) { sim.World.SetTerrain(x, y, TerrainKind.Water); }
        string before = StateHash.ComputeDigest(sim);
        Assert.False(projects.CanQueue(sim, 0, 10, 10, 4)); Assert.Null(projects.Queue(sim, 0, 10, 10, 4));
        Assert.Equal(before, StateHash.ComputeDigest(sim)); Assert.Equal(0, projects.ActiveCount);
    }
    [Fact("工程排队不立即改变地形或强制产生社会结果")]
    public void QueueDoesNotApplyEarly()
    {
        var sim = World(); var projects = new LandProjects(); var tiles = sim.World.Tiles.ToArray();
        Assert.NotNull(projects.Queue(sim, 0, 10, 10, 4)); projects.Advance(sim);
        Assert.True(tiles.SequenceEqual(sim.World.Tiles)); Assert.Equal(0, sim.Buildings.TotalCompleted); Assert.Equal(0, sim.Settlements.ActiveCount);
    }
    [Fact("工程按三个日边界生效；完成后的报告不随世界继续漂移")]
    public void StagesAndReportsStayStable()
    {
        var sim = World(); var projects = new LandProjects(); var plan = projects.Queue(sim, 0, 10, 10, 4)!;
        Day(sim, projects); Assert.Equal(1, plan.Stage); Assert.Equal(TerrainKind.Grass, sim.World.TerrainAt(10, 10));
        Day(sim, projects); Assert.Equal(2, plan.Stage); Assert.True(sim.World.TileAt(10, 10).Resource.Amount > 0);
        Day(sim, projects); Assert.Equal(3, plan.Stage); Assert.False(plan.Active);
        string result = plan.After.Encode().ToJson(); PlayerTools.Apply(sim, PlayerTool.Meteor, 10, 10, 4, 100); Day(sim, projects);
        Assert.Equal(result, plan.After.Encode().ToJson());
    }
    [Fact("生态工程保留水域和既有建筑")]
    public void BuildingsAndWaterArePreserved()
    {
        foreach (int kind in Enumerable.Range(0, 4))
        {
            var sim = World(); var projects = new LandProjects();
            sim.World.SetTerrain(9, 10, TerrainKind.Water);
            int house = sim.Buildings.Place(sim.World, BuildingKind.House, 10, 10, 1); sim.Buildings.MarkComplete(house, sim.Clock);
            var tile = sim.World.TileAt(10, 10);
            projects.Queue(sim, kind, 10, 10, 4);
            for (int i = 0; i < 3; i++) { Day(sim, projects); }
            Assert.Equal(TerrainKind.Water, sim.World.TerrainAt(9, 10)); Assert.Equal(tile.Terrain, sim.World.TerrainAt(10, 10));
            Assert.True(sim.Buildings.IsAlive(house)); Assert.Equal(house + 1, sim.World.TileAt(10, 10).BuildingId);
        }
    }
    [Fact("防火走廊确实清除燃料，湿地修复改善土壤")]
    public void PlansChangeConditions()
    {
        var sim = World(); var projects = new LandProjects(); sim.InterveneGrowForest(10, 10, 6, 1);
        projects.Queue(sim, 1, 10, 10, 5); for (int i = 0; i < 3; i++) { Day(sim, projects); }
        Assert.Equal(TerrainKind.Road, sim.World.TerrainAt(10, 10)); Assert.Equal(0f, sim.World.TileAt(10, 10).Vegetation);
        sim.World.SetTerrain(10, 10, TerrainKind.Grass); sim.World.SetMoisture(10, 10, 0); sim.World.SetFertility(10, 10, 0);
        var wet = projects.Queue(sim, 2, 10, 10, 2)!; for (int i = 0; i < 3; i++) { Day(sim, projects); }
        Assert.Greater(wet.After.Moisture, wet.Before.Moisture); Assert.Greater(sim.World.TileAt(10, 10).Fertility, 0f);
    }
    [Fact("取消只停止未完成阶段，不重复施加效果")]
    public void CancelStopsFutureWork()
    {
        var sim = World(); var projects = new LandProjects(); var plan = projects.Queue(sim, 0, 10, 10, 3)!;
        Day(sim, projects); Assert.True(projects.Cancel(sim, plan.Id)); var stage = plan.Stage;
        for (int i = 0; i < 3; i++) { Day(sim, projects); }
        Assert.Equal(stage, plan.Stage); Assert.True(plan.Cancelled); Assert.False(projects.Cancel(sim, plan.Id));
    }
    [Fact("工程读档续跑保持核心摘要和工程记录一致")]
    public void ProjectSaveContinuation()
    {
        var sim = World(); var projects = new LandProjects();
        for (int kind = 0; kind < 4; kind++) { projects.Queue(sim, kind, 5 + kind * 4, 12, 2); }
        Day(sim, projects);
        var restored = Simulation.CreateForRestore(sim.Config.Clone(), 24, 20, 1);
        Assert.True(SaveLoader.Load(restored, SaveFile.Encode(sim)).DigestMatches);
        var loaded = LandProjects.Decode(JsonParser.Parse(projects.Encode().ToJson()));
        for (int i = 0; i < 4; i++)
        {
            Day(sim, projects); Day(restored, loaded);
            Assert.Equal(StateHash.ComputeDigest(sim), StateHash.ComputeDigest(restored));
            Assert.Equal(projects.Encode().ToJson(), loaded.Encode().ToJson());
        }
    }
    [Fact("排队上限与边界有效，空世界的观测不改写状态")]
    public void CapacityAndObservation()
    {
        var sim = World(); var projects = new LandProjects();
        for (int i = 0; i < 4; i++) { Assert.NotNull(projects.Queue(sim, 0, 10, 10, 4)); }
        Assert.Null(projects.Queue(sim, 0, 10, 10, 4)); Assert.Null(projects.Queue(sim, 2, -1, 3, 4));
        string before = StateHash.ComputeDigest(sim); LocalConditions.Observe(sim, 0, 0, 8); WorldAlerts.Observe(sim);
        Assert.Equal(before, StateHash.ComputeDigest(sim));
    }
    [Fact("局势卡片按实际火情、疫病与需求排序")]
    public void AlertsUsePhysicalRisks()
    {
        var sim = World(); sim.InterveneSpawnHumans(10, 10, 5, 1);
        int slot = sim.Agents.AliveSlots().First(); sim.Agents.SetHunger(slot, .9f); sim.Diseases.Infect(slot);
        sim.InterveneGrowForest(8, 8, 1, 1); sim.Fire.Ignite(8, 8, sim.Clock, "测试");
        string before = StateHash.ComputeDigest(sim); var alerts = WorldAlerts.Observe(sim);
        Assert.Equal("fire", alerts[0].Key); Assert.Equal("disease", alerts[1].Key);
        Assert.True(alerts.Any(a => a.Key == "food")); Assert.Equal(before, StateHash.ComputeDigest(sim));
    }
    private static Simulation World()
    {
        var sim = new Simulation(new SimConfig(), 24, 20, 71); sim.Config.Fire.Enabled = false;
        for (int y = 0; y < 20; y++) for (int x = 0; x < 24; x++) { sim.World.SetTerrain(x, y, TerrainKind.Grass); sim.World.ApplyDefaultResource(x, y); }
        sim.World.RefreshSpatialIndex(); return sim;
    }
    private static void Day(Simulation sim, LandProjects projects) { sim.Tick(sim.Config.Clock.TicksPerDay); projects.Advance(sim); }
}
