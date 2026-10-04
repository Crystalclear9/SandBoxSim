using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Save;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

/// <summary>
/// 存档确定性诊断工具（可复用，不是一次性的）。
///
/// # 为什么它值得保留而不是用完就删
///
/// 定位"读档后世界悄悄变成另一个"这类问题，靠的不是灵光一闪，而是一套**固定流程**：
///
///   1. 复刻出问题的规模，逐步 tick 找出**第一个分叉的 tick**；
///   2. 打印该 tick 的**分段摘要差异** —— 把"一个大数字对不上"缩小到"是哪一段"；
///   3. 若差异在实体段，逐个字段对比；若在 tiles 段，**逐个 tile** 对比；
///   4. 比对 8 条随机流的状态 —— 用来区分"漏了状态"与"随机源被扰动"。
///
/// 这套流程已经定位出**八个**"不进摘要或进了摘要但不进存档"的字段。
/// 每定位一个，分叉点就往后推。所以这个工具是**收敛过程的量具**，
/// 删掉它等于下次遇到同类问题要从头再写一遍。
///
/// 用法（**默认跳过**，用 `SBOX_SIM_PROBE=1` 开启）：
/// <code>
/// $env:SBOX_SIM_PROBE = '1'
/// dotnet src/SandBoxSim.Tests/bin/Debug/net8.0/SandBoxSim.Tests.dll --filter SaveDivergenceProbe
/// </code>
///
/// 规模用环境变量切换，因为不同的问题在不同的规模上才会暴露：
/// <code>
/// $env:SBOX_SIM_MAP = '60'    # 默认 60（对应 SaveLoadRoundTripPreservesDigest 的规模，跑得快）
/// $env:SBOX_SIM_MAP = '100'   # CLI 默认规模（Phase 0 的问题只在这里出现）
/// </code>
/// </summary>
public sealed class SaveDivergenceProbe
{
    private static int MapSize
    {
        get
        {
            string raw = System.Environment.GetEnvironmentVariable("SBOX_SIM_MAP") ?? "60";
            return int.TryParse(raw, out int size) && size > 0 ? size : 60;
        }
    }

    private const int Seed = 60 == 0 ? 11001 : 11001;   // 保持与失败用例同源
    private const int Agents = 14;
    private const int HalfDays = 8;
    private const int TicksPerDay = 1440;

