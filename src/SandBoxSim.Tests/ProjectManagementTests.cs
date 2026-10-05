using System;
using System.Linq;
using SandBoxSim.Core;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Save;
using SandBoxSim.Core.Systems;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;
public sealed class ProjectManagementTests
{
    [Fact("森林工程拒绝无适用地形，资源维护不会在不适用地形空扣费用")]
    public void UnsuitableResourceTerrainIsRejected()
    {
        var sim = World(); var p = new LandProjects();
        for (int y = 0; y < 20; y++) for (int x = 0; x < 24; x++) { sim.World.SetTerrain(x, y, TerrainKind.Farmland); }
        Assert.False(p.CanQueue(sim, 3, 10, 10, 3)); Assert.Null(p.Queue(sim, 3, 10, 10, 3));
        var plan = Finished(sim, p, 0); p.SetPolicy(sim, plan.Id, 1);
        for (int y = 0; y < 20; y++) for (int x = 0; x < 24; x++) { sim.World.SetTerrain(x, y, TerrainKind.Desert); }
        sim.InterveneSpawnHumans(18, 18, 5, 0); var trial = new WorldTrial(); Assert.True(trial.Start(sim, 0));
        Day(sim, p, trial); Assert.Equal(120f, trial.Influence); Assert.Equal(0, plan.CareDays); Assert.Equal(1, plan.SkippedDays);
    }
    private static Simulation World()
    {
        var config = new SimConfig(); config.Fire.Enabled = false;
        var sim = new Simulation(config, 24, 20, 1);
        for (int y = 0; y < 20; y++) for (int x = 0; x < 24; x++)
        {
            sim.InterveneSetTerrain(x, y, TerrainKind.Grass);
            sim.World.SetMoisture(x, y, .6f); sim.World.SetFertility(x, y, .6f);
        }
        return sim;
    }
    private static void Day(Simulation sim, LandProjects p, WorldTrial? t = null) { sim.Tick(sim.Config.Clock.TicksPerDay); p.Advance(sim, t); }
    private static LandProject Finished(Simulation sim, LandProjects p, int kind = 0)
    {
        var plan = p.Queue(sim, kind, 10, 10, 3)!;
        for (int i = 0; i < plan.Duration; i++) { Day(sim, p); } return plan;
    }
    [Fact("新工程实际铺设迁徙通道和林粮镶嵌")]
    public void NewRecipesChangeTerrain()
    {
        var sim = World(); var p = new LandProjects(); Finished(sim, p, 4);
        Assert.Equal(TerrainKind.Road, sim.World.TerrainAt(10, 10)); Assert.Equal(0f, sim.World.TileAt(10, 10).Vegetation);
        var other = World(); var m = new LandProjects(); Finished(other, m, 5);
        Assert.True(other.World.Tiles.Any(t => t.Terrain == TerrainKind.Forest));
        Assert.True(other.World.Tiles.Any(t => t.Terrain == TerrainKind.Grass && t.Resource.Kind == ResourceKind.Food));
    }
    [Fact("配方日阶段由 JSON 决定，存档嵌入配方而非依赖当前资源文件")]
    public void CustomRecipeDurationAndSave()
    {
        var root = JsonParser.Parse(ProjectCatalog.DefaultJson); var recipe = root.Get("projects")[0];
        recipe.Set("steps", JsonValue.Array().Add(JsonValue.Object().Set("action", JsonValue.From("wetland")).Set("amount", JsonValue.From(.1f))));
        recipe.Set("name", JsonValue.From("自定义湿润工程"));
        var sim = World(); var projects = new LandProjects(ProjectCatalog.Parse(root.ToJson())); var plan = Finished(sim, projects);
        Assert.Equal(1, plan.Stage); Assert.False(plan.Active);
        var loaded = LandProjects.Decode(JsonParser.Parse(projects.Encode().ToJson()));
        Assert.Equal("自定义湿润工程", loaded.Recipe(0).Name); Assert.Equal(1, loaded.Items[0].Duration);
        Assert.True(loaded.SetPolicy(sim, plan.Id, 1));
    }
    [Fact("配方拒绝未知动作、重复 key 和无效数值")]
    public void InvalidRecipesAreRejected()
    {
        foreach (int variant in Enumerable.Range(0, 3))
        {
            var root = JsonParser.Parse(ProjectCatalog.DefaultJson); var recipe = root.Get("projects")[0];
            if (variant == 0) { recipe.Get("steps")[0].Set("action", JsonValue.From("teleport_city")); }
            if (variant == 1) { recipe.Set("key", JsonValue.From("wetland")); }
            if (variant == 2) { recipe.Get("care").Set("minMoisture", JsonValue.From(2)); }
            bool rejected = false; try { ProjectCatalog.Parse(root.ToJson()); } catch (ArgumentException) { rejected = true; }
            Assert.True(rejected);
        }
    }
    [Fact("生态维护要求适用土壤，湿地工程能解除粮地的水分限制")]
    public void WetlandSupportsFoodManagement()
    {
        var sim = World(); var projects = new LandProjects(); var food = Finished(sim, projects);
        Assert.True(projects.SetPolicy(sim, food.Id, 1));
        for (int y = 7; y <= 13; y++) for (int x = 7; x <= 13; x++) { sim.World.SetMoisture(x, y, 0); }
        Day(sim, projects); Assert.Equal(0, food.CareDays); Assert.Equal(1, food.SkippedDays);
        sim.World.SetFertility(10, 10, .6f);
        projects.Queue(sim, 2, 10, 10, 3);
        for (int i = 0; i < 3; i++) { Day(sim, projects); }
        Assert.Greater(food.CareDays, 0);
    }
    [Fact("资源优先有真实土壤代价，关闭政策会停止后续维护")]
    public void ExtractionTradeoffAndStop()
    {
        var sim = World(); var p = new LandProjects(); var plan = Finished(sim, p);
        Assert.True(p.SetPolicy(sim, plan.Id, 2));
        sim.World.SetMoisture(10, 10, .8f); sim.World.SetFertility(10, 10, .8f);
        // Isolate the project effect from weather and ecology tick updates.
        sim.Tick(sim.Config.Clock.TicksPerDay);
        float moisture = sim.World.TileAt(10, 10).Moisture, fertility = sim.World.TileAt(10, 10).Fertility;
        p.Advance(sim); Assert.Equal(1, plan.CareDays);
        Assert.True(sim.World.TileAt(10, 10).Moisture < moisture); Assert.True(sim.World.TileAt(10, 10).Fertility < fertility);
        Assert.True(p.SetPolicy(sim, plan.Id, 0)); Day(sim, p); Assert.Equal(1, plan.CareDays);
    }
    [Fact("维护额度不足时不施加效果，不重复扣款")]
    public void InsufficientBudgetPausesCare()
    {
        var sim = World(); var p = new LandProjects(); var plan = Finished(sim, p, 2);
        p.SetPolicy(sim, plan.Id, 1); sim.InterveneSpawnHumans(18, 18, 5, 0);
        var trial = new WorldTrial(); Assert.True(trial.Start(sim, 0)); trial.TrySpendPoints(120);
        sim.Tick(sim.Config.Clock.TicksPerDay); string before = StateHash.ComputeDigest(sim); p.Advance(sim, trial);
        Assert.Equal(before, StateHash.ComputeDigest(sim)); Assert.Equal(0, plan.CareDays); Assert.Equal(1, plan.SkippedDays);
        Assert.Equal(0f, trial.Influence); p.Advance(sim, trial); Assert.Equal(1, plan.SkippedDays);
    }
    [Fact("持续管理读档后逐日物理效果、预算和报告完全一致")]
    public void ManagedSaveContinuation()
    {
        var sim = World(); var p = new LandProjects(); var plan = Finished(sim, p, 2);
        p.SetPolicy(sim, plan.Id, 1); sim.InterveneSpawnHumans(18, 18, 5, 0);
        var trial = new WorldTrial(); Assert.True(trial.Start(sim, 0));
        var other = Simulation.CreateForRestore(sim.Config.Clone(), 24, 20, 1);
        Assert.True(SaveLoader.Load(other, SaveFile.Encode(sim)).DigestMatches);
        var q = LandProjects.Decode(JsonParser.Parse(p.Encode().ToJson())); var otherTrial = WorldTrial.Decode(JsonParser.Parse(trial.Encode().ToJson()));
        string frozen = plan.After.Encode().ToJson();
        for (int i = 0; i < 4; i++)
        {
            Day(sim, p, trial); Day(other, q, otherTrial);
            Assert.Equal(StateHash.ComputeDigest(sim), StateHash.ComputeDigest(other));
            Assert.Equal(p.Encode().ToJson(), q.Encode().ToJson()); Assert.Equal(trial.Encode().ToJson(), otherTrial.Encode().ToJson());
        }
        Assert.Equal(frozen, plan.After.Encode().ToJson());
    }
    [Fact("管理名额有限，切换无效政策不改变世界")]
    public void ManagementCapacityAndPurity()
    {
        var sim = World(); var p = new LandProjects();
        for (int i = 0; i < 4; i++) { p.Queue(sim, 2, 10, 10, 2); }
        for (int i = 0; i < 3; i++) { Day(sim, p); }
        foreach (var plan in p.Items) { Assert.True(p.SetPolicy(sim, plan.Id, 1)); }
        var extra = Finished(sim, p, 2); string before = StateHash.ComputeDigest(sim);
        Assert.False(p.SetPolicy(sim, extra.Id, 1)); Assert.False(p.SetPolicy(sim, extra.Id, 99));
        Assert.Equal(before, StateHash.ComputeDigest(sim)); Assert.Equal(4, p.ManagedCount);
    }
}
