using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Ai;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Pathing;

namespace SandBoxSim.Core.Systems;

/// <summary>
/// 一次决策需要的全部上下文。
///
/// 显式传结构体而不是让动作去抓全局：动作函数因此是**纯函数风格**的
/// （输入全在参数里），既可以单测，也能保证不会偷偷读 UI 状态或消耗随机数。
/// </summary>
public struct ActionContext
{
    public SandBoxSim.Core.Environment.World World;
    public AgentStore Store;
    public int Slot;
    public int X;
    public int Y;
    public SimConfig Config;
    public AiConfig Ai;

    /// <summary>个体所在 chunk 的聚合信息（附近资源/地形），避免动作自己扫图。</summary>
    public ChunkStatsReadOnly Chunk;

    /// <summary>地面物资堆（M2）：存/取动作读它。</summary>
    public GroundStockStore? GroundStocks;

    /// <summary>野生动物（M2）：狩猎动作读它。</summary>
    public Agents.WildlifeStore? Wildlife;

    /// <summary>建筑（M3）：建造动作与"住房缺口"读它。</summary>
    public BuildingStore? Buildings;

    /// <summary>共享库存（M3）：建造扣料与存放/取回读它。</summary>
    public StorageStore? Storage;

    /// <summary>当前 tick。</summary>
    public long Tick;

    /// <summary>是否夜晚（影响睡眠倾向）。</summary>
    public bool IsNight;

    /// <summary>出生点（暂时作为"家"的近似，用于离家惩罚）。M7 会换成真实住所。</summary>
    public int HomeX;
    public int HomeY;

    /// <summary>可用于选靶的随机源（必须是 Agents 流，保证确定性）。</summary>
    public DeterministicRandom Rng;

    /// <summary>移动速度（每 tick 前进多少格）。</summary>
    public float MoveSpeedPerTick;
}

/// <summary>
/// 动作的效用评估委托。
///
/// 为什么要自定义委托而不是用 <c>Func&lt;in ActionContext, ActionScore&gt;</c>：
/// C# 的变体修饰符（in/out）只允许出现在**接口与委托声明**的参数上，
/// 而 <c>Func&lt;...&gt;</c> 的类型参数本身不支持 in —— 写 <c>Func&lt;in T, R&gt;</c> 会直接编译失败（CS1960）。
/// 自定义委托既能保留"按引用传递大结构体"的性能，也让签名读起来更明确。
/// </summary>
public delegate ActionScore ActionEvaluator(in ActionContext ctx);

/// <summary>动作的选靶委托：返回 null 表示"视野内没有可用目标"。</summary>
public delegate Int2? TargetSelector(in ActionContext ctx, AStarPathfinder pathfinder);

/// <summary>
/// 一个动作的定义：怎么算它的效用（第 15 节），以及怎么给它选目标。
///
/// 把"效用计算"和"执行"分开是这个设计的关键：
///   * 效用函数只看状态，不产生副作用 ⇒ 可以被测试逐条断言（需求升高必须让效用升高）；
///   * 执行逻辑在 <see cref="ActionSystem"/> 里，负责移动/耗时/结算。
/// 混在一起的话，"为什么他没去砍树"就无法回答了（第 92 条可解释性）。
/// </summary>
public sealed class ActionDef
{
    /// <summary>动作种类。</summary>
    public ActionKind Kind { get; }

    /// <summary>显示名（UI 与调试输出）。</summary>
    public string DisplayName { get; }

    /// <summary>是否属于"工作"（勤劳性格会给它加成）。</summary>
    public bool IsWork { get; }

    /// <summary>是否需要移动到一个目标格才能执行。</summary>
    public bool NeedsTarget { get; init; }

    /// <summary>
    /// 效用评估。实现里必须：
    ///   1. 先判断前置条件（不满足时返回一个"被门挡住"的分数）；
    ///   2. 把每个影响因素写成 <see cref="Consideration"/>，不要图省事直接算一个数字 ——
    ///      那些 Consideration 就是玩家在检查器里看到的解释。
    /// </summary>
    public ActionEvaluator? Evaluate { get; init; }

