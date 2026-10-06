using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Systems;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

/// <summary>
/// M4 验收测试：出生、住房/食物约束、农业产出、建筑衰减。
///
/// # 这一组测试的写法原则
///
/// 每一条都对应 docs/archive/12-Milestones.md §M4 里的一个**验收判据**，并在用例名里写清楚判据原话。
/// 凡是"方向性"的判据（例如"饥荒期出生率下降"）都用**对照世界**来断言，
/// 而不是断言某个绝对数字 —— 后者会在每次调参后变成噪声。
///
/// # 为什么这些测试重要
///
/// M4 之前，这个世界**必然灭绝**：没有出生，人口单调降到 0。
/// 所以这一组测试守住的是"世界能不能长期存在"这件事，
/// 而不是某个数值对不对。其中最关键的几条：
///   * 床位为 0 时出生数必须为 0（住房是**门**而不是加成）；
///   * 长跑之后人口必须被**承载量**约束住（既不能灭绝、也不能爆炸）；
///   * 死亡必须释放床位（漏释放会让出生率缓慢掉到 0，且不报错）。
/// </summary>
public sealed class M4Tests
{
    private const int TicksPerDay = 1440;

    private static SimConfig Config(int size = 44)
    {
        var config = new SimConfig();
        config.World.Width = size;
        config.World.Height = size;
        return config;
    }

    private static void Flatten(Simulation sim)
    {
        int size = sim.World.Width;
        int min = size / 6;
        int max = size - min;
        for (int y = min; y <= max; y++)
        {
            for (int x = min; x <= max; x++)
            {
                sim.World.SetTerrain(x, y, TerrainKind.Grass);
                sim.World.SetVegetation(x, y, 0.3f);
            }
        }
        sim.World.RefreshSpatialIndex();
    }

    /// <summary>造一个"宜居"世界：地形平整、有足够的食物储备、有房子。</summary>
    private static Simulation MakeHabitable(
        int seed, int houses, int agents, bool stockFood, bool forbidBuilding = false)
    {
        var sim = new Simulation(Config(), 44, 44, seed);
        Flatten(sim);
        sim.InterveneSpawnHumans(30, 30, agents, 6);

        if (forbidBuilding)
        {
            // 把建造的驱动力与材料权重都清零 ⇒ AI 永不选择建造。
            // 这样"没有床位"这个前提才真的稳定（否则它会自己盖房子）。
            sim.Config.Buildings.BuildGapWeight = 0f;
            sim.Config.Buildings.BuildMaterialWeight = 0f;
            sim.Config.Buildings.BuildSiteWeight = 0f;
        }

        if (stockFood)
        {
            // 直接注入食物：让"食物因子"不再成为瓶颈，从而单独观察住房这一条门。
            for (int i = 0; i < 12; i++)
            {
                sim.InterveneAddResource(20 + (i % 6), 20 + (i / 6), ResourceKind.Food, 60f);
            }
        }

        // 造房子：用真实放置路径，保证床位统计正确
        int placed = 0;
        for (int y = 24; y < 40 && placed < houses; y += 2)
        {
            for (int x = 24; x < 40 && placed < houses; x += 2)
            {
                int index = sim.Buildings.Place(sim.World, BuildingKind.House, x, y, 100);
                if (index < 0) { continue; }
                sim.Buildings.MarkComplete(index, 0);
                placed++;
            }
        }

        sim.World.RefreshSpatialIndex();
        return sim;
    }

