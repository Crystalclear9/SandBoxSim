using System.Collections.Generic;
using SandBoxSim.Core;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Save;
using SandBoxSim.Core.Systems;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

public sealed class PredatorBehaviorTests
{
    private static Simulation DryWorld()
    {
        var sim = new Simulation(new SimConfig(), 24, 24, 5151);
        sim.Wildlife.ClearAllKeepCapacity();
        for (int y = 0; y < 24; y++) for (int x = 0; x < 24; x++)
        {
            sim.World.SetTerrain(x, y, TerrainKind.Grass);
            sim.World.SetMoisture(x, y, 0); sim.World.SetVegetation(x, y, 0);
        }
        sim.World.RefreshSpatialIndex();
        return sim;
    }

    [Fact("无局部猎物的狼应探索，不因微小疲劳永远原地睡眠")]
    public void IsolatedPredatorExploresInsteadOfSleepingForever()
    {
        var sim = DryWorld(); Assert.True(sim.Predators.Spawn(12, 12));
        var wolf = sim.Predators.Wolves[0]; wolf.Energy = .6f;
        var visited = new HashSet<Int2> { new(wolf.X, wolf.Y) };
        for (int i = 1; i <= 40; i++)
        {
            sim.Predators.TickFast(i * 10);
            Assert.NotEqual("Sleep", wolf.Action);
            visited.Add(new(wolf.X, wolf.Y));
        }
        Assert.Greater(visited.Count, 3);
        Assert.Greater(wolf.Fatigue, 0f);
    }

    [Fact("疲惫的狼仍会睡眠恢复，休息充分后不继续强制睡眠")]
    public void PredatorSleepsForRealFatigue()
    {
        var sim = DryWorld(); sim.Predators.Spawn(12, 12);
        var wolf = sim.Predators.Wolves[0]; wolf.Energy = .8f; wolf.Fatigue = .9f;
        sim.Predators.TickFast(10);
        Assert.Equal("Sleep", wolf.Action); Assert.Less(wolf.Fatigue, .9f);
        wolf.Fatigue = 0; sim.Predators.TickFast(20);
        Assert.Equal("Rest", wolf.Action);
    }

    [Fact("干渴的狼沿可达缺口寻找饮水点，不能穿墙或走入水域")]
    public void PredatorFindsWaterThroughReachableGap()
    {
        var sim = DryWorld(); sim.World.SetTerrain(12, 8, TerrainKind.Water);
        for (int y = 5; y <= 11; y++) if (y != 10) sim.World.SetTerrain(9, y, TerrainKind.Lava);
        sim.Predators.Spawn(6, 8); var wolf = sim.Predators.Wolves[0]; wolf.Energy = .8f; wolf.Thirst = .85f;
        bool drank = false;
        for (int i = 1; i <= 30; i++)
        {
            var before = new Int2(wolf.X, wolf.Y); float thirst = wolf.Thirst;
            sim.Predators.TickFast(i * 10);
            Assert.True(sim.World.TileAt(wolf.X, wolf.Y).Walkable);
            Assert.LessOrEqual(Int2.ManhattanDistance(before, new(wolf.X, wolf.Y)), 1);
            if (wolf.Thirst < thirst)
            {
                Assert.True(WildlifeSystem.CanDrinkAt(sim.World, wolf.X, wolf.Y)); drank = true; break;
            }
        }
        Assert.True(drank, "A nearby water source is reachable through the wall gap");
    }

    [Fact("不可达的水源不能隔墙缓解干渴")]
    public void PredatorCannotDrinkThroughBlockedRoute()
    {
        var sim = DryWorld(); sim.World.SetTerrain(15, 12, TerrainKind.Water);
        foreach(var direction in new[]{Int2.Left,Int2.Right,Int2.Up,Int2.Down})
            sim.World.SetTerrain(12+direction.X,12+direction.Y,TerrainKind.Lava);
        sim.Predators.Spawn(12, 12); var wolf=sim.Predators.Wolves[0]; wolf.Energy=.8f;wolf.Thirst=.85f;
        sim.Predators.TickFast(10);
        Assert.Greater(wolf.Thirst,.85f);Assert.NotEqual("Drink",wolf.Action);
        Assert.Equal(new Int2(12,12),new Int2(wolf.X,wolf.Y));
    }

    [Fact("狼的探索、寻水与随机流在存档续跑后保持确定性")]
    public void PredatorSearchAndExplorationResumeExactly()
    {
        var sim=DryWorld();sim.World.SetTerrain(17,12,TerrainKind.Water);sim.Predators.Spawn(12,12);
        sim.Predators.Wolves[0].Energy=.6f;sim.Predators.Wolves[0].Thirst=.85f;
        sim.Tick(30);
        var loaded=Simulation.CreateForRestore(sim.Config.Clone(),24,24,1);
        Assert.True(SaveLoader.Load(loaded,SaveFile.Encode(sim)).DigestMatches);
        for(int i=0;i<40;i++){sim.Tick(10);loaded.Tick(10);Assert.Equal(StateHash.ComputeDigest(sim),StateHash.ComputeDigest(loaded));}
    }
}
