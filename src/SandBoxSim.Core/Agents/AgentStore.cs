using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Ai;

namespace SandBoxSim.Core.Agents;

/// <summary>
/// 面向对象的结构体数组（SoA）Agent 存储。
///
/// 为什么用 SoA 而不是 `List&lt;Agent&gt;` 或 class：
///   1. 决策与需求更新会遍历**全部**个体的**同一类字段**，SoA 的访问模式是连续的，
///      缓存命中率远高于"每个对象 200 字节、跳着访问"；
///   2. 没有对象引用 ⇒ GC 压力接近零，长期运行（几十万 tick）不会出现停顿；
///   3. 状态摘要可以直接按数组顺序遍历，天然确定。
///
/// 槽位复用 + 代次（generation）：个体死亡后槽位会被新个体复用，
/// 外部持有的 <see cref="AgentRef"/> 因此带代次校验，避免"看错人"。
/// </summary>
public sealed class AgentStore : ISimEntitySet
{
    // ---- 容量与统计 ----

    private int[] _generation = System.Array.Empty<int>();
    private bool[] _alive = System.Array.Empty<bool>();
    private int _capacity;
    private int _liveCount;
    private int _peakPopulation;

    /// <summary>存活槽位前缀数组（按槽位升序；前 _liveCount 项有效）。见 LiveSlotsRaw。</summary>
    private int[] _liveSlots = System.Array.Empty<int>();

    /// <summary>
    /// 迁移意愿的到期时刻（tick）。0 或已过期 = 不想搬家。
    ///
    /// 为什么需要一个独立的标志位，而不是只靠"家离得很远"来判断：
    /// "离家距离"是**结果**，不是**意愿**。用它当判据会得到一个荒谬的自反馈 ——
    /// 一旦个体因为任何原因离家远了，"迁移"动作的效用就变高，
    /// 于是它一直被选中、一直在"往新家走"，其它一切行为（采集/存放/狩猎）都被饿死。
    /// 实测：40 天里 106,354 次决策中有 92,656 次选中了"迁移"（87%）。
    ///
    /// 正确做法：迁移意愿只由 <c>MigrationSystem</c> 在每日评估时授予，
    /// 并带一个到期时刻；动作层只负责"意愿还存在时朝新家走"。
    /// 这就是"**意图与执行分离**"在个体内部的具体应用。
    /// </summary>
    private long[] _migrateUntil = System.Array.Empty<long>();

    /// <summary>下一个可用的槽位提示（避免每次分配都从头扫）。</summary>
    private int _nextFreeHint;

    // ---- 位置与运动 ----

    private int[] _x = System.Array.Empty<int>();
    private int[] _y = System.Array.Empty<int>();
    private int[] _prevX = System.Array.Empty<int>();
    private int[] _prevY = System.Array.Empty<int>();
    private int[] _homeX = System.Array.Empty<int>();
    private int[] _homeY = System.Array.Empty<int>();
    private byte[] _facing = System.Array.Empty<byte>();

    // ---- 需求（0 = 满足，1 = 极端） ----

    private float[] _hunger = System.Array.Empty<float>();
    private float[] _fatigue = System.Array.Empty<float>();
    private float[] _thirst = System.Array.Empty<float>();
    private float[] _social = System.Array.Empty<float>();

    // ---- 生理与身份 ----

    private float[] _health = System.Array.Empty<float>();
    private short[] _ageDays = System.Array.Empty<short>();
    private byte[] _lifeStage = System.Array.Empty<byte>();
    private byte[] _job = System.Array.Empty<byte>();
    private byte[] _deathCause = System.Array.Empty<byte>();
    private long[] _birthTick = System.Array.Empty<long>();
    private long[] _deathTick = System.Array.Empty<long>();

    // ---- 库存（4 种资源各一个数组 = 更好读，也更省内存） ----

    private float[] _invFood = System.Array.Empty<float>();
    private float[] _invWood = System.Array.Empty<float>();
    private float[] _invStone = System.Array.Empty<float>();
    private float[] _invIron = System.Array.Empty<float>();

    // ---- 性格 ----

    private float[] _aggression = System.Array.Empty<float>();
    private float[] _greed = System.Array.Empty<float>();
    private float[] _kindness = System.Array.Empty<float>();
    private float[] _bravery = System.Array.Empty<float>();
    private float[] _industriousness = System.Array.Empty<float>();
    private float[] _sociability = System.Array.Empty<float>();

    // ---- 决策与动作状态 ----

