using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Pathing;

namespace SandBoxSim.Core.Systems;

/// <summary>
/// 动作执行系统：负责"到了没到、做完没做完、结算出了什么"（第 18 节的后半段）。
///
/// 与 <see cref="AiSystem"/> 的分工：
///   * AiSystem 只回答"现在最该做什么"（纯打分 + 选靶）；
///   * ActionSystem 负责把决定变成现实：移动、耗时、资源结算、失败判定。
///
/// 为什么必须分开：如果把执行也塞进决策，就很难单独测试"效用函数是否符合预期"，
/// 而且执行里失败（目标消失）会污染效用统计。分开之后，两个系统各自都能被独立断言。
/// </summary>
public sealed class ActionSystem
{
    private readonly Simulation _sim;
    private readonly AgentStore _store;
    private readonly SimConfig _config;
    private readonly AiConfig _ai;
    private readonly AStarPathfinder _pathfinder;

    /// <summary>小数步进度（槽位 → 剩余步数）。用数组而不是字典，避免迭代顺序问题。</summary>
    private float[] _moveProgress = System.Array.Empty<float>();

    /// <summary>本 tick 的移动次数与完成次数（性能与观测量）。</summary>
    public int MovesThisTick { get; private set; }
    public int CompletedThisTick { get; private set; }
    public int FailedThisTick { get; private set; }

    /// <summary>累计完成/放弃的动作数（报告里反映"世界运行得顺不顺"）。</summary>
    public long TotalCompleted { get; private set; }
    public long TotalFailed { get; private set; }

    /// <summary>累计采集到的资源量（按种类；下标 = (int)ResourceKind）。</summary>
    public float[] HarvestedByKind { get; } = new float[8];

    /// <summary>累计吃掉的食物量。</summary>
    public float TotalFoodEaten { get; private set; }

    /// <summary>累计睡眠时长（tick）。</summary>
    public long TotalSleepTicks { get; private set; }

    public ActionSystem(Simulation sim, AgentStore store, AStarPathfinder pathfinder)
    {
        _sim = sim ?? throw new System.ArgumentNullException(nameof(sim));
        _store = store ?? throw new System.ArgumentNullException(nameof(store));
        _pathfinder = pathfinder ?? throw new System.ArgumentNullException(nameof(pathfinder));
        _config = sim.Config;
        _ai = sim.Config.Ai;
        EnsureCapacity(store.Capacity);
    }

    private void EnsureCapacity(int capacity)
    {
        if (_moveProgress.Length >= capacity) { return; }
        System.Array.Resize(ref _moveProgress, capacity);
    }

    /// <summary>
    /// 每 tick 推进所有个体的动作。必须在 AiSystem 之前调用：
    /// 先让"上一轮的动作"推进到完成/失败，AI 才能基于最新状态重新决策。
    /// </summary>
    public void Tick(long tick)
    {
        EnsureCapacity(_store.Capacity);

        MovesThisTick = 0;
        CompletedThisTick = 0;
        FailedThisTick = 0;

        for (int slot = 0; slot < _store.Capacity; slot++)
        {
            if (!_store.IsSlotAlive(slot)) { continue; }

            ActionPhase phase = _store.PhaseOf(slot);
            if (phase == ActionPhase.Idle || phase == ActionPhase.Done || phase == ActionPhase.Failed)
            {
                continue;
            }

            int actionTicks = _store.ActionTicksOf(slot);
            _store.IncrementActionTicks(slot);

            // 耐心上限：超过就放弃。没有这个兜底，个体可能永远卡在一个永远到不了的目标上。
            if (actionTicks > _ai.ActionPatienceTicks)
            {
                Fail(slot, ActionFailReason.Unreachable);
                continue;
            }

            if (phase == ActionPhase.Moving)
            {
                TickMovement(slot, tick);
            }
            else if (phase == ActionPhase.Executing)
            {
                TickExecution(slot, tick);
            }
        }
    }

