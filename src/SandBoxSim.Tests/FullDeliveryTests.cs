using System.Linq;
using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Save;
using SandBoxSim.Core.Systems;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

public sealed class FullDeliveryTests
{
    [Fact("决策缓存必须与直接计算选择相同目标，逐 tick 保持相同世界状态")]
    public void DecisionCachePreservesSimulation()
    {
        var cached = SandboxScenarios.Create(0, 4242, 36); var direct = SandboxScenarios.Create(0, 4242, 36);
        direct.Ai.UseWorldCache = false;
        for (int i = 0; i < 60; i++)
        {
            cached.Tick(120); direct.Tick(120);
            Assert.Equal(StateHash.ComputeDigest(direct), StateHash.ComputeDigest(cached));
        }
    }
    [Fact("四种沙箱初始环境只设置地形和生命，社会结果必须由居民产生")]
    public void ScenariosProvideConditionsNotOutcomes()
    {
        for (int scenario = 0; scenario < SandboxScenarios.Names.Length; scenario++)
        {
            var sim = SandboxScenarios.Create(scenario, 4242, 40);
            Assert.Equal(0, sim.Buildings.TotalCompleted); Assert.Equal(0, sim.Settlements.ActiveCount);
            Assert.True(sim.Agents.LiveCount > 0); Assert.True(sim.World.CountTerrain()[(int)TerrainKind.Water] > 0);
            if (scenario == 3) { Assert.True(sim.Predators.Wolves.Count > 0); }
            if (scenario == 1)
            {
                Assert.True(sim.Agents.AliveSlots().Any(i => sim.Agents.XOf(i) < 47));
                Assert.True(sim.Agents.AliveSlots().Any(i => sim.Agents.XOf(i) > 55));
            }
            var restored = Simulation.CreateForRestore(sim.Config.Clone(), 100, 100, 1);
            Assert.True(SaveLoader.Load(restored, SaveFile.Encode(sim)).DigestMatches);
        }
    }
    [Fact("死亡立即释放床位、解开伴侣并只统计一次；槽位复用保留旧人物历史")]
    public void DeathIsRecordedOnceAndPreservesIdentity()
    {
        var sim = World(); sim.InterveneSpawnHumans(20, 20, 2, 0);
        int[] people = sim.Agents.AliveSlots().ToArray(); int victim = people[0], partner = people[1];
        long oldId = sim.Society.Identity(victim);
        int house = sim.Buildings.Place(sim.World, BuildingKind.House, 20, 20, 1);
        sim.Buildings.MarkComplete(house, 0); Assert.True(sim.Buildings.TryOccupyBedOf(house));
        sim.Agents.SetDwelling(victim, house); sim.Agents.SetPartner(victim, partner); sim.Agents.SetPartner(partner, victim);
        sim.Needs.Kill(sim.Agents, victim, DeathCause.Combat, sim.Clock);
        Assert.Equal(1, sim.Stats.TotalDeaths); Assert.Equal(0, sim.Buildings.OccupiedBeds);
        Assert.Equal(-1, sim.Agents.PartnerOf(partner));
        Assert.False(sim.Society.Find(oldId)!.Alive); Assert.Equal(sim.Clock, sim.Society.Find(oldId)!.DiedTick);
        sim.InterveneSpawnHumans(20, 20, 1, 0);
        Assert.True(sim.Agents.IsSlotAlive(victim)); Assert.True(sim.Society.Identity(victim) != oldId);
        Assert.True(sim.Society.OfSlot(victim)!.Alive);
        Assert.Equal(1, sim.Society.TimelineOf(oldId).Count(e => e.Type == (int)SandBoxSim.Core.History.WorldEventType.AgentDied));
        sim.Config.Rules.NoDeath = true; sim.Tick(1440);
        Assert.Equal(1, sim.Stats.TotalDeaths);
    }