    private byte[] _state = System.Array.Empty<byte>();
    private byte[] _action = System.Array.Empty<byte>();
    private byte[] _actionPhase = System.Array.Empty<byte>();
    private byte[] _failReason = System.Array.Empty<byte>();
    private int[] _targetX = System.Array.Empty<int>();
    private int[] _targetY = System.Array.Empty<int>();
    private int[] _pathNextX = System.Array.Empty<int>();
    private int[] _pathNextY = System.Array.Empty<int>();
    private bool[] _hasPathStep = System.Array.Empty<bool>();
    private int[] _actionTicks = System.Array.Empty<int>();

    /// <summary>每个个体的决策相位（分批更新用：phase = Slot % batchCount）。</summary>
    private int[] _decisionPhase = System.Array.Empty<int>();

    /// <summary>下一次允许重新决策的 tick（防止每 tick 都改主意）。</summary>
    private long[] _nextDecisionTick = System.Array.Empty<long>();

    // ---- 存档用：上次决策的打分 ----

    private UtilityBreakdown[] _lastDecision = System.Array.Empty<UtilityBreakdown>();

    // ---- 命名 ----

    private static readonly string[] SyllablesA =
    {
        "Al", "Bri", "Ca", "Dor", "El", "Fa", "Gra", "Ha", "I", "Jo",
        "Ke", "Li", "Ma", "Ne", "Or", "Pa", "Ro", "Si", "Ta", "Ur",
        "Va", "We", "Xi", "Ya", "Ze", "Kai", "Mer", "Nas", "Ol", "Pri",
    };

    private static readonly string[] SyllablesB =
    {
        "ra", "na", "li", "mo", "ta", "ve", "si", "ka", "no", "de",
        "wu", "sha", "rin", "bel", "dor", "fen", "gil", "han", "ju", "kel",
    };

    private static readonly string[] Suffixes = { "a", "u", "o", "in", "en", "is", "ar", "os" };

    /// <summary>存档恢复用的名字覆盖表（键为槽位）。</summary>
    private readonly System.Collections.Generic.Dictionary<int, string> _nameOverrides =
        new System.Collections.Generic.Dictionary<int, string>();

    public int Capacity => _capacity;

    /// <summary>存活个体数。</summary>
    public int EntityCount => _liveCount;

    public int LiveCount => _liveCount;

    /// <summary>历史最高人口（统计与报告用）。</summary>
    public int PeakPopulation => _peakPopulation;

    /// <summary>累计出生数（含初始放置）。</summary>
    public int TotalBorn { get; private set; }

    /// <summary>累计死亡数。</summary>
    public int TotalDied { get; private set; }

    public int NameHashSeed { get; set; } = 20251003;

    public AgentStore(int capacity = 512)
    {
        Resize(capacity > 0 ? capacity : 512);
    }

    // ---------------------------------------------------------------------
    // 容量
    // ---------------------------------------------------------------------

    private void Resize(int capacity)
    {
        if (capacity < 1) { capacity = 1; }

        System.Array.Resize(ref _generation, capacity);
        System.Array.Resize(ref _alive, capacity);
        System.Array.Resize(ref _x, capacity);
        System.Array.Resize(ref _y, capacity);
        System.Array.Resize(ref _prevX, capacity);
        System.Array.Resize(ref _prevY, capacity);
        System.Array.Resize(ref _homeX, capacity);
        System.Array.Resize(ref _homeY, capacity);
        System.Array.Resize(ref _facing, capacity);
        System.Array.Resize(ref _hunger, capacity);
        System.Array.Resize(ref _fatigue, capacity);
        System.Array.Resize(ref _thirst, capacity);
        System.Array.Resize(ref _social, capacity);
        System.Array.Resize(ref _health, capacity);
        System.Array.Resize(ref _ageDays, capacity);
        System.Array.Resize(ref _lifeStage, capacity);
        System.Array.Resize(ref _job, capacity);
        System.Array.Resize(ref _deathCause, capacity);
        System.Array.Resize(ref _birthTick, capacity);
        System.Array.Resize(ref _deathTick, capacity);
        System.Array.Resize(ref _invFood, capacity);
        System.Array.Resize(ref _invWood, capacity);
        System.Array.Resize(ref _invStone, capacity);
        System.Array.Resize(ref _invIron, capacity);
        System.Array.Resize(ref _aggression, capacity);
        System.Array.Resize(ref _greed, capacity);
        System.Array.Resize(ref _kindness, capacity);
        System.Array.Resize(ref _bravery, capacity);
        System.Array.Resize(ref _industriousness, capacity);
        System.Array.Resize(ref _sociability, capacity);
        System.Array.Resize(ref _state, capacity);
        System.Array.Resize(ref _action, capacity);
        System.Array.Resize(ref _actionPhase, capacity);
        System.Array.Resize(ref _failReason, capacity);
        System.Array.Resize(ref _targetX, capacity);
        System.Array.Resize(ref _targetY, capacity);
        System.Array.Resize(ref _pathNextX, capacity);
        System.Array.Resize(ref _pathNextY, capacity);
        System.Array.Resize(ref _hasPathStep, capacity);
        System.Array.Resize(ref _actionTicks, capacity);
        System.Array.Resize(ref _decisionPhase, capacity);
        System.Array.Resize(ref _nextDecisionTick, capacity);
        System.Array.Resize(ref _lastDecision, capacity);
        System.Array.Resize(ref _migrateUntil, capacity);
        System.Array.Resize(ref _liveSlots, capacity);

        _capacity = capacity;
    }