    [Fact("诊断：读档续跑的第一个分叉 tick（需 SBOX_SIM_PROBE=1）")]
    public void Probe()
    {
        if (System.Environment.GetEnvironmentVariable("SBOX_SIM_PROBE") != "1")
        {
            System.Console.WriteLine("  [探针] 已跳过（设 SBOX_SIM_PROBE=1 运行）。");
            System.Console.WriteLine("  [探针] 常规回归由 SaveLoadTests 把关（秒级）。");
            Assert.Skip("需 SBOX_SIM_PROBE=1");
        }

        int size = MapSize;
        int half = TicksPerDay * HalfDays;
        int total = TicksPerDay * HalfDays * 2;

        System.Console.WriteLine("  [探针] 规模 " + size + "×" + size
            + "，seed " + Seed + "，人 " + Agents
            + "，存/续各 " + HalfDays + " 天");

        var directConfig = MakeConfig(size);
        var direct = new Simulation(directConfig, size, size, Seed);
        Flatten(direct);
        direct.InterveneSpawnHumans(size / 2, size / 2, Agents, 6);
        direct.Tick(half);

        var sourceConfig = MakeConfig(size);
        var source = new Simulation(sourceConfig, size, size, Seed);
        Flatten(source);
        source.InterveneSpawnHumans(size / 2, size / 2, Agents, 6);
        source.Tick(half);

        if (direct.StateDigestString() != source.StateDigestString())
        {
            System.Console.WriteLine("  [探针] 两个同源世界的摘要不同 —— 用例构造有误");
            return;
        }

        string json = source.SaveToText();

        // 与失败用例一致：**同一个 seed** 构造目标世界。
        // （Phase 0 那条坑是"必须用不同 seed"，这里用同一个 seed 是为了复刻当前这条失败。）
        var targetConfig = MakeConfig(size);
        var restored = Simulation.CreateForRestore(targetConfig, size, size, Seed);

        SaveFile.LoadResult load = restored.LoadFromText(json);
        System.Console.WriteLine("  [探针] 读档自校验：" + (load.DigestMatches ? "一致" : "不一致 -> " + load.SegmentDifference));

        DumpExactTileDeltaAtLoad(direct, restored);

        long firstDiff = -1;
        for (int i = 0; i < total - half; i++)
        {
            direct.Tick(1);
            restored.Tick(1);
            if (direct.StateDigestString() != restored.StateDigestString())
            {
                firstDiff = i + 1;
                break;
            }
        }

        System.Console.WriteLine("  [探针] 第一个分叉 tick（相对读档点）=" + firstDiff
            + "（绝对 tick " + (half + firstDiff) + "，"
            + "绝对 tick % 1440 = " + ((half + firstDiff) % TicksPerDay)
            + "，% 60 = " + ((half + firstDiff) % 60)
            + "，% 10 = " + ((half + firstDiff) % 10) + "）");

        if (firstDiff < 0)
        {
            System.Console.WriteLine("  [探针] 续跑段完全一致 —— 该规模下已收敛。");
            return;
        }

        System.Console.WriteLine("  [探针] 分段差异：" + StateHash.FirstSegmentDifference(
            StateHash.DescribeSegments(direct), StateHash.DescribeSegments(restored)));

        DumpSlotSets(direct, restored);
        DumpRngComparison(direct, restored);
        DumpFirstTileDifference(direct, restored);
        DumpCounts(direct, restored);
        DumpBuildingDelta(direct, restored);
        DumpAgentDelta(direct, restored);
        DumpAgentFieldDelta(direct, restored);
    }

