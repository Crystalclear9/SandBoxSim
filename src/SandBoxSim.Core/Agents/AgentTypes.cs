using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Agents;

/// <summary>
/// 一个"稳定的个体引用"。
///
/// 为什么不用索引当 ID：Agent 会死，索引会被回收给新个体。
/// 如果外部（Inspector、事件日志、关系系统）拿着裸索引，个体死亡+槽位复用之后
/// 就会"看错人" —— 这类 bug 在模拟游戏里极难发现，因为它不报错，只是数据串了。
///
/// 因此引用 = (槽位, 代次)。槽位复用时代次 +1，旧引用立刻失效（<see cref="IsValid"/> 返回 false）。
/// </summary>
public readonly struct AgentRef : System.IEquatable<AgentRef>
{
    public readonly int Slot;
    public readonly int Generation;

    public AgentRef(int slot, int generation)
    {
        Slot = slot;
        Generation = generation;
    }

    public static readonly AgentRef None = new AgentRef(-1, -1);

    public bool IsNone => Slot < 0;

    public bool Equals(AgentRef other) => Slot == other.Slot && Generation == other.Generation;
    public override bool Equals(object? obj) => obj is AgentRef other && Equals(other);
    public override int GetHashCode() => (Slot * 397) ^ Generation;
    public override string ToString() => IsNone ? "none" : "#" + Slot + "g" + Generation;
}

/// <summary>
/// 个体当前的宏观状态（用于 UI 显示与调试；决策本身看的是动作与需求）。
/// </summary>
public enum AgentState : byte
{
    Idle = 0,
    Working = 1,
    Eating = 2,
    Sleeping = 3,
    Moving = 4,
    Socializing = 5,
    Fleeing = 6,
    Fighting = 7,
    Dead = 8,
}

/// <summary>个体当前在执行的动作（M1 只需要漫游/探索；其余随里程碑开放）。</summary>
public enum ActionKind : byte
{
    None = 0,

    /// <summary>在附近随机走动：没有更迫切需求时的默认行为。</summary>
    Wander = 1,

    /// <summary>朝远处探索：为后续的"发现新资源/新定居点"做准备。</summary>
    Explore = 2,

    // ---- M2 起陆续开放 ----
    Eat = 10,
    Drink = 11,
    Sleep = 12,
    GatherFood = 13,
    GatherWood = 14,
    GatherStone = 15,
    GatherIron = 16,
    Deposit = 17,
    BuildHouse = 18,
    BuildFarm = 19,
    BuildStorage = 20,
    Farm = 21,
    Socialize = 22,
    Flee = 23,
    Attack = 24,
    ShareFood = 25,

    /// <summary>迁往新的住地（M2）：把"家"搬到远处并由个体自己走过去。</summary>
    Migrate = 26,

    /// <summary>狩猎（M2）：猎杀附近的野生动物换取食物。</summary>
    Hunt = 27,

    /// <summary>从地面物资堆取回物资（M2）。</summary>
    Take = 28,
}

/// <summary>动作的执行阶段（第 18 节：Condition → 选靶 → 移动 → 执行 → 结算）。</summary>
public enum ActionPhase : byte
{
    Idle = 0,

    /// <summary>正在朝目标移动。</summary>
    Moving = 1,

    /// <summary>已到达目标，正在执行（采集/进食/施工的耗时部分）。</summary>
    Executing = 2,

    /// <summary>本动作已完成，等待下一次决策。</summary>
    Done = 3,

    /// <summary>本轮动作失败（目标消失/不可达），原因记在 <c>ActionFailReason</c>。</summary>
    Failed = 4,
}

/// <summary>个体职业（第 37 节）。M1 只做最基本的四类，分配逻辑在 M4 接入。</summary>
public enum JobType : byte
{
    None = 0,
    Gatherer = 1,
    Farmer = 2,
    Builder = 3,
    Miner = 4,
}

/// <summary>生命阶段（第 36 / M6 节）。M1 先只有成年，年龄系统在 M2 接入。</summary>
public enum LifeStage : byte
{
    Child = 0,
    Adult = 1,
    Elder = 2,
    Dead = 3,
}

/// <summary>
/// 六维性格（第 32 / 33 节），全部归一化到 [0,1]。
///
/// 为什么用 [0,1] 而不是 [-1,1]：性格只用于"加权推进效用"，
/// 用非负区间可以避免"负权重导致效用为负"这类需要额外裁剪的边界情况。
/// 反向性格（例如"懒惰"）通过取其补值（1 − industriousness）表达。
/// </summary>
public struct Personality
{
    public float Aggression;         // 侵略性：冲突/战斗倾向
    public float Greed;              // 贪婪：囤积倾向、分享意愿下降
    public float Kindness;           // 善良：分享、帮助、照顾
    public float Bravery;            // 勇敢：逃跑阈值提高
    public float Industriousness;    // 勤劳：工作意愿
    public float Sociability;        // 社交性：社交需求