    /// <summary>按需扩容（人口增长超过容量时调用）。</summary>
    public void EnsureCapacity(int required)
    {
        if (required <= _capacity) { return; }
        int next = _capacity;
        while (next < required) { next *= 2; }
        Resize(next);
    }

    // ---------------------------------------------------------------------
    // 生命周期
    // ---------------------------------------------------------------------

    /// <summary>
    /// 放置一个个体的初始位置：优先用给定的期望位置，若不可走就在附近螺旋寻找。
    /// 返回 false 表示地图上没有可站的地方（病态情况，调用方应报错而不是静默失败）。
    /// </summary>
    public bool FindSpawnPosition(Environment.World world, int preferredX, int preferredY, int radius, out int x, out int y)
    {
        x = preferredX;
        y = preferredY;

        if (world.IsInBounds(x, y) && world.TileAt(x, y).Walkable) { return true; }

        for (int r = 1; r <= radius; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    // 只看环上的格子（内部已经在上一次迭代里检查过）
                    int cheb = System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy));
                    if (cheb != r) { continue; }

                    int cx = preferredX + dx;
                    int cy = preferredY + dy;
                    if (!world.IsInBounds(cx, cy)) { continue; }
                    if (!world.TileAt(cx, cy).Walkable) { continue; }

                    x = cx;
                    y = cy;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 新增一个个体。
    /// </summary>
    /// <param name="world">用于校验出生点可走。</param>
    /// <param name="spawnX">期望出生点 X。</param>
    /// <param name="spawnY">期望出生点 Y。</param>
    /// <param name="rng">性格抽样用（必须由调用方指定流，保证确定性）。</param>
    /// <param name="ageDays">初始年龄（天）。</param>
    /// <param name="birthTick">出生时刻（tick）；用于事件日志与个人时间线。</param>
    public AgentRef Add(Environment.World world, int spawnX, int spawnY, DeterministicRandom rng, int ageDays = 0, long birthTick = 0)
    {
        int slot = AllocateSlot();
        if (slot < 0) { return AgentRef.None; }

        if (!FindSpawnPosition(world, spawnX, spawnY, 24, out int x, out int y))
        {
            // 没有可站的位置：回滚槽位，避免出现"半死不活"的个体
            ReleaseSlot(slot);
            return AgentRef.None;
        }

        Personality p = Personality.Sample(rng);

        _x[slot] = x;
        _y[slot] = y;
        _prevX[slot] = x;
        _prevY[slot] = y;

        // "家"= 出生点。M1 没有建筑，用出生点作为活动中心即可表达"离家惩罚"；
        // M3 有了房屋之后会改成真实住所（届时这里的语义自然升级）。
        _homeX[slot] = x;
        _homeY[slot] = y;
        _facing[slot] = 0;

        // 初始需求带一点随机：避免所有人同时饿、同时睡（否则世界会"同频"，看起来像机器）
        _hunger[slot] = (float)(rng.NextDouble() * 0.15);
        _fatigue[slot] = (float)(rng.NextDouble() * 0.25);
        _thirst[slot] = (float)(rng.NextDouble() * 0.2);
        _social[slot] = (float)(rng.NextDouble() * 0.4);

        _health[slot] = 1f;
        _ageDays[slot] = (short)SimMath.Clamp(ageDays, 0, 32000);
        _lifeStage[slot] = (byte)LifeStage.Adult;
        _job[slot] = (byte)JobType.None;
        _deathCause[slot] = (byte)DeathCause.None;
        _birthTick[slot] = birthTick;
        _deathTick[slot] = -1;

        _invFood[slot] = 0f;
        _invWood[slot] = 0f;
        _invStone[slot] = 0f;
        _invIron[slot] = 0f;

        _aggression[slot] = p.Aggression;
        _greed[slot] = p.Greed;
        _kindness[slot] = p.Kindness;
        _bravery[slot] = p.Bravery;
        _industriousness[slot] = p.Industriousness;
        _sociability[slot] = p.Sociability;

        _state[slot] = (byte)AgentState.Idle;
        _action[slot] = (byte)ActionKind.None;
        _actionPhase[slot] = (byte)ActionPhase.Idle;
        _failReason[slot] = (byte)ActionFailReason.None;
        _targetX[slot] = -1;
        _targetY[slot] = -1;
        _pathNextX[slot] = -1;
        _pathNextY[slot] = -1;
        _hasPathStep[slot] = false;
        _actionTicks[slot] = 0;

        // 决策相位：用个体的名字哈希（与位置无关）分散，保证"同一个人永远在同一相位"
        _decisionPhase[slot] = 0;
        _nextDecisionTick[slot] = 0;
        _lastDecision[slot] = default;
        _migrateUntil[slot] = 0;

        _liveCount++;
        TotalBorn++;
        if (_liveCount > _peakPopulation) { _peakPopulation = _liveCount; }
        RebuildLiveSlots();

        return new AgentRef(slot, _generation[slot]);
    }

