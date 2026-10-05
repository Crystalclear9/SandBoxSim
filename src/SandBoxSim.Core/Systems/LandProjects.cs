using System;
using System.Collections.Generic;
using System.Linq;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Systems;

public sealed class LocalConditions
{
    public int Residents, Hungry, Sick, Housed, Burning;
    public float Food, Vegetation, Moisture;
    public static LocalConditions Observe(Simulation sim, int x, int y, int radius)
    {
        var result = new LocalConditions(); int tiles = 0;
        for (int py = Math.Max(0, y - radius); py <= Math.Min(sim.World.Height - 1, y + radius); py++)
            for (int px = Math.Max(0, x - radius); px <= Math.Min(sim.World.Width - 1, x + radius); px++)
            {
                if ((px - x) * (px - x) + (py - y) * (py - y) > radius * radius) { continue; }
                var tile = sim.World.TileAt(px, py); tiles++;
                result.Vegetation += tile.Vegetation; result.Moisture += tile.Moisture;
                if (tile.Fire == FireState.Burning) { result.Burning++; }
                if (tile.Resource.Kind == ResourceKind.Food) { result.Food += tile.Resource.Amount; }
            }
        if (tiles > 0) { result.Vegetation /= tiles; result.Moisture /= tiles; }
        foreach (int slot in sim.Agents.AliveSlots())
        {
            if (Int2.SquaredDistance(new Int2(x, y), sim.Agents.PositionOf(slot)) > radius * radius) { continue; }
            result.Residents++; if (sim.Agents.HungerOf(slot) > .7f) { result.Hungry++; }
            if (sim.Agents.DwellingOf(slot) >= 0) { result.Housed++; }
            if (sim.Diseases.OfSlot(slot)?.Active == true) { result.Sick++; }
        }
        return result;
    }
    public JsonValue Encode() => JsonValue.Object().Set("residents", JsonValue.From(Residents)).Set("hungry", JsonValue.From(Hungry))
        .Set("sick", JsonValue.From(Sick)).Set("housed", JsonValue.From(Housed)).Set("burning", JsonValue.From(Burning))
        .Set("food", JsonValue.From(Food)).Set("vegetation", JsonValue.From(Vegetation)).Set("moisture", JsonValue.From(Moisture));
    public static LocalConditions Decode(JsonValue value) => new() { Residents = value.GetInt("residents"), Hungry = value.GetInt("hungry"),
        Sick = value.GetInt("sick"), Housed = value.GetInt("housed"), Burning = value.GetInt("burning"),
        Food = value.GetFloat("food"), Vegetation = value.GetFloat("vegetation"), Moisture = value.GetFloat("moisture") };
}

public sealed class LandProject
{
    public int Id, Kind, X, Y, Radius, Stage;
    public long StartDay;
    public bool Cancelled;
    public bool Active => !Cancelled && Stage < 3;
    public LocalConditions Before = new(), After = new();
}

