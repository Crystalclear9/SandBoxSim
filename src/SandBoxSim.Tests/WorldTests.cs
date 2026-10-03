using SandBoxSim.Core;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Environment;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

/// <summary>
/// 世界生成、空间索引、资源记账与模拟 tick 的测试。
///
/// 这组测试是"M0 是否真的成立"的判据：地形规则自洽、空间索引与真相一致、
/// 资源再生不会超容量、tick 调度准确、状态摘要可复现。
/// </summary>
public sealed class WorldTests
{
    private static SimConfig Config(int width = 40, int height = 40)
    {
        var config = new SimConfig();
        config.World.Width = width;
        config.World.Height = height;
        // 测试用小图，加快运行；参数本身与默认保持一致以确保覆盖真实路径。
        return config;
    }

    // ---- 地形规则表 ----

    [Fact("地形规则表必须自洽：水域不可走也不可建")]
    public void WaterRules()
    {
        Assert.False(TerrainInfo.IsWalkable(TerrainKind.Water));
        Assert.False(TerrainInfo.IsBuildable(TerrainKind.Water));
        Assert.False(TerrainInfo.IsFarmable(TerrainKind.Water));
        Assert.True(TerrainInfo.ProvidesWater(TerrainKind.Water));
    }

    [Fact("地形规则表必须自洽：草地可走可建可开垦")]
    public void GrassRules()
    {
        Assert.True(TerrainInfo.IsWalkable(TerrainKind.Grass));
        Assert.True(TerrainInfo.IsBuildable(TerrainKind.Grass));
        Assert.True(TerrainInfo.IsFarmable(TerrainKind.Grass));
    }

    [Fact("山地移动代价必须明显高于草地")]
    public void MountainCostIsHigher()
    {
        Assert.Greater(TerrainInfo.MoveCost(TerrainKind.Mountain), TerrainInfo.MoveCost(TerrainKind.Grass));
        Assert.Greater(TerrainInfo.MoveCost(TerrainKind.Forest), TerrainInfo.MoveCost(TerrainKind.Grass));
        Assert.Less(TerrainInfo.MoveCost(TerrainKind.Road), TerrainInfo.MoveCost(TerrainKind.Grass));
    }

    [Fact("所有地形枚举值都必须有规则项（防止新增地形时漏配）")]
    public void EveryTerrainHasRules()
    {
        foreach (TerrainKind kind in System.Enum.GetValues<TerrainKind>())
        {
            // 不可走地形给出很大的代价，但绝不能是 0 或负数（否则寻路会穿墙）
            float cost = TerrainInfo.MoveCost(kind);
            Assert.Greater(cost, 0f, "地形 " + kind + " 的移动代价非法：" + cost);
            Assert.True(TerrainInfo.IsWalkable(kind) || cost > 10f,
                "地形 " + kind + " 不可走却没有给出高代价，寻路可能穿墙");
        }
    }

    [Fact("地形名称解析必须往返一致")]
    public void TerrainNameParsing()
    {
        foreach (TerrainKind kind in System.Enum.GetValues<TerrainKind>())
        {
            string name = TerrainInfo.NameOf(kind);
            if (name == "unknown") { continue; }
            Assert.True(TerrainInfo.TryParse(name, out TerrainKind parsed), "无法解析地形名 " + name);
            Assert.Equal(kind, parsed);
        }
        Assert.False(TerrainInfo.TryParse("definitely-not-a-terrain", out TerrainKind _));
    }

    [Fact("资源名称解析必须往返一致")]
    public void ResourceNameParsing()
    {
        foreach (ResourceKind kind in ResourceInfo.All)
        {
            string name = ResourceInfo.NameOf(kind);
            Assert.True(ResourceInfo.TryParse(name, out ResourceKind parsed), "无法解析资源名 " + name);
            Assert.Equal(kind, parsed);
            Assert.InRange(ResourceInfo.ToIndex(kind), 0, ResourceInfo.Count - 1);
            Assert.Equal(kind, ResourceInfo.FromIndex(ResourceInfo.ToIndex(kind)));
        }
    }

    // ---- 资源节点与库存 ----

    [Fact("Logistic 再生必须封顶在容量上")]
    public void LogisticRegenerationCapsAtCapacity()
    {
        float amount = 95f;
        float capacity = 100f;

        for (int i = 0; i < 100; i++)
        {
            amount = ResourceNode.RegenerateLogistic(amount, capacity, 0.5f, 1.0);
            Assert.LessOrEqual(amount, capacity + 1e-3f);
        }

        Assert.Near(100.0, amount, 0.5, "长期再生应逼近容量");
    }

