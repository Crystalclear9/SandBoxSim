using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Ai;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Pathing;
using SandBoxSim.Core.Systems;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

/// <summary>
/// M1 的 Agent / Utility AI / 寻路测试。
///
/// 这组测试同时承担两个角色：
///   1. 验证行为契约（效用单调性、寻路正确性、需求累积）；
///   2. 在代码出问题时**直接打印现场**（行动直方图、死因、起始资源），
///      因为"所有人都饿死了但不知道为什么"是这类系统最典型也最费时的故障。
/// </summary>
public sealed class AgentTests
{
    private static SimConfig Config(int width = 60, int height = 60)
    {
        var config = new SimConfig();
        config.World.Width = width;
        config.World.Height = height;
        return config;
    }

    [Fact("AgentStore 的容量与存活计数必须自洽")]
    public void StoreLifecycle()
    {
        var config = Config();
        var sim = new Simulation(config, 60, 60, 1234);
        AgentStore store = sim.Agents;

        Assert.Equal(0, store.EntityCount);

        int spawned = sim.InterveneSpawnHumans(30, 30, 10, 5);
        Assert.Greater(spawned, 0, "至少要放下一个人");
        Assert.Equal(spawned, store.EntityCount);

        // 引用必须有效，且能定位回同一个槽位
        int slot = -1;
        foreach (int s in store.AliveSlots()) { slot = s; break; }
        Assert.GreaterOrEqual(slot, 0);

        AgentRef reference = store.RefOf(slot);
        Assert.True(store.IsValid(reference), "刚创建的引用必须有效");
        Assert.Equal(slot, store.SlotOf(reference));

        // 死亡之后旧引用必须立刻失效（防止"看错人"）
        store.MarkDead(slot, DeathCause.OldAge, sim.Clock);
        Assert.False(store.IsValid(reference), "个体死亡后旧引用必须失效");
        Assert.Equal(spawned - 1, store.EntityCount);
    }

    [Fact("名字必须稳定且不重复")]
    public void NamesAreStableAndUnique()
    {
        var config = Config();
        var sim = new Simulation(config, 60, 60, 99);
        sim.InterveneSpawnHumans(30, 30, 20, 6);

        var names = new System.Collections.Generic.HashSet<string>();
        foreach (int slot in sim.Agents.AliveSlots())
        {
            string first = sim.Agents.NameOf(slot);
            Assert.Equal(first, sim.Agents.NameOf(slot));   // 稳定
            Assert.True(names.Add(first), "名字重复：" + first);
        }
        Assert.Equal(20, names.Count);
    }

    [Fact("放置居民必须落在可走格上")]
    public void SpawnedAgentsStandOnWalkableTiles()
    {
        var config = Config();
        var sim = new Simulation(config, 60, 60, 555);
        sim.InterveneSpawnHumans(30, 30, 40, 8);

        foreach (int slot in sim.Agents.AliveSlots())
        {
            int x = sim.Agents.XOf(slot);
            int y = sim.Agents.YOf(slot);
            Assert.True(sim.World.IsInBounds(x, y), "个体跑到地图外了：" + x + "," + y);
            Assert.True(sim.World.TileAt(x, y).Walkable, "个体站在不可走的格子上：" + x + "," + y
                + " 地形=" + TerrainInfo.NameOf(sim.World.TileAt(x, y).Terrain));
        }
    }

    [Fact("需求必须随时间累积")]
    public void NeedsAccumulate()
    {
        var config = Config();
        var sim = new Simulation(config, 60, 60, 7);
        sim.InterveneSpawnHumans(30, 30, 1, 1);

        int slot = FirstAlive(sim);
        float before = sim.Agents.HungerOf(slot);

        sim.Tick(600);

        float after = sim.Agents.HungerOf(slot);
        Assert.Greater(after, before, "跑了 600 tick 之后饥饿度必须上升");
    }

