using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

/// <summary>
/// M6 脱水分叉的定位诊断（需 `SBOX_SIM_M6_OPEN=1`）。
///
/// # 它回答的问题
///
/// 基础的 44×44 测试定居点在**某些种子**上会在第 2 天全员脱水而死，
/// 而在另一些种子上活得很好（实测：seed 9001 活、seed 9003 死）。
/// 前面几轮已经排除了饥饿频率、森林挡路（Forest 其实可通行）、取水距离、
/// 饥饿钉死、地形覆写 —— 全部不是原因。
///
/// 剩下的可能只有两类，而它们的修法完全不同：
///
///   * **不去喝**（效用问题）：`Drink` 的效用被别的动作压住，人根本不去水边；
///   * **找不到水**（选靶问题）：想去喝，但取水搜索找不到水，于是反复失败。
///
/// 所以这里把三样东西并排打出来：**干渴度曲线 + Drink 被选中次数 + 选靶失败次数**。
/// 三者一比就能区分这两类 —— 这正是 M4 定位脱水时用过的办法，那次一次就定位到了。
/// </summary>
public sealed class M6WaterDiagnostics
{
    private const int TicksPerDay = 1440;

    private static bool Enabled
        => System.Environment.GetEnvironmentVariable("SBOX_SIM_M6_OPEN") == "1";

    [Fact("诊断：定居点取水失败的定位（默认执行）")]
    public void DiagnoseWaterAccess()
    {

        int seed = 9003;   // 实测会全员脱水的那个种子
        var sim = new Simulation(new SimConfig { World = { Width = 44, Height = 44 } }, 44, 44, seed);

        for (int y = 0; y < 44; y++)
        {
            for (int x = 0; x < 44; x++)
            {
                bool nearCenter = System.Math.Abs(x - 30) <= 8 && System.Math.Abs(y - 30) <= 8;
                sim.World.SetTerrain(x, y, nearCenter ? TerrainKind.Grass : TerrainKind.Forest);
                sim.World.SetVegetation(x, y, nearCenter ? 0.3f : 0.8f);
                sim.World.SetMoisture(x, y, 0.4f);
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

        System.Console.WriteLine("  [取水诊断] tick 存活 平均干渴 平均健康 | Drink Eat GatherFood Wander | 选靶失败");
        System.Console.WriteLine("  [取水诊断] 水在第 16 列（y=18..26），出生点 (30,30)。"
            + "第 16..31 列属同一个 16×16 chunk。");

        int lastDrink = 0;
        for (int step = 0; step < 24; step++)
        {
            sim.Tick(120);

            int live = sim.Agents.LiveCount;
            float thirst = 0f;
            float health = 0f;
            foreach (int slot in sim.Agents.AliveSlots())
            {
                thirst += sim.Agents.ThirstOf(slot);
                health += sim.Agents.HealthOf(slot);
            }

            int drink = sim.Ai.ChosenByAction[(int)ActionKind.Drink];
            System.Console.WriteLine("  [取水诊断] " + (step * 120).ToString("0000")
                + " " + live.ToString("00")
                + " " + (live > 0 ? (thirst / live).ToString("0.000") : "-")
                + " " + (live > 0 ? (health / live).ToString("0.000") : "-")
                + " | " + drink.ToString("000") + "(+" + (drink - lastDrink) + ")"
                + " " + sim.Ai.ChosenByAction[(int)ActionKind.Eat].ToString("000")
                + " " + sim.Ai.ChosenByAction[(int)ActionKind.GatherFood].ToString("000")
                + " " + sim.Ai.ChosenByAction[(int)ActionKind.Wander].ToString("000")
                + " | " + sim.Ai.TargetSelectionFailures);

            lastDrink = drink;
        }

        // 最后把死因打出来
        var causes = new System.Text.StringBuilder();
        for (int c = 0; c < sim.Needs.DeathsByCause.Length; c++)
        {
            if (sim.Needs.DeathsByCause[c] > 0)
            {
                causes.Append((DeathCause)c).Append(" x").Append(sim.Needs.DeathsByCause[c]).Append("; ");
            }
        }
        System.Console.WriteLine("  [取水诊断] 死因：" + causes);
        Assert.True(sim.Agents.LiveCount > 0, "取水诊断场景不能全灭");
        Assert.Equal(0, sim.Needs.DeathsByCause[(int)DeathCause.Dehydration], "可达水源场景不应出现脱水死亡");
    }
}