    /// <summary>
    /// 逐个体的**每一个进摘要的字段**比对。
    ///
    /// 为什么需要它而不仅是"家族字段"那一版：分叉可能来自任何一项
    /// （需求、位置、动作、目标、库存、性格……），
    /// 而只打印家族字段会得到"什么都没不同"的假象。
    /// 这正是 Phase 0 定位 tiles 段时用过的同一招：**把字段清单穷举掉**。
    /// </summary>
    internal static void DumpAgentFieldDelta(Simulation a, Simulation b)
    {
        AgentStore x = a.Agents;
        AgentStore y = b.Agents;

        foreach (int slot in x.AliveSlots())
        {
            if (!y.IsSlotAlive(slot))
            {
                System.Console.WriteLine("  [探针] 槽位 " + slot + " 只在 direct 里活着");
                continue;
            }

            void Report(string field, string av, string bv)
            {
                System.Console.WriteLine("  [探针] 槽位 " + slot + " 字段 " + field
                    + " direct=" + av + " restored=" + bv);
            }

            if (x.XOf(slot) != y.XOf(slot)) { Report("x", x.XOf(slot).ToString(), y.XOf(slot).ToString()); }
            if (x.YOf(slot) != y.YOf(slot)) { Report("y", x.YOf(slot).ToString(), y.YOf(slot).ToString()); }
            if (x.HomeXOf(slot) != y.HomeXOf(slot)) { Report("homeX", x.HomeXOf(slot).ToString(), y.HomeXOf(slot).ToString()); }
            if (x.HungerOf(slot) != y.HungerOf(slot)) { Report("hunger", x.HungerOf(slot).ToString("R"), y.HungerOf(slot).ToString("R")); }
            if (x.FatigueOf(slot) != y.FatigueOf(slot)) { Report("fatigue", x.FatigueOf(slot).ToString("R"), y.FatigueOf(slot).ToString("R")); }
            if (x.ThirstOf(slot) != y.ThirstOf(slot)) { Report("thirst", x.ThirstOf(slot).ToString("R"), y.ThirstOf(slot).ToString("R")); }
            if (x.SocialOf(slot) != y.SocialOf(slot)) { Report("social", x.SocialOf(slot).ToString("R"), y.SocialOf(slot).ToString("R")); }
            if (x.HealthOf(slot) != y.HealthOf(slot)) { Report("health", x.HealthOf(slot).ToString("R"), y.HealthOf(slot).ToString("R")); }
            if (x.AgeDaysOf(slot) != y.AgeDaysOf(slot)) { Report("ageDays", x.AgeDaysOf(slot).ToString(), y.AgeDaysOf(slot).ToString()); }
            if (x.JobOf(slot) != y.JobOf(slot)) { Report("job", x.JobOf(slot).ToString(), y.JobOf(slot).ToString()); }
            if (x.StateOf(slot) != y.StateOf(slot)) { Report("state", x.StateOf(slot).ToString(), y.StateOf(slot).ToString()); }
            if (x.ActionOf(slot) != y.ActionOf(slot)) { Report("action", x.ActionOf(slot).ToString(), y.ActionOf(slot).ToString()); }
            if (x.PhaseOf(slot) != y.PhaseOf(slot)) { Report("phase", x.PhaseOf(slot).ToString(), y.PhaseOf(slot).ToString()); }
            if (x.TargetOf(slot).X != y.TargetOf(slot).X) { Report("targetX", x.TargetOf(slot).X.ToString(), y.TargetOf(slot).X.ToString()); }
            if (x.TargetOf(slot).Y != y.TargetOf(slot).Y) { Report("targetY", x.TargetOf(slot).Y.ToString(), y.TargetOf(slot).Y.ToString()); }
            if (x.ActionTicksOf(slot) != y.ActionTicksOf(slot)) { Report("actionTicks", x.ActionTicksOf(slot).ToString(), y.ActionTicksOf(slot).ToString()); }
            if (x.FailReasonOf(slot) != y.FailReasonOf(slot)) { Report("failReason", x.FailReasonOf(slot).ToString(), y.FailReasonOf(slot).ToString()); }
            if (x.InventoryOf(slot, ResourceKind.Food) != y.InventoryOf(slot, ResourceKind.Food)) { Report("invFood", x.InventoryOf(slot, ResourceKind.Food).ToString("R"), y.InventoryOf(slot, ResourceKind.Food).ToString("R")); }
            if (x.InventoryOf(slot, ResourceKind.Wood) != y.InventoryOf(slot, ResourceKind.Wood)) { Report("invWood", x.InventoryOf(slot, ResourceKind.Wood).ToString("R"), y.InventoryOf(slot, ResourceKind.Wood).ToString("R")); }
            if (x.InventoryOf(slot, ResourceKind.Stone) != y.InventoryOf(slot, ResourceKind.Stone)) { Report("invStone", x.InventoryOf(slot, ResourceKind.Stone).ToString("R"), y.InventoryOf(slot, ResourceKind.Stone).ToString("R")); }
            if (x.PersonalityOf(slot).Aggression != y.PersonalityOf(slot).Aggression) { Report("aggression", x.PersonalityOf(slot).Aggression.ToString("R"), y.PersonalityOf(slot).Aggression.ToString("R")); }
            if (x.PersonalityOf(slot).Industriousness != y.PersonalityOf(slot).Industriousness) { Report("industriousness", x.PersonalityOf(slot).Industriousness.ToString("R"), y.PersonalityOf(slot).Industriousness.ToString("R")); }
            if (x.DecisionPhaseOf(slot) != y.DecisionPhaseOf(slot)) { Report("decisionPhase", x.DecisionPhaseOf(slot).ToString(), y.DecisionPhaseOf(slot).ToString()); }
            if (x.NextDecisionTickOf(slot) != y.NextDecisionTickOf(slot)) { Report("nextDecisionTick", x.NextDecisionTickOf(slot).ToString(), y.NextDecisionTickOf(slot).ToString()); }
            if (x.MigrateUntilOf(slot) != y.MigrateUntilOf(slot)) { Report("migrateUntil", x.MigrateUntilOf(slot).ToString(), y.MigrateUntilOf(slot).ToString()); }
            if (x.MotherOf(slot) != y.MotherOf(slot)) { Report("mother", x.MotherOf(slot).ToString(), y.MotherOf(slot).ToString()); }
            if (x.FatherOf(slot) != y.FatherOf(slot)) { Report("father", x.FatherOf(slot).ToString(), y.FatherOf(slot).ToString()); }
            if (a.Actions.MoveProgressOf(slot) != b.Actions.MoveProgressOf(slot))
            {
                Report("moveProgress", a.Actions.MoveProgressOf(slot).ToString("R"), b.Actions.MoveProgressOf(slot).ToString("R"));
            }
            if (a.Migration.CooldownUntilOf(slot) != b.Migration.CooldownUntilOf(slot))
            {
                Report("migrationCooldown", a.Migration.CooldownUntilOf(slot).ToString(), b.Migration.CooldownUntilOf(slot).ToString());
            }
            if (x.HasPathStep(slot) != y.HasPathStep(slot)) { Report("hasPathStep", x.HasPathStep(slot).ToString(), y.HasPathStep(slot).ToString()); }
            if (x.HasPathStep(slot) && x.PathStepOf(slot) != y.PathStepOf(slot))
            {
                Report("pathStep", x.PathStepOf(slot).ToString(), y.PathStepOf(slot).ToString());
            }
            if (x.GenerationOf(slot) != y.GenerationOf(slot)) { Report("generation", x.GenerationOf(slot).ToString(), y.GenerationOf(slot).ToString()); }
        }
    }

