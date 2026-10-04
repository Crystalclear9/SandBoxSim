using System;
using System.Collections.Generic;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.History;

namespace SandBoxSim.Core.Systems;

/// <summary>持久人物身份、亲属、居所、职业、成员归属、领土和领袖。行为由现有需求驱动。</summary>
public sealed class SocietySystem : ISimEntitySet
{
    public sealed class Person
    {
        public long Id, Mother, Father, Partner, BornTick, DiedTick = -1;
        public int Slot, Generation, Age, Settlement, Home = -1, Workplace = -1, Job;
        public bool Alive = true;
        public string Name = "";
    }
    public sealed class Leadership { public int Settlement; public long Person; }
    public sealed class Entry
    {
        public long Tick, Actor, Target;
        public int Type, X, Y, Settlement;
        public string Description = "", Cause = "";
    }
    private readonly Simulation _sim;
    private readonly List<Person> _people = new();
    private readonly List<Leadership> _leaders = new();
    private readonly List<Entry> _history = new();
    private readonly Dictionary<long, Person> _byId = new();
    public IReadOnlyList<Person> People => _people;
    public IReadOnlyList<Entry> History => _history;
    public IReadOnlyList<Leadership> Leaders => _leaders;
    public int EntityCount => _people.Count;

    public SocietySystem(Simulation sim) { _sim = sim; sim.Events.Recorded += Observe; }
    public long Identity(int slot) => slot < 0 || slot >= _sim.Agents.Capacity ? 0
        : ((long)(_sim.Agents.GenerationOf(slot) + 1) << 32) | (uint)slot;
    public Person? Find(long id) => _byId.TryGetValue(id, out Person? person) ? person : null;
    public Person? OfSlot(int slot) => Find(Identity(slot));

    private Person Ensure(int slot, int? generationOverride = null)
    {
        int generation = generationOverride ?? _sim.Agents.GenerationOf(slot);
        long id = ((long)(generation + 1) << 32) | (uint)slot;
        if (_byId.TryGetValue(id, out Person? person)) { return person; }
        var a = _sim.Agents;
        person = new Person { Id = id, Slot = slot, Generation = generation, Name = a.NameOrOverride(slot),
            Mother = Identity(a.MotherOf(slot)), Father = Identity(a.FatherOf(slot)),
            BornTick = _sim.Clock - (long)a.AgeDaysOf(slot) * _sim.World.Calendar.TicksPerDay };
        _people.Add(person); _byId[id] = person;
        return person;
    }

    private void Observe(WorldEvent ev)
    {
        bool personEvent = (int)ev.Type >= 20 && (int)ev.Type <= 29 || ev.Type == WorldEventType.Marriage
            || ev.Type == WorldEventType.AgentChangedJob || ev.Type == WorldEventType.LeaderElected || ev.Type == WorldEventType.Disease;
        personEvent |= ev.Type == WorldEventType.TradeRouteEstablished && ev.Actor >= 0;
        long actor = 0, target = 0;
        if (personEvent && ev.Actor >= 0 && ev.Actor < _sim.Agents.Capacity)
        {
            Person p = ev.Type == WorldEventType.AgentDied
                ? Ensure(ev.Actor, _sim.Agents.GenerationOf(ev.Actor) - 1) : Ensure(ev.Actor);
            actor = p.Id;
            if (ev.Type == WorldEventType.AgentDied) { p.Alive = false; p.Age = _sim.Agents.AgeDaysOf(ev.Actor); p.DiedTick = ev.Tick; }
            if (ev.Target >= 0 && ev.Target < _sim.Agents.Capacity) { target = Identity(ev.Target); }
        }
        if (ev.Importance < EventImportance.Normal) { return; }
        _history.Add(new Entry { Tick = ev.Tick, Actor = actor, Target = target, Type = (int)ev.Type,
            X = ev.Location.X, Y = ev.Location.Y, Settlement = TerritoryAt(ev.Location.X, ev.Location.Y),
            Description = ev.Description, Cause = ev.Cause });
        int excess = _history.Count - _sim.Config.Society.HistoryCapacity;
        if (excess > 0) { _history.RemoveRange(0, excess); }
    }

