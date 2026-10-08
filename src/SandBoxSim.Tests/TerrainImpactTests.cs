using SandBoxSim.Core;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;
public sealed class TerrainImpactTests
{
    private static Simulation World()
    {
        var config=new SimConfig();config.World.Width=24;config.World.Height=24;config.Ai.MoveSpeedPerTick=1;
        var sim=new Simulation(config,24,24,17);
        for(int i=0;i<sim.World.Tiles.Length;i++){sim.World.Tiles[i]=Tile.CreateDefault(TerrainKind.Grass,.7f,.6f,.5f);sim.World.Tiles[i].Height=.4f;}
        sim.World.RefreshSpatialIndex();return sim;
    }
    [Fact]
    public void FootfallsAccumulateGraduallyAndRecover()
    {
        var sim=World();float original=sim.World.TileAt(8,8).Height;
        sim.World.RecordFootfall(8,8);float first=sim.World.TileAt(8,8).FootTraffic;
        Assert.True(first>0 && first<.002f);
        for(int i=0;i<300;i++)sim.World.RecordFootfall(8,8);
        var worn=sim.World.TileAt(8,8);Assert.True(worn.FootTraffic>.3f && worn.FootTraffic<.5f);
        Assert.True(worn.Height<original && original-worn.Height<.006f);Assert.True(worn.Vegetation<.5f);
        for(int i=0;i<168;i++)sim.World.RecoverFootTraffic();
        Assert.True(sim.World.TileAt(8,8).FootTraffic<worn.FootTraffic*.6f);
        Assert.True(sim.World.TileAt(8,8).Height>worn.Height);
    }
    [Fact]
    public void ActualMovementLeavesContactButIdleDoesNot()
    {
        var sim=World();Assert.Equal(1,sim.InterveneSpawnHumans(4,4,1,0));int slot=-1;
        foreach(int alive in sim.Agents.AliveSlots()){slot=alive;break;}
        sim.Agents.SetPosition(slot,4,4);sim.Agents.SetTarget(slot,9,4);
        sim.Agents.SetAction(slot,ActionKind.GatherFood,ActionPhase.Moving);
        for(int i=0;i<6;i++)sim.Actions.Tick(i+1);
        float total=0;foreach(var tile in sim.World.Tiles)total+=tile.FootTraffic;
        Assert.True(total>0);sim.Agents.SetAction(slot,ActionKind.None,ActionPhase.Idle);
        for(int i=0;i<40;i++)sim.Actions.Tick(i+10);
        float after=0;foreach(var tile in sim.World.Tiles)after+=tile.FootTraffic;Assert.Equal(total,after);
    }
    [Fact]
    public void WaterRoadsAndInvalidPressureAreProtected()
    {
        var sim=World();sim.World.SetTerrain(2,2,TerrainKind.Water);sim.World.SetTerrain(3,3,TerrainKind.Road);
        sim.World.RecordFootfall(2,2);sim.World.RecordFootfall(3,3);sim.World.RecordFootfall(4,4,float.NaN);
        Assert.Equal(0f,sim.World.TileAt(2,2).FootTraffic);Assert.Equal(0f,sim.World.TileAt(3,3).FootTraffic);Assert.Equal(0f,sim.World.TileAt(4,4).FootTraffic);
    }
    [Fact]
    public void CompactionSuppressesActualResourceRegeneration()
    {
        var clean=World();var compacted=World();
        foreach(var sim in new[]{clean,compacted})sim.World.Tiles[5*24+5].Resource=new ResourceNode {Kind=ResourceKind.Food,Amount=50,Capacity=100,RegenerationRate=1};
        compacted.World.Tiles[5*24+5].FootTraffic=.8f;clean.ResourceSystem.Regenerate(1);compacted.ResourceSystem.Regenerate(1);
        Assert.True(compacted.World.TileAt(5,5).Resource.Amount<clean.World.TileAt(5,5).Resource.Amount);
    }
    [Fact]
    public void SaveRestoreContinuesAccumulationAndRecoveryExactly()
    {
        var source=World();for(int i=0;i<200;i++)source.World.RecordFootfall(8,8);
        string json=source.SaveToText();var restored=World();var result=restored.LoadFromText(json);
        Assert.True(result.Success,result.Error);Assert.Equal(source.StateDigestString(),restored.StateDigestString());
        for(int i=0;i<150;i++){source.World.RecordFootfall(8,8);restored.World.RecordFootfall(8,8);source.World.RecoverFootTraffic();restored.World.RecoverFootTraffic();}
        source.Tick(120);restored.Tick(120);Assert.Equal(source.StateDigestString(),restored.StateDigestString());
    }
    [Fact]
    public void OldSavesWithoutTrafficLoadAsUntouchedSoil()
    {
        var source=World();string json=System.Text.RegularExpressions.Regex.Replace(source.SaveToText(),"\"footTraffic\"\\s*:\\s*\\[[^\\]]*\\]\\s*,","");
        json=json.Replace("\"version\": 4","\"version\": 3");
        var restored=World();var result=restored.LoadFromText(json);Assert.True(result.Success,result.Error);
        Assert.Equal(source.StateDigestString(),restored.StateDigestString());Assert.Equal(0f,restored.World.TileAt(8,8).FootTraffic);
    }
}
