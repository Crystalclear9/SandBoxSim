using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Ai;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Pathing;

namespace SandBoxSim.Core.Systems;

/// <summary>
/// Utility AI 决策系统（第 14–17、33 节）。
///
/// 每个个体在需要决策时：
///   1. 算出所有可行动作的效用（含完整分解）；
///   2. 取效用最高的那个（平局按动作登记表的固定顺序，保证确定性）；
///   3. 为它选一个目标（可能失败 ⇒ 退而选次优）。
///
/// 分批（Staggered）更新是"大量个体也能跑"的关键（第 73 条）：
/// 每 tick 只让 1/batchCount 的个体做决策，其余继续执行上一轮的动作。
/// 代价是信息滞后几拍（"他上一拍还在往已经没资源的地方走"），
/// 因此执行前必须重新确认目标仍然有效。
/// </summary>
public sealed class AiSystem
{
    private readonly Simulation _sim;
    private readonly AgentStore _store;
    private readonly SimConfig _config;
    private readonly AiConfig _ai;
    private readonly AStarPathfinder _pathfinder;

    /// <summary>可复用的打分缓冲（避免每个个体都分配一个数组）。</summary>
    private ActionScore[] _scoreBuffer = System.Array.Empty<ActionScore>();

    /// <summary>与打分缓冲对应的"本轮是否已尝试过该动作"标记（避免用魔数污染效用）。</summary>
    private bool[] _scoreTried = System.Array.Empty<bool>();

    private int _scoreCount;

    /// <summary>当前生效的分批数（自适应，见 <see cref="ResolveBatchCount"/>）。</summary>
    public int BatchCount { get; private set; } = 6;

    /// <summary>本 tick 实际做出决策的个体数（性能观测）。</summary>
    public int DecisionsThisTick { get; private set; }

    /// <summary>累计决策次数与"找不到目标"的次数（后者高说明世界太贫瘠或太封闭）。</summary>
    public long TotalDecisions { get; private set; }
    public long TargetSelectionFailures { get; private set; }

    /// <summary>累计"每个动作被选中多少次"（下标 = (int)ActionKind）。</summary>
    public int[] ChosenByAction { get; } = new int[64];

    /// <summary>累计"每个动作被评估多少次"（与 ChosenByAction 对比可以看出哪些动作从不被选）。</summary>
    public int[] EvaluatedByAction { get; } = new int[64];

    /// <summary>决策相位与分批数最近是否变化过（供测试与 UI 观测）。</summary>
    public int LastPhaseChangeTick { get; private set; }

    public AStarPathfinder Pathfinder => _pathfinder;

    public AiSystem(Simulation sim, AgentStore store, AStarPathfinder pathfinder)
    {
        _sim = sim ?? throw new System.ArgumentNullException(nameof(sim));
        _store = store ?? throw new System.ArgumentNullException(nameof(store));
        _pathfinder = pathfinder ?? throw new System.ArgumentNullException(nameof(pathfinder));
        _config = sim.Config;
        _ai = sim.Config.Ai;

        BatchCount = ResolveBatchCount();
        _store.AssignDecisionPhases(BatchCount);
        EnsureScoreBuffer(ActionRegistry.All.Length);
    }

    private void EnsureScoreBuffer(int size)
    {
        if (_scoreBuffer.Length >= size) { return; }
        _scoreBuffer = new ActionScore[size];
        _scoreTried = new bool[size];
    }

    /// <summary>
    /// 计算本 tick 应该让多少个体决策。
    ///
    /// 自适应逻辑：人口少时不分批（反应更灵敏），人口多时增大分批数，
    /// 把"每 tick 决策数"控制在 <c>TargetDecisionsPerTick</c> 附近。
    /// 这比写死常数更能适应"从 10 人到 1000 人"的跨度。
    /// </summary>
    private int ResolveBatchCount()
    {
        if (_ai.BatchCount > 0) { return _ai.BatchCount; }

        int population = _store.LiveCount;
        int target = _ai.TargetDecisionsPerTick > 0 ? _ai.TargetDecisionsPerTick : 12;
        if (population <= target) { return 1; }

        int batch = (population + target - 1) / target;
        if (batch < 1) { batch = 1; }
        if (batch > 64) { batch = 64; }
        return batch;
    }

