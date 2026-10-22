using System.Collections.Generic;
using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Save;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

public sealed class WildlifeLifecycleTests
{
    private static Simulation GrassWorld(int seed=61001)
    {
        var sim=new Simulation(new SimConfig(),24,24,seed);
        sim.Wildlife.ClearAllKeepCapacity();
        sim.Config.Predator.Enabled=false;
        for(int y=0;y<24;y++)for(int x=0;x<24;x++)
        {
            sim.World.SetTerrain(x,y,TerrainKind.Grass);
            sim.World.SetMoisture(x,y,.8f);sim.World.SetVegetation(x,y,1);
        }
        sim.World.RefreshSpatialIndex();return sim;
    }
    private static void AssertLiveOrder(WildlifeStore store)
    {
        var found=new HashSet<int>();
        for(int i=0;i<store.LiveCount;i++)
        {
            int slot=store.LiveAt(i);Assert.True(store.IsAlive(slot));
            Assert.True(found.Add(slot),"A live slot occurs twice in the update order");
        }
        int alive=0;for(int i=0;i<store.Capacity;i++)if(store.IsAlive(i))alive++;
        Assert.Equal(alive,store.LiveCount);
        bool valid=store.TryValidateStructure(out string error);Assert.True(valid,error);
    }
    [Fact("动物扩容后死亡必须准确摘除旧槽，复用不得重复登记")]
    public void GrowthPreservesReverseIndicesThroughDeathAndReuse()
    {
        var sim=GrassWorld();var store=new WildlifeStore(2);var rng=sim.Random.Get(RngStream.Events);
        int first=store.Add(sim.World,4,4,0,rng),second=store.Add(sim.World,5,4,0,rng);
        int third=store.Add(sim.World,6,4,0,rng);
        Assert.Greater(store.Capacity,2);Assert.Equal(3,store.LiveCount);
        int generation=store.GenerationOf(first);
        store.Kill(first,false);Assert.Equal(2,store.LiveCount);AssertLiveOrder(store);
        Assert.Equal(first,store.Add(sim.World,7,4,0,rng));
        Assert.Equal(generation+1,store.GenerationOf(first));AssertLiveOrder(store);
        store.Kill(second,true);store.Kill(third,false);store.Kill(first,false);
        Assert.Equal(0,store.LiveCount);AssertLiveOrder(store);
        Assert.Equal(4,store.TotalBorn);Assert.Equal(4,store.TotalDied);Assert.Equal(1,store.TotalHunted);
    }
    [Fact("失败动物落点不能改变活体、世代、容量或随机流，之后应正常创建")]
    public void FailedPlacementIsAtomic()
    {
        var sim=GrassWorld();var store=new WildlifeStore(2);var rng=sim.Random.Get(RngStream.Events);
        for(int y=0;y<24;y++)for(int x=0;x<24;x++)sim.World.SetTerrain(x,y,TerrainKind.Water);
        var randomBefore=rng.ExportState();
        for(int i=0;i<12;i++)Assert.Equal(-1,store.Add(sim.World,12,12,3,rng));
        Assert.Equal(0,store.LiveCount);Assert.Equal(2,store.Capacity);Assert.Equal(0,store.TotalBorn);
        Assert.Equal(0,store.GenerationOf(0));Assert.SequenceEqual(randomBefore,rng.ExportState());
        sim.World.SetTerrain(12,12,TerrainKind.Grass);
        Assert.Equal(0,store.Add(sim.World,12,12,0,rng));Assert.Equal(1,store.LiveCount);
        AssertLiveOrder(store);
    }
    [Fact("日界只更新开始时的动物，新生个体必须保持零日龄且不能当天再繁殖")]
    public void NewbornsWaitUntilNextDailyCohort()
    {
        var sim=GrassWorld();sim.Config.Wildlife.CapacityPerVegetationTile=.5f;
        sim.Config.Wildlife.NaturalDeathChancePerDay=0;sim.Config.Wildlife.ReproductionChancePerDay=1;
        var starting=new HashSet<int>();
        for(int i=0;i<20;i++)
        {
            int slot=sim.Wildlife.Add(sim.World,4+i%10,4+i/10,0,sim.Random.Get(RngStream.Events));
            starting.Add(slot);sim.Wildlife.SetAgeDays(slot,8);sim.Wildlife.SetEnergy(slot,1);
        }
        sim.WildlifeSystem.TickDay(sim.Config.Clock.TicksPerDay);
        Assert.Greater(sim.WildlifeSystem.BirthsThisTick,0);
        Assert.LessOrEqual(sim.WildlifeSystem.BirthsThisTick,starting.Count);
        foreach(int slot in sim.Wildlife.ExportLiveOrder())
            Assert.Equal(starting.Contains(slot)?9:0,sim.Wildlife.AgeDaysOf(slot));
        AssertLiveOrder(sim.Wildlife);
    }
    [Fact("日界死亡交换删除时每个幸存原动物只更新一次，新生替补不参与本轮")]
    public void DailyDeathsAndBirthsDoNotRevisitOrSkipCohort()
    {
        var sim=GrassWorld(61002);sim.Config.Wildlife.CapacityPerVegetationTile=.5f;
        sim.Config.Wildlife.NaturalDeathChancePerDay=.5f;sim.Config.Wildlife.ReproductionChancePerDay=1;
        for(int i=0;i<40;i++)
        {
            int slot=sim.Wildlife.Add(sim.World,3+i%15,3+i/15,0,sim.Random.Get(RngStream.Events));
            sim.Wildlife.SetAgeDays(slot,8);sim.Wildlife.SetEnergy(slot,1);
        }
        var generations=new int[sim.Wildlife.Capacity];
        for(int i=0;i<40;i++)generations[i]=sim.Wildlife.GenerationOf(i);
        sim.WildlifeSystem.TickDay(sim.Config.Clock.TicksPerDay);
        Assert.Greater(sim.WildlifeSystem.DeathsThisTick,0);Assert.Greater(sim.WildlifeSystem.BirthsThisTick,0);
        foreach(int slot in sim.Wildlife.ExportLiveOrder())
        {
            bool original=slot<40&&sim.Wildlife.GenerationOf(slot)==generations[slot];
            Assert.Equal(original?9:0,sim.Wildlife.AgeDaysOf(slot));
        }
        Assert.Equal(40-sim.WildlifeSystem.DeathsThisTick+sim.WildlifeSystem.BirthsThisTick,sim.Wildlife.LiveCount);
        AssertLiveOrder(sim.Wildlife);
    }
    [Fact("扩容死亡与复用后的动物存档必须保留顺序并确定性续跑")]
    public void GrownPopulationResumesFromSaveExactly()
    {
        var sim=GrassWorld();var rng=sim.Random.Get(RngStream.Events);
        for(int i=0;i<270;i++)sim.Wildlife.Add(sim.World,2+i%20,2+(i/20)%20,0,rng);
        for(int i=0;i<30;i+=2)sim.Wildlife.Kill(i,false);
        for(int i=0;i<8;i++)sim.Wildlife.Add(sim.World,10+i,10,0,rng);
        AssertLiveOrder(sim.Wildlife);
        var restored=Simulation.CreateForRestore(sim.Config.Clone(),24,24,1);
        var result=SaveLoader.Load(restored,SaveFile.Encode(sim));
        Assert.True(result.Success);Assert.True(result.DigestMatches);
        Assert.SequenceEqual(sim.Wildlife.ExportLiveOrder(),restored.Wildlife.ExportLiveOrder());
        for(int i=0;i<12;i++)
        {
            sim.Tick(10);restored.Tick(10);
            AssertLiveOrder(sim.Wildlife);AssertLiveOrder(restored.Wildlife);
            Assert.Equal(StateHash.ComputeDigest(sim),StateHash.ComputeDigest(restored));
        }
    }
    [Fact("模拟不变量检查必须发现动物重复活体槽，不能只验证地块数值")]
    public void SimulationInvariantsRejectCorruptWildlifeOrder()
    {
        var sim=GrassWorld();int slot=sim.Wildlife.Add(sim.World,8,8,0,sim.Random.Get(RngStream.Events));
        sim.Wildlife.RestoreLiveOrder(new[]{slot,slot});
        Assert.Throws<System.InvalidOperationException>(()=>sim.ValidateInvariants());
        Assert.False(sim.LastInvariantCheckPassed);Assert.True(sim.LastInvariantFailure.Contains("Wildlife"));
    }
}
