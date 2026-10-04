using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Systems;

/// <summary>
/// 出生系统（M4）—— 这个世界**唯一缺失的机制**。
///
/// # 为什么它是 M4 的核心
///
/// M2 / M3 的长跑结论非常明确：**没有出生 ⇒ 人口单调下降 ⇒ 100 天后归零**
/// （实测 150 天：38 例衰老、2 例脱水、**0 例饥饿**）。
/// 也就是说，前面几个里程碑做出来的经济、建造、迁徙，
/// 最终都会因为"没有人接替"而走向同一个结局。补上出生，
/// 世界才第一次有可能**长期存在**。
///
/// # 为什么是四条因子而不是一条
///
/// ```text
/// P_birth = Base × FoodFactor × HousingFactor × HealthFactor × (1 − PopPressure)
/// ```
///
/// 四条各自管一件**不同的**事，缺任何一条都会退化成一种无聊的世界：
///
/// | 因子 | 管什么 | 缺了会怎样 |
/// |---|---|---|
/// | `FoodFactor` | 饿着的时候不该生孩子 | 饥荒里人口照样涨 ⇒ 食物机制形同虚设 |
/// | `HousingFactor` | **硬门**：没床位就生不了 | 人口只受食物限制 ⇒ 建造没有意义 |
/// | `HealthFactor` | 虚弱/生病的人不该生孩子 | 生育变成纯抽奖，健康不参与 |
/// | `(1 − 人口压力)` | 增长会自己慢下来 | 指数爆炸 ⇒ 世界几十天就崩 |
///
/// 其中 `HousingFactor` 是**门（gate）而不是加成**：它要么 1 要么 0。
/// 这不是数值选择，而是"住房约束"这条验收判据要求的形式 ——
/// "床位为 0 时出生数为 0"只有在它是乘性零因子时才成立。
///
/// # 确定性
///
/// * 遍历**按槽位升序**（`AgentStore.AliveSlots` 本身就是升序）；
/// * 配对规则固定（每个个体只与自己配对的伴侣生育，且由**较小槽位**一方发起）；
/// * 随机数只取自 `RngStream.Agents`；
/// * 所有新增状态（伴侣、住所、生育数、上次生育时刻）都进 `StateHash` 与存档。
///
/// 换句话说：**同 seed + 同放置指令 ⇒ 人口曲线逐 tick 一致**。
/// </summary>
public sealed class BirthSystem
{
    private readonly Simulation _sim;
    private readonly AgentStore _store;
    private readonly BirthConfig _config;

    /// <summary>本日出生数。</summary>
    public int BirthsThisDay { get; private set; }

    /// <summary>累计出生数（与 <see cref="SimulationStats.TotalBirths"/> 同步）。</summary>
    public int TotalBirths { get; private set; }

    /// <summary>本日评估的合格伴侣对数（调试/UI 用："今天有几对能生"）。</summary>
    public int EligiblePairsThisDay { get; private set; }

    /// <summary>本日因为**没有空床位**而被门挡下的次数 —— 直接对应验收项 2。</summary>
    public int BlockedByHousingThisDay { get; private set; }

    /// <summary>本日因为食物不足导致概率被压低的平均系数（0..1，1 = 食物充足）。</summary>
    public float AverageFoodFactorThisDay { get; private set; }

    /// <summary>最近一次出生的事件描述（调试/UI 用）。</summary>
    public string LastBirthDetail { get; private set; } = string.Empty;

    public BirthSystem(Simulation sim, AgentStore store)
    {
        _sim = sim ?? throw new System.ArgumentNullException(nameof(sim));
        _store = store ?? throw new System.ArgumentNullException(nameof(store));
        _config = sim.Config.Birth;
    }

