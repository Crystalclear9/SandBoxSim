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
    public int Id, Kind, X, Y, Radius, Stage, Duration = 3, Policy, CareDays, SkippedDays;
    public long StartDay, LastCareDay;
    public bool Cancelled;
    public string LastNotice = "";
    public bool Active => !Cancelled && Stage < Duration;
    public bool Managed => !Cancelled && Stage == Duration && Policy > 0;
    public LocalConditions Before = new(), After = new();
}

/// <summary>Data-driven land interventions, followed by optional daily management with physical tradeoffs.</summary>
public sealed class LandProjects
{
    public static string[] Names => ProjectCatalog.Default.Recipes.Select(r => r.Name).ToArray();
    public static string[] Briefs => ProjectCatalog.Default.Recipes.Select(r => r.Brief).ToArray();
    public static readonly string[] Policies = { "自然演替", "生态维护", "资源优先" };
    public static int Cost(int radius) => 24 + Math.Clamp(radius, 2, 8) * 2;
    public ProjectCatalog Catalog { get; }
    public LandProjects(ProjectCatalog? catalog = null) { Catalog = catalog ?? ProjectCatalog.Default; }
    public ProjectRecipe Recipe(int kind) => Catalog.Recipes[kind];
    private readonly List<LandProject> _items = new();
    public IReadOnlyList<LandProject> Items => _items;
    public int ActiveCount => _items.Count(p => p.Active);
    public int ManagedCount => _items.Count(p => p.Managed);
    private int _nextId = 1;
    public bool CanQueue(Simulation sim, int kind, int x, int y, int radius = 5)
    {
        if (kind < 0 || kind >= Catalog.Recipes.Count || !sim.World.IsInBounds(x, y) || ActiveCount >= 4) { return false; }
        radius = Math.Clamp(radius, 2, 8); var recipe = Recipe(kind);
        for (int py = Math.Max(0, y - radius); py <= Math.Min(sim.World.Height - 1, y + radius); py++)
            for (int px = Math.Max(0, x - radius); px <= Math.Min(sim.World.Width - 1, x + radius); px++)
                if ((px - x) * (px - x) + (py - y) * (py - y) <= radius * radius
                    && recipe.Steps.Any(s => Shape(s.Action, px - x, py - y) && Suitable(sim.World.TileAt(px, py), recipe, s)))
                    { return true; }
        return false;
    }
    private static bool Eligible(Tile tile, ProjectRecipe recipe) => tile.BuildingId <= 0
        && tile.Terrain is not (TerrainKind.Water or TerrainKind.Lava or TerrainKind.Mountain)
        && (!recipe.ExcludeForest || tile.Terrain != TerrainKind.Forest)
        && (!recipe.ExcludeRoad || tile.Terrain != TerrainKind.Road)
        && (!recipe.NoBurning || tile.Fire != FireState.Burning);
    private static bool Shape(string action, int dx, int dy) => action == "clear_strip" ? Math.Abs(dy) <= 1 : action != "trail" || Math.Abs(dx - dy) <= 1;
    public LandProject? Queue(Simulation sim, int kind, int x, int y, int radius)
    {
        if (!CanQueue(sim, kind, x, y, radius)) { return null; }
        radius = Math.Clamp(radius, 2, 8);
        while (_items.Count >= 12)
        {
            var old = _items.FirstOrDefault(p => !p.Active && !p.Managed);
            if (old == null) { return null; } _items.Remove(old);
        }
        var plan = new LandProject { Id = _nextId++, Kind = kind, X = x, Y = y, Radius = radius, Duration = Recipe(kind).Steps.Length,
            StartDay = sim.Clock / sim.Config.Clock.TicksPerDay, Before = LocalConditions.Observe(sim, x, y, radius) };
        _items.Add(plan); sim.InterveneRecordAuxiliary("开始生态工程：" + Recipe(kind).Name + " @ " + new Int2(x, y)); return plan;
    }
    public bool Cancel(Simulation sim, int id)
    {
        var plan = _items.FirstOrDefault(p => p.Id == id && p.Active); if (plan == null) { return false; }
        plan.Cancelled = true; plan.After = LocalConditions.Observe(sim, plan.X, plan.Y, plan.Radius);
        sim.InterveneRecordAuxiliary("停止剩余工程：" + Recipe(plan.Kind).Name); return true;
    }
    public bool SetPolicy(Simulation sim, int id, int policy)
    {
        var p = _items.FirstOrDefault(p => p.Id == id && !p.Cancelled && !p.Active);
        if (p == null || policy < 0 || policy > 2 || p.Policy == policy || policy > 0 && !p.Managed && ManagedCount >= 4) { return false; }
        p.Policy = policy; p.LastCareDay = sim.Clock / sim.Config.Clock.TicksPerDay;
        p.LastNotice = Policies[policy]; sim.InterveneRecordAuxiliary(Recipe(p.Kind).Name + " · " + Policies[policy]); return true;
    }
    public void Advance(Simulation sim, WorldTrial? trial = null)
    {
        long day = sim.Clock / sim.Config.Clock.TicksPerDay;
        foreach (var plan in _items)
        {
            while (plan.Active && day >= plan.StartDay + plan.Stage + 1)
            {
                plan.Stage++;
                Apply(sim, plan, Recipe(plan.Kind).Steps[plan.Stage - 1], plan.Stage);
                if (!plan.Active)
                {
                    plan.LastCareDay = day; plan.After = LocalConditions.Observe(sim, plan.X, plan.Y, plan.Radius);
                    sim.InterveneRecordAuxiliary("生态工程完成：" + Recipe(plan.Kind).Name + " @ " + new Int2(plan.X, plan.Y));
                }
            }
            while (plan.Managed && plan.LastCareDay < day)
            {
                plan.LastCareDay++; var recipe = Recipe(plan.Kind); var step = plan.Policy == 1 ? recipe.Care : recipe.Harvest;
                if (!HasSuitableGround(sim, plan, step)) { plan.SkippedDays++; plan.LastNotice = "土地条件不足 · 本日未扣维护费用"; continue; }
                if (trial != null && !trial.TrySpendPoints(recipe.Upkeep)) { plan.SkippedDays++; plan.LastNotice = "额度不足 · 本日维护暂停"; continue; }
                Apply(sim, plan, step, 0); plan.CareDays++;
                plan.LastNotice = Policies[plan.Policy] + " · 已维护 " + plan.CareDays + " 天";
            }
        }
    }
    private bool HasSuitableGround(Simulation sim, LandProject p, RecipeStep step)
    {
        for (int y = Math.Max(0, p.Y - p.Radius); y <= Math.Min(sim.World.Height - 1, p.Y + p.Radius); y++)
            for (int x = Math.Max(0, p.X - p.Radius); x <= Math.Min(sim.World.Width - 1, p.X + p.Radius); x++)
                if ((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y) <= p.Radius * p.Radius
                    && Suitable(sim.World.TileAt(x, y), Recipe(p.Kind), step) && Shape(step.Action, x - p.X, y - p.Y)) { return true; }
        return false;
    }
    private static bool Suitable(Tile t, ProjectRecipe r, RecipeStep s)
    {
        if (!Eligible(t, r) || t.Moisture < s.MinMoisture || t.Fertility < s.MinFertility) { return false; }
        if (s.Action is "food" or "extract_food") { return t.Terrain is TerrainKind.Grass or TerrainKind.Sand; }
        if (s.Action == "extract_wood") { return t.Terrain == TerrainKind.Forest; }
        if (s.Action is "forest" or "forest_ring") { return t.Terrain is TerrainKind.Grass or TerrainKind.Sand or TerrainKind.Forest; }
        return true;
    }
    private void Apply(Simulation sim, LandProject p, RecipeStep step, int phase)
    {
        for (int y = Math.Max(0, p.Y - p.Radius); y <= Math.Min(sim.World.Height - 1, p.Y + p.Radius); y++)
            for (int x = Math.Max(0, p.X - p.Radius); x <= Math.Min(sim.World.Width - 1, p.X + p.Radius); x++)
            {
                int dx = x - p.X, dy = y - p.Y, distance = dx * dx + dy * dy;
                var tile = sim.World.TileAt(x, y);
                if (distance > p.Radius * p.Radius || !Suitable(tile, Recipe(p.Kind), step) || !Shape(step.Action, dx, dy)) { continue; }
                switch (step.Action)
                {
                    case "meadow":
                        sim.World.SetFertility(x, y, SimMath.Clamp01(tile.Fertility + step.Amount)); sim.InterveneSetTerrain(x, y, TerrainKind.Grass); break;
                    case "food":
                        sim.InterveneAddResource(x, y, ResourceKind.Food, step.Amount); break;
                    case "rain": PlayerTools.Apply(sim, PlayerTool.Rain, x, y, 0, step.Amount); break;
                    case "wetland":
                        sim.World.SetMoisture(x, y, SimMath.Clamp01(tile.Moisture + step.Amount));
                        sim.World.SetFertility(x, y, SimMath.Clamp01(tile.Fertility + step.Amount * .54545456f)); break;
                    case "clear_strip":
                    case "trail":
                        if (phase > 0 && (dx + p.Radius) % p.Duration != phase - 1) { break; }
                        sim.World.SetVegetation(x, y, 0); sim.World.ClearResource(x, y); sim.World.SetFire(x, y, FireState.None);
                        sim.InterveneSetTerrain(x, y, TerrainKind.Road, false); break;
                    case "forest_ring":
                        int ring = Math.Min(p.Duration - 1, (int)(Math.Sqrt(distance) * p.Duration / (p.Radius + 1)));
                        if (ring != phase - 1) { break; }
                        goto case "forest";
                    case "forest":
                        if (tile.Fire == FireState.Burning) { break; }
                        if (tile.Terrain == TerrainKind.Forest)
                        {
                            sim.World.SetVegetation(x, y, SimMath.Clamp01(tile.Vegetation + .035f * step.Amount));
                            sim.InterveneAddResource(x, y, ResourceKind.Wood, step.Amount * 2); break;
                        }
                        if (tile.Fire == FireState.Burnt) { sim.World.SetFire(x, y, FireState.None); }
                        sim.InterveneGrowForest(x, y, 0, step.Amount); break;
                    case "mosaic":
                        if ((x + y) % 3 == 0)
                        {
                            if (tile.Fire != FireState.Burning)
                            {
                                if (tile.Terrain == TerrainKind.Forest) { sim.InterveneAddResource(x, y, ResourceKind.Wood, step.Amount); }
                                else { sim.InterveneGrowForest(x, y, 0, 1); }
                            }
                        }
                        else if (tile.Terrain != TerrainKind.Forest)
                        {
                            if (tile.Terrain != TerrainKind.Grass) { sim.InterveneSetTerrain(x, y, TerrainKind.Grass); }
                            sim.InterveneAddResource(x, y, ResourceKind.Food, step.Amount);
                        }
                        break;
                    case "extract_food":
                    case "extract_wood":
                        sim.InterveneAddResource(x, y, step.Action == "extract_food" ? ResourceKind.Food : ResourceKind.Wood, step.Amount);
                        sim.World.SetMoisture(x, y, SimMath.Clamp01(tile.Moisture - .05f));
                        sim.World.SetFertility(x, y, SimMath.Clamp01(tile.Fertility - .025f));
                        sim.World.SetVegetation(x, y, SimMath.Clamp01(tile.Vegetation - .03f)); break;
                }
            }
        sim.World.RefreshSpatialIndex();
    }
    public JsonValue Encode()
    {
        var plans = JsonValue.Array();
        foreach (var p in _items) { plans.Add(JsonValue.Object().Set("id", JsonValue.From(p.Id)).Set("kind", JsonValue.From(p.Kind))
            .Set("x", JsonValue.From(p.X)).Set("y", JsonValue.From(p.Y)).Set("radius", JsonValue.From(p.Radius)).Set("stage", JsonValue.From(p.Stage))
            .Set("duration", JsonValue.From(p.Duration)).Set("policy", JsonValue.From(p.Policy)).Set("careDays", JsonValue.From(p.CareDays))
            .Set("skippedDays", JsonValue.From(p.SkippedDays)).Set("lastCareDay", JsonValue.From(p.LastCareDay)).Set("lastNotice", JsonValue.From(p.LastNotice))
            .Set("startDay", JsonValue.From(p.StartDay)).Set("cancelled", JsonValue.From(p.Cancelled)).Set("before", p.Before.Encode()).Set("after", p.After.Encode())); }
        return JsonValue.Object().Set("nextId", JsonValue.From(_nextId)).Set("catalog", Catalog.Encode()).Set("items", plans);
    }
    public static LandProjects Decode(JsonValue root)
    {
        var projects = new LandProjects(root.Get("catalog").IsNull ? null : ProjectCatalog.Parse(root.Get("catalog").ToJson()));
        foreach (var v in root.Get("items").Items.Take(12))
        {
            int kind = v.GetInt("kind", -1); if (kind < 0 || kind >= projects.Catalog.Recipes.Count) { continue; }
            int duration = projects.Recipe(kind).Steps.Length;
            projects._items.Add(new LandProject { Id = v.GetInt("id"), Kind = kind, X = v.GetInt("x"), Y = v.GetInt("y"),
                Radius = Math.Clamp(v.GetInt("radius", 5), 2, 8), Stage = Math.Clamp(v.GetInt("stage"), 0, duration), Duration = duration,
                Policy = Math.Clamp(v.GetInt("policy"), 0, 2), CareDays = v.GetInt("careDays"), SkippedDays = v.GetInt("skippedDays"),
                LastCareDay = v.GetLong("lastCareDay", v.GetLong("startDay") + duration), LastNotice = v.GetString("lastNotice"),
                StartDay = v.GetLong("startDay"), Cancelled = v.GetBool("cancelled"), Before = LocalConditions.Decode(v.Get("before")), After = LocalConditions.Decode(v.Get("after")) });
        }
        projects._nextId = Math.Max(root.GetInt("nextId", 1), projects._items.Count == 0 ? 1 : projects._items.Max(p => p.Id) + 1);
        return projects;
    }
}
