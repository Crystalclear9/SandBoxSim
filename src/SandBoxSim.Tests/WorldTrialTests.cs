using System;
using System.Linq;
using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Save;
using SandBoxSim.Core.Systems;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

public sealed class WorldTrialTests
{
    [Fact("三个种子的真实玩法对照：主动救治能扭转疫病试炼结果")]
    public void GuidedAidChangesPlayableOutcomes()
    {
        foreach (int seed in new[] { 839102, 4242, 77 })
            for (int kind = 0; kind < 3; kind++)
            {
                var passive = Play(seed, kind, false); var active = Play(seed, kind, true);
                Assert.False(passive.Running); Assert.False(active.Running);
                Console.WriteLine($"  [玩法对照] seed={seed} trial={kind} 等待：成功={passive.Won} 存活={passive.Survivors} 患病={passive.Sick}；干预：成功={active.Won} 存活={active.Survivors} 患病={active.Sick} 消耗次数={active.Interventions}");
                if (kind == 2) { Assert.False(passive.Won); Assert.True(active.Won); Assert.Less(active.Sick, passive.Sick); }
            }
    }
    private static WorldTrial Play(int seed, int kind, bool aid)
    {
        var sim = SandboxScenarios.Create(0, seed, 40); var trial = new WorldTrial(); trial.Start(sim, kind);
        for (int day = 0; day < 14 && trial.Running; day++)
        {
            if (aid)
            {
                void Use(PlayerTool tool, int x, int y, int radius, float strength)
                { if (trial.TrySpend(tool, radius, strength)) { PlayerTools.Apply(sim, tool, x, y, radius, strength); } }
                trial.Refresh(sim); var p = trial.Location;
                if (day % 2 == 0) { Use(PlayerTool.Food, p.X, p.Y, 4, 35); }
                if (day == 0) { Use(PlayerTool.Wood, p.X - 3, p.Y, 3, 35); Use(PlayerTool.Stone, p.X + 3, p.Y, 3, 35); }
                if (kind == 1)
                {
                    int fires = 0;
                    for (int i = 0; i < sim.World.Tiles.Length && fires < 8; i++)
                        if (sim.World.Tiles[i].Fire == FireState.Burning) { Use(PlayerTool.Rain, i % sim.World.Width, i / sim.World.Width, 4, 70); fires++; }
                }
                if (kind == 2)
                    foreach (int slot in sim.Agents.AliveSlots().ToArray())
                        if (sim.Diseases.OfSlot(slot)?.Active == true) { Use(PlayerTool.Heal, sim.Agents.XOf(slot), sim.Agents.YOf(slot), 3, 10); }
            }
            Day(sim, trial, 1);
        }
        return trial;
    }
    [Fact("自由沙盒不会消耗额度或触发试炼事件")]
    public void FreeModeDoesNotChangeWorld()
    {
        var sim = World(); var trial = new WorldTrial(); string before = StateHash.ComputeDigest(sim);
        trial.Advance(sim); Assert.True(trial.TrySpend(PlayerTool.Meteor, 20, 100));
        Assert.Equal(before, StateHash.ComputeDigest(sim)); Assert.Equal(120f, trial.Influence);
    }
    [Fact("额度不足拒绝操作；读取预算不改写模拟")]
    public void SpendingIsAtomic()
    {
        var sim = World(); var trial = new WorldTrial(); Assert.True(trial.Start(sim, 0));
        Assert.True(trial.TrySpend(PlayerTool.Human, 0, 40)); Assert.Equal(0f, trial.Influence);
        string before = StateHash.ComputeDigest(sim);
        Assert.False(trial.TrySpend(PlayerTool.Food, 4, 35)); Assert.Equal(1, trial.Interventions);
        Assert.True(trial.TrySpend(PlayerTool.Inspect, 20, 100)); Assert.Equal(before, StateHash.ComputeDigest(sim));
    }
    [Fact("旱灾确实消耗粮食，不直接创建住房或社会结果")]
    public void DroughtChangesPhysicalResources()
    {
        var sim = World(); var trial = new WorldTrial(); trial.Start(sim, 0);
        Day(sim, trial, 3);
        PlayerTools.Apply(sim, PlayerTool.Food, trial.Location.X, trial.Location.Y, 4, 100);
        // Disable regeneration for a measurable physical-stock comparison across the next day.
        foreach (int i in Enumerable.Range(0, sim.World.Tiles.Length))
        { var node = sim.World.Tiles[i].Resource; node.RegenerationRate = 0; sim.World.SetResource(i % sim.World.Width, i / sim.World.Width, node); }
        float before = sim.World.Tiles.Sum(t => t.Resource.Kind == ResourceKind.Food ? t.Resource.Amount : 0);
        Day(sim, trial, 1);
        float after = sim.World.Tiles.Sum(t => t.Resource.Kind == ResourceKind.Food ? t.Resource.Amount : 0);
        Assert.Less(after, before); Assert.True(trial.Notice.Contains("旱季")); Assert.Equal(4, trial.Days);
    }
    [Fact("局部降雨扑灭未烧尽的火，保留植被并提高湿度")]
    public void RainMakesFireRescuePossible()
    {
        var sim = World(); sim.InterveneGrowForest(18, 18, 2, 1);
        Assert.True(sim.Fire.Ignite(18, 18, sim.Clock, "测试"));
        float vegetation = sim.World.TileAt(18, 18).Vegetation;
        PlayerTools.Apply(sim, PlayerTool.Rain, 18, 18, 1, 70);
        Assert.Equal(FireState.None, sim.World.TileAt(18, 18).Fire);
        Assert.Equal(vegetation, sim.World.TileAt(18, 18).Vegetation);
        Assert.GreaterOrEqual(sim.World.TileAt(18, 18).Moisture, .7f);
    }
    [Fact("补充新人无法替代死亡的原居民，槽位复用也不会作弊")]
    public void CohortSurvivalUsesStableIdentity()
    {
        var sim = World(); var trial = new WorldTrial(); trial.Start(sim, 2);
        int victim = sim.Agents.AliveSlots().First();
        sim.Config.Rules.NoDeath = false; sim.Needs.Kill(sim.Agents, victim, DeathCause.Disaster, sim.Clock);
        sim.InterveneSpawnHumans(18, 18, 5, 0); trial.Refresh(sim);
        Assert.Equal(trial.OriginalCount - 1, trial.Survivors);
    }
    [Fact("试炼和核心一起读档，后续危机与预算完全一致")]
    public void SaveContinuationPreservesTrial()
    {
        var sim = World(); var trial = new WorldTrial(); trial.Start(sim, 1); Day(sim, trial, 3);
        trial.TrySpend(PlayerTool.Rain, 4, 70);
        string saved = SaveFile.Encode(sim), session = trial.Encode().ToJson();
        var restored = Simulation.CreateForRestore(sim.Config.Clone(), 36, 32, 77);
        Assert.True(SaveLoader.Load(restored, saved).DigestMatches);
        var loadedTrial = WorldTrial.Decode(JsonParser.Parse(session));
        for (int i = 0; i < 5; i++)
        {
            Day(sim, trial, 1); Day(restored, loadedTrial, 1);
            Assert.Equal(StateHash.ComputeDigest(sim), StateHash.ComputeDigest(restored));
            Assert.Equal(trial.Encode().ToJson(), loadedTrial.Encode().ToJson());
        }
    }
    [Fact("至少两天满足真实住房、食物和存活条件后才完成试炼")]
    public void CompletionRequiresSustainedRecovery()
    {
        var sim = World(); var trial = new WorldTrial(); trial.Start(sim, 0); Day(sim, trial, 7);
        var site = Enumerable.Range(0, sim.World.Tiles.Length).First(i => sim.World.Tiles[i].BuildingId == 0);
        sim.World.SetTerrain(site % sim.World.Width, site / sim.World.Width, TerrainKind.Grass);
        int house = sim.Buildings.Place(sim.World, BuildingKind.House, site % sim.World.Width, site / sim.World.Width, 1);
        Assert.True(house >= 0); sim.Buildings.MarkComplete(house, sim.Clock);
        foreach (int slot in sim.Agents.AliveSlots())
        {
            if (sim.Buildings.TryOccupyBedOf(house)) { sim.Agents.SetDwelling(slot, house); }
            sim.Agents.SetHunger(slot, 0);
        }
        Day(sim, trial, 1); Assert.Equal(1, trial.StableDays); Assert.True(trial.Running);
        foreach (int slot in sim.Agents.AliveSlots()) { sim.Agents.SetHunger(slot, 0); }
        Day(sim, trial, 1); Assert.True(trial.Won); Assert.False(trial.Running);
    }
    [Fact("未满足住房目标的试炼在截止日结束")]
    public void DeadlineEndsUnresolvedTrial()
    {
        var sim = World(); var trial = new WorldTrial(); trial.Start(sim, 0);
        for (int y = 0; y < sim.World.Height; y++) for (int x = 0; x < sim.World.Width; x++) { sim.World.SetBuildable(x, y, false); }
        Day(sim, trial, 14); Assert.False(trial.Running); Assert.False(trial.Won); Assert.Equal(14, trial.Days);
    }
    private static Simulation World()
    {
        var sim = new Simulation(new SimConfig(), 36, 32, 77); sim.Config.Rules.NoDeath = true;
        sim.Config.Needs.HungerPerDay = 0;
        sim.InterveneSpawnHumans(18, 18, 10, 2);
        foreach (int slot in sim.Agents.AliveSlots()) { sim.Agents.SetNextDecisionTick(slot, 1000000); }
        return sim;
    }
    private static void Day(Simulation sim, WorldTrial trial, int days)
    { for (int i = 0; i < days; i++) { sim.Tick(sim.Config.Clock.TicksPerDay); trial.Advance(sim); } }
}
