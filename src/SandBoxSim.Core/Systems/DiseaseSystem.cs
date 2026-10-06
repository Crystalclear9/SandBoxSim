using System;
using System.Collections.Generic;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.History;

namespace SandBoxSim.Core.Systems;

/// <summary>接触传播、潜伏、恢复与免疫。按日快照传播，避免一次更新感染全世界。</summary>
public sealed class DiseaseSystem : ISimEntitySet
{
    public sealed class Infection
    {
        public int Slot, Generation, Days, ImmuneDays;
        public bool Active;
    }
    private readonly Simulation _sim;
    private readonly List<Infection> _cases = new();
    public IReadOnlyList<Infection> Cases => _cases;
    public int EntityCount => _cases.Count;
    public DiseaseSystem(Simulation sim) { _sim = sim; }
    public Infection? OfSlot(int slot) => _cases.Find(c => c.Slot == slot && c.Generation == _sim.Agents.GenerationOf(slot));
    public bool Infect(int slot)
    {
        if (!_sim.Agents.IsSlotAlive(slot)) { return false; }
        var c = OfSlot(slot);
        if (c != null && (c.Active || c.ImmuneDays > 0)) { return false; }
        if (c == null) { c = new Infection { Slot = slot, Generation = _sim.Agents.GenerationOf(slot) }; _cases.Add(c); }
        c.Active = true; c.Days = 0;
        _sim.Events.Record(_sim.Clock, WorldEventType.Disease, _sim.Agents.NameOrOverride(slot) + " 感染疾病",
            EventImportance.Normal, _sim.Agents.PositionOf(slot), slot, -1, "接触传播或疫病干预");
        return true;
    }
    public void Heal(int slot)
    {
        var c = OfSlot(slot);
        if (c != null) { c.Active = false; c.Days = 0; c.ImmuneDays = _sim.Config.Disease.ImmunityDays; }
        if (_sim.Agents.IsSlotAlive(slot)) { _sim.Agents.SetHealth(slot, 1); }
    }
    public float LaborMultiplier(int slot) => _sim.Config.Disease.Enabled && OfSlot(slot)?.Active == true ? _sim.Config.Disease.SickLaborMultiplier : 1;
    public void TickDay(long tick)
    {
        if (!_sim.Config.Disease.Enabled) { return; }
        var cfg = _sim.Config.Disease;
        var sources = new List<int>();
        foreach (var c in _cases)
            if (c.Active && _sim.Agents.IsSlotAlive(c.Slot) && c.Generation == _sim.Agents.GenerationOf(c.Slot)
                && c.Days >= cfg.IncubationDays) { sources.Add(c.Slot); }
        foreach (var c in _cases)
        {
            if (!_sim.Agents.IsSlotAlive(c.Slot) || c.Generation != _sim.Agents.GenerationOf(c.Slot)) { c.Active = false; continue; }
            if (!c.Active) { c.ImmuneDays = Math.Max(0, c.ImmuneDays - 1); continue; }
            c.Days++;
            if (c.Days >= cfg.RecoveryDays) { Heal(c.Slot); continue; }
            if (c.Days < cfg.IncubationDays) { continue; }
            _sim.Agents.AddHealth(c.Slot, -cfg.HealthLossPerDay);
            if (_sim.Agents.HealthOf(c.Slot) <= 0) { _sim.Needs.Kill(_sim.Agents, c.Slot, DeathCause.Illness, tick); c.Active = false; }
        }
        foreach (int slot in _sim.Agents.AliveSlots())
        {
            var existing = OfSlot(slot);
            if (existing != null && (existing.Active || existing.ImmuneDays > 0)) { continue; }
            int contacts = 0;
            foreach (int source in sources)
                if (source != slot && Int2.SquaredDistance(_sim.Agents.PositionOf(source), _sim.Agents.PositionOf(slot)) <= cfg.ContactRadius * cfg.ContactRadius) { contacts++; }
            if (contacts == 0) { continue; }
            double probability = 1 - Math.Pow(1 - cfg.TransmissionPerContact, contacts);
            if (_sim.Random.Get(RngStream.Agents).NextFloat() < probability) { Infect(slot); }
        }
        _cases.RemoveAll(c => !_sim.Agents.IsSlotAlive(c.Slot) || c.Generation != _sim.Agents.GenerationOf(c.Slot)
            || !c.Active && c.ImmuneDays == 0);
    }
    public void Reset() => _cases.Clear();
    public JsonValue Encode() => PersistentData.Encode(_cases);
    public void Restore(JsonValue value) { PersistentData.Restore(value, _cases); }
    public ulong HashInto(ulong hash) => Hash64.Combine(hash, Encode().ToJson(false));
}
public sealed class DiseaseConfig
{
    public bool Enabled = true;
    public int IncubationDays = 2, RecoveryDays = 14, ImmunityDays = 30, ContactRadius = 3;
    public float TransmissionPerContact = 0.08f, HealthLossPerDay = 0.04f, SickLaborMultiplier = 0.5f;
}