    public static Personality Average => new Personality
    {
        Aggression = 0.5f,
        Greed = 0.5f,
        Kindness = 0.5f,
        Bravery = 0.5f,
        Industriousness = 0.5f,
        Sociability = 0.5f,
    };

    /// <summary>
    /// 从随机源抽样性格。用"两个均匀数取平均"得到中间偏高、两端偏少的分布：
    /// 大多数人是"普通水平"，极端性格稀有 —— 这样个体的差异才有观察价值。
    /// </summary>
    public static Personality Sample(DeterministicRandom rng)
    {
        return new Personality
        {
            Aggression = SampleTrait(rng),
            Greed = SampleTrait(rng),
            Kindness = SampleTrait(rng),
            Bravery = SampleTrait(rng),
            Industriousness = SampleTrait(rng),
            Sociability = SampleTrait(rng),
        };
    }

    private static float SampleTrait(DeterministicRandom rng)
    {
        float sum = rng.NextFloat() + rng.NextFloat();
        return SimMath.Clamp01((sum * 0.5f * 0.8f) + 0.1f);
    }

    /// <summary>孩子的性格 = 双亲均值 + 小幅突变（M6 用；M1 只用到 Sample）。</summary>
    public static Personality Inherit(Personality a, Personality b, DeterministicRandom rng)
    {
        return new Personality
        {
            Aggression = Mutate((a.Aggression + b.Aggression) * 0.5f, rng),
            Greed = Mutate((a.Greed + b.Greed) * 0.5f, rng),
            Kindness = Mutate((a.Kindness + b.Kindness) * 0.5f, rng),
            Bravery = Mutate((a.Bravery + b.Bravery) * 0.5f, rng),
            Industriousness = Mutate((a.Industriousness + b.Industriousness) * 0.5f, rng),
            Sociability = Mutate((a.Sociability + b.Sociability) * 0.5f, rng),
        };
    }

    private static float Mutate(float value, DeterministicRandom rng)
        => SimMath.Clamp01(value + ((float)rng.Jitter(0.08)));

    /// <summary>把六维压缩成一行（UI 与调试用）。</summary>
    public string ShortLabel()
    {
        return "aggr" + (Aggression * 9f).ToString("0", System.Globalization.CultureInfo.InvariantCulture)
             + " greed" + (Greed * 9f).ToString("0", System.Globalization.CultureInfo.InvariantCulture)
             + " kind" + (Kindness * 9f).ToString("0", System.Globalization.CultureInfo.InvariantCulture)
             + " brave" + (Bravery * 9f).ToString("0", System.Globalization.CultureInfo.InvariantCulture)
             + " ind" + (Industriousness * 9f).ToString("0", System.Globalization.CultureInfo.InvariantCulture)
             + " soc" + (Sociability * 9f).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// 个体需求的内部数组下标。集中定义避免"数组第 3 位到底是 hunger 还是 energy"这种问题。
/// </summary>
public enum NeedIndex
{
    /// <summary>饥饿（1 = 极度饥饿）。</summary>
    Hunger = 0,

    /// <summary>疲劳（1 = 极度疲劳）。</summary>
    Fatigue = 1,

    /// <summary>干渴（1 = 极度缺水）。</summary>
    Thirst = 2,

    /// <summary>社交需求（1 = 极度孤独）。M6 才真正驱动行为。</summary>
    Social = 3,
}

/// <summary>动作失败的常见原因（写进 Agent 与事件日志，便于排查"为什么他没去干这事"）。</summary>
public enum ActionFailReason : byte
{
    None = 0,
    NoTarget = 1,
    Unreachable = 2,
    TargetGone = 3,
    OutOfRange = 4,
    PrerequisiteLost = 5,
}

/// <summary>
/// 死因（第 28 / 91 节）。
///
/// 这个枚举看着不起眼，但它是"观察价值"的关键：玩家必须能知道**为什么**人死了，
/// 否则死亡只是一次静默的计数变化，不构成任何叙事。
/// 因此死亡事件必须带上死因，报告里也按死因分类统计。
/// </summary>
public enum DeathCause : byte
{
    None = 0,
    Starvation = 1,
    Dehydration = 2,
    OldAge = 3,
    Illness = 4,
    Injury = 5,
    Fire = 6,
    Combat = 7,
    Disaster = 8,
}
