using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.History;

/// <summary>
/// 事件类型（第 56 节）。新增事件类型时必须同步：
///   1. <see cref="EventLog"/> 的中文描述生成；
///   2. docs/10-DebugAndObservation.md 的事件表；
///   3. 报告里"关键事件"的筛选规则。
/// 漏掉任何一处都会让玩家看到"这条事件没有说明"，破坏可解释性（第 91 / 92 节）。
/// </summary>
public enum WorldEventType : byte
{
    None = 0,

    // 世界级
    WorldGenerated = 1,
    TerrainChanged = 2,
    ResourceInjected = 3,
    RuleChanged = 4,
    WeatherForced = 5,
    WeatherChanged = 6,

    /// <summary>玩家调整了地力（M4 的 Blessing 工具）。</summary>
    FertilityChanged = 7,

    /// <summary>玩家放置了野生动物（M4 的 Create 工具）。</summary>
    WildlifeSpawned = 8,

    // 火灾与灾害（M5）
    FireStarted = 10,
    FireSpread = 11,
    FireExtinguished = 12,
    Disaster = 13,

    // 人物（M2+）
    AgentSpawned = 20,
    AgentBorn = 21,
    AgentDied = 22,
    AgentMigrated = 23,
    AgentChangedJob = 24,
    AgentStarving = 25,
    AgentAte = 26,

    // 人与人之间（M6）
    AgentSocialized = 27,
    AgentSharedFood = 28,
    AgentAttacked = 29,

    // 建筑（M3+）
    BuildingStarted = 30,
    BuildingCompleted = 31,
    BuildingDestroyed = 32,

    // 聚落（M4+）
    SettlementFounded = 40,
    SettlementGrew = 41,
    SettlementDeclined = 42,
    SettlementAbandoned = 43,
    SettlementTierChanged = 44,

    // 社会（M6/M8）
    RelationshipFormed = 50,
    RelationshipBroken = 51,
    Marriage = 52,
    Birth = 53,
    Death = 54,
    LeaderElected = 55,
    TradeRouteEstablished = 56,
    WarDeclared = 57,
    WarEnded = 58,
    Conflict = 59,
}

/// <summary>事件重要度：玩家时间线默认只显示 Important 及以上。</summary>
public enum EventImportance : byte
{
    Trivial = 0,
    Minor = 1,
    Normal = 2,
    Important = 3,
    Critical = 4,
}

/// <summary>
/// 一条世界事件（第 55 节）。
///
/// 刻意保持"扁平 + 少字段"：事件的 VALUE 在于数量大、可检索、可聚合成故事，
/// 而不是每条都携带丰富结构。需要细节时用 Actor/Target 回查实体。
/// </summary>
public struct WorldEvent
{
    /// <summary>发生时间（游戏 tick）。</summary>
    public long Tick;

    /// <summary>事件类型。</summary>
    public WorldEventType Type;

    /// <summary>主要行为者（-1 表示无，例如自然事件）。</summary>
    public int Actor;

    /// <summary>承受者/对象（-1 表示无）。</summary>
    public int Target;

    /// <summary>发生地点（-1,-1 表示无地点）。</summary>
    public Int2 Location;

    /// <summary>重要度。</summary>
    public EventImportance Importance;

    /// <summary>描述文本。为空时由 EventLog 按类型生成默认描述。</summary>
    public string Description;

    /// <summary>因果标签：这条事件由哪条因果链产生（第 92 节可解释性）。</summary>
    public string Cause;

    public bool HasLocation => Location.X >= 0 && Location.Y >= 0;

    public override string ToString()
        => "T" + Tick + " [" + Type + "] " + Description;
}

/// <summary>
/// 事件日志（第 55 / 56 / 58 节）。
///
/// 实现要点：环形缓冲。事件只增不删，但玩家永远只关心最近的若干条，
/// 因此保留最近 <see cref="DefaultCapacity"/> 条，超出后覆盖最旧的。
/// 这是"长期运行的模拟不无限吃内存"的关键一招（第 73 节性能原则）。
/// </summary>
public sealed class EventLog
{
    public const int DefaultCapacity = 20000;

