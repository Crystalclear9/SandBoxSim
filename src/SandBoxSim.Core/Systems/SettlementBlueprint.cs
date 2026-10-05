using System;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Systems;

/// <summary>An optional local objective observes actual buildings and residents; it never creates them.</summary>
public sealed class SettlementBlueprint
{
    public static readonly string[] Names = { "河畔农庄", "林间驿站", "集市小镇" };
    public static readonly string[] Briefs = { "住房 2 · 仓库 1 · 农田 2 · 居民 6", "住房 2 · 仓库 1 · 森林 12 格 · 道路 8 格 · 居民 6", "住房 3 · 仓库 2 · 农田 1 · 道路 12 格 · 居民 10" };
    public int Kind { get; private set; } = -1;
    public int X { get; private set; }
    public int Y { get; private set; }
    public int StableDays { get; private set; }
    public bool Completed { get; private set; }
    private long _lastDay;
    public bool Start(Simulation sim, int kind, int x, int y)
    {
        if (kind < 0 || kind >= Names.Length || !sim.World.IsInBounds(x, y) || !sim.World.TileAt(x, y).Walkable) return false;
        Kind = kind; X = x; Y = y; StableDays = 0; Completed = false; _lastDay = sim.Clock / sim.Config.Clock.TicksPerDay;
        sim.InterveneRecordAuxiliary("开始聚落蓝图：" + Names[kind] + " @ " + new Int2(x, y)); return true;
    }
    public void Leave() { Kind = -1; StableDays = 0; Completed = false; }
    public BlueprintState Observe(Simulation sim)
    {
        int houses = 0, stores = 0, farms = 0, roads = 0, forest = 0, people = 0;
        bool Inside(int x, int y) => (x - X) * (x - X) + (y - Y) * (y - Y) <= 100;
        for (int k = 0; k < sim.Buildings.LiveCount; k++)
        {
            int index = sim.Buildings.LiveAt(k);
            if (!sim.Buildings.IsAlive(index) || sim.Buildings.StateOf(index) != BuildingState.Complete || !Inside(sim.Buildings.XOf(index), sim.Buildings.YOf(index))) continue;
            switch (sim.Buildings.KindOf(index)) { case BuildingKind.House: houses++; break; case BuildingKind.Storage: stores++; break; case BuildingKind.Farm: farms++; break; }
        }
        foreach (int slot in sim.Agents.AliveSlots()) if (Inside(sim.Agents.XOf(slot), sim.Agents.YOf(slot))) people++;
        for (int y = Math.Max(0, Y - 10); y <= Math.Min(sim.World.Height - 1, Y + 10); y++)
            for (int x = Math.Max(0, X - 10); x <= Math.Min(sim.World.Width - 1, X + 10); x++)
                if (Inside(x, y)) { var t = sim.World.TileAt(x, y); if (t.Terrain == TerrainKind.Road) roads++; if (t.Terrain == TerrainKind.Forest && t.Fire != FireState.Burning) forest++; }
        bool meets = Kind switch { 0 => houses >= 2 && stores >= 1 && farms >= 2 && people >= 6,
            1 => houses >= 2 && stores >= 1 && forest >= 12 && roads >= 8 && people >= 6,
            2 => houses >= 3 && stores >= 2 && farms >= 1 && roads >= 12 && people >= 10, _ => false };
        return new BlueprintState(houses, stores, farms, roads, forest, people, meets);
    }
    public void Advance(Simulation sim)
    {
        long day = sim.Clock / sim.Config.Clock.TicksPerDay;
        if (Kind < 0 || Completed || day <= _lastDay) return;
        // A late update cannot prove skipped days; count only the observed boundary.
        StableDays = Observe(sim).Meets ? (day == _lastDay + 1 ? StableDays + 1 : 1) : 0; _lastDay = day;
        if (StableDays >= 2) { Completed = true; sim.InterveneRecordAuxiliary("聚落蓝图达成：" + Names[Kind]); }
    }
    public JsonValue Encode() => JsonValue.Object().Set("kind", JsonValue.From(Kind)).Set("x", JsonValue.From(X)).Set("y", JsonValue.From(Y))
        .Set("stable", JsonValue.From(StableDays)).Set("completed", JsonValue.From(Completed)).Set("day", JsonValue.From(_lastDay));
    public static SettlementBlueprint Decode(JsonValue v) => new() { Kind = Math.Clamp(v.GetInt("kind", -1), -1, 2), X = v.GetInt("x"), Y = v.GetInt("y"),
        StableDays = Math.Clamp(v.GetInt("stable"), 0, 2), Completed = v.GetBool("completed"), _lastDay = v.GetLong("day") };
}
public sealed record BlueprintState(int Houses, int Stores, int Farms, int Roads, int Forest, int People, bool Meets);
