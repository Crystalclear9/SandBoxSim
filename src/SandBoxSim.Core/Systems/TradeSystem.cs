using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Systems;

/// <summary>
/// 贸易与价格（M8）。
///
/// # 核心公式
///
/// ```text
/// Price = Base × ((Demand + ε) / (Supply + ε)) ^ α
/// ```
///
/// 这是任务书第 71 条要求的形状。三个部分的含义：
///
///   * **`Base`**：这种资源"本身值多少"。石料比木材贵、铁比石料贵 —— 由配方决定，
///     不由世界状态决定；
///   * **`(Demand/Supply)^α`**：稀缺度。**注意 ε**：没有它，
///     "供给为 0"会让价格变成无穷大（或除零），而"需求为 0"会让价格变成 0 ——
///     两者都会让价格在极端情况下失去意义。ε 让价格在两端都平滑地趋近有限值；
///   * **`α`**（价格弹性）：它决定"稀缺有多贵"。α = 1 是线性，
///     α < 1 表示"就算很缺也贵不到哪去"。
///
/// # 为什么整个系统**不存任何状态**
///
/// 价格是**现有状态的纯函数**：给定"每个聚落手上有什么、需要什么"，
/// 价格就完全确定了。因此它不需要进存档、也不需要进摘要 ——
/// 读档之后重算一遍就得到完全相同的值。
///
/// 这不是为了省事，而是一条刻意的设计选择。M4 期间有**八个**字段因为
/// "看起来只是派生量、实际影响了行为"而造成读档分叉；
/// 反过来，凡是**能由现有状态算出来**的东西就**不该**被存起来 ——
/// 存了反而多一个可能不一致的副本。
///
/// 判据可以写得更明确：**如果它能在读档后一秒内重算出来，就不要存它。**
///
/// # 价格有什么可观察的价值
///
/// 它是玩家理解世界的透镜：粮价暴涨意味着某地要饿死人了，而玩家
/// 不用去看每一个人的饥饿度。**一个由状态导出的数字，比状态本身更能说明问题。**
/// </summary>
public sealed class TradeSystem
{
    private readonly Simulation _sim;
    private readonly TradeConfig _config;

    /// <summary>四种资源的当前价格（下标 = (int)ResourceKind）。派生量，不存档。</summary>
    private readonly float[] _price = new float[8];

    /// <summary>供玩家与报告读取的"上一次算出来的价格"。</summary>
    public float PriceOf(ResourceKind kind) => _price[(int)kind];

    /// <summary>累计算价次数（观测用）。</summary>
    public long TotalQuotes { get; private set; }

    /// <summary>最近一次全图粮食供需比（稀缺度的直接读数）。</summary>
    public float LastFoodRatio { get; private set; }

    /// <summary>最近一次全图木材供需比。</summary>
    public float LastWoodRatio { get; private set; }

    public TradeSystem(Simulation sim)
    {
        _sim = sim ?? throw new System.ArgumentNullException(nameof(sim));
        _config = sim.Config.Trade;

        // 初始价 = 基准价（世界还没算过价）
        ResetToBase();
    }

    private void ResetToBase()
    {
        for (int i = 0; i < _price.Length; i++) { _price[i] = 1f; }
        _price[(int)ResourceKind.Food] = _config.BaseFoodPrice;
        _price[(int)ResourceKind.Wood] = _config.BaseWoodPrice;
        _price[(int)ResourceKind.Stone] = _config.BaseStonePrice;
        _price[(int)ResourceKind.Iron] = _config.BaseIronPrice;
    }

    public void ResetStatistics()
    {
        ResetToBase();
        TotalQuotes = 0;
        LastFoodRatio = LastWoodRatio = 0f;
    }

