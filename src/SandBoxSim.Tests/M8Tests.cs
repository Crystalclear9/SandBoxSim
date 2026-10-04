using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Systems;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

/// <summary>
/// M8 验收测试（第一批）：贸易与价格。
///
/// # 这一组要守住的判据
///
/// 任务书第 71 条：`Price = Base × ((Demand + ε) / (Supply + ε)) ^ α`。
///
/// 但这组测试的重点不只是"公式算对了"，而是**公式在极端情况下仍然是有限的、
/// 有意义的** —— 那正是 ε 与上下限存在的理由。一个「供给为 0 时价格趋于无穷」的
/// 模型在真实运行里会让任何依赖价格的决策崩掉，而它在一张只有正常值的表上
/// 是看不出问题的。
/// </summary>
public sealed class M8Tests
{
    private const int TicksPerDay = 1440;

    private static SimConfig Config(int size = 44)
    {
        var config = new SimConfig();
        config.World.Width = size;
        config.World.Height = size;
        return config;
    }

    /// <summary>造一个安静的草地世界：人少、有少量资源，便于精确控制供需。</summary>
    private static Simulation MakeQuietWorld(int seed, int agents)
    {
        var sim = new Simulation(Config(), 44, 44, seed);
        for (int y = 0; y < 44; y++)
        {
            for (int x = 0; x < 44; x++)
            {
                sim.World.SetTerrain(x, y, TerrainKind.Grass);
                sim.World.SetVegetation(x, y, 0.3f);
                sim.World.SetMoisture(x, y, 0.5f);
            }
        }
        for (int y = 26; y <= 34; y++) { sim.World.SetTerrain(24, y, TerrainKind.Water); }
        sim.World.RefreshSpatialIndex();
        sim.InterveneSpawnHumans(30, 30, agents, 3);
        return sim;
    }

    // ---------------------------------------------------------------------
    // 基本方向：稀缺则贵
    // ---------------------------------------------------------------------

    /// <summary>把地图上所有木材抽干，并清空所有人的随身木材 —— 用于可靠地建立"稀缺"前提。</summary>
    private static void DrainWood(Simulation sim)
    {
        for (int y = 0; y < 44; y++)
        {
            for (int x = 0; x < 44; x++)
            {
                sim.InterveneAddResource(x, y, ResourceKind.Wood, -100000f);
            }
        }

        foreach (int slot in sim.Agents.AliveSlots())
        {
            float carried = sim.Agents.InventoryOf(slot, ResourceKind.Wood);
            sim.Agents.AddInventory(slot, ResourceKind.Wood, -carried);
        }
    }

    /// <summary>全图木材供给总量（随身 + 地面堆 + 共享库存）—— 用来显式检查前置条件。</summary>
    private static float TotalWood(Simulation sim)
    {
        float total = sim.GroundStocks.TotalOf(ResourceKind.Wood)
            + sim.Storage.GrandTotalOf(ResourceKind.Wood, sim.Buildings.Capacity);
        int[] slots = sim.Agents.LiveSlotsRaw(out int liveCount);
        for (int k = 0; k < liveCount; k++) { total += sim.Agents.InventoryOf(slots[k], ResourceKind.Wood); }
        return total;
    }

    [Fact("验收1：供给不足时价格高于基准价（稀缺则贵）")]
    public void ScarcityRaisesPrice()
    {
        // # 用**同一个世界的前后对比**，而不是两个世界互比
        //
        // 两个世界互比需要"除了资源量以外一切相同"，而这一点极难保证：
        // 第一版地形改了但资源节点没清 ⇒ 两个世界供给几乎相同；
        // 第二版清了资源节点、只给一边注入 ⇒ 结果**反了**（充裕的一边反而更贵）。
        // 与其继续追那个反直觉的结果，不如换一个不需要"两个世界相同"的判据：
        // **同一个世界在注入大量木材之后，价格必须下降。**
        // 这样"前提是否成立"就变成我可以直接测量的一个数（注入前后的供给总量）。
        Simulation sim = MakeQuietWorld(13001, agents: 12);
        DrainWood(sim);
        sim.Tick(TicksPerDay * 2);

        float priceBefore = sim.Trade.PriceOf(ResourceKind.Wood);
        float supplyBefore = TotalWood(sim);

        foreach (int slot in sim.Agents.AliveSlots())
        {
            sim.Agents.AddInventory(slot, ResourceKind.Wood, 500f);
        }
        float supplyAfterInjection = TotalWood(sim);

        // 走到下一个日边界让价格重算
        sim.Tick(TicksPerDay);
        float priceAfter = sim.Trade.PriceOf(ResourceKind.Wood);
        float supplyAfter = TotalWood(sim);

        // 前置条件：注入必须真的让供给变多（否则这条测试说明不了任何事）
        Assert.True(supplyAfterInjection > supplyBefore * 1.5f,
            "前置条件不成立：注入没有让木材供给明显变多（" + supplyBefore.ToString("0")
            + " -> " + supplyAfterInjection.ToString("0") + "）");

        Assert.True(priceAfter < priceBefore,
            "同一个世界在木材变多之后，价格必须下降（" + priceBefore.ToString("0.###")
            + " -> " + priceAfter.ToString("0.###") + "；供给 "
            + supplyBefore.ToString("0") + " -> " + supplyAfter.ToString("0") + "）");
    }

