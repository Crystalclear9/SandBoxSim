using System;
using System.Collections.Generic;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Core.Systems;
public enum CropKind { Grain, Roots, Legumes }

/// <summary>Derived from saved clock, location and soil. Observation consumes no random state.</summary>
public static class LivingAgriculture
{
    public static CropKind CropAt(int seed,int x,int y)
    {
        uint hash=unchecked((uint)(seed*83492791^x*73856093^y*19349663));
        return (CropKind)(hash%3);
    }
    public static int Phase(long tick,int ticksPerDay)=>WildPlaces.Phase(Math.Max(0,tick)/Math.Max(1,ticksPerDay));
    public static string CropName(CropKind crop)=>crop==CropKind.Grain?"谷物":crop==CropKind.Roots?"根茎":"豆科";
    public static float YieldFactor(CropKind crop,int phase)
    {
        if(crop==CropKind.Grain)return phase switch {0=>.45f,1=>1f,2=>1.65f,_=>.15f};
        if(crop==CropKind.Roots)return phase switch {0=>.8f,1=>1f,2=>1.25f,_=>.45f};
        return phase switch {0=>.6f,1=>1.2f,2=>1.05f,_=>.2f};
    }
    public static void UpdateSoil(Simulation sim,int slot,float labor,int phase,CropKind crop)
    {
        if(!sim.Buildings.IsAlive(slot)||sim.Buildings.KindOf(slot)!=BuildingKind.Farm||sim.Buildings.StateOf(slot)!=BuildingState.Complete)return;
        var config=sim.Config.Buildings;if(!config.LivingAgricultureEnabled)return;
        var p=sim.Buildings.PositionOf(slot);var tile=sim.World.TileAt(p.X,p.Y);
        float intensity=Math.Clamp(labor/Math.Max(.01f,config.FarmLaborPerDayCap),0,1);
        float recovery=Math.Max(0,config.FarmSoilRecoveryPerDay);
        float change=phase==3||intensity==0?recovery*(.3f+.7f*tile.Moisture):crop==CropKind.Legumes?recovery*intensity:-Math.Max(0,config.FarmSoilUsePerDay)*intensity;
        sim.World.SetFertility(p.X,p.Y,Math.Clamp(tile.Fertility+change,0,1));
    }
    public static float SpoilGroundFood(Simulation sim,List<int> slots)
    {
        var config=sim.Config.Buildings;float total=0;
        if(!config.LivingAgricultureEnabled||config.GroundFoodSpoilagePerDay<=0)return 0;
        slots.Clear();foreach(int slot in sim.GroundStocks.AliveIndices())slots.Add(slot);
        foreach(int slot in slots)
        {
            var p=sim.GroundStocks.PositionOf(slot);if(!sim.World.IsInBounds(p.X,p.Y))continue;
            var tile=sim.World.TileAt(p.X,p.Y);float food=sim.GroundStocks.AmountOf(slot,ResourceKind.Food);
            if(food<=0)continue;
            float rate=Math.Clamp(config.GroundFoodSpoilagePerDay*(.5f+tile.Temperature)*(1+.2f*tile.Moisture),0,1);
            float lost=sim.GroundStocks.Withdraw(p.X,p.Y,ResourceKind.Food,food*rate);total+=lost;
            if(tile.Terrain is not (TerrainKind.Water or TerrainKind.Road or TerrainKind.Mountain or TerrainKind.Lava)&&tile.Fire==FireState.None)
                sim.World.SetFertility(p.X,p.Y,Math.Clamp(tile.Fertility+Math.Min(.02f,lost*.0005f),0,1));
        }
        return total;
    }
}