    /// <summary>最近聚落的实际领土，边界可争夺；不授予不相邻的全图资源所有权。</summary>
    public int TerritoryAt(int x, int y)
    {
        int id = 0; double nearest = double.MaxValue;
        for (int i = 0; i < _sim.Settlements.EntityCount; i++)
        {
            var s = _sim.Settlements.At(i); if (s.Dissolved) { continue; }
            long dx = x - s.CenterX, dy = y - s.CenterY;
            double distance = dx * dx + dy * dy;
            if (distance <= _sim.Config.Society.TerritoryRadius * _sim.Config.Society.TerritoryRadius && distance < nearest)
            { nearest = distance; id = s.Id; }
        }
        return id;
    }

    public long LeaderOf(int settlement)
    { foreach (Leadership leader in _leaders) { if (leader.Settlement == settlement) { return leader.Person; } } return 0; }

    public IEnumerable<Person> ChildrenOf(long person)
    { foreach (Person child in _people) { if (child.Mother == person || child.Father == person) { yield return child; } } }
    public IEnumerable<Person> SiblingsOf(long person)
    {
        Person? p = Find(person); if (p == null) { yield break; }
        foreach (Person other in _people)
            if (other.Id != person && (p.Mother != 0 && p.Mother == other.Mother || p.Father != 0 && p.Father == other.Father))
                yield return other;
    }
    public IEnumerable<Entry> TimelineOf(long id)
    { foreach (Entry ev in _history) { if (ev.Actor == id || ev.Target == id) { yield return ev; } } }
    public string SettlementName(int id)
    {
        if (id <= 0) { return "未定居"; }
        string[] prefixes = { "青河", "松岭", "南溪", "石岸", "白杨", "丰原", "望山", "清泉" };
        int index = (int)(Hash64.Of(_sim.World.Seed ^ id) % (ulong)prefixes.Length);
        return prefixes[index] + "聚落 " + id;
    }
    public IEnumerable<Person> MembersOf(int settlement)
    { foreach (var p in _people) { if (p.Alive && p.Settlement == settlement) { yield return p; } } }
    public IEnumerable<int> BuildingsOf(int settlement)
    {
        for (int i = 0; i < _sim.Buildings.Capacity; i++)
            if (_sim.Buildings.IsAlive(i) && TerritoryAt(_sim.Buildings.XOf(i), _sim.Buildings.YOf(i)) == settlement) { yield return i; }
    }

