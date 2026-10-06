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
            int target = -1, nearest = int.MaxValue;
            for (int i = 0; i < _sim.Wildlife.Capacity; i++)
            {
                if (!_sim.Wildlife.IsAlive(i)) { continue; }
                int distance = Int2.SquaredDistance(new Int2(wolf.X, wolf.Y), _sim.Wildlife.PositionOf(i));
                if (distance <= _sim.Config.Predator.SenseRadius * _sim.Config.Predator.SenseRadius && distance < nearest)
                { target = i; nearest = distance; }
            }
            bool threat = _sim.Agents.TryFindNearest(wolf.X, wolf.Y, 3, out int human, out int humanDistance);
            wolf.HuntUtility = target < 0 ? 0 : (1 - wolf.Energy) * 0.8f + 0.2f;
            wolf.RestUtility = wolf.Energy * 0.65f;
            wolf.FleeUtility = threat ? 0.85f : 0;
            int dx = 0, dy = 0;
            if (wolf.FleeUtility > wolf.HuntUtility && wolf.FleeUtility > wolf.RestUtility)
            {
                wolf.Action = "Flee";
                dx = Math.Sign(wolf.X - _sim.Agents.XOf(human)); dy = Math.Sign(wolf.Y - _sim.Agents.YOf(human));
            }
            else if (wolf.DrinkUtility > wolf.HuntUtility && wolf.DrinkUtility > wolf.SleepUtility
                && WildlifeSystem.CanDrinkAt(_sim.World, wolf.X, wolf.Y))
            { wolf.Action = "Drink"; wolf.Thirst = SimMath.Clamp01(wolf.Thirst - elapsedDays * 2f); }
            else if (wolf.SleepUtility > wolf.HuntUtility)
            { wolf.Action = "Sleep"; wolf.Fatigue = SimMath.Clamp01(wolf.Fatigue - elapsedDays * 2f); }
            else if (wolf.HuntUtility > wolf.RestUtility)
            {
                wolf.Action = "Hunt";
                if (nearest <= 1)
                {
                    _sim.Wildlife.Kill(target, false); TotalKills++;
                    wolf.Action = "Eat";
                    wolf.Energy = Math.Min(1, wolf.Energy + _sim.Config.Predator.MealEnergy);
                }
                else { dx = Math.Sign(_sim.Wildlife.XOf(target) - wolf.X); dy = Math.Sign(_sim.Wildlife.YOf(target) - wolf.Y); }
            }
            else if (wolf.Energy > 0.7f) { wolf.Action = "Rest"; }
            else
            {
                wolf.Action = "Wander"; int direction = _sim.Random.Get(RngStream.Events).NextInt(4);
                dx = direction == 0 ? 1 : direction == 1 ? -1 : 0;
                dy = direction == 2 ? 1 : direction == 3 ? -1 : 0;
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