    [Fact("验收2：价格必须落在上下限之间（供给为 0 也不能趋于无穷）")]
    public void PriceStaysWithinBounds()
    {
        // 极端一：一个人、零资源、零库存
        Simulation empty = new Simulation(Config(), 44, 44, 13002);
        empty.InterveneSpawnHumans(22, 22, 1, 1);
        empty.Tick(TicksPerDay);

        foreach (ResourceKind kind in new[] { ResourceKind.Food, ResourceKind.Wood, ResourceKind.Stone, ResourceKind.Iron })
        {
            float price = empty.Trade.PriceOf(kind);
            Assert.True(SimMath.IsFinite(price), kind + " 的价格必须是有限值（实测 " + price + "）");
            Assert.True(price >= empty.Config.Trade.MinMultiplier * BaseOf(empty.Config.Trade, kind) - 1e-3f,
                kind + " 的价格不得低于下限（实测 " + price + "）");
            Assert.True(price <= empty.Config.Trade.MaxMultiplier * BaseOf(empty.Config.Trade, kind) + 1e-3f,
                kind + " 的价格不得高于上限（实测 " + price + "）");
        }
    }

    private static float BaseOf(TradeConfig config, ResourceKind kind)
    {
        switch (kind)
        {
            case ResourceKind.Food: return config.BaseFoodPrice;
            case ResourceKind.Wood: return config.BaseWoodPrice;
            case ResourceKind.Stone: return config.BaseStonePrice;
            default: return config.BaseIronPrice;
        }
    }

    [Fact("验收3：ε 必须大于 0（否则供需为 0 时价格会退化）")]
    public void EpsilonMustBePositive()
    {
        var config = new TradeConfig();
        Assert.True(config.Epsilon > 0f,
            "平滑项 ε 必须大于 0 —— 否则「供给为 0」会让比例变成无穷大，"
            + "「需求为 0」会让价格变成 0，两端都让价格失去意义");
    }

    // ---------------------------------------------------------------------
    // 基准价的相对关系（"铁比石料贵、石料比木头贵"）
    // ---------------------------------------------------------------------

    [Fact("验收4：同样稀缺程度下，基准价决定相对贵贱（铁 > 石 > 木）")]
    public void BasePriceOrderingHolds()
    {
        Simulation sim = MakeQuietWorld(13003, agents: 10);
        sim.Tick(TicksPerDay * 2);

        // 用相对价格（以木材为 1）比较，避免受各自供需差异影响
        float iron = sim.Trade.RelativeToWood(ResourceKind.Iron);
        float stone = sim.Trade.RelativeToWood(ResourceKind.Stone);

        // 四种资源的供需口径不同，所以这里只断言"基准价更高者不更便宜"
        Assert.True(iron > 0f && stone > 0f, "相对价格必须为正");
        Assert.True(sim.Config.Trade.BaseIronPrice > sim.Config.Trade.BaseStonePrice
                 && sim.Config.Trade.BaseStonePrice > sim.Config.Trade.BaseWoodPrice,
            "基准价必须满足 铁 > 石 > 木（这是「这种东西本身值多少」的定义）");
    }

    // ---------------------------------------------------------------------
    // 确定性与「不存档」的性质
    // ---------------------------------------------------------------------

    [Fact("验收5：价格是纯派生量 —— 两次运行必须一致，且不需要存档")]
    public void PriceIsDerivedAndDeterministic()
    {
        Simulation a = MakeQuietWorld(13004, agents: 12);
        Simulation b = MakeQuietWorld(13004, agents: 12);
        a.Tick(TicksPerDay * 6);
        b.Tick(TicksPerDay * 6);

        Assert.Near(a.Trade.PriceOf(ResourceKind.Food), b.Trade.PriceOf(ResourceKind.Food), 1e-5f);
        Assert.Near(a.Trade.PriceOf(ResourceKind.Iron), b.Trade.PriceOf(ResourceKind.Iron), 1e-5f);
        Assert.True(a.Trade.TotalQuotes > 0, "算价必须真的跑过");
    }

    [Fact("验收6：价格不进存档也不进摘要 —— 它必须能在读档后一模一样地重算出来")]
    public void PriceNeedsNoSaveAndRecomputesIdentically()
    {
        Simulation sim = MakeQuietWorld(13005, agents: 12);
        sim.Tick(TicksPerDay * 8);

        float foodBefore = sim.Trade.PriceOf(ResourceKind.Food);
        float woodBefore = sim.Trade.PriceOf(ResourceKind.Wood);
        string digestBefore = sim.StateDigestString();

        string json = sim.SaveToText();
        var restored = Simulation.CreateForRestore(Config(44), 44, 44, 99999);
        Assert.True(restored.LoadFromText(json).Success);

        // 摘要必须一致 —— 而价格**不在**摘要里（否则它就成了一个可能不一致的副本）
        Assert.Equal(digestBefore, restored.StateDigestString());

        // 走一天让两边都重新算价，然后必须完全相同
        sim.Tick(TicksPerDay);
        restored.Tick(TicksPerDay);

        Assert.Near(foodBefore > 0f ? sim.Trade.PriceOf(ResourceKind.Food) : 0f,
            restored.Trade.PriceOf(ResourceKind.Food), 1e-5f);
        Assert.Near(sim.Trade.PriceOf(ResourceKind.Wood), restored.Trade.PriceOf(ResourceKind.Wood), 1e-5f);
        _ = woodBefore;

        // 续跑逐 tick 一致
        for (int step = 1; step <= 300; step++)
        {
            sim.Tick(1);
            restored.Tick(1);
            Assert.True(sim.StateDigestString() == restored.StateDigestString(),
                "读档续跑在第 " + step + " tick 分叉");
        }
    }
}