    /// <summary>
    /// 逐日算价（由 `Simulation.TickDay` 调用）。
    ///
    /// 它扫一遍世界，统计每种资源的"可动用供给"与"预期需求"，
    /// 再按公式算出价格。**不写任何需要存档的状态** —— 只更新派生缓存。
    /// </summary>
    public void TickDay(long tick)
    {
        if (!_config.Enabled) { return; }

        // ---- 供给：共享库存 + 地面物资堆 + 随身 ----
        //
        // 注意数组长度：`ResourceKind` 是 None=0, Food=1, Wood=2, Stone=3, Iron=4 ——
        // **从 1 开始**，所以按下标访问时长度至少要 5。第一版写成 `new float[4]`
        // 直接抛 IndexOutOfRange（而它只在真正的世界里才触发，单测"看着没问题"的写法
        // 就是这么来的）。这里统一用 8，留出扩展余量。
        float[] supply = new float[8];
        int[] slots = _sim.Agents.LiveSlotsRaw(out int liveCount);

        for (int k = 0; k < liveCount; k++)
        {
            int slot = slots[k];
            supply[(int)ResourceKind.Food] += _sim.Agents.InventoryOf(slot, ResourceKind.Food);
            supply[(int)ResourceKind.Wood] += _sim.Agents.InventoryOf(slot, ResourceKind.Wood);
            supply[(int)ResourceKind.Stone] += _sim.Agents.InventoryOf(slot, ResourceKind.Stone);
            supply[(int)ResourceKind.Iron] += _sim.Agents.InventoryOf(slot, ResourceKind.Iron);
        }

        supply[(int)ResourceKind.Food] += _sim.GroundStocks.TotalOf(ResourceKind.Food);
        supply[(int)ResourceKind.Wood] += _sim.GroundStocks.TotalOf(ResourceKind.Wood);
        supply[(int)ResourceKind.Stone] += _sim.GroundStocks.TotalOf(ResourceKind.Stone);
        supply[(int)ResourceKind.Iron] += _sim.GroundStocks.TotalOf(ResourceKind.Iron);

        int buildingCapacity = _sim.Buildings.Capacity;
        supply[(int)ResourceKind.Food] += _sim.Storage.GrandTotalOf(ResourceKind.Food, buildingCapacity);
        supply[(int)ResourceKind.Wood] += _sim.Storage.GrandTotalOf(ResourceKind.Wood, buildingCapacity);
        supply[(int)ResourceKind.Stone] += _sim.Storage.GrandTotalOf(ResourceKind.Stone, buildingCapacity);
        supply[(int)ResourceKind.Iron] += _sim.Storage.GrandTotalOf(ResourceKind.Iron, buildingCapacity);

        // ---- 需求：人口 × 人均目标 + 建造的用料预期 ----
        //
        // 用"人均目标"而不是"当前缺口"，因为价格要反映**结构性**稀缺，
        // 而不是"这一刻谁手上空着"。后者每天都在剧烈波动，价格会变成噪声。
        float population = System.Math.Max(1, liveCount);
        float[] demand = new float[8];
        demand[(int)ResourceKind.Food] = population * _config.FoodPerCapitaNeed;
        demand[(int)ResourceKind.Wood] = population * _config.WoodPerCapitaNeed;
        demand[(int)ResourceKind.Stone] = population * _config.StonePerCapitaNeed;
        demand[(int)ResourceKind.Iron] = population * _config.IronPerCapitaNeed;

        // ---- 价格 ----
        //
        // # 下标必须统一用 `(int)ResourceKind.X`，不能写字面量 0/1/2/3
        //
        // 这里踩过一次：数组的**累加**用的是 `(int)ResourceKind.Wood`（= 2），
        // 而**取价**时写成了字面量 `supply[1]` —— 于是"木材的价格"是用**食物**的供给算出来的，
        // 而食物用的是 `supply[0]`（永远是 0，因为 ResourceKind 从 1 开始）。
        //
        // 症状很隐蔽：价格看起来"在动"、也不报错，只是**方向反了** ——
        // 实测同一个世界木材供给从 76 涨到 5996，价格却从 0.681 升到 0.915。
        // 只测"价格是正数且在上下限内"永远抓不到它。
        // **枚举类型的下标一律强转，绝不写字面量。**
        _price[(int)ResourceKind.Food] = Quote(
            _config.BaseFoodPrice, demand[(int)ResourceKind.Food], supply[(int)ResourceKind.Food]);
        _price[(int)ResourceKind.Wood] = Quote(
            _config.BaseWoodPrice, demand[(int)ResourceKind.Wood], supply[(int)ResourceKind.Wood]);
        _price[(int)ResourceKind.Stone] = Quote(
            _config.BaseStonePrice, demand[(int)ResourceKind.Stone], supply[(int)ResourceKind.Stone]);
        _price[(int)ResourceKind.Iron] = Quote(
            _config.BaseIronPrice, demand[(int)ResourceKind.Iron], supply[(int)ResourceKind.Iron]);

        LastFoodRatio = Ratio(demand[(int)ResourceKind.Food], supply[(int)ResourceKind.Food]);
        LastWoodRatio = Ratio(demand[(int)ResourceKind.Wood], supply[(int)ResourceKind.Wood]);
        TotalQuotes++;
    }