    // ---------------------------------------------------------------------
    // 移动
    // ---------------------------------------------------------------------

    private void TickMovement(int slot, long tick)
    {
        if (!_store.HasTarget(slot))
        {
            Fail(slot, ActionFailReason.NoTarget);
            return;
        }

        Int2 target = _store.TargetOf(slot);
        int x = _store.XOf(slot);
        int y = _store.YOf(slot);

        if (x == target.X && y == target.Y)
        {
            Arrive(slot);
            return;
        }

        // 目标可能已经不可走（玩家把地形改成水、森林被烧成焦土……）
        if (!_sim.World.TileAtClamped(target.X, target.Y).Walkable)
        {
            Fail(slot, ActionFailReason.TargetGone);
            return;
        }

        float progress = _moveProgress[slot] + _ai.MoveSpeedPerTick;

        // 一 tick 内推进多格是允许的（速度 > 1 时），因此用 while 而不是 if
        while (progress >= 1f)
        {
            progress -= 1f;

            PathResult result = _pathfinder.FindNextStep(x, y, target.X, target.Y, out Int2 next);

            if (!result.Success)
            {
                _moveProgress[slot] = progress;
                Fail(slot, ActionFailReason.Unreachable);
                return;
            }

            if (next.X == x && next.Y == y)
            {
                // 已经站在目标上（可能目标就是自己）
                break;
            }

            // 只允许走到可走格：寻路已经保证，但地形可能在两次调用之间变化
            if (!_sim.World.TileAtClamped(next.X, next.Y).Walkable)
            {
                _moveProgress[slot] = progress;
                Fail(slot, ActionFailReason.TargetGone);
                return;
            }

            byte facing = DirectionTo(x, y, next.X, next.Y);
            _store.SetPosition(slot, next.X, next.Y);
            _store.SetFacing(slot, facing);
            x = next.X;
            y = next.Y;
            MovesThisTick++;

            if (x == target.X && y == target.Y)
            {
                progress = 0f;
                break;
            }
        }

        _moveProgress[slot] = progress;

        if (x == target.X && y == target.Y)
        {
            Arrive(slot);
        }
    }

    private static byte DirectionTo(int fromX, int fromY, int toX, int toY)
    {
        int dx = toX - fromX;
        int dy = toY - fromY;
        if (dx > 0 && dy == 0) { return 0; }
        if (dx < 0 && dy == 0) { return 1; }
        if (dx == 0 && dy > 0) { return 2; }
        if (dx == 0 && dy < 0) { return 3; }
        if (dx > 0 && dy > 0) { return 4; }
        if (dx > 0 && dy < 0) { return 5; }
        if (dx < 0 && dy > 0) { return 6; }
        if (dx < 0 && dy < 0) { return 7; }
        return 0;
    }

    private void Arrive(int slot)
    {
        _moveProgress[slot] = 0f;
        _store.ClearPathStep(slot);
        _store.SetPhase(slot, ActionPhase.Executing);

        ActionKind action = _store.ActionOf(slot);
        switch (action)
        {
            case ActionKind.Eat:
            case ActionKind.Drink:
                _store.SetState(slot, AgentState.Eating);
                break;
            case ActionKind.Sleep:
                _store.SetState(slot, AgentState.Sleeping);
                break;
            case ActionKind.Wander:
            case ActionKind.Explore:
                // 走到就算完成：漫游/探索没有"执行"阶段
                Complete(slot, tick: _sim.World.Tick);
                break;
            default:
                _store.SetState(slot, AgentState.Working);
                break;
        }
    }

    // ---------------------------------------------------------------------
    // 执行
    // ---------------------------------------------------------------------