    /// <summary>
    /// 杀死一个个体的记录（需求/年龄系统调用）。
    /// 注意：<paramref name="slot"/> 的数组内容会**保留**（不清理），
    /// 这样死亡时刻的位置/年龄/死因还能被事件日志与报告读到；只有代次与存活标志变化。
    /// </summary>
    public void MarkDead(int slot, DeathCause cause, long tick)
    {
        if (!IsSlotAlive(slot)) { return; }

        _alive[slot] = false;
        _state[slot] = (byte)AgentState.Dead;
        _lifeStage[slot] = (byte)LifeStage.Dead;
        _action[slot] = (byte)ActionKind.None;
        _actionPhase[slot] = (byte)ActionPhase.Idle;
        _hasPathStep[slot] = false;
        _deathCause[slot] = (byte)cause;
        _deathTick[slot] = tick;
        _migrateUntil[slot] = 0;

        // 代次 +1：所有旧引用立刻失效
        _generation[slot]++;
        _liveCount--;
        TotalDied++;
        RebuildLiveSlots();

        if (slot < _nextFreeHint) { _nextFreeHint = slot; }
    }

    public DeathCause DeathCauseOf(int slot) => (DeathCause)_deathCause[slot];
    public long BirthTickOf(int slot) => _birthTick[slot];
    public long DeathTickOf(int slot) => _deathTick[slot];

    private int AllocateSlot()
    {
        // 先在线性区找空位（通常就是 _nextFreeHint 或它后面一格）
        for (int i = _nextFreeHint; i < _capacity; i++)
        {
            if (!_alive[i]) { _nextFreeHint = i; return ClaimSlot(i); }
        }
        // 再从头找（处理死亡后释放的低位槽位）
        for (int i = 0; i < _capacity; i++)
        {
            if (!_alive[i]) { _nextFreeHint = i; return ClaimSlot(i); }
        }
        return -1;   // 满了：调用方应先 EnsureCapacity
    }

    private int ClaimSlot(int slot)
    {
        _alive[slot] = true;
        return slot;
    }

    private void ReleaseSlot(int slot)
    {
        _alive[slot] = false;
        _generation[slot]++;
        if (slot < _nextFreeHint) { _nextFreeHint = slot; }
    }

    // ---------------------------------------------------------------------
    // 查询
    // ---------------------------------------------------------------------

    public bool IsSlotAlive(int slot) => slot >= 0 && slot < _capacity && _alive[slot];

    public bool IsValid(AgentRef reference)
        => reference.Slot >= 0 && reference.Slot < _capacity
           && _alive[reference.Slot] && _generation[reference.Slot] == reference.Generation;

    public int GenerationOf(int slot) => slot >= 0 && slot < _capacity ? _generation[slot] : -1;

    public AgentRef RefOf(int slot)
        => IsSlotAlive(slot) ? new AgentRef(slot, _generation[slot]) : AgentRef.None;

    public int SlotOf(AgentRef reference) => IsValid(reference) ? reference.Slot : -1;

    public Int2 PositionOf(int slot) => new Int2(_x[slot], _y[slot]);
    public Int2 PrevPositionOf(int slot) => new Int2(_prevX[slot], _prevY[slot]);
    public int XOf(int slot) => _x[slot];
    public int YOf(int slot) => _y[slot];

    /// <summary>出生点 / 活动中心（M3 之后会变成真实住所位置）。</summary>
    public int HomeXOf(int slot) => _homeX[slot];
    public int HomeYOf(int slot) => _homeY[slot];

    /// <summary>设置住所位置（建好房子后由建造系统调用）。</summary>
    public void SetHome(int slot, int x, int y)
    {
        _homeX[slot] = x;
        _homeY[slot] = y;
    }

    public byte FacingOf(int slot) => _facing[slot];

