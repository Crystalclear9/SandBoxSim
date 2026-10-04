using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Systems;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

/// <summary>
/// 诊断「有人却从不形成聚落」的种子（需 `SBOX_SIM_M7_DIAG=1`）。
///
/// # 它要区分的两件事
///
/// 20 种子批量验收里有 5 个种子完全没有形成聚落，而其中 4 个世界里人口仍有 40-43。
/// 两种原因需要完全不同的修法：
///
///   * **人群被切成碎块** —— 大家分散成好几摊，每摊都不到 `FoundPeople = 6`。
///     修法方向是"相邻人群是否应当合并"，或者降低 `FoundPeople`；
///   * **没有仓库** —— 人聚在一起、住房也有，但始终没盖出仓库，
///     于是 `MinStorages = 1`（"共用"的硬证据）永远不达标。修法是建造意愿，不是聚落规则。
///
/// 判据很清楚：看 `LargestClusterPeople` 与 `LargestClusterStorages` 谁不达标。
/// </summary>
public sealed class M7ClusterDiagnostics
{
    private const int TicksPerDay = 1440;

    private static bool Enabled
        => System.Environment.GetEnvironmentVariable("SBOX_SIM_M7_DIAG") == "1";

    private static SimConfig Config(int size = 100)
    {
        var config = new SimConfig();
        config.World.Width = size;
        config.World.Height = size;
        return config;
    }

    private static float TotalWood(Simulation sim)
    {
        float total = 0f;
        int[] slots = sim.Agents.LiveSlotsRaw(out int liveCount);
        for (int k = 0; k < liveCount; k++) { total += sim.Agents.InventoryOf(slots[k], ResourceKind.Wood); }
        return total;
    }

    [Fact("诊断：为什么某些种子有人却不形成聚落（需 SBOX_SIM_M7_DIAG=1）")]
    public void DiagnoseNonFormingSeeds()
    {
        if (!Enabled) { Assert.Skip("需要显式开启此用例的环境变量，未执行验收"); }

        // 批量验收里 4 个「有人但没聚落」的种子
        int[] seeds = { 70960, 71508, 71645, 71919 };
        if (int.TryParse(System.Environment.GetEnvironmentVariable("SBOX_SIM_M7_DIAG_SEED"), out int singleSeed))
        { seeds = new[] { singleSeed }; }
        int days = int.TryParse(System.Environment.GetEnvironmentVariable("SBOX_SIM_M7_DIAG_DAYS"), out int requestedDays)
            && requestedDays > 0 ? requestedDays : 200;

        System.Console.WriteLine("  [聚落诊断] seed 人口 人群数 最大人群 该人群住房/仓库 候选天数 已成立");

        for (int s = 0; s < seeds.Length; s++)
        {
            var sim = new Simulation(Config(), 100, 100, seeds[s]);
            sim.InterveneSpawnHumans(50, 50, 40, 8);
            sim.InterveneAddResource(48, 48, ResourceKind.Food, 60f);
            sim.InterveneAddResource(52, 52, ResourceKind.Wood, 60f);

            for (int day = 0; day < days; day++)
            {
                sim.Tick(TicksPerDay);
                if ((day + 1) % 10 == 0)
                {
                    System.Console.WriteLine("  [聚落诊断] day " + (day + 1)
                        + " pop " + sim.Agents.LiveCount + " houses " + sim.Buildings.TotalBeds
                        + " storages " + sim.Buildings.CompletedStorages
                        + " cluster " + sim.Settlements.LargestClusterPeople
                        + " facilities " + sim.Settlements.LargestClusterHouses + "/" + sim.Settlements.LargestClusterStorages
                        + " candidate " + sim.Settlements.CandidateDays
                        + " build " + sim.Actions.BuildsStarted
                        + " selectedStorage " + sim.Ai.ChosenByAction[(int)ActionKind.BuildStorage]);
                }
            }

            // 区分"没被选中"与"选了但没盖成"：
            //   BuildStorage 被选中次数 = 0        ⇒ 效用问题
            //   被选中很多但完工数 = 0             ⇒ 选址或施工问题
            int storageStarted = 0;
            int storageComplete = 0;
            for (int k = 0; k < sim.Buildings.Capacity; k++)
            {
                if (!sim.Buildings.IsAlive(k)) { continue; }
                if (sim.Buildings.KindOf(k) != BuildingKind.Storage) { continue; }
                if (sim.Buildings.StateOf(k) == BuildingState.Complete) { storageComplete++; }
                else { storageStarted++; }
            }
            System.Console.WriteLine("  [聚落诊断]   -> BuildStorage 被选中 "
                + sim.Ai.ChosenByAction[(int)ActionKind.BuildStorage]
                + " 次；仓库在地 施工中 " + storageStarted + " 完工 " + storageComplete
                + "；累计木材 " + ((int)TotalWood(sim)));

            System.Console.WriteLine("  [聚落诊断] " + seeds[s]
                + " " + sim.Agents.LiveCount.ToString("00")
                + " 人群 " + sim.Settlements.LastClusterCount.ToString("00")
                + " 最大 " + sim.Settlements.LargestClusterPeople.ToString("00")
                + " 住房/仓库 " + sim.Settlements.LargestClusterHouses
                + "/" + sim.Settlements.LargestClusterStorages
                + " 候选天数 " + sim.Settlements.CandidateDays
                + " 已成立 " + sim.Settlements.TotalFounded);
        }

        System.Console.WriteLine("  [聚落诊断] 阈值：FoundPeople="
            + new SettlementConfig().FoundPeople
            + " MinHouses=" + new SettlementConfig().MinHouses
            + " MinStorages=" + new SettlementConfig().MinStorages
            + " FoundDays=" + new SettlementConfig().FoundDays);
    }
}