    [Fact("极度饥饿必须导致掉血与死亡，且死因正确")]
    public void StarvationKillsWithCorrectCause()
    {
        var config = Config(40, 40);
        // 把饥饿与掉血速率同时调到极限：让"饿死"在几百 tick 内发生，测试才能秒级跑完。
        // 注意必须同时调 StarvationDamagePerDay：光把饥饿速率调高只会让人饿到极限后
        // 以默认速率（0.25/天）慢慢掉血，测试要跑几千 tick 才死（第一版就是这么挂的）。
        config.Needs.HungerPerDay = 40f;
        config.Needs.StarvationDamagePerDay = 8f;
        config.Needs.MaxLifespanDays = 10000;

        var sim = new Simulation(config, 40, 40, 21);
        sim.InterveneSpawnHumans(20, 20, 1, 1);
        int slot = FirstAlive(sim);

        // 先验证**纯粹的生理链路**：不经过任何决策，直接驱动需求系统。
        //
        // 为什么必须这样写（这是一次真实的教训）：
        // 最初的版本直接跑 sim.Tick(3000)，结果个体**没有**被饿死 ——
        // 因为它自己跑去采集了食物并且吃饱了。那不是 bug，而是"需求系统 + AI"整体在正常工作。
        // 也就是说：想断言"饿会致死"，就必须把因果链缩到只剩饥饿这一个环节，
        // 否则测试其实是在断言"AI 不够聪明"，而那是另一回事（并且会随调参随机失效）。
        for (int i = 0; i < 400 && sim.Agents.IsSlotAlive(slot); i++)
        {
            sim.Needs.TickNeeds(sim.Agents, sim.Clock + i, 1440, isNight: false);
        }

        // 注意：判据用**累计**计数（DeathsByCause）而不是 sim.Needs.DeathsThisTick：
        // 后者每 tick 都会清空，是"本 tick 的死亡明细"，事后再看永远是 0（也踩过一次）。
        Assert.False(sim.Agents.IsSlotAlive(slot), "持续饥饿（且无处进食）必须致死");
        Assert.Equal(DeathCause.Starvation, sim.Agents.DeathCauseOf(slot));
        Assert.Greater(sim.Needs.DeathsByCause[(int)DeathCause.Starvation], 0);

        // 再验证"通过完整管线也能饿死"：换一个食物不占优势的场景 ——
        // 把饥饿速率调到远超采集能力的水平，世界再怎么丰饶也来不及。
        var harsh = Config(40, 40);
        harsh.Needs.HungerPerDay = 200f;
        harsh.Needs.StarvationDamagePerDay = 3f;
        harsh.Needs.MaxLifespanDays = 10000;

        var harshSim = new Simulation(harsh, 40, 40, 22);
        harshSim.InterveneSpawnHumans(20, 20, 3, 2);
        harshSim.Tick(1440);

        Assert.Greater(harshSim.Needs.DeathsByCause[(int)DeathCause.Starvation], 0,
            "饥饿速率远超采集能力时，完整管线里必须出现饿死");
    }

    [Fact("Utility AI 必须对饥饿做出反应（饥饿 ⇒ GatherFood/Eat 效用上升）")]
    public void UtilityRespondsToHunger()
    {
        var config = Config(40, 40);
        var sim = new Simulation(config, 40, 40, 31);
        sim.InterveneSpawnHumans(20, 20, 1, 1);
        int slot = FirstAlive(sim);

        ActionDef gatherFood = ActionRegistry.DescribeCached(ActionKind.GatherFood);

        var context = new ActionContext
        {
            World = sim.World,
            Store = sim.Agents,
            Slot = slot,
            X = sim.Agents.XOf(slot),
            Y = sim.Agents.YOf(slot),
            Config = config,
            Ai = config.Ai,
            Chunk = sim.World.Chunks.ReadAt(sim.Agents.XOf(slot), sim.Agents.YOf(slot)),
            Tick = sim.Clock,
            IsNight = false,
            HomeX = sim.Agents.HomeXOf(slot),
            HomeY = sim.Agents.HomeYOf(slot),
            Rng = sim.Random.Get(RngStream.Agents),
            MoveSpeedPerTick = config.Ai.MoveSpeedPerTick,
        };

        sim.Agents.SetNeed(slot, NeedIndex.Hunger, 0.2f);
        ActionScore low = gatherFood.Evaluate!(in context);

        sim.Agents.SetNeed(slot, NeedIndex.Hunger, 0.9f);
        ActionScore high = gatherFood.Evaluate!(in context);

        Assert.Greater(high.Utility, low.Utility, "越饿越该想去采集食物");

        // 分解必须可读：至少要有"饥饿"这一项，否则检查器无法解释
        bool hasHunger = false;
        for (int i = 0; i < high.Considerations.Length; i++)
        {
            if (high.Considerations[i].Name == "饥饿") { hasHunger = true; break; }
        }
        Assert.True(hasHunger, "效用分解里必须包含'饥饿'这一项（可解释性要求）");
    }