    /// <summary>
    /// 每天维护一次伴侣关系与住所（M4 的"家庭雏形"）。
    ///
    /// # 为什么用"同住/同地"近似伴侣
    ///
    /// 任务书里的关系系统是 M6。M4 需要一个**能用的**近似来回答
    /// "这两个人算不算一家人"，而"离得近 + 都成年"是最朴素也最可解释的答案。
    /// 它的好处是**完全由已有状态推导**：没有新的隐藏状态，
    /// 因此不会成为 Phase 0 那类"漏存一个字段就跑偏"的源头。
    ///
    /// 规则（全部按槽位升序，保证确定性）：
    ///   1. 清理失效关系（伴侣死了、或自己有了住所且伴侣不住一起）；
    ///   2. 没有伴侣的合格个体，找 `PairRadius` 内**最近**的合格且无伴侣的个体配对；
    ///   3. 有住所的个体优先与**同住所**的人配对（"住在一起"比"离得近"更强）。
    /// </summary>
    public void TickPairing(long tick)
    {
        int radius = (int)System.Math.Ceiling(_config.PairRadius);
        int radiusSquared = radius * radius;

        // ---- 1) 清理：伴侣已死 / 已不再是合格生育者 ----
        foreach (int slot in _store.AliveSlots())
        {
            int partner = _store.PartnerOf(slot);
            if (partner < 0) { continue; }

            bool broken = !_store.IsSlotAlive(partner)
                || _store.PartnerOf(partner) != slot
                || !IsEligible(partner);

            if (broken) { _store.SetPartner(slot, -1); }
        }

        // ---- 2) 配对 ----
        foreach (int slot in _store.AliveSlots())
        {
            if (!IsEligible(slot)) { continue; }
            if (_store.PartnerOf(slot) >= 0) { continue; }

            int ownDwelling = _store.DwellingOf(slot);

            // 优先找同住所的（"住在一起"比"离得近"更强），
            // 其次才是半径内最近的。两轮都按槽位升序，结果与遍历顺序无关。
            int best = -1;
            int bestDistance = int.MaxValue;

            if (ownDwelling >= 0)
            {
                foreach (int candidate in _store.AliveSlots())
                {
                    if (candidate == slot) { continue; }
                    if (!IsEligible(candidate)) { continue; }
                    if (_store.PartnerOf(candidate) >= 0) { continue; }
                    if (_store.DwellingOf(candidate) != ownDwelling) { continue; }

                    if (candidate < best || best < 0) { best = candidate; }
                }
            }

            if (best < 0)
            {
                int sx = _store.XOf(slot);
                int sy = _store.YOf(slot);

                foreach (int candidate in _store.AliveSlots())
                {
                    if (candidate == slot) { continue; }
                    if (!IsEligible(candidate)) { continue; }
                    if (_store.PartnerOf(candidate) >= 0) { continue; }

                    int dx = _store.XOf(candidate) - sx;
                    int dy = _store.YOf(candidate) - sy;
                    int distanceSquared = (dx * dx) + (dy * dy);
                    if (distanceSquared > radiusSquared) { continue; }

                    if (distanceSquared < bestDistance)
                    {
                        bestDistance = distanceSquared;
                        best = candidate;
                    }
                }
            }

            if (best < 0) { continue; }

            _store.SetPartner(slot, best);
            _store.SetPartner(best, slot);

            _sim.Events.Record(
                tick,
                History.WorldEventType.RelationshipFormed,
                _store.NameOrOverride(slot) + " 与 " + _store.NameOrOverride(best) + " 组成家庭",
                History.EventImportance.Normal,
                new Int2(_store.XOf(slot), _store.YOf(slot)),
                slot,
                -1,
                "同住/同地");
        }

        // ---- 3) 住所分配：有家的人不该一直"无家可归" ----
        foreach (int slot in _store.AliveSlots())
        {
            if (_store.DwellingOf(slot) >= 0) { continue; }
            if (_store.LifeStageOf(slot) == LifeStage.Child) { continue; }

            int house = _sim.BuildingSystem.FindHouseWithFreeBed(_store.XOf(slot), _store.YOf(slot));
            if (house < 0) { continue; }
            if (!_sim.Buildings.TryOccupyBedOf(house)) { continue; }

            _store.SetDwelling(slot, house);
        }
    }

    /// <summary>
    /// 个体死亡/迁出时释放床位并解开伴侣关系。
    ///
    /// 为什么必须有这一步：床位占用是**计数的**，漏释放会让
    /// "床位够不够"永远偏向"不够"，出生率缓慢掉到 0 ——
    /// 而且没有任何报错，只表现为"这个世界的孩子越来越少"。
    /// </summary>
    public void OnAgentRemoved(int slot)
    {
        int dwelling = _store.DwellingOf(slot);
        if (dwelling >= 0) { _sim.Buildings.ReleaseBedOf(dwelling); }

        int partner = _store.PartnerOf(slot);
        if (partner >= 0 && _store.IsSlotAlive(partner) && _store.PartnerOf(partner) == slot)
        {
            _store.SetPartner(partner, -1);
        }

        _store.SetPartner(slot, -1);
        _store.SetDwelling(slot, -1);
    }

    /// <summary>读档时恢复累计出生数。</summary>
    public void RestoreCounters(int totalBirths) => TotalBirths = totalBirths;