    public float HungerOf(int slot) => _hunger[slot];
    public float FatigueOf(int slot) => _fatigue[slot];
    public float ThirstOf(int slot) => _thirst[slot];
    public float SocialOf(int slot) => _social[slot];
    public float HealthOf(int slot) => _health[slot];
    public int AgeDaysOf(int slot) => _ageDays[slot];
    public LifeStage LifeStageOf(int slot) => (LifeStage)_lifeStage[slot];
    public JobType JobOf(int slot) => (JobType)_job[slot];
    public AgentState StateOf(int slot) => (AgentState)_state[slot];
    public ActionKind ActionOf(int slot) => (ActionKind)_action[slot];
    public ActionPhase PhaseOf(int slot) => (ActionPhase)_actionPhase[slot];
    public ActionFailReason FailReasonOf(int slot) => (ActionFailReason)_failReason[slot];
    public int ActionTicksOf(int slot) => _actionTicks[slot];
    public Int2 TargetOf(int slot) => new Int2(_targetX[slot], _targetY[slot]);
    public bool HasTarget(int slot) => _targetX[slot] >= 0 && _targetY[slot] >= 0;
    public bool HasPathStep(int slot) => _hasPathStep[slot];
    public Int2 PathStepOf(int slot) => new Int2(_pathNextX[slot], _pathNextY[slot]);
    public long NextDecisionTickOf(int slot) => _nextDecisionTick[slot];
    public int DecisionPhaseOf(int slot) => _decisionPhase[slot];
    public ref readonly UtilityBreakdown LastDecisionOf(int slot) => ref _lastDecision[slot];

    public Personality PersonalityOf(int slot) => new Personality
    {
        Aggression = _aggression[slot],
        Greed = _greed[slot],
        Kindness = _kindness[slot],
        Bravery = _bravery[slot],
        Industriousness = _industriousness[slot],
        Sociability = _sociability[slot],
    };

    public float InventoryOf(int slot, ResourceKind kind)
    {
        switch (kind)
        {
            case ResourceKind.Food: return _invFood[slot];
            case ResourceKind.Wood: return _invWood[slot];
            case ResourceKind.Stone: return _invStone[slot];
            case ResourceKind.Iron: return _invIron[slot];
            default: return 0f;
        }
    }

    public ResourceStock StockOf(int slot) => new ResourceStock
    {
        Food = _invFood[slot],
        Wood = _invWood[slot],
        Stone = _invStone[slot],
        Iron = _invIron[slot],
    };

    /// <summary>个体库存总物量（跨聚落搬运与"负重"判定的输入）。</summary>
    public float InventoryTotalOf(int slot)
        => _invFood[slot] + _invWood[slot] + _invStone[slot] + _invIron[slot];

    // ---------------------------------------------------------------------
    // 修改（只有系统层会调用；UI 必须走 Simulation 的干预接口）
    // ---------------------------------------------------------------------

    public void SetPosition(int slot, int x, int y)
    {
        _prevX[slot] = _x[slot];
        _prevY[slot] = _y[slot];
        _x[slot] = x;
        _y[slot] = y;
    }

    /// <summary>记录朝向（八方向，仅用于渲染）。</summary>
    public void SetFacing(int slot, byte facing) => _facing[slot] = facing;

    public void SetNeed(int slot, NeedIndex need, float value)
    {
        float v = SimMath.Clamp01(value);
        switch (need)
        {
            case NeedIndex.Hunger: _hunger[slot] = v; break;
            case NeedIndex.Fatigue: _fatigue[slot] = v; break;
            case NeedIndex.Thirst: _thirst[slot] = v; break;
            case NeedIndex.Social: _social[slot] = v; break;
            default: break;
        }
    }

    public float NeedOf(int slot, NeedIndex need)
    {
        switch (need)
        {
            case NeedIndex.Hunger: return _hunger[slot];
            case NeedIndex.Fatigue: return _fatigue[slot];
            case NeedIndex.Thirst: return _thirst[slot];
            case NeedIndex.Social: return _social[slot];
            default: return 0f;
        }
    }

    public void AddNeed(int slot, NeedIndex need, float delta)
        => SetNeed(slot, need, NeedOf(slot, need) + delta);

    public void SetHealth(int slot, float value) => _health[slot] = SimMath.Clamp01(value);
    public void AddHealth(int slot, float delta) => SetHealth(slot, _health[slot] + delta);

    public void SetAgeDays(int slot, int days) => _ageDays[slot] = (short)SimMath.Clamp(days, 0, 32000);

    public void SetLifeStage(int slot, LifeStage stage) => _lifeStage[slot] = (byte)stage;

    public void SetJob(int slot, JobType job) => _job[slot] = (byte)job;

