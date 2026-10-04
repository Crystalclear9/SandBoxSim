using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Systems;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

/// <summary>
/// M3 的建造测试：建筑存储、选址规则、扣料、施工推进、共享库存。
///
/// 这一组测试的重点是**事务性**与**空间规则**：
/// 建造是第一个"一次性消耗大量资源、并且永久改变地图"的行为，
/// 因此"扣了料却没建成""建到水里去了"这类问题必须被测试挡住，
/// 而不是靠事后观察报告发现。
/// </summary>
public sealed class BuildingTests
{
    private static SimConfig Config(int width = 60, int height = 60)
    {
        var config = new SimConfig();
        config.World.Width = width;
        config.World.Height = height;
        return config;
    }

    /// <summary>造一片干净的平地（避免随机地图让测试变得不可复现）。</summary>
    private static void Flatten(Simulation sim, int minX, int minY, int maxX, int maxY)
    {
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                sim.World.SetTerrain(x, y, TerrainKind.Grass);
                sim.World.SetVegetation(x, y, 0.3f);
            }
        }
        sim.World.RefreshSpatialIndex();
    }

    [Fact("建筑必须能被放置，并且真的锚定到格子上")]
    public void BuildingIsPlacedAndAnchored()
    {
        var config = Config();
        var sim = new Simulation(config, 60, 60, 3001);
        Flatten(sim, 20, 20, 40, 40);

        int index = sim.Buildings.Place(sim.World, BuildingKind.House, 30, 30, 200);

        Assert.GreaterOrEqual(index, 0, "平地上必须能放下房子");
        Assert.Equal(1, sim.Buildings.LiveCount);
        Assert.Equal(BuildingKind.House, sim.Buildings.KindOf(index));
        Assert.Equal(BuildingState.UnderConstruction, sim.Buildings.StateOf(index));

        // Tile 上的锚点必须是"槽位 + 1"（0 = 无建筑）
        Assert.Equal(index + 1, sim.World.TileAt(30, 30).BuildingId);
    }

    [Fact("同一个格子不能放两个建筑")]
    public void BuildingCannotOverlap()
    {
        var config = Config();
        var sim = new Simulation(config, 60, 60, 3011);
        Flatten(sim, 20, 20, 40, 40);

        int first = sim.Buildings.Place(sim.World, BuildingKind.House, 30, 30, 200);
        int second = sim.Buildings.Place(sim.World, BuildingKind.House, 30, 30, 200);

        Assert.GreaterOrEqual(first, 0);
        Assert.Equal(-1, second, "同一个格子必须拒绝第二个建筑");
        Assert.Equal(1, sim.Buildings.LiveCount);
    }

    [Fact("建筑不能建在水里；矿场只能建在山地")]
    public void BuildingPlacementRespectsTerrain()
    {
        var config = Config();
        var sim = new Simulation(config, 60, 60, 3021);
        Flatten(sim, 20, 20, 40, 40);

        sim.World.SetTerrain(25, 25, TerrainKind.Water);
        sim.World.SetTerrain(26, 25, TerrainKind.Mountain);
        sim.World.RefreshSpatialIndex();

        Assert.False(BuildingStore.CanPlaceAt(sim.World, BuildingKind.House, 25, 25), "不能建在水里");
        Assert.False(BuildingStore.CanPlaceAt(sim.World, BuildingKind.House, 26, 25), "住房不能建在山地");

        // 矿场只能建在山地上（配方里的 RequiredTerrain）
        Assert.True(BuildingStore.CanPlaceAt(sim.World, BuildingKind.Mine, 26, 25), "矿场必须能建在山地");
        Assert.False(BuildingStore.CanPlaceAt(sim.World, BuildingKind.Mine, 30, 30), "矿场不能建在草地");

        // 农田/道路这类"已有功能"的格子上不能盖房子
        sim.World.SetTerrain(31, 31, TerrainKind.Road);
        sim.World.SetTerrain(32, 31, TerrainKind.Farmland);
        sim.World.RefreshSpatialIndex();
        Assert.False(BuildingStore.CanPlaceAt(sim.World, BuildingKind.House, 31, 31), "不能盖在道路上");
        Assert.False(BuildingStore.CanPlaceAt(sim.World, BuildingKind.House, 32, 31), "不能盖在农田上");
    }

    [Fact("农田必须临水，且只能建在草地上")]
    public void FarmRequiresGrassAndWaterAccess()
    {
        var config = Config();
        var sim = new Simulation(config, 60, 60, 3031);
        Flatten(sim, 20, 20, 40, 40);

        // 没有水的草地：不能建农田
        Assert.False(BuildingStore.CanPlaceAt(sim.World, BuildingKind.Farm, 30, 30),
            "看不见水的草地不该能建农田");

        // 在 (30,30) 旁边挖一格水：就能建了
        sim.World.SetTerrain(31, 30, TerrainKind.Water);
        sim.World.RefreshSpatialIndex();
        Assert.True(BuildingStore.CanPlaceAt(sim.World, BuildingKind.Farm, 30, 30),
            "临水的草地必须能建农田");

        // 但山地（即使临水）不能建农田
        sim.World.SetTerrain(35, 35, TerrainKind.Mountain);
        Assert.False(BuildingStore.CanPlaceAt(sim.World, BuildingKind.Farm, 35, 35),
            "农田只能建在草地上");
    }

    [Fact("建成之前不能提供床位；建成之后才有")]
    public void BedsOnlyCountWhenComplete()
    {
        var config = Config();
        var sim = new Simulation(config, 60, 60, 3041);
        Flatten(sim, 20, 20, 40, 40);

        int index = sim.Buildings.Place(sim.World, BuildingKind.House, 30, 30, 200);
        Assert.Equal(0, sim.Buildings.TotalBeds);
        Assert.Equal(0, sim.Buildings.TotalCompleted);

        sim.Buildings.MarkComplete(index, 100);
        Assert.Equal(BuildingRegistry.Of(BuildingKind.House).Beds, sim.Buildings.TotalBeds);
        Assert.Equal(1, sim.Buildings.CountOf(BuildingKind.House));
        Assert.Equal(1, sim.Buildings.TotalCompleted);
    }

    [Fact("施工必须按 tick 推进，而不是一次完工")]
    public void ConstructionTakesTime()
    {
        var config = Config();
        var sim = new Simulation(config, 60, 60, 3051);
        Flatten(sim, 20, 20, 40, 40);

        int shards = BuildingRegistry.Of(BuildingKind.House).BuildWorkTicks;
        int index = sim.Buildings.Place(sim.World, BuildingKind.House, 30, 30, shards);

        Assert.False(sim.Buildings.AdvanceWork(index, shards / 2), "一半工作量不该完工");
        Assert.True(sim.Buildings.AdvanceWork(index, (shards / 2) + 1), "补足工作量之后必须完工");
    }

    [Fact("建造必须是一次事务：材料不足时既不能扣料也不能放下工地")]
    public void BuildIsTransactionalWhenMaterialsAreMissing()
    {
        var config = Config();
        var sim = new Simulation(config, 60, 60, 3061);
        Flatten(sim, 20, 20, 40, 40);
        sim.InterveneSpawnHumans(30, 30, 1, 2);
        int slot = FirstAlive(sim);

        // 什么都不给
        sim.Agents.SetInventory(slot, ResourceKind.Wood, 0f);
        sim.Agents.SetInventory(slot, ResourceKind.Stone, 0f);

        bool ok = sim.BuildingSystem.TryStartBuilding(
            sim.Agents, slot, BuildingKind.House, 32, 30, out int index, out string failure);

        Assert.False(ok, "材料不足时必须失败");
        Assert.Equal(-1, index);
        Assert.True(failure.Length > 0, "失败必须给出可读原因");
        Assert.Equal(0, sim.Buildings.LiveCount, "失败时不能留下工地");
    }

    [Fact("材料足够时必须扣料并放下工地")]
    public void BuildConsumesMaterialsAndPlacesBuilding()
    {
        var config = Config();
        var sim = new Simulation(config, 60, 60, 3071);
        Flatten(sim, 20, 20, 40, 40);
        sim.InterveneSpawnHumans(30, 30, 1, 2);
        int slot = FirstAlive(sim);

        BuildingRecipe recipe = BuildingRegistry.Of(BuildingKind.House);
        sim.Agents.SetInventory(slot, ResourceKind.Wood, recipe.WoodCost + 10f);

        float before = sim.Agents.InventoryOf(slot, ResourceKind.Wood);

        bool ok = sim.BuildingSystem.TryStartBuilding(
            sim.Agents, slot, BuildingKind.House, 32, 30, out int index, out string failure);

        Assert.True(ok, "材料足够时必须成功（原因：" + failure + "）");
        Assert.GreaterOrEqual(index, 0);
        Assert.Equal(1, sim.Buildings.LiveCount);
        Assert.Equal(before - recipe.WoodCost, sim.Agents.InventoryOf(slot, ResourceKind.Wood),
            "必须精确扣掉造价");
    }

    [Fact("材料可以来自附近的仓库（共享库存参与建造）")]
    public void BuildCanUseStorageMaterials()
    {
        var config = Config();
        var sim = new Simulation(config, 60, 60, 3081);
        Flatten(sim, 20, 20, 40, 40);
        sim.InterveneSpawnHumans(30, 30, 1, 2);
        int slot = FirstAlive(sim);

        // 先造一个已完工的仓库并塞满木材
        int storage = sim.Buildings.Place(sim.World, BuildingKind.Storage, 29, 30, 300);
        sim.Buildings.MarkComplete(storage, 0);
        sim.Storage.SetCapacity(storage, 600f);
        sim.Storage.Deposit(storage, ResourceKind.Wood, 200f);
        sim.Storage.Deposit(storage, ResourceKind.Stone, 100f);

        // 个体身上什么都没有
        sim.Agents.SetInventory(slot, ResourceKind.Wood, 0f);

        bool ok = sim.BuildingSystem.TryStartBuilding(
            sim.Agents, slot, BuildingKind.House, 32, 30, out int index, out string failure);

        Assert.True(ok, "仓库里有木料时应当能用公共物资建造（原因：" + failure + "）");
        Assert.GreaterOrEqual(index, 0);
        Assert.Less(sim.Storage.AmountOf(storage, ResourceKind.Wood), 200f, "仓库的木材必须被扣掉");
    }

    [Fact("施工由建造系统按 FastTick 推进到完工")]
    public void BuildingSystemCompletesConstruction()
    {
        var config = Config();
        var sim = new Simulation(config, 60, 60, 3091);
        Flatten(sim, 20, 20, 40, 40);
        sim.InterveneSpawnHumans(30, 30, 1, 2);
        int slot = FirstAlive(sim);

        sim.Agents.SetInventory(slot, ResourceKind.Wood, 100f);
        Assert.True(sim.BuildingSystem.TryStartBuilding(
            sim.Agents, slot, BuildingKind.House, 32, 30, out int index, out string _));

        // 推进足够多的 FastTick（每 10 tick 一次）。
        // 循环条件写成"还没完工就继续"，并且用**上限**兜底（而不是精确算次数）——
        // 精确算次数会随 WorkPerFastTick/BuildWorkTicks 的调整而失效，
        // 而且"多推了一次"会让断言（等于 1）失败，看起来像逻辑错了。
        int guard = 0;
        while (sim.Buildings.StateOf(index) != BuildingState.Complete && guard < 200)
        {
            sim.BuildingSystem.TickFast((guard + 1) * 10);
            guard++;
        }

        Assert.Equal(BuildingState.Complete, sim.Buildings.StateOf(index), "足够多次 FastTick 之后必须完工");
        Assert.Equal(1, sim.Buildings.TotalCompleted, "只能完工一次（不能重复计数）");
        Assert.Greater(sim.Buildings.TotalBeds, 0);
        sim.BuildingSystem.TickFast((guard + 1) * 10);
        Assert.Equal(1, sim.Buildings.TotalCompleted, "已完工建筑不能在后续 FastTick 再次完工");
    }

    [Fact("农田建成时必须把地形改成农田")]
    public void CompletedFarmChangesTerrain()
    {
        var config = Config();
        var sim = new Simulation(config, 60, 60, 3101);
        Flatten(sim, 20, 20, 40, 40);
        sim.World.SetTerrain(31, 30, TerrainKind.Water);

        Assert.True(BuildingStore.CanPlaceAt(sim.World, BuildingKind.Farm, 30, 30));

        int index = sim.Buildings.Place(sim.World, BuildingKind.Farm, 30, 30, 1);
        Assert.GreaterOrEqual(index, 0);

        sim.BuildingSystem.TickFast(sim.Clock);
        sim.BuildingSystem.TickFast(sim.Clock + 10);

        Assert.Equal(BuildingState.Complete, sim.Buildings.StateOf(index));
        Assert.Equal(TerrainKind.Farmland, sim.World.TileAt(30, 30).Terrain,
            "农田建成后地表必须变成 Farmland（这是'建筑改变地形'的第一个例子）");
    }

    [Fact("拆除必须清掉地形锚点并归还床位")]
    public void DemolishClearsAnchorAndBeds()
    {
        var config = Config();
        var sim = new Simulation(config, 60, 60, 3111);
        Flatten(sim, 20, 20, 40, 40);

        int index = sim.Buildings.Place(sim.World, BuildingKind.House, 30, 30, 200);
        sim.Buildings.MarkComplete(index, 0);
        Assert.Equal(2, sim.Buildings.TotalBeds);

        sim.Buildings.Demolish(sim.World, index);

        Assert.Equal(0, sim.Buildings.LiveCount);
        Assert.Equal(0, sim.Buildings.TotalBeds);
        Assert.Equal(0, sim.World.TileAt(30, 30).BuildingId, "拆除后 Tile 上的锚点必须清掉");
    }

    [Fact("多格/多次放置超过初始容量时必须自动扩容（不能崩）")]
    public void BuildingStoreGrowsBeyondInitialCapacity()
    {
        var config = Config();
        var sim = new Simulation(config, 60, 60, 3121);
        Flatten(sim, 0, 0, 59, 59);

        int initial = sim.Buildings.Capacity;
        int placed = 0;

        for (int y = 0; y < 60 && placed < initial + 8; y++)
        {
            for (int x = 0; x < 60 && placed < initial + 8; x++)
            {
                if (sim.Buildings.Place(sim.World, BuildingKind.House, x, y, 10) < 0) { continue; }
                placed++;
            }
        }

        Assert.Equal(initial + 8, placed);
        Assert.Greater(sim.Buildings.Capacity, initial, "超过初始容量时必须扩容");
    }

    [Fact("建造必须进入确定性摘要")]
    public void BuildingsParticipateInDigest()
    {
        var config = Config(40, 40);

        var first = new Simulation(config, 40, 40, 3131);
        Flatten(first, 10, 10, 30, 30);
        first.Buildings.Place(first.World, BuildingKind.House, 20, 20, 200);
        first.Buildings.MarkComplete(0, 0);
        first.Storage.SetCapacity(0, 100f);
        first.Storage.Deposit(0, ResourceKind.Wood, 50f);

        var second = new Simulation(config, 40, 40, 3131);
        Flatten(second, 10, 10, 30, 30);
        second.Buildings.Place(second.World, BuildingKind.House, 20, 20, 200);
        second.Buildings.MarkComplete(0, 0);
        second.Storage.SetCapacity(0, 100f);
        second.Storage.Deposit(0, ResourceKind.Wood, 50f);

        Assert.Equal(first.StateDigestString(), second.StateDigestString());

        // 建筑状态不同 ⇒ 摘要必须不同（否则说明建筑没进摘要）
        var third = new Simulation(config, 40, 40, 3131);
        Flatten(third, 10, 10, 30, 30);
        third.Buildings.Place(third.World, BuildingKind.House, 20, 20, 200);
        third.Buildings.MarkComplete(0, 0);

        Assert.NotEqual(first.StateDigestString(), third.StateDigestString());
    }

    [Fact("共享库存必须有容量上限，且不覆盖已有内容")]
    public void StorageRespectsCapacity()
    {
        var config = Config();
        var sim = new Simulation(config, 60, 60, 3141);

        sim.Storage.SetCapacity(0, 100f);

        float accepted = sim.Storage.Deposit(0, ResourceKind.Wood, 150f);
        Assert.Equal(100f, accepted);
        Assert.Equal(100f, sim.Storage.AmountOf(0, ResourceKind.Wood));

        float again = sim.Storage.Deposit(0, ResourceKind.Wood, 50f);
        Assert.Equal(0f, again, "满了必须返回 0，而不是覆盖");
        Assert.Equal(100f, sim.Storage.AmountOf(0, ResourceKind.Wood));

        // 没有容量的槽位不能存东西
        Assert.Equal(0f, sim.Storage.Deposit(5, ResourceKind.Wood, 10f));
    }

    [Fact("建造行为诊断：无人干预时房子会不会自己出现")]
    public void BuildingDiagnostics()
    {
        var config = new SimConfig();
        config.World.Width = 100;
        config.World.Height = 100;

        var sim = new Simulation(config, 100, 100, 839102);
        int spawned = sim.InterveneSpawnHumans(87, 7, 40, 8);

        System.Console.WriteLine("  [诊断] 放置 " + spawned + " 人，观察建造");

        for (int day = 1; day <= 60; day++)
        {
            sim.Tick(1440);
            if (day % 10 != 0 && day != 60) { continue; }

            System.Console.WriteLine("  [诊断] 第 " + day + " 天：人 " + sim.Agents.LiveCount
                + "，木材采集 " + sim.Actions.HarvestedByKind[(int)ResourceKind.Wood].ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                + "，石料采集 " + sim.Actions.HarvestedByKind[(int)ResourceKind.Stone].ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                + "，开工 " + sim.Actions.BuildsStarted
                + "，完工 " + sim.Buildings.TotalCompleted
                + "（住房 " + sim.Buildings.CountOf(BuildingKind.House)
                + "，仓库 " + sim.Buildings.CountOf(BuildingKind.Storage)
                + "，农田 " + sim.Buildings.CountOf(BuildingKind.Farm) + "）"
                + "，床位 " + sim.Buildings.TotalBeds
                + "，仓库存取 " + sim.Storage.TotalDeposited.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                + "/" + sim.Storage.TotalWithdrawn.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
        }

        Assert.Greater(spawned, 0);
    }

    private static int FirstAlive(Simulation sim)
    {
        foreach (int slot in sim.Agents.AliveSlots()) { return slot; }
        return -1;
    }
}
