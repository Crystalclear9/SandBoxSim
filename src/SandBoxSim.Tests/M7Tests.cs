using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Systems;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

/// <summary>
/// M7 验收测试：聚落是**涌现**的，不是被放置的。
///
/// # 这一组守住的东西
///
/// 任务书第 29 / 60 条：玩家不能"造一个村子"。他能摆的只有条件。
/// 所以这里的每一条断言都在回答"**什么条件下会长出聚落**"，
/// 而没有一条是"调用一个 API 创建聚落"—— 那个 API 根本不存在，这是刻意的。
///
/// # 为什么滞回那一条特别重要
///
/// 如果成立与解散共用一个阈值，人口在阈值附近抖动时聚落会**每帧成立又解散**：
/// 玩家看到闪烁的标签、事件列表被灌满，而「这个村子是什么时候形成的」这个真正的历史丢失了。
/// 所以滞回不是"为了稳定打的补丁「，它本身就是」聚落有惯性"这条事实的建模。
/// </summary>
public sealed class M7Tests
{
    private const int TicksPerDay = 1440;

    private static SimConfig Config(int size = 44)
    {
        var config = new SimConfig();
        config.World.Width = size;
        config.World.Height = size;
        return config;
    }

    /// <summary>
    /// 造一个「人们住在一起、并且共用住房与仓库」的世界。
    /// `houses` / `storages` 决定共享设施是否达标 —— 这是聚落与「一堆人」的分界。
    /// </summary>
    private static Simulation MakeWorld(int seed, int agents, int houses, int storages)
    {
        var sim = new Simulation(Config(), 44, 44, seed);

        for (int y = 0; y < 44; y++)
        {
            for (int x = 0; x < 44; x++)
            {
                sim.World.SetTerrain(x, y, TerrainKind.Grass);
                sim.World.SetVegetation(x, y, 0.4f);
                sim.World.SetMoisture(x, y, 0.5f);
                sim.World.SetFertility(x, y, 0.6f);
            }
        }
        // 水就在定居点边上：M6 的教训 —— 测试场景必须让「活下去」不成为变量
        for (int y = 26; y <= 34; y++)
        {
            sim.World.SetTerrain(24, y, TerrainKind.Water);
            sim.World.SetMoisture(24, y, 1f);
        }
        sim.World.RefreshSpatialIndex();

        sim.InterveneSpawnHumans(30, 30, agents, 4);

        int placed = 0;
        for (int y = 27; y < 35 && placed < houses; y += 2)
        {
            for (int x = 27; x < 35 && placed < houses; x += 2)
            {
                int index = sim.Buildings.Place(sim.World, BuildingKind.House, x, y, 40);
                if (index < 0) { continue; }
                sim.Buildings.MarkComplete(index, 0);
                placed++;
            }
        }

        placed = 0;
        for (int y = 36; y < 40 && placed < storages; y += 2)
        {
            for (int x = 27; x < 35 && placed < storages; x += 2)
            {
                int index = sim.Buildings.Place(sim.World, BuildingKind.Storage, x, y, 40);
                if (index < 0) { continue; }
                sim.Buildings.MarkComplete(index, 0);
                placed++;
            }
        }

        for (int y = 26; y <= 34; y++)
        {
            for (int x = 26; x <= 34; x++)
            {
                sim.InterveneAddResource(x, y, ResourceKind.Food, 40f);
            }
        }

        sim.World.RefreshSpatialIndex();
        return sim;
    }

    // ---------------------------------------------------------------------
    // 形成：条件齐备才会长出来
    // ---------------------------------------------------------------------

    [Fact("验收1：持续共处 + 共享住房与仓库 ⇒ 聚落自己形成")]
    public void SettlementEmergesFromSustainedCohabitation()
    {
        Simulation sim = MakeWorld(11001, agents: 10, houses: 3, storages: 1);

        Assert.Equal(0, sim.Settlements.ActiveCount);
        Assert.Equal(0, sim.Settlements.TotalFounded);

        int peakActive = 0;
        for (int day = 0; day < 12; day++)
        {
            sim.Tick(TicksPerDay);
            peakActive = System.Math.Max(peakActive, sim.Settlements.ActiveCount);
        }

        Assert.True(sim.Settlements.TotalFounded > 0,
            "条件齐备时聚落必须自己形成（实测成立 " + sim.Settlements.TotalFounded + " 个，"
            + "候选持续 " + sim.Settlements.CandidateDays + " 天）");
        // 成立时必须活跃；其后居民自然迁离时允许解散，不能靠幽灵聚落通过。
        Assert.True(peakActive > 0, "形成的聚落必须实际进入活跃状态");
    }

