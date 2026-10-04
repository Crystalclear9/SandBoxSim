using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

/// <summary>
/// M6 #4 的定位诊断：读档后续跑在 `agents` 段分叉（需 `SBOX_SIM_M6_OPEN=1`）。
///
/// # 它回答的问题
///
/// 逐 tick 诊断已经给出：读档瞬间摘要一致，**第一个分叉在读档后第 248 tick，分段是 `agents`**。
/// 也就是说有一个"属于个体、会影响未来行为、却既没进存档也没进摘要"的字段。
///
/// 这个诊断做的事与 Phase 0 定位 `Resource.RegenerationRate` 时完全一样：
/// **把每一个进摘要的字段逐个比一遍，指名道姓地说出是哪一个不同。**
/// 上一轮已经用排除法缩小了范围（性格、需求、关系、相位、目标、moveProgress、
/// 迁移冷却都不是），所以这里要的是**穷举**，不是再猜一轮。
/// </summary>
public sealed class M6SaveDiagnostics
{
    private const int TicksPerDay = 1440;

    private static bool Enabled
        => System.Environment.GetEnvironmentVariable("SBOX_SIM_M6_OPEN") == "1";

    private static SimConfig Config(int size = 44)
    {
        var config = new SimConfig();
        config.World.Width = size;
        config.World.Height = size;
        return config;
    }

    private static Simulation MakeWorld(int seed)
    {
        var sim = new Simulation(Config(), 44, 44, seed);
        for (int y = 0; y < 44; y++)
        {
            for (int x = 0; x < 44; x++)
            {
                bool nearCenter = System.Math.Abs(x - 30) <= 8 && System.Math.Abs(y - 30) <= 8;
                sim.World.SetTerrain(x, y, nearCenter ? TerrainKind.Grass : TerrainKind.Forest);
                sim.World.SetVegetation(x, y, nearCenter ? 0.3f : 0.8f);
                sim.World.SetMoisture(x, y, 0.4f);
                sim.World.SetFertility(x, y, 0.6f);
            }
        }
        for (int y = 18; y <= 26; y++)
        {
            sim.World.SetTerrain(16, y, TerrainKind.Water);
            sim.World.SetMoisture(16, y, 1f);
        }
        for (int y = 16; y <= 28; y++)
        {
            for (int x = 17; x <= 20; x++)
            {
                sim.World.SetTerrain(x, y, TerrainKind.Grass);
                sim.World.SetVegetation(x, y, 0.3f);
            }
        }
        sim.World.RefreshSpatialIndex();
        sim.InterveneSpawnHumans(30, 30, 14, 5);

        for (int y = 28; y <= 32; y++)
        {
            for (int x = 28; x <= 32; x++)
            {
                sim.InterveneAddResource(x, y, ResourceKind.Food, 60f);
                sim.InterveneAddResource(x, y, ResourceKind.Wood, 40f);
            }
        }
        return sim;
    }

