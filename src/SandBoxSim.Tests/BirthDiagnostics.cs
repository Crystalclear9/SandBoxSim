using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

/// <summary>
/// M4 出生系统的**诊断台**（默认跳过，需要时用 `SBOX_SIM_PROBE=1` 运行）。
///
/// 为什么要有它：调"人口曲线"时只看最终人口数字会失去全部信息 ——
/// 到底是"没有合格伴侣"、"床位不够"、"食物不足"还是"概率太低"，
/// 四种原因的修法完全不同。这个诊断台把**每一天的四项中间量**打出来，
/// 于是"为什么没有孩子"是一个可以直接读出来的答案。
/// </summary>
public sealed class BirthDiagnostics
{
    private const int TicksPerDay = 1440;

    [Fact("诊断：出生系统的逐日中间量（需 SBOX_SIM_PROBE=1）")]
    public void DailyBirthTrace()
    {
        if (System.Environment.GetEnvironmentVariable("SBOX_SIM_PROBE") != "1")
        {
            System.Console.WriteLine("  [出生诊断] 已跳过（设 SBOX_SIM_PROBE=1 运行）。");
            return;
        }

        int days = 120;
        var config = new SimConfig();
        config.World.Width = 100;
        config.World.Height = 100;

        var sim = new Simulation(config, 100, 100, 839102);
        sim.InterveneSpawnHumans(50, 50, 40, 8);

        int lastDeaths = sim.Stats.TotalDeaths;
        int lastBirths = sim.Stats.TotalBirths;

        // 死因累计（打印时需要窗口内的增量）
        int causeCount = System.Enum.GetValues<Core.Agents.DeathCause>().Length;
        var lastCause = new int[causeCount];

        System.Console.WriteLine("  天  人口 出生 死亡 合格对 缺房 食物因子 空床 已婚 住房 农田 耕种次数 | 死因窗口分布");

        for (int day = 1; day <= days; day++)
        {
            sim.Tick(TicksPerDay);

            // 注意：这些增量是**当天的**，因此必须在循环里每次更新 last*，
            // 而不是只在打印的那一天更新 —— 后者会把"5 天的死亡"显示成"1 天"，
            // 于是诊断台会低估死亡率 5 倍，把排查带向完全错误的方向。
            int deaths = sim.Stats.TotalDeaths - lastDeaths;
            int births = sim.Stats.TotalBirths - lastBirths;
            lastDeaths = sim.Stats.TotalDeaths;
            lastBirths = sim.Stats.TotalBirths;

            string causes = string.Empty;
            for (int c = 0; c < causeCount; c++)
            {
                int now = sim.Needs.DeathsByCause[c];
                int delta = now - lastCause[c];
                lastCause[c] = now;
                if (delta <= 0) { continue; }
                causes += (Core.Agents.DeathCause)c + "×" + delta + " ";
            }

            if (day % 5 != 0) { continue; }

            int paired = 0;
            foreach (int slot in sim.Agents.AliveSlots())
            {
                if (sim.Agents.PartnerOf(slot) >= 0) { paired++; }
            }

            int houses = 0;
            int farms = 0;
            for (int i = 0; i < sim.Buildings.Capacity; i++)
            {
                if (!sim.Buildings.IsAlive(i)) { continue; }
                if (sim.Buildings.StateOf(i) != BuildingState.Complete) { continue; }
                if (sim.Buildings.KindOf(i) == BuildingKind.House) { houses++; }
                if (sim.Buildings.KindOf(i) == BuildingKind.Farm) { farms++; }
            }

            System.Console.WriteLine("  " + day.ToString("000")
                + " " + sim.Agents.LiveCount.ToString("0000")
                + " " + births.ToString("000")
                + " " + deaths.ToString("000")
                + " " + sim.Births.EligiblePairsThisDay.ToString("000")
                + " " + sim.Births.BlockedByHousingThisDay.ToString("000")
                + " " + sim.Births.AverageFoodFactorThisDay.ToString("0.00")
                + " " + sim.Buildings.FreeBeds.ToString("000")
                + " " + (paired / 2).ToString("000")
                + " " + houses.ToString("000")
                + " " + farms.ToString("000")
                + " " + sim.Actions.FarmVisits.ToString("0000")
                + " | " + causes);
        }
    }
}
