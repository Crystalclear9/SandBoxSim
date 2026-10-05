using System.Linq;
using SandBoxSim.Core;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Save;
using SandBoxSim.Core.Systems;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;
public sealed class SettlementBlueprintTests
{
    private static Simulation World()
    {
        var sim = new Simulation(new SimConfig(), 24, 24, 71);
        for (int y = 0; y < 24; y++) for (int x = 0; x < 24; x++) sim.InterveneSetTerrain(x, y, TerrainKind.Grass);
        sim.InterveneSetTerrain(8, 8, TerrainKind.Water); sim.InterveneSpawnHumans(10, 10, 6, 0);
        int h1 = sim.Buildings.Place(sim.World, BuildingKind.House, 10, 11, 10); sim.Buildings.MarkComplete(h1, 0);
        int h2 = sim.Buildings.Place(sim.World, BuildingKind.House, 12, 11, 10); sim.Buildings.MarkComplete(h2, 0);
        int store = sim.Buildings.Place(sim.World, BuildingKind.Storage, 10, 12, 10); sim.Buildings.MarkComplete(store, 0);
        int f1 = sim.Buildings.Place(sim.World, BuildingKind.Farm, 8, 9, 10); sim.Buildings.MarkComplete(f1, 0);
        int f2 = sim.Buildings.Place(sim.World, BuildingKind.Farm, 9, 8, 10); sim.Buildings.MarkComplete(f2, 0);
        return sim;
    }
    [Fact("蓝图只统计半径内的真实完工建筑和居民，观察不改世界")]
    public void ObservationIsLocalAndPure()
    {
        var sim = World(); var b = new SettlementBlueprint(); Assert.True(b.Start(sim, 0, 10, 10));
        string before = StateHash.ComputeDigest(sim); var state = b.Observe(sim);
        Assert.True(state.Meets); Assert.Equal(2, state.Houses); Assert.Equal(6, state.People);
        Assert.Equal(before, StateHash.ComputeDigest(sim));
        var distant = new SettlementBlueprint(); Assert.True(distant.Start(sim, 0, 23, 23)); Assert.False(distant.Observe(sim).Meets);
    }
    [Fact("蓝图需要两个不同日界，不重复计数；存档恢复延续进度")]
    public void SustainedGoalAndSave()
    {
        var sim = World(); var b = new SettlementBlueprint(); b.Start(sim, 0, 10, 10);
        sim.World.RestoreTick(sim.Config.Clock.TicksPerDay); b.Advance(sim); b.Advance(sim);
        Assert.Equal(1, b.StableDays); Assert.False(b.Completed);
        var saved = SettlementBlueprint.Decode(JsonParser.Parse(b.Encode().ToJson()));
        sim.World.RestoreTick(sim.Clock + sim.Config.Clock.TicksPerDay); b.Advance(sim); saved.Advance(sim);
        Assert.True(saved.Completed); Assert.Equal(b.Encode().ToJson(), saved.Encode().ToJson());
        string before = StateHash.ComputeDigest(sim); saved.Advance(sim); Assert.Equal(before, StateHash.ComputeDigest(sim));
    }
    [Fact("条件丢失重置稳定天数，跨越未知日界不能补算达成")]
    public void LostConditionsResetProgress()
    {
        var sim = World(); var b = new SettlementBlueprint(); b.Start(sim, 0, 10, 10);
        sim.World.RestoreTick(sim.Config.Clock.TicksPerDay); b.Advance(sim); Assert.Equal(1, b.StableDays);
        foreach (int slot in sim.Agents.AliveSlots().ToArray()) sim.Agents.SetPosition(slot, 23, 23);
        sim.World.RestoreTick(sim.Clock + sim.Config.Clock.TicksPerDay); b.Advance(sim); Assert.Equal(0, b.StableDays);
        foreach (int slot in sim.Agents.AliveSlots().ToArray()) sim.Agents.SetPosition(slot, 10, 10);
        sim.World.RestoreTick(sim.Clock + sim.Config.Clock.TicksPerDay * 3); b.Advance(sim); Assert.Equal(1, b.StableDays); Assert.False(b.Completed);
    }
    [Fact("林间蓝图必须有道路和未燃烧森林；无效蓝图不改变状态")]
    public void WoodlandAndInvalidSelection()
    {
        var sim = World(); var b = new SettlementBlueprint(); b.Start(sim, 1, 10, 10); Assert.False(b.Observe(sim).Meets);
        for (int x = 5; x < 17; x++) sim.InterveneSetTerrain(x, 14, TerrainKind.Forest);
        for (int x = 5; x < 13; x++) sim.InterveneSetTerrain(x, 15, TerrainKind.Road);
        Assert.True(b.Observe(sim).Meets);
        string before = StateHash.ComputeDigest(sim), state = b.Encode().ToJson();
        Assert.False(b.Start(sim, 99, 10, 10)); Assert.False(b.Start(sim, 0, 8, 8));
        Assert.Equal(before, StateHash.ComputeDigest(sim)); Assert.Equal(state, b.Encode().ToJson());
        Assert.Equal(-1, SettlementBlueprint.Decode(JsonValue.Null()).Kind);
    }
}
