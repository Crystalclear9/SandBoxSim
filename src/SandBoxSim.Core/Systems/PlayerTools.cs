using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.History;

namespace SandBoxSim.Core.Systems;

public enum PlayerTool
{
    Inspect, Human, Animal, Forest, Food, Wood, Stone, Iron,
    Grass, Water, Mountain, Sand, Farmland, Road, Snow, Swamp, Desert, Lava,
    Raise, Lower, River, RemoveWater, Fertility, BirthBlessing, Heal, Production,
    Fire, Lightning, Flood, Drought, Plague, Meteor, Wolf, Rain
}
/// <summary>UI 与自动验收共用的干预入口。玩家改变条件，不能直接创建社会结果。</summary>
public static class PlayerTools
{
    public static void Apply(Simulation sim, PlayerTool tool, int x, int y, int radius = 2, float strength = 10)
    {
        if (!sim.World.IsInBounds(x, y) || tool == PlayerTool.Inspect) { return; }
        radius = System.Math.Clamp(radius, 0, 20);
        strength = SimMath.Clamp(strength, 0, 100);
        if (tool == PlayerTool.Human) { sim.InterveneSpawnHumans(x, y, System.Math.Max(1, (int)strength), radius); return; }
        if (tool == PlayerTool.Animal) { sim.InterveneSpawnAnimals(x, y, System.Math.Max(1, (int)strength), radius); return; }
        if (tool == PlayerTool.Wolf) { sim.Predators.Spawn(x, y); return; }
        if (tool == PlayerTool.Forest) { sim.InterveneGrowForest(x, y, radius, 1); return; }
        if (tool == PlayerTool.Fertility) { sim.InterveneSetFertility(x, y, radius, strength / 100); return; }
        if (tool == PlayerTool.BirthBlessing) { sim.Config.Rules.HighBirthRate = true; }
        foreach (int slot in sim.Agents.AliveSlots())
        {
            if (Int2.SquaredDistance(new Int2(x, y), sim.Agents.PositionOf(slot)) > radius * radius) { continue; }
            if (tool == PlayerTool.Heal) { sim.Diseases.Heal(slot); }
            if (tool == PlayerTool.Plague) { sim.Diseases.Infect(slot); }
            if (tool == PlayerTool.Lightning || tool == PlayerTool.Meteor)
            {
                sim.Agents.AddHealth(slot, -strength / 100);
                if (sim.Agents.HealthOf(slot) <= 0) { sim.Needs.Kill(sim.Agents, slot, DeathCause.Disaster, sim.Clock); }
            }
        }
        for (int py = y - radius; py <= y + radius; py++)
            for (int px = x - radius; px <= x + radius; px++)
            {
                if (!sim.World.IsInBounds(px, py) || (px - x) * (px - x) + (py - y) * (py - y) > radius * radius) { continue; }
                var tile = sim.World.TileAt(px, py);
                if ((tool == PlayerTool.Meteor || tool == PlayerTool.Flood) && tile.BuildingId > 0)
                    sim.BuildingSystem.Destroy(tile.BuildingId - 1, "玩家灾害：" + tool);
                if (tool >= PlayerTool.Food && tool <= PlayerTool.Iron)
                    sim.InterveneAddResource(px, py, (ResourceKind)((int)tool - (int)PlayerTool.Food + 1), strength);
                if (tool >= PlayerTool.Grass && tool <= PlayerTool.Lava)
                    sim.InterveneSetTerrain(px, py, tool switch
                    {
                        PlayerTool.Grass => TerrainKind.Grass, PlayerTool.Water => TerrainKind.Water,
                        PlayerTool.Mountain => TerrainKind.Mountain, PlayerTool.Sand => TerrainKind.Sand,
                        PlayerTool.Farmland => TerrainKind.Farmland, PlayerTool.Road => TerrainKind.Road,
                        PlayerTool.Snow => TerrainKind.Snow, PlayerTool.Swamp => TerrainKind.Swamp,
                        PlayerTool.Desert => TerrainKind.Desert, _ => TerrainKind.Lava
                    });
                if (tool == PlayerTool.Water || tool == PlayerTool.River || tool == PlayerTool.Flood)
                { sim.InterveneSetTerrain(px, py, TerrainKind.Water); sim.World.SetMoisture(px, py, 1); }
                if (tool == PlayerTool.RemoveWater && tile.Terrain == TerrainKind.Water) { sim.InterveneSetTerrain(px, py, TerrainKind.Grass); }
                if (tool == PlayerTool.Raise || tool == PlayerTool.Lower)
                {
                    float height = SimMath.Clamp01(tile.Height + (tool == PlayerTool.Raise ? 1 : -1) * strength / 100);
                    sim.World.Tiles[py * sim.World.Width + px].Height = height;
                    sim.World.MarkDirtyAt(px, py);
                    sim.InterveneSetTerrain(px, py, height < 0.25f ? TerrainKind.Water : height > 0.75f ? TerrainKind.Mountain : TerrainKind.Grass);
                }
                if (tool == PlayerTool.Drought) { sim.World.SetMoisture(px, py, 0); }
                if (tool == PlayerTool.Rain)
                {
                    sim.World.SetMoisture(px, py, SimMath.Clamp01(tile.Moisture + strength / 100));
                    if (tile.Fire == FireState.Burning && strength >= 25)
                    {
                        sim.World.SetFire(px, py, FireState.None);
                        sim.Events.Record(sim.Clock, WorldEventType.FireExtinguished, "降雨扑灭火情", EventImportance.Minor, new Int2(px, py), cause: "玩家降雨");
                    }
                }
                if (tool == PlayerTool.Production && tile.Resource.RegenerationRate > 0)
                { var node = tile.Resource; node.RegenerationRate *= 1 + strength / 100; sim.World.SetResource(px, py, node); }
                if (tool == PlayerTool.Fire || tool == PlayerTool.Lightning) { sim.Fire.Ignite(px, py, sim.Clock, "玩家干预"); }
                if (tool == PlayerTool.Meteor)
                { sim.World.SetVegetation(px, py, 0); sim.World.ClearResource(px, py); sim.InterveneSetTerrain(px, py, TerrainKind.Sand, false); }
            }
        sim.Events.Record(sim.Clock, WorldEventType.Disaster, "玩家使用 " + tool, EventImportance.Important,
            new Int2(x, y), cause: "范围 " + radius + "，强度 " + strength);
        sim.World.RefreshSpatialIndex();
    }
}
