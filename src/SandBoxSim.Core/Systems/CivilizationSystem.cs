using System;
using System.Collections.Generic;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.History;

namespace SandBoxSim.Core.Systems;

/// <summary>实际聚落交换、商队、外交、联盟、文明和冲突。库存与通路驱动结果。</summary>
public sealed class CivilizationSystem : ISimEntitySet
{
    public sealed class Civilization
    {
        public int Id, Tools, Weapons;
        public int[] Settlements = Array.Empty<int>();
        public string Name = "";
        public string Culture = "";
        public float Militarism, Expansionism, TradePreference, TechnologyFocus, Isolationism, Research;
    }
    public sealed class Diplomacy
    {
        public int A, B, PressureDays, PeaceDays, Trades;
        public bool War, Alliance;
        public float Trust, Hostility, Pressure;
    }
    public sealed class Caravan
    {
        public int Id, From, To, X, Y, Resource, Payment;
        public float Cargo, PaymentCargo;
        public long StartedTick;
        public bool Delivered;
        public bool Returning;
        public bool Aborted;
        public bool PaymentInTransit;
        public long Merchant;
    }
    private readonly Simulation _sim;
    private readonly List<Civilization> _civilizations = new();
    private readonly List<Diplomacy> _diplomacy = new();
    private readonly List<Caravan> _caravans = new();
    private int _nextCivilization = 1, _nextCaravan = 1;
    public IReadOnlyList<Civilization> Civilizations => _civilizations;
    public IReadOnlyList<Diplomacy> Relations => _diplomacy;
    public IReadOnlyList<Caravan> Caravans => _caravans;
    public int EntityCount => _civilizations.Count + _caravans.Count + _diplomacy.Count;
    public CivilizationSystem(Simulation sim) { _sim = sim; }
    public bool IsTransporting(int slot)
    {
        if (_sim.Agents.ActionOf(slot) != ActionKind.Trade) { return false; }
        return HasAssignment(slot);
    }
    public bool HasAssignment(int slot)
    {
        long identity = _sim.Society.Identity(slot);
        foreach (var c in _caravans) { if (!c.Delivered && c.Merchant == identity) { return true; } }
        return false;
    }
    public Civilization? OfSettlement(int settlement)
    { foreach (var c in _civilizations) { if (Array.IndexOf(c.Settlements, settlement) >= 0) { return c; } } return null; }
    public SettlementStore.Settlement? Settlement(int id)
    {
        for (int i = 0; i < _sim.Settlements.EntityCount; i++)
        { var s = _sim.Settlements.At(i); if (s.Id == id && !s.Dissolved) { return s; } }
        return null;
    }
    public Diplomacy Relation(int a, int b)
    {
        if (a > b) { (a, b) = (b, a); }
        foreach (var d in _diplomacy) { if (d.A == a && d.B == b) { return d; } }
        var relation = new Diplomacy { A = a, B = b }; _diplomacy.Add(relation); return relation;
    }
    public float Stock(int settlement, ResourceKind kind)
    {
        float sum = 0;
        for (int i = 0; i < _sim.Buildings.Capacity; i++)
            if (IsWarehouse(i, settlement)) { sum += _sim.Storage.AmountOf(i, kind); }
        return sum;
    }
    private bool IsWarehouse(int index, int settlement) => _sim.Buildings.IsAlive(index)
        && _sim.Buildings.StateOf(index) == BuildingState.Complete && _sim.Buildings.KindOf(index) == BuildingKind.Storage
        && _sim.Society.TerritoryAt(_sim.Buildings.XOf(index), _sim.Buildings.YOf(index)) == settlement;
    public float Need(int settlement, ResourceKind kind)
    {
        int population = 0; foreach (var p in _sim.Society.People) { if (p.Alive && p.Settlement == settlement) { population++; } }
        float target = kind switch { ResourceKind.Food => _sim.Config.Trade.FoodPerCapitaNeed,
            ResourceKind.Wood => _sim.Config.Trade.WoodPerCapitaNeed,
            ResourceKind.Stone => _sim.Config.Trade.StonePerCapitaNeed, _ => _sim.Config.Trade.IronPerCapitaNeed };
        return population * target;
    }
    public float Price(int settlement, ResourceKind kind)
    {
        var cfg = _sim.Config.Trade;
        float basic = kind switch { ResourceKind.Food => cfg.BaseFoodPrice, ResourceKind.Wood => cfg.BaseWoodPrice,
            ResourceKind.Stone => cfg.BaseStonePrice, _ => cfg.BaseIronPrice };
        return basic * SimMath.Clamp(MathF.Pow((Need(settlement, kind) + cfg.Epsilon) / (Stock(settlement, kind) + cfg.Epsilon),
            cfg.Elasticity), cfg.MinMultiplier, cfg.MaxMultiplier);
    }
    private float Withdraw(int settlement, ResourceKind kind, float amount)
    {
        float taken = 0;
        for (int i = 0; i < _sim.Buildings.Capacity && taken < amount; i++)
            if (IsWarehouse(i, settlement)) { taken += _sim.Storage.Withdraw(i, kind, amount - taken); }
        return taken;
    }
    private float Deliver(int settlement, ResourceKind kind, float amount, int fallbackX, int fallbackY)
    {
        for (int i = 0; i < _sim.Buildings.Capacity && amount > 0; i++)
            if (IsWarehouse(i, settlement)) { amount -= _sim.Storage.Deposit(i, kind, amount); }
        if (amount > 0)
        {
            amount -= _sim.GroundStocks.Deposit(fallbackX, fallbackY, kind, amount, _sim.Config.GroundStocks);
        }
        return amount;
    }
    public bool TryDispatch(int from, int to)
    {
        var source = Settlement(from); var destination = Settlement(to);
        if (from == to || source == null || destination == null || Relation(from, to).War) { return false; }
        foreach (var c in _caravans) { if (!c.Delivered && (c.From == from && c.To == to || c.From == to && c.To == from)) { return false; } }
        var path = _sim.Pathfinder.FindNextStep(source.Value.CenterX, source.Value.CenterY,
            destination.Value.CenterX, destination.Value.CenterY, out Int2 _);
        if (!path.Success) { return false; }
        ResourceKind goods = ResourceKind.None, payment = ResourceKind.None;
        float gain = 0;
        for (int k = 1; k <= 4; k++)
        {
            var kind = (ResourceKind)k;
            float surplus = Stock(from, kind) - Need(from, kind), shortage = Need(to, kind) - Stock(to, kind);
            if (surplus <= 0 || shortage <= 0) { continue; }
            float score = Math.Min(surplus, shortage) * (Price(to, kind) - Price(from, kind));
            if (score > gain) { gain = score; goods = kind; }
        }
        if (goods == ResourceKind.None) { return false; }
        float bestPayment = 0;
        for (int k = 1; k <= 4; k++)
        {
            var kind = (ResourceKind)k; if (kind == goods) { continue; }
            float excess = Stock(to, kind) - Need(to, kind);
            if (excess > bestPayment) { bestPayment = excess; payment = kind; }
        }
        if (payment == ResourceKind.None) { return false; }
        SocietySystem.Person? merchant = null;
        foreach (var person in _sim.Society.MembersOf(from))
            if (_sim.Agents.LifeStageOf(person.Slot) == LifeStage.Adult && !HasAssignment(person.Slot)
                && person.Id != _sim.Society.LeaderOf(from)) { merchant = person; break; }
        if (merchant == null) { return false; }
        float rate = Price(from, goods) / Math.Max(0.01f, Price(to, payment));
        var traderCulture = OfSettlement(from);
        float capacity = _sim.Config.Civilization.CaravanCapacity * (0.5f + (traderCulture?.TradePreference ?? 0.5f) * 0.5f)
            * (1 - (traderCulture?.Isolationism ?? 0) * 0.3f);
        float cargo = Math.Min(capacity,
            Math.Min(Stock(from, goods) - Need(from, goods), Math.Min(Need(to, goods) - Stock(to, goods), bestPayment / rate)));
        if (cargo < 0.1f) { return false; }
        float taken = Withdraw(from, goods, cargo), paid = Withdraw(to, payment, taken * rate);
        // 单线程事务，核对实物，不能凭报价制造资源。
        if (paid + 0.001f < taken * rate)
        { Deliver(from, goods, taken, source.Value.CenterX, source.Value.CenterY); Deliver(to, payment, paid, destination.Value.CenterX, destination.Value.CenterY); return false; }
        _caravans.Add(new Caravan { Id = _nextCaravan++, From = from, To = to, Resource = (int)goods, Payment = (int)payment,
            Cargo = taken, PaymentCargo = paid, X = _sim.Agents.XOf(merchant.Slot), Y = _sim.Agents.YOf(merchant.Slot),
            StartedTick = _sim.Clock, Merchant = merchant.Id });
        _sim.Agents.SetJob(merchant.Slot, JobType.Trader);
        _sim.Agents.SetAction(merchant.Slot, ActionKind.Trade, ActionPhase.Moving);
        _sim.Events.Record(_sim.Clock, WorldEventType.TradeRouteEstablished, "聚落 " + from + " 向 " + to + " 发出商队",
            EventImportance.Important, new Int2(source.Value.CenterX, source.Value.CenterY), merchant.Slot, -1, "本地供需差价、真实库存和可达道路");
        return true;
    }
    public void TickFast(long tick)
    {
        if (!_sim.Config.Civilization.Enabled) { return; }
        foreach (var caravan in _caravans)
        {
            if (caravan.Delivered) { continue; }
            var merchant = _sim.Society.Find(caravan.Merchant);
            var target = Settlement(caravan.Returning ? caravan.From : caravan.To); var source = Settlement(caravan.From);
            if (target == null || source == null || merchant == null || !_sim.Agents.IsSlotAlive(merchant.Slot)
                || _sim.Society.Identity(merchant.Slot) != merchant.Id)
            {
                caravan.Cargo -= _sim.GroundStocks.Deposit(caravan.X, caravan.Y, (ResourceKind)caravan.Resource, caravan.Cargo, _sim.Config.GroundStocks);
                if (caravan.PaymentInTransit)
                    caravan.PaymentCargo -= _sim.GroundStocks.Deposit(caravan.X, caravan.Y, (ResourceKind)caravan.Payment, caravan.PaymentCargo, _sim.Config.GroundStocks);
                else
                {
                    var buyer = Settlement(caravan.To);
                    caravan.PaymentCargo = Deliver(caravan.To, (ResourceKind)caravan.Payment, caravan.PaymentCargo,
                        buyer?.CenterX ?? caravan.X, buyer?.CenterY ?? caravan.Y);
                }
                caravan.Aborted = true;
                caravan.Delivered = caravan.Cargo <= 0 && caravan.PaymentCargo <= 0;
                if (caravan.Delivered) { ReleaseMerchant(caravan); }
                continue;
            }
            var agents = _sim.Agents; int slot = merchant.Slot;
            caravan.X = agents.XOf(slot); caravan.Y = agents.YOf(slot);
            if (!caravan.Returning && Relation(caravan.From, caravan.To).War)
            {
                caravan.Aborted = true; caravan.Returning = true; target = source;
                var buyer = Settlement(caravan.To);
                caravan.PaymentCargo = Deliver(caravan.To, (ResourceKind)caravan.Payment, caravan.PaymentCargo,
                    buyer?.CenterX ?? caravan.X, buyer?.CenterY ?? caravan.Y);
            }
            bool needsBreak = agents.HungerOf(slot) > 0.65f || agents.ThirstOf(slot) > 0.65f || agents.FatigueOf(slot) > 0.8f;
            if (needsBreak)
            {
                if (agents.ActionOf(slot) == ActionKind.Trade) { agents.SetAction(slot, ActionKind.None, ActionPhase.Idle); agents.SetNextDecisionTick(slot, 0); }
                continue;
            }
            if ((agents.ActionOf(slot) == ActionKind.Eat || agents.ActionOf(slot) == ActionKind.Drink || agents.ActionOf(slot) == ActionKind.Sleep)
                && (agents.PhaseOf(slot) == ActionPhase.Executing || agents.PhaseOf(slot) == ActionPhase.Moving)) { continue; }
            agents.SetJob(slot, JobType.Trader);
            merchant.Job = (int)JobType.Trader;
            agents.SetState(slot, AgentState.Moving);
            if (agents.ActionOf(slot) != ActionKind.Trade) { agents.SetAction(slot, ActionKind.Trade, ActionPhase.Moving); }
            agents.SetTarget(slot, target.Value.CenterX, target.Value.CenterY);
            if (caravan.X == target.Value.CenterX && caravan.Y == target.Value.CenterY)
            {
                if (!caravan.Returning)
                {
                    caravan.Cargo = Deliver(caravan.To, (ResourceKind)caravan.Resource, caravan.Cargo, caravan.X, caravan.Y);
                    if (caravan.Cargo > 0) { continue; }
                    caravan.Returning = true;
                    caravan.PaymentInTransit = true;
                    continue;
                }
                if (caravan.Aborted)
                {
                    caravan.Cargo = Deliver(caravan.From, (ResourceKind)caravan.Resource, caravan.Cargo, caravan.X, caravan.Y);
                    var buyer = Settlement(caravan.To);
                    caravan.PaymentCargo = Deliver(caravan.To, (ResourceKind)caravan.Payment, caravan.PaymentCargo,
                        buyer?.CenterX ?? caravan.X, buyer?.CenterY ?? caravan.Y);
                    caravan.Delivered = caravan.Cargo <= 0 && caravan.PaymentCargo <= 0;
                    if (caravan.Delivered) { ReleaseMerchant(caravan); }
                    continue;
                }
                caravan.PaymentCargo = Deliver(caravan.From, (ResourceKind)caravan.Payment, caravan.PaymentCargo, caravan.X, caravan.Y);
                if (caravan.PaymentCargo > 0) { continue; }
                var relation = Relation(caravan.From, caravan.To); relation.Trades++; relation.Trust = Math.Min(1, relation.Trust + 0.15f);
                caravan.Delivered = true;
                ReleaseMerchant(caravan);
                _sim.Events.Record(tick, WorldEventType.TradeRouteEstablished, "商队 " + caravan.Id + " 完成交付",
                    EventImportance.Important, new Int2(caravan.X, caravan.Y), slot, -1, "实物交换提高信任，缓解当地稀缺");
                continue;
            }
            var route = _sim.Pathfinder.FindNextStep(caravan.X, caravan.Y, target.Value.CenterX, target.Value.CenterY, out Int2 step);
            if (route.Success) { caravan.X = step.X; caravan.Y = step.Y; agents.SetPosition(slot, step.X, step.Y); }
        }
    }
    private void ReleaseMerchant(Caravan caravan)
    {
        var person = _sim.Society.Find(caravan.Merchant);
        if (person == null || !_sim.Agents.IsSlotAlive(person.Slot) || _sim.Society.Identity(person.Slot) != person.Id) { return; }
        _sim.Agents.SetJob(person.Slot, JobType.Gatherer);
        _sim.Agents.SetAction(person.Slot, ActionKind.None, ActionPhase.Idle);
        _sim.Agents.ClearTarget(person.Slot);
    }
    public void TickDay(long tick)
    {
        if (!_sim.Config.Civilization.Enabled) { return; }
        // 交付历史已进入事件流；只保留最近的商队显示记录，避免长期世界持续膨胀。
        if (_caravans.Count > 512)
        {
            int excess = _caravans.Count - 512;
            for (int i = 0; i < _caravans.Count && excess > 0;)
                if (_caravans[i].Delivered) { _caravans.RemoveAt(i); excess--; } else { i++; }
        }
        for (int i = 0; i < _sim.Settlements.EntityCount; i++)
        {
            var s = _sim.Settlements.At(i); if (s.Dissolved || s.Tier < SettlementTier.Village || OfSettlement(s.Id) != null) { continue; }
            _civilizations.Add(new Civilization { Id = _nextCivilization++, Name = _sim.Society.SettlementName(s.Id) + "文明", Settlements = new[] { s.Id } });
            _sim.Events.Record(tick, WorldEventType.CivilizationFounded, _civilizations[^1].Name + " 形成",
                EventImportance.Important, new Int2(s.CenterX, s.CenterY), cause: "聚落人口和设施达到村庄规模");
        }
        foreach (var civilization in _civilizations) { UpdateTraitsAndCrafting(civilization); }
        for (int i = 0; i < _sim.Settlements.EntityCount; i++)
        {
            var a = _sim.Settlements.At(i); if (a.Dissolved) { continue; }
            for (int j = i + 1; j < _sim.Settlements.EntityCount; j++)
            {
                var b = _sim.Settlements.At(j); if (b.Dissolved) { continue; }
                var relation = Relation(a.Id, b.Id);
                foreach (var left in _sim.Society.MembersOf(a.Id))
                    foreach (var right in _sim.Society.MembersOf(b.Id))
                        relation.Hostility = Math.Max(relation.Hostility,
                            Math.Max(0, -_sim.Relationships.AffinityOf(left.Slot, right.Slot)));
                float scarcity = Math.Max(1 - SimMath.Clamp01(Stock(a.Id, ResourceKind.Food) / Math.Max(1, Need(a.Id, ResourceKind.Food))),
                    1 - SimMath.Clamp01(Stock(b.Id, ResourceKind.Food) / Math.Max(1, Need(b.Id, ResourceKind.Food))));
                float distance = (float)Int2.Distance(new Int2(a.CenterX, a.CenterY), new Int2(b.CenterX, b.CenterY));
                float overlap = 1 - SimMath.Clamp01(distance / (_sim.Config.Society.TerritoryRadius * 2));
                float aggression = ((OfSettlement(a.Id)?.Militarism ?? 0) + (OfSettlement(b.Id)?.Militarism ?? 0)) * 0.5f;
                relation.Pressure = SimMath.Clamp01(scarcity * 0.35f + overlap * 0.2f + aggression * 0.2f
                    + relation.Hostility * 0.25f - relation.Trust * 0.35f - Math.Min(0.3f, relation.Trades * 0.03f));
                bool peaceful = _sim.Config.Rules.PeaceMode || _sim.Config.Rules.DisableWar || relation.Alliance || OfSettlement(a.Id) == OfSettlement(b.Id)
                    && OfSettlement(a.Id) != null;
                if (!relation.War)
                {
                    relation.PressureDays = !peaceful && relation.Pressure >= _sim.Config.Civilization.WarThreshold ? relation.PressureDays + 1 : 0;
                    if (relation.PressureDays >= _sim.Config.Civilization.WarPressureDays)
                    {
                        relation.War = true;
                        _sim.Events.Record(tick, WorldEventType.WarDeclared, "聚落 " + a.Id + " 与 " + b.Id + " 交战",
                            EventImportance.Critical, new Int2(a.CenterX, a.CenterY), -1, -1,
                            "资源 " + scarcity * 0.35f + "，领土 " + overlap * 0.2f + "，好斗 " + aggression * 0.2f
                            + "，敌意 " + relation.Hostility * 0.25f + "，贸易/信任减压 " + (relation.Trust * 0.35f + Math.Min(0.3f, relation.Trades * 0.03f))
                            + "；总压力 " + relation.Pressure);
                    }
                }
                else
                {
                    relation.PeaceDays = peaceful || relation.Pressure < _sim.Config.Civilization.PeaceThreshold ? relation.PeaceDays + 1 : 0;
                    if (peaceful || relation.PeaceDays >= 3)
                    {
                        relation.War = false; relation.PressureDays = 0;
                        _sim.Events.Record(tick, WorldEventType.WarEnded, "聚落 " + a.Id + " 与 " + b.Id + " 停战",
                            EventImportance.Important, new Int2(a.CenterX, a.CenterY), -1, -1, "贸易、资源恢复或和平规则降低压力");
                    }
                    else { Mobilize(relation); }
                }
                if (!relation.War)
                {
                    TryDispatch(a.Id, b.Id); TryDispatch(b.Id, a.Id);
                    if (!relation.Alliance && relation.Trades >= _sim.Config.Civilization.AllianceTrades && relation.Trust >= 0.6f)
                    {
                        relation.Alliance = true;
                        _sim.Events.Record(tick, WorldEventType.AllianceFormed, "聚落 " + a.Id + " 与 " + b.Id + " 结盟",
                            EventImportance.Important, new Int2(a.CenterX, a.CenterY), cause: "多次实际交换与持续信任");
                        var ca = OfSettlement(a.Id); var cb = OfSettlement(b.Id);
                        if (ca != null && cb != null && ca != cb)
                        {
                            var members = new List<int>(ca.Settlements); members.AddRange(cb.Settlements); members.Sort(); ca.Settlements = members.ToArray();
                            cb.Settlements = Array.Empty<int>();
                        }
                    }
                }
                relation.Hostility = Math.Max(0, relation.Hostility - 0.01f);
            }
        }
    }
    private void Mobilize(Diplomacy relation)
    {
        SocietySystem.Person? a = null, b = null;
        foreach (var p in _sim.Society.People)
        {
            if (!p.Alive || _sim.Agents.LifeStageOf(p.Slot) != LifeStage.Adult) { continue; }
            if (p.Settlement == relation.A && a == null) { a = p; }
            if (p.Settlement == relation.B && b == null) { b = p; }
        }
        if (a == null || b == null) { return; }
        _sim.Relationships.Interact(a.Slot, b.Slot, -0.15f, _sim.Clock);
        _sim.Agents.SetJob(a.Slot, JobType.Soldier); _sim.Agents.SetJob(b.Slot, JobType.Soldier);
        _sim.Agents.SetAction(a.Slot, ActionKind.Attack, ActionPhase.Moving);
        _sim.Agents.SetTarget(a.Slot, _sim.Agents.XOf(b.Slot), _sim.Agents.YOf(b.Slot));
        // 军事行动有实际补给损耗，不能仅写一次战争事件。
        Withdraw(relation.A, ResourceKind.Food, 0.5f);
        Withdraw(relation.B, ResourceKind.Food, 0.5f);
        relation.Hostility = Math.Min(1, relation.Hostility + 0.03f);
    }
    private void UpdateTraitsAndCrafting(Civilization c)
    {
        float aggression = 0, bravery = 0, kind = 0, work = 0, social = 0; int count = 0;
        foreach (var p in _sim.Society.People)
        {
            if (!p.Alive || Array.IndexOf(c.Settlements, p.Settlement) < 0) { continue; }
            var t = _sim.Agents.PersonalityOf(p.Slot); aggression += t.Aggression; bravery += t.Bravery;
            kind += t.Kindness; work += t.Industriousness; social += t.Sociability; count++;
        }
        if (count == 0) { return; }
        c.Militarism = aggression / count; c.Expansionism = bravery / count; c.TradePreference = kind / count;
        c.TechnologyFocus = work / count; c.Isolationism = 1 - social / count;
        c.Culture = c.Isolationism > 0.6f ? "内聚" : c.Militarism > c.TradePreference ? "尚武" : "互助";
        if (c.Settlements.Length == 0) { return; }
        int settlement = c.Settlements[0];
        // 研究由当地可工作的工匠产生；疾病、缺粮和人口规模实际影响进度。
        float craftLabor = 0; int craftsmen = 0;
        int desiredCraftsmen = Math.Max(1, count / 8);
        if (Stock(settlement, ResourceKind.Iron) >= 2 && Stock(settlement, ResourceKind.Wood) >= 1)
            foreach (var p in _sim.Society.MembersOf(settlement))
            {
                int slot = p.Slot;
                if (craftsmen >= desiredCraftsmen) { break; }
                if (_sim.Agents.LifeStageOf(slot) != LifeStage.Adult || HasAssignment(slot)
                    || _sim.Agents.HungerOf(slot) > 0.7f || _sim.Agents.ThirstOf(slot) > 0.7f) { continue; }
                if (p.Id == _sim.Society.LeaderOf(settlement)) { continue; }
                _sim.Agents.SetJob(slot, JobType.Craftsman); p.Job = (int)JobType.Craftsman;
                craftLabor += _sim.Agents.PersonalityOf(slot).Industriousness * _sim.Diseases.LaborMultiplier(slot);
                craftsmen++;
            }
        float progress = craftLabor * _sim.Config.Civilization.ResearchPerDay;
        bool war = _diplomacy.Exists(d => d.War && (Array.IndexOf(c.Settlements, d.A) >= 0 || Array.IndexOf(c.Settlements, d.B) >= 0));
        if (war && c.Weapons < _sim.Config.Civilization.MaxTechnologyLevel
            && Stock(settlement, ResourceKind.Iron) >= 3 && Stock(settlement, ResourceKind.Wood) >= 2)
        {
            c.Research += progress;
            if (c.Research >= 1)
            {
                Withdraw(settlement, ResourceKind.Iron, 3); Withdraw(settlement, ResourceKind.Wood, 2);
                c.Research -= 1; c.Weapons++;
                _sim.Events.Record(_sim.Clock, WorldEventType.Innovation, c.Name + " 制成武器 " + c.Weapons,
                    EventImportance.Important, cause: "战争需求、研究与实际铁木消耗，提高战斗伤害");
            }
            return;
        }
        if (c.Tools >= _sim.Config.Civilization.MaxTechnologyLevel) { return; }
        if (Stock(settlement, ResourceKind.Iron) >= 2 && Stock(settlement, ResourceKind.Wood) >= 1)
        {
            c.Research += progress;
            if (c.Research >= 1)
            {
                Withdraw(settlement, ResourceKind.Iron, 2); Withdraw(settlement, ResourceKind.Wood, 1);
                c.Research -= 1; c.Tools++;
                _sim.Events.Record(_sim.Clock, WorldEventType.Innovation, c.Name + " 制成铁制工具 " + c.Tools,
                    EventImportance.Important, cause: "工匠研究和实际消耗铁木，提高生产率");
            }
        }
    }
    public float ProductionMultiplier(int x, int y) => 1 + (OfSettlement(_sim.Society.TerritoryAt(x, y))?.Tools ?? 0) * 0.15f;
    public float CombatMultiplier(int x, int y) => 1 + (OfSettlement(_sim.Society.TerritoryAt(x, y))?.Weapons ?? 0) * 0.2f;
    public void Reset() { _civilizations.Clear(); _diplomacy.Clear(); _caravans.Clear(); _nextCivilization = _nextCaravan = 1; }
    public JsonValue Encode() => JsonValue.Object().Set("civilizations", PersistentData.Encode(_civilizations))
        .Set("relations", PersistentData.Encode(_diplomacy)).Set("caravans", PersistentData.Encode(_caravans))
        .Set("nextCivilization", JsonValue.From(_nextCivilization)).Set("nextCaravan", JsonValue.From(_nextCaravan));
    public void Restore(JsonValue root)
    {
        Reset(); PersistentData.Restore(root.Get("civilizations"), _civilizations); PersistentData.Restore(root.Get("relations"), _diplomacy);
        PersistentData.Restore(root.Get("caravans"), _caravans); _nextCivilization = root.GetInt("nextCivilization", 1); _nextCaravan = root.GetInt("nextCaravan", 1);
    }
    public ulong HashInto(ulong hash) => Hash64.Combine(hash, Encode().ToJson(false));
}
public sealed class CivilizationConfig
{
    public bool Enabled = true;
    public float CaravanCapacity = 30f, WarThreshold = 0.65f, PeaceThreshold = 0.25f, ResearchPerDay = 0.05f;
    public int WarPressureDays = 5, AllianceTrades = 5, MaxTechnologyLevel = 3;
}