    private static SimConfig MakeConfig(int size)
    {
        var config = new SimConfig();
        config.World.Width = size;
        config.World.Height = size;
        return config;
    }

    private static void Flatten(Simulation sim)
    {
        int size = sim.World.Width;
        int min = size / 6;
        int max = size - min;
        for (int y = min; y <= max; y++)
        {
            for (int x = min; x <= max; x++)
            {
                sim.World.SetTerrain(x, y, TerrainKind.Grass);
                sim.World.SetVegetation(x, y, 0.3f);
            }
        }
        sim.World.RefreshSpatialIndex();
    }

    /// <summary>
    /// 比对"哪些槽位活着"以及各自的**代次**。
    ///
    /// 为什么这一步单独做：代次差异说明"某个槽位里的个体被换过人"，
    /// 也就是**曾有一个人死掉、槽位被回收给新个体**。
    /// 这比"某个数值不同"是更本质的差异 —— 它说明两个世界的历史不同，
    /// 而不只是某一帧的取值不同。
    /// </summary>
    private static void DumpSlotSets(Simulation a, Simulation b)
    {
        AgentStore x = a.Agents;
        AgentStore y = b.Agents;

        for (int slot = 0; slot < System.Math.Min(x.Capacity, y.Capacity); slot++)
        {
            bool ax = x.IsSlotAlive(slot);
            bool by = y.IsSlotAlive(slot);
            if (ax == by && (!ax || x.GenerationOf(slot) == y.GenerationOf(slot))) { continue; }

            System.Console.WriteLine("  [探针] 槽位 " + slot
                + " 存活 direct=" + ax + " restored=" + by
                + " 代次 direct=" + x.GenerationOf(slot) + " restored=" + y.GenerationOf(slot));
        }

        if (x.Capacity != y.Capacity)
        {
            System.Console.WriteLine("  [探针] 容量不同：direct=" + x.Capacity + " restored=" + y.Capacity);
        }
        System.Console.WriteLine("  [探针] NextFreeHint direct=" + x.NextFreeHint + " restored=" + y.NextFreeHint
            + " | 建筑 NextFreeHint direct=" + a.Buildings.NextFreeHint + " restored=" + b.Buildings.NextFreeHint);

        // 代次之和与 MarkDead 计数。
        //
        // 为什么要单独看这两个：代次只在 `MarkDead` 里自增（或被 ClaimSlot 复用），
        // 而 `Stats.TotalDeaths` 只统计**经由需求系统上报**的死亡。
        // 两者不一致就说明"有人被 MarkDead 却没有被计入统计" ——
        // 那正是"槽位代次对不上但死亡数相同"这类矛盾的来源。
        int sumGenX = 0;
        int sumGenY = 0;
        int aliveX = 0;
        int aliveY = 0;
        for (int slot = 0; slot < System.Math.Min(x.Capacity, y.Capacity); slot++)
        {
            sumGenX += x.GenerationOf(slot);
            sumGenY += y.GenerationOf(slot);
            if (x.IsSlotAlive(slot)) { aliveX++; }
            if (y.IsSlotAlive(slot)) { aliveY++; }
        }

        System.Console.WriteLine("  [探针] 代次之和 direct=" + sumGenX + " restored=" + sumGenY
            + " | 存活计数 " + aliveX + "/" + aliveY
            + " | TotalDied direct=" + x.TotalDied + " restored=" + y.TotalDied);

        // 逐个列出"有代次或有死亡记录"的槽位 —— 这能直接指出两个世界各自死过谁。
        for (int slot = 0; slot < System.Math.Min(x.Capacity, y.Capacity); slot++)
        {
            bool interesting = x.GenerationOf(slot) != 0 || y.GenerationOf(slot) != 0
                || x.DeathTickOf(slot) >= 0 || y.DeathTickOf(slot) >= 0;
            if (!interesting) { continue; }

            System.Console.WriteLine("  [探针] 死亡槽位 " + slot
                + " | direct 存活=" + x.IsSlotAlive(slot) + " 代次=" + x.GenerationOf(slot)
                + " 死亡tick=" + x.DeathTickOf(slot)
                + " | restored 存活=" + y.IsSlotAlive(slot) + " 代次=" + y.GenerationOf(slot)
                + " 死亡tick=" + y.DeathTickOf(slot));
        }
    }