    /// <summary>
    /// 每天评估一次出生（由 `Simulation.TickDay` 调用，**在年龄推进之后**）。
    ///
    /// 为什么在年龄推进之后：否则"今天刚成年的个体"要等到第二天才可能生育，
    /// 而"今天刚进入老年的个体"还会多生一天。这类错位在长跑里会累积成可见的偏差。
    /// </summary>
    public void TickDay(long tick)
    {
        BirthsThisDay = 0;
        EligiblePairsThisDay = 0;
        BlockedByHousingThisDay = 0;
        AverageFoodFactorThisDay = 1f;

        if (_store.LiveCount < 2) { return; }

        World world = _sim.World;
        int ticksPerDay = world.Calendar.TicksPerDay;

        float foodFactor = ComputeFoodFactor();
        AverageFoodFactorThisDay = foodFactor;

        // 人口压力：用"床位提供的承载量"而不是地图面积 ——
        // 后者会让"没人盖房子"的世界也能无限制增长，住房约束就白设了。
        float pressure = ComputePopulationPressure();
        float pressureFactor = 1f - (SimMath.Clamp01(pressure) * _config.PopPressureScale);

        DeterministicRandom rng = _sim.Random.Get(RngStream.Agents);

        float foodFactorFloorAdjusted = System.Math.Max(foodFactor, _config.FoodFactorFloor);
        float sumFood = 0f;
        int pairs = 0;

        // 升序遍历保证确定性；配对由**较小槽位**一方发起，因此每对只算一次。
        foreach (int slot in _store.AliveSlots())
        {
            if (!IsEligible(slot)) { continue; }

            int partner = _store.PartnerOf(slot);
            if (partner < 0 || partner == slot) { continue; }
            if (!_store.IsSlotAlive(partner)) { continue; }
            if (!IsEligible(partner)) { continue; }

            // 只由较小槽位发起 ⇒ 每对每天恰好评估一次（与遍历顺序无关）
            if (slot > partner) { continue; }

            // 生育间隔门：同一对不能连续生
            long last = _store.LastBirthTickOf(slot);
            if (last >= 0)
            {
                long minGap = (long)(_config.MinDaysBetweenBirths * ticksPerDay);
                if (tick - last < minGap) { continue; }
            }

            EligiblePairsThisDay++;
            pairs++;

            // 住房是**硬门**：全聚落只要还有一张空床就能生，一张都没有就完全不能生。
            //
            // 这里刻意用"聚落级空床"而不是"这一对住的房子里有没有空床"。
            // 一开始写成了后者，结果是：配对偏好"同住"，于是伴侣往往同住一间**已经住满**的房子，
            // 门永远关闭，出生数恒为 0 —— 而且不报错，只表现为"这个世界没有孩子"。
            // 语义上也是聚落级更对："床位够不够"是一个聚落问题，不是一间房子的问题。
            if (_sim.Buildings.FreeBeds <= 0)
            {
                BlockedByHousingThisDay++;
                continue;
            }

            float health = (_store.HealthOf(slot) + _store.HealthOf(partner)) * 0.5f;

            // 健康因子取 `0.5 + 0.5 × health` 而不是裸 `health`。
            //
            // 理由与"健康不该是门"是同一个：慢性亚健康（例如长期缺水导致健康停在 0.4）
            // 会让裸 `health` 把出生率压到 4 成，而且**永远恢复不了** ——
            // 实测 40 人世界里出生率只有约 0.2/天，而死亡约 0.44/天，世界必然灭绝。
            // 健康在这里的作用应该是"明显虚弱时显著降低生育"，而不是
            // "只要不是满血就几乎不能生"。这也是真实人口学里的形状：
            // 生育率与健康是正相关，但不是线性归零。
            float healthFactor = 0.5f + (0.5f * health);

            // M5 规则开关：`HighBirthRate` 把基础概率翻倍。
            // 乘以**基础值**而不是乘在最终概率上，是为了让 `MaxChancePerDay` 这道防爆闸
            // 依然对最终结果生效 —— 否则"高出生率 + 极端参数"可以绕过上限。
            float baseChance = _config.BaseChancePerDay;
            if (_sim.Config.Rules.HighBirthRate) { baseChance *= 2f; }

            float p = baseChance
                * foodFactorFloorAdjusted
                * 1f                  // 住房满足 ⇒ 因子为 1（门已在上面判定）
                * healthFactor
                * pressureFactor;

            if (p > _config.MaxChancePerDay) { p = _config.MaxChancePerDay; }
            if (p <= 0f) { continue; }

            if (rng.NextDouble() >= p) { continue; }

            if (TryBirth(slot, partner, tick)) { sumFood += foodFactorFloorAdjusted; }
        }

        if (pairs > 0 && sumFood > 0f) { AverageFoodFactorThisDay = sumFood / pairs; }
    }

