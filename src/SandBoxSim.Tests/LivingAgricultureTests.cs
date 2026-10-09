using System.Collections.Generic;
using SandBoxSim.Core;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Systems;
using SandBoxSim.Tests.Framework;
namespace SandBoxSim.Tests;
public sealed class LivingAgricultureTests
{
    private static Simulation World()
    {
        var config=new SimConfig();config.Buildings.DecayPerDay=0;
        var sim=new Simulation(config,32,32,73);
        for(int y=1;y<31;y++)for(int x=1;x<31;x++)
        {sim.World.SetTerrain(x,y,TerrainKind.Grass);sim.World.SetMoisture(x,y,.6f);sim.World.SetFertility(x,y,.6f);}
        sim.World.SetTerrain(10,11,TerrainKind.Water);sim.World.Weather.ForceKind(WeatherKind.Clear,999);return sim;
    }
    private static int Farm(Simulation sim)
    {
        int slot=sim.Buildings.Place(sim.World,BuildingKind.Farm,10,10,1);Assert.True(slot>=0);sim.BuildingSystem.TickFast(10);Assert.Equal(BuildingState.Complete,sim.Buildings.StateOf(slot));return slot;
    }
    [Fact("三类作物具有季节差异，农田真实产量随周期变化")]
    public void SeasonalHarvestDiffers()
    {
        var a=World();var b=World();int fa=Farm(a),fb=Farm(b);a.Buildings.AddLabor(fa,3);b.Buildings.AddLabor(fb,3);
        a.BuildingSystem.TickDay(1440);b.BuildingSystem.TickDay(25*1440);
        Assert.Greater(b.BuildingSystem.FoodProducedThisDay,a.BuildingSystem.FoodProducedThisDay);
        Assert.Near(b.BuildingSystem.FoodProducedThisDay,b.GroundStocks.TotalOf(ResourceKind.Food));
        Assert.True(LivingAgriculture.YieldFactor(CropKind.Roots,3)>LivingAgriculture.YieldFactor(CropKind.Grain,3));
    }
    [Fact("持续耕作、豆科与休耕实际改变地力")]
    public void SoilConsequences()
    {
        var sim=World();int farm=Farm(sim);
        LivingAgriculture.UpdateSoil(sim,farm,3,1,CropKind.Grain);float exhausted=sim.World.TileAt(10,10).Fertility;
        Assert.True(exhausted<.6f);
        LivingAgriculture.UpdateSoil(sim,farm,3,1,CropKind.Legumes);Assert.Greater(sim.World.TileAt(10,10).Fertility,exhausted);
        float before=sim.World.TileAt(10,10).Fertility;LivingAgriculture.UpdateSoil(sim,farm,0,1,CropKind.Grain);
        Assert.Greater(sim.World.TileAt(10,10).Fertility,before);
    }
    [Fact("地面食物腐损回到土壤，仓库和矿石不腐损")]
    public void FoodReturnsToSoil()
    {
        var sim=World();sim.GroundStocks.Deposit(10,10,ResourceKind.Food,100,sim.Config.GroundStocks);
        sim.GroundStocks.Deposit(10,10,ResourceKind.Stone,10,sim.Config.GroundStocks);
        int store=sim.Buildings.Place(sim.World,BuildingKind.Storage,14,10,1);sim.BuildingSystem.TickFast(10);sim.Storage.Deposit(store,ResourceKind.Food,50);
        float soil=sim.World.TileAt(10,10).Fertility;float lost=LivingAgriculture.SpoilGroundFood(sim,new List<int>());
        Assert.Greater(lost,0);Assert.Near(100-lost,sim.GroundStocks.TotalOf(ResourceKind.Food));
        Assert.Near(10,sim.GroundStocks.TotalOf(ResourceKind.Stone));Assert.Near(50,sim.Storage.AmountOf(store,ResourceKind.Food));
        Assert.Greater(sim.World.TileAt(10,10).Fertility,soil);
    }
    [Fact("关闭新规则保持传统生产且不腐损")]
    public void DisablePreservesTraditionalRules()
    {
        var a=World();var b=World();a.Config.Buildings.LivingAgricultureEnabled=false;b.Config.Buildings.LivingAgricultureEnabled=false;
        int fa=Farm(a),fb=Farm(b);a.Buildings.AddLabor(fa,3);b.Buildings.AddLabor(fb,3);
        a.BuildingSystem.TickDay(1440);b.BuildingSystem.TickDay(25*1440);Assert.Near(a.BuildingSystem.FoodProducedThisDay,b.BuildingSystem.FoodProducedThisDay);
        float stock=a.GroundStocks.TotalOf(ResourceKind.Food);LivingAgriculture.SpoilGroundFood(a,new List<int>());Assert.Near(stock,a.GroundStocks.TotalOf(ResourceKind.Food));
    }
    [Fact("满库存的未接收产出不计入实际生产统计")]
    public void FullStockDoesNotInventProduction()
    {
        var sim=World();Farm(sim);sim.Config.GroundStocks.CapacityPerKind=1;
        sim.GroundStocks.Deposit(10,10,ResourceKind.Food,1,sim.Config.GroundStocks);sim.Config.Buildings.GroundFoodSpoilagePerDay=0;
        sim.BuildingSystem.TickDay(1440);Assert.Near(0,sim.BuildingSystem.FoodProducedThisDay);
    }
    [Fact("日界前后保存恢复保留生产、土壤和腐损的确定性")]
    public void SaveReplayAcrossCycle()
    {
        var a=World();Farm(a);a.Tick(1400);string save=a.SaveToText();var b=World();Assert.True(b.LoadFromText(save).Success);
        a.Tick(100);b.Tick(100);Assert.Equal(a.StateDigestString(),b.StateDigestString());
        string before=a.StateDigestString();LivingAgriculture.CropAt(a.World.Seed,10,10);LivingAgriculture.Phase(a.Clock,1440);Assert.Equal(before,a.StateDigestString());
    }
}