    [Fact("资源接近枯竭时再生必须变慢（这是不可逆损伤的来源）")]
    public void RegenerationSlowsWhenDepleted()
    {
        float high = ResourceNode.RegenerateLogistic(90f, 100f, 0.05f, 1.0) - 90f;
        float low = ResourceNode.RegenerateLogistic(5f, 100f, 0.05f, 1.0) - 5f;

        Assert.Greater(high, low, "存量高时的绝对再生量应大于存量低时");
    }

    [Fact("采集不能超过存量，也不能产生负数")]
    public void HarvestRespectsStock()
    {
        var node = new ResourceNode { Kind = ResourceKind.Wood, Amount = 3f, Capacity = 10f };
        float taken = node.Harvest(10f);

        Assert.Near(3.0, taken, 1e-6);
        Assert.Near(0.0, node.Amount, 1e-6);

        float again = node.Harvest(5f);
        Assert.Near(0.0, again, 1e-6, "空节点再采集应为 0");
    }

    [Fact("零容量节点不应再生")]
    public void ZeroCapacityDoesNotRegenerate()
    {
        Assert.Near(0.0, ResourceNode.RegenerateLogistic(0f, 0f, 1f, 1.0), 1e-9);
        Assert.Near(0.0, ResourceNode.RegenerateLogistic(5f, 0f, 1f, 1.0), 1e-9);
    }

    [Fact("ResourceStock 的增删必须按种类隔离")]
    public void StockIsolatesKinds()
    {
        var stock = ResourceStock.Empty;
        stock.Add(ResourceKind.Food, 10f);
        stock.Add(ResourceKind.Wood, 5f);

        Assert.Near(10.0, stock.Get(ResourceKind.Food), 1e-6);
        Assert.Near(5.0, stock.Get(ResourceKind.Wood), 1e-6);
        Assert.Near(0.0, stock.Get(ResourceKind.Stone), 1e-6);
        Assert.Near(15.0, stock.Total, 1e-6);
    }

    [Fact("库存不允许出现负值")]
    public void StockClampsNegative()
    {
        var stock = ResourceStock.Empty;
        stock.Add(ResourceKind.Food, 5f);
        stock.Add(ResourceKind.Food, -10f);
        Assert.Near(0.0, stock.Get(ResourceKind.Food), 1e-6);
    }

    // ---- 世界生成 ----

    [Fact("同 seed 生成的世界必须逐格一致")]
    public void GenerationIsDeterministic()
    {
        SimConfig config = Config();
        Tile[] a = WorldGenerator.Generate(config, 40, 40, 777, out _);
        Tile[] b = WorldGenerator.Generate(config, 40, 40, 777, out _);

        for (int i = 0; i < a.Length; i++)
        {
            Assert.Equal(a[i].Terrain, b[i].Terrain, "第 " + i + " 格地形不同");
            Assert.True(SimMath.NearlyEqual(a[i].Fertility, b[i].Fertility, 1e-6f), "第 " + i + " 格肥沃度不同");
            Assert.True(SimMath.NearlyEqual(a[i].Moisture, b[i].Moisture, 1e-6f), "第 " + i + " 格湿度不同");
            Assert.True(SimMath.NearlyEqual(a[i].Resource.Amount, b[i].Resource.Amount, 1e-4f), "第 " + i + " 格资源不同");
        }
    }

