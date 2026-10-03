using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Systems;

/// <summary>
/// 迁移系统（M2；第 54 节的简化版）。
///
/// M2 只做"个体离开原住地"，不做"建立新聚落"（那是 M7）。
/// 但即使只是"离开"，它也已经构成一个可观察的涌现现象：
///
///     一片地方被采光 → 本地资源紧张 → 有人开始往外走 → 人口在空间上重新分布
///
/// 效用公式（每一项都必须能在检查器里被单独看到）：
///
///     U_migrate = ScarcityWeight      × 本地资源紧张度
///               + HungerWeight        × 自身饥饿
///               + PopulationWeight    × 人口拥挤度
///               + DangerWeight        × 危险度（M5 接入火灾/冲突）
///               + OpportunityWeight   × 附近的机会（别处比这里好多少）
///               − HomeAttachmentWeight× 对原住地的依恋
///
/// 关键设计：**"机会"不是随机的**，而是真的沿八个方向探出去看别处的 chunk 统计
/// （第 44 条：状态驱动而不是随机）。因此"迁到哪儿"是可以解释的 ——
/// 玩家抽掉某处的资源，迁移方向就会跟着变。
/// </summary>
public sealed class MigrationSystem
{
    private readonly Simulation _sim;
    private readonly AgentStore _store;
    private readonly MigrationConfig _config;

    /// <summary>每个个体的迁移冷却到期 tick（用数组避免字典迭代顺序问题）。</summary>
    private long[] _cooldownUntil = System.Array.Empty<long>();

    /// <summary>累计迁移次数。</summary>
    public int TotalMigrations { get; private set; }

    /// <summary>本 tick 发起的迁移次数。</summary>
    public int MigrationsThisTick { get; private set; }

    /// <summary>最近一次评估的效用（调试/UI 用；-1 表示还没评估过）。</summary>
    public float LastEvaluatedUtility { get; private set; } = -1f;

    /// <summary>最近一次评估的分解（UI 可显示"为什么他想走"）。</summary>
    public string LastEvaluationDetail { get; private set; } = string.Empty;

    public MigrationSystem(Simulation sim, AgentStore store)
    {
        _sim = sim ?? throw new System.ArgumentNullException(nameof(sim));
        _store = store ?? throw new System.ArgumentNullException(nameof(store));
        _config = sim.Config.Migration;
        EnsureCapacity(store.Capacity);
    }

    private void EnsureCapacity(int capacity)
    {
        if (_cooldownUntil.Length >= capacity) { return; }
        System.Array.Resize(ref _cooldownUntil, capacity);
    }

    /// <summary>
    /// 每天评估一次全部个体是否该迁移。
    /// 为什么是"每天"而不是"每次决策"：迁移是一个**人生决定**，
    /// 不该在"刚想砍树又想去喝水"的粒度上被重新考虑 —— 否则会出现人人反复横跳。
    /// </summary>
    public void TickDay(long tick)
    {
        MigrationsThisTick = 0;
        EnsureCapacity(_store.Capacity);

        World world = _sim.World;
        DeterministicRandom rng = _sim.Random.Get(RngStream.Agents);
        int ticksPerDay = world.Calendar.TicksPerDay;
        long cooldownTicks = (long)(_config.CooldownDays * ticksPerDay);

        for (int slot = 0; slot < _store.Capacity; slot++)
        {
            if (!_store.IsSlotAlive(slot)) { continue; }
            if (tick < _cooldownUntil[slot]) { continue; }

            // 只有成年人会独立迁移（儿童跟着家庭走，M6 接入）
            if (_store.LifeStageOf(slot) == LifeStage.Child) { continue; }

            float utility = Evaluate(slot, out Int2 destination, out string detail);
            LastEvaluatedUtility = utility;
            LastEvaluationDetail = detail;

            if (utility < _config.Threshold) { continue; }
            if (destination.X < 0) { continue; }

            if (TryMigrate(slot, destination, tick))
            {
                _cooldownUntil[slot] = tick + cooldownTicks;
                MigrationsThisTick++;
                TotalMigrations++;
            }
        }
    }