    private static void DumpRngComparison(Simulation a, Simulation b)
    {
        ulong[][] ra = a.Random.ExportState();
        ulong[][] rb = b.Random.ExportState();
        for (int s = 0; s < ra.Length && s < rb.Length; s++)
        {
            bool same = ra[s].Length == rb[s].Length;
            if (same)
            {
                for (int k = 0; k < ra[s].Length; k++)
                {
                    if (ra[s][k] != rb[s][k]) { same = false; break; }
                }
            }
            if (!same) { System.Console.WriteLine("  [探针] 随机流不同：" + (RngStream)s); }
        }
    }

    private static void DumpExactTileDeltaAtLoad(Simulation a, Simulation b)
    {
        Tile[] ta = a.World.Tiles;
        Tile[] tb = b.World.Tiles;
        int amountDiff = 0;
        int regenDiff = 0;
        for (int i = 0; i < ta.Length; i++)
        {
            if (ta[i].Resource.Amount != tb[i].Resource.Amount) { amountDiff++; }
            if (ta[i].Resource.RegenerationRate != tb[i].Resource.RegenerationRate) { regenDiff++; }
        }
        System.Console.WriteLine("  [探针] 读档瞬间精确比对：资源量差异 " + amountDiff
            + " 格，再生率差异 " + regenDiff + " 格");
    }