/// <summary>Optional god interventions over three days. Never builds NPC homes or forces social outcomes.</summary>
public sealed class LandProjects
{
    public static readonly string[] Names = { "食物绿洲", "防火走廊", "湿地修复", "林地复苏" };
    public static readonly string[] Briefs = {
        "改善空地 → 播散野生粮食 → 湿润土壤。水域和已有建筑保留。",
        "逐段清除燃料，留下可通行的走廊。适合提前隔开火源。",
        "三天补充土壤水分、改善肥力。不能替代饮用水域。",
        "分三圈恢复适合的空地植被与木材。不能在水域造林。" };
    public static int Cost(int radius) => 24 + Math.Clamp(radius, 2, 8) * 2;
    private readonly List<LandProject> _items = new();
    public IReadOnlyList<LandProject> Items => _items;
    public int ActiveCount => _items.Count(p => p.Active);
    private int _nextId = 1;
    public bool CanQueue(Simulation sim, int kind, int x, int y, int radius = 5)
    {
        if (kind < 0 || kind >= Names.Length || !sim.World.IsInBounds(x, y) || ActiveCount >= 4) { return false; }
        radius = Math.Clamp(radius, 2, 8);
        for (int py = Math.Max(0, y - radius); py <= Math.Min(sim.World.Height - 1, y + radius); py++)
            for (int px = Math.Max(0, x - radius); px <= Math.Min(sim.World.Width - 1, x + radius); px++)
                if ((px - x) * (px - x) + (py - y) * (py - y) <= radius * radius && (kind != 1 || Math.Abs(py - y) <= 1)
                    && Eligible(sim.World.TileAt(px, py), kind)) { return true; }
        return false;
    }
    private static bool Eligible(Tile tile, int kind) => tile.BuildingId <= 0 && tile.Terrain is not (TerrainKind.Water or TerrainKind.Lava or TerrainKind.Mountain)
        && (kind != 0 || tile.Terrain is not (TerrainKind.Forest or TerrainKind.Road))
        && (kind != 3 || tile.Terrain != TerrainKind.Road && tile.Fire != FireState.Burning);
    public LandProject? Queue(Simulation sim, int kind, int x, int y, int radius)
    {
        if (!CanQueue(sim, kind, x, y, radius)) { return null; }
        radius = Math.Clamp(radius, 2, 8);
        while (_items.Count >= 12) { var old = _items.FirstOrDefault(p => !p.Active); if (old == null) { return null; } _items.Remove(old); }
        var plan = new LandProject { Id = _nextId++, Kind = kind, X = x, Y = y, Radius = radius,
            StartDay = sim.Clock / sim.Config.Clock.TicksPerDay, Before = LocalConditions.Observe(sim, x, y, radius) };
        _items.Add(plan); sim.InterveneRecordAuxiliary("开始生态工程：" + Names[kind] + " @ " + new Int2(x, y)); return plan;
    }
    public bool Cancel(Simulation sim, int id)
    {
        var plan = _items.FirstOrDefault(p => p.Id == id && p.Active); if (plan == null) { return false; }
        plan.Cancelled = true; plan.After = LocalConditions.Observe(sim, plan.X, plan.Y, plan.Radius);
        sim.InterveneRecordAuxiliary("停止剩余工程：" + Names[plan.Kind]); return true;
    }
    public void Advance(Simulation sim)
    {
        long day = sim.Clock / sim.Config.Clock.TicksPerDay;
        foreach (var plan in _items)
            while (plan.Active && day >= plan.StartDay + plan.Stage + 1)
            {
                plan.Stage++; ApplyStage(sim, plan);
                if (plan.Stage == 3)
                {
                    plan.After = LocalConditions.Observe(sim, plan.X, plan.Y, plan.Radius);
                    sim.InterveneRecordAuxiliary("生态工程完成：" + Names[plan.Kind] + " @ " + new Int2(plan.X, plan.Y));
                }
            }
    }
    private static void ApplyStage(Simulation sim, LandProject p)
    {
        for (int y = Math.Max(0, p.Y - p.Radius); y <= Math.Min(sim.World.Height - 1, p.Y + p.Radius); y++)
            for (int x = Math.Max(0, p.X - p.Radius); x <= Math.Min(sim.World.Width - 1, p.X + p.Radius); x++)
            {
                int distance = (x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y);
                if (distance > p.Radius * p.Radius) { continue; }
                var tile = sim.World.TileAt(x, y);
                if (!Eligible(tile, p.Kind)) { continue; }
                if (p.Kind == 0)
                {
                    if (tile.Terrain == TerrainKind.Forest || tile.Terrain == TerrainKind.Road) { continue; }
                    if (p.Stage == 1) { sim.World.SetFertility(x, y, SimMath.Clamp01(tile.Fertility + .25f)); sim.InterveneSetTerrain(x, y, TerrainKind.Grass); }
                    if (p.Stage == 2) { sim.InterveneAddResource(x, y, ResourceKind.Food, 28); }
                    if (p.Stage == 3) { PlayerTools.Apply(sim, PlayerTool.Rain, x, y, 0, 35); }
                }
                else if (p.Kind == 1)
                {
                    if (Math.Abs(y - p.Y) > 1 || (x - p.X + p.Radius) % 3 != p.Stage - 1) { continue; }
                    sim.World.SetVegetation(x, y, 0); sim.World.ClearResource(x, y); sim.World.SetFire(x, y, FireState.None);
                    sim.InterveneSetTerrain(x, y, TerrainKind.Road, false);
                }
                else if (p.Kind == 2)
                {
                    sim.World.SetMoisture(x, y, SimMath.Clamp01(tile.Moisture + .22f));
                    sim.World.SetFertility(x, y, SimMath.Clamp01(tile.Fertility + .12f));
                }
                else
                {
                    if (tile.Terrain == TerrainKind.Road || tile.Fire == FireState.Burning) { continue; }
                    int ring = Math.Min(2, (int)(Math.Sqrt(distance) * 3 / (p.Radius + 1)));
                    if (ring == p.Stage - 1) { if (tile.Fire == FireState.Burnt) { sim.World.SetFire(x, y, FireState.None); } sim.InterveneGrowForest(x, y, 0, 1); }
                }
            }
        sim.World.RefreshSpatialIndex();
    }
    public JsonValue Encode()
    {
        var plans = JsonValue.Array();
        foreach (var p in _items) { plans.Add(JsonValue.Object().Set("id", JsonValue.From(p.Id)).Set("kind", JsonValue.From(p.Kind))
            .Set("x", JsonValue.From(p.X)).Set("y", JsonValue.From(p.Y)).Set("radius", JsonValue.From(p.Radius)).Set("stage", JsonValue.From(p.Stage))
            .Set("startDay", JsonValue.From(p.StartDay)).Set("cancelled", JsonValue.From(p.Cancelled)).Set("before", p.Before.Encode()).Set("after", p.After.Encode())); }
        return JsonValue.Object().Set("nextId", JsonValue.From(_nextId)).Set("items", plans);
    }
    public static LandProjects Decode(JsonValue root)
    {
        var projects = new LandProjects();
        foreach (var v in root.Get("items").Items.Take(12))
        {
            int kind = v.GetInt("kind", -1); if (kind < 0 || kind >= Names.Length) { continue; }
            projects._items.Add(new LandProject { Id = v.GetInt("id"), Kind = kind, X = v.GetInt("x"), Y = v.GetInt("y"),
                Radius = Math.Clamp(v.GetInt("radius", 5), 2, 8), Stage = Math.Clamp(v.GetInt("stage"), 0, 3),
                StartDay = v.GetLong("startDay"), Cancelled = v.GetBool("cancelled"), Before = LocalConditions.Decode(v.Get("before")), After = LocalConditions.Decode(v.Get("after")) });
        }
        projects._nextId = Math.Max(root.GetInt("nextId", 1), projects._items.Count == 0 ? 1 : projects._items.Max(p => p.Id) + 1);
        return projects;
    }
}
