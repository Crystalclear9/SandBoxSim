using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Save;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

/// <summary>
/// 存档 / 读档测试（第 76 节）。
///
/// 这一组测试里只有一条是真正重要的：
/// **`跑一半 → 存档 → 读档 → 再跑一半` 与 `直接跑满` 的状态摘要必须完全相同。**
///
/// 它之所以是"唯一重要的一条"，是因为确定性契约的终点就在这里：
/// 只要读档路径漏掉任何一个影响未来的字段，这条断言就会失败。
/// 而漏字段的后果是"存档后世界悄悄变成另一个世界"—— 那种 bug 可以在几万 tick
/// 之后才表现为人口曲线不同，没有这条断言基本不可能定位。
///
/// 其余用例都是围绕它做的**定位辅助**：失败时能立刻知道是哪个字段漏了。
///
/// # 已验证的范围（写清楚边界，避免把"没测到"当成"没问题"）
///
///   * 本组用例验证的是 **60×60、8 天 + 8 天**（见 <see cref="MakeBusyWorld"/> 的调用点）。
///   * **已知残留问题**：在更大的规模上（100×100、20 天 + 20 天，即 CLI 的默认配置）
///     仍然存在一处未定位的分叉 —— 读档瞬间摘要一致（存档自带的自校验摘要可以证明），
///     但续跑到第 60 tick 时 **tiles 段**（逐格资源/植被）开始不同，而全部 8 条随机流
///     的状态在此之前逐位相同。已经排除的原因包括：随机流状态、决策相位与下次决策时刻、
///     迁移意愿与冷却、小数步进度、寻路下一步缓存、动物存活列表顺序、
///     槽位索引、chunk 聚合统计、统计计数、配置。
///     详尽的现象与已排查项记录在 CHANGELOG 中，供后续继续收敛。
///
/// 换句话说：**存档在写档那一瞬间是无损的（有自校验摘要为证），
/// 长程续跑的完全一致性尚未达成，这属于已知未竟事项，不应当被当作已完成。**
/// </summary>
public sealed class SaveLoadTests
{
    private static SimConfig Config(int width = 60, int height = 60)
    {
        var config = new SimConfig();
        config.World.Width = width;
        config.World.Height = height;
        return config;
    }

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

    /// <summary>造一个"有事发生"的世界：有人、有动物、有建筑、有物资流动。</summary>
    private static Simulation MakeBusyWorld(int seed, int ticks)
    {
        var config = Config(60, 60);
        var sim = new Simulation(config, 60, 60, seed);
        Flatten(sim, 10, 10, 50, 50);
        sim.InterveneSpawnHumans(30, 30, 14, 6);

        // 跑一段时间，让建造、迁移、物资搬运都真的发生过
        sim.Tick(ticks);
        return sim;
    }

    // ---------------------------------------------------------------------
    // 核心：往返摘要一致
    // ---------------------------------------------------------------------

    [Fact("跑一半再存档读档续跑，必须与直接跑完全一致（最关键的确定性验收）")]
    public void SaveLoadRoundTripPreservesDigest()
    {
        const int half = 1440 * 8;
        const int total = 1440 * 16;

        // A：直接跑满
        Simulation direct = MakeBusyWorld(11001, total);

        // B：跑一半 → 存档 → 读档 → 再跑一半
        Simulation source = MakeBusyWorld(11001, half);
        string json = source.SaveToText();

        var config = Config(60, 60);
        var restored = Simulation.CreateForRestore(config, 60, 60, 11001);
        SaveFile.LoadResult result = restored.LoadFromText(json);

        Assert.True(result.Success, "读档必须成功：" + result.Error);
        Assert.Equal(source.StateDigestString(), restored.StateDigestString(),
            "读档之后的状态必须与存档时**逐位一致**（任何差异都说明漏了字段）");

        restored.Tick(total - half);

        Assert.Equal(direct.StateDigestString(), restored.StateDigestString(),
            "「存档 → 读档 → 续跑」必须与「直接跑」完全相同。"
            + "不一致说明某个参与摘要的状态没有被保存 —— "
            + "请检查 RNG 流状态、决策相位、nextDecisionTick、migrateUntil、"
            + "moveProgress、pathStep、迁移冷却、动物存活列表顺序是否都在 SaveFile 里");
    }