    private float Ratio(float demand, float supply)
        => (demand + _config.Epsilon) / (supply + _config.Epsilon);

    /// <summary>
    /// 单个报价：`Base × ratio^α`，再夹到 `[MinMultiplier, MaxMultiplier]` 倍基准价。
    ///
    /// **上下限不是"防溢出"的补丁，而是语义的一部分**：
    /// 一个价格如果可以是无限大，那么任何依赖它的决策都会在极端情况下崩掉；
    /// 而现实里的价格也从来不会真的趋于无穷 —— 会有替代品、会有配给。
    /// 把这条现实约束显式写进模型，比让下游各自去 clamp 要好。
    /// </summary>
    private float Quote(float basePrice, float demand, float supply)
    {
        float ratio = Ratio(demand, supply);
        float price = basePrice * System.MathF.Pow(ratio, _config.Elasticity);

        float min = basePrice * _config.MinMultiplier;
        float max = basePrice * _config.MaxMultiplier;
        if (price < min) { price = min; }
        if (price > max) { price = max; }
        return price;
    }

    /// <summary>以木材为 1.0 的相对价格（"一块铁值几块木头"）—— 报告与 UI 用。</summary>
    public float RelativeToWood(ResourceKind kind)
    {
        float wood = _price[(int)ResourceKind.Wood];
        if (wood <= 0f) { return 1f; }
        return _price[(int)kind] / wood;
    }
}

/// <summary>贸易与价格参数（M8）。</summary>
public sealed class TradeConfig
{
    public bool Enabled = true;

    /// <summary>
    /// 价格弹性 α。`Price = Base × ((Demand+ε)/(Supply+ε))^α`。
    ///
    /// α = 1 是线性；α &lt; 1 表示"再缺也贵不到哪去"（有替代品）；
    /// α &gt; 1 表示"稍微缺一点就暴涨"。
    /// </summary>
    public float Elasticity = 0.7f;

    /// <summary>
    /// 平滑项 ε。**它必须大于 0**：
    /// 否则"供给为 0"会让比例变成无穷大，"需求为 0"会让价格变成 0 ——
    /// 两端都会让价格失去意义。
    /// </summary>
    public float Epsilon = 1f;

    /// <summary>价格下限（相对基准价的倍数）。</summary>
    public float MinMultiplier = 0.2f;

    /// <summary>价格上限（相对基准价的倍数）。</summary>
    public float MaxMultiplier = 8f;

    // 基准价（"这种东西本身值多少"，由配方决定，不由世界状态决定）
    public float BaseFoodPrice = 1.0f;
    public float BaseWoodPrice = 1.0f;
    public float BaseStonePrice = 1.6f;
    public float BaseIronPrice = 3.0f;

    // 人均需求目标（决定"结构性稀缺"的口径）
    public float FoodPerCapitaNeed = 8f;
    public float WoodPerCapitaNeed = 12f;
    public float StonePerCapitaNeed = 3f;
    public float IronPerCapitaNeed = 1f;
}