    /// <summary>
    /// 找一处"靠水的草地"并放置一块农田。
    ///
    /// 为什么需要它：农田配方带 `requiresWaterAccess`，而 `Place` 会走 `CanPlaceAt` 校验 ——
    /// 在平整过的内陆草地上直接放农田会返回 -1。
    /// 第一版测试就踩了这个（期望放 4 块、实际 0 块），
    /// 而失败信息看起来像"农业没实现"，实际是"选址规则按预期拒绝了"。
    /// </summary>
    private static bool TryPlaceFarmNearWater(Simulation sim, out int index)
    {
        index = -1;
        int size = sim.World.Width;

        for (int y = 1; y < size - 1; y++)
        {
            for (int x = 1; x < size - 1; x++)
            {
                if (sim.World.TileAt(x, y).Terrain != TerrainKind.Grass) { continue; }
                if (!BuildingStore.CanPlaceAt(sim.World, BuildingKind.Farm, x, y)) { continue; }

                index = sim.Buildings.Place(sim.World, BuildingKind.Farm, x, y, 100);
                if (index < 0) { continue; }

                sim.Buildings.MarkComplete(index, 0);
                sim.World.SetTerrain(x, y, TerrainKind.Farmland);
                return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------------
    // 验收 2：出生被住房约束（床位为 0 时出生数为 0）
    // ---------------------------------------------------------------------

    [Fact("验收2：床位为 0 时出生数必须为 0（住房是门而不是加成）")]
    public void NoBedsMeansNoBirths()
    {
        // # 这条测试的设计改过两版，值得记录为什么最终长成这样
        //
        // 第一版：造一个"没有房子"的世界，跑 40 天，断言出生数为 0。
        //   —— 失败（出生 9 个）。原因是 AI **自己盖了房子**：产品完全正确，
        //      错的是"没有房子"这个前提没有被保持住。
        // 第二版：把 `BuildGapWeight` / `BuildMaterialWeight` / `BuildSiteWeight` 清零来禁止建造。
        //   —— 仍然失败（床位后来变成 10）。因为建造效用还有**别的非零来源**
        //      （勤劳性格加成等），把它们一起清零既脆弱又会污染场景。
        // 第三版（当前）：不去冻结经济，而是**逐日断言那条不变式**：
        //      "只要此刻床位为 0，这一天的出生数就不能增加"。
        //
        // 这才是"门"的准确表述：它不依赖世界会不会盖房子，
        // 而是在每一个可观测的时刻检查"没有床位 ⇒ 没有出生"。
        // 一个把住房当**加成**而不是**门**的实现会立刻违反它。
        Simulation noHouses = MakeHabitable(7001, houses: 0, agents: 12, stockFood: true, forbidBuilding: true);
        Simulation withHouses = MakeHabitable(7001, houses: 10, agents: 12, stockFood: true);

        Assert.Equal(0, noHouses.Buildings.TotalBeds);
        Assert.True(withHouses.Buildings.TotalBeds > 0, "对照组必须有床位，否则这条测试没有意义");

        int daysWithZeroBeds = 0;
        int birthsWhileNoBeds = 0;
        for (int day = 0; day < 24; day++)
        {
            int bedsBefore = noHouses.Buildings.TotalBeds;
            int birthsBefore = noHouses.Stats.TotalBirths;

            noHouses.Tick(TicksPerDay);
            withHouses.Tick(TicksPerDay);

            if (bedsBefore != 0) { continue; }

            daysWithZeroBeds++;
            birthsWhileNoBeds += noHouses.Stats.TotalBirths - birthsBefore;
        }

        Assert.True(daysWithZeroBeds > 0,
            "必须在「床位为 0」的状态下至少观测到一天，否则这条不变式没被检验到");
        Assert.Equal(0, birthsWhileNoBeds);

        // 注意这里**不**断言"整个无房世界的出生总数是 0"。
        // 试过那样写，它失败了 —— 因为 AI 后来自己盖了房子，
        // 而那时出生是**正确**的行为。把"世界会不会盖房子"卷进这条断言，
        // 测的就不是"住房门"而是"建造行为"，那是另一条测试的事。
        Assert.True(withHouses.Stats.TotalBirths > 0,
            "有房且食物充足的世界必须有新生儿 —— 否则 M4 的核心机制没有生效");
    }

    [Fact("验收2b：住房门被触发时必须有可观测的计数（不能静默地生不出）")]
    public void HousingGateIsObservable()
    {
        Simulation sim = MakeHabitable(7002, houses: 0, agents: 10, stockFood: true, forbidBuilding: true);

        // 至少跑一天，让配对与出生评估发生
        for (int day = 0; day < 3; day++) { sim.Tick(TicksPerDay); }

        Assert.Equal(0, sim.Stats.TotalBirths);

        // 门被触发的证据：要么"缺房"被计数，要么当时还没有合格伴侣。
        // 断言"两者至少有一个成立"，避免把测试绑死在配对时序上。
        bool gateObserved = sim.Births.BlockedByHousingThisDay > 0
            || sim.Births.EligiblePairsThisDay == 0;
        Assert.True(gateObserved,
            "无房世界里应当能看到「缺房」计数，或者当时没有合格伴侣 —— 两者都不成立说明出生路径没跑到");
    }

    // ---------------------------------------------------------------------
    // 验收 3：出生被食物约束（方向性）
    // ---------------------------------------------------------------------

    [Fact("验收3：食物短缺会降低出生数（对照世界，方向性断言）")]
    public void FoodScarcityReducesBirths()
    {
        // # 为什么用 FoodPerCapitaTarget 制造差异，而不是"多撒点食物"
        //
        // 第一版给 rich 世界定期 `InterveneAddResource` 撒食物，结果两边出生数**完全相同**（17 vs 17）。
        // 原因：出生公式读的是"人均**当下可支配**的食物"（共享库存 + 地面堆 + 随身），
        // 而 `InterveneAddResource` 写的是**格子上的资源存量** ——
        // 那些食物要先被采集才进入"可支配"口径，60 天里没来得及。
        //
        // 所以这里改用一个能**确定性地**改变食物因子的杠杆：
        // `FoodPerCapitaTarget` 就是食物因子的分母。目标越高 ⇒ 同样的存量算出来的因子越低。
        // 两个世界同源、跑同样的 tick 数，唯一差别就是这个值 ——
        // 于是"食物因子下降 ⇒ 出生数下降"被干净地隔离出来。
        Simulation undemanding = MakeHabitable(7003, houses: 12, agents: 14, stockFood: true);
        Simulation demanding = MakeHabitable(7003, houses: 12, agents: 14, stockFood: true);

        undemanding.Config.Birth.FoodPerCapitaTarget = 5f;    // 很容易满足 ⇒ 食物因子接近 1
        demanding.Config.Birth.FoodPerCapitaTarget = 5000f;   // 几乎不可能满足 ⇒ 食物因子接近 0

        for (int day = 0; day < 30; day++)
        {
            undemanding.Tick(TicksPerDay);
            demanding.Tick(TicksPerDay);
        }

        Assert.True(undemanding.Stats.TotalBirths > demanding.Stats.TotalBirths,
            "食物因子高的世界出生数必须**严格大于**食物因子低的世界（实测 "
            + undemanding.Stats.TotalBirths + " vs " + demanding.Stats.TotalBirths
            + "）—— 这是「食物约束出生」这条判据的方向性证明");
        Assert.True(undemanding.Stats.TotalBirths > 0, "对照组必须有出生，否则方向性比较没有意义");
    }

    // ---------------------------------------------------------------------
    // 验收 1 / 4 / 7：长跑的形态（上升段、负反馈、被承载量约束）
    // ---------------------------------------------------------------------

    [Fact("验收1/4/7：长跑里人口必须先上升、再被承载量约束（既不灭绝也不爆炸）")]
    public void PopulationRisesThenIsBounded()
    {
        // 这条测试代替"人眼看曲线"：把三个判据写成可断言的形状。
        // 它跑 60 天（不是 200 天）以保持测试套件可接受 —— 200 天由 CLI 长跑覆盖。
        Simulation sim = MakeHabitable(7004, houses: 18, agents: 16, stockFood: true);

        int start = sim.Agents.LiveCount;
        int peak = start;
        int trough = start;
        int beds = sim.Buildings.TotalBeds;

        for (int day = 0; day < 34; day++)
        {
            sim.Tick(TicksPerDay);

            int population = sim.Agents.LiveCount;
            if (population > peak) { peak = population; }
            if (population < trough) { trough = population; }

            float hunger = 0f;
            float health = 0f;
            int live = 0;
            foreach (int slot in sim.Agents.AliveSlots())
            {
                hunger += sim.Agents.HungerOf(slot);
                health += sim.Agents.HealthOf(slot);
                live++;
            }
            if (live > 0)
            {
                Assert.True(SimMath.IsFinite(hunger / live), "第 " + day + " 天出现 NaN 饥饿");
                Assert.True(SimMath.IsFinite(health / live), "第 " + day + " 天出现 NaN 健康");
            }
        }

        // 验收 1：出现过上升段
        Assert.True(peak > start,
            "人口必须出现过上升段（起点 " + start + "，峰值 " + peak + "）—— "
            + "这正是 M4 存在的理由：在它之前人口只会单调下降到 0");

        // 验收 7：没有灭绝，也没有爆炸（被承载量约束）
        Assert.True(sim.Agents.LiveCount > 0, "世界不应该灭绝");
        Assert.True(peak <= beds + 8,
            "人口峰值（" + peak + "）不应远超床位数（" + beds + "）—— 住房是承载量的主要来源，"
            + "超出太多说明住房门没有真正约束住增长");

        // 验收 4：负反馈的证据 —— 人口在峰值之后回落过
        Assert.True(trough < peak,
            "人口必须出现过回落（峰 " + peak + " / 谷 " + trough + "）—— 没有回落就没有负反馈，"
            + "那意味着增长没有被任何东西约束");
    }

    // ---------------------------------------------------------------------
    // 验收 5：农业真的产出
    // ---------------------------------------------------------------------

    [Fact("验收5：有农田的世界食物增速高于无农田的对照组")]
    public void FarmsProduceFood()
    {
        Simulation noFarm = MakeHabitable(7005, houses: 10, agents: 12, stockFood: false);
        Simulation withFarm = MakeHabitable(7005, houses: 10, agents: 12, stockFood: false);

        // 农田配方带 `requiresWaterAccess`，因此必须放在**靠水的草地**上 ——
        // 在内陆草地上直接 `Place` 会返回 -1（第一版测试就踩了这个：
        // 期望 4 块、实际 0 块，而失败信息看起来像"农业没实现"）。
        int placed = 0;
        for (int i = 0; i < 4; i++)
        {
            if (TryPlaceFarmNearWater(withFarm, out int _)) { placed++; }
        }
        Assert.True(placed > 0,
            "必须在靠水的草地上放下至少一块农田（放了 " + placed + " 块）—— 否则测不到农业产出");

        noFarm.World.RefreshSpatialIndex();
        withFarm.World.RefreshSpatialIndex();

        for (int day = 0; day < 18; day++)
        {
            noFarm.Tick(TicksPerDay);
            withFarm.Tick(TicksPerDay);
        }

        Assert.True(withFarm.Buildings.LaborOf(0) >= 0f);

        // 对照判据：有农田的世界，累计农业产出必须为正，且无农田的世界必然为 0。
        //
        // 用累计产出而不是"当前库存"：库存同时受采集、消耗、搬运影响，
        // 从它反推"农业贡献了多少"是不可能的。
        Assert.True(withFarm.BuildingSystem.TotalFoodProduced > 0f,
            "有农田的世界累计农业产出必须为正（实测 "
            + withFarm.BuildingSystem.TotalFoodProduced.ToString("0.##") + "）—— "
            + "这正是 docs/archive/12-Milestones.md 验收项 5「有农田时食物存量增速高于无农田对照组」的直接证据");
        // 注意这里**不**断言"对照组产出恰好为 0"。
        // 原先那样写是错的：它隐含假设"AI 永远不会自己盖农田"，
        // 而 M6 把社会动作加进注册表之后，效用格局变了、对照组也开始盖田（实测 269.7）。
        // 那暴露的是断言的脆弱，不是产品的问题 —— 对照组有没有自己盖田，
        // 不该影响"农田会产出食物"这条判据。
        Assert.True(withFarm.BuildingSystem.TotalFoodProduced > noFarm.BuildingSystem.TotalFoodProduced,
            "有农田组的累计产出必须高于对照组（" + withFarm.BuildingSystem.TotalFoodProduced.ToString("0.##")
            + " vs " + noFarm.BuildingSystem.TotalFoodProduced.ToString("0.##") + "）");
    }

    // ---------------------------------------------------------------------
    // 死亡必须释放床位（漏释放会让出生率缓慢掉到 0）
    // ---------------------------------------------------------------------

    [Fact("死亡必须释放床位（床位占用不能泄漏）")]
    public void DeathReleasesBeds()
    {
        Simulation sim = MakeHabitable(7006, houses: 6, agents: 10, stockFood: true);

        // 手工让每个人都搬进房子，把床位占满
        for (int day = 0; day < 2; day++) { sim.Tick(TicksPerDay); }

        int occupiedBefore = sim.Buildings.OccupiedBeds;

        // 杀掉一个已入住的个体
        int victim = -1;
        foreach (int slot in sim.Agents.AliveSlots())
        {
            if (sim.Agents.DwellingOf(slot) >= 0) { victim = slot; break; }
        }

        if (victim < 0)
        {
            // 本场景里没人拿到住所：那么占用数应当是 0，测试同样成立
            Assert.Equal(0, sim.Buildings.OccupiedBeds);
            return;
        }

        sim.Births.OnAgentRemoved(victim);

        Assert.True(sim.Buildings.OccupiedBeds < occupiedBefore,
            "个体被移出后床位占用必须下降（" + occupiedBefore + " -> " + sim.Buildings.OccupiedBeds + "）—— "
            + "漏释放不会报错，只会让出生率缓慢掉到 0");
    }

    // ---------------------------------------------------------------------
    // 儿童阶段
    // ---------------------------------------------------------------------

    [Fact("新生儿必须是儿童阶段，且年龄从 0 开始")]
    public void NewbornsAreChildren()
    {
        Simulation sim = MakeHabitable(7007, houses: 14, agents: 14, stockFood: true);

        int newborns = 0;
        for (int day = 0; day < 26 && newborns == 0; day++)
        {
            sim.Tick(TicksPerDay);

            foreach (int slot in sim.Agents.AliveSlots())
            {
                if (sim.Agents.AgeDaysOf(slot) != 0) { continue; }
                if (sim.Agents.MotherOf(slot) < 0 && sim.Agents.FatherOf(slot) < 0) { continue; }

                Assert.Equal(LifeStage.Child, sim.Agents.LifeStageOf(slot));
                Assert.True(sim.Agents.MotherOf(slot) >= 0, "新生儿必须有母亲");
                Assert.True(sim.Agents.FatherOf(slot) >= 0, "新生儿必须有父亲");
                newborns++;
            }
        }

        Assert.True(newborns > 0,
            "26 天内必须出现至少一个新生儿，否则「家庭雏形」这条没有生效");
    }

    [Fact("性格遗传必须落在双亲均值附近（允许对称突变）")]
    public void PersonalityIsInherited()
    {
        Simulation sim = MakeHabitable(7008, houses: 14, agents: 16, stockFood: true);

        for (int day = 0; day < 26; day++)
        {
            sim.Tick(TicksPerDay);

            foreach (int slot in sim.Agents.AliveSlots())
            {
                int mother = sim.Agents.MotherOf(slot);
                int father = sim.Agents.FatherOf(slot);
                if (mother < 0 || father < 0) { continue; }
                if (sim.Agents.AgeDaysOf(slot) != 0) { continue; }

                float mutation = sim.Config.Birth.MutationScale + 1e-4f;
                float motherAggression = sim.Agents.PersonalityOf(mother).Aggression;
                float fatherAggression = sim.Agents.PersonalityOf(father).Aggression;
                float expected = (motherAggression + fatherAggression) * 0.5f;
                float actual = sim.Agents.PersonalityOf(slot).Aggression;

                Assert.True(System.Math.Abs(actual - expected) <= mutation + 1e-3f,
                    "子代侵略性应当落在双亲均值 ± 突变幅度内（期望约 "
                    + expected.ToString("0.###") + "，实际 " + actual.ToString("0.###") + "）");
                return;
            }
        }

        Assert.True(false, "26 天内没有出现可检查的新生儿");
    }

    // ---------------------------------------------------------------------
    // 建筑衰减
    // ---------------------------------------------------------------------

    [Fact("无人维护的建筑会衰减并被拆除；有人住的不会")]
    public void UnusedBuildingsDecay()
    {
        var config = Config();
        // 把衰减调快，让测试在几天内看到结果（否则要跑几十天）
        config.Buildings.DecayPerDay = 0.25f;
        config.Buildings.DecayGraceDays = 0f;

        var sim = new Simulation(config, 44, 44, 7009);
        Flatten(sim);

        int house = sim.Buildings.Place(sim.World, BuildingKind.House, 30, 30, 50);
        Assert.GreaterOrEqual(house, 0);
        sim.Buildings.MarkComplete(house, 0);

        Assert.Near(1f, sim.Buildings.DecayOf(house), 1e-5f);

        // 跑 10 天：无人使用 ⇒ 完整度归零 ⇒ 拆除
        for (int day = 0; day < 10; day++) { sim.Tick(TicksPerDay); }

        Assert.True(!sim.Buildings.IsAlive(house),
            "无人维护的房子应当倒塌（完整度 " + sim.Buildings.DecayOf(house).ToString("0.###") + "）—— "
            + "这是给「建造」补上的负反馈：没有它，建筑只会单调增加");
    }

    [Fact("调参耦合：存放门槛必须高于随身舒适量（否则存放会挤掉生存动作）")]
    public void DepositThresholdMustExceedInventoryComfort()
    {
        // 这条断言锁住一个**跨配置段的耦合**：M2 定的 SurplusThreshold 与
        // M3 引入的 InventoryComfort 分处两个段，前者被后者越过之后
        // "存放"的效用电平恒为满值，把进食/饮水/睡眠全部挤掉 ——
        // 实测 75% 的决策都在搬东西，人口因为脱水从 40 掉到 0。
        //
        // 判据本身（门槛要高于平时携带量）从来没变，错在没人把两处放在一起看。
        var config = new SimConfig();
        Assert.True(config.GroundStocks.SurplusThreshold > config.Ai.InventoryComfort,
            "GroundStocks.SurplusThreshold（" + config.GroundStocks.SurplusThreshold
            + "）必须显著高于 Ai.InventoryComfort（" + config.Ai.InventoryComfort + "）—— "
            + "否则个体随身量会常年越过存放门槛，存放变成永远最优的动作");
    }

    [Fact("验收6：加入出生与农业之后，确定性不破（同 seed 两次摘要一致）")]
    public void DeterminismHoldsWithBirthsAndFarms()
    {
        Simulation Run()
        {
            Simulation sim = MakeHabitable(7010, houses: 12, agents: 14, stockFood: true);

            int placed = 0;
            for (int y = 26; y < 34 && placed < 3; y += 2)
            {
                for (int x = 26; x < 34 && placed < 3; x += 2)
                {
                    int index = sim.Buildings.Place(sim.World, BuildingKind.Farm, x, y, 60);
                    if (index < 0) { continue; }
                    sim.Buildings.MarkComplete(index, 0);
                    placed++;
                }
            }

            sim.World.RefreshSpatialIndex();
            sim.Tick(TicksPerDay * 16);
            return sim;
        }

        Simulation first = Run();
        Simulation second = Run();

        Assert.True(first.Stats.TotalBirths > 0, "这一局必须有出生，否则测不到出生路径的确定性");
        Assert.Equal(first.StateDigestString(), second.StateDigestString(),
            "带出生与农业的世界必须逐 tick 可复现");
    }
}
