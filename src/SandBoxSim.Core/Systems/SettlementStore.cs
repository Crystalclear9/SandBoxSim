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

    public struct Candidate
    {
        public int X;
        public int Y;
        public int Days;
        public int People;
        public long LastTick;
    }
    private System.Collections.Generic.List<Candidate> _candidates = new System.Collections.Generic.List<Candidate>();
    public System.Collections.Generic.IEnumerable<Candidate> Candidates => _candidates;

    /// <summary>`ISimEntitySet`：实体数量。</summary>
    public int EntityCount => _count;

    // ---------------------------------------------------------------------
    // 观测（不进摘要：它们只是"今天的快照"，不参与任何判定）
    //
    // 加这几个字段是因为批量验收里出现了"人口 40+ 却从不形成聚落"的种子，
    // 而要区分"人群被切成碎块"与"没有仓库"这两种原因，光看聚落数是分不出来的。
    // ---------------------------------------------------------------------

    /// <summary>今天识别出的"人群"个数。</summary>
    public int LastClusterCount { get; private set; }

    /// <summary>今天最大那个人群的人数。</summary>
    public int LargestClusterPeople { get; private set; }

    /// <summary>最大人群附近的已完工住房数。</summary>
    public int LargestClusterHouses { get; private set; }

    /// <summary>最大人群附近的已完工仓库数。</summary>
    public int LargestClusterStorages { get; private set; }

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
        _candidates.Clear();
        LastClusterCount = LargestClusterPeople = LargestClusterHouses = LargestClusterStorages = 0;
    }

    /// <summary>
    /// 逐日评估共享设施周围的实际居民群，并独立累计各候选的连续共处天数。
    /// 稳定设施锚点避免全图质心落在无人处，也避免附近活动误算为整体迁离。
    /// 人口达到原成立条件时建立记录，降至解散阈值时保留解散历史。
    /// </summary>
    public void TickDay(long tick)
    {
        if (!_config.Enabled) { return; }

        if (_sim.Agents.LiveCount == 0)
        {
            DissolveAll(tick);
            _candidates.Clear();
            UpdateCandidateProjection();
            LastClusterCount = LargestClusterPeople = LargestClusterHouses = LargestClusterStorages = 0;
            return;
        }

        // 方法名不能叫 Cluster()：那会与类型 Cluster 在同一作用域里撞名（CS0102）。
        var clusters = BuildClusters();

        LastClusterCount = clusters.Count;
        LargestClusterPeople = 0;
        LargestClusterHouses = 0;
        LargestClusterStorages = 0;

        for (int i = 0; i < clusters.Count; i++)
        {
            if (clusters[i].People > LargestClusterPeople)
            {
                LargestClusterPeople = clusters[i].People;
                LargestClusterHouses = CountCompleted(
                    BuildingKind.House, clusters[i].CenterX, clusters[i].CenterY, _config.FacilityRadius);
                LargestClusterStorages = CountCompleted(
                    BuildingKind.Storage, clusters[i].CenterX, clusters[i].CenterY, _config.FacilityRadius);
            }
        }
        var nextCandidates = new System.Collections.Generic.List<Candidate>();
        var candidateMatched = new bool[_candidates.Count];
        var activeMatched = new bool[_count];
        for (int i = 0; i < clusters.Count; i++)
        { Evaluate(tick, clusters[i], nextCandidates, candidateMatched, activeMatched); }
        for (int i = 0; i < activeMatched.Length; i++)
        {
            if (_items[i].Dissolved || activeMatched[i]) { continue; }
            if (CountResidentsNear(_items[i].CenterX, _items[i].CenterY) <= _config.DissolvePeople)
            { Dissolve(_items[i].Id, tick); }
        }
        _candidates = nextCandidates;
        UpdateCandidateProjection();
    }

    /// <summary>一个"人群"：空间上连成一片的个体。</summary>
    private struct Cluster
    {
        public int CenterX;
        public int CenterY;
        public int People;
    }

    /// <summary>
    /// 已完工仓库提供稳定的共享设施锚点；每人只属于半径内最近的锚点。
    /// 近邻设施合并，防止同一居民被多座仓库重复计数。
    /// </summary>
    private System.Collections.Generic.List<Cluster> BuildClusters()
    {
        var anchors = new System.Collections.Generic.List<Cluster>();
        float radiusSq = _config.ClusterRadius * _config.ClusterRadius;
        // 活跃身份优先，随后按建筑槽位选新锚点，保持存档前后顺序一致。
        for (int i = 0; i < _count; i++)
            if (!_items[i].Dissolved)
                anchors.Add(new Cluster { CenterX = _items[i].CenterX, CenterY = _items[i].CenterY });
        for (int index = 0; index < _sim.Buildings.Capacity; index++)
        {
            if (_sim.Buildings.KindOf(index) != BuildingKind.Storage
                || _sim.Buildings.StateOf(index) != BuildingState.Complete) { continue; }
            int x = _sim.Buildings.XOf(index);
            int y = _sim.Buildings.YOf(index);
            bool covered = false;
            foreach (Cluster c in anchors)
            {
                long dx = x - c.CenterX, dy = y - c.CenterY;
                if (dx * dx + dy * dy <= radiusSq) { covered = true; break; }
            }
            if (!covered) { anchors.Add(new Cluster { CenterX = x, CenterY = y }); }
        }
        var unassigned = new System.Collections.Generic.List<Cluster>();
        int[] slots = _sim.Agents.LiveSlotsRaw(out int liveCount);
        for (int k = 0; k < liveCount; k++)
        {
            int x = _sim.Agents.XOf(slots[k]), y = _sim.Agents.YOf(slots[k]);
            int nearest = -1;
            double best = double.MaxValue;
            for (int i = 0; i < anchors.Count; i++)
            {
                long dx = x - anchors[i].CenterX, dy = y - anchors[i].CenterY;
                double distance = dx * dx + dy * dy;
                if (distance <= radiusSq && distance < best) { nearest = i; best = distance; }
            }
            if (nearest >= 0)
            {
                Cluster c = anchors[nearest]; c.People++; anchors[nearest] = c;
            }
            else
            {
                // 无共享设施的居民只参与观察，不以远处设施冒充共同生活。
                int group = -1;
                for (int i = 0; i < unassigned.Count; i++)
                    if (unassigned[i].CenterX == x && unassigned[i].CenterY == y) { group = i; break; }
                if (group < 0) { unassigned.Add(new Cluster { CenterX = x, CenterY = y, People = 1 }); }
                else { Cluster c = unassigned[group]; c.People++; unassigned[group] = c; }
            }
        }
        var result = new System.Collections.Generic.List<Cluster>();
        foreach (Cluster c in anchors) { if (c.People > 0) { result.Add(c); } }
        result.AddRange(unassigned);
        return result;
    }
    /// <summary>评估一个人群：匹配已有聚落，或按条件成立新的。</summary>
    private void Evaluate(long tick, Cluster c, System.Collections.Generic.List<Candidate> nextCandidates,
        bool[] candidateMatched, bool[] activeMatched)
    {
        int houses = CountCompleted(BuildingKind.House, c.CenterX, c.CenterY, _config.FacilityRadius);
        int storages = CountCompleted(BuildingKind.Storage, c.CenterX, c.CenterY, _config.FacilityRadius);
        bool hasFacilities = houses >= _config.MinHouses && storages >= _config.MinStorages;

        int activeIndex = FindActiveNear(c.CenterX, c.CenterY, _config.MatchRadius, activeMatched);

        if (activeIndex >= 0)
        {
            activeMatched[activeIndex] = true;
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
        int previous = -1;
        long bestDistance = long.MaxValue;
        for (int i = 0; i < _candidates.Count; i++)
        {
            Candidate candidate = _candidates[i];
            if (candidateMatched[i] || tick - candidate.LastTick != _sim.World.Calendar.TicksPerDay) { continue; }
            int dx = c.CenterX - candidate.X;
            int dy = c.CenterY - candidate.Y;
            if (System.Math.Abs(dx) > _config.CenterDriftTolerance || System.Math.Abs(dy) > _config.CenterDriftTolerance) { continue; }
            long distance = (long)dx * dx + (long)dy * dy;
            if (distance >= bestDistance) { continue; }
            previous = i;
            bestDistance = distance;
        }
        int days = previous < 0 ? 1 : _candidates[previous].Days + 1;
        if (previous >= 0) { candidateMatched[previous] = true; }
        if (days >= _config.FoundDays)
        {
            Found(tick, c.CenterX, c.CenterY, c.People, houses, storages);
        }
        else { nextCandidates.Add(new Candidate { X = c.CenterX, Y = c.CenterY, Days = days, People = c.People, LastTick = tick }); }
    }

    /// <summary>找出中心附近的活跃聚落（用于跨天保持同一身份）。</summary>
    private int FindActiveNear(int x, int y, int radius, bool[] matched)
    {
        int best = -1;
        int bestDist = int.MaxValue;
        int radiusSq = radius * radius;

        for (int i = 0; i < matched.Length; i++)
        {
            if (_items[i].Dissolved || matched[i]) { continue; }

            int dx = _items[i].CenterX - x;
            int dy = _items[i].CenterY - y;
            int distSq = (dx * dx) + (dy * dy);
            if (distSq > radiusSq || distSq >= bestDist) { continue; }

            bestDist = distSq;
            best = i;
        }

        return best;
    }

    /// <summary>将最持久的候选投影到旧的单候选观察字段。</summary>
    private void UpdateCandidateProjection()
    {
        _candidateCenterX = _candidateCenterY = -1;
        _candidateDays = _candidatePeople = 0;
        foreach (Candidate candidate in _candidates)
        {
            if (candidate.Days < _candidateDays || (candidate.Days == _candidateDays && candidate.People <= _candidatePeople)) { continue; }
            _candidateCenterX = candidate.X;
            _candidateCenterY = candidate.Y;
            _candidateDays = candidate.Days;
            _candidatePeople = candidate.People;
        }
    }

    private int CountResidentsNear(int x, int y)
    {
        int count = 0;
        float radiusSq = _config.ClusterRadius * _config.ClusterRadius;
        int[] slots = _sim.Agents.LiveSlotsRaw(out int liveCount);
        for (int i = 0; i < liveCount; i++)
        {
            int dx = _sim.Agents.XOf(slots[i]) - x;
            int dy = _sim.Agents.YOf(slots[i]) - y;
            if ((long)dx * dx + (long)dy * dy <= radiusSq) { count++; }
        }
        return count;
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
        _candidates.Clear();
        if (days > 0) { RestoreCandidateEntry(centerX, centerY, days, people, _sim.Clock); }
        else { UpdateCandidateProjection(); }
    }

    public void RestoreCandidateEntry(int x, int y, int days, int people, long lastTick)
    {
        _candidates.Add(new Candidate { X = x, Y = y, Days = days, People = people, LastTick = lastTick });
        UpdateCandidateProjection();
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
        hash = Hash64.Combine(hash, _candidates.Count);
        foreach (Candidate candidate in _candidates)
        {
            hash = Hash64.Combine(hash, candidate.X);
            hash = Hash64.Combine(hash, candidate.Y);
            hash = Hash64.Combine(hash, candidate.Days);
            hash = Hash64.Combine(hash, candidate.People);
            hash = Hash64.Combine(hash, candidate.LastTick);
        }

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