    /// <summary>人口变化后重新评估分批数（由 Simulation 在日边界调用）。</summary>
    public void RefreshBatchCount()
    {
        int resolved = ResolveBatchCount();
        if (resolved == BatchCount) { return; }

        BatchCount = resolved;
        _store.AssignDecisionPhases(BatchCount);
        LastPhaseChangeTick = (int)_sim.World.Tick;
    }

    /// <summary>
    /// 读档专用：采用存档里的分批数与相位变化时刻，**绝不重新分配相位**。
    ///
    /// # 这是一个真实踩到的坑，而且非常隐蔽
    ///
    /// `NotifyAfterLoad` 原先调用的是 <see cref="RefreshBatchCount"/>，
    /// 而它在分批数**发生变化时**会调用 `AssignDecisionPhases` ——
    /// 于是"加载存档"这个动作悄悄地把所有人的决策相位重排了一遍。
    ///
    /// 为什么读档瞬间看不出来：`decisionPhase` 与 `nextDecisionTick`
    /// **当时都不在状态摘要里**，所以"读档后摘要一致"这条自校验完全通过。
    /// 症状要到大约一个决策间隔（600 tick）之后才出现：
    /// 某个个体在 direct 里轮到决策并去吃东了，在 restored 里还没轮到 ——
    /// 表现为 `state=Eating` vs `Idle`、`target=(57,5)` vs `(-1,-1)`。
    ///
    /// 修法有两半，缺一不可：
    ///   1. 读档走这个"只采用、不重排"的入口；
    ///   2. 把 `decisionPhase` 与 `nextDecisionTick` **加进状态摘要**，
    ///      这样同类问题会在读档瞬间就被抓住，而不是 600 tick 之后。
    /// </summary>
    public void AdoptBatchCountAfterLoad(int batchCount, int lastPhaseChangeTick)
    {
        BatchCount = batchCount > 0 ? batchCount : ResolveBatchCount();
        LastPhaseChangeTick = lastPhaseChangeTick;
    }

    /// <summary>
    /// 推进一轮决策。返回本 tick 做出决策的个体数。
    /// </summary>
    public int Tick(long tick, bool isNight)
    {
        EnsureScoreBuffer(ActionRegistry.All.Length);

        DecisionsThisTick = 0;

        int batch = BatchCount;
        int phase = (int)(tick % batch);

        World world = _sim.World;
        DeterministicRandom rng = _sim.Random.Get(RngStream.Agents);

        for (int slot = 0; slot < _store.Capacity; slot++)
        {
            if (!_store.IsSlotAlive(slot)) { continue; }

            if (batch > 1)
            {
                int individualPhase = _store.DecisionPhaseOf(slot) % batch;
                if (individualPhase != phase) { continue; }
            }

            ActionPhase actionPhase = _store.PhaseOf(slot);
            bool needsDecision = actionPhase == ActionPhase.Idle
                                 || actionPhase == ActionPhase.Done
                                 || actionPhase == ActionPhase.Failed;

            if (!needsDecision) { continue; }

            // 不该过于频繁地改主意：两次决策之间至少要隔一小段时间，
            // 否则"刚决定去砍树，下一 tick 又决定去喝水"，看起来像多动症。
            //
            // **但生存需求必须可以打断这个冷却**（饥饿/干渴越过阈值的时点）。
            // 原因不是"更聪明"，而是**可行性**：如果饥饿只能在每 10 游戏小时一次的
            // 决策窗口里被处理，那么无论食物多充足、算法多正确，一天最多也只能进食两次 ——
            // 结果必然是全体饿死（实测过：饮食统计在涨、随身食物有剩余、人还是在死）。
            // 让"快饿死"能打断当前计划，是把生物需求与决策频率解耦的正确做法。
            long nextAllowed = _store.NextDecisionTickOf(slot);
            if (tick < nextAllowed && !HasCriticalNeed(slot)) { continue; }

            DecideFor(slot, tick, isNight, rng, world);
            DecisionsThisTick++;
        }

        return DecisionsThisTick;
    }

