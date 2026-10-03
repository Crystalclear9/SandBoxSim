using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Systems;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

/// <summary>
/// 地图级诊断：把"100×100 地图 + 按 chunk 评分选址 + 放人"这条链路完整跑一遍并打印现场。
///
/// 存在的理由：M1 联调时出现过"20 人小队在 50×50 上活得很好，但 40 人在 100×100 上两天全饿死"。
/// 这不是 AI 的问题，而是**选址与资源可得性**的问题。要分清责任，就必须能直接看到
/// 出生区域周围到底有多少可采食物 —— 否则只能在效用公式里瞎猜。
/// </summary>
public sealed class MapDiagnosticsTests
{
    [Fact("100×100 地图上的选址与资源可得性诊断")]
    public void SpawnSelectionDiagnostics()
    {
        var config = new SimConfig();
        config.World.Width = 100;
        config.World.Height = 100;

        var sim = new Simulation(config, 100, 100, 839102);

        // 复刻 Program.SpawnInitialAgents 的选址逻辑（同样的评分公式）
        int bestScore = int.MinValue;
        int bestX = 50;
        int bestY = 50;

        for (int cy = 0; cy < sim.World.Chunks.ChunkRows; cy++)
        {
            for (int cx = 0; cx < sim.World.Chunks.ChunkCols; cx++)
            {
                ChunkStatsReadOnly stats = sim.World.Chunks.Read(cx, cy);
                if (!stats.IsValid || stats.CellCount == 0) { continue; }

                int score = (stats.WalkableTiles * 2)
                            + ((int)stats.GrassTiles * 3)
                            + ((int)stats.ForestTiles * 2)
                            + ((int)stats.FoodAmount / 8)
                            - (stats.WaterTiles * 4);

                if (score > bestScore)
                {
                    bestScore = score;
                    sim.World.Chunks.GetBounds(cx, cy, out int minX, out int minY, out int maxX, out int maxY);
                    bestX = (minX + maxX) / 2;
                    bestY = (minY + maxY) / 2;
                }
            }
        }

        System.Console.WriteLine("  [诊断] 选址中心 " + bestX + "," + bestY + "（评分 " + bestScore + "）");

        ChunkStatsReadOnly here = sim.World.Chunks.ReadAt(bestX, bestY);
        System.Console.WriteLine("  [诊断] 该 chunk：可走 " + here.WalkableTiles + "，草地 " + here.GrassTiles
            + "，森林 " + here.ForestTiles + "，水 " + here.WaterTiles
            + "，食物存量 " + here.FoodAmount.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));

        // 全图资源总量与地形分布
        int[] terrain = sim.World.CountTerrain();
        int grassWithFood = 0;
        for (int i = 0; i < sim.World.Tiles.Length; i++)
        {
            Tile tile = sim.World.Tiles[i];
            if (tile.Terrain == TerrainKind.Grass && tile.Resource.Kind == ResourceKind.Food && tile.Resource.Amount > 0.01f)
            {
                grassWithFood++;
            }
        }
        System.Console.WriteLine("  [诊断] 全图：草地 " + terrain[(int)TerrainKind.Grass]
            + "（其中带食物 " + grassWithFood + "），森林 " + terrain[(int)TerrainKind.Forest]
            + "，水域 " + terrain[(int)TerrainKind.Water]
            + "，山地 " + terrain[(int)TerrainKind.Mountain]);

        // 放 40 人并跑 2 天，看行为与生存
        int spawned = sim.InterveneSpawnHumans(bestX, bestY, 40, 8);
        System.Console.WriteLine("  [诊断] 实际放置 " + spawned + " 人");

        for (int day = 1; day <= 10; day++)
        {
            sim.Tick(1440);
            int alive = sim.Agents.LiveCount;
            float hunger = 0f;
            int hungry = 0;
            float foodLeft = 0f;
            foreach (int slot in sim.Agents.AliveSlots())
            {
                float h = sim.Agents.HungerOf(slot);
                hunger += h;
                foodLeft += sim.Agents.InventoryOf(slot, ResourceKind.Food);
                if (h > 0.6f) { hungry++; }
            }
            float avgHunger = alive > 0 ? hunger / alive : 0f;
            System.Console.WriteLine("  [诊断] 第 " + day + " 天：存活 " + alive
                + "，平均饥饿 " + avgHunger.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                + "，饥饿>0.6 有 " + hungry
                + "，随身食物 " + foodLeft.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                + "，累计采集 " + sim.Actions.HarvestedByKind[(int)ResourceKind.Food].ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                + "，吃掉 " + sim.Actions.TotalFoodEaten.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        }

        System.Console.WriteLine("  [诊断] 找到目标失败次数 " + sim.Ai.TargetSelectionFailures
            + "，动作完成 " + sim.Actions.TotalCompleted + "，动作失败 " + sim.Actions.TotalFailed);

        // 每个动作被"评估"与"选中"的次数。
        // 这两个数放在一起看才能定位问题：
        //   * 评估很多但从不被选 ⇒ 效用公式有问题（权重/曲线/门槛）；
        //   * 从来没被评估     ⇒ 注册表漏了它（比效用问题更基础）。
        for (int i = 0; i < sim.Ai.ChosenByAction.Length; i++)
        {
            int chosen = sim.Ai.ChosenByAction[i];
            int evaluated = sim.Ai.EvaluatedByAction[i];
            if (chosen == 0 && evaluated == 0) { continue; }
            System.Console.WriteLine("  [诊断] 动作 " + (ActionKind)i
                + "：被评估 " + evaluated + " 次，被选中 " + chosen + " 次");
        }

        Assert.Greater(spawned, 0, "必须能放下人");
    }
}
