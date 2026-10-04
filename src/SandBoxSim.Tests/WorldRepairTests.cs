using SandBoxSim.Core;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

public sealed class WorldRepairTests
{
    private static Simulation Forest(int limit = 1000)
    {
        var config = new SimConfig();
        config.World.Width = config.World.Height = 16;
        config.Fire.BaseIgnitionChancePerFastTick = 0;
        config.Fire.LightningChanceDuringStorm = 0;
        config.Fire.SpreadChancePerFastTick = 100;
        config.Fire.WindInfluence = 0;
        config.Fire.MaxBurningTiles = limit;
        var sim = new Simulation(config, 16, 16, 9881);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                sim.World.SetTerrain(x, y, TerrainKind.Forest);
                sim.World.SetVegetation(x, y, 1);
                sim.World.SetMoisture(x, y, 0);
            }
        return sim;
    }

    [Fact("新点燃的格子不能在同一个 FastTick 再继续蔓延")]
    public void FireSpreadsOneGenerationPerTick()
    {
        var sim = Forest();
        Assert.True(sim.Fire.Ignite(8, 8, 0, "回归"));
        sim.Fire.TickFast(10);
        int burning = 0;
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                if (sim.World.TileAt(x, y).Fire == FireState.Burning)
                {
                    burning++;
                    Assert.True(System.Math.Abs(x - 8) <= 1 && System.Math.Abs(y - 8) <= 1);
                }
        Assert.Equal(9, burning);
        Assert.Equal(burning, sim.Fire.BurningTiles);
    }

    [Fact("蔓延每点燃一格都必须检查同时燃烧上限")]
    public void FireRespectsConcurrentLimit()
    {
        var sim = Forest(3);
        Assert.True(sim.Fire.Ignite(8, 8, 0, "回归"));
        sim.Fire.TickFast(10);
        int burning = 0;
        foreach (Tile tile in sim.World.Tiles) { if (tile.Fire == FireState.Burning) { burning++; } }
        Assert.Equal(3, burning);
        Assert.Equal(burning, sim.Fire.BurningTiles);
    }

    [Fact("达到同时燃烧上限时自然点燃也必须停止")]
    public void NaturalFireCannotExceedLimit()
    {
        var sim = Forest(1);
        sim.Config.Fire.BaseIgnitionChancePerFastTick = 100;
        Assert.True(sim.Fire.Ignite(8, 8, 0, "回归"));
        sim.Fire.TickFast(10);
        Assert.Equal(1, sim.Fire.BurningTiles);
        Assert.Equal(1, sim.Fire.TotalIgnitions);
        Assert.Equal(0L, sim.Fire.NaturalIgnitionRolls);
    }

    [Fact("连续降温后气温仍有稳定值，晴天必须使它恢复")]
    public void TemperatureRecoversFromLongColdWeather()
    {
        var sim = Forest();
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                sim.World.SetTemperature(x, y, sim.Config.World.AmbientTemperature);
        sim.World.Weather.ForceKind(WeatherKind.Snow, 10000);
        for (int hour = 0; hour < 500; hour++) { sim.WeatherTick(); }
        Assert.True(sim.World.AverageTemperature() > 0.1f);
        Assert.True(sim.World.AverageTemperature() < sim.Config.World.AmbientTemperature);
        sim.World.Weather.ForceKind(WeatherKind.Clear, 10000);
        for (int hour = 0; hour < 500; hour++) { sim.WeatherTick(); }
        Assert.True(sim.World.AverageTemperature() > sim.Config.World.AmbientTemperature);
    }

    [Fact("地面库存总量必须来自现存物资，不能随增量浮点误差或读档改变")]
    public void GroundStockTotalIsCanonical()
    {
        var config = new GroundStockConfig();
        var direct = new GroundStockStore();
        direct.Deposit(3, 3, ResourceKind.Wood, 200, config);
        for (int i = 0; i < 1000; i++)
        {
            direct.Deposit(3, 3, ResourceKind.Wood, 0.1f, config);
            direct.Withdraw(3, 3, ResourceKind.Wood, 0.1f);
        }
        var restored = new GroundStockStore();
        foreach (int index in direct.AliveIndices())
        {
            var position = direct.PositionOf(index);
            restored.RestorePile(index, position.X, position.Y, direct.StockOf(index));
        }
        Assert.Equal(direct.TotalOf(ResourceKind.Wood), restored.TotalOf(ResourceKind.Wood));
    }

    [Fact("等距物资堆的选择不能依赖删除后的内部活跃顺序")]
    public void GroundStockTieUsesStableIdentity()
    {
        var config = new GroundStockConfig();
        var store = new GroundStockStore();
        store.Deposit(90, 90, ResourceKind.Wood, 1, config);
        store.Deposit(10, 10, ResourceKind.Wood, 20, config);
        store.Deposit(14, 10, ResourceKind.Wood, 1, config);
        store.Withdraw(90, 90, ResourceKind.Wood, 1);
        Assert.True(store.TryFindNearby(12, 10, ResourceKind.Wood, 5, out int index, out int _));
        Assert.Equal(1, index);
    }
}