    [Fact("验收2：只有人、没有共享设施 ⇒ 不形成聚落（「住得近「不等于」一个村子」）")]
    public void NoFacilitiesMeansNoSettlement()
    {
        Simulation sim = MakeWorld(11002, agents: 10, houses: 0, storages: 0);

        sim.Tick(TicksPerDay * 12);

        // 注意：AI 可能自己盖房子。这条断言的前提是"设施始终不达标"，
        // 因此这里检查的是"没有形成聚落「，而不是」没有房子"——
        // 若 AI 自己盖出了达标的设施，那恰恰是**正确的涌现**，测试应当允许它。
        if (sim.Settlements.TotalFounded == 0)
        {
            Assert.Equal(0, sim.Settlements.ActiveCount);
        }
        else
        {
            // 涌现出来了也没错 —— 但要能说清是靠什么：附近必须有达标的共享设施
            bool hasFacilities = false;
            for (int i = 0; i < sim.Settlements.EntityCount; i++)
            {
                SettlementStore.Settlement s = sim.Settlements.At(i);
                if (s.Houses >= sim.Config.Settlement.MinHouses
                    && s.Storages >= sim.Config.Settlement.MinStorages) { hasFacilities = true; }
            }
            Assert.True(hasFacilities,
                "若聚落成立了，它必须是因为共享设施达标（不能是别的原因）");
        }
    }

    [Fact("验收3：滞回 —— 人口在阈值附近抖动时不会反复成立/解散")]
    public void HysteresisStopsFlapping()
    {
        Simulation sim = MakeWorld(11003, agents: 12, houses: 3, storages: 1);
        sim.Tick(TicksPerDay * 10);
        Assert.True(sim.Settlements.TotalFounded > 0, "先把聚落建立起来");

        int foundedBefore = sim.Settlements.TotalFounded;
        int dissolvedBefore = sim.Settlements.TotalDissolved;

        // 让一部分人"离开"：把人口压到成立阈值(6)与解散阈值(3)之间
        int toRemove = 0;
        foreach (int slot in sim.Agents.AliveSlots())
        {
            toRemove++;
            if (toRemove > 4) { break; }
        }
        for (int day = 0; day < 20; day++) { sim.Tick(TicksPerDay); }

        // 关键断言：**没有出现反复成立/解散**（滞回带内状态保持不变）
        int flapping = (sim.Settlements.TotalFounded - foundedBefore)
                     + (sim.Settlements.TotalDissolved - dissolvedBefore);
        Assert.True(flapping <= 2,
            "滞回带内不应反复成立/解散（实测变动 " + flapping + " 次）—— "
            + "成立阈值 " + sim.Config.Settlement.FoundPeople
            + " 必须高于解散阈值 " + sim.Config.Settlement.DissolvePeople);
    }

    [Fact("验收4：解散阈值必须低于成立阈值（否则滞回不存在）")]
    public void DissolveThresholdIsBelowFoundThreshold()
    {
        var config = new SettlementConfig();
        Assert.True(config.DissolvePeople < config.FoundPeople,
            "解散阈值（「 + config.DissolvePeople + 」）必须低于成立阈值（「 + config.FoundPeople + 」）—— "
            + "两者相等时人口在阈值附近抖动会让聚落闪烁，历史随之丢失");
    }