    public void TickDay(long tick)
    {
        if (!_sim.Config.Society.Enabled) { return; }
        foreach (Person p in _people) { p.Alive = _sim.Agents.IsSlotAlive(p.Slot) && Identity(p.Slot) == p.Id; }
        foreach (int slot in _sim.Agents.AliveSlots())
        {
            Person p = Ensure(slot); var a = _sim.Agents;
            p.Age = a.AgeDaysOf(slot); p.Home = a.DwellingOf(slot);
            long partner = a.PartnerOf(slot) >= 0 ? Identity(a.PartnerOf(slot)) : 0;
            if (partner != 0 && partner != p.Partner && p.Id < partner)
                _sim.Events.Record(tick, WorldEventType.Marriage, p.Name + " 与 " + a.NameOrOverride(a.PartnerOf(slot)) + " 结为伴侣",
                    EventImportance.Important, a.PositionOf(slot), slot, a.PartnerOf(slot), "相处、成年与婚姻条件满足");
            p.Partner = partner;
            // 成员身份由居住地决定；商人和士兵经过他国领土不会自动换阵营。
            int settlement = TerritoryAt(a.HomeXOf(slot), a.HomeYOf(slot));
            if (p.Settlement != settlement)
            {
                p.Settlement = settlement;
                _sim.Events.Record(tick, WorldEventType.AgentMigrated, p.Name + " 加入聚落 " + settlement,
                    EventImportance.Normal, a.PositionOf(slot), slot, -1, "当地共享设施与实际居住位置");
            }
            JobType job = a.JobOf(slot);
            if (a.LifeStageOf(slot) == LifeStage.Adult && settlement != 0)
            {
                float foodPrice = _sim.Civilizations.Price(settlement, ResourceKind.Food);
                // 市场粮价和现有土地共同驱动分工，不凭空授予收入。
                if (foodPrice > _sim.Config.Trade.BaseFoodPrice && _sim.Buildings.CountOf(BuildingKind.Farm) > 0
                    && a.PersonalityOf(slot).Industriousness >= 0.4f)
                    job = JobType.Farmer;
                else if (a.ActionOf(slot) == ActionKind.Hunt) { job = JobType.Hunter; }
                else if (a.ActionOf(slot) == ActionKind.GatherIron) { job = JobType.Miner; }
                else if (a.ActionOf(slot) == ActionKind.Trade) { job = JobType.Trader; }
                else if (a.ActionOf(slot) == ActionKind.BuildHouse || a.ActionOf(slot) == ActionKind.BuildStorage
                    || a.ActionOf(slot) == ActionKind.BuildFarm || a.ActionOf(slot) == ActionKind.BuildMine) { job = JobType.Builder; }
                else if (a.ActionOf(slot) == ActionKind.GatherWood || a.ActionOf(slot) == ActionKind.GatherFood) { job = JobType.Gatherer; }
                if (job != a.JobOf(slot))
                {
                    a.SetJob(slot, job);
                    _sim.Events.Record(tick, WorldEventType.AgentChangedJob, p.Name + " 从事 " + job,
                        EventImportance.Normal, a.PositionOf(slot), slot, -1, "本地需求与正在执行的工作");
                }
            }
            p.Job = (int)job;
            if (a.HasTarget(slot))
            {
                var target = a.TargetOf(slot);
                var tile = _sim.World.TileAtClamped(target.X, target.Y);
                if (tile.BuildingId > 0) { p.Workplace = tile.BuildingId - 1; }
            }
        }
        for (int i = 0; i < _sim.Settlements.EntityCount; i++)
        {
            var s = _sim.Settlements.At(i); if (s.Dissolved) { continue; }
            Person? best = null; float bestScore = -1;
            foreach (Person p in _people)
            {
                if (!p.Alive || p.Settlement != s.Id || _sim.Agents.LifeStageOf(p.Slot) != LifeStage.Adult) { continue; }
                var traits = _sim.Agents.PersonalityOf(p.Slot);
                float score = traits.Sociability + traits.Kindness + traits.Bravery + traits.Industriousness;
                float support = 0; int neighbors = 0;
                foreach (Person peer in _people)
                    if (peer.Alive && peer.Settlement == s.Id && peer.Id != p.Id)
                    { support += _sim.Relationships.AffinityOf(p.Slot, peer.Slot); neighbors++; }
                if (neighbors > 0) { score += support / neighbors; }
                if (score > bestScore) { best = p; bestScore = score; }
            }
            long selected = best?.Id ?? 0;
            Leadership? leader = _leaders.Find(l => l.Settlement == s.Id);
            if (leader == null) { leader = new Leadership { Settlement = s.Id }; _leaders.Add(leader); }
            if (leader.Person != selected)
            {
                leader.Person = selected;
                if (best != null) { _sim.Events.Record(tick, WorldEventType.LeaderElected, best.Name + " 成为聚落领袖",
                    EventImportance.Important, new Int2(s.CenterX, s.CenterY), best.Slot, -1, "勇敢、亲善、勤劳、组织能力与居民关系支持"); }
            }
            if (best != null) { _sim.Agents.SetJob(best.Slot, JobType.Leader); best.Job = (int)JobType.Leader; }
        }
    }

    public void Reset() { _people.Clear(); _leaders.Clear(); _history.Clear(); _byId.Clear(); }
    public JsonValue Encode() => JsonValue.Object().Set("people", PersistentData.Encode(_people))
        .Set("leaders", PersistentData.Encode(_leaders)).Set("history", PersistentData.Encode(_history));
    public void Restore(JsonValue value)
    {
        Reset(); PersistentData.Restore(value.Get("people"), _people); PersistentData.Restore(value.Get("leaders"), _leaders);
        PersistentData.Restore(value.Get("history"), _history);
        foreach (Person p in _people) { _byId[p.Id] = p; }
    }
    public ulong HashInto(ulong hash)
    {
        foreach (Person p in _people)
        {
            hash = Hash64.Combine(hash, p.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            hash = Hash64.Combine(hash, p.Settlement); hash = Hash64.Combine(hash, p.Job);
            hash = Hash64.Combine(hash, p.Partner.ToString(System.Globalization.CultureInfo.InvariantCulture));
            hash = Hash64.Combine(hash, p.Alive);
        }
        foreach (Leadership l in _leaders) { hash = Hash64.Combine(hash, l.Settlement); hash = Hash64.Combine(hash, (ulong)l.Person); }
        return hash;
    }
}

public sealed class SocietyConfig
{
    public bool Enabled = true;
    public float TerritoryRadius = 32f;
    public int HistoryCapacity = 20000;
}