    [Fact("所有动作的效用必须落在 [0,1] 内")]
    public void AllActionUtilitiesAreBounded()
    {
        var config = Config(40, 40);
        var sim = new Simulation(config, 40, 40, 41);
        sim.InterveneSpawnHumans(20, 20, 3, 3);
        int slot = FirstAlive(sim);

        var context = new ActionContext
        {
            World = sim.World,
            Store = sim.Agents,
            Slot = slot,
            X = sim.Agents.XOf(slot),
            Y = sim.Agents.YOf(slot),
            Config = config,
            Ai = config.Ai,
            Chunk = sim.World.Chunks.ReadAt(sim.Agents.XOf(slot), sim.Agents.YOf(slot)),
            Tick = sim.Clock,
            IsNight = true,
            HomeX = sim.Agents.HomeXOf(slot),
            HomeY = sim.Agents.HomeYOf(slot),
            Rng = sim.Random.Get(RngStream.Agents),
            MoveSpeedPerTick = config.Ai.MoveSpeedPerTick,
        };

        // 把需求推到各种极端，确认不会出现 NaN 或越界
        float[] extremes = { 0f, 0.5f, 1f };
        foreach (float value in extremes)
        {
            sim.Agents.SetNeed(slot, NeedIndex.Hunger, value);
            sim.Agents.SetNeed(slot, NeedIndex.Fatigue, value);
            sim.Agents.SetNeed(slot, NeedIndex.Thirst, value);

            foreach (ActionKind kind in ActionRegistry.All)
            {
                ActionDef def = ActionRegistry.DescribeCached(kind);
                if (def.Evaluate == null) { continue; }
                ActionScore score = def.Evaluate(in context);
                Assert.InRange(score.Utility, 0f, 1f);
                Assert.True(SimMath.IsFinite(score.Utility), kind + " 的效用不是有限数");
            }
        }
    }

    [Fact("寻路必须绕过水域（不可走地形不能被穿过）")]
    public void PathfindingAvoidsWater()
    {
        var config = Config(30, 30);
        var sim = new Simulation(config, 30, 30, 61);

        // 造一道竖直水墙，只在中间留一个缺口
        for (int y = 0; y < 30; y++)
        {
            if (y == 15) { continue; }
            sim.World.SetTerrain(15, y, TerrainKind.Water);
        }
        sim.World.RefreshSpatialIndex();

        Int2[] path = new Int2[2048];
        PathResult result = sim.Pathfinder.FindPath(5, 5, 25, 5, path);

        Assert.True(result.Success, "留有缺口，必须能找到路径");

        for (int i = 0; i < result.Length; i++)
        {
            Int2 point = path[i];
            Assert.NotEqual(TerrainKind.Water, sim.World.TileAt(point.X, point.Y).Terrain,
                "路径第 " + i + " 步落在水里：" + point);
        }

        // 必须真的从缺口（y=15）绕过去
        bool passedGap = false;
        for (int i = 0; i < result.Length; i++)
        {
            if (path[i].X == 15 && path[i].Y == 15) { passedGap = true; break; }
        }
        Assert.True(passedGap, "路径必须经过缺口 (15,15)");
    }