    /// <summary>
    /// 「看不见的状态」回归测试（本组里最有价值的一条）。
    ///
    /// # 为什么需要它
    ///
    /// 实现存档时踩到了四个**不进状态摘要、但会影响未来行为**的字段，
    /// 它们共同的特征是：读档瞬间摘要**完全一致**，要到续跑几步之后才分叉。
    /// 四个都是靠 `SaveLoadRoundTripPreservesDigest` 才发现的，而定位它们
    /// 花了很久。这一条测试把四者各自锁住，下次谁再删掉其中一个，
    /// 失败信息会直接点名是哪一个，而不是只报"摘要不一致"。
    ///
    /// 四个字段与它们各自的表现：
    ///
    ///   * `ActionSystem.MoveProgress` —— 小数步进度。不存 → 移动节奏错开一格，
    ///     **第 2 个 tick** 就分叉（位置差一步）。
    ///   * `AgentStore.HasPathStep/PathStep` —— 缓存的寻路下一步。不存 → 重新算路径。
    ///   * `MigrationSystem.CooldownUntil` —— 迁移冷却。不存 → 全体立刻重新评估搬家。
    ///   * `WildlifeStore` 存活列表**顺序** —— 系统按它遍历并消耗随机数；
    ///     恢复成升序 → 随机数分给不同的动物，**第 1 个 tick** 就分叉。
    ///     （摘要按槽位升序算，所以顺序问题在摘要里根本看不见。）
    ///
    /// 判据的统一说法：**"它会不会影响未来的行为？"** —— 而不是"它看起来是不是状态"。
    /// </summary>
    [Fact("凡影响未来行为的字段都必须进存档（四个踩过的'隐形状态'）")]
    public void InvisibleButBehaviouralStateIsPersisted()
    {
        const int half = 1440 * 8;

        var config = Config(60, 60);
        config.World.TemperatureRecoveryPerHour = 0f;
        var sim = new Simulation(config, 60, 60, 11002);
        Flatten(sim, 10, 10, 50, 50);
        sim.InterveneSpawnHumans(30, 30, 14, 6);
        sim.Tick(half);

        // 先确认这些字段在这一局里**确实非默认值**，否则测的是空气。
        //
        // 其中两项（迁移冷却、缓存的下一步）**手工设定**而不是等模拟自己产生：
        // 让测试依赖平衡数值会带来"调参之后这条用例随机失败"的麻烦，
        // 而我们要测的是存档，不是平衡。手工设定之后这两项就恒定被覆盖到。
        //
        // 关于 pathStep：它在采样时刻（tick 边界）通常是**已清除**的 ——
        // 移动系统在同一 tick 内设置并使用它，抵达后就 ClearPathStep 了。
        // 因此这里手工把它设上，确保这个字段真的被往返验证过，
        // 而不是"因为恰好是空的所以通过了"。
        bool sawMoveProgress = false;
        foreach (int slot in sim.Agents.AliveSlots())
        {
            if (sim.Actions.MoveProgressOf(slot) > 0f) { sawMoveProgress = true; }
        }

        int[] liveOrderBefore = sim.Wildlife.ExportLiveOrder();
        bool isAscending = true;
        for (int i = 1; i < liveOrderBefore.Length; i++)
        {
            if (liveOrderBefore[i - 1] > liveOrderBefore[i]) { isAscending = false; break; }
        }

        // 注意：这里**不再**断言"这一局恰好有人的小数步进度处于中间值"。
        //
        // 原先的写法 `Assert.True(sawMoveProgress, ...)` 在 M4 之后开始失败 ——
        // 不是因为它发现了 bug，而是因为**世界参数一变，采样那一刻是否恰好有人走了一半就变了**。
        // 这是一类会在每次调参后变成噪声的断言。
        //
        // 正确做法是：要测某个字段是否往返，就把它**设成一个确定的值**（见下面 probeSlot 那几行），
        // 而不是依赖模拟恰好产生它。
        _ = sawMoveProgress;
        Assert.False(isAscending,
            "动物的存活列表顺序应当**不是**升序（否则区分不出'顺序是否被保存'）—— "
            + "若不是升序说明测试场景需要调整（例如让人为制造一次动物死亡）");

        // 手工设定两项，确保它们不是"恰好为空所以通过"
        int probeSlot = -1;
        foreach (int slot in sim.Agents.AliveSlots()) { probeSlot = slot; break; }
        Assert.GreaterOrEqual(probeSlot, 0);

        sim.Migration.RestoreCooldown(probeSlot, sim.Clock + 3000);
        sim.Agents.SetPathStep(probeSlot, 17, 23);
        sim.Actions.RestoreMoveProgress(probeSlot, 0.625f);

        // 小数步进度也**手工设定**，而不是断言"这一局里恰好有人处于中间值"。
        //
        // 原先的写法是 `Assert.True(sawMoveProgress, ...)`，它在 M4 之后开始偶发失败 ——
        // 因为世界的参数一变，"采样那一刻是否恰好有人走了一半"就变了。
        // 这是测试设计的教训：**要测某个字段是否往返，就把它设成一个确定的值**，
        // 而不是依赖模拟恰好产生它。依赖场景的断言会在每次调参后变成噪声。
        sawMoveProgress = true;

        string json = sim.SaveToText();

        // Exact continuation requires identical rules. The source explicitly disables
        // hourly temperature recovery above; a fresh default config changes tile state.
        var restoredConfig = config.Clone();
        var restored = Simulation.CreateForRestore(restoredConfig, 60, 60, 11002);
        var continuationResult = restored.LoadFromText(json);
        Assert.True(continuationResult.Success);
        Assert.True(continuationResult.ConfigMatches, "精确续跑必须使用相同配置");

        // 1) 小数步进度
        foreach (int slot in sim.Agents.AliveSlots())
        {
            Assert.Near(sim.Actions.MoveProgressOf(slot), restored.Actions.MoveProgressOf(slot), 1e-5f,
                "槽位 " + slot + " 的小数步进度没有恢复 —— 移动节奏会错开一格");
        }

        // 2) 缓存的寻路下一步
        foreach (int slot in sim.Agents.AliveSlots())
        {
            Assert.Equal(sim.Agents.HasPathStep(slot), restored.Agents.HasPathStep(slot),
                "槽位 " + slot + " 的 pathStep 存在性没有恢复");
            Assert.Equal(sim.Agents.PathStepOf(slot), restored.Agents.PathStepOf(slot),
                "槽位 " + slot + " 的 pathStep 坐标没有恢复");
        }

        // 3) 迁移冷却
        foreach (int slot in sim.Agents.AliveSlots())
        {
            Assert.Equal(sim.Migration.CooldownUntilOf(slot), restored.Migration.CooldownUntilOf(slot),
                "槽位 " + slot + " 的迁移冷却没有恢复");
        }

        // 4) 动物存活列表的**顺序**
        int[] liveOrderAfter = restored.Wildlife.ExportLiveOrder();
        Assert.Equal(liveOrderBefore.Length, liveOrderAfter.Length);
        for (int i = 0; i < liveOrderBefore.Length; i++)
        {
            Assert.Equal(liveOrderBefore[i], liveOrderAfter[i]);
        }

        // 5) chunk 聚合统计（第五个"隐形状态"）
        //
        // 它是**派生数据**，直觉上不该进存档 —— 重算一遍就好。但它是**增量维护**的，
        // 而 AI 真的会读它（ActionSearch.TryFindWaterAccess 先看 WaterTiles==0
        // 决定要不要跳过这个 chunk）。不存的表现：读档瞬间摘要完全一致
        // （chunk 统计不进摘要），到第 31 tick 才分叉，且所有随机流逐位相同。
        sim.World.Chunks.ExportState(out double[][] chunkFieldsBefore, out int[] cellCountsBefore, out bool[] dirtyBefore);
        restored.World.Chunks.ExportState(out double[][] chunkFieldsAfter, out int[] cellCountsAfter, out bool[] dirtyAfter);

        Assert.Equal(cellCountsBefore.Length, cellCountsAfter.Length);
        for (int c = 0; c < cellCountsBefore.Length; c++)
        {
            Assert.Equal(cellCountsBefore[c], cellCountsAfter[c]);
            Assert.Equal(dirtyBefore[c], dirtyAfter[c]);

            for (int f = 0; f < chunkFieldsBefore[c].Length; f++)
            {
                Assert.Near(chunkFieldsBefore[c][f], chunkFieldsAfter[c][f], 0.0,
                    "chunk " + c + " 的第 " + f + " 个聚合字段没有恢复");
            }
        }

        // 最后：真的续跑一段，确认没有隐藏的第六个字段
        for (int step = 1; step <= 1440; step++)
        {
            sim.Tick(1);
            restored.Tick(1);
            if (sim.StateDigestString() != restored.StateDigestString())
            { SaveDivergenceProbe.DumpAgentFieldDelta(sim, restored); }
            Assert.Equal(sim.StateDigestString(), restored.StateDigestString(),
                "续跑第 " + step + " tick 分叉：" + StateHash.FirstSegmentDifference(
                    StateHash.DescribeSegments(sim), StateHash.DescribeSegments(restored)));
        }
    }

