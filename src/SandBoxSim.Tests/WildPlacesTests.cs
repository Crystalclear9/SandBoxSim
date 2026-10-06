using System.Linq;
using SandBoxSim.Core;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Save;
using SandBoxSim.Core.Systems;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

public sealed class WildPlacesTests
{
    private static Simulation World(int seed=839102)
    {
        var sim=new Simulation(new SimConfig(),80,80,seed);
        for(int y=0;y<80;y++)for(int x=0;x<80;x++) { sim.World.SetTerrain(x,y,TerrainKind.Grass);sim.World.ApplyDefaultResource(x,y); }
        return sim;
    }
    [Fact("荒野地点由种子确定，包含不同生态与有限矿产")]
    public void DeterministicGeography()
    {
        var a=World();var b=World();var wa=WildPlaces.Create(a);var wb=WildPlaces.Create(b);
        Assert.Equal(wa.Encode().ToJson(),wb.Encode().ToJson());Assert.Equal(StateHash.ComputeDigest(a),StateHash.ComputeDigest(b));
        Assert.Equal(8,wa.Places.Select(p=>p.Kind).Distinct().Count());
        foreach(var p in wa.Places)Assert.True(a.World.IsInBounds(p.X-3,p.Y-3) && a.World.IsInBounds(p.X+3,p.Y+3));
        var ore=wa.Places.First(p=>p.Kind==WildPlaceKind.Ore);
        Assert.Equal(ResourceKind.Iron,a.World.TileAt(ore.X,ore.Y).Resource.Kind);
        Assert.Equal(0f,a.World.TileAt(ore.X,ore.Y).Resource.RegenerationRate);
    }
    [Fact("荒野描述与地点查询不会修改模拟或随机流")]
    public void ObservationsArePure()
    {
        var sim=World();var wild=WildPlaces.Create(sim);string before=StateHash.ComputeDigest(sim),state=wild.Encode().ToJson();
        foreach(var p in wild.Places) { Assert.NotNull(wild.At(p.X,p.Y));wild.Describe(sim,p); }
        Assert.Equal(before,StateHash.ComputeDigest(sim));Assert.Equal(state,wild.Encode().ToJson());
    }
    [Fact("泉眼滋养土壤，填平后不会自动恢复或继续补水")]
    public void SpringCanBeChanged()
    {
        var sim=World();var wild=WildPlaces.Create(sim);var p=wild.Places.First(p=>p.Kind==WildPlaceKind.Spring);
        sim.World.SetMoisture(p.X+1,p.Y,.1f);sim.World.RestoreTick(sim.Config.Clock.TicksPerDay);wild.Advance(sim);
        Assert.Greater(sim.World.TileAt(p.X+1,p.Y).Moisture,.1f);
        sim.InterveneSetTerrain(p.X,p.Y,TerrainKind.Grass);sim.World.SetMoisture(p.X+1,p.Y,.1f);
        sim.World.RestoreTick(sim.Config.Clock.TicksPerDay*2);wild.Advance(sim);
        Assert.Equal(.1f,sim.World.TileAt(p.X+1,p.Y).Moisture);Assert.Equal(TerrainKind.Grass,sim.World.TerrainAt(p.X,p.Y));
    }
    [Fact("野果结果受水土与周期影响，同一日不会重复增产")]
    public void BerriesRespectConditionsAndCycle()
    {
        var sim=World();var wild=WildPlaces.Create(sim);var p=wild.Places.First(p=>p.Kind==WildPlaceKind.Berries);
        sim.ResourceSystem.Harvest(p.X,p.Y,ResourceKind.Food,10000);sim.World.SetMoisture(p.X,p.Y,.8f);
        sim.World.RestoreTick(sim.Config.Clock.TicksPerDay);wild.Advance(sim);
        Assert.Greater(sim.World.TileAt(p.X,p.Y).Resource.Amount,0f);
        float amount=sim.World.TileAt(p.X,p.Y).Resource.Amount;wild.Advance(sim);Assert.Equal(amount,sim.World.TileAt(p.X,p.Y).Resource.Amount);
        var dormant=WildPlaces.Decode(wild.Encode().Set("lastDay",JsonValue.From(35)));
        sim.ResourceSystem.Harvest(p.X,p.Y,ResourceKind.Food,10000);sim.World.RestoreTick(sim.Config.Clock.TicksPerDay*36);dormant.Advance(sim);
        Assert.Equal(0f,sim.World.TileAt(p.X,p.Y).Resource.Amount);
        sim.World.SetMoisture(p.X,p.Y,.1f);sim.World.RestoreTick(sim.Config.Clock.TicksPerDay*48);dormant.Advance(sim);
        Assert.Equal(0f,sim.World.TileAt(p.X,p.Y).Resource.Amount);
    }
    [Fact("遗迹材料一次性存在，取走后不会因时间或恢复再次产生")]
    public void RuinsDoNotRespawnStock()
    {
        var sim=World();var wild=WildPlaces.Create(sim);var p=wild.Places.First(p=>p.Kind==WildPlaceKind.Ruins);
        Assert.Equal(42f,sim.GroundStocks.Withdraw(p.X,p.Y,ResourceKind.Stone,1000));
        Assert.Equal(24f,sim.GroundStocks.Withdraw(p.X,p.Y,ResourceKind.Wood,1000));
        var restored=WildPlaces.Decode(wild.Encode());sim.World.RestoreTick(sim.Config.Clock.TicksPerDay*30);restored.Advance(sim);
        Assert.Equal(-1,sim.GroundStocks.FindAt(p.X,p.Y));
    }
    [Fact("荒野日界、命名手记与核心状态在存读档后继续一致")]
    public void SaveContinuation()
    {
        var sim=World();var wild=WildPlaces.Create(sim);Assert.True(wild.Remember(18,22,"我的河湾"));
        sim.Tick(sim.Config.Clock.TicksPerDay);wild.Advance(sim);
        var restored=Simulation.CreateForRestore(sim.Config,80,80,sim.World.Seed);
        var result=SaveLoader.Load(restored,SaveFile.Encode(sim));Assert.True(result.Success);Assert.True(result.DigestMatches);
        var restoredWild=WildPlaces.Decode(wild.Encode());
        sim.Tick(sim.Config.Clock.TicksPerDay);wild.Advance(sim);
        restored.Tick(restored.Config.Clock.TicksPerDay);restoredWild.Advance(restored);
        Assert.Equal(StateHash.ComputeDigest(sim),StateHash.ComputeDigest(restored));Assert.Equal(wild.Encode().ToJson(),restoredWild.Encode().ToJson());
        Assert.Equal(0,WildPlaces.Decode(JsonValue.Null()).Places.Count);
    }
    [Fact("苇泽涵养可被改造停止，旧档地貌枚举继续识别")]
    public void WetlandCanBeChanged()
    {
        var sim=World();var wild=WildPlaces.Create(sim);var p=wild.Places.First(p=>p.Kind==WildPlaceKind.Wetland);
        sim.World.SetMoisture(p.X+1,p.Y,.1f);sim.World.RestoreTick(sim.Config.Clock.TicksPerDay);wild.Advance(sim);
        Assert.Greater(sim.World.TileAt(p.X+1,p.Y).Moisture,.1f);
        sim.World.SetTerrain(p.X,p.Y,TerrainKind.Grass);sim.World.SetMoisture(p.X+1,p.Y,.1f);
        sim.World.RestoreTick(sim.Config.Clock.TicksPerDay*2);wild.Advance(sim);Assert.Equal(.1f,sim.World.TileAt(p.X+1,p.Y).Moisture);
        Assert.Equal(8,WildPlaces.Decode(wild.Encode()).Places.Select(p=>p.Kind).Distinct().Count());
    }
    [Fact("倒木物资有限，取走后停止朽木肥力作用")]
    public void FallenWoodIsFinite()
    {
        var sim=World();var wild=WildPlaces.Create(sim);var p=wild.Places.First(p=>p.Kind==WildPlaceKind.FallenWood);
        sim.World.SetFertility(p.X+1,p.Y,.2f);sim.World.RestoreTick(sim.Config.Clock.TicksPerDay);wild.Advance(sim);
        Assert.Greater(sim.World.TileAt(p.X+1,p.Y).Fertility,.2f);
        Assert.Equal(48f,sim.GroundStocks.Withdraw(p.X,p.Y,ResourceKind.Wood,10000));sim.World.SetFertility(p.X+1,p.Y,.2f);
        sim.World.RestoreTick(sim.Config.Clock.TicksPerDay*30);wild.Advance(sim);
        Assert.Equal(.2f,sim.World.TileAt(p.X+1,p.Y).Fertility);Assert.Equal(-1,sim.GroundStocks.FindAt(p.X,p.Y));
    }
    [Fact("草甸食物随生长周期变化，休眠停止额外结籽")]
    public void MeadowHasNoObjectiveOrReward()
    {
        var sim=World();var wild=WildPlaces.Create(sim);var p=wild.Places.First(p=>p.Kind==WildPlaceKind.Meadow);
        sim.ResourceSystem.Harvest(p.X,p.Y,ResourceKind.Food,10000);sim.World.RestoreTick(sim.Config.Clock.TicksPerDay);wild.Advance(sim);
        Assert.Greater(sim.World.TileAt(p.X,p.Y).Resource.Amount,0f);
        var dormant=WildPlaces.Decode(wild.Encode().Set("lastDay",JsonValue.From(35)));
        sim.ResourceSystem.Harvest(p.X,p.Y,ResourceKind.Food,10000);sim.World.RestoreTick(sim.Config.Clock.TicksPerDay*36);dormant.Advance(sim);
        Assert.Equal(0f,sim.World.TileAt(p.X,p.Y).Resource.Amount);
    }
}