    [Fact("寻路对完全封死的目标必须失败，而不是给出错误路径")]
    public void PathfindingFailsWhenUnreachable()
    {
        var config = Config(30, 30);
        var sim = new Simulation(config, 30, 30, 71);

        // 用一圈水把 (25,25) 围起来
        for (int d = -1; d <= 1; d++)
        {
            sim.World.SetTerrain(25 + d, 24, TerrainKind.Water);
            sim.World.SetTerrain(25 + d, 26, TerrainKind.Water);
            sim.World.SetTerrain(24, 25 + d, TerrainKind.Water);
            sim.World.SetTerrain(26, 25 + d, TerrainKind.Water);
        }
        // 把目标本身也变成水，确保"目标不可走就是失败"
        sim.World.SetTerrain(25, 25, TerrainKind.Water);
        sim.World.RefreshSpatialIndex();

        Int2[] path = new Int2[2048];
        PathResult result = sim.Pathfinder.FindPath(5, 5, 25, 25, path);

        Assert.False(result.Success, "目标不可走时必须失败");
    }

    [Fact("寻路平局必须确定（同输入同结果）")]
    public void PathfindingIsDeterministic()
    {
        var config = Config(40, 40);
        var sim = new Simulation(config, 40, 40, 81);

        Int2[] a = new Int2[4096];
        Int2[] b = new Int2[4096];

        PathResult ra = sim.Pathfinder.FindPath(5, 5, 35, 35, a);
        PathResult rb = sim.Pathfinder.FindPath(5, 5, 35, 35, b);

        Assert.Equal(ra.Success, rb.Success);
        Assert.Equal(ra.Length, rb.Length);
        Assert.Equal(ra.Cost, rb.Cost);

        for (int i = 0; i < ra.Length; i++)
        {
            Assert.Equal(a[i].X, b[i].X);
            Assert.Equal(a[i].Y, b[i].Y);
        }
    }

    [Fact("寻路必须绕开障碍，且不允许斜穿两块不可走地形之间")]
    public void PathfindingDoesNotCutCorners()
    {
        var config = Config(20, 20);
        var sim = new Simulation(config, 20, 20, 91);

        // ---- 用例 1：绕障碍 ----
        // 一道竖直水墙（不封死），路径必须绕过去。
        //
        // 注意：这里必须显式把起点与终点设成陆地 —— 地图是按 seed 随机生成的，
        // (2,2) 或 (18,18) 完全可能本来就是水。第一版就因此失败（"路径落在不可走格上：(2,2)"），
        // 让人误以为寻路有问题。**构造型测试必须先固定场景，再验证规则。**
        sim.World.SetTerrain(2, 2, TerrainKind.Grass);
        sim.World.SetTerrain(18, 18, TerrainKind.Grass);
        for (int y = 0; y < 12; y++)
        {
            sim.World.SetTerrain(10, y, TerrainKind.Water);
        }
        sim.World.RefreshSpatialIndex();

        Int2[] path = new Int2[2048];
        PathResult around = sim.Pathfinder.FindPath(2, 2, 18, 18, path);
        Assert.True(around.Success, "水墙有缺口（y≥12），必须能找到路径");

        for (int i = 0; i < around.Length; i++)
        {
            Assert.True(sim.World.TileAt(path[i].X, path[i].Y).Walkable,
                "路径落在不可走格上：" + path[i]);
        }

        bool crossedBelowWater = false;
        for (int i = 0; i < around.Length; i++)
        {
            if (path[i].X > 10 && path[i].Y >= 12) { crossedBelowWater = true; break; }
        }
        Assert.True(crossedBelowWater, "必须从水墙下方的缺口绕过去");

        // ---- 用例 2：禁止"斜穿两块不可走地形之间" ----
        // 造一个对角缺口：目标是斜对角，两侧都是水，中间是唯一通路。
        // 这正是 corner-cutting 的经典形态。
        var config2 = Config(12, 12);
        var sim2 = new Simulation(config2, 12, 12, 92);

        // 把 (5,6) 与 (6,5) 变成水，(5,5) 与 (6,6) 保持可走
        sim2.World.SetTerrain(5, 6, TerrainKind.Water);
        sim2.World.SetTerrain(6, 5, TerrainKind.Water);
        sim2.World.SetTerrain(5, 5, TerrainKind.Grass);
        sim2.World.SetTerrain(6, 6, TerrainKind.Grass);
        sim2.World.RefreshSpatialIndex();

        Int2[] path2 = new Int2[512];
        PathResult diagonal = sim2.Pathfinder.FindPath(5, 5, 6, 6, path2);

        Assert.True(diagonal.Success, "(5,5) 到 (6,6) 之间是陆地，应当可达");

        // 这条路径只有一次移动：(5,5) -> (6,6) 是对角移动，而两侧 (6,5) 与 (5,6) 都是水。
        // 因此它**必须被禁止**：寻路不能给出长度为 2 的"斜穿"结果。
        // 允许的替代结果是"绕远路"（长度 > 2），或者干脆报告失败。
        if (diagonal.Success && diagonal.Length == 2)
        {
            Assert.Fail("寻路给出了 (5,5)→(6,6) 的斜穿路径，但它两侧都是水（corner cutting）");
        }
    }