    private void TickExecution(int slot, long tick)
    {
        switch (_store.ActionOf(slot))
        {
            case ActionKind.Sleep:
                TickSleep(slot);
                break;

            case ActionKind.Eat:
                TickEat(slot);
                break;

            case ActionKind.Drink:
                TickDrink(slot);
                break;

            case ActionKind.GatherFood:
            case ActionKind.GatherWood:
            case ActionKind.GatherStone:
            case ActionKind.GatherIron:
                TickGather(slot, tick);
                break;

            default:
                // 未知动作：立刻完成，避免卡住
                Complete(slot, tick);
                break;
        }
    }

    /// <summary>
    /// 睡觉：疲劳由 <see cref="NeedsSystem"/> 在每 tick 反解。
    /// 这里只负责"睡到什么时候醒"：疲劳降到很低，或者睡眠累计够长（防止永远睡不够）。
    /// </summary>
    private void TickSleep(int slot)
    {
        TotalSleepTicks++;

        if (_store.FatigueOf(slot) <= 0.05f)
        {
            Complete(slot, _sim.World.Tick);
            return;
        }

        if (_store.ActionTicksOf(slot) > 720)
        {
            // 睡了半天还没恢复（例如被饥饿打断过）：先起来，让 AI 重新决定
            Complete(slot, _sim.World.Tick);
        }
    }

    /// <summary>进食：每 tick 消耗一点食物，同时压低饥饿。吃够了就结束。</summary>
    private void TickEat(int slot)
    {
        const float FoodPerTick = 0.6f;
        const float HungerReliefPerFood = 0.15f;

        float food = _store.InventoryOf(slot, ResourceKind.Food);
        if (food <= 0f)
        {
            // 食物在吃的过程里用完了（例如被其他系统拿走）
            Fail(slot, ActionFailReason.PrerequisiteLost);
            return;
        }

        float eaten = food < FoodPerTick ? food : FoodPerTick;
        _store.AddInventory(slot, ResourceKind.Food, -eaten);
        _store.AddNeed(slot, NeedIndex.Hunger, -eaten * HungerReliefPerFood);
        TotalFoodEaten += eaten;

        if (_store.HungerOf(slot) <= 0.05f || _store.InventoryOf(slot, ResourceKind.Food) <= 0f)
        {
            Complete(slot, _sim.World.Tick);
        }
    }

    /// <summary>饮水：站在临水格上把干渴压回去。</summary>
    private void TickDrink(int slot)
    {
        if (!HasAdjacentWater(slot))
        {
            Fail(slot, ActionFailReason.PrerequisiteLost);
            return;
        }

        _store.AddNeed(slot, NeedIndex.Thirst, -0.35f);

        if (_store.ThirstOf(slot) <= 0.05f)
        {
            Complete(slot, _sim.World.Tick);
        }
    }

    private bool HasAdjacentWater(int slot)
    {
        int x = _store.XOf(slot);
        int y = _store.YOf(slot);
        World world = _sim.World;

        return world.TileAtClamped(x - 1, y).Terrain == TerrainKind.Water
            || world.TileAtClamped(x + 1, y).Terrain == TerrainKind.Water
            || world.TileAtClamped(x, y - 1).Terrain == TerrainKind.Water
            || world.TileAtClamped(x, y + 1).Terrain == TerrainKind.Water;
    }

