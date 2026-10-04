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
    /// # 为什么它必须做**空间聚类**，而不是"全体质心"
    ///
    /// 第一版用的是全体存活个体的质心，再在质心附近半径内数人。
    /// 44×44 的手工场景里它工作得很好 —— 因为人本来就挤在一起。
    /// 但 20 种子批量验收（100×100、40 人、200 天）立刻暴露了它完全不行：
    ///
    /// ```text
    /// seed 70001 人口 58 聚落 1 (活跃 0) 首成 34天 最高等级 Camp
    /// ```
    ///
    /// **人口 58，聚落却解散了、等级停在 Camp。** 原因很直白：
    /// 人分散在一整张地图上时，全体质心会落在**没人的地方**，
    /// 于是"质心附近有几个人"很小，聚落被自己的解散阈值判死。
    ///
    /// 更要紧的是：全体质心**在原理上就只能产出一个聚落**。
    /// 而验收判据之一是"≥5/20 个种子有 ≥2 个聚落" ——
    /// 那条判据在这个架构下**永远不可能达成**，不是参数问题。
    ///
    /// 所以现在改成真正的空间聚类：把个体按 `ClusterRadius` 分格，
    /// 合并相邻的占用格，每个连通块算一个"人群"，逐个评估。
    /// 这同时解决三件事：解散误判、等级永远 Camp、以及**多聚落**。
    ///
    /// 顺带一条教训：**手工摆好的场景通过，不等于机制成立。**
    /// M7Tests 的 8 项断言每一条都真实通过，但它们共享同一个友好前提（人挤在一起）。
    /// 批量验收的价值就在于它**不听我摆布**。
    /// </summary>
    public void TickDay(long tick)
    {
        if (!_config.Enabled) { return; }

        if (_sim.Agents.LiveCount == 0)
        {
            DissolveAll(tick);
            return;
        }

        // 方法名不能叫 Cluster()：那会与类型 Cluster 在同一作用域里撞名（CS0102）。
        var clusters = BuildClusters();
        for (int i = 0; i < clusters.Count; i++)
        {
            Evaluate(tick, clusters[i]);
        }
    }

    /// <summary>一个"人群"：空间上连成一片的个体。</summary>
    private struct Cluster
    {
        public int CenterX;
        public int CenterY;
        public int People;
    }

    /// <summary>
    /// 把个体按 `ClusterRadius` 分格并合并相邻占用格。
    ///
    /// 用**并查集**而不是递归漫水：递归在极端情况下会栈溢出，
    /// 而并查集的合并顺序完全由格子遍历顺序决定 ⇒ 确定性由构造保证
    /// （与"靠调用方自觉"相比，这是本项目一贯偏好的做法）。
    /// </summary>
    private System.Collections.Generic.List<Cluster> BuildClusters()
    {
        var result = new System.Collections.Generic.List<Cluster>();

        int cell = _config.ClusterRadius >= 1f ? (int)_config.ClusterRadius : 1;
        int width = _sim.World.Width;
        int height = _sim.World.Height;
        int cellsX = ((width + cell - 1) / cell) + 1;
        int cellsY = ((height + cell - 1) / cell) + 1;
        int cellCount = cellsX * cellsY;

        int[] parent = new int[cellCount];
        int[] people = new int[cellCount];
        long[] sumX = new long[cellCount];
        long[] sumY = new long[cellCount];
        bool[] occupied = new bool[cellCount];
        for (int i = 0; i < cellCount; i++) { parent[i] = i; }

        int[] slots = _sim.Agents.LiveSlotsRaw(out int liveCount);
        for (int k = 0; k < liveCount; k++)
        {
            int slot = slots[k];
            int ax = _sim.Agents.XOf(slot);
            int ay = _sim.Agents.YOf(slot);
            int cx = ax / cell;
            int cy = ay / cell;
            if (cx < 0 || cy < 0 || cx >= cellsX || cy >= cellsY) { continue; }

            int idx = (cy * cellsX) + cx;
            occupied[idx] = true;
            people[idx]++;
            sumX[idx] += ax;
            sumY[idx] += ay;
        }

        // 合并 4 邻接的占用格（固定顺序：先右后下）
        for (int y = 0; y < cellsY; y++)
        {
            for (int x = 0; x < cellsX; x++)
            {
                int idx = (y * cellsX) + x;
                if (!occupied[idx]) { continue; }
                if (x + 1 < cellsX && occupied[idx + 1]) { Union(parent, idx, idx + 1); }
                if (y + 1 < cellsY && occupied[idx + cellsX]) { Union(parent, idx, idx + cellsX); }
            }
        }

        // 按根分组（格序遍历 ⇒ 输出顺序固定）
        var rootIndex = new System.Collections.Generic.Dictionary<int, int>();
        for (int i = 0; i < cellCount; i++)
        {
            if (!occupied[i]) { continue; }

            int root = Find(parent, i);
            if (!rootIndex.TryGetValue(root, out int slotIndex))
            {
                slotIndex = result.Count;
                rootIndex[root] = slotIndex;
                result.Add(new Cluster { CenterX = 0, CenterY = 0, People = 0 });
            }

            Cluster c = result[slotIndex];
            c.People += people[i];
            c.CenterX += (int)(sumX[i] / 1);   // 先累加位置和，最后再除人数
            c.CenterY += (int)(sumY[i] / 1);
            result[slotIndex] = c;
        }

        for (int i = 0; i < result.Count; i++)
        {
            Cluster c = result[i];
            if (c.People > 0) { c.CenterX /= c.People; c.CenterY /= c.People; }
            result[i] = c;
        }

        return result;
    }

    private static int Find(int[] parent, int i)
    {
        while (parent[i] != i) { i = parent[i] = parent[parent[i]]; }
        return i;
    }

    private static void Union(int[] parent, int a, int b)
    {
        int ra = Find(parent, a);
        int rb = Find(parent, b);
        if (ra == rb) { return; }
        // 较小的根作为父节点 ⇒ 与合并顺序无关的规范形式
        if (ra < rb) { parent[rb] = ra; } else { parent[ra] = rb; }
    }

    /// <summary>评估一个人群：匹配已有聚落，或按条件成立新的。</summary>
    private void Evaluate(long tick, Cluster c)
    {
        int houses = CountCompleted(BuildingKind.House, c.CenterX, c.CenterY, _config.FacilityRadius);
        int storages = CountCompleted(BuildingKind.Storage, c.CenterX, c.CenterY, _config.FacilityRadius);
        bool hasFacilities = houses >= _config.MinHouses && storages >= _config.MinStorages;

        int activeIndex = FindActiveNear(c.CenterX, c.CenterY, _config.MatchRadius);

        if (activeIndex >= 0)
        {
            Settlement s = _items[activeIndex];

            // 滞回：**解散阈值低于成立阈值**，两者之间保持不变。
            if (c.People <= _config.DissolvePeople)
            {
                Dissolve(s.Id, tick);
                return;
            }

            s.Population = c.People;
            s.Houses = houses;
            s.Storages = storages;
            s.LastActiveTick = tick;
            s.Tier = TierOf(s.Population, s.Houses);
            s.CenterX = c.CenterX;
            s.CenterY = c.CenterY;
            _items[activeIndex] = s;
            return;
        }

        // 没有可匹配的活跃聚落 ⇒ 走"候选持续性"这条路
        if (!hasFacilities || c.People < _config.FoundPeople) { return; }
        if (!NearCandidate(c.CenterX, c.CenterY)) { _candidateDays = 0; }

        _candidateDays++;
        _candidateCenterX = c.CenterX;
        _candidateCenterY = c.CenterY;
        _candidatePeople = c.People;

        if (_candidateDays >= _config.FoundDays)
        {
            Found(tick, c.CenterX, c.CenterY, c.People, houses, storages);
        }
    }

    /// <summary>找出中心附近的活跃聚落（用于跨天保持同一身份）。</summary>
    private int FindActiveNear(int x, int y, int radius)
    {
        int best = -1;
        int bestDist = int.MaxValue;
        int radiusSq = radius * radius;

        for (int i = 0; i < _count; i++)
        {
            if (_items[i].Dissolved) { continue; }

            int dx = _items[i].CenterX - x;
            int dy = _items[i].CenterY - y;
            int distSq = (dx * dx) + (dy * dy);
            if (distSq > radiusSq || distSq >= bestDist) { continue; }

            bestDist = distSq;
            best = i;
        }

        return best;
    }

    /// <summary>候选中心是否还是同一个（"整体搬家"不算同一个聚落）。</summary>
    private bool NearCandidate(int x, int y)
    {
        if (_candidateCenterX < 0) { return true; }
        return System.Math.Abs(x - _candidateCenterX) <= _config.CenterDriftTolerance
            && System.Math.Abs(y - _candidateCenterY) <= _config.CenterDriftTolerance;
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

    /// <summary>
    /// 一个"人群"与一个已有聚落相距多近算同一个（跨天保持身份）。
    ///
    /// 必须**明显小于** `ClusterRadius`：否则两个相邻人群会争抢同一个聚落身份，
    /// 表现为"聚落数忽多忽少"。取 `ClusterRadius` 的一半是个直观的起点。
    /// </summary>
    public int MatchRadius = 18;

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