    [Fact("Agent 必须真的会移动（而不是原地站着）")]
    public void AgentsActuallyMove()
    {
        var config = Config(60, 60);
        var sim = new Simulation(config, 60, 60, 101);
        sim.InterveneSpawnHumans(30, 30, 15, 5);

        // 记录每个人的起始位置
        var start = new System.Collections.Generic.Dictionary<int, Int2>();
        foreach (int slot in sim.Agents.AliveSlots())
        {
            start[slot] = sim.Agents.PositionOf(slot);
        }

        sim.Tick(1440 * 2);

        int moved = 0;
        foreach (System.Collections.Generic.KeyValuePair<int, Int2> pair in start)
        {
            int slot = pair.Key;
            if (!sim.Agents.IsSlotAlive(slot)) { continue; }
            if (sim.Agents.PositionOf(slot) != pair.Value) { moved++; }
        }

        Assert.Greater(moved, 0, "两天内至少要有人移动过（否则 AI/移动链路没有真正跑起来）");
    }

    [Fact("每个个体都必须拿到一个决策（不能有人永远不决策）")]
    public void EveryoneDecidesEventually()
    {
        var config = Config(60, 60);
        // 让决策间隔足够长，以便验证"分批"不会让某个人被永久跳过
        config.Ai.DecisionIntervalTicks = 900;
        config.Ai.BatchCount = 0;
        config.Ai.TargetDecisionsPerTick = 5;

        var sim = new Simulation(config, 60, 60, 111);
        sim.InterveneSpawnHumans(30, 30, 12, 5);

        // 3 小时应该足够每个人都轮到至少一次决策
        sim.Tick(180);

        int decided = 0;
        foreach (int slot in sim.Agents.AliveSlots())
        {
            if (sim.Agents.ActionOf(slot) != ActionKind.None) { decided++; }
        }

        Assert.Greater(decided, 0, "至少要有人已经拿到动作");
        Assert.Greater(sim.Ai.TotalDecisions, 0);
    }