    private static void DumpFirstTileDifference(Simulation a, Simulation b)
    {
        Tile[] ta = a.World.Tiles;
        Tile[] tb = b.World.Tiles;
        int width = a.World.Width;

        for (int i = 0; i < ta.Length; i++)
        {
            ref readonly Tile x = ref ta[i];
            ref readonly Tile y = ref tb[i];
            bool same = x.Terrain == y.Terrain && x.Fire == y.Fire
                && x.Resource.Kind == y.Resource.Kind
                && x.Resource.Amount == y.Resource.Amount
                && x.Resource.Capacity == y.Resource.Capacity
                && x.Resource.RegenerationRate == y.Resource.RegenerationRate
                && x.Moisture == y.Moisture && x.Temperature == y.Temperature
                && x.Fertility == y.Fertility && x.Vegetation == y.Vegetation
                && x.BuildingId == y.BuildingId;
            if (same) { continue; }

            System.Console.WriteLine("  [探针] 首个不同的格子 (" + (i % width) + "," + (i / width) + ")");
            System.Console.WriteLine("  [探针]   direct   地形" + x.Terrain + " 资源" + x.Resource.Kind
                + " 量" + x.Resource.Amount.ToString("R") + " 容" + x.Resource.Capacity.ToString("R")
                + " 再生率" + x.Resource.RegenerationRate.ToString("R")
                + " 湿" + x.Moisture.ToString("R") + " 植" + x.Vegetation.ToString("R")
                + " 建筑" + x.BuildingId);
            System.Console.WriteLine("  [探针]   restored 地形" + y.Terrain + " 资源" + y.Resource.Kind
                + " 量" + y.Resource.Amount.ToString("R") + " 容" + y.Resource.Capacity.ToString("R")
                + " 再生率" + y.Resource.RegenerationRate.ToString("R")
                + " 湿" + y.Moisture.ToString("R") + " 植" + y.Vegetation.ToString("R")
                + " 建筑" + y.BuildingId);
            return;
        }
        System.Console.WriteLine("  [探针] 没有 tile 不同");
    }

    /// <summary>逐建筑逐字段比对 —— M4 新增了床位/劳动量/完整度，这一段现在是重点。</summary>
    private static void DumpBuildingDelta(Simulation a, Simulation b)
    {
        BuildingStore x = a.Buildings;
        BuildingStore y = b.Buildings;

        if (x.LiveCount != y.LiveCount)
        {
            System.Console.WriteLine("  [探针] 建筑数量不同：" + x.LiveCount + " vs " + y.LiveCount);
        }
        if (x.TotalBeds != y.TotalBeds)
        {
            System.Console.WriteLine("  [探针] 床位总数不同：" + x.TotalBeds + " vs " + y.TotalBeds);
        }
        if (x.OccupiedBeds != y.OccupiedBeds)
        {
            System.Console.WriteLine("  [探针] 已占床位不同：" + x.OccupiedBeds + " vs " + y.OccupiedBeds);
        }
        if (x.NextFreeHint != y.NextFreeHint)
        {
            System.Console.WriteLine("  [探针] 建筑 NextFreeHint 不同：" + x.NextFreeHint + " vs " + y.NextFreeHint);
        }

        for (int i = 0; i < System.Math.Min(x.Capacity, y.Capacity); i++)
        {
            if (!x.IsAlive(i) && !y.IsAlive(i)) { continue; }
            if (x.IsAlive(i) != y.IsAlive(i))
            {
                System.Console.WriteLine("  [探针] 建筑槽 " + i + " 存活不同：" + x.IsAlive(i) + " vs " + y.IsAlive(i));
                continue;
            }

            if (x.KindOf(i) != y.KindOf(i) || x.StateOf(i) != y.StateOf(i)
                || x.XOf(i) != y.XOf(i) || y.YOf(i) != y.YOf(i)
                || x.WorkDoneOf(i) != y.WorkDoneOf(i)
                || x.OccupiedBedsOf(i) != y.OccupiedBedsOf(i)
                || x.LaborOf(i) != y.LaborOf(i)
                || x.DecayOf(i) != y.DecayOf(i))
            {
                System.Console.WriteLine("  [探针] 建筑槽 " + i + " 不同："
                    + " 类型" + x.KindOf(i) + "/" + y.KindOf(i)
                    + " 状态" + x.StateOf(i) + "/" + y.StateOf(i)
                    + " 进度" + x.WorkDoneOf(i) + "/" + y.WorkDoneOf(i)
                    + " 占床" + x.OccupiedBedsOf(i) + "/" + y.OccupiedBedsOf(i)
                    + " 劳动" + x.LaborOf(i).ToString("R") + "/" + y.LaborOf(i).ToString("R")
                    + " 完整度" + x.DecayOf(i).ToString("R") + "/" + y.DecayOf(i).ToString("R"));
            }
        }
    }

