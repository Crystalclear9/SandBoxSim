using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Systems;

/// <summary>
/// 聚落（M7）：当一群人**持续**在相近的地方生活、共用仓库与住房时，
/// 这片地方就从"一堆人碰巧住在一起"变成"一个聚落"。
///
/// # 为什么聚落必须是**涌现**的，而不是被放置的
///
/// 任务书第 29 / 60 条的要求是：玩家不能"造一个村子"。
/// 他能做的只是把条件摆好（地形、资源、气候、规则），然后看会不会长出聚落。
///
/// 因此这里的形成条件完全是可观测的行为统计：
/// **N 个人在半径 R 内持续共处 D 天，且共享至少一所住房与一座仓库**。
/// 没有任何一步是"玩家点了一下"。
///
/// # 为什么必须有滞回（hysteresis）
///
/// 如果"成立"与"解散"用同一个阈值，那么人口在阈值附近抖动时，
/// 聚落会**每帧成立又解散**。玩家看到的将是闪烁的标签和一个疯狂增长的事件列表，
/// 而真正的历史（"这个村子是什么时候形成的"）完全丢失。
///
/// 所以成立阈值高于解散阈值（默认 `FoundPeople=6` / `DissolvePeople=3`），
/// 两者之间是"保持不变"的过渡带。这不是为了稳定而打的补丁 ——
/// **它本身就是"一个聚落有惯性"这条事实的建模**。
///
/// # 等级由什么决定
///
/// `Camp → Village → Town → City` 只看**人口**与**建筑数**，
/// 不看任何"发展点数"。等级是观察者给这片地方贴的标签，
/// 而不是一个被模拟单独驱动的变量 —— 这样它永远不会与实际情况脱节。
/// </summary>
public sealed class SettlementStore : ISimEntitySet
{
    /// <summary>单个聚落的记录。</summary>
    public struct Settlement
    {
        public int Id;
        public int CenterX;
        public int CenterY;
        public SettlementTier Tier;
        public int Population;
        public int Houses;
        public int Storages;

        /// <summary>最近一次有人活动的 tick（用于判断是否荒废）。</summary>
        public long LastActiveTick;

        /// <summary>成立的 tick（历史里的"建村时间"）。</summary>
        public long FoundedTick;

        /// <summary>是否已解散（解散后保留一条记录，供历史与报告使用）。</summary>
        public bool Dissolved;
    }

    private readonly Simulation _sim;
    private readonly SettlementConfig _config;

    // 用"数组 + 活跃数"而不是 List<Settlement>：与项目其它 store 一致，
    // 便于确定性遍历（按 Id 升序）与容量管理。
    private Settlement[] _items = new Settlement[8];
    private int _count;

    private int _nextId = 1;

    /// <summary>当前成立的聚落数（不含已解散）。</summary>
    public int ActiveCount { get; private set; }

    /// <summary>累计成立过的聚落数（历史统计）。</summary>
    public int TotalFounded { get; private set; }

    /// <summary>累计解散过的聚落数。</summary>
    public int TotalDissolved { get; private set; }

    /// <summary>上一次评估时的"候选中心"（逐日更新，用于判断持续性）。</summary>
    private int _candidateCenterX = -1;
    private int _candidateCenterY = -1;
    private int _candidateDays;
    private int _candidatePeople;

    /// <summary>`ISimEntitySet`：实体数量。</summary>
    public int EntityCount => _count;

    public SettlementStore(Simulation sim)
    {
        _sim = sim ?? throw new System.ArgumentNullException(nameof(sim));
        _config = sim.Config.Settlement;
    }

    /// <summary>按 Id 升序读取全部记录（含已解散）。</summary>
    public Settlement At(int index) => _items[index];

    /// <summary>清空（重置世界）。</summary>
    public void Reset()
    {
        for (int i = 0; i < _count; i++) { _items[i] = default; }
        _count = 0;
        ActiveCount = 0;
        TotalFounded = 0;
        TotalDissolved = 0;
        _nextId = 1;
        _candidateCenterX = -1;
        _candidateCenterY = -1;
        _candidateDays = 0;
        _candidatePeople = 0;
    }