    [Fact("水距缓存精确匹配逐格搜索，并响应河流改道")]
    public void WaterDistanceCacheTracksTerrain()
    {
        var sim = new Simulation(new SimConfig(), 17, 13, 123);
        for (int pass = 0; pass < 2; pass++)
        {
            for (int y = 0; y < 13; y++) for (int x = 0; x < 17; x++)
            {
                int expected = 9;
                for (int wy = 0; wy < 13; wy++) for (int wx = 0; wx < 17; wx++)
                    if (sim.World.TileAt(wx, wy).Terrain == TerrainKind.Water)
                        expected = System.Math.Min(expected, System.Math.Max(System.Math.Abs(x - wx), System.Math.Abs(y - wy)));
                Assert.Equal(expected, sim.World.DistanceToWater(x, y, 8));
            }
            sim.World.SetTerrain(8, 6, TerrainKind.Water); sim.World.SetTerrain(0, 0, TerrainKind.Grass);
        }
    }

    [Fact("食草动物的觅食消耗植被，饮水和睡眠缓解真实需求")]
    public void AnimalsConsumeVegetationAndMeetNeeds()
    {
        var sim = World(); sim.Wildlife.ClearAllKeepCapacity();
        for (int y = 0; y < 100; y++) for (int x = 0; x < 100; x++)
        { sim.World.SetVegetation(x, y, 1); sim.World.SetMoisture(x, y, 1); }
        int deer = sim.Wildlife.Add(sim.World, 50, 50, 0, sim.Random.Get(RngStream.Events));
        sim.Wildlife.SetEnergy(deer, 0.2f);
        float before = sim.World.Tiles.Sum(t => t.Vegetation);
        sim.WildlifeSystem.Tick(1);
        Assert.True(sim.World.Tiles.Sum(t => t.Vegetation) < before);
        sim.Wildlife.SetEnergy(deer, 0.9f);
        sim.Wildlife.SetBehavior(deer, 0.8f, 0, ActionKind.None);
        sim.WildlifeSystem.Tick(2);
        Assert.Equal(ActionKind.Drink, sim.Wildlife.ActionOf(deer));
        Assert.True(sim.Wildlife.ThirstOf(deer) < 0.8f);
        sim.Wildlife.SetBehavior(deer, 0, 0.8f, ActionKind.None);
        sim.WildlifeSystem.Tick(3);
        Assert.Equal(ActionKind.Sleep, sim.Wildlife.ActionOf(deer));
        Assert.True(sim.Wildlife.FatigueOf(deer) < 0.8f);
    }

    [Fact("陨石真正摧毁住房，释放居所和床位")]
    public void MeteorDestroysHousing()
    {
        var sim = World(); sim.InterveneSpawnHumans(20, 20, 1, 0);
        int house = sim.Buildings.Place(sim.World, BuildingKind.House, 20, 20, 1);
        Assert.True(house >= 0); sim.Buildings.MarkComplete(house, 0);
        int person = sim.Agents.AliveSlots().First();
        Assert.True(sim.Buildings.TryOccupyBedOf(house)); sim.Agents.SetDwelling(person, house);
        PlayerTools.Apply(sim, PlayerTool.Meteor, 20, 20, 0, 1);
        Assert.False(sim.Buildings.IsAlive(house)); Assert.Equal(-1, sim.Agents.DwellingOf(person));
        Assert.Equal(0, sim.Buildings.TotalBeds); Assert.Equal(0, sim.Buildings.OccupiedBeds);
    }

    [Fact("战争中断商路时返还实物，不能记为成功贸易")]
    public void InterruptedTradeRefundsActualCargo()
    {
        var sim = Communities(out int a, out int b);
        sim.Storage.Deposit(a, ResourceKind.Food, 200); sim.Storage.Deposit(b, ResourceKind.Wood, 300);
        float food = Total(sim, ResourceKind.Food), wood = Total(sim, ResourceKind.Wood);
        Assert.True(sim.Civilizations.TryDispatch(1, 2));
        sim.Civilizations.TickFast(10); sim.Civilizations.Relation(1, 2).War = true;
        for (int i = 2; i < 180; i++) { sim.Civilizations.TickFast(i * 10); }
        Assert.True(sim.Civilizations.Caravans[0].Aborted && sim.Civilizations.Caravans[0].Delivered);
        Assert.Equal(0, sim.Civilizations.Relation(1, 2).Trades);
        Assert.Near(food, Total(sim, ResourceKind.Food), 0.001); Assert.Near(wood, Total(sim, ResourceKind.Wood), 0.001);
    }