    [Fact("不同 seed 生成的世界必须不同")]
    public void GenerationDiffersBySeed()
    {
        SimConfig config = Config();
        Tile[] a = WorldGenerator.Generate(config, 40, 40, 1, out _);
        Tile[] b = WorldGenerator.Generate(config, 40, 40, 2, out _);

        int differences = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].Terrain != b[i].Terrain) { differences++; }
        }

        Assert.Greater(differences, a.Length / 10, "不同 seed 的地形差异过小，种子可能没生效");
    }

    [Fact("多 seed 都必须生成完整地形（遍历数据表）")]
    public void GenerationProducesAllCoreTerrains()
    {
        foreach (TheoryCase theoryCase in TerrainSeedCases)
        {
            AssertTerrainsForSeed((int)theoryCase.Arguments[0]!);
        }
    }

    private static void AssertTerrainsForSeed(int seed)
    {
        SimConfig config = Config(50, 50);
        Tile[] tiles = WorldGenerator.Generate(config, 50, 50, seed, out WorldGenerator.Result info);

        Assert.Greater(info.WaterTiles, 0, "seed " + seed + " 的世界必须至少有一片水域");
        Assert.Greater(info.ForestTiles, 0, "seed " + seed + " 的世界必须至少有一片森林");
        Assert.Greater(info.GrassTiles, 0, "seed " + seed + " 的世界必须至少有草地");
        Assert.Greater(info.MountainTiles, 0, "seed " + seed + " 的世界必须至少有山地");
        Assert.Equal(tiles.Length, info.WaterTiles + info.ForestTiles + info.GrassTiles + info.MountainTiles + info.SandTiles,
            "地形计数之和必须等于格子总数（说明没有漏计的地形）");
    }

    /// <summary>地形生成用例数据表（见 Framework/TestAttributes.cs 关于特性实参限制的说明）。</summary>
    private static readonly TheoryCase[] TerrainSeedCases = Theory.Cases(
        Theory.Case("seed 1", 1),
        Theory.Case("seed 839102", 839102),
        Theory.Case("seed 999983", 999983));

    [Fact("森林格必须带木材节点，山地格必须带石/铁节点")]
    public void ResourcesMatchTerrain()
    {
        SimConfig config = Config(50, 50);
        Tile[] tiles = WorldGenerator.Generate(config, 50, 50, 4242, out _);

        for (int i = 0; i < tiles.Length; i++)
        {
            switch (tiles[i].Terrain)
            {
                case TerrainKind.Forest:
                    Assert.Equal(ResourceKind.Wood, tiles[i].Resource.Kind, "森林格第 " + i + " 格应带木材");
                    Assert.Greater(tiles[i].Resource.Capacity, 0f);
                    break;

                case TerrainKind.Mountain:
                    Assert.True(tiles[i].Resource.Kind == ResourceKind.Stone || tiles[i].Resource.Kind == ResourceKind.Iron,
                        "山地格第 " + i + " 格应带石或铁，实际 " + tiles[i].Resource.Kind);
                    break;

                case TerrainKind.Grass:
                case TerrainKind.Sand:
                    Assert.Equal(ResourceKind.Food, tiles[i].Resource.Kind, "草地/沙地第 " + i + " 格应带食物");
                    break;

                default:
                    break;
            }

            // 资源量必须在 [0, 容量] 之内（生成阶段也不允许越界）
            Assert.InRange(tiles[i].Resource.Amount, 0f, System.Math.Max(1e-3f, tiles[i].Resource.Capacity) + 1e-3f);
        }
    }

    [Fact("地形属性必须落在 [0,1] 且可通行标志与地形一致")]
    public void TilePropertiesAreSane()
    {
        SimConfig config = Config(40, 40);
        Tile[] tiles = WorldGenerator.Generate(config, 40, 40, 31337, out _);

        for (int i = 0; i < tiles.Length; i++)
        {
            Assert.InRange(tiles[i].Fertility, 0f, 1f);
            Assert.InRange(tiles[i].Moisture, 0f, 1f);
            Assert.InRange(tiles[i].Temperature, 0f, 1f);
            Assert.InRange(tiles[i].Vegetation, 0f, 1f);
            Assert.Equal(TerrainInfo.IsWalkable(tiles[i].Terrain), tiles[i].Walkable,
                "第 " + i + " 格的可行走标志与地形规则不一致");
            Assert.Equal(TerrainInfo.IsBuildable(tiles[i].Terrain), tiles[i].Buildable,
                "第 " + i + " 格的可建造标志与地形规则不一致");
        }
    }

    [Fact("水域的湿度必须接近饱和（水边不该是干的）")]
    public void WaterTilesAreWet()
    {
        SimConfig config = Config(50, 50);
        Tile[] tiles = WorldGenerator.Generate(config, 50, 50, 2025, out _);

        int checkedTiles = 0;
        float lowest = 1f;
        for (int i = 0; i < tiles.Length; i++)
        {
            if (tiles[i].Terrain != TerrainKind.Water) { continue; }
            if (tiles[i].Moisture < lowest) { lowest = tiles[i].Moisture; }
            checkedTiles++;
            if (checkedTiles > 200) { break; }
        }

        Assert.Greater(checkedTiles, 0);
        Assert.Greater(lowest, 0.5f, "水域格的最小湿度应明显偏高，实测最低值 " + lowest);
    }

    // ---- 空间索引 ----

    [Fact("空间索引的统计必须与逐格真值一致")]
    public void ChunkStatsMatchGroundTruth()
    {
        var config = new SimConfig();
        config.World.Width = 64;
        config.World.Height = 48;
        config.World.ChunkSize = 16;

        var world = new SandBoxSim.Core.Environment.World(config, 64, 48, 1234);
        world.RefreshSpatialIndex();

        for (int cy = 0; cy < world.Chunks.ChunkRows; cy++)
        {
            for (int cx = 0; cx < world.Chunks.ChunkCols; cx++)
            {
                world.Chunks.GetBounds(cx, cy, out int minX, out int minY, out int maxX, out int maxY);

                int forest = 0;
                int water = 0;
                int walkable = 0;
                double fertilitySum = 0;
                float woodSum = 0f;

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        ref readonly Tile tile = ref world.TileAt(x, y);
                        if (tile.Terrain == TerrainKind.Forest) { forest++; }
                        if (tile.Terrain == TerrainKind.Water) { water++; }
                        if (tile.Walkable) { walkable++; }
                        fertilitySum += tile.Fertility;
                        if (tile.Resource.Kind == ResourceKind.Wood) { woodSum += tile.Resource.Amount; }
                    }
                }

                ChunkStatsReadOnly stats = world.Chunks.Read(cx, cy);
                Assert.Equal(forest, stats.ForestTiles, "chunk(" + cx + "," + cy + ") 森林计数不符");
                Assert.Equal(water, stats.WaterTiles, "chunk(" + cx + "," + cy + ") 水域计数不符");
                Assert.Equal(walkable, stats.WalkableTiles, "chunk(" + cx + "," + cy + ") 可通行计数不符");
                Assert.Near(fertilitySum, stats.Raw(ChunkField.FertilitySum), 1e-3, "chunk(" + cx + "," + cy + ") 肥沃度求和不符");
                Assert.Near(woodSum, stats.WoodAmount, 1e-2, "chunk(" + cx + "," + cy + ") 木材求和不符");
            }
        }
    }

    [Fact("改变地形后空间索引必须在刷新后同步")]
    public void ChunkStatsUpdateAfterMutation()
    {
        var config = new SimConfig();
        config.World.Width = 32;
        config.World.Height = 32;
        var world = new SandBoxSim.Core.Environment.World(config, 32, 32, 5);
        world.RefreshSpatialIndex();

        int before = world.Chunks.Read(1, 1).ForestTiles;
        int changed = 0;

        // 把 chunk(1,1) 范围内的草地全部改成森林
        for (int y = 16; y < 32 && changed < 10; y++)
        {
            for (int x = 16; x < 32 && changed < 10; x++)
            {
                if (world.TileAt(x, y).Terrain == TerrainKind.Grass)
                {
                    world.SetTerrain(x, y, TerrainKind.Forest);
                    changed++;
                }
            }
        }

        if (changed == 0) { return; }   // 该 chunk 恰好没有草地，跳过

        Assert.Greater(world.Chunks.DirtyCount, 0, "改变地形后 chunk 必须被标脏");
        world.RefreshSpatialIndex();

        int after = world.Chunks.Read(1, 1).ForestTiles;
        Assert.Equal(before + changed, after, "刷新后森林计数未同步");
        Assert.Equal(0, world.Chunks.DirtyCount, "刷新后不应还有脏 chunk");
    }

    [Fact("越界读取必须返回安全值而不是崩溃")]
    public void OutOfBoundsReadsAreSafe()
    {
        var config = new SimConfig();
        var world = new SandBoxSim.Core.Environment.World(config, 8, 8, 1);

        Tile tile = world.TileAtClamped(-5, -5);
        Assert.False(tile.Walkable, "越界应被当作不可通行");

        Tile tile2 = world.TileAtClamped(100, 100);
        Assert.False(tile2.Walkable);

        Assert.False(world.IsInBounds(-1, 0));
        Assert.True(world.IsInBounds(7, 7));
        Assert.False(world.IsInBounds(8, 7));
    }

    [Fact("无效 chunk 查询必须返回无效视图而不是抛异常")]
    public void InvalidChunkQueryIsSafe()
    {
        var config = new SimConfig();
        config.World.ChunkSize = 16;
        var world = new SandBoxSim.Core.Environment.World(config, 32, 32, 1);

        ChunkStatsReadOnly invalid = world.Chunks.Read(-1, 0);
        Assert.False(invalid.IsValid);
        Assert.Equal(0, invalid.CellCount);
        Assert.Near(0.0, invalid.AverageFertility, 1e-9);
    }

    // ---- 资源系统 ----

    [Fact("资源再生必须增加存量且不超过容量")]
    public void RegenerationIncreasesStock()
    {
        var config = Config(32, 32);
        var sim = new Simulation(config, 32, 32, 2024);

        float before = sim.World.TotalResource(ResourceKind.Wood);
        sim.ResourceSystem.Regenerate(1.0 / 24.0);
        float after = sim.World.TotalResource(ResourceKind.Wood);

        Assert.GreaterOrEqual(after, before, "再生不应减少存量");
        Assert.LessOrEqual(after, sim.World.TotalCapacity(ResourceKind.Wood) + 1e-2f, "再生后不应超过容量");
    }

    [Fact("采集必须扣减 Tile 存量并累加统计")]
    public void HarvestDecreasesTileStock()
    {
        var config = Config(32, 32);
        var sim = new Simulation(config, 32, 32, 31415);

        // 找一格森林
        int found = -1;
        for (int i = 0; i < sim.World.Tiles.Length; i++)
        {
            if (sim.World.Tiles[i].Terrain == TerrainKind.Forest) { found = i; break; }
        }
        Assert.Greater(found, 0, "测试地图里应至少有一格森林");

        int x = found % sim.World.Width;
        int y = found / sim.World.Width;
        float before = sim.World.TileAt(x, y).Resource.Amount;

        float taken = sim.ResourceSystem.Harvest(x, y, ResourceKind.Wood, 5f);

        Assert.Greater(taken, 0f, "采集应成功");
        Assert.Near(before - taken, sim.World.TileAt(x, y).Resource.Amount, 1e-4);
        Assert.Near(taken, sim.ResourceSystem.TotalHarvested, 1e-4);
    }

    [Fact("采集空的或类型不符的资源必须返回 0")]
    public void HarvestWrongKindReturnsZero()
    {
        var config = Config(32, 32);
        var sim = new Simulation(config, 32, 32, 1);

        int found = -1;
        for (int i = 0; i < sim.World.Tiles.Length; i++)
        {
            if (sim.World.Tiles[i].Terrain == TerrainKind.Forest) { found = i; break; }
        }
        Assert.Greater(found, 0);

        int x = found % sim.World.Width;
        int y = found / sim.World.Width;

        float taken = sim.ResourceSystem.Harvest(x, y, ResourceKind.Iron, 10f);
        Assert.Near(0.0, taken, 1e-6, "森林上没有铁矿，采集应为 0");
    }

    [Fact("反复采集同一格必须触发枯竭事件（过度采集可观测）")]
    public void RepeatedHarvestCausesDepletion()
    {
        var config = Config(32, 32);
        var sim = new Simulation(config, 32, 32, 2718);

        int found = -1;
        for (int i = 0; i < sim.World.Tiles.Length; i++)
        {
            if (sim.World.Tiles[i].Terrain == TerrainKind.Forest) { found = i; break; }
        }
        Assert.Greater(found, 0);

        int x = found % sim.World.Width;
        int y = found / sim.World.Width;

        for (int i = 0; i < 100; i++)
        {
            sim.ResourceSystem.Harvest(x, y, ResourceKind.Wood, 10f);
        }

        Assert.Near(0.0, sim.World.TileAt(x, y).Resource.Amount, 1e-3, "反复采集后该格应被采空");
        Assert.Greater(sim.ResourceSystem.DepletionEvents, 0, "采空必须记录枯竭事件");
    }

    // ---- Simulation tick 与调度 ----

    [Fact("tick 必须精确推进日历")]
    public void TickAdvancesCalendar()
    {
        var config = Config(24, 24);
        var sim = new Simulation(config, 24, 24, 7);

        Assert.Equal(1, sim.World.Calendar.Day);
        Assert.Equal(0, sim.World.Calendar.Hour);

        sim.Tick(60);
        Assert.Equal(1, sim.World.Calendar.Hour);
        Assert.Equal(1, sim.World.Calendar.Day);

        sim.Tick(1440 - 60);
        Assert.Equal(2, sim.World.Calendar.Day);
        Assert.Equal(0, sim.World.Calendar.Hour);
        Assert.Equal(1440, sim.TickCount);
    }

    [Fact("每小时的周期事件必须恰好触发一次")]
    public void HourEventsFireOnce()
    {
        var config = Config(24, 24);
        var sim = new Simulation(config, 24, 24, 11);

        sim.Tick(1440);

        Assert.Equal(24, sim.HourEventsFired, "一天应触发 24 次小时事件");
        Assert.Equal(1, sim.DayEventsFired, "一天应触发 1 次日事件");
    }

    [Fact("每天结束时必须产生一条统计样本")]
    public void DailySampleIsRecorded()
    {
        var config = Config(24, 24);
        var sim = new Simulation(config, 24, 24, 13);

        // tick 到 3×1440：经过 tick 1440/2880/4320 三个日边界，
        // 分别完成第 1、2、3 天的 DailyTick，因此样本数 = 3。
        // 注意此刻日历已经进入第 4 天 00:00，所以 Day == 4 而最后一条样本记的是第 3 天。
        sim.Tick(1440 * 3);

        Assert.Equal(3, sim.Stats.DaysRecorded, "三天应产生三条日样本");
        Assert.Equal(4, sim.World.Calendar.Day, "3×1440 tick 正好推进到第 4 天 00:00");
        Assert.Equal(3, sim.LastDailySample.Day, "最后一条样本记录的应是刚结束的第 3 天");
        Assert.Equal(0, sim.LastDailySample.Population, "M0 还没有 agent，人口应为 0");
    }

    [Fact("天气推进必须改变 Tile 的湿度")]
    public void WeatherAffectsTiles()
    {
        var config = Config(24, 24);
        var sim = new Simulation(config, 24, 24, 17);

        sim.InterveneForceWeather(WeatherKind.Drought, 24);
        float before = sim.World.AverageMoisture();
        sim.Tick(600);
        float after = sim.World.AverageMoisture();

        Assert.Less(after, before, "强制干旱后平均湿度必须下降");
    }

    [Fact("夜间光照必须低于白天")]
    public void LightLevelFollowsDayNight()
    {
        var config = Config(8, 8);
        var sim = new Simulation(config, 8, 8, 19);

        // 中午
        sim.Tick(12 * 60);
        float noon = sim.World.Calendar.LightLevel;

        // 午夜（再过 12 小时）
        sim.Tick(12 * 60);
        float midnight = sim.World.Calendar.LightLevel;

        Assert.Greater(noon, midnight, "正午光照应高于午夜");
        Assert.Less(midnight, 0.2f, "午夜光照应很暗");
        Assert.Greater(noon, 0.8f, "正午光照应接近满值");
    }

    [Fact("不变量检查必须通过（M0 世界不应产生非法数值）")]
    public void InvariantsHold()
    {
        var config = Config(40, 40);
        var sim = new Simulation(config, 40, 40, 23);

        sim.Tick(1440 * 2);

        sim.ValidateInvariants();
        Assert.True(sim.LastInvariantCheckPassed, sim.LastInvariantFailure);
    }

    // ---- 确定性 ----

    [Fact("同 seed 两跑的状态摘要必须一致（M0 的核心承诺）")]
    public void StateDigestIsReproducible()
    {
        var config = Config(32, 32);

        var first = new Simulation(config, 32, 32, 555);
        first.Tick(1440);

        var second = new Simulation(config, 32, 32, 555);
        second.Tick(1440);

        Assert.Equal(first.StateDigestString(), second.StateDigestString(),
            "同 seed 两跑摘要不同，模拟存在不确定性");
    }

    [Fact("不同 seed 的状态摘要必须不同")]
    public void StateDigestDiffersBySeed()
    {
        var config = Config(32, 32);

        var first = new Simulation(config, 32, 32, 1);
        first.Tick(1440);

        var second = new Simulation(config, 32, 32, 2);
        second.Tick(1440);

        Assert.NotEqual(first.StateDigestString(), second.StateDigestString());
    }

    [Fact("重生成世界必须把日历与统计清零")]
    public void RegenerateWorldResetsState()
    {
        var config = Config(24, 24);
        var sim = new Simulation(config, 24, 24, 31);

        sim.Tick(1440 * 5);
        Assert.Equal(5, sim.Stats.DaysRecorded);

        sim.RegenerateWorld(999);

        Assert.Equal(0, sim.TickCount);
        Assert.Equal(1, sim.World.Calendar.Day);
        Assert.Equal(0, sim.Stats.DaysRecorded);
        Assert.Equal(999, sim.World.Seed);
    }

    // ---- 玩家干预 ----

    [Fact("改变地形必须同步规则与资源")]
    public void InterveneSetTerrainUpdatesRules()
    {
        var config = Config(24, 24);
        var sim = new Simulation(config, 24, 24, 41);

        // 找一格草地，改成水域
        int found = -1;
        for (int i = 0; i < sim.World.Tiles.Length; i++)
        {
            if (sim.World.Tiles[i].Terrain == TerrainKind.Grass) { found = i; break; }
        }
        Assert.Greater(found, 0);

        int x = found % sim.World.Width;
        int y = found / sim.World.Width;

        sim.InterveneSetTerrain(x, y, TerrainKind.Water);

        Assert.Equal(TerrainKind.Water, sim.World.TileAt(x, y).Terrain);
        Assert.False(sim.World.TileAt(x, y).Walkable, "改成水域后必须不可走");
        Assert.Equal(ResourceKind.None, sim.World.TileAt(x, y).Resource.Kind, "水域上不应残留草地食物");
    }

    [Fact("向同种资源的格子注入必须增加存量并以容量为上限")]
    public void InjectResourceAddsUpToCapacity()
    {
        var config = Config(24, 24);
        var sim = new Simulation(config, 24, 24, 43);

        int found = FindTerrain(sim, TerrainKind.Mountain);
        Assert.Greater(found, 0, "测试地图里应至少有一格山地");

        int x = found % sim.World.Width;
        int y = found / sim.World.Width;

        // 注意：这里必须把数量**取值**存成 float，不能保留 ref readonly Tile。
        // ref 是指向数组元素的活引用，注入之后它读到的就是新值，
        // "before + added == after" 这类算术断言会因此失去意义（真踩过这个坑：断言自身制造失败）。
        float beforeAmount = sim.World.TileAt(x, y).Resource.Amount;

        // 山地默认铺石头，因此往这里注入石头是合法操作。
        float added = sim.InterveneAddResource(x, y, ResourceKind.Stone, 5f);

        ref readonly Tile after = ref sim.World.TileAt(x, y);
        Assert.Greater(added, 0f, "注入石头应成功（地形 " + after.Terrain + "，节点 " + after.Resource.Kind + "）");
        Assert.Equal(ResourceKind.Stone, after.Resource.Kind);
        Assert.Near(beforeAmount + added, after.Resource.Amount, 1e-4);

        // 大量注入必须以容量为上限，不允许冲破不变量
        sim.InterveneAddResource(x, y, ResourceKind.Stone, 100000f);
        Assert.LessOrEqual(sim.World.TileAt(x, y).Resource.Amount, sim.World.TileAt(x, y).Resource.Capacity + 1e-3f,
            "注入后存量不得超过容量");
    }

    [Fact("向不同资源的格子注入必须被拒绝（不覆盖已有资源）")]
    public void InjectResourceRejectsDifferentKind()
    {
        var config = Config(24, 24);
        var sim = new Simulation(config, 24, 24, 44);

        int found = FindTerrain(sim, TerrainKind.Mountain);
        Assert.Greater(found, 0);

        int x = found % sim.World.Width;
        int y = found / sim.World.Width;

        float added = sim.InterveneAddResource(x, y, ResourceKind.Iron, 25f);
        Assert.Near(0.0, added, 1e-6, "山地已经是石头，注入铁必须被拒绝");
        Assert.Equal(ResourceKind.Stone, sim.World.TileAt(x, y).Resource.Kind, "拒绝注入时不应改变原有资源");
    }

    [Fact("向不产该资源的地形注入必须被拒绝（水域不会凭空长矿）")]
    public void InjectResourceRejectsWrongTerrain()
    {
        var config = Config(24, 24);
        var sim = new Simulation(config, 24, 24, 45);

        // 找一格草地，改成水域后再注入。
        // 注意断言下界是 0（首个格子也可能就是草坪）：写 Assert.Greater(found, 0) 会
        // 把"索引 0 合法"误判成"找不到"，这类 off-by-one 在测试里最浪费时间。
        int found = FindTerrain(sim, TerrainKind.Grass);
        Assert.GreaterOrEqual(found, 0, "测试地图里应至少有一格草地");

        int x = found % sim.World.Width;
        int y = found / sim.World.Width;
        sim.InterveneSetTerrain(x, y, TerrainKind.Water);

        float added = sim.InterveneAddResource(x, y, ResourceKind.Iron, 25f);
        Assert.Near(0.0, added, 1e-6, "水域不应该能注入矿产");
    }

    /// <summary>找到第一格指定地形的索引；找不到返回 -1（测试里必须显式断言）。</summary>
    private static int FindTerrain(Simulation sim, TerrainKind terrain)
    {
        for (int i = 0; i < sim.World.Tiles.Length; i++)
        {
            if (sim.World.Tiles[i].Terrain == terrain) { return i; }
        }
        return -1;
    }

    [Fact("催生森林必须把草地变成森林")]
    public void GrowForestConvertsGrass()
    {
        var config = Config(32, 32);
        var sim = new Simulation(config, 32, 32, 47);

        // 找一片草地中心
        int cx = -1;
        int cy = -1;
        for (int y = 2; y < 30 && cx < 0; y++)
        {
            for (int x = 2; x < 30; x++)
            {
                if (sim.World.TileAt(x, y).Terrain == TerrainKind.Grass)
                {
                    cx = x;
                    cy = y;
                    break;
                }
            }
        }
        Assert.Greater(cx, 0);

        int changed = sim.InterveneGrowForest(cx, cy, 3, 1.0);
        Assert.Greater(changed, 0, "催生森林应至少改变一格");
        Assert.Equal(TerrainKind.Forest, sim.World.TileAt(cx, cy).Terrain);
        Assert.Equal(ResourceKind.Wood, sim.World.TileAt(cx, cy).Resource.Kind);
    }

    [Fact("所有玩家干预都必须写进事件日志（可解释性）")]
    public void InterventionsAreLogged()
    {
        var config = Config(24, 24);
        var sim = new Simulation(config, 24, 24, 53);

        long before = sim.Events.TotalRecorded;

        sim.InterveneSetTerrain(5, 5, TerrainKind.Water);
        sim.InterveneForceWeather(WeatherKind.Rain, 6);
        sim.InterveneMultiplyRegeneration(1.5f);

        Assert.GreaterOrEqual(sim.Events.TotalRecorded, before + 3, "每次干预都应留下事件记录");

        // 只检查本次产生的最后 3 条事件：WorldGenerated 在 tick 0 就写入了，
        // 用下标从 0 开始查会顺带查到它，导致"明明记录了却断言失败"这种误导性结论。
        Core.History.WorldEvent terrainEvent = sim.Events[sim.Events.Count - 3];
        Core.History.WorldEvent weatherEvent = sim.Events[sim.Events.Count - 2];
        Core.History.WorldEvent ruleEvent = sim.Events[sim.Events.Count - 1];

        Assert.Equal(Core.History.WorldEventType.TerrainChanged, terrainEvent.Type);
        Assert.Equal(Core.History.WorldEventType.WeatherForced, weatherEvent.Type);
        Assert.Equal(Core.History.WorldEventType.RuleChanged, ruleEvent.Type);
    }

    [Fact("事件日志的环形缓冲必须保留最近的条目")]
    public void EventLogKeepsRecentEntries()
    {
        var log = new Core.History.EventLog(capacity: 10);

        for (int i = 0; i < 25; i++)
        {
            log.Record(i, Core.History.WorldEventType.Disaster, "事件 " + i);
        }

        Assert.Equal(10, log.Count);
        Assert.Equal(25, log.TotalRecorded);
        Assert.Equal("事件 15", log[0].Description, "环形缓冲应保留最新的 10 条");
        Assert.Equal("事件 24", log[log.Count - 1].Description);
    }

    [Fact("事件按实体检索必须只返回相关条目")]
    public void EventLogQueriesByActor()
    {
        var log = new Core.History.EventLog(capacity: 100);
        log.Record(1, Core.History.WorldEventType.AgentBorn, "A 出生", actor: 1);
        log.Record(2, Core.History.WorldEventType.AgentBorn, "B 出生", actor: 2);
        log.Record(3, Core.History.WorldEventType.AgentDied, "A 死亡", actor: 1);

        Core.History.WorldEvent[] events = log.ForActor(1);
        Assert.Equal(2, events.Length);
        Assert.Equal("A 出生", events[0].Description);
        Assert.Equal("A 死亡", events[1].Description);
    }
}