    /// <summary>
    /// 是否"结构上合格"的生育者：存活、且不是儿童。
    ///
    /// # 为什么健康**不**在这里判定（一个真实踩到的设计错误）
    ///
    /// 一开始这里还带了 `Health > 0.35`，结果是灾难性的：
    /// 健康被当成了**关系的资格**，于是某个人一旦暂时虚弱，
    /// 他的伴侣关系就被**拆掉**（清理规则会认为"伴侣不再合格"），
    /// 而拆掉之后要重新配对。实测 40 人世界里配对数从 16 对在 5 天内崩到 3 对，
    /// 出生数恒为 0 —— 看起来像"概率太低"，实际是"关系被反复拆散"。
    ///
    /// 任务书的公式里 `HealthFactor` 是一个**乘性因子**，不是门。
    /// 改成因子之后语义才对：**虚弱让生育概率变低，但不该让两个人不再是伴侣。**
    /// 这类"把因子误做成门"的错误在数值系统里很常见，而它的表现是
    /// "某个机制完全失效"，不是"数值偏小"。
    /// </summary>
    private bool IsEligible(int slot)
    {
        LifeStage stage = _store.LifeStageOf(slot);
        return stage != LifeStage.Child && stage != LifeStage.Dead;
    }

    /// <summary>
    /// 食物因子：人均可支配食物 / 目标值，夹到 [0,1]。
    ///
    /// 统计口径刻意选"**当下真的能吃到的**"：共享库存 + 地面物资堆 + 各人随身。
    /// 不把"地里还没长出来的"算进来 —— 那是"潜力"而不是"当下的口粮"，
    /// 而生育决策在直觉上依赖的是后者。
    /// </summary>
    private float ComputeFoodFactor()
    {
        int population = _store.LiveCount;
        if (population <= 0) { return 0f; }

        float total = _sim.Storage.GrandTotalOf(ResourceKind.Food, _sim.Buildings.Capacity)
            + _sim.GroundStocks.TotalOf(ResourceKind.Food);

        foreach (int slot in _store.AliveSlots())
        {
            total += _store.InventoryOf(slot, ResourceKind.Food);
        }

        float perCapita = total / population;
        if (_config.FoodPerCapitaTarget <= 0f) { return 1f; }
        return SimMath.Clamp01(perCapita / _config.FoodPerCapitaTarget);
    }

    /// <summary>
    /// 人口压力 [0,1+]：人口 / 床位承载量。
    ///
    /// 用床位而不是地图面积是有意的（见 <see cref="TickDay"/> 的注释）：
    /// 它让"盖房子"成为增长的**前提**，而不是增长的装饰。
    /// 没有床位时压力取一个大于 1 的值 ⇒ 压力因子被压到很低，
    /// 但**不是 0** —— 因为"完全不能生"已经由住房门表达了，
    /// 这里再叠一个 0 会让两套机制重复，出问题时无法区分是哪一套在起作用。
    /// </summary>
    private float ComputePopulationPressure()
    {
        int population = _store.LiveCount;
        int beds = _sim.Buildings.TotalBeds;
        if (beds <= 0) { return 1.5f; }
        return (float)population / beds;
    }