    /// <summary>逐个体比对 M4 新增的家族/住所字段。</summary>
    private static void DumpAgentDelta(Simulation a, Simulation b)
    {
        AgentStore x = a.Agents;
        AgentStore y = b.Agents;

        if (x.LiveCount != y.LiveCount)
        {
            System.Console.WriteLine("  [探针] 人口不同：" + x.LiveCount + " vs " + y.LiveCount);
        }
        if (x.NextFreeHint != y.NextFreeHint)
        {
            System.Console.WriteLine("  [探针] Agent NextFreeHint 不同：" + x.NextFreeHint + " vs " + y.NextFreeHint);
        }

        int reported = 0;
        foreach (int slot in x.AliveSlots())
        {
            if (!y.IsSlotAlive(slot)) { continue; }
            if (x.PartnerOf(slot) != y.PartnerOf(slot)
                || x.ChildCountOf(slot) != y.ChildCountOf(slot)
                || x.DwellingOf(slot) != y.DwellingOf(slot)
                || x.LastBirthTickOf(slot) != y.LastBirthTickOf(slot)
                || x.LifeStageOf(slot) != y.LifeStageOf(slot))
            {
                System.Console.WriteLine("  [探针] 个体槽 " + slot + " 家族字段不同："
                    + " 伴侣" + x.PartnerOf(slot) + "/" + y.PartnerOf(slot)
                    + " 生育数" + x.ChildCountOf(slot) + "/" + y.ChildCountOf(slot)
                    + " 住所" + x.DwellingOf(slot) + "/" + y.DwellingOf(slot)
                    + " 上次生育" + x.LastBirthTickOf(slot) + "/" + y.LastBirthTickOf(slot)
                    + " 阶段" + x.LifeStageOf(slot) + "/" + y.LifeStageOf(slot));
                reported++;
                if (reported >= 3) { break; }
            }
        }
    }

    private static void DumpCounts(Simulation a, Simulation b)
    {
        System.Console.WriteLine("  [探针] 人口 " + a.Agents.LiveCount + " vs " + b.Agents.LiveCount
            + " | 动物 " + a.Wildlife.LiveCount + " vs " + b.Wildlife.LiveCount
            + " | 建筑 " + a.Buildings.LiveCount + " vs " + b.Buildings.LiveCount
            + " | 堆 " + a.GroundStocks.LiveCount + " vs " + b.GroundStocks.LiveCount);
        System.Console.WriteLine("  [探针] 统计 出生" + a.Stats.TotalBirths + "/" + b.Stats.TotalBirths
            + " 死亡" + a.Stats.TotalDeaths + "/" + b.Stats.TotalDeaths
            + " 迁移" + a.Stats.TotalMigrations + "/" + b.Stats.TotalMigrations
            + " 枯竭" + a.ResourceSystem.DepletionEvents + "/" + b.ResourceSystem.DepletionEvents
            + " | 出生系统计数 " + a.Births.TotalBirths + "/" + b.Births.TotalBirths);
        System.Console.WriteLine("  [探针] 脏块 " + a.World.Chunks.DirtyCount + " vs " + b.World.Chunks.DirtyCount);
    }
}