    [Fact("地形通行与建造覆盖值必须保存，不能按默认地形覆盖")]
    public void TerrainOverridesSurviveSave()
    {
        var sim = World();
        sim.World.SetWalkable(3, 4, false); sim.World.SetBuildable(4, 5, false);
        var loaded = Simulation.CreateForRestore(new SimConfig(), 100, 100, 7111);
        Assert.True(SaveLoader.Load(loaded, SaveFile.Encode(sim)).DigestMatches);
        Assert.False(loaded.World.TileAt(3, 4).Walkable);
        Assert.False(loaded.World.TileAt(4, 5).Buildable);
    }

    [Fact("寻路缓存遇到天气、火灾和地形修改后仍与未缓存模拟一致")]
    public void PathMemoizationPreservesSimulation()
    {
        var cached = new Simulation(new SimConfig(), 44, 44, 8010);
        var direct = new Simulation(new SimConfig(), 44, 44, 8010);
        direct.Pathfinder.UseMemoization = false;
        foreach (var sim in new[] { cached, direct })
        {
            sim.InterveneSpawnHumans(22, 22, 20, 6);
            sim.World.SetTerrain(20, 20, TerrainKind.Forest);
            sim.Fire.Ignite(20, 20, 0, "缓存验收");
        }
        for (int block = 0; block < 48; block++)
        {
            cached.Tick(60); direct.Tick(60);
            Assert.Equal(direct.StateDigest(), cached.StateDigest());
        }
        cached.World.SetWalkable(22, 21, false);
        direct.World.SetWalkable(22, 21, false);
        cached.Tick(60); direct.Tick(60);
        Assert.Equal(direct.StateDigest(), cached.StateDigest());
    }