    /// <summary>
    /// 采集：站在目标格上扣减 Tile 资源，放进个体背包。
    /// 采集量由配置决定（第 11 节的公式落到 ResourceSystem 上执行）。
    /// </summary>
    private void TickGather(int slot, long tick)
    {
        ActionKind action = _store.ActionOf(slot);
        ResourceKind kind = KindForAction(action);
        if (kind == ResourceKind.None)
        {
            Fail(slot, ActionFailReason.PrerequisiteLost);
            return;
        }

        // 目标格优先用记录的目标；没有目标（走到之后被清掉）就用当前格
        Int2 target = _store.HasTarget(slot) ? _store.TargetOf(slot) : new Int2(_store.XOf(slot), _store.YOf(slot));

        // 注意：TileAtClamped 是**按值返回**的（为了越界安全），因此这里必须是值拷贝而不是 ref。
        // 写成 `ref readonly Tile tile = ref TileAtClamped(...)` 会编译失败（CS8156）——
        // 这是好事：它提醒我们"越界安全读取本来就无法返回引用"。
        Tile tile = _sim.World.TileAtClamped(target.X, target.Y);
        if (tile.Resource.Kind != kind || tile.Resource.Amount <= 0f)
        {
            // 资源被采空了：这是一个正常现象（过度采集），不是 bug
            Fail(slot, ActionFailReason.TargetGone);
            return;
        }

        float perAction = PerHarvestForAction(kind);
        float taken = _sim.ResourceSystem.Harvest(target.X, target.Y, kind, perAction);
        if (taken <= 0f)
        {
            Fail(slot, ActionFailReason.TargetGone);
            return;
        }

        _store.AddInventory(slot, kind, taken);
        int index = (int)kind;
        if (index >= 0 && index < HarvestedByKind.Length) { HarvestedByKind[index] += taken; }

        // 采集动作是"一次结算"：拿完就走，AI 会决定是继续采还是去做别的。
        // 这样行为更容易观察（每次采集都是一条事件），也避免个体在原地站一整天。
        Complete(slot, tick);
    }

    private static ResourceKind KindForAction(ActionKind action)
    {
        switch (action)
        {
            case ActionKind.GatherFood: return ResourceKind.Food;
            case ActionKind.GatherWood: return ResourceKind.Wood;
            case ActionKind.GatherStone: return ResourceKind.Stone;
            case ActionKind.GatherIron: return ResourceKind.Iron;
            default: return ResourceKind.None;
        }
    }

    private float PerHarvestForAction(ResourceKind kind)
    {
        switch (kind)
        {
            case ResourceKind.Food: return MathMax(1f, _config.Resources.FoodHarvestPerAction);
            case ResourceKind.Wood: return MathMax(1f, _config.Resources.WoodHarvestPerAction);
            case ResourceKind.Stone: return MathMax(1f, _config.Resources.StoneHarvestPerAction);
            case ResourceKind.Iron: return MathMax(1f, _config.Resources.IronHarvestPerAction);
            default: return 1f;
        }
    }

    private static float MathMax(float a, float b) => a > b ? a : b;

    // ---------------------------------------------------------------------
    // 完成 / 失败
    // ---------------------------------------------------------------------

    private void Complete(int slot, long tick)
    {
        _store.SetPhase(slot, ActionPhase.Done);
        _store.SetState(slot, AgentState.Idle);
        _store.ClearTarget(slot);
        _moveProgress[slot] = 0f;
        CompletedThisTick++;
        TotalCompleted++;
        _ = tick;
    }

    private void Fail(int slot, ActionFailReason reason)
    {
        _store.SetPhase(slot, ActionPhase.Failed);
        _store.SetFailReason(slot, reason);
        _store.SetState(slot, AgentState.Idle);
        _store.ClearTarget(slot);
        _moveProgress[slot] = 0f;
        FailedThisTick++;
        TotalFailed++;

        // 失败之后允许立刻重新决策（不等那 10 小时），否则个体被卡住时会"呆站很久"。
        _store.SetNextDecisionTick(slot, 0);
    }

    /// <summary>重置统计（世界重建时）。</summary>
    public void ResetStatistics()
    {
        TotalCompleted = 0;
        TotalFailed = 0;
        TotalFoodEaten = 0f;
        TotalSleepTicks = 0;
        MovesThisTick = 0;
        for (int i = 0; i < HarvestedByKind.Length; i++) { HarvestedByKind[i] = 0f; }
        if (_moveProgress.Length > 0) { System.Array.Clear(_moveProgress, 0, _moveProgress.Length); }
    }

    /// <summary>累计采集总量（报告用）。</summary>
    public float TotalHarvested()
    {
        float sum = 0f;
        for (int i = 0; i < HarvestedByKind.Length; i++) { sum += HarvestedByKind[i]; }
        return sum;
    }
}