    [Fact("读档必须采用存档里的 seed（而不是构造时传的那个）")]
    public void LoadedWorldAdoptsSavedSeed()    {
        // 这条测试的写法是刻意的：**故意用一个不同的 seed 去构造目标世界**。
        //
        // 起因是一个真实踩到的 bug：读档时先用命令行给的 seed 造空世界、
        // 再用存档覆盖逐格数据，但忘了把 World.Seed 改回存档里的值。
        // 表现是"读档瞬间摘要就不一致"，因为 seed 参与摘要。
        //
        // 而所有"自己存、自己读"的测试都会**恰好传同一个 seed**，
        // 于是这个 bug 在整套测试里完全隐藏 —— 直到用 CLI 做端到端验证才暴露。
        // 教训：验证"恢复了什么"的测试，必须让**输入**与**期望值**不同，
        // 否则它只是在验证"我没改东西"。
        var config = Config(40, 40);
        var sim = new Simulation(config, 40, 40, 424242);
        Flatten(sim, 5, 5, 35, 35);
        sim.InterveneSpawnHumans(20, 20, 5, 3);
        sim.Tick(720);

        string before = sim.StateDigestString();
        string json = sim.SaveToText();

        // 关键：构造时给一个**完全不同**的 seed
        var restored = Simulation.CreateForRestore(Config(40, 40), 40, 40, 999999);
        Assert.Equal(999999, restored.World.Seed);

        SaveFile.LoadResult result = restored.LoadFromText(json);
        Assert.True(result.Success, result.Error);

        Assert.Equal(424242, restored.World.Seed,
            "读档之后 World.Seed 必须变成存档里的值 —— 否则摘要不一致（且只有在 seed 不同时才暴露）");
        Assert.Equal(before, restored.StateDigestString());
    }

