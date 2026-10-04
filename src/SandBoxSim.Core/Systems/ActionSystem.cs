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

    /// <summary>
    /// 读取某个个体的小数步进度（存档专用）。
    ///
    /// **这是"小数进度也必须进存档"的一个实例，而且是最不容易想到的一类**：
    /// 它不进状态摘要（摘要只看整数格位置），但它决定"这个人下一 tick 会不会跨到下一格"。
    /// 不保存它 → 读档后进度归零 → 移动节奏整体错开一格，
    /// 于是"读档续跑"与"直接跑"在**第 2 tick** 就分叉，而读档瞬间的摘要完全一致。
    ///
    /// 判据仍然是那一句：会不会影响未来的行为。小数进度会。
    /// </summary>
    public float MoveProgressOf(int slot)
        => slot >= 0 && slot < _moveProgress.Length ? _moveProgress[slot] : 0f;

    /// <summary>读档时恢复某个个体的小数步进度。</summary>
    public void RestoreMoveProgress(int slot, float progress)
    {
        EnsureCapacity(slot + 1);
        if (slot >= 0 && slot < _moveProgress.Length) { _moveProgress[slot] = progress < 0f ? 0f : progress; }
    }

    /// <summary>读档时先把全部小数进度清零，再逐条恢复。</summary>
    public void ClearAllMoveProgress()
    {
        for (int i = 0; i < _moveProgress.Length; i++) { _moveProgress[i] = 0f; }
    }

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

    /// <summary>累计猎杀数与由此获得的食物（"人类活动影响生态"的直接证据）。</summary>
    public int TotalHunted { get; private set; }
    public float TotalHuntedFood { get; private set; }

    /// <summary>累计存入/取出地面物资堆的数量（M2）。</summary>
    public float TotalDeposited { get; private set; }
    public float TotalTaken { get; private set; }

    /// <summary>累计开工的建造次数（M3）。</summary>
    public int BuildsStarted { get; private set; }

    /// <summary>累计存入仓库的次数（M3）。</summary>
    public int StoresIntoBuilding { get; private set; }

    /// <summary>清空某个体的移动进度（迁移等操作会打断移动，必须同步清掉）。</summary>
    public void ClearMoveProgress(int slot)
    {
        if (slot >= 0 && slot < _moveProgress.Length) { _moveProgress[slot] = 0f; }
    }

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

        // 只遍历**存活**槽位（前缀数组），不遍历整个容量数组。
        // 容量是按峰值人口预留的，人口回落之后大部分槽位是空的；
        // 每 tick 扫一遍空槽位在长期运行里是纯粹的浪费（这类"看不见的常数"最容易被忽略）。
        int[] slots = _store.LiveSlotsRaw(out int liveCount);
        for (int k = 0; k < liveCount; k++)
        {
            int slot = slots[k];

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

            _pathfinder.MarkOrigin(AStarPathfinder.SearchOrigin.Movement);
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

            case ActionKind.Migrate:
                // 到达新家 ⇒ 清除迁移意愿，恢复正常生活。
                // 如果目的地被中断（比如中途被打断），意愿会在到期时刻自动失效。
                _store.ClearMigrateIntent(slot);
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

            case ActionKind.Socialize:

                TickSocialize(slot, tick);

                break;


            case ActionKind.ShareFood:

                TickShareFood(slot, tick);

                break;


            case ActionKind.Flee:

                TickFlee(slot, tick);

                break;


            case ActionKind.Attack:

                TickAttack(slot, tick);

                break;

            case ActionKind.Hunt:
                TickHunt(slot, tick);
                break;

            case ActionKind.Deposit:
                TickDeposit(slot, tick);
                break;

            case ActionKind.Take:
                TickTake(slot, tick);
                break;

            case ActionKind.Migrate:
                // 迁移的"执行"就是走到新家；到达即完成（见 Arrive）。
                Complete(slot, tick);
                break;

            case ActionKind.BuildHouse:
            case ActionKind.BuildStorage:
            case ActionKind.BuildFarm:
                TickBuild(slot, tick);
                break;

            case ActionKind.StoreInBuilding:
                TickStoreInBuilding(slot, tick);
                break;

            case ActionKind.Farm:
                TickFarm(slot, tick);
                break;

            default:
                // 未知动作：立刻完成，避免卡住
                Complete(slot, tick);
                break;
        }
    }

    /// <summary>
    /// 狩猎（M2）：到达猎物附近后需要若干 tick 才能成功猎杀。
    ///
    /// 为什么要"若干 tick"而不是"到了就拿到肉"：
    /// 拉长这个过程之后，猎物有时间逃跑（动物系统每 tick 都会躲人），
    /// 于是"打猎"变成一个可能失败的行为 —— 而这个失败率会随**动物密度**变化。
    /// 这正是"砍光森林 → 猎物变少 → 打猎更难"这条链能被玩家观察到的机制基础。
    /// </summary>
    private void TickHunt(int slot, long tick)
    {
        WildlifeStore wildlife = _sim.Wildlife;
        int x = _store.XOf(slot);
        int y = _store.YOf(slot);

        if (!wildlife.TryFindNearest(x, y, _config.Wildlife.HuntRange, out int prey, out int _))
        {
            // 猎物跑了（或被别人抢了）：如果还在附近就再追，否则放弃
            if (wildlife.TryFindNearest(x, y, _config.Wildlife.FleeRadius, out int nearby, out int _))
            {
                _store.SetTarget(slot, wildlife.XOf(nearby), wildlife.YOf(nearby));
                _store.SetPhase(slot, ActionPhase.Moving);
                _store.SetState(slot, AgentState.Moving);
                return;
            }

            Fail(slot, ActionFailReason.TargetGone);
            return;
        }

        // 需要持续瞄准若干 tick
        if (_store.ActionTicksOf(slot) < _config.Wildlife.HuntTicks)
        {
            _ = prey;
            return;
        }

        float food = _sim.WildlifeSystem.Hunt(x, y);
        if (food <= 0f)
        {
            Fail(slot, ActionFailReason.TargetGone);
            return;
        }

        _store.AddInventory(slot, ResourceKind.Food, food);
        TotalHunted++;
        TotalHuntedFood += food;
        int index = (int)ResourceKind.Food;
        HarvestedByKind[index] += food;

        _sim.Events.Record(tick, History.WorldEventType.AgentAte,
            _store.NameOrOverride(slot) + " 猎获一只动物（+" + food.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " 食物）",
            History.EventImportance.Minor,
            new Int2(x, y),
            slot,
            -1,
            "狩猎");

        Complete(slot, tick);
    }

    /// <summary>存放物资：把随身物资放到地面堆上（在堆附近则叠加，否则就地新建）。</summary>
    private void TickDeposit(int slot, long tick)
    {
        GroundStockConfig config = _config.GroundStocks;
        GroundStockStore stocks = _sim.GroundStocks;
        Int2 position = _store.HasTarget(slot) ? _store.TargetOf(slot) : new Int2(_store.XOf(slot), _store.YOf(slot));

        float moved = 0f;
        for (int kind = (int)ResourceKind.Food; kind <= (int)ResourceKind.Iron; kind++)
        {
            ResourceKind resource = (ResourceKind)kind;
            float carried = _store.InventoryOf(slot, resource);
            if (carried <= 0f) { continue; }

            float wanted = carried < config.DropAmount ? carried : config.DropAmount;
            float accepted = stocks.Deposit(position.X, position.Y, resource, wanted, config);
            if (accepted <= 0f) { continue; }

            _store.AddInventory(slot, resource, -accepted);
            moved += accepted;
        }

        if (moved <= 0f)
        {
            // 放不下（堆满了或没有可放的资源）：不是错误，只是这次没事可做
            Fail(slot, ActionFailReason.PrerequisiteLost);
            return;
        }

        TotalDeposited += moved;
        _sim.Events.Record(tick, History.WorldEventType.ResourceInjected,
            _store.NameOrOverride(slot) + " 在 " + position + " 存放了 " + moved.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " 物资",
            History.EventImportance.Minor,
            position,
            slot,
            -1,
            "存放物资");

        Complete(slot, tick);
    }

    /// <summary>
    /// 建造（M3）：到达工地后**开工**，然后离开 —— 施工由 <see cref="BuildingSystem"/> 推进。
    ///
    /// 为什么不让个体一直站在工地上：
    ///   1. 那会让"建造"占用一个人的全部时间，从而把经济压垮（一个人至少要不吃不喝好几天）；
    ///   2. 现实里工地也是"开工之后工人就走了"；
    ///   3. 它让"劳动力"与"工期"解耦 —— 工期由系统推进，而不是由某个人守着。
    ///
    /// 因此这个动作只做一件事：**扣料、放下工地**。剩下的交给系统。
    /// </summary>
    private void TickBuild(int slot, long tick)
    {
        ActionKind action = _store.ActionOf(slot);
        BuildingKind kind = KindForBuildAction(action);
        if (kind == BuildingKind.None)
        {
            Fail(slot, ActionFailReason.PrerequisiteLost);
            return;
        }

        // 目标格优先用记录的目标（选靶时确定的空地）
        Int2 site = _store.HasTarget(slot) ? _store.TargetOf(slot) : new Int2(_store.XOf(slot), _store.YOf(slot));

        if (!_sim.BuildingSystem.TryStartBuilding(_store, slot, kind, site.X, site.Y, out int buildingIndex, out string failure))
        {
            // 失败不是异常：可能是"位置刚被别人占了"或"材料路上被取走了"。
            // 事件日志里记下来，检查器能看到原因 —— 这比静默失败好得多。
            _sim.Events.Record(tick, History.WorldEventType.BuildingStarted,
                _store.NameOrOverride(slot) + " 建造失败：" + failure,
                History.EventImportance.Minor,
                site, slot, -1, failure);
            Fail(slot, ActionFailReason.PrerequisiteLost);
            return;
        }

        BuildsStarted++;
        _ = buildingIndex;
        Complete(slot, tick);
    }

    /// <summary>
    /// 耕种（M4）：到达农田后把一份劳动量记到那块田上，然后离开。
    ///
    /// 与 <see cref="TickBuild"/> 同样是**一次性的**：到了、干了、走了。
    /// 为什么不让个体一直站在田里：那会把一个人的全部时间吃掉，
    /// 于是"饥荒时大家都去种地"会变成"没人去找吃的"，经济直接崩掉。
    /// 一次访问 = 一份劳动量，反而让"要不要派这个人下地"成为一个真实的选择。
    ///
    /// 目标用**目标格**反查建筑（`Tile.BuildingId`），因此不需要给 AgentStore
    /// 再加一个"目标建筑"字段 —— 少一个字段就少一处存档与摘要的维护点。
    /// </summary>
    private void TickFarm(int slot, long tick)
    {
        if (!_store.HasTarget(slot))
        {
            Fail(slot, ActionFailReason.TargetGone);
            return;
        }

        Int2 target = _store.TargetOf(slot);
        if (!_sim.World.IsInBounds(target.X, target.Y))
        {
            Fail(slot, ActionFailReason.TargetGone);
            return;
        }

        int buildingId = _sim.World.TileAt(target.X, target.Y).BuildingId;
        if (buildingId <= 0)
        {
            Fail(slot, ActionFailReason.TargetGone);
            return;
        }

        int index = buildingId - 1;
        if (!_sim.Buildings.IsAlive(index)
            || _sim.Buildings.KindOf(index) != BuildingKind.Farm
            || _sim.Buildings.StateOf(index) != BuildingState.Complete)
        {
            Fail(slot, ActionFailReason.TargetGone);
            return;
        }

        BuildingConfig cfg = _sim.Config.Buildings;
        float cap = System.Math.Max(0.01f, cfg.FarmLaborPerDayCap);
        if (_sim.Buildings.LaborOf(index) < cap)
        {
            _sim.Buildings.AddLabor(index, System.Math.Max(0.01f, cfg.FarmWorkPerAction));
            FarmVisits++;
        }

        // 田里今天的活已经满了也走"完成"而不是"失败"：
        // 失败会留下失败原因并触发重决策，看起来像出了问题，其实一切正常。
        Complete(slot, tick);
    }

    /// <summary>累计耕种次数（观测"农业是否真的在运转"）。</summary>
    public long FarmVisits { get; private set; }

    private static BuildingKind KindForBuildAction(ActionKind action)
    {
        switch (action)
        {            case ActionKind.BuildHouse: return BuildingKind.House;
            case ActionKind.BuildStorage: return BuildingKind.Storage;
            case ActionKind.BuildFarm: return BuildingKind.Farm;
            default: return BuildingKind.None;
        }
    }

    /// <summary>把随身物资存进最近的仓库（M3）。</summary>
    private void TickStoreInBuilding(int slot, long tick)
    {
        if (_sim.Buildings == null || _sim.Storage == null)
        {
            Fail(slot, ActionFailReason.PrerequisiteLost);
            return;
        }

        Int2 position = _store.HasTarget(slot) ? _store.TargetOf(slot) : new Int2(_store.XOf(slot), _store.YOf(slot));

        // 用"目标格"上的建筑：这比重新搜一遍更可靠（选靶与执行用的是同一个坐标）
        if (!_sim.World.IsInBounds(position.X, position.Y))
        {
            Fail(slot, ActionFailReason.TargetGone);
            return;
        }

        int buildingId = _sim.World.TileAt(position.X, position.Y).BuildingId;
        if (buildingId <= 0)
        {
            Fail(slot, ActionFailReason.TargetGone);
            return;
        }

        int buildingIndex = buildingId - 1;
        if (!_sim.Buildings.IsAlive(buildingIndex)
            || _sim.Buildings.KindOf(buildingIndex) != BuildingKind.Storage
            || _sim.Buildings.StateOf(buildingIndex) != BuildingState.Complete)
        {
            Fail(slot, ActionFailReason.TargetGone);
            return;
        }

        float moved = 0f;
        for (int kind = (int)ResourceKind.Food; kind <= (int)ResourceKind.Iron; kind++)
        {
            ResourceKind resource = (ResourceKind)kind;
            float carried = _store.InventoryOf(slot, resource);
            if (carried <= 0f) { continue; }

            float wanted = carried < _config.GroundStocks.DropAmount ? carried : _config.GroundStocks.DropAmount;
            float accepted = _sim.Storage.Deposit(buildingIndex, resource, wanted);
            if (accepted <= 0f) { continue; }

            _store.AddInventory(slot, resource, -accepted);
            moved += accepted;
        }

        if (moved <= 0f)
        {
            Fail(slot, ActionFailReason.PrerequisiteLost);
            return;
        }

        StoresIntoBuilding++;
        Complete(slot, tick);
    }

    /// <summary>取回物资：从地面堆里取自己最缺的东西（食物优先）。</summary>
    private void TickTake(int slot, long tick)
    {
        GroundStockConfig config = _config.GroundStocks;
        GroundStockStore stocks = _sim.GroundStocks;
        Int2 position = _store.HasTarget(slot) ? _store.TargetOf(slot) : new Int2(_store.XOf(slot), _store.YOf(slot));

        float moved = 0f;
        for (int kind = (int)ResourceKind.Food; kind <= (int)ResourceKind.Iron; kind++)
        {
            ResourceKind resource = (ResourceKind)kind;
            if (_store.InventoryOf(slot, resource) >= config.TakeAmount) { continue; }

            float taken = stocks.Withdraw(position.X, position.Y, resource, config.TakeAmount);
            if (taken <= 0f) { continue; }

            _store.AddInventory(slot, resource, taken);
            moved += taken;
            break;   // 一次只取一种，避免"一次把所有东西都搬空"
        }

        if (moved <= 0f)
        {
            Fail(slot, ActionFailReason.TargetGone);
            return;
        }

        TotalTaken += moved;
        Complete(slot, tick);
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
    /// <summary>读档时恢复累计统计（不影响演化，只有报告与诊断读它们）。</summary>
    public void RestoreCounters(float totalFoodEaten, float totalDeposited, float totalTaken)
    {
        TotalFoodEaten = totalFoodEaten;
        TotalDeposited = totalDeposited;
        TotalTaken = totalTaken;
    }

    public void ResetStatistics()
    {
        TotalCompleted = 0;
        TotalFailed = 0;
        TotalFoodEaten = 0f;
        TotalSleepTicks = 0;
        TotalHunted = 0;
        TotalHuntedFood = 0f;
        TotalDeposited = 0f;
        TotalTaken = 0f;
        BuildsStarted = 0;
        StoresIntoBuilding = 0;
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
    // ---------------------------------------------------------------------
    // M6：人与人之间的四个动作
    //
    // 共同点：它们的"目标"是一个**人**，而 AgentStore 里存的是位置。
    // 因此执行阶段必须**按位置重新找一遍人**（而不是记住槽位）：
    //   * 记住槽位的话，对方走开了你还会对着空气社交；
    //   * 重新找则自然表达"我去到那儿，和当时在那儿的人互动"。
    // 代价是同一个人可能在走到半路时换了对象 —— 而那恰恰是真实的行为。
    // ---------------------------------------------------------------------

    /// <summary>社交：和身边的人相处一会儿，双方关系变好、社交需求被满足。</summary>
    private void TickSocialize(int slot, long tick)
    {
        if (!TryFindNeighbor(slot, _config.Relationship.SocializeRadius, out int other))
        {
            // 人走了（或自己走偏了）：再走近一次，否则放弃。
            if (!TryResolvePartnerTarget(slot, _config.Relationship.SocializeRadius))
            {
                Fail(slot, ActionFailReason.TargetGone);
                return;
            }
            return;
        }

        // 双方都受益：社交是**对称**的，因此两边各记一次互动
        // （`Interact` 本身也是对称的，写一次就够了 —— 这里只写一次）。
        _sim.Relationships.Interact(slot, other, _config.Relationship.SocializeGain, tick);

        _store.SetSocial(slot, 1f);
        _store.SetSocial(other, 1f);

        Complete(slot, tick);
    }

    /// <summary>分享食物：把自己的一部分食物给附近最饿的人。</summary>
    private void TickShareFood(int slot, long tick)
    {
        RelationshipConfig rel = _config.Relationship;
        float carried = _store.InventoryOf(slot, ResourceKind.Food);
        if (carried < rel.ShareFoodAmount)
        {
            Fail(slot, ActionFailReason.TargetGone);
            return;
        }

        if (!TryFindHungriestNeighbor(slot, rel.SocializeRadius, out int other))
        {
            Fail(slot, ActionFailReason.TargetGone);
            return;
        }

        float amount = rel.ShareFoodAmount;
        if (amount > carried) { amount = carried; }

        _store.AddInventory(slot, ResourceKind.Food, -amount);
        _store.AddInventory(other, ResourceKind.Food, amount);

        // 分享关系：亲和度提升，而且顺手把对方的饥饿往下压一点
        _sim.Relationships.Interact(slot, other, rel.ShareFoodGain, tick);

        _sim.Events.Record(
            tick,
            History.WorldEventType.AgentSharedFood,
            _store.NameOrOverride(slot) + " 分享食物给 " + _store.NameOrOverride(other),
            History.EventImportance.Normal,
            new Int2(_store.XOf(slot), _store.YOf(slot)),
            slot,
            other,
            "分享 " + amount.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));

        Complete(slot, tick);
    }

    /// <summary>逃跑：只要还在威胁附近就继续跑，脱离之后结束。</summary>
    private void TickFlee(int slot, long tick)
    {
        if (!TryFindThreat(slot, 8f, out int threat))
        {
            Complete(slot, tick);
            return;
        }

        // 还在威胁范围内：重新选一个更远的方向继续跑
        if (!TryResolveFleeTarget(slot, threat))
        {
            // 无路可逃（被逼到角落）：接受现实，结束动作让对方有机会动手
            Complete(slot, tick);
        }
    }

    /// <summary>攻击：对身边关系敌对的人造成伤害，并让双方关系进一步恶化。</summary>
    private void TickAttack(int slot, long tick)
    {
        if (_config.Rules.PeaceMode)
        {
            Fail(slot, ActionFailReason.TargetGone);
            return;
        }

        if (!TryFindThreat(slot, 2f, out int victim))
        {
            if (!TryResolvePartnerTarget(slot, 4f))
            {
                Fail(slot, ActionFailReason.TargetGone);
            }
            return;
        }

        float damage = _config.Relationship.AttackDamage;
        _store.SetHealth(victim, _store.HealthOf(victim) - damage);

        // 攻击是**单向的伤害**，但关系是**对称的恶化**：
        // 被打的人也会记住这件事（`Interact` 的对称性保证了这一点）。
        _sim.Relationships.Interact(slot, victim, -_config.Relationship.AttackLoss, tick);

        if (_config.Relationship.AttackRaisesAggression)
        {
            _store.SetHealth(slot, _store.HealthOf(slot) - (damage * 0.25f));
        }

        _sim.Events.Record(
            tick,
            History.WorldEventType.AgentAttacked,
            _store.NameOrOverride(slot) + " 攻击了 " + _store.NameOrOverride(victim),
            History.EventImportance.Important,
            new Int2(_store.XOf(victim), _store.YOf(victim)),
            slot,
            victim,
            "伤害 " + damage.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));

        Complete(slot, tick);
    }

    /// <summary>找指定半径内最近的另一个个体（槽位升序 ⇒ 平局取较小槽位）。</summary>
    private bool TryFindNeighbor(int slot, float radius, out int found)
    {
        found = -1;
        float best = float.MaxValue;
        float radiusSq = radius * radius;

        int x = _store.XOf(slot);
        int y = _store.YOf(slot);
        int[] slots = _store.LiveSlotsRaw(out int liveCount);

        for (int k = 0; k < liveCount; k++)
        {
            int other = slots[k];
            if (other == slot) { continue; }

            float dx = _store.XOf(other) - x;
            float dy = _store.YOf(other) - y;
            float distSq = (dx * dx) + (dy * dy);
            if (distSq > radiusSq || distSq >= best) { continue; }

            best = distSq;
            found = other;
        }

        return found >= 0;
    }

    private bool TryFindHungriestNeighbor(int slot, float radius, out int found)
    {
        found = -1;
        float worst = 0f;
        float radiusSq = radius * radius;

        int x = _store.XOf(slot);
        int y = _store.YOf(slot);
        int[] slots = _store.LiveSlotsRaw(out int liveCount);

        for (int k = 0; k < liveCount; k++)
        {
            int other = slots[k];
            if (other == slot) { continue; }

            float dx = _store.XOf(other) - x;
            float dy = _store.YOf(other) - y;
            if ((dx * dx) + (dy * dy) > radiusSq) { continue; }

            float hunger = _store.HungerOf(other);
            if (hunger <= worst) { continue; }

            worst = hunger;
            found = other;
        }

        return found >= 0;
    }

    private bool TryFindThreat(int slot, float radius, out int threat)
    {
        threat = -1;
        float worst = 0f;
        float radiusSq = radius * radius;

        int x = _store.XOf(slot);
        int y = _store.YOf(slot);
        int[] slots = _store.LiveSlotsRaw(out int liveCount);

        for (int k = 0; k < liveCount; k++)
        {
            int other = slots[k];
            if (other == slot) { continue; }

            float dx = _store.XOf(other) - x;
            float dy = _store.YOf(other) - y;
            if ((dx * dx) + (dy * dy) > radiusSq) { continue; }

            float affinity = _sim.Relationships.AffinityOf(slot, other);
            if (affinity > _config.Relationship.HostileAffinityThreshold) { continue; }

            float hostility = SimMath.Clamp01(-affinity);
            if (hostility <= worst) { continue; }

            worst = hostility;
            threat = other;
        }

        return threat >= 0;
    }

    /// <summary>重新把目标设为某个邻居的位置（用于"对方走开了"的情形）。</summary>
    private bool TryResolvePartnerTarget(int slot, float radius)
    {
        if (!TryFindNeighbor(slot, radius, out int other)) { return false; }

        _store.SetTarget(slot, _store.XOf(other), _store.YOf(other));
        _store.SetPhase(slot, ActionPhase.Moving);
        _store.SetState(slot, AgentState.Moving);
        return true;
    }

    private bool TryResolveFleeTarget(int slot, int threat)
    {
        int stepX = _store.XOf(slot) - _store.XOf(threat);
        int stepY = _store.YOf(slot) - _store.YOf(threat);
        stepX = stepX == 0 ? 1 : System.Math.Sign(stepX);
        stepY = stepY == 0 ? 0 : System.Math.Sign(stepY);

        int nx = _store.XOf(slot) + (stepX * 3);
        int ny = _store.YOf(slot) + (stepY * 3);
        if (!_sim.World.IsInBounds(nx, ny)) { return false; }
        if (!_sim.World.TileAt(nx, ny).Walkable) { return false; }

        _store.SetTarget(slot, nx, ny);
        _store.SetPhase(slot, ActionPhase.Moving);
        _store.SetState(slot, AgentState.Fleeing);
        return true;
    }
}