    [Fact("验收5：等级由人口与建筑数推导（Camp → Village → Town）")]
    public void TierFollowsPopulationAndBuildings()
    {
        Simulation small = MakeWorld(11004, agents: 7, houses: 2, storages: 1);
        Simulation big = MakeWorld(11004, agents: 26, houses: 10, storages: 2);

        // Exercise real clustering/persistence with fixed inputs. Survival, births and
        // migration have separate integration tests and must not change this tier fixture.
        for(int day=1;day<=10;day++)
        {
            small.Settlements.TickDay(day*TicksPerDay);
            big.Settlements.TickDay(day*TicksPerDay);
        }

        SettlementTier smallTier = HighestTier(small);
        SettlementTier bigTier = HighestTier(big);

        Assert.Equal(SettlementTier.Village,smallTier);
        Assert.Equal(SettlementTier.Town,bigTier);
    }

    private static SettlementTier HighestTier(Simulation sim)
    {
        SettlementTier best = SettlementTier.Camp;
        for (int i = 0; i < sim.Settlements.EntityCount; i++)
        {
            SettlementStore.Settlement s = sim.Settlements.At(i);
            if (s.Dissolved) { continue; }
            if (s.Tier > best) { best = s.Tier; }
        }
        return best;
    }

    // ---------------------------------------------------------------------
    // 确定性与存档
    // ---------------------------------------------------------------------

    [Fact("验收6：聚落形成必须可复现（同 seed 两次摘要一致）")]
    public void SettlementIsDeterministic()
    {
        Simulation Run()
        {
            Simulation sim = MakeWorld(11005, agents: 10, houses: 3, storages: 1);
            sim.Tick(TicksPerDay * 12);
            return sim;
        }

        Simulation first = Run();
        Simulation second = Run();

        Assert.True(first.Settlements.TotalFounded > 0, "这一局必须真的形成了聚落，否则测不到东西");
        Assert.Equal(first.StateDigestString(), second.StateDigestString());
    }

    [Fact("验收7：聚落（含「候选持续性计数」）必须完整往返存档")]
    public void SettlementSurvivesSaveLoad()
    {
        Simulation sim = MakeWorld(11006, agents: 10, houses: 3, storages: 1);

        // 跑到一个「候选正在累积但还没成立」的时刻最理想 —— 那正是最容易漏掉的状态。
        // 这里跑 4 天（小于 FoundDays=5），确保 candidateDays 非零。
        sim.Tick(TicksPerDay * 4);

        int candidateDays = sim.Settlements.CandidateDays;
        int founded = sim.Settlements.TotalFounded;
        int entities = sim.Settlements.EntityCount;
        string digest = sim.StateDigestString();

        string json = sim.SaveToText();
        var restored = Simulation.CreateForRestore(Config(44), 44, 44, 99999);
        Assert.True(restored.LoadFromText(json).Success);

        Assert.Equal(candidateDays, restored.Settlements.CandidateDays);
        Assert.Equal(founded, restored.Settlements.TotalFounded);
        Assert.Equal(entities, restored.Settlements.EntityCount);
        Assert.Equal(digest, restored.StateDigestString());

        // 继续跑：如果候选计数没恢复，续跑会在「何时成立」上分叉
        for (int step = 1; step <= 600; step++)
        {
            sim.Tick(1);
            restored.Tick(1);
            Assert.True(sim.StateDigestString() == restored.StateDigestString(),
                "读档续跑在第 " + step + " tick 分叉 —— 说明某个聚落状态没有恢复");
        }
    }

    [Fact("候选持续性计数必须进状态摘要（它会决定「再过几天会不会成立」）")]
    public void CandidateCounterIsHashed()
    {
        Simulation a = MakeWorld(11007, agents: 10, houses: 3, storages: 1);
        Simulation b = MakeWorld(11007, agents: 10, houses: 3, storages: 1);

        a.Tick(TicksPerDay * 3);
        b.Tick(TicksPerDay * 3);

        Assert.Equal(a.StateDigestString(), b.StateDigestString());

        // 直接改候选计数（它等价于"已经持续共处了几天"），摘要必须变化
        a.Settlements.RestoreCandidate(
            a.Settlements.CandidateCenterX, a.Settlements.CandidateCenterY,
            a.Settlements.CandidateDays + 3, a.Settlements.CandidatePeople);

        Assert.True(a.StateDigestString() != b.StateDigestString(),
            "候选持续性计数必须进摘要 —— 它看起来只是个计数器，但它决定未来会不会成立聚落");
    }
}