    /// <summary>
    /// 逐日评估（由 `Simulation.TickDay` 调用）。
    ///
    /// 它做三件事，顺序固定：
    ///   1. 统计当前"候选中心"（人口最密集处的质心）与共处人数；
    ///   2. 判断是否成立新聚落 / 是否该解散已有聚落（带滞回）；
    ///   3. 更新各聚落的人口、建筑数与等级。
    /// </summary>
    public void TickDay(long tick)
    {
        if (!_config.Enabled) { return; }

        int population = _sim.Agents.LiveCount;
        if (population == 0)
        {
            DissolveAll(tick);
            return;
        }

        // ---- 1) 候选中心：取全部存活个体的质心 ----
        //
        // 用"全体质心"而不是聚类：后者需要一个聚类算法与它的各种参数，
        // 而 M7 的目标是**先把"有没有聚落"这件事做出来**。
        // 全体质心足以区分"大家住在一起"与"散在各地"这两种状态。
        int sumX = 0;
        int sumY = 0;
        int counted = 0;
        int[] slots = _sim.Agents.LiveSlotsRaw(out int liveCount);
        for (int k = 0; k < liveCount; k++)
        {
            sumX += _sim.Agents.XOf(slots[k]);
            sumY += _sim.Agents.YOf(slots[k]);
            counted++;
        }
        if (counted == 0) { return; }

        int centerX = sumX / counted;
        int centerY = sumY / counted;

        // ---- 2) 共处人数：中心半径内有多少人 ----
        float radiusSq = _config.ClusterRadius * _config.ClusterRadius;
        int nearby = 0;
        for (int k = 0; k < liveCount; k++)
        {
            float dx = _sim.Agents.XOf(slots[k]) - centerX;
            float dy = _sim.Agents.YOf(slots[k]) - centerY;
            if ((dx * dx) + (dy * dy) <= radiusSq) { nearby++; }
        }

        // 中心明显移动 ⇒ 持续性计数清零（"大家一起搬走了"不该算作同一个聚落）
        if (_candidateCenterX >= 0)
        {
            int shift = System.Math.Max(
                System.Math.Abs(centerX - _candidateCenterX),
                System.Math.Abs(centerY - _candidateCenterY));
            if (shift > _config.CenterDriftTolerance) { _candidateDays = 0; }
        }

        _candidateCenterX = centerX;
        _candidateCenterY = centerY;
        _candidatePeople = nearby;

        // ---- 3) 共享设施：这是"聚落"与"一群人"的分界 ----
        //
        // 光住得近不算聚落 —— 那只是同路。**共用的住房与仓库**才是"这个地方"的实体。
        int houses = CountCompleted(BuildingKind.House, centerX, centerY, _config.FacilityRadius);
        int storages = CountCompleted(BuildingKind.Storage, centerX, centerY, _config.FacilityRadius);

        bool hasFacilities = houses >= _config.MinHouses && storages >= _config.MinStorages;

        Settlement? active = FindActive();

        if (hasFacilities && nearby >= _config.FoundPeople)
        {
            _candidateDays++;
        }
        else
        {
            // 条件不满足就缓慢衰减，而不是立刻清零 ——
            // 一次采集远征不该把"已经住了 20 天"的历史抹掉。
            if (_candidateDays > 0) { _candidateDays--; }
        }

        if (!active.HasValue)
        {
            if (_candidateDays >= _config.FoundDays)
            {
                Found(tick, centerX, centerY, nearby, houses, storages);
            }
        }
        else
        {
            Settlement s = active.Value;

            // 滞回：**解散阈值低于成立阈值**，两者之间保持不变。
            if (nearby <= _config.DissolvePeople)
            {
                Dissolve(s.Id, tick);
            }
            else
            {
                s.Population = nearby;
                s.Houses = houses;
                s.Storages = storages;
                s.LastActiveTick = tick;
                s.Tier = TierOf(s.Population, s.Houses);

                // 中心跟着人走（聚落会漂移，这是真实的行为）
                s.CenterX = centerX;
                s.CenterY = centerY;
                Write(s);
            }
        }
    }