    public void SetInventory(int slot, ResourceKind kind, float value)
    {
        float v = value < 0f ? 0f : value;
        switch (kind)
        {
            case ResourceKind.Food: _invFood[slot] = v; break;
            case ResourceKind.Wood: _invWood[slot] = v; break;
            case ResourceKind.Stone: _invStone[slot] = v; break;
            case ResourceKind.Iron: _invIron[slot] = v; break;
            default: break;
        }
    }

    public void AddInventory(int slot, ResourceKind kind, float delta)
        => SetInventory(slot, kind, InventoryOf(slot, kind) + delta);

    public void SetState(int slot, AgentState state) => _state[slot] = (byte)state;

    public void SetAction(int slot, ActionKind action, ActionPhase phase)
    {
        _action[slot] = (byte)action;
        _actionPhase[slot] = (byte)phase;
        _actionTicks[slot] = 0;
    }

    public void SetPhase(int slot, ActionPhase phase) => _actionPhase[slot] = (byte)phase;

    public void SetFailReason(int slot, ActionFailReason reason) => _failReason[slot] = (byte)reason;

    public void SetTarget(int slot, int x, int y)
    {
        _targetX[slot] = x;
        _targetY[slot] = y;
    }

    public void ClearTarget(int slot)
    {
        _targetX[slot] = -1;
        _targetY[slot] = -1;
        ClearPathStep(slot);
    }

    public void SetPathStep(int slot, int x, int y)
    {
        _pathNextX[slot] = x;
        _pathNextY[slot] = y;
        _hasPathStep[slot] = true;
    }

    public void ClearPathStep(int slot) => _hasPathStep[slot] = false;

    public void SetDecisionPhase(int slot, int phase) => _decisionPhase[slot] = (byte)SimMath.Clamp(phase, 0, 255);

    public void SetNextDecisionTick(int slot, long tick) => _nextDecisionTick[slot] = tick;

    public void IncrementActionTicks(int slot) => _actionTicks[slot]++;

    public void SetLastDecision(int slot, in UtilityBreakdown breakdown)
    {
        // 注意：这里是**值拷贝**（UtilityBreakdown 内含数组），
        // 因此每次决策会分配一次小数组。分批决策把它摊薄到可接受的水平。
        _lastDecision[slot] = breakdown;
    }

    /// <summary>
    /// 为所有存活个体重新分配决策相位。
    /// 分批决策要求 phase = Slot % batchCount；但槽位复用会让同一个槽位的相位保持一致，
    /// 因此这里用"名字哈希 + 槽位"混合，保证同一批里的分布均匀。
    /// </summary>
    public void AssignDecisionPhases(int batchCount)
    {
        if (batchCount < 1) { batchCount = 1; }
        for (int slot = 0; slot < _capacity; slot++)
        {
            if (!_alive[slot]) { continue; }
            int mixed = (slot * 31) + (NameHashSeed & 0xFF);
            _decisionPhase[slot] = mixed % batchCount;
        }
    }

    // ---------------------------------------------------------------------
    // 遍历辅助
    // ---------------------------------------------------------------------

    /// <summary>按槽位顺序遍历所有存活个体（顺序固定 ⇒ 确定性）。</summary>
    public System.Collections.Generic.IEnumerable<int> AliveSlots()
    {
        for (int slot = 0; slot < _capacity; slot++)
        {
            if (_alive[slot]) { yield return slot; }
        }
    }

    /// <summary>
    /// 存活槽位的**原始数组与前缀长度**（前 <paramref name="count"/> 项有效，且**按槽位升序**）。
    ///
    /// 为什么要有这个"不够优雅"的接口：高频批量查询（例如"动物每 tick 找附近的人"）
    /// 不能走 <see cref="AliveSlots"/> 那样的迭代器 —— 迭代器每次调用都会分配状态机对象，
    /// 在每 tick 调用数十次、跑几十万 tick 的规模下会变成明显的 GC 压力。
    ///
    /// 与 <c>WildlifeStore</c> 的存活列表不同，这里的顺序**始终是槽位升序**
    /// （用"压缩写入 + 整体重建"维护，而不是末尾交换），因为人的顺序参与确定性判定，不能乱。
    /// </summary>
    public int[] LiveSlotsRaw(out int count)
    {
        count = _liveCount;
        return _liveSlots;
    }

    /// <summary>设置迁移意愿的到期时刻（由 MigrationSystem 调用）。</summary>
    public void SetMigrateUntil(int slot, long tick)
    {
        if (slot >= 0 && slot < _migrateUntil.Length) { _migrateUntil[slot] = tick; }
    }

    /// <summary>当前是否还有迁移意愿（意愿过期即自动清除，不需要额外的清理逻辑）。</summary>
    public bool IsMigrating(int slot, long now)
    {
        if (slot < 0 || slot >= _migrateUntil.Length) { return false; }
        if (_migrateUntil[slot] == 0) { return false; }
        if (now >= _migrateUntil[slot]) { _migrateUntil[slot] = 0; return false; }
        return true;
    }

