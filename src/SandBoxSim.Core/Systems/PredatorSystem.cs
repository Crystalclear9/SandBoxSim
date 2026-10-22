using System;
using System.Collections.Generic;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.History;

namespace SandBoxSim.Core.Systems;

/// <summary>狼的局部感知与效用决策。真实猎杀消耗猎物，种群依赖食物恢复与繁殖。</summary>
public sealed class PredatorSystem : ISimEntitySet
{
    public sealed class Predator
    {
        public int Id, X, Y, AgeDays;
        public float Energy = 0.7f;
        public float Thirst, Fatigue, DrinkUtility, SleepUtility;
        public string Action = "Wander";
        public float HuntUtility, RestUtility, FleeUtility;
    }
    private readonly Simulation _sim;
    private readonly List<Predator> _wolves = new();
    // Scratch search state is rebuilt for each decision and is not simulation state.
    private readonly Queue<(Int2 Position, Int2 First, int Depth)> _drinkSearch = new();
    private readonly HashSet<Int2> _drinkVisited = new();
    private readonly Dictionary<Int2, int> _preyByPosition = new();
    private static readonly Int2[] Directions = { Int2.Right, Int2.Left, Int2.Down, Int2.Up };
    private int _nextId = 1;
    public int TotalKills { get; private set; }
    public IReadOnlyList<Predator> Wolves => _wolves;
    public int EntityCount => _wolves.Count;
    public PredatorSystem(Simulation sim) { _sim = sim; }
    public bool Spawn(int x, int y)
    {
        if (!_sim.World.IsInBounds(x, y) || !_sim.World.TileAt(x, y).Walkable || _wolves.Count >= _sim.Config.Predator.MaxPopulation) { return false; }
        _wolves.Add(new Predator { Id = _nextId++, X = x, Y = y });
        _sim.Events.Record(_sim.Clock, WorldEventType.WildlifeSpawned, "一只狼出现在世界", EventImportance.Normal, new Int2(x, y));
        return true;
    }
    public bool Nearest(int x, int y, int radius, out Int2 location, out int distance)
    {
        location = default; distance = int.MaxValue; bool found = false;
        foreach (var wolf in _wolves)
        {
            int d = Int2.SquaredDistance(new Int2(x, y), new Int2(wolf.X, wolf.Y));
            if (d <= radius * radius && d < distance) { location = new Int2(wolf.X, wolf.Y); distance = d; found = true; }
        }
        return found;
    }
    public void TickFast(long tick)
    {
        if (!_sim.Config.Predator.Enabled) { return; }
        foreach (var wolf in _wolves)
        {
            float elapsedDays = 10f / _sim.World.Calendar.TicksPerDay;
            wolf.Thirst = SimMath.Clamp01(wolf.Thirst + elapsedDays * 0.2f);
            wolf.Fatigue = SimMath.Clamp01(wolf.Fatigue + elapsedDays * 0.3f);
            wolf.DrinkUtility = wolf.Thirst * wolf.Thirst;
            wolf.SleepUtility = wolf.Fatigue * wolf.Fatigue;
            // Decide from reachable prey, not the nearest animal behind a wall.
            // Search only when hunger can beat resting; scratch state is reused.
            int target = -1;
            var huntStep = new Int2(wolf.X, wolf.Y);
            float possibleHuntUtility = (1 - wolf.Energy) * 0.8f + 0.2f;
            wolf.RestUtility = wolf.Energy * 0.65f;
            if (possibleHuntUtility > wolf.RestUtility) { TryHuntStep(wolf, out target, out huntStep); }
            bool threat = _sim.Agents.TryFindNearest(wolf.X, wolf.Y, 3, out int human, out int humanDistance);
            wolf.HuntUtility = target < 0 ? 0 : (1 - wolf.Energy) * 0.8f + 0.2f;
            wolf.FleeUtility = threat ? 0.85f : 0;
            int dx = 0, dy = 0;
            if (wolf.FleeUtility > wolf.HuntUtility && wolf.FleeUtility > wolf.RestUtility)
            {
                wolf.Action = "Flee";
                dx = Math.Sign(wolf.X - _sim.Agents.XOf(human)); dy = Math.Sign(wolf.Y - _sim.Agents.YOf(human));
            }
            else if (wolf.DrinkUtility > wolf.HuntUtility && wolf.DrinkUtility > wolf.SleepUtility
                && wolf.DrinkUtility > wolf.RestUtility && TryDrinkStep(wolf, out var drinkStep))
            {
                wolf.Action = "Drink";
                if (drinkStep.X == wolf.X && drinkStep.Y == wolf.Y)
                    wolf.Thirst = SimMath.Clamp01(wolf.Thirst - elapsedDays * 2f);
                else { dx = drinkStep.X - wolf.X; dy = drinkStep.Y - wolf.Y; }
            }
            // A missing prey target has utility zero. Comparing sleep only to
            // that zero trapped every isolated wolf in a tiny-fatigue sleep loop.
            else if (wolf.Fatigue >= .45f && wolf.SleepUtility > Math.Max(wolf.HuntUtility, wolf.RestUtility))
            { wolf.Action = "Sleep"; wolf.Fatigue = SimMath.Clamp01(wolf.Fatigue - elapsedDays * 2f); }
            else if (wolf.HuntUtility > wolf.RestUtility)
            {
                wolf.Action = "Hunt";
                if (huntStep.X == wolf.X && huntStep.Y == wolf.Y)
                {
                    _sim.Wildlife.Kill(target, false); TotalKills++;
                    wolf.Action = "Eat";
                    wolf.Energy = Math.Min(1, wolf.Energy + _sim.Config.Predator.MealEnergy);
                }
                else { dx = huntStep.X - wolf.X; dy = huntStep.Y - wolf.Y; }
            }
            else if (wolf.Energy > 0.7f) { wolf.Action = "Rest"; }
            else
            {
                wolf.Action = "Wander"; int direction = _sim.Random.Get(RngStream.Events).NextInt(4);
                for (int choice = 0; choice < Directions.Length; choice++)
                {
                    var step = Directions[(direction + choice) % Directions.Length];
                    int nx = wolf.X + step.X, ny = wolf.Y + step.Y;
                    if (!_sim.World.IsInBounds(nx, ny) || !_sim.World.TileAt(nx, ny).Walkable) { continue; }
                    dx = step.X; dy = step.Y; break;
                }
            }
            if (dx != 0 || dy != 0)
            {
                int nx = wolf.X + dx, ny = wolf.Y + (dx != 0 ? 0 : dy);
                if (_sim.World.IsInBounds(nx, ny) && _sim.World.TileAt(nx, ny).Walkable) { wolf.X = nx; wolf.Y = ny; }
            }
            wolf.Energy = Math.Max(0, wolf.Energy - _sim.Config.Predator.EnergyLossPerFastTick);
            if (wolf.Thirst >= 0.95f) { wolf.Energy = Math.Max(0, wolf.Energy - elapsedDays); }
        }
        _wolves.RemoveAll(w => w.Energy <= 0);
    }
    private bool TryHuntStep(Predator wolf, out int target, out Int2 step)
    {
        var origin = new Int2(wolf.X, wolf.Y); target = -1; step = origin;
        int radius = Math.Max(0, _sim.Config.Predator.SenseRadius);
        _preyByPosition.Clear();
        for (int k = 0; k < _sim.Wildlife.LiveCount; k++)
        {
            int slot = _sim.Wildlife.LiveAt(k);
            var position = _sim.Wildlife.PositionOf(slot);
            if (Int2.SquaredDistance(origin, position) > radius * radius) { continue; }
            // Explicit slot tie-break keeps decisions independent of swap-delete order.
            if (!_preyByPosition.TryGetValue(position, out int previous) || slot < previous)
                _preyByPosition[position] = slot;
        }
        if (_preyByPosition.Count == 0) { return false; }
        _drinkSearch.Clear(); _drinkVisited.Clear();
        _drinkSearch.Enqueue((origin, origin, 0)); _drinkVisited.Add(origin);
        // A finite detour budget permits obstacles without scanning the whole world.
        int pathBudget = radius * 2;
        while (_drinkSearch.Count > 0)
        {
            var current = _drinkSearch.Dequeue();
            if (_preyByPosition.TryGetValue(current.Position, out target))
            { step = current.First; return true; }
            foreach (var direction in Directions)
            {
                if (_preyByPosition.TryGetValue(current.Position + direction, out target))
                { step = current.First; return true; }
            }
            if (current.Depth >= pathBudget) { continue; }
            foreach (var direction in Directions)
            {
                var next = current.Position + direction;
                if (!_sim.World.IsInBounds(next.X, next.Y) || !_sim.World.TileAt(next.X, next.Y).Walkable || !_drinkVisited.Add(next)) { continue; }
                var first = current.Depth == 0 ? next : current.First;
                _drinkSearch.Enqueue((next, first, current.Depth + 1));
            }
        }
        target = -1; return false;
    }
    private bool TryDrinkStep(Predator wolf, out Int2 step)
    {
        var origin = new Int2(wolf.X, wolf.Y); step = origin;
        if (WildlifeSystem.CanDrinkAt(_sim.World, wolf.X, wolf.Y)) { return true; }
        int reach = Math.Max(0, _sim.Config.Predator.SenseRadius);
        _drinkSearch.Clear(); _drinkVisited.Clear();
        _drinkSearch.Enqueue((origin, origin, 0)); _drinkVisited.Add(origin);
        while (_drinkSearch.Count > 0)
        {
            var current = _drinkSearch.Dequeue();
            if (current.Depth >= reach) { continue; }
            foreach (var direction in Directions)
            {
                var next = current.Position + direction;
                if (!_sim.World.IsInBounds(next.X, next.Y) || !_sim.World.TileAt(next.X, next.Y).Walkable || !_drinkVisited.Add(next)) { continue; }
                var first = current.Depth == 0 ? next : current.First;
                if (WildlifeSystem.CanDrinkAt(_sim.World, next.X, next.Y)) { step = first; return true; }
                _drinkSearch.Enqueue((next, first, current.Depth + 1));
            }
        }
        return false;
    }
    public void TickDay(long tick)
    {
        if (!_sim.Config.Predator.Enabled) { return; }
        var newborn = new List<Int2>();
        foreach (var wolf in _wolves)
        {
            wolf.AgeDays++;
            if (wolf.Energy > 0.8f && wolf.AgeDays >= 10 && _sim.Wildlife.LiveCount >= _wolves.Count * 4
                && _sim.Random.Get(RngStream.Events).NextDouble() < _sim.Config.Predator.ReproductionChance)
            { newborn.Add(new Int2(wolf.X, wolf.Y)); wolf.Energy -= 0.25f; }
        }
        _wolves.RemoveAll(w => w.AgeDays > _sim.Config.Predator.LifespanDays);
        foreach (var p in newborn) { Spawn(p.X, p.Y); }
    }
    public void Reset() { _wolves.Clear(); _nextId = 1; TotalKills = 0; }
    public JsonValue Encode() => JsonValue.Object().Set("wolves", PersistentData.Encode(_wolves))
        .Set("nextId", JsonValue.From(_nextId)).Set("totalKills", JsonValue.From(TotalKills));
    public void Restore(JsonValue value)
    { Reset(); PersistentData.Restore(value.Get("wolves"), _wolves); _nextId = value.GetInt("nextId", 1); TotalKills = value.GetInt("totalKills"); }
    public ulong HashInto(ulong hash) => Hash64.Combine(hash, Encode().ToJson(false));
}
public sealed class PredatorConfig
{
    public bool Enabled = true;
    public int SenseRadius = 10, MaxPopulation = 40, LifespanDays = 150;
    public float MealEnergy = 0.4f, EnergyLossPerFastTick = 0.002f, ReproductionChance = 0.05f;
}