    /// <summary>由人口与建筑数推导等级（观察者的标签，不是独立驱动的变量）。</summary>
    private SettlementTier TierOf(int population, int houses)
    {
        if (population >= _config.CityPopulation && houses >= _config.CityHouses) { return SettlementTier.City; }
        if (population >= _config.TownPopulation && houses >= _config.TownHouses) { return SettlementTier.Town; }
        if (population >= _config.VillagePopulation) { return SettlementTier.Village; }
        return SettlementTier.Camp;
    }

    private void Found(long tick, int x, int y, int population, int houses, int storages)
    {
        EnsureCapacity(_count + 1);

        _items[_count] = new Settlement
        {
            Id = _nextId++,
            CenterX = x,
            CenterY = y,
            Tier = TierOf(population, houses),
            Population = population,
            Houses = houses,
            Storages = storages,
            FoundedTick = tick,
            LastActiveTick = tick,
            Dissolved = false,
        };
        _count++;
        ActiveCount++;
        TotalFounded++;
        _candidateDays = 0;

        _sim.Events.Record(
            tick,
            History.WorldEventType.SettlementFounded,
            "聚落形成 @" + new Int2(x, y) + "（" + population + " 人，共享 " + houses + " 住房 / " + storages + " 仓库）",
            History.EventImportance.Important,
            new Int2(x, y),
            -1,
            -1,
            "持续共处 " + _config.FoundDays + " 天");
    }

    private void Dissolve(int id, long tick)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_items[i].Id != id || _items[i].Dissolved) { continue; }

            _items[i].Dissolved = true;
            ActiveCount--;
            TotalDissolved++;

            _sim.Events.Record(
                tick,
                History.WorldEventType.SettlementFounded,
                "聚落解散 @" + new Int2(_items[i].CenterX, _items[i].CenterY),
                History.EventImportance.Normal,
                new Int2(_items[i].CenterX, _items[i].CenterY),
                -1,
                -1,
                "人口降到 " + _config.DissolvePeople + " 人以下");
            return;
        }
    }

    private void DissolveAll(long tick)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_items[i].Dissolved) { continue; }
            _items[i].Dissolved = true;
            ActiveCount--;
            TotalDissolved++;
        }
    }

    private Settlement? FindActive()
    {
        for (int i = 0; i < _count; i++)
        {
            if (!_items[i].Dissolved) { return _items[i]; }
        }
        return null;
    }

    private void Write(Settlement value)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_items[i].Id == value.Id) { _items[i] = value; return; }
        }
    }

    /// <summary>统计中心附近某种**已完工**建筑的数量。</summary>
    private int CountCompleted(BuildingKind kind, int centerX, int centerY, float radius)
    {
        BuildingStore buildings = _sim.Buildings;
        float radiusSq = radius * radius;
        int count = 0;

        for (int i = 0; i < buildings.Capacity; i++)
        {
            if (!buildings.IsAlive(i)) { continue; }
            if (buildings.KindOf(i) != kind) { continue; }
            if (buildings.StateOf(i) != BuildingState.Complete) { continue; }

            Int2 p = buildings.PositionOf(i);
            float dx = p.X - centerX;
            float dy = p.Y - centerY;
            if ((dx * dx) + (dy * dy) <= radiusSq) { count++; }
        }

        return count;
    }

    private void EnsureCapacity(int needed)
    {
        if (needed <= _items.Length) { return; }
        int size = _items.Length;
        while (size < needed) { size *= 2; }
        System.Array.Resize(ref _items, size);
    }

    // ---------------------------------------------------------------------
    // 存档与摘要
    // ---------------------------------------------------------------------

    /// <summary>只读遍历全部记录（存档与报告用；顺序为数组序，即创建序）。</summary>
    public System.Collections.Generic.IEnumerable<Settlement> All()
    {
        for (int i = 0; i < _count; i++) { yield return _items[i]; }
    }

    /// <summary>读档：直接写入一条记录（不走 Found，避免"恢复"被当成一次成立）。</summary>
    public void Restore(int id, int x, int y, SettlementTier tier, int population,
        int houses, int storages, long lastActiveTick, long foundedTick, bool dissolved)
    {
        EnsureCapacity(_count + 1);
        _items[_count++] = new Settlement
        {
            Id = id,
            CenterX = x,
            CenterY = y,
            Tier = tier,
            Population = population,
            Houses = houses,
            Storages = storages,
            LastActiveTick = lastActiveTick,
            FoundedTick = foundedTick,
            Dissolved = dissolved,
        };
        if (!dissolved) { ActiveCount++; }
        if (id >= _nextId) { _nextId = id + 1; }
    }

    /// <summary>读档：恢复"候选持续性"计数（它会影响未来会不会成立聚落）。</summary>
    public void RestoreCandidate(int centerX, int centerY, int days, int people)
    {
        _candidateCenterX = centerX;
        _candidateCenterY = centerY;
        _candidateDays = days;
        _candidatePeople = people;
    }

    public void RestoreCounters(int totalFounded, int totalDissolved)
    {
        TotalFounded = totalFounded;
        TotalDissolved = totalDissolved;
    }

    public int CandidateDays => _candidateDays;
    public int CandidatePeople => _candidatePeople;
    public int CandidateCenterX => _candidateCenterX;
    public int CandidateCenterY => _candidateCenterY;

    /// <summary>
    /// 状态摘要。
    ///
    /// 进摘要的判据仍然是"会不会影响未来的行为"：
    ///   * **候选持续性计数会** —— 它决定"再过几天会不会成立聚落"，是真正的状态；
    ///   * 已解散记录**不会** —— 它们只是历史，保留是为了报告与 UI；
    ///   * `LastActiveTick` **不会** —— 目前只用于展示。
    ///
    /// 换句话说：这里刻意**不**把历史记录哈希进来。
    /// 一条解散了 100 天的记录不应该影响今天的结果，否则"历史"就变成了状态。
    /// </summary>
    public ulong HashInto(ulong hash)
    {
        hash = Hash64.Combine(hash, _candidateDays);
        hash = Hash64.Combine(hash, _candidatePeople);
        hash = Hash64.Combine(hash, _candidateCenterX);
        hash = Hash64.Combine(hash, _candidateCenterY);

        for (int i = 0; i < _count; i++)
        {
            if (_items[i].Dissolved) { continue; }
            hash = Hash64.Combine(hash, _items[i].Id);
            hash = Hash64.Combine(hash, _items[i].CenterX);
            hash = Hash64.Combine(hash, _items[i].CenterY);
            hash = Hash64.Combine(hash, (int)_items[i].Tier);
            hash = Hash64.Combine(hash, _items[i].Population);
            hash = Hash64.Combine(hash, _items[i].Houses);
            hash = Hash64.Combine(hash, _items[i].Storages);
            hash = Hash64.Combine(hash, _items[i].FoundedTick);
        }

        return hash;
    }
}