    /// <summary>清除迁移意愿（到达新家、或放弃迁移时调用）。</summary>
    public void ClearMigrateIntent(int slot)
    {
        if (slot >= 0 && slot < _migrateUntil.Length) { _migrateUntil[slot] = 0; }
    }

    /// <summary>
    /// 重建存活槽位前缀数组。必须在"存活集合发生变化"之后调用（Add / MarkDead / Reset）。
    /// 位置：<see cref="Add"/>、<see cref="MarkDead"/>、<see cref="Reset"/>。
    /// </summary>
    private void RebuildLiveSlots()
    {
        if (_liveSlots.Length < _capacity) { _liveSlots = new int[_capacity]; }

        int count = 0;
        for (int slot = 0; slot < _capacity; slot++)
        {
            if (_alive[slot]) { _liveSlots[count++] = slot; }
        }
        _liveCount = count;
    }

    /// <summary>
    /// 找出离 (x,y) 最近的存活个体（切比雪夫距离）。
    /// 用带最大半径的搜索避免全表扫描：UI 点选与"附近有人吗"都走这里。
    ///
    /// 实现细节：遍历存活槽位前缀数组而不是整个容量数组。
    /// 这一步很关键 —— 动物的"逃跑"逻辑每 tick 都会调用它，
    /// 而容量数组在人口少的时候绝大部分是空的（实测这曾经让整体测试耗时翻了两倍多）。
    /// </summary>
    public bool TryFindNearest(int x, int y, int maxRadius, out int slot, out int distance)
    {
        slot = -1;
        distance = int.MaxValue;

        int count = _liveCount;
        for (int k = 0; k < count; k++)
        {
            int i = _liveSlots[k];
            int d = System.Math.Max(System.Math.Abs(_x[i] - x), System.Math.Abs(_y[i] - y));
            if (d < distance && d <= maxRadius)
            {
                distance = d;
                slot = i;
            }
        }

        return slot >= 0;
    }

    /// <summary>统计某个矩形范围内的存活人数（热力图与聚落评估用）。</summary>
    public int CountInRect(int minX, int minY, int maxX, int maxY)
    {
        int count = 0;
        for (int i = 0; i < _capacity; i++)
        {
            if (!_alive[i]) { continue; }
            if (_x[i] >= minX && _x[i] <= maxX && _y[i] >= minY && _y[i] <= maxY) { count++; }
        }
        return count;
    }

    // ---------------------------------------------------------------------
    // 全图统计（统计系统与报告用；不要放进每 tick 热路径）
    // ---------------------------------------------------------------------

    public float TotalHunger()
    {
        float sum = 0f;
        for (int i = 0; i < _capacity; i++) { if (_alive[i]) { sum += _hunger[i]; } }
        return sum;
    }

    public float TotalFatigue()
    {
        float sum = 0f;
        for (int i = 0; i < _capacity; i++) { if (_alive[i]) { sum += _fatigue[i]; } }
        return sum;
    }

    public float AverageHealth()
    {
        if (_liveCount == 0) { return 0f; }
        float sum = 0f;
        for (int i = 0; i < _capacity; i++) { if (_alive[i]) { sum += _health[i]; } }
        return sum / _liveCount;
    }

    /// <summary>处于"极度饥饿"（≥ 阈值）的人数 —— 饥荒的直接观测量。</summary>
    public int CountStarving(float threshold = 0.8f)
    {
        int count = 0;
        for (int i = 0; i < _capacity; i++)
        {
            if (_alive[i] && _hunger[i] >= threshold) { count++; }
        }
        return count;
    }