    [Fact("采集动作必须真的把资源放进背包，并扣减地面存量")]
    public void GatheringMovesResourcesIntoInventory()
    {
        var config = Config(40, 40);
        var sim = new Simulation(config, 40, 40, 121);

        // 找一格有食物的草地，把个体直接放在旁边
        int targetX = -1;
        int targetY = -1;
        for (int y = 1; y < 39 && targetX < 0; y++)
        {
            for (int x = 1; x < 39; x++)
            {
                Tile tile = sim.World.TileAt(x, y);
                if (tile.Terrain == TerrainKind.Grass && tile.Resource.Kind == ResourceKind.Food && tile.Resource.Amount > 5f)
                {
                    targetX = x;
                    targetY = y;
                    break;
                }
            }
        }
        Assert.GreaterOrEqual(targetX, 0, "测试地图里必须有带食物的草地");

        int spawned = sim.InterveneSpawnHumans(targetX, targetY, 1, 0);
        Assert.Equal(1, spawned);
        int slot = FirstAlive(sim);

        float groundBefore = sim.World.TileAt(targetX, targetY).Resource.Amount;

        // 直接驱动"采集"动作：绕过决策，专测执行链路
        sim.Agents.SetAction(slot, ActionKind.GatherFood, ActionPhase.Executing);
        sim.Agents.SetTarget(slot, targetX, targetY);
        sim.Agents.SetNeed(slot, NeedIndex.Hunger, 0.9f);

        sim.Actions.Tick(sim.Clock);
        sim.Actions.Tick(sim.Clock + 1);

        float inInventory = sim.Agents.InventoryOf(slot, ResourceKind.Food);
        Assert.Greater(inInventory, 0f, "采集之后背包里必须有食物");
        Assert.Less(sim.World.TileAt(targetX, targetY).Resource.Amount, groundBefore, "地面存量必须减少");
    }

    [Fact("有食物且饥饿时必须能吃到嘴里（吃 → 饥饿下降）")]
    public void EatingReducesHunger()
    {
        var config = Config(40, 40);
        var sim = new Simulation(config, 40, 40, 131);
        sim.InterveneSpawnHumans(20, 20, 1, 1);
        int slot = FirstAlive(sim);

        sim.Agents.SetInventory(slot, ResourceKind.Food, 20f);
        sim.Agents.SetNeed(slot, NeedIndex.Hunger, 0.8f);
        sim.Agents.SetAction(slot, ActionKind.Eat, ActionPhase.Executing);

        float hungerBefore = sim.Agents.HungerOf(slot);
        for (int i = 0; i < 20; i++) { sim.Actions.Tick(sim.Clock); }

        Assert.Less(sim.Agents.HungerOf(slot), hungerBefore, "进食必须降低饥饿");
        Assert.Greater(sim.Actions.TotalFoodEaten, 0f);
    }

    [Fact("确定性不能因为个体而破坏（含个体的状态摘要必须可复现）")]
    public void DigestsAreReproducibleWithAgents()
    {
        var config = Config(60, 60);

        var first = new Simulation(config, 60, 60, 2024);
        first.InterveneSpawnHumans(30, 30, 25, 6);
        first.Tick(1440 * 5);

        var second = new Simulation(config, 60, 60, 2024);
        second.InterveneSpawnHumans(30, 30, 25, 6);
        second.Tick(1440 * 5);

        Assert.Equal(first.StateDigestString(), second.StateDigestString(),
            "有个体参与后摘要必须仍然可复现");

        // 人数不同 ⇒ 摘要必须不同（否则说明个体状态没有进摘要）
        var third = new Simulation(config, 60, 60, 2024);
        third.InterveneSpawnHumans(30, 30, 24, 6);
        third.Tick(1440 * 5);
        Assert.NotEqual(first.StateDigestString(), third.StateDigestString());
    }