    /// <summary>
    /// 计算某个体的迁移效用，并选出最佳目的地。
    /// 返回效用 [0,1]；<paramref name="destination"/> 为 (-1,-1) 表示没有可去之处。
    /// </summary>
    public float Evaluate(int slot, out Int2 destination, out string detail)
    {
        destination = new Int2(-1, -1);
        detail = string.Empty;

        World world = _sim.World;
        int x = _store.XOf(slot);
        int y = _store.YOf(slot);

        // ---- 本地条件 ----
        float localScarcity = LocalScarcity(x, y);
        float hunger = _store.HungerOf(slot);
        float crowd = LocalCrowding(x, y, out int nearbyPeople);

        // ---- 机会：沿多个方向探出去，比较"别处的资源密度" ----
        float bestOpportunity = 0f;
        Int2 bestTarget = new Int2(-1, -1);

        for (int direction = 0; direction < _config.ProbeDirections; direction++)
        {
            double angle = (6.283185307179586 * direction) / _config.ProbeDirections;
            int tx = x + (int)System.Math.Round(System.Math.Cos(angle) * _config.ProbeDistance);
            int ty = y + (int)System.Math.Round(System.Math.Sin(angle) * _config.ProbeDistance);

            if (!world.IsInBounds(tx, ty)) { continue; }

            // 从探针点向内收缩，找到最近的可走格（否则会选到水里）
            if (!TryFindWalkableNear(world, tx, ty, 8, out tx, out ty)) { continue; }

            float opportunity = OpportunityAt(x, y, tx, ty);
            if (opportunity <= bestOpportunity) { continue; }

            bestOpportunity = opportunity;
            bestTarget = new Int2(tx, ty);
        }

        // ---- 依恋：原住地附近的资源其实还行的话，人就不太想走 ----
        float homeDistance = SimMath.Clamp01(
            (float)Int2.Distance(new Int2(x, y), new Int2(_store.HomeXOf(slot), _store.HomeYOf(slot)))
            / System.Math.Max(1, _config.HomeRegionRadius));
        float attachment = 1f - homeDistance;   // 离家越近越依恋

        float scarcityTerm = _config.ScarcityWeight * SimMath.Clamp01(localScarcity);
        float hungerTerm = _config.HungerWeight * SimMath.Clamp01(hunger);
        float populationTerm = _config.PopulationPressureWeight * SimMath.Clamp01(crowd);
        float opportunityTerm = _config.NearbyOpportunityWeight * SimMath.Clamp01(bestOpportunity);
        float attachmentTerm = _config.HomeAttachmentWeight * SimMath.Clamp01(attachment);

        float total = scarcityTerm + hungerTerm + populationTerm + opportunityTerm - attachmentTerm;
        float utility = SimMath.Clamp01(total);

        detail = "紧张 " + scarcityTerm.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
               + " + 饥饿 " + hungerTerm.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
               + " + 拥挤 " + populationTerm.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
               + " + 机会 " + opportunityTerm.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
               + " - 依恋 " + attachmentTerm.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
               + " = " + utility.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
               + "（附近 " + nearbyPeople + " 人）";

        // 硬性门槛：本地资源不紧张、或者没有更好的地方可去，就不该走。
        // 没有这两道门槛，个体会因为"饥饿 + 拥挤"而在资源充足时四处乱迁。
        if (localScarcity < _config.ScarcityGate) { destination = new Int2(-1, -1); return utility * 0.3f; }
        if (bestOpportunity <= 0.05f) { destination = new Int2(-1, -1); return utility * 0.3f; }

        destination = bestTarget;
        return utility;
    }

    /// <summary>本地资源紧张度 [0,1]：1 = 附近几乎没有可采食物。</summary>
    private float LocalScarcity(int x, int y)
    {
        World world = _sim.World;
        ChunkStatsReadOnly chunk = world.Chunks.ReadAt(x, y);
        if (!chunk.IsValid) { return 1f; }

        // 用"食物 + 木材"的可得性综合判断：
        // 只盯食物会让"森林被砍光但野果还多"的地方被误判为不紧张。
        float foodPerCell = chunk.FoodAmount / System.Math.Max(1, chunk.CellCount);
        float woodPerCell = chunk.WoodAmount / System.Math.Max(1, chunk.CellCount);

        // 参考密度：食物 30/格、木材 40/格 视为"充足"
        float foodScore = SimMath.Clamp01(foodPerCell / 30f);
        float woodScore = SimMath.Clamp01(woodPerCell / 40f);
        float abundance = (foodScore * 0.65f) + (woodScore * 0.35f);

        return 1f - abundance;
    }

    /// <summary>本地人口拥挤度 [0,1]：由所在 chunk 与邻域的人数决定。</summary>
    private float LocalCrowding(int x, int y, out int nearbyPeople)
    {
        World world = _sim.World;
        ChunkGrid chunks = world.Chunks;

        int chunkX = x / chunks.ChunkSize;
        int chunkY = y / chunks.ChunkSize;
        chunks.GetBounds(chunkX, chunkY, out int minX, out int minY, out int maxX, out int maxY);

        nearbyPeople = _store.CountInRect(minX, minY, maxX, maxY);

        // 参考密度：一块 16×16 的块里 12 个人算"拥挤"
        return SimMath.Clamp01(nearbyPeople / 12f);
    }