    [Fact("诊断：读档续跑在 agents 段分叉时，指出是哪一个字段（需 SBOX_SIM_M6_OPEN=1）")]
    public void DiagnoseAgentFieldDivergence()
    {
        if (!Enabled) { return; }

        Simulation direct = MakeWorld(9007);
        direct.Tick(TicksPerDay * 15);

        string json = direct.SaveToText();

        Simulation restored = Simulation.CreateForRestore(Config(44), 44, 44, 99999);
        Assert.True(restored.LoadFromText(json).Success, "读档本身必须成功");
        Assert.True(direct.StateDigestString() == restored.StateDigestString(),
            "读档瞬间的摘要必须一致（否则问题在恢复阶段，而不是在续跑阶段）");

        // # 判别实验：load vs load
        //
        // 从**同一份 JSON** 再读一次，然后让两个读档世界一起跑。
        //   * 如果 load-vs-load 也分叉 ⇒ 问题在**读档路径残留的隐藏状态**
        //     （最可疑的是 A* 的开放集：它在搜索结束时靠一个"压入过谁"的列表来清空，
        //      若某条提前返回的路径没走清空，堆里就会留下上一次搜索的节点）；
        //   * 如果 load-vs-load 一致、而 direct-vs-load 分叉 ⇒ 是 direct 那边的"热状态"，
        //     而不是恢复得不完整。
        // 这两者的修法完全不同，所以必须先分开。
        Simulation restored2 = Simulation.CreateForRestore(Config(44), 44, 44, 99999);
        Assert.True(restored2.LoadFromText(json).Success);
        for (int step = 1; step <= 600; step++)
        {
            restored.Tick(1);
            restored2.Tick(1);
            if (restored.StateDigestString() != restored2.StateDigestString())
            {
                System.Console.WriteLine("  [存档诊断] load-vs-load 在第 " + step + " tick 分叉 ⇒ 读档路径残留隐藏状态");
                break;
            }
            if (step == 600) { System.Console.WriteLine("  [存档诊断] load-vs-load 600 tick 完全一致"); }
        }

        // 复位到读档点重新开始 direct-vs-load 的比较
        restored = Simulation.CreateForRestore(Config(44), 44, 44, 99999);
        Assert.True(restored.LoadFromText(json).Success);

        int firstDiff = -1;
        for (int step = 1; step <= 600; step++)
        {
            direct.Tick(1);
            restored.Tick(1);
            if (direct.StateDigestString() != restored.StateDigestString()) { firstDiff = step; break; }
        }

        System.Console.WriteLine("  [存档诊断] 第一个分叉 tick（相对读档点）=" + firstDiff
            + "，分段差异：" + StateHash.FirstSegmentDifference(
                StateHash.DescribeSegments(direct), StateHash.DescribeSegments(restored)));

        if (firstDiff < 0)
        {
            System.Console.WriteLine("  [存档诊断] 600 tick 内没有分叉。");
            return;
        }

        DumpAgentFields(direct, restored);
        DumpRelationships(direct, restored);
    }