    public int CountWorking()
    {
        int count = 0;
        for (int i = 0; i < _capacity; i++)
        {
            if (!_alive[i]) { continue; }
            ActionKind action = (ActionKind)_action[i];
            if (action == ActionKind.GatherFood || action == ActionKind.GatherWood
                || action == ActionKind.GatherStone || action == ActionKind.GatherIron
                || action == ActionKind.BuildHouse || action == ActionKind.BuildFarm
                || action == ActionKind.BuildStorage || action == ActionKind.Farm)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>当前正在移动的人数（用于观察"世界是否在动"）。</summary>
    public int CountMoving()
    {
        int count = 0;
        for (int i = 0; i < _capacity; i++)
        {
            if (_alive[i] && (ActionPhase)_actionPhase[i] == ActionPhase.Moving) { count++; }
        }
        return count;
    }

    /// <summary>职业分布（数组下标 = JobType）。</summary>
    public int[] CountJobs()
    {
        int[] counts = new int[8];
        for (int i = 0; i < _capacity; i++)
        {
            if (!_alive[i]) { continue; }
            int job = _job[i];
            if (job >= 0 && job < counts.Length) { counts[job]++; }
        }
        return counts;
    }

    // ---------------------------------------------------------------------
    // 命名
    // ---------------------------------------------------------------------

    /// <summary>
    /// 生成一个稳定的名字。用确定性哈希而不是 System.Random：
    /// 名字会出现在事件日志与时间线里，必须可复现（同 seed ⇒ 同一个人叫同一个名字）。
    ///
    /// 名字由"音节 A + 音节 B + 词尾"组成，读起来像名字而不是编号；
    /// 但哈希里带槽位与代次，因此不同个体不会重名。
    /// </summary>
    public string NameOf(int slot)
    {
        int generation = (slot >= 0 && slot < _capacity) ? _generation[slot] : 0;

        ulong h = Hash64.Begin();
        h = Hash64.Combine(h, slot * 7919);
        h = Hash64.Combine(h, generation * 104729);
        h = Hash64.Combine(h, NameHashSeed);

        // 把 64 位哈希切成三段，分别用来选音节与词尾
        uint a = (uint)(h & 0xFFFF);
        uint b = (uint)((h >> 16) & 0xFFFF);
        uint c = (uint)((h >> 32) & 0xFFFF);

        string first = SyllablesA[(int)(a % (uint)SyllablesA.Length)];
        string second = SyllablesB[(int)(b % (uint)SyllablesB.Length)];
        string tail = Suffixes[(int)(c % (uint)Suffixes.Length)];
        return first + second + tail;
    }

    /// <summary>重命名（存档恢复用，M5 起使用）。</summary>
    public void SetNameOverride(int slot, string name) => _nameOverrides[slot] = name;

    /// <summary>取名字：有覆盖用覆盖，否则按规则生成。</summary>
    public string NameOrOverride(int slot)
        => _nameOverrides.TryGetValue(slot, out string? name) ? name : NameOf(slot);

    // ---------------------------------------------------------------------
    // ISimEntitySet
    // ---------------------------------------------------------------------

    public ulong HashInto(ulong hash)
    {
        hash = Hash64.Combine(hash, _liveCount);
        for (int i = 0; i < _capacity; i++)
        {
            if (!_alive[i]) { continue; }
            hash = Hash64.Combine(hash, i);
            hash = Hash64.Combine(hash, _generation[i]);
            hash = Hash64.Combine(hash, _x[i]);
            hash = Hash64.Combine(hash, _y[i]);
            hash = Hash64.Combine(hash, _homeX[i]);
            hash = Hash64.Combine(hash, _homeY[i]);
            hash = Hash64.Combine(hash, (int)(_hunger[i] * 1000f));
            hash = Hash64.Combine(hash, (int)(_fatigue[i] * 1000f));
            hash = Hash64.Combine(hash, (int)(_thirst[i] * 1000f));
            hash = Hash64.Combine(hash, (int)(_social[i] * 1000f));
            hash = Hash64.Combine(hash, (int)(_health[i] * 1000f));
            hash = Hash64.Combine(hash, _ageDays[i]);
            hash = Hash64.Combine(hash, _job[i]);
            hash = Hash64.Combine(hash, _state[i]);
            hash = Hash64.Combine(hash, _action[i]);
            hash = Hash64.Combine(hash, _actionPhase[i]);
            hash = Hash64.Combine(hash, _deathCause[i]);
            hash = Hash64.Combine(hash, _targetX[i]);
            hash = Hash64.Combine(hash, _targetY[i]);
            hash = Hash64.Combine(hash, (int)(_invFood[i] * 100f));
            hash = Hash64.Combine(hash, (int)(_invWood[i] * 100f));
            hash = Hash64.Combine(hash, (int)(_invStone[i] * 100f));
            hash = Hash64.Combine(hash, (int)(_invIron[i] * 100f));
            hash = Hash64.Combine(hash, (int)(_aggression[i] * 1000f));
            hash = Hash64.Combine(hash, (int)(_industriousness[i] * 1000f));
        }
        return hash;
    }

    /// <summary>清空全部个体（世界重建时调用）。</summary>
    public void Reset()
    {
        for (int i = 0; i < _capacity; i++)
        {
            if (_alive[i]) { _generation[i]++; }
            _alive[i] = false;
        }
        _liveCount = 0;
        _nextFreeHint = 0;
        _peakPopulation = 0;
        TotalBorn = 0;
        TotalDied = 0;
        _nameOverrides.Clear();
        if (_migrateUntil.Length > 0) { System.Array.Clear(_migrateUntil, 0, _migrateUntil.Length); }
        RebuildLiveSlots();
    }
}