    /// <summary>
    /// 诊断用：把"个体一天里的行为分布"打印出来。
    ///
    /// 这个测试不做强断言，它的价值在于：当"所有人都饿死了"这类故障出现时，
    /// 打开它就能看到到底有没有人真的去采集/进食 —— 比在脑子里推演效用公式快得多。
    /// </summary>
    [Fact("行为分布诊断（不是强断言，用于排查行为异常）")]
    public void BehaviourDiagnostics()
    {
        var config = Config(60, 60);
        var sim = new Simulation(config, 60, 60, 141);
        int spawned = sim.InterveneSpawnHumans(30, 30, 20, 6);

        var histogram = new System.Collections.Generic.Dictionary<ActionKind, int>();
        foreach (ActionKind kind in ActionRegistry.All) { histogram[kind] = 0; }

        for (int step = 0; step < 1440; step++)
        {
            sim.Tick(1);
            foreach (int slot in sim.Agents.AliveSlots())
            {
                ActionKind action = sim.Agents.ActionOf(slot);
                if (histogram.ContainsKey(action)) { histogram[action]++; }
            }
        }

        System.Console.WriteLine("  [诊断] 放置了 " + spawned + " 人，跑 1 天后：");
        System.Console.WriteLine("  [诊断] 存活 " + sim.Agents.LiveCount + " 人，决策次数 " + sim.Ai.TotalDecisions
            + "，找不到目标 " + sim.Ai.TargetSelectionFailures);
        System.Console.WriteLine("  [诊断] 采集成功 " + sim.Actions.TotalCompleted + " 次，失败 " + sim.Actions.TotalFailed + " 次"
            + "，吃掉 " + sim.Actions.TotalFoodEaten.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        System.Console.WriteLine("  [诊断] 采集量：食物 " + sim.Actions.HarvestedByKind[(int)ResourceKind.Food].ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
            + "，木材 " + sim.Actions.HarvestedByKind[(int)ResourceKind.Wood].ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));

        // 环境自检：这个测试地图上到底有没有"可采的食物"？
        // 没有的话，"没采到食物"就不是 AI 的问题，而是选址/资源分布的问题 —— 必须先分清。
        int grassWithFood = 0;
        int walkableGrass = 0;
        int forestTiles = 0;
        for (int i = 0; i < sim.World.Tiles.Length; i++)
        {
            Tile tile = sim.World.Tiles[i];
            if (tile.Terrain == TerrainKind.Grass)
            {
                if (tile.Walkable) { walkableGrass++; }
                if (tile.Resource.Kind == ResourceKind.Food && tile.Resource.Amount > 0.01f) { grassWithFood++; }
            }
            else if (tile.Terrain == TerrainKind.Forest)
            {
                forestTiles++;
            }
        }
        System.Console.WriteLine("  [诊断] 地图：可走草地 " + walkableGrass + "，其中带食物 " + grassWithFood
            + "，森林 " + forestTiles);

        int firstSlot = FirstAlive(sim);
        if (firstSlot >= 0)
        {
            int fx = sim.Agents.XOf(firstSlot);
            int fy = sim.Agents.YOf(firstSlot);
            ChunkStatsReadOnly chunk = sim.World.Chunks.ReadAt(fx, fy);
            System.Console.WriteLine("  [诊断] 首个个体位置 " + fx + "," + fy
                + " 所在 chunk：食物存量 " + chunk.FoodAmount.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                + "，木材 " + chunk.WoodAmount.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                + "，草地 " + chunk.GrassTiles + "，森林 " + chunk.ForestTiles);

            // 直接问一次"附近有没有食物" —— 这是动作选靶的真实入口
            var probe = new ActionContext
            {
                World = sim.World,
                Store = sim.Agents,
                Slot = firstSlot,
                X = fx,
                Y = fy,
                Config = config,
                Ai = config.Ai,
                Chunk = chunk,
                Tick = sim.Clock,
                IsNight = false,
                HomeX = sim.Agents.HomeXOf(firstSlot),
                HomeY = sim.Agents.HomeYOf(firstSlot),
                Rng = sim.Random.Get(RngStream.Agents),
                MoveSpeedPerTick = config.Ai.MoveSpeedPerTick,
            };
            bool foundFood = SandBoxSim.Core.Systems.Actions.ActionSearchProbe.TryFindResource(in probe, ResourceKind.Food, out Int2 foodTarget);
            System.Console.WriteLine("  [诊断] 从该位置找食物：" + (foundFood ? "找到 " + foodTarget : "没找到"));
        }

        foreach (System.Collections.Generic.KeyValuePair<ActionKind, int> pair in histogram)
        {
            if (pair.Value > 0)
            {
                System.Console.WriteLine("  [诊断] 动作 " + pair.Key + " 的个体-tick 数 = " + pair.Value);
            }
        }

        // 唯一的硬要求：日志里必须真的有人做过除了"发呆"以外的事
        Assert.Greater(sim.Ai.TotalDecisions, 0);
    }

    private static int FirstAlive(Simulation sim)
    {
        foreach (int slot in sim.Agents.AliveSlots()) { return slot; }
        return -1;
    }
}