    /// <summary>
    /// 真正生一个孩子：性格从双亲遗传（带突变）、消耗母体健康、占用床位、记事件。
    ///
    /// # 为什么床位必须在**创建孩子之前**就定下来（一次真实踩到的坑）
    ///
    /// 早先的写法是：先 `AddChild`，再找床；找不到就 `MarkDead` 把孩子收回。
    /// 那个"收回"看起来无害，实际上会**烧掉一个槽位代次**（`MarkDead` 让
    /// `_generation[slot]++`），而代次进状态摘要 ——
    /// 于是"读档续跑"与"直接跑"只要在某一刻的床位记账上有一点点差异，
    /// 就会在槽位复用上分叉：实测表现为某个槽位的代次 1 vs 0、
    /// 以及该个体后续所有字段都对不上。
    ///
    /// 更要紧的是它**在语义上也是错的**：一个"从出生到死亡"的过程不应该被
    /// 一个记账分支触发。所以现在是事务性的 —— 先确认有床，再创建。
    /// 这与 `BuildingSystem.TryStartBuilding` 的"要么都成功、要么什么都不变"
    /// 是同一条原则。
    /// </summary>
    private bool TryBirth(int mother, int father, long tick)
    {
        // ---- 1) 先把床位定下来（事务的第一半）----
        int motherDwelling = _store.DwellingOf(mother);
        int dwelling = -1;

        if (motherDwelling >= 0 && _sim.Buildings.HasFreeBed(motherDwelling)) { dwelling = motherDwelling; }
        else { dwelling = _sim.BuildingSystem.FindHouseWithFreeBed(_store.XOf(mother), _store.YOf(mother)); }

        if (dwelling < 0) { return false; }

        // ---- 2) 再创建孩子 ----
        // 孩子出生在母亲所在格（而不是"配对中点"）——
        // 后者会在两人相隔很远时把孩子生到无人处，看起来像凭空出现。
        int x = _store.XOf(mother);
        int y = _store.YOf(mother);

        Personality personality = Inherit(_store.PersonalityOf(mother), _store.PersonalityOf(father));
        AgentRef child = _store.AddChild(x, y, mother, father, personality, tick);
        if (child.IsNone) { return false; }

        // ---- 3) 占床（此时一定还有位置：上面刚确认过，且这一步之间没有别人能插手）----
        if (!_sim.Buildings.TryOccupyBedOf(dwelling))
        {
            // 走到这里说明"有空床"的判定与"占用成功"不一致 —— 那是记账 bug，
            // 不应该被静默吞掉。把它记成事件，让它在报告与检查器里可见，
            // 而不是"安静地少生一个孩子"。
            _sim.Events.Record(tick, History.WorldEventType.Birth,
                "出生失败：床位记账不一致（建筑槽 " + dwelling + "）",
                History.EventImportance.Critical, new Int2(x, y), mother, dwelling, "床位记账不一致");
            return false;
        }

        _store.SetDwelling(child.Slot, dwelling);
        if (_store.DwellingOf(mother) < 0) { _store.SetDwelling(mother, dwelling); }
        if (_store.DwellingOf(father) < 0) { _store.SetDwelling(father, dwelling); }

        // 双亲各自记一次生育（间隔门是**按个体**记的，因此两边都会进入冷却）
        _store.RecordBirth(mother, tick);
        _store.RecordBirth(father, tick);

        // 母体付出健康代价 —— 让"连续生育"不是免费的
        if (_config.HealthCostPerBirth > 0f)
        {
            _store.SetHealth(mother, _store.HealthOf(mother) - _config.HealthCostPerBirth);
        }

        TotalBirths++;
        _sim.Stats.RecordBirth();

        string motherName = _store.NameOrOverride(mother);
        string fatherName = _store.NameOrOverride(father);
        string childName = _store.NameOrOverride(child.Slot);

        LastBirthDetail = childName + " 出生（" + motherName + " 与 " + fatherName + "）";

        _sim.Events.Record(
            tick,
            History.WorldEventType.Birth,
            LastBirthDetail,
            History.EventImportance.Important,
            new Int2(x, y),
            child.Slot,
            -1,
            "出生");

        _sim.Events.Record(
            tick,
            History.WorldEventType.AgentBorn,
            childName + " 出生于 " + new Int2(x, y),
            History.EventImportance.Normal,
            new Int2(x, y),
            child.Slot,
            -1,
            "出生");

        BirthsThisDay++;
        return true;
    }

    /// <summary>
    /// 性格遗传：双亲均值 + 对称突变。
    ///
    /// 突变是**对称**的（不是"只会更温和"），否则几代之后性格会整体漂移到一个极端。
    /// 这是"遗传 + 变异"里最容易被忽略的一点：单向变异看起来只是数值，实际是趋势。
    /// </summary>
    private Personality Inherit(Personality a, Personality b)
    {
        DeterministicRandom rng = _sim.Random.Get(RngStream.Agents);
        float scale = _config.MutationScale;

        return new Personality
        {
            Aggression = Mutate((a.Aggression + b.Aggression) * 0.5f, scale, rng),
            Greed = Mutate((a.Greed + b.Greed) * 0.5f, scale, rng),
            Kindness = Mutate((a.Kindness + b.Kindness) * 0.5f, scale, rng),
            Bravery = Mutate((a.Bravery + b.Bravery) * 0.5f, scale, rng),
            Industriousness = Mutate((a.Industriousness + b.Industriousness) * 0.5f, scale, rng),
            Sociability = Mutate((a.Sociability + b.Sociability) * 0.5f, scale, rng),
        };
    }

    private static float Mutate(float value, float scale, DeterministicRandom rng)
    {
        if (scale <= 0f) { return SimMath.Clamp01(value); }
        double delta = (rng.NextDouble() * 2.0 - 1.0) * scale;
        return SimMath.Clamp01((float)(value + delta));
    }
}