    [Fact("逐格字段必须**逐位**往返 —— 且必须在一个不同 seed 的世界里读档")]
    public void TilesRoundTripBitExactUnderDifferentSeed()
    {
        // # 这条测试为什么长成这样（它是 Phase 0 的结论，值得写清楚）
        //
        // 之前已经有一条 `TilesRoundTrip`，它跑得通 —— 但它在
        // `Simulation.CreateForRestore(..., seed)` 里传了**与存档相同的 seed**。
        // 于是"读档时新生成的地形"恰好与存档里的地形同源，
        // 任何**会被地形生成写入、却不会被存档恢复**的按格字段
        // （典型：`Resource.RegenerationRate`）都被无意中填对了值。
        //
        // 真实后果：读档瞬间逐位一致（那个字段不进摘要），
        // 到读档后第 60 tick（第一个小时边界）再生一次，
        // 3637/10000 格的再生量同时跑偏 —— 而摘要比对要到那一刻才报错。
        //
        // 所以这条测试做两件事：
        //   1. **故意用一个不同的 seed** 构造目标世界（让"地形生成"无法替存档兜底）；
        //   2. 断言 `Tile` 的**每一个**字段逐位相等（不做任何量化）。
        var config = Config(40, 40);
        var sim = new Simulation(config, 40, 40, 246810);
        Flatten(sim, 5, 5, 35, 35);
        sim.InterveneSpawnHumans(20, 20, 5, 3);
        sim.Tick(1440);

        string json = sim.SaveToText();

        // 关键：完全不同的 seed
        var restored = Simulation.CreateForRestore(Config(40, 40), 40, 40, 135791);
        Assert.True(restored.LoadFromText(json).Success);

        Tile[] a = sim.World.Tiles;
        Tile[] b = restored.World.Tiles;
        Assert.Equal(a.Length, b.Length);

        int firstBad = -1;
        string reason = string.Empty;

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].Terrain != b[i].Terrain) { firstBad = i; reason = "Terrain"; break; }
            if (a[i].Fire != b[i].Fire) { firstBad = i; reason = "Fire"; break; }
            if (a[i].BuildingId != b[i].BuildingId) { firstBad = i; reason = "BuildingId"; break; }
            if (a[i].Fertility != b[i].Fertility) { firstBad = i; reason = "Fertility"; break; }
            if (a[i].Moisture != b[i].Moisture) { firstBad = i; reason = "Moisture"; break; }
            if (a[i].Temperature != b[i].Temperature) { firstBad = i; reason = "Temperature"; break; }
            if (a[i].Vegetation != b[i].Vegetation) { firstBad = i; reason = "Vegetation"; break; }
            if (a[i].Resource.Kind != b[i].Resource.Kind) { firstBad = i; reason = "Resource.Kind"; break; }
            if (a[i].Resource.Amount != b[i].Resource.Amount) { firstBad = i; reason = "Resource.Amount"; break; }
            if (a[i].Resource.Capacity != b[i].Resource.Capacity) { firstBad = i; reason = "Resource.Capacity"; break; }
            if (a[i].Resource.RegenerationRate != b[i].Resource.RegenerationRate)
            {
                firstBad = i;
                reason = "Resource.RegenerationRate";
                break;
            }
        }

        Assert.True(firstBad < 0,
            "格 (" + (firstBad % 40) + "," + (firstBad / 40) + ") 的 " + reason
            + " 没有逐位往返 —— 只有在一个**不同 seed** 的世界里读档才会暴露这类字段");

        // 派生值不入档，但读档后必须正确
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].Walkable != b[i].Walkable) { Assert.True(false, "Walkable 派生值重算错误 @" + i); }
            if (a[i].Buildable != b[i].Buildable) { Assert.True(false, "Buildable 派生值重算错误 @" + i); }
        }

        // 最后走一段最长的路：跨过一个小时边界，确认再生没有跑偏
        sim.Tick(120);
        restored.Tick(120);
        Assert.Equal(sim.StateDigestString(), restored.StateDigestString(),
            "跨过小时边界之后仍必须一致 —— 再生量跑偏正是 RegenerationRate 漏存的表现");
    }

    [Fact("存档必须覆盖 Tile / ResourceNode 的每一个字段（防止将来又漏）")]
    public void SaveCoversEveryTileField()
    {
        // # 这是一条**防漏测试**，不是功能测试
        //
        // Phase 0 的教训：`Resource.RegenerationRate` 被漏掉，
        // 而且整套测试都发现不了（因为读档测试恰好传了相同 seed）。
        // 靠"下次记得写全"是不可靠的 —— 所以这里用反射**枚举** `Tile` 与
        // `ResourceNode` 的公开实例字段，并要求存档的 `tiles` 段里
        // 每一个都有对应的键（或用注释显式声明它是派生值）。
        //
        // 于是"新增一个字段却忘了存"会直接让这条测试失败，
        // 而不是在某个遥远的 tick 上表现为世界跑偏。

        // 显式声明：这些字段是**派生值**，刻意不入档，读档时由 ApplyTerrainRules 重算。
        var derivedFields = new System.Collections.Generic.HashSet<string>
        {
        };

        // Tile 的公开实例字段 → 存档键名（同一字段可能拆成多个键，例如 Resource 的四个子字段）
        var tileFieldKeys = new System.Collections.Generic.Dictionary<string, string>
        {
            ["Terrain"] = "terrain",
            ["Fire"] = "fire",
            ["BuildingId"] = "buildingId",
            ["Fertility"] = "fertility",
            ["Moisture"] = "moisture",
            ["Temperature"] = "temperature",
            ["Vegetation"] = "vegetation",
            ["Height"] = "height",
            ["Walkable"] = "walkable",
            ["Buildable"] = "buildable",
            ["Resource"] = "resourceKind",   // 展开成四个键，见下面的 resourceFieldKeys
        };

        var resourceFieldKeys = new System.Collections.Generic.Dictionary<string, string>
        {
            ["Kind"] = "resourceKind",
            ["Amount"] = "resourceAmount",
            ["Capacity"] = "resourceCapacity",
            ["RegenerationRate"] = "resourceRegenerationRate",
        };

        // 1) Tile 本身的字段
        System.Reflection.FieldInfo[] tileFields = typeof(Tile).GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        foreach (System.Reflection.FieldInfo field in tileFields)
        {
            if (derivedFields.Contains(field.Name)) { continue; }

            Assert.True(tileFieldKeys.ContainsKey(field.Name),
                "Tile." + field.Name + " 既没有对应的存档键、也没有被显式列为派生值。"
                + "新增格上字段时必须在 SaveFile.EncodeTiles/SaveLoader.RestoreTiles 里补上，"
                + "或在 SaveCoversEveryTileField 的 derivedFields 里说明它为什么是派生值。");
        }

        // 2) ResourceNode 的字段
        System.Reflection.FieldInfo[] resourceFields = typeof(SandBoxSim.Core.Environment.ResourceNode).GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        foreach (System.Reflection.FieldInfo field in resourceFields)
        {
            Assert.True(resourceFieldKeys.ContainsKey(field.Name),
                "ResourceNode." + field.Name + " 没有对应的存档键。"
                + "资源节点的每个字段都是按格写死、读档时不会重算的 —— 漏一个就会让再生跑偏。");
        }

        // 3) 反向检查：声明的键必须真的出现在存档 JSON 里
        //    （防止"映射表写对了但编码器忘了写"）
        var probe = new Simulation(Config(20, 20), 20, 20, 112233);
        SandBoxSim.Core.Foundation.JsonValue root =
            SandBoxSim.Core.Foundation.JsonParser.Parse(probe.SaveToText());
        SandBoxSim.Core.Foundation.JsonValue tiles = root.Get("tiles");

        foreach (System.Collections.Generic.KeyValuePair<string, string> pair in resourceFieldKeys)
        {
            Assert.True(tiles.Get(pair.Value).IsArray,
                "存档的 tiles 段里缺少 '" + pair.Value + "' 数组 —— 编码器没有写出这个字段");
        }

        foreach (System.Reflection.FieldInfo field in tileFields)
        {
            if (derivedFields.Contains(field.Name) || field.Name == "Resource") { continue; }
            Assert.True(tiles.Get(tileFieldKeys[field.Name]).IsArray,
                "存档的 tiles 段里缺少 '" + tileFieldKeys[field.Name] + "' 数组");
        }
    }

    [Fact("换配置读档必须被检出（状态摘要不覆盖配置，否则会静默换规则）")]
    public void ConfigMismatchIsDetected()
    {
        // 这条测试锁定一个**摘要的盲区**：`StateHash` 只覆盖世界状态，不含配置。
        // 于是"用一套不同的规则去读同一份存档"会通过自校验（摘要一致），
        // 然后跑出另一个世界。这不是错误（换规则是正当实验），但绝不能是静默的。
        var config = Config(40, 40);
        var sim = new Simulation(config, 40, 40, 112233);
        Flatten(sim, 5, 5, 35, 35);
        sim.InterveneSpawnHumans(20, 20, 5, 3);
        sim.Tick(720);

        string json = sim.SaveToText();

        // 1) 同一份配置 ⇒ 指纹一致
        var same = Simulation.CreateForRestore(Config(40, 40), 40, 40, 112233);
        SaveFile.LoadResult sameResult = same.LoadFromText(json);
        Assert.True(sameResult.Success, sameResult.Error);
        Assert.True(sameResult.ConfigMatches, "同一份配置必须被认为一致");

        // 2) 改一个会影响演化的参数 ⇒ 指纹必须不同
        SimConfig altered = Config(40, 40);
        altered.Needs.HungerPerDay *= 3f;

        var changed = Simulation.CreateForRestore(altered, 40, 40, 112233);
        SaveFile.LoadResult changedResult = changed.LoadFromText(json);

        Assert.True(changedResult.Success, changedResult.Error);
        Assert.False(changedResult.ConfigMatches,
            "改了饥饿速率之后必须被检出配置不一致 —— 否则玩家会以为在复现原来的实验");
        Assert.True(changedResult.DigestMatches,
            "配置变化**不影响**读档瞬间的摘要一致（这正是为什么需要单独的配置指纹）");
    }

    [Fact("连续两次「存档 → 读档」不产生任何漂移")]
    public void RepeatedSaveLoadDoesNotDrift()
    {
        var config = Config(60, 60);
        Simulation sim = MakeBusyWorld(11011, 1440 * 4);

        string first = sim.StateDigestString();

        // 往返 3 次：任何"每往返一次就漂一点"的实现都会在这里累积暴露出来
        for (int i = 0; i < 3; i++)
        {
            string json = sim.SaveToText();
            SaveFile.LoadResult result = sim.LoadFromText(json);
            Assert.True(result.Success, "第 " + (i + 1) + " 次往返失败：" + result.Error);
        }

        Assert.Equal(first, sim.StateDigestString(),
            "反复存档读档不能让世界漂移 —— 这是「存档是无损快照」的直接体现");
    }

    // ---------------------------------------------------------------------
    // 定位辅助：逐个字段族验证
    // ---------------------------------------------------------------------

    [Fact("随机流状态必须完整往返（漏一条就会在几万 tick 后分叉）")]
    public void RngStreamsRoundTrip()
    {
        var config = Config(40, 40);
        Simulation sim = MakeBusyWorld(11021, 1440 * 2);

        // 先推进所有流：天气/决策/事件都应该已经抽过随机数了
        ulong[][] before = sim.Random.ExportState();
        int streams = before.Length;

        string json = sim.SaveToText();

        var restoredConfig = Config(60, 60);
        var restored = Simulation.CreateForRestore(restoredConfig, 60, 60, 11021);
        Assert.True(restored.LoadFromText(json).Success);

        ulong[][] after = restored.Random.ExportState();
        Assert.Equal(streams, after.Length);

        for (int s = 0; s < streams; s++)
        {
            Assert.Equal(before[s].Length, after[s].Length);
            for (int i = 0; i < before[s].Length; i++)
            {
                Assert.Equal(before[s][i], after[s][i]);
            }
        }
    }

    [Fact("迁移意愿必须被保存（漏掉会让正在搬家的人停在半路）")]
    public void MigrationIntentRoundTrips()
    {
        var config = Config(60, 60);
        var sim = new Simulation(config, 60, 60, 11031);
        Flatten(sim, 5, 5, 55, 55);
        sim.InterveneSpawnHumans(30, 30, 5, 4);

        // 手工制造"正在搬家"的状态：比起等模拟自己触发，这样能确定地测到字段
        int slot = -1;
        foreach (int candidate in sim.Agents.AliveSlots()) { slot = candidate; break; }
        Assert.GreaterOrEqual(slot, 0);

        long intentUntil = sim.Clock + 5000;
        sim.Agents.SetHome(slot, 45, 45);
        sim.Agents.SetMigrateUntil(slot, intentUntil);

        string json = sim.SaveToText();

        var restoredConfig = Config(60, 60);
        var restored = Simulation.CreateForRestore(restoredConfig, 60, 60, 11031);
        Assert.True(restored.LoadFromText(json).Success);

        Assert.Equal(intentUntil, restored.Agents.MigrateUntilOf(slot),
            "迁移意愿必须原样恢复");
        Assert.True(restored.Agents.IsMigrating(slot, restored.Clock),
            "读档后必须仍然认为他在搬家");
    }

    [Fact("决策相位与下次决策时刻必须被保存（漏掉会让所有人在读档瞬间同时决策）")]
    public void DecisionScheduleRoundTrips()
    {
        var config = Config(60, 60);
        Simulation sim = MakeBusyWorld(11041, 1440 * 2);

        var beforePhase = new System.Collections.Generic.Dictionary<int, int>();
        var beforeNext = new System.Collections.Generic.Dictionary<int, long>();
        foreach (int slot in sim.Agents.AliveSlots())
        {
            beforePhase[slot] = sim.Agents.DecisionPhaseOf(slot);
            beforeNext[slot] = sim.Agents.NextDecisionTickOf(slot);
        }

        string json = sim.SaveToText();

        var restoredConfig = Config(60, 60);
        var restored = Simulation.CreateForRestore(restoredConfig, 60, 60, 11041);
        Assert.True(restored.LoadFromText(json).Success);

        foreach (System.Collections.Generic.KeyValuePair<int, int> pair in beforePhase)
        {
            Assert.Equal(pair.Value, restored.Agents.DecisionPhaseOf(pair.Key));
        }

        foreach (System.Collections.Generic.KeyValuePair<int, long> pair in beforeNext)
        {
            Assert.Equal(pair.Value, restored.Agents.NextDecisionTickOf(pair.Key));
        }
    }

    [Fact("个体的主要属性必须完整往返")]
    public void AgentAttributesRoundTrip()
    {
        var config = Config(60, 60);
        Simulation sim = MakeBusyWorld(11051, 1440 * 3);

        int slot = -1;
        foreach (int candidate in sim.Agents.AliveSlots()) { slot = candidate; break; }
        Assert.GreaterOrEqual(slot, 0);

        AgentRef reference = sim.Agents.RefOf(slot);
        Personality personality = sim.Agents.PersonalityOf(slot);

        string json = sim.SaveToText();

        var restoredConfig = Config(60, 60);
        var restored = Simulation.CreateForRestore(restoredConfig, 60, 60, 11051);
        Assert.True(restored.LoadFromText(json).Success);

        Assert.True(restored.Agents.IsValid(reference), "代次必须被保存，否则旧引用会失效");
        Assert.Equal(sim.Agents.NameOf(slot), restored.Agents.NameOf(slot));
        Assert.Equal(sim.Agents.XOf(slot), restored.Agents.XOf(slot));
        Assert.Equal(sim.Agents.YOf(slot), restored.Agents.YOf(slot));
        Assert.Equal(sim.Agents.HomeXOf(slot), restored.Agents.HomeXOf(slot));
        Assert.Equal(sim.Agents.AgeDaysOf(slot), restored.Agents.AgeDaysOf(slot));
        Assert.Equal(sim.Agents.LifeStageOf(slot), restored.Agents.LifeStageOf(slot));
        Assert.Equal(sim.Agents.HealthOf(slot), restored.Agents.HealthOf(slot));
        Assert.Equal(sim.Agents.HungerOf(slot), restored.Agents.HungerOf(slot));
        Assert.Equal(sim.Agents.InventoryOf(slot, ResourceKind.Food), restored.Agents.InventoryOf(slot, ResourceKind.Food));

        Personality after = restored.Agents.PersonalityOf(slot);
        Assert.Equal(personality.Aggression, after.Aggression);
        Assert.Equal(personality.Greed, after.Greed);
        Assert.Equal(personality.Kindness, after.Kindness);
        Assert.Equal(personality.Bravery, after.Bravery);
        Assert.Equal(personality.Industriousness, after.Industriousness);
        Assert.Equal(personality.Sociability, after.Sociability);
    }

    [Fact("建筑、共享库存与地面物资堆都必须往返")]
    public void BuildingsStocksAndStorageRoundTrip()
    {
        var config = Config(60, 60);
        var sim = new Simulation(config, 60, 60, 11061);
        Flatten(sim, 10, 10, 50, 50);
        sim.InterveneSpawnHumans(30, 30, 10, 5);

        // 用手工构造代替"等模拟自己盖出来"：这样测试不依赖平衡数值，
        // 也不会因为调参而随机失败。
        int house = sim.Buildings.Place(sim.World, BuildingKind.House, 28, 30, 200);
        int storage = sim.Buildings.Place(sim.World, BuildingKind.Storage, 32, 30, 300);
        Assert.GreaterOrEqual(house, 0);
        Assert.GreaterOrEqual(storage, 0);

        sim.Buildings.MarkComplete(house, 100);
        sim.Buildings.MarkComplete(storage, 100);
        sim.Storage.SetCapacity(storage, 600f);
        sim.Storage.Deposit(storage, ResourceKind.Wood, 200f);
        sim.Storage.Deposit(storage, ResourceKind.Food, 50f);

        sim.GroundStocks.Deposit(30, 34, ResourceKind.Stone, 80f, config.GroundStocks);

        int wildlifeBefore = sim.Wildlife.LiveCount;

        string json = sim.SaveToText();

        var restoredConfig = Config(60, 60);
        var restored = Simulation.CreateForRestore(restoredConfig, 60, 60, 11061);
        Assert.True(restored.LoadFromText(json).Success);

        Assert.Equal(sim.Buildings.TotalCompleted, restored.Buildings.TotalCompleted);
        Assert.Equal(sim.Buildings.TotalBeds, restored.Buildings.TotalBeds);
        Assert.Equal(sim.Buildings.CompletedStorages, restored.Buildings.CompletedStorages);
        Assert.Equal(BuildingState.Complete, restored.Buildings.StateOf(storage));
        Assert.Equal(house + 1, restored.World.TileAt(28, 30).BuildingId);

        Assert.Equal(200f, restored.Storage.AmountOf(storage, ResourceKind.Wood));
        Assert.Equal(50f, restored.Storage.AmountOf(storage, ResourceKind.Food));
        Assert.Equal(80f, restored.GroundStocks.TotalOf(ResourceKind.Stone));
        Assert.Equal(wildlifeBefore, restored.Wildlife.LiveCount);
    }

    [Fact("地形与资源必须逐格往返")]
    public void TilesRoundTrip()
    {
        var config = Config(40, 40);
        var sim = new Simulation(config, 40, 40, 11071);

        // 制造一些"与生成结果不同"的地形，确保测的是存档而不是生成
        sim.World.SetTerrain(10, 10, TerrainKind.Water);
        sim.World.SetTerrain(11, 10, TerrainKind.Mountain);
        sim.World.SetFertility(12, 10, 0.123f);
        sim.InterveneAddResource(13, 10, ResourceKind.Wood, 40f);
        sim.World.RefreshSpatialIndex();

        string json = sim.SaveToText();

        var restoredConfig = Config(60, 60);
        var restored = Simulation.CreateForRestore(restoredConfig, 40, 40, 11071);
        Assert.True(restored.LoadFromText(json).Success);

        Assert.Equal(TerrainKind.Water, restored.World.TileAt(10, 10).Terrain);
        Assert.Equal(TerrainKind.Mountain, restored.World.TileAt(11, 10).Terrain);
        Assert.Near(0.123f, restored.World.TileAt(12, 10).Fertility, 1e-4f);
        Assert.Near(sim.World.TileAt(13, 10).Resource.Amount, restored.World.TileAt(13, 10).Resource.Amount, 1e-3f);

        // 派生值必须被重算（不入档，但读档后必须正确）
        Assert.False(restored.World.TileAt(10, 10).Walkable, "水域必须不可走（派生值重算）");
        Assert.True(restored.World.TileAt(11, 10).Walkable, "山地可走（派生值重算）");
    }

    [Fact("时钟与天气必须往返（否则读档后时间倒流或天气跳变）")]
    public void ClockAndWeatherRoundTrip()
    {
        var config = Config(40, 40);
        Simulation sim = MakeBusyWorld(11081, 1440 * 3 + 517);

        long tickBefore = sim.Clock;
        WeatherKind weatherBefore = sim.World.Weather.Kind;
        int durationBefore = sim.World.Weather.DurationHours;

        string json = sim.SaveToText();

        var restoredConfig = Config(60, 60);
        var restored = Simulation.CreateForRestore(restoredConfig, 60, 60, 11081);
        Assert.True(restored.LoadFromText(json).Success);

        Assert.Equal(tickBefore, restored.Clock);
        Assert.Equal(weatherBefore, restored.World.Weather.Kind);
        Assert.Equal(durationBefore, restored.World.Weather.DurationHours);
    }

    // ---------------------------------------------------------------------
    // 错误处理
    // ---------------------------------------------------------------------

    [Fact("版本不匹配必须明确拒绝，而不是静默降级")]
    public void WrongVersionIsRejected()
    {
        var config = Config(40, 40);
        var sim = new Simulation(config, 40, 40, 11091);

        string json = sim.SaveToText();

        // 刻意用"当前版本"拼出要替换的字符串，而不是硬编码 "version": 1 ——
        // 硬编码会在每次提升存档格式版本时让这条测试**静默失去意义**
        // （替换不到任何东西 ⇒ 文件其实是合法存档 ⇒ 载入成功 ⇒ 测试失败，
        //  而失败原因看起来像"版本校验坏了"，实际是测试自己过期了）。
        string from = "\"version\": " + SaveFile.CurrentVersion;
        System.Console.WriteLine("  [测试] 版本替换：" + from + " -> \"version\": 999");
        string tampered = json.Replace(from, "\"version\": 999");
        Assert.False(tampered == json, "测试自身失效：没有找到可替换的版本字段");

        var target = Simulation.CreateForRestore(Config(40, 40), 40, 40, 11091);
        SaveFile.LoadResult result = target.LoadFromText(tampered);

        Assert.False(result.Success, "版本不匹配必须失败");
        Assert.True(result.Error.Contains("版本"), "错误信息必须点明是版本问题：" + result.Error);
    }

    [Fact("尺寸不匹配必须明确拒绝")]
    public void SizeMismatchIsRejected()
    {
        var sim = new Simulation(Config(40, 40), 40, 40, 11101);
        string json = sim.SaveToText();

        var different = Simulation.CreateForRestore(Config(80, 80), 80, 80, 11101);
        SaveFile.LoadResult result = different.LoadFromText(json);

        Assert.False(result.Success);
        Assert.True(result.Error.Contains("尺寸"), "错误信息必须点明是尺寸问题：" + result.Error);
    }

    [Fact("非法 JSON 必须返回失败而不是抛异常（载入破损存档不能让程序崩掉）")]
    public void MalformedJsonIsHandledGracefully()
    {
        var sim = Simulation.CreateForRestore(Config(40, 40), 40, 40, 11111);

        SaveFile.LoadResult result = sim.LoadFromText("{ this is not json }");
        Assert.False(result.Success);
        Assert.True(result.Error.Length > 0, "必须给出可读的错误原因");
    }

    [Fact("不存在的文件必须返回失败而不是抛异常")]
    public void MissingFileIsHandledGracefully()
    {
        var sim = Simulation.CreateForRestore(Config(40, 40), 40, 40, 11121);
        SaveFile.LoadResult result = sim.LoadFromFile("runs/__definitely_missing_save__.simsave");

        Assert.False(result.Success);
        Assert.True(result.Error.Contains("不存在"));
    }

    [Fact("真写盘再读回来也必须一致（覆盖文件 I/O 路径）")]
    public void FileRoundTripWorks()
    {
        string directory = System.IO.Path.Combine("runs", "savetest");
        string path = System.IO.Path.Combine(directory, "roundtrip.simsave");

        try
        {
            var sim = new Simulation(Config(40, 40), 40, 40, 11131);
            Flatten(sim, 5, 5, 35, 35);
            sim.InterveneSpawnHumans(20, 20, 6, 4);
            sim.Tick(1440);

            string before = sim.StateDigestString();
            sim.SaveToFile(path);

            Assert.True(System.IO.File.Exists(path), "存档文件必须真的被创建");

            var restored = Simulation.CreateForRestore(Config(40, 40), 40, 40, 11131);
            SaveFile.LoadResult result = restored.LoadFromFile(path);

            Assert.True(result.Success, result.Error);
            Assert.Equal(before, restored.StateDigestString());
        }
        finally
        {
            if (System.IO.File.Exists(path)) { System.IO.File.Delete(path); }
        }
    }
}
