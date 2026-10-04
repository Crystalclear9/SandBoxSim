using System;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Systems;

/// <summary>Playable initial conditions. No houses, settlements, alliances or wars are scripted.</summary>
public static class SandboxScenarios
{
    public static readonly string[] Names = { "河谷新生", "两岸之间", "旱地绿洲", "荒野生态" };
    public static readonly string[] Questions =
    {
        "沿河的居民能否建立家园？观察住房、粮食与第一代孩子。",
        "两岸资源互补却被河流分隔。开辟通道后，贸易会发生吗？",
        "干旱草原上的水与粮食有限。引水、植林或降雨，比较变化。",
        "森林、鹿群与狼相互制约。改变植被或捕食者，观察生态变化。"
    };

    public static Simulation Create(int scenario, int seed, int population = 40)
    {
        scenario = Math.Clamp(scenario, 0, Names.Length - 1);
        var sim = new Simulation(new SimConfig(), 100, 100, seed);
        // The geography is deterministic and all resource stock remains finite.
        for (int y = 0; y < 100; y++)
            for (int x = 0; x < 100; x++)
            {
                double ridge = Math.Sin(x * .11 + seed % 19) + Math.Cos(y * .13);
                TerrainKind terrain = ridge > 1.45 ? TerrainKind.Mountain
                    : ridge < -.65 ? TerrainKind.Forest : TerrainKind.Grass;
                int river = 50 + (int)(Math.Sin(y * .08) * 4);
                if (scenario != 2 && Math.Abs(x - river) <= (scenario == 1 ? 2 : 1)) { terrain = TerrainKind.Water; }
                if (scenario == 2)
                {
                    terrain = ridge > 1.45 ? TerrainKind.Mountain : TerrainKind.Desert;
                    if ((x - 47) * (x - 47) + (y - 50) * (y - 50) < 12) { terrain = TerrainKind.Water; }
                    else if ((x - 47) * (x - 47) + (y - 50) * (y - 50) < 110) { terrain = TerrainKind.Grass; }
                }
                sim.World.SetTerrain(x, y, terrain);
                sim.World.ApplyDefaultResource(x, y);
                sim.World.SetVegetation(x, y, terrain == TerrainKind.Forest ? .9f : terrain == TerrainKind.Grass ? .55f : .03f);
                sim.World.Tiles[y * 100 + x].Height = terrain == TerrainKind.Water ? .18f : terrain == TerrainKind.Mountain ? .85f : .45f;
                if (scenario == 2) { sim.World.SetMoisture(x, y, terrain == TerrainKind.Water ? 1 : .08f); }
            }
        PrepareBank(sim, 42, 50, scenario == 2 ? 5 : 9);
        if (scenario == 1)
        {
            PrepareBank(sim, 61, 50, 9);
            sim.InterveneSpawnHumans(42, 50, population / 2, 5);
            sim.InterveneSpawnHumans(61, 50, population - population / 2, 5);
            PlayerTools.Apply(sim, PlayerTool.Wood, 39, 45, 4, 65);
            PlayerTools.Apply(sim, PlayerTool.Iron, 65, 45, 4, 50);
        }
        else if (scenario != 3) { sim.InterveneSpawnHumans(42, 50, population, 5); }
        else
        {
            sim.InterveneGrowForest(38, 48, 12, 1);
            sim.InterveneSpawnAnimals(38, 48, 30, 10);
            sim.Predators.Spawn(33, 48); sim.Predators.Spawn(37, 43);
            sim.InterveneSpawnHumans(62, 50, Math.Min(population, 12), 4);
        }
        sim.InterveneRecordAuxiliary("初始环境：" + Names[scenario] + "。" + Questions[scenario]);
        sim.World.RefreshSpatialIndex();
        return sim;
    }

    private static void PrepareBank(Simulation sim, int x, int y, int radius)
    {
        for (int py = y - radius; py <= y + radius; py++)
            for (int px = x - radius; px <= x + radius; px++)
            {
                if (!sim.World.IsInBounds(px, py) || sim.World.TerrainAt(px, py) == TerrainKind.Water) { continue; }
                if (Math.Abs(px - x) <= 5 && Math.Abs(py - y) <= 5)
                    { sim.World.SetTerrain(px, py, TerrainKind.Grass); sim.World.ApplyDefaultResource(px, py); sim.World.SetVegetation(px, py, .55f); }
            }
        sim.InterveneGrowForest(x - 7, y + 4, 4, 1);
        PlayerTools.Apply(sim, PlayerTool.Food, x, y + 2, 5, 28);
        PlayerTools.Apply(sim, PlayerTool.Stone, x - 6, y - 4, 3, 35);
    }
}