    private readonly WorldEvent[] _events;
    private int _start;      // 最旧事件的下标
    private int _count;      // 当前事件数
    private long _totalRecorded;

    public int Capacity => _events.Length;

    /// <summary>当前保留的事件数（≤ Capacity）。</summary>
    public int Count => _count;

    /// <summary>历史累计记录的事件总数（包括已被覆盖的）。</summary>
    public long TotalRecorded => _totalRecorded;

    public EventLog(int capacity = DefaultCapacity)
    {
        int cap = capacity > 0 ? capacity : DefaultCapacity;
        _events = new WorldEvent[cap];
    }

    /// <summary>记录一条事件（最简形式）。</summary>
    public void Record(long tick, WorldEventType type, string description,
        EventImportance importance = EventImportance.Normal,
        Int2 location = default,
        int actor = -1,
        int target = -1,
        string cause = "")
    {
        var ev = new WorldEvent
        {
            Tick = tick,
            Type = type,
            Actor = actor,
            Target = target,
            Location = location,
            Importance = importance,
            Description = description ?? string.Empty,
            Cause = cause ?? string.Empty,
        };
        Record(in ev);
    }

    public void Record(in WorldEvent ev)
    {
        int index = (_start + _count) % _events.Length;
        _events[index] = ev;

        if (_count < _events.Length)
        {
            _count++;
        }
        else
        {
            // 满了：丢最旧的
            _start = (_start + 1) % _events.Length;
        }
        _totalRecorded++;
    }

    /// <summary>按时间序访问第 i 条（0 = 最旧）。</summary>
    public WorldEvent this[int index]
    {
        get
        {
            if (index < 0 || index >= _count) { return default; }
            return _events[(_start + index) % _events.Length];
        }
    }

    /// <summary>最近一条事件（无事件时返回默认值）。</summary>
    public WorldEvent Latest => _count > 0 ? this[_count - 1] : default;

    /// <summary>从最新往回找第一条满足条件的事件。</summary>
    public bool TryFindLast(System.Func<WorldEvent, bool> predicate, out WorldEvent found)
    {
        for (int i = _count - 1; i >= 0; i--)
        {
            WorldEvent ev = this[i];
            if (predicate(ev))
            {
                found = ev;
                return true;
            }
        }
        found = default;
        return false;
    }

    /// <summary>统计某类型事件的数量（在保留窗口内）。</summary>
    public int CountOf(WorldEventType type)
    {
        int n = 0;
        for (int i = 0; i < _count; i++)
        {
            if (this[i].Type == type) { n++; }
        }
        return n;
    }

    /// <summary>
    /// 取最近 n 条中重要度 ≥ minImportance 的事件，按时间正序返回。
    /// 报告与 TUI 事件面板用这个，而不是遍历全部事件。
    /// </summary>
    public WorldEvent[] Recent(int n, EventImportance minImportance = EventImportance.Trivial)
    {
        if (n <= 0) { return System.Array.Empty<WorldEvent>(); }
        var buffer = new System.Collections.Generic.List<WorldEvent>(n);
        for (int i = _count - 1; i >= 0 && buffer.Count < n; i--)
        {
            WorldEvent ev = this[i];
            if (ev.Importance >= minImportance) { buffer.Add(ev); }
        }
        buffer.Reverse();
        return buffer.ToArray();
    }

    /// <summary>取某实体相关的全部事件（个人时间线，第 57 节）。</summary>
    public WorldEvent[] ForActor(int actorId, int max = 200)
    {
        var list = new System.Collections.Generic.List<WorldEvent>();
        for (int i = 0; i < _count; i++)
        {
            WorldEvent ev = this[i];
            if (ev.Actor == actorId || ev.Target == actorId)
            {
                list.Add(ev);
                if (list.Count >= max) { break; }
            }
        }
        return list.ToArray();
    }

    public void Clear()
    {
        _start = 0;
        _count = 0;
        _totalRecorded = 0;
    }
}