    /// <summary>逐字段比对所有存活个体的全部"进摘要"字段。</summary>
    private static void DumpAgentFields(Simulation a, Simulation b)
    {
        AgentStore x = a.Agents;
        AgentStore y = b.Agents;

        System.Console.WriteLine("  [存档诊断] 存活 " + x.LiveCount + " / " + y.LiveCount
            + "，容量 " + x.Capacity + " / " + y.Capacity);

        int reported = 0;
        foreach (int slot in x.AliveSlots())
        {
            if (slot >= y.Capacity || !y.IsSlotAlive(slot))
            {
                System.Console.WriteLine("  [存档诊断] 槽位 " + slot + " 存活状态不同");
                continue;
            }

            void R(string field, object av, object bv)
            {
                if (reported++ > 40) { return; }
                System.Console.WriteLine("  [存档诊断] 槽位 " + slot + " 字段 " + field
                    + " direct=" + av + " restored=" + bv);
            }

            if (x.XOf(slot) != y.XOf(slot)) { R("x", x.XOf(slot), y.XOf(slot)); }
            if (x.YOf(slot) != y.YOf(slot)) { R("y", x.YOf(slot), y.YOf(slot)); }
            if (x.HomeXOf(slot) != y.HomeXOf(slot)) { R("homeX", x.HomeXOf(slot), y.HomeXOf(slot)); }
            if (x.HomeYOf(slot) != y.HomeYOf(slot)) { R("homeY", x.HomeYOf(slot), y.HomeYOf(slot)); }
            if (x.HungerOf(slot) != y.HungerOf(slot)) { R("hunger", x.HungerOf(slot), y.HungerOf(slot)); }
            if (x.FatigueOf(slot) != y.FatigueOf(slot)) { R("fatigue", x.FatigueOf(slot), y.FatigueOf(slot)); }
            if (x.ThirstOf(slot) != y.ThirstOf(slot)) { R("thirst", x.ThirstOf(slot), y.ThirstOf(slot)); }
            if (x.SocialOf(slot) != y.SocialOf(slot)) { R("social", x.SocialOf(slot), y.SocialOf(slot)); }
            if (x.HealthOf(slot) != y.HealthOf(slot)) { R("health", x.HealthOf(slot), y.HealthOf(slot)); }
            if (x.AgeDaysOf(slot) != y.AgeDaysOf(slot)) { R("ageDays", x.AgeDaysOf(slot), y.AgeDaysOf(slot)); }
            if (x.JobOf(slot) != y.JobOf(slot)) { R("job", x.JobOf(slot), y.JobOf(slot)); }
            if (x.StateOf(slot) != y.StateOf(slot)) { R("state", x.StateOf(slot), y.StateOf(slot)); }
            if (x.ActionOf(slot) != y.ActionOf(slot)) { R("action", x.ActionOf(slot), y.ActionOf(slot)); }
            if (x.PhaseOf(slot) != y.PhaseOf(slot)) { R("phase", x.PhaseOf(slot), y.PhaseOf(slot)); }
            if (x.TargetOf(slot).X != y.TargetOf(slot).X) { R("targetX", x.TargetOf(slot).X, y.TargetOf(slot).X); }
            if (x.TargetOf(slot).Y != y.TargetOf(slot).Y) { R("targetY", x.TargetOf(slot).Y, y.TargetOf(slot).Y); }
            if (x.ActionTicksOf(slot) != y.ActionTicksOf(slot)) { R("actionTicks", x.ActionTicksOf(slot), y.ActionTicksOf(slot)); }
            if (x.FailReasonOf(slot) != y.FailReasonOf(slot)) { R("failReason", x.FailReasonOf(slot), y.FailReasonOf(slot)); }
            if (x.InventoryOf(slot, ResourceKind.Food) != y.InventoryOf(slot, ResourceKind.Food)) { R("invFood", x.InventoryOf(slot, ResourceKind.Food), y.InventoryOf(slot, ResourceKind.Food)); }
            if (x.InventoryOf(slot, ResourceKind.Wood) != y.InventoryOf(slot, ResourceKind.Wood)) { R("invWood", x.InventoryOf(slot, ResourceKind.Wood), y.InventoryOf(slot, ResourceKind.Wood)); }
            if (x.InventoryOf(slot, ResourceKind.Stone) != y.InventoryOf(slot, ResourceKind.Stone)) { R("invStone", x.InventoryOf(slot, ResourceKind.Stone), y.InventoryOf(slot, ResourceKind.Stone)); }
            if (x.PersonalityOf(slot).Aggression != y.PersonalityOf(slot).Aggression) { R("aggression", x.PersonalityOf(slot).Aggression, y.PersonalityOf(slot).Aggression); }
            if (x.PersonalityOf(slot).Greed != y.PersonalityOf(slot).Greed) { R("greed", x.PersonalityOf(slot).Greed, y.PersonalityOf(slot).Greed); }
            if (x.PersonalityOf(slot).Kindness != y.PersonalityOf(slot).Kindness) { R("kindness", x.PersonalityOf(slot).Kindness, y.PersonalityOf(slot).Kindness); }
            if (x.PersonalityOf(slot).Bravery != y.PersonalityOf(slot).Bravery) { R("bravery", x.PersonalityOf(slot).Bravery, y.PersonalityOf(slot).Bravery); }
            if (x.PersonalityOf(slot).Industriousness != y.PersonalityOf(slot).Industriousness) { R("industriousness", x.PersonalityOf(slot).Industriousness, y.PersonalityOf(slot).Industriousness); }
            if (x.PersonalityOf(slot).Sociability != y.PersonalityOf(slot).Sociability) { R("sociability", x.PersonalityOf(slot).Sociability, y.PersonalityOf(slot).Sociability); }
            if (x.DecisionPhaseOf(slot) != y.DecisionPhaseOf(slot)) { R("decisionPhase", x.DecisionPhaseOf(slot), y.DecisionPhaseOf(slot)); }
            if (x.NextDecisionTickOf(slot) != y.NextDecisionTickOf(slot)) { R("nextDecisionTick", x.NextDecisionTickOf(slot), y.NextDecisionTickOf(slot)); }
            if (x.MigrateUntilOf(slot) != y.MigrateUntilOf(slot)) { R("migrateUntil", x.MigrateUntilOf(slot), y.MigrateUntilOf(slot)); }
            if (x.MotherOf(slot) != y.MotherOf(slot)) { R("mother", x.MotherOf(slot), y.MotherOf(slot)); }
            if (x.FatherOf(slot) != y.FatherOf(slot)) { R("father", x.FatherOf(slot), y.FatherOf(slot)); }
            if (x.PartnerOf(slot) != y.PartnerOf(slot)) { R("partner", x.PartnerOf(slot), y.PartnerOf(slot)); }
            if (x.ChildCountOf(slot) != y.ChildCountOf(slot)) { R("childCount", x.ChildCountOf(slot), y.ChildCountOf(slot)); }
            if (x.DwellingOf(slot) != y.DwellingOf(slot)) { R("dwelling", x.DwellingOf(slot), y.DwellingOf(slot)); }
            if (x.LastBirthTickOf(slot) != y.LastBirthTickOf(slot)) { R("lastBirthTick", x.LastBirthTickOf(slot), y.LastBirthTickOf(slot)); }
            if (x.LifeStageOf(slot) != y.LifeStageOf(slot)) { R("lifeStage", x.LifeStageOf(slot), y.LifeStageOf(slot)); }
            if (x.GenerationOf(slot) != y.GenerationOf(slot)) { R("generation", x.GenerationOf(slot), y.GenerationOf(slot)); }
            if (a.Actions.MoveProgressOf(slot) != b.Actions.MoveProgressOf(slot)) { R("moveProgress", a.Actions.MoveProgressOf(slot), b.Actions.MoveProgressOf(slot)); }
            if (a.Migration.CooldownUntilOf(slot) != b.Migration.CooldownUntilOf(slot)) { R("migrationCooldown", a.Migration.CooldownUntilOf(slot), b.Migration.CooldownUntilOf(slot)); }
            if (x.HasPathStep(slot) != y.HasPathStep(slot)) { R("hasPathStep", x.HasPathStep(slot), y.HasPathStep(slot)); }
            if (x.HasPathStep(slot) && x.PathStepOf(slot) != y.PathStepOf(slot)) { R("pathStep", x.PathStepOf(slot), y.PathStepOf(slot)); }
        }

        System.Console.WriteLine("  [存档诊断] 字段层共报告 " + reported + " 处差异。");
    }

    private static void DumpRelationships(Simulation a, Simulation b)
    {
        var pa = a.Relationships.PairsAscending();
        var pb = b.Relationships.PairsAscending();

        System.Console.WriteLine("  [存档诊断] 关系条数 " + pa.Count + " / " + pb.Count);

        int n = System.Math.Min(pa.Count, pb.Count);
        int diff = 0;
        for (int i = 0; i < n && diff < 10; i++)
        {
            if (pa[i].Key != pb[i].Key
                || pa[i].Value.Affinity != pb[i].Value.Affinity
                || pa[i].Value.Interactions != pb[i].Value.Interactions)
            {
                diff++;
                System.Console.WriteLine("  [存档诊断] 关系差异 key=" + pa[i].Key + "/" + pb[i].Key
                    + " 亲和 " + pa[i].Value.Affinity + "/" + pb[i].Value.Affinity
                    + " 互动 " + pa[i].Value.Interactions + "/" + pb[i].Value.Interactions);
            }
        }
        System.Console.WriteLine("  [存档诊断] 关系层共 " + diff + " 处差异。");
    }
}