    /// <summary>
    /// 是否存在"必须立刻处理"的生存需求。
    ///
    /// 阈值取 0.7：明显高于"想要"的水平（0.2~0.4 由效用函数处理），
    /// 又低于开始掉血的 0.85，留出一段"还来得及"的缓冲。
    /// </summary>
    private bool HasCriticalNeed(int slot)
    {
        return _store.HungerOf(slot) >= 0.7f
            || _store.ThirstOf(slot) >= 0.7f
            || _store.HealthOf(slot) <= 0.35f;
    }

    private void DecideFor(int slot, long tick, bool isNight, DeterministicRandom rng, World world)
    {
        var ctx = BuildContext(slot, tick, isNight, rng, world);

        _scoreCount = 0;
        for (int i = 0; i < ActionRegistry.All.Length; i++)
        {
            ActionKind kind = ActionRegistry.All[i];
            ActionDef def = ActionRegistry.DescribeCached(kind);
            if (def.Evaluate == null) { continue; }

            ActionScore score = def.Evaluate(in ctx);

            int kindIndex = (int)kind;
            if (kindIndex >= 0 && kindIndex < EvaluatedByAction.Length) { EvaluatedByAction[kindIndex]++; }

            if (_scoreCount < _scoreBuffer.Length)
            {
                _scoreBuffer[_scoreCount] = score;
                _scoreTried[_scoreCount] = false;
                _scoreCount++;
            }
        }

        TotalDecisions++;

        // 逐个尝试候选动作（按效用降序），直到有一个成功选到目标。
        // 为什么要这样：效用最高的动作可能"附近没目标"，而次优动作可以做 ——
        // 只试最优就放弃的话，个体会站在没有森林的地方一直"想砍树"。
        ActionKind chosen = ActionKind.None;
        Int2 target = default;
        bool chosenNeedsTarget = false;

        int maxAttempts = _scoreCount < 5 ? _scoreCount : 5;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            int bestIndex = -1;
            float bestScore = -1f;

            for (int i = 0; i < _scoreCount; i++)
            {
                if (_scoreTried[i]) { continue; }
                if (_scoreBuffer[i].Utility > bestScore)
                {
                    bestScore = _scoreBuffer[i].Utility;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0) { break; }

            _scoreTried[bestIndex] = true;
            ActionKind candidate = _scoreBuffer[bestIndex].Action;
            ActionDef candidateDef = ActionRegistry.DescribeCached(candidate);

            if (candidateDef.NeedsTarget)
            {
                if (candidateDef.SelectTarget == null) { continue; }

                Int2? selected = candidateDef.SelectTarget(in ctx, _pathfinder);
                if (selected == null)
                {
                    if (attempt == 0) { TargetSelectionFailures++; }
                    continue;
                }

                chosen = candidate;
                target = selected.Value;
                chosenNeedsTarget = true;

                // 顺手把"赢了谁"记下来：Top 分数里要能看出次优是什么
                break;
            }

            chosen = candidate;
            chosenNeedsTarget = false;
            break;
        }

        RecordDecision(slot, tick, chosen);

        int chosenIndex = (int)chosen;
        if (chosenIndex >= 0 && chosenIndex < ChosenByAction.Length) { ChosenByAction[chosenIndex]++; }

        if (chosen == ActionKind.None)
        {
            // 所有候选动作都不可行：标记为"找不到目标"，过一会儿再试。
            _store.SetAction(slot, ActionKind.None, ActionPhase.Failed);
            _store.SetFailReason(slot, ActionFailReason.NoTarget);
            _store.SetState(slot, AgentState.Idle);
            _store.SetNextDecisionTick(slot, tick + System.Math.Max(30, _ai.DecisionIntervalTicks / 4));
            return;
        }

        _store.SetFailReason(slot, ActionFailReason.None);
        _store.SetNextDecisionTick(slot, tick + _ai.DecisionIntervalTicks);

        if (chosenNeedsTarget)
        {
            _store.SetTarget(slot, target.X, target.Y);
            _store.ClearPathStep(slot);
            _store.SetAction(slot, chosen, ActionPhase.Moving);
            _store.SetState(slot, AgentState.Moving);

            // 已经在目标格上：直接进入执行阶段
            if (_store.XOf(slot) == target.X && _store.YOf(slot) == target.Y)
            {
                _store.SetPhase(slot, ActionPhase.Executing);
                _store.SetState(slot, ActionStateFor(chosen));
            }
            return;
        }

        // 不需要移动的动作（进食、睡觉）：立刻进入执行阶段
        _store.ClearTarget(slot);
        _store.SetAction(slot, chosen, ActionPhase.Executing);
        _store.SetState(slot, ActionStateFor(chosen));
    }