    private static Simulation World()
    {
        var sim = new Simulation(new SimConfig(), 100, 100, 7111);
        for (int y = 0; y < 100; y++)
            for (int x = 0; x < 100; x++) { sim.World.SetTerrain(x, y, TerrainKind.Grass); }
        sim.World.RefreshSpatialIndex(); return sim;
    }
    private static int Warehouse(Simulation sim, int x, int y)
    {
        int slot = sim.Buildings.Place(sim.World, BuildingKind.Storage, x, y, 1);
        Assert.True(slot >= 0); sim.Buildings.MarkComplete(slot, 0);
        sim.Storage.SetCapacity(slot, 600); return slot;
    }
    private static Simulation Communities(out int first, out int second)
    {
        var sim = World(); sim.InterveneSpawnHumans(10, 50, 8, 0); sim.InterveneSpawnHumans(80, 50, 8, 0);
        sim.Settlements.Restore(1, 10, 50, SettlementTier.Village, 8, 2, 1, 0, 0, false);
        sim.Settlements.Restore(2, 80, 50, SettlementTier.Village, 8, 2, 1, 0, 0, false);
        first = Warehouse(sim, 10, 51); second = Warehouse(sim, 80, 51);
        sim.Society.TickDay(0); sim.Civilizations.TickDay(0); return sim;
    }
    private static float Total(Simulation sim, ResourceKind kind)
    {
        float sum = sim.Storage.GrandTotalOf(kind, sim.Buildings.Capacity) + sim.GroundStocks.TotalOf(kind);
        foreach (var c in sim.Civilizations.Caravans) { if (!c.Delivered) { if (c.Resource == (int)kind) { sum += c.Cargo; } if (c.Payment == (int)kind) { sum += c.PaymentCargo; } } }
        return sum;
    }
    [Fact("本地供需必须产生不同价格，真实贸易不创造或丢失货物")]
    public void TradeMovesGoodsAndConservesCargo()
    {
        var sim = Communities(out int a, out int b);
        sim.Storage.Deposit(a, ResourceKind.Food, 200);
        sim.Storage.Deposit(b, ResourceKind.Wood, 300);
        Assert.True(sim.Civilizations.Price(2, ResourceKind.Food) > sim.Civilizations.Price(1, ResourceKind.Food));
        float food = Total(sim, ResourceKind.Food), wood = Total(sim, ResourceKind.Wood);
        Assert.True(sim.Civilizations.TryDispatch(1, 2));
        var caravan = sim.Civilizations.Caravans[0]; Assert.Equal(10, caravan.X);
        sim.Civilizations.TickFast(10); Assert.True(caravan.X > 10);
        Assert.Near(food, Total(sim, ResourceKind.Food), 0.001);
        Assert.Near(wood, Total(sim, ResourceKind.Wood), 0.001);
        for (int i = 2; i < 180; i++) { sim.Civilizations.TickFast(i * 10); }
        Assert.True(caravan.Delivered, "商队应完成去程和回程");
        Assert.True(sim.Storage.AmountOf(b, ResourceKind.Food) > 0);
        Assert.True(sim.Storage.AmountOf(a, ResourceKind.Wood) > 0);
        Assert.Near(food, Total(sim, ResourceKind.Food), 0.001);
        Assert.Near(wood, Total(sim, ResourceKind.Wood), 0.001);
        Assert.Equal(1, sim.Civilizations.Relation(1, 2).Trades);
    }
    [Fact("隔绝水域阻止商队成立，不能只增加交易统计")]
    public void BarrierPreventsTrade()
    {
        var sim = Communities(out int a, out int b);
        sim.Storage.Deposit(a, ResourceKind.Food, 200); sim.Storage.Deposit(b, ResourceKind.Wood, 300);
        for (int y = 0; y < 100; y++) { sim.World.SetTerrain(45, y, TerrainKind.Water); }
        sim.World.RefreshSpatialIndex();
        Assert.False(sim.Civilizations.TryDispatch(1, 2));
        Assert.Equal(0, sim.Civilizations.Caravans.Count);
    }
    [Fact("持续压力形成战争，和平规则立即结束战争")]
    public void WarAndPeaceChangeActualState()
    {
        var sim = Communities(out int _, out int _);
        sim.Config.Civilization.WarThreshold = 0.3f;
        for (int i = 0; i < sim.Config.Civilization.WarPressureDays; i++) { sim.Civilizations.TickDay(i * 1440); }
        Assert.True(sim.Civilizations.Relation(1, 2).War);
        sim.Civilizations.TickDay(10 * 1440);
        Assert.True(sim.Agents.AliveSlots().Any(s => sim.Agents.JobOf(s) == JobType.Soldier));
        sim.Config.Rules.PeaceMode = true; sim.Civilizations.TickDay(11 * 1440);
        Assert.False(sim.Civilizations.Relation(1, 2).War);
    }
    [Fact("技术必须消耗铁木并真正提高生产率")]
    public void TechnologyConsumesMaterials()
    {
        var sim = Communities(out int a, out int _);
        sim.Storage.Deposit(a, ResourceKind.Iron, 20); sim.Storage.Deposit(a, ResourceKind.Wood, 20);
        sim.Config.Civilization.ResearchPerDay = 10;
        float before = sim.Civilizations.ProductionMultiplier(10, 50);
        sim.Civilizations.TickDay(1440);
        Assert.True(sim.Civilizations.OfSettlement(1)!.Tools > 0);
        Assert.True(sim.Civilizations.ProductionMultiplier(10, 50) > before);
        Assert.True(sim.Storage.AmountOf(a, ResourceKind.Iron) < 20);
        Assert.True(sim.Storage.AmountOf(a, ResourceKind.Wood) < 20);
    }
    [Fact("疾病只传播到接触范围，治愈给予免疫，影响实际劳动")]
    public void DiseaseUsesContactAndImmunity()
    {
        var sim = World(); sim.InterveneSpawnHumans(10, 10, 2, 0); sim.InterveneSpawnHumans(80, 80, 1, 0);
        int[] slots = sim.Agents.AliveSlots().ToArray();
        sim.Config.Disease.IncubationDays = 0; sim.Config.Disease.TransmissionPerContact = 1;
        Assert.True(sim.Diseases.Infect(slots[0])); sim.Diseases.TickDay(1440);
        Assert.True(sim.Diseases.OfSlot(slots[1])?.Active == true);
        Assert.True(sim.Diseases.OfSlot(slots[2]) == null);
        Assert.True(sim.Diseases.LaborMultiplier(slots[1]) < 1);
        sim.Diseases.Heal(slots[1]); Assert.False(sim.Diseases.Infect(slots[1]));
        Assert.Near(1, sim.Diseases.LaborMultiplier(slots[1]));
    }
    [Fact("瘟疫干预必须感染人物，而非直接改死亡计数")]
    public void PlagueToolUsesDisease()
    {
        var sim = World(); sim.InterveneSpawnHumans(10, 10, 2, 0);
        PlayerTools.Apply(sim, PlayerTool.Plague, 10, 10, 2, 10);
        Assert.Equal(2, sim.Diseases.Cases.Count(c => c.Active));
        Assert.Equal(0, sim.Stats.TotalDeaths);
        PlayerTools.Apply(sim, PlayerTool.Heal, 10, 10, 2, 10);
        Assert.Equal(0, sim.Diseases.Cases.Count(c => c.Active));
    }
    [Fact("狼必须通过局部感知猎杀真实猎物并恢复能量")]
    public void PredatorConsumesPrey()
    {
        var sim = World(); sim.Wildlife.Reset();
        sim.InterveneSpawnAnimals(10, 10, 1, 0); Assert.True(sim.Predators.Spawn(10, 10));
        sim.Predators.Wolves[0].Energy = 0.2f;
        sim.Predators.TickFast(10);
        Assert.Equal(0, sim.Wildlife.LiveCount); Assert.Equal(1, sim.Predators.TotalKills);
        Assert.True(sim.Predators.Wolves[0].Energy > 0.2f);
    }
    [Fact("新系统、历史和每日曲线必须完整存档并精确续跑")]
    public void AllNewStateSurvivesSaveLoad()
    {
        var sim = Communities(out int a, out int b);
        sim.Storage.Deposit(a, ResourceKind.Food, 200); sim.Storage.Deposit(b, ResourceKind.Wood, 300);
        sim.Civilizations.TryDispatch(1, 2); sim.Diseases.Infect(sim.Agents.AliveSlots().First()); sim.Predators.Spawn(30, 30);
        sim.Tick(1440);
        string json = SaveFile.Encode(sim);
        var loaded = Simulation.CreateForRestore(sim.Config.Clone(), 100, 100, 3);
        var result = SaveLoader.Load(loaded, json);
        Assert.True(result.Success && result.DigestMatches);
        Assert.Equal(sim.Events.Count, loaded.Events.Count); Assert.Equal(sim.Stats.Daily.Count, loaded.Stats.Daily.Count);
        Assert.Equal(sim.Society.Encode().ToJson(false), loaded.Society.Encode().ToJson(false));
        Assert.Equal(sim.Civilizations.Encode().ToJson(false), loaded.Civilizations.Encode().ToJson(false));
        Assert.Equal(sim.Diseases.Encode().ToJson(false), loaded.Diseases.Encode().ToJson(false));
        Assert.Equal(sim.Predators.Encode().ToJson(false), loaded.Predators.Encode().ToJson(false));
        for (int i = 0; i < 3000; i++) { sim.Tick(1); loaded.Tick(1); }
        Assert.Equal(StateHash.ComputeDigest(sim), StateHash.ComputeDigest(loaded));
    }
    [Fact("升降工具必须修改海拔，并完整保存")]
    public void HeightToolPersists()
    {
        var sim = World(); float before = sim.World.TileAt(20, 20).Height;
        PlayerTools.Apply(sim, PlayerTool.Raise, 20, 20, 0, 10);
        Assert.True(sim.World.TileAt(20, 20).Height > before);
        var loaded = Simulation.CreateForRestore(sim.Config.Clone(), 100, 100, 1);
        Assert.True(SaveLoader.Load(loaded, SaveFile.Encode(sim)).DigestMatches);
        Assert.Near(sim.World.TileAt(20, 20).Height, loaded.World.TileAt(20, 20).Height);
    }
}