    /// <summary>
    /// 别处比这里好多少 [0,1]：比较两地的"食物与木材密度"。
    /// 差值为正且越大 ⇒ 机会越大。
    /// </summary>
    private float OpportunityAt(int fromX, int fromY, int toX, int toY)
    {
        World world = _sim.World;
        ChunkStatsReadOnly here = world.Chunks.ReadAt(fromX, fromY);
        ChunkStatsReadOnly there = world.Chunks.ReadAt(toX, toY);
        if (!there.IsValid) { return 0f; }
        if (there.WalkableTiles < 8) { return 0f; }   // 几乎全是水/山的地方不去

        float hereFood = here.FoodAmount / System.Math.Max(1, here.CellCount);
        float thereFood = there.FoodAmount / System.Math.Max(1, there.CellCount);
        float hereWood = here.WoodAmount / System.Math.Max(1, here.CellCount);
        float thereWood = there.WoodAmount / System.Math.Max(1, there.CellCount);

        float foodGain = (thereFood - hereFood) / 40f;
        float woodGain = (thereWood - hereWood) / 60f;

        float gain = (foodGain * 0.7f) + (woodGain * 0.3f);
        return SimMath.Clamp01(gain);
    }

    private static bool TryFindWalkableNear(World world, int x, int y, int radius, out int foundX, out int foundY)
    {
        foundX = x;
        foundY = y;
        if (world.IsInBounds(x, y) && world.TileAt(x, y).Walkable) { return true; }

        for (int r = 1; r <= radius; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    int cheb = System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy));
                    if (cheb != r) { continue; }

                    int cx = x + dx;
                    int cy = y + dy;
                    if (!world.IsInBounds(cx, cy)) { continue; }
                    if (!world.TileAt(cx, cy).Walkable) { continue; }

                    foundX = cx;
                    foundY = cy;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 执行迁移：把个体的"家"搬到目的地，并清空当前动作让他重新安顿下来。
    ///
    /// 注意这里**不是瞬移**：个体只是把"家"改到远处并放弃当前动作，
    /// 接下来他会自己走/探索过去（由 AI 与寻路完成）。
    /// 这样迁移是"慢过程"，玩家能看到一群人陆续往外走 —— 而不是一闪就消失。
    /// </summary>
    private bool TryMigrate(int slot, Int2 destination, long tick)
    {
        World world = _sim.World;
        int x = _store.XOf(slot);
        int y = _store.YOf(slot);

        float distance = (float)Int2.Distance(new Int2(x, y), destination);
        if (distance < _config.MinDistance) { return false; }

        _store.SetHome(slot, destination.X, destination.Y);

        // 授予"迁移意愿"：动作层看到它才会朝新家走。
        //
        // 为什么要"有意愿期"而不是永久意愿：搬完家之后必须能恢复正常生活，
        // 否则个体到达新家后仍然一直"想迁移"，这里的经济永远起不来。
        //
        // 时长按里程估算：距离 / 每 tick 移动速度（0.35 格），再乘 3 倍余量
        // —— 直线距离不等于实际路径长度（要绕水绕山），3 倍是很粗但够用的估计。
        float moveSpeed = System.Math.Max(0.05f, _sim.Config.Ai.MoveSpeedPerTick);
        long travelTicks = (long)(distance / moveSpeed);
        long willingTicks = System.Math.Max(2000L, travelTicks * 3L);
        _store.SetMigrateUntil(slot, tick + willingTicks);

        // 放弃当前动作，让他重新规划（新的"家"会让 Explore/Wander 的离家惩罚重算）
        _store.SetAction(slot, ActionKind.None, ActionPhase.Idle);
        _store.ClearTarget(slot);
        _sim.Actions.ClearMoveProgress(slot);
        _store.SetNextDecisionTick(slot, 0);

        _sim.Events.Record(
            tick,
            History.WorldEventType.AgentMigrated,
            _store.NameOrOverride(slot) + " 迁离 " + new Int2(x, y) + "，目标 " + destination
                + "（距离 " + (int)distance + " 格）",
            History.EventImportance.Important,
            new Int2(x, y),
            slot,
            -1,
            LastEvaluationDetail);

        return true;
    }

    public void ResetStatistics()
    {
        TotalMigrations = 0;
        MigrationsThisTick = 0;
        LastEvaluatedUtility = -1f;
        LastEvaluationDetail = string.Empty;
        if (_cooldownUntil.Length > 0) { System.Array.Clear(_cooldownUntil, 0, _cooldownUntil.Length); }
    }
}
