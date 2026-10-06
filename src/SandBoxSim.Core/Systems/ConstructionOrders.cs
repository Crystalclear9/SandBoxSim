using System;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Pathing;

namespace SandBoxSim.Core.Systems;

/// <summary>Player-selected sites use the same material transaction and construction as resident projects.</summary>
public static class ConstructionOrders
{
    public static bool TryStart(Simulation sim, BuildingKind kind, int x, int y, out int index, out string reason)
    {
        index = -1; reason = "位置不适合这类建筑";
        if (!BuildingStore.CanPlaceAt(sim.World, kind, x, y)) { return false; }
        if (kind == BuildingKind.House && sim.World.DistanceToWater(x, y, 8) > 6)
            { reason = "住房需要在水域六格以内"; return false; }
        var paths = new AStarPathfinder(sim.World);
        bool nearby = false, reachable = false;
        foreach (int slot in sim.Agents.AliveSlots())
        {
            if (sim.Agents.LifeStageOf(slot) != LifeStage.Adult) { continue; }
            int sx = sim.Agents.XOf(slot), sy = sim.Agents.YOf(slot);
            if (Math.Max(Math.Abs(sx - x), Math.Abs(sy - y)) > 8) { continue; }
            nearby = true;
            if (!paths.FindNextStep(sx, sy, x, y, out var _).Success) { continue; }
            reachable = true;
            if (sim.BuildingSystem.TryStartBuilding(sim.Agents, slot, kind, x, y, out index, out reason))
            {
                sim.InterveneRecordAuxiliary("建造委托：" + BuildingRegistry.NameOf(kind)); return true;
            }
        }
        if (!nearby) { reason = "八格内需要成年居民；先引导人口和资源靠近"; }
        else if (!reachable) { reason = "居民无法到达工地；先打通道路"; }
        return false;
    }
}