/// <summary>聚落等级（由人口与建筑数推导）。</summary>
public enum SettlementTier : byte
{
    Camp = 0,
    Village = 1,
    Town = 2,
    City = 3,
}

/// <summary>聚落参数（M7）。</summary>
public sealed class SettlementConfig
{
    public bool Enabled = true;

    /// <summary>候选中心附近多少人算"聚在一起"。</summary>
    public float ClusterRadius = 24f;

    /// <summary>共享设施必须在这个半径内才算"这个聚落的"。</summary>
    public float FacilityRadius = 28f;

    /// <summary>成立聚落需要附近至少几所住房。</summary>
    public int MinHouses = 2;

    /// <summary>成立聚落需要附近至少几座仓库（"共用"的硬证据）。</summary>
    public int MinStorages = 1;

    /// <summary>成立所需的人数。</summary>
    public int FoundPeople = 6;

    /// <summary>成立所需的持续天数（"持续共处"的门槛）。</summary>
    public int FoundDays = 5;

    /// <summary>解散阈值（**必须低于成立阈值**，两者之间构成滞回带）。</summary>
    public int DissolvePeople = 3;

    /// <summary>中心漂移超过多少格就重算持续性（"整体搬家"不算同一个聚落）。</summary>
    public int CenterDriftTolerance = 8;

    // 等级门槛
    public int VillagePopulation = 6;
    public int TownPopulation = 20;
    public int TownHouses = 8;
    public int CityPopulation = 45;
    public int CityHouses = 18;
}
