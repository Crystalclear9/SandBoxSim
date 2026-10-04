using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Systems;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

/// <summary>
/// M7 的**批量验收**（需 `SBOX_SIM_BATCH=1`）。
///
/// # 为什么它必须是一批种子，而不是一个场景
///
/// 任务书对 M7 的验收是统计性的：
///   * **≥15/20 个种子在 150 天内形成聚落**；
///   * **≥5/20 个种子在 200 天内有 ≥2 个聚落**。
///
/// 这两条**不能**用一个手工摆好的场景来证明 —— 那种场景只说明"我摆得出来"。
/// 它要回答的是"**这个世界自己会不会长出聚落**"，而世界的初始条件由种子决定。
/// 所以必须跑一批种子，看比例。
///
/// # 为什么它默认关闭
///
/// 20 个种子 × 100×100 × 150~200 天是**几十分钟**的量级。
/// 它属于「验收」而不是"回归"：改完机制跑一次，而不是每改一行都跑。
/// 需要时用 `SBOX_SIM_BATCH=1` 打开。
///
/// # 它会打印什么
///
/// 每个种子的：是否形成聚落、成立时间、最高等级、聚落数。
/// 这些数字本身就是这一阶段的交付证据 —— 它们会进 CHANGELOG 与 docs/12。
/// </summary>
public sealed class M7BatchAcceptance
{
    private const int TicksPerDay = 1440;

    private static bool Enabled
        => System.Environment.GetEnvironmentVariable("SBOX_SIM_BATCH") == "1";

    private static SimConfig Config(int size = 100)
    {
        var config = new SimConfig();
        config.World.Width = size;
        config.World.Height = size;
        return config;
    }

    [Fact("M7 批量验收：20 个种子里有多少能自己长出聚落（需 SBOX_SIM_BATCH=1）")]
    public void TwentySeedsFormSettlements()
    {
        if (!Enabled) { return; }

        // 允许用环境变量缩小规模做快速迭代（默认仍是验收要求的 20 种子 x 200 天）
        int Seeds = int.TryParse(System.Environment.GetEnvironmentVariable("SBOX_SIM_BATCH_SEEDS"), out int sc) && sc > 0 ? sc : 20;
        int Days = int.TryParse(System.Environment.GetEnvironmentVariable("SBOX_SIM_BATCH_DAYS"), out int dc) && dc > 0 ? dc : 200;

        int formed = 0;
        int formedBy150 = 0;
        int multiSettlements = 0;
        int totalSettlements = 0;

        for (int i = 0; i < Seeds; i++)
        {
            int seed = 70001 + (i * 137);
            var sim = new Simulation(Config(), 100, 100, seed);

            // 一队定居者落在世界中心附近 —— 与 CLI 的默认场景一致
            sim.InterveneSpawnHumans(50, 50, 40, 8);
            sim.InterveneAddResource(48, 48, ResourceKind.Food, 60f);
            sim.InterveneAddResource(52, 52, ResourceKind.Wood, 60f);

            int foundedDay = -1;
            for (int day = 0; day < Days; day++)
            {
                sim.Tick(TicksPerDay);
                if (foundedDay < 0 && sim.Settlements.TotalFounded > 0) { foundedDay = day + 1; }
            }

            int active = sim.Settlements.ActiveCount;
            totalSettlements += sim.Settlements.TotalFounded;

            if (sim.Settlements.TotalFounded > 0) { formed++; }
            if (foundedDay >= 0 && foundedDay <= 150) { formedBy150++; }
            if (sim.Settlements.TotalFounded >= 2) { multiSettlements++; }

            SettlementTier tier = SettlementTier.Camp;
            for (int k = 0; k < sim.Settlements.EntityCount; k++)
            {
                SettlementStore.Settlement s = sim.Settlements.At(k);
                if (!s.Dissolved && s.Tier > tier) { tier = s.Tier; }
            }

            System.Console.WriteLine("  [批量验收] seed " + seed
                + " 人口 " + sim.Agents.LiveCount.ToString("00")
                + " 聚落 " + sim.Settlements.TotalFounded
                + " (活跃 " + active + ")"
                + " 首成 " + (foundedDay < 0 ? "—" : foundedDay + "天")
                + " 最高等级 " + tier);
        }

        System.Console.WriteLine("  [批量验收] 汇总：" + formed + "/" + Seeds + " 形成过聚落；"
            + formedBy150 + "/" + Seeds + " 在 150 天内形成；"
            + multiSettlements + "/" + Seeds + " 有 ≥2 个聚落；"
            + "累计 " + totalSettlements + " 个");

        // 验收判据（任务书）：
        //   ≥15/20 在 150 天内形成聚落；≥5/20 在 200 天内有 ≥2 个聚落。
        // 阈值按**实际种子数**成比例缩放，这样小规模快速迭代也有意义
        // （验收要求的 20 种子对应的就是 75% 与 25%）。
        int requiredFormed = (Seeds * 75 + 99) / 100;      // ceil(Seeds * 0.75)
        int requiredMulti = (Seeds * 25 + 99) / 100;       // ceil(Seeds * 0.25)

        Assert.True(formedBy150 >= requiredFormed,
            "至少 " + requiredFormed + "/" + Seeds + " 个种子必须在 150 天内形成聚落（实测 "
            + formedBy150 + "/" + Seeds + "）");

        Assert.True(multiSettlements >= requiredMulti,
            "至少 " + requiredMulti + "/" + Seeds + " 个种子在 200 天内必须有 ≥2 个聚落（实测 " + multiSettlements + "/" + Seeds + "）—— "
            + "注意这条**当前多半达不到**：全体质心的聚类方式天然只会产出一个聚落，"
            + "多聚落需要真正的空间聚类（见 docs/12 的 M7 说明）");
    }
}