    /// <summary>
    /// 选靶：返回 null 表示找不到目标（动作会被判为不可行）。
    /// 只允许在 <c>Ai.SearchRadius</c> 内寻找（第 19 条：NPC 不允许全知）。
    /// </summary>
    public TargetSelector? SelectTarget { get; init; }

    public ActionDef(ActionKind kind, string displayName, bool isWork = false, bool needsTarget = false)
    {
        Kind = kind;
        DisplayName = displayName;
        IsWork = isWork;
        NeedsTarget = needsTarget;
    }
}

/// <summary>
/// 动作登记表（数据驱动，第 96.3 条）。
///
/// 新增一个动作只需要在这里加一条定义 —— AI 决策循环、检查器、测试都不需要改。
/// 这是本项目"少量规则、易扩展"的具体体现：规则集中在表里，而不是散在巨大 switch 里。
/// </summary>
public static class ActionRegistry
{
    private const int KindCount = 256;

    private static ActionKind[]? _all;
    private static ActionDef[]? _cache;

    /// <summary>所有已注册的动作（固定顺序 ⇒ 确定性；平局时按此顺序取先者）。</summary>
    public static ActionKind[] All
    {
        get
        {
            if (_all == null)
            {
                // 顺序即"平局优先级"：越靠前越优先。
                // 把生存类动作放在漫游/探索前面，避免"又累又饿却还在闲逛"；
                // 把"存/取物资"与"狩猎"放在采集之后 —— 它们是补充手段，不是第一选择；
                // 建造排在搬运之后：先把料备齐，再动工（顺序本身就编码了一条行为规范）。
                _all = new[]
                {
                    ActionKind.Eat,
                    ActionKind.Drink,
                    ActionKind.Sleep,
                    ActionKind.GatherFood,
                    ActionKind.Hunt,
                    ActionKind.Take,
                    ActionKind.GatherWood,
                    ActionKind.GatherStone,
                    ActionKind.StoreInBuilding,
                    ActionKind.Deposit,
                    ActionKind.BuildHouse,
                    ActionKind.BuildStorage,

                    // M4：农田。这两个都不能漏 —— `BuildFarm` 曾经只在 Describe 里注册、
                    // 却不在 All 里，于是 AI **从不评估**建农田，世界永远没有农业，
                    // 而"食物不足"就成了一个无法被玩家干预的死结。
                    // 一个动作"已定义但不可达"是非常难发现的失效方式：没有任何报错，
                    // 只有一个静默的 0。这与 M2 里"存放/取回被选中 0 次"是同一类问题。
                    ActionKind.BuildFarm,
                    ActionKind.Farm,

                    ActionKind.Migrate,
                    ActionKind.Explore,
                    ActionKind.Wander,
                };
            }
            return _all;
        }
    }

    /// <summary>
    /// 取动作定义（带缓存）。
    ///
    /// 缓存很重要：决策循环对每个个体、每个候选动作都要取定义，
    /// 而 <see cref="Describe"/> 会分配闭包与对象。100 个体 × 8 动作 × 每秒若干次
    /// 会产生大量垃圾 —— 这类"看不见的分配"是模拟游戏掉帧的常见原因。
    /// </summary>
    public static ActionDef DescribeCached(ActionKind kind)
    {
        _cache ??= new ActionDef[KindCount];
        int index = (int)kind;
        if (index < 0 || index >= KindCount) { return Describe(kind); }

        ActionDef? cached = _cache[index];
        if (cached == null)
        {
            cached = Describe(kind);
            _cache[index] = cached;
        }
        return cached;
    }