    private static AgentState ActionStateFor(ActionKind action)
    {
        switch (action)
        {
            case ActionKind.Eat:
            case ActionKind.Drink: return AgentState.Eating;
            case ActionKind.Sleep: return AgentState.Sleeping;
            case ActionKind.Wander:
            case ActionKind.Explore: return AgentState.Moving;
            default: return AgentState.Working;
        }
    }

    /// <summary>
    /// 保存 Top-N 打分（供检查器显示"为什么选了这个、次优是什么"）。
    /// 用**非破坏性**的排序：不能修改 _scoreBuffer，因为候选尝试循环还在用它。
    /// </summary>
    private void RecordDecision(int slot, long tick, ActionKind chosen)
    {
        int keep = _ai.TopScoresToKeep;
        if (keep < 1) { keep = 1; }
        if (keep > _scoreCount) { keep = _scoreCount; }

        var top = new ActionScore[keep];
        var taken = new bool[_scoreCount];
        float chosenUtility = 0f;

        for (int i = 0; i < keep; i++)
        {
            int bestIndex = -1;
            float bestScore = -1f;

            for (int j = 0; j < _scoreCount; j++)
            {
                if (taken[j]) { continue; }
                if (_scoreBuffer[j].Utility > bestScore)
                {
                    bestScore = _scoreBuffer[j].Utility;
                    bestIndex = j;
                }
            }

            if (bestIndex < 0) { break; }

            taken[bestIndex] = true;
            top[i] = _scoreBuffer[bestIndex];

            if (_scoreBuffer[bestIndex].Action == chosen) { chosenUtility = bestScore; }
        }

        _store.SetLastDecision(slot, new UtilityBreakdown(chosen, top, chosenUtility, tick));
    }

    private ActionContext BuildContext(int slot, long tick, bool isNight, DeterministicRandom rng, World world)
    {
        int x = _store.XOf(slot);
        int y = _store.YOf(slot);

        return new ActionContext
        {
            World = world,
            Store = _store,
            Slot = slot,
            X = x,
            Y = y,
            Config = _config,
            Ai = _ai,
            Chunk = world.Chunks.ReadAt(x, y),
            GroundStocks = _sim.GroundStocks,
            Wildlife = _sim.Wildlife,
            Buildings = _sim.Buildings,
            Storage = _sim.Storage,
            Tick = tick,
            IsNight = isNight,
            HomeX = _store.HomeXOf(slot),
            HomeY = _store.HomeYOf(slot),
            Rng = rng,
            MoveSpeedPerTick = _ai.MoveSpeedPerTick,
        };
    }

    /// <summary>重置统计（世界重建时）。</summary>
    public void ResetStatistics()
    {
        TotalDecisions = 0;
        TargetSelectionFailures = 0;
        DecisionsThisTick = 0;
        for (int i = 0; i < ChosenByAction.Length; i++) { ChosenByAction[i] = 0; }
        for (int i = 0; i < EvaluatedByAction.Length; i++) { EvaluatedByAction[i] = 0; }
        BatchCount = ResolveBatchCount();
        _store.AssignDecisionPhases(BatchCount);
    }
}