    public static ActionDef Describe(ActionKind kind)
    {
        switch (kind)
        {
            case ActionKind.Wander:
                return new ActionDef(kind, "漫游")
                {
                    Evaluate = Actions.WanderAction.Evaluate,
                    SelectTarget = Actions.WanderAction.SelectTarget,
                    NeedsTarget = true,
                };

            case ActionKind.Explore:
                return new ActionDef(kind, "探索")
                {
                    Evaluate = Actions.ExploreAction.Evaluate,
                    SelectTarget = Actions.ExploreAction.SelectTarget,
                    NeedsTarget = true,
                };

            case ActionKind.Sleep:
                return new ActionDef(kind, "睡觉")
                {
                    Evaluate = Actions.SleepAction.Evaluate,
                };

            case ActionKind.Eat:
                return new ActionDef(kind, "进食")
                {
                    Evaluate = Actions.EatAction.Evaluate,
                };

            case ActionKind.Drink:
                return new ActionDef(kind, "饮水")
                {
                    Evaluate = Actions.DrinkAction.Evaluate,
                    SelectTarget = Actions.DrinkAction.SelectTarget,
                    NeedsTarget = true,
                };

            case ActionKind.GatherFood:
                return new ActionDef(kind, "采集食物", isWork: true)
                {
                    Evaluate = Actions.GatherFoodAction.Evaluate,
                    SelectTarget = Actions.GatherFoodAction.SelectTarget,
                    NeedsTarget = true,
                };

            case ActionKind.GatherWood:
                return new ActionDef(kind, "采伐木材", isWork: true)
                {
                    Evaluate = Actions.GatherWoodAction.Evaluate,
                    SelectTarget = Actions.GatherWoodAction.SelectTarget,
                    NeedsTarget = true,
                };

            case ActionKind.GatherStone:
                return new ActionDef(kind, "开采石料", isWork: true)
                {
                    Evaluate = Actions.GatherStoneAction.Evaluate,
                    SelectTarget = Actions.GatherStoneAction.SelectTarget,
                    NeedsTarget = true,
                };

            case ActionKind.Hunt:
                return new ActionDef(kind, "狩猎", isWork: true)
                {
                    Evaluate = Actions.HuntAction.Evaluate,
                    SelectTarget = Actions.HuntAction.SelectTarget,
                    NeedsTarget = true,
                };

            case ActionKind.Deposit:
                return new ActionDef(kind, "存放物资", isWork: true)
                {
                    Evaluate = Actions.DepositAction.Evaluate,
                    SelectTarget = Actions.DepositAction.SelectTarget,
                    NeedsTarget = true,
                };

            case ActionKind.Take:
                return new ActionDef(kind, "取回物资", isWork: true)
                {
                    Evaluate = Actions.TakeAction.Evaluate,
                    SelectTarget = Actions.TakeAction.SelectTarget,
                    NeedsTarget = true,
                };

            case ActionKind.Migrate:
                return new ActionDef(kind, "迁往新住地")
                {
                    Evaluate = Actions.MigrateAction.Evaluate,
                    SelectTarget = Actions.MigrateAction.SelectTarget,
                    NeedsTarget = true,
                };

            case ActionKind.BuildHouse:
                return new ActionDef(kind, "建造住房", isWork: true)
                {
                    Evaluate = Actions.BuildHouseAction.Evaluate,
                    SelectTarget = Actions.BuildHouseAction.SelectTarget,
                    NeedsTarget = true,
                };

            case ActionKind.BuildStorage:
                return new ActionDef(kind, "建造仓库", isWork: true)
                {
                    Evaluate = Actions.BuildStorageAction.Evaluate,
                    SelectTarget = Actions.BuildStorageAction.SelectTarget,
                    NeedsTarget = true,
                };

            case ActionKind.BuildFarm:
                return new ActionDef(kind, "开垦农田", isWork: true)
                {
                    Evaluate = Actions.BuildFarmAction.Evaluate,
                    SelectTarget = Actions.BuildFarmAction.SelectTarget,
                    NeedsTarget = true,
                };

            case ActionKind.Farm:
                return new ActionDef(kind, "耕种", isWork: true)
                {
                    Evaluate = Actions.FarmAction.Evaluate,
                    SelectTarget = Actions.FarmAction.SelectTarget,
                    NeedsTarget = true,
                };

            case ActionKind.StoreInBuilding:
                return new ActionDef(kind, "存入仓库", isWork: true)
                {
                    Evaluate = Actions.StoreInBuildingAction.Evaluate,
                    SelectTarget = Actions.StoreInBuildingAction.SelectTarget,
                    NeedsTarget = true,
                };

            default:
                return new ActionDef(kind, kind.ToString());
        }
    }

    /// <summary>人类可读的动作名（UI 用）。</summary>
    public static string DisplayNameOf(ActionKind kind) => Describe(kind).DisplayName;
}
