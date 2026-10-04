using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Systems;

/// <summary>
/// 冲突压力（M8）。
///
/// # 公式（任务书第 74 条）
///
/// ```text
/// WarPressure = 资源冲突 + 领土冲突 + 侵略性 + 历史敌意
///             − 贸易收益 − 关系
/// ```
///
/// 前四项是"推"，后两项是"拉"。这个形状本身就是 M8 的核心主张：
/// **战争不是被触发的，而是被"推"到超过"拉"之后自己发生的。**
/// 玩家能改的是条件（资源分布、贸易通路、谁和谁住得近），
/// 而不是按一个"开战"按钮。
///
/// # 与贸易相同：**不存任何状态**
///
/// 它是现有状态（关掉资源、位置、关系史）的纯函数，因此不进存档、不进摘要 ——
/// 读档后重算得完全相同的值。判据与 <see cref="TradeSystem"/> 一致：
/// **能在一秒内重算出来的东西就不要存。**
///
/// # 它怎么被使用
///
/// 目前接到 `AttackAction` 的效用上：压力越大，越容易动手。
/// 这就是"关系恶化 → 冲突"这条链的最后一环 ——
/// 而它的输入端（资源稀缺、住得近、历史敌意）全都是**别的系统已经在算的东西**，
/// 这也是为什么它适合做成纯派生量。
/// </summary>
public sealed class ConflictSystem
{
    private readonly Simulation _sim;
    private readonly ConflictConfig _config;

    /// <summary>全图最近一次算出的平均冲突压力（观测用）。</summary>
    public float AveragePressure { get; private set; }

    /// <summary>压力最高的那一对的压力值（观测用）。</summary>
    public float PeakPressure { get; private set; }

    /// <summary>被评估过的对数（观测用）。</summary>
    public long TotalEvaluations { get; private set; }

    public ConflictSystem(Simulation sim)
    {
        _sim = sim ?? throw new System.ArgumentNullException(nameof(sim));
        _config = sim.Config.Conflict;
    }

    public void ResetStatistics()
    {
        AveragePressure = PeakPressure = 0f;
        TotalEvaluations = 0;
    }

    /// <summary>
    /// 逐日汇总一次全图压力（只用于观测与报告）。
    ///
    /// 真正的逐对压力由 <see cref="PressureOf"/> 在需要时按需计算 ——
    /// 因为"谁想打谁"是一个**局部**问题，没必要每天为所有对算一遍 O(N²)。
    /// </summary>
    public void TickDay(long tick)
    {
        if (!_config.Enabled) { return; }

        int[] slots = _sim.Agents.LiveSlotsRaw(out int liveCount);
        if (liveCount < 2) { AveragePressure = 0f; PeakPressure = 0f; return; }

        float sum = 0f;
        float peak = 0f;
        int pairs = 0;

        for (int i = 0; i < liveCount; i++)
        {
            for (int j = i + 1; j < liveCount; j++)
            {
                float p = PressureOf(slots[i], slots[j]);
                sum += p;
                if (p > peak) { peak = p; }
                pairs++;
            }
        }

        AveragePressure = pairs > 0 ? sum / pairs : 0f;
        PeakPressure = peak;
        TotalEvaluations += pairs;
    }

    /// <summary>
    /// 一对个体之间的冲突压力。
    ///
    /// 六项的含义与来源：
    ///   * **资源冲突**：两边随身食物都不足时最高（抢同一口饭）；
    ///   * **领土冲突**：住得越近越高（离得远的人打不起来）；
    ///   * **侵略性**：双方 `Aggression` 的均值（性格是权重，不是门）；
    ///   * **历史敌意**：亲和度为负的程度（M6 的关系史）；
    ///   * **贸易收益**（减）：两边都有余粮时降低压力 —— "有得赚就不打"；
    ///   * **关系**（减）：正的亲和度直接抵消。
    /// </summary>
    public float PressureOf(int slotA, int slotB)
    {
        if (!_config.Enabled) { return 0f; }
        if (slotA == slotB) { return 0f; }
        if (!_sim.Agents.IsSlotAlive(slotA) || !_sim.Agents.IsSlotAlive(slotB)) { return 0f; }

        // ---- 资源冲突：双方都缺食物 ----
        float foodA = _sim.Agents.InventoryOf(slotA, ResourceKind.Food);
        float foodB = _sim.Agents.InventoryOf(slotB, ResourceKind.Food);
        float shortA = 1f - SimMath.Clamp01(foodA / System.Math.Max(1f, _config.EnoughFood));
        float shortB = 1f - SimMath.Clamp01(foodB / System.Math.Max(1f, _config.EnoughFood));
        // 双方都缺才构成冲突；一边富一边穷是"怨恨"（M6 已有），不是"资源冲突"
        float resource = SimMath.Clamp01(System.Math.Min(shortA, shortB));

        // ---- 领土冲突：住得越近越高 ----
        float dx = _sim.Agents.XOf(slotA) - _sim.Agents.XOf(slotB);
        float dy = _sim.Agents.YOf(slotA) - _sim.Agents.YOf(slotB);
        float distance = System.MathF.Sqrt((dx * dx) + (dy * dy));
        float territory = 1f - SimMath.Clamp01(distance / System.Math.Max(1f, _config.TerritoryRadius));

        // ---- 侵略性：双方均值 ----
        float aggression = 0.5f * (_sim.Agents.PersonalityOf(slotA).Aggression
                                 + _sim.Agents.PersonalityOf(slotB).Aggression);

        // ---- 历史敌意：负亲和度 ----
        float affinity = _sim.Relationships.AffinityOf(slotA, slotB);
        float hostility = affinity < 0f ? SimMath.Clamp01(-affinity) : 0f;

        // ---- 贸易收益（减）：双方都有余粮时"有得赚就不打" ----
        float trade = 0f;
        if (foodA > _config.EnoughFood && foodB > _config.EnoughFood) { trade = 1f; }

        // ---- 关系（减）：正亲和度直接抵消 ----
        float relationship = affinity > 0f ? SimMath.Clamp01(affinity) : 0f;

        float pressure = _config.ResourceWeight * resource
                       + _config.TerritoryWeight * territory
                       + _config.AggressionWeight * aggression
                       + _config.HostilityWeight * hostility
                       - _config.TradeWeight * trade
                       - _config.RelationshipWeight * relationship;

        return SimMath.Clamp(pressure, 0f, _config.MaxPressure);
    }

    /// <summary>
    /// 某个个体对周围所有人的最大压力（"他最想打谁"）—— 供 `AttackAction` 使用。
    ///
    /// 返回 0 表示"不想打任何人"。注意它**不选靶**，只给出压力值；
    /// 选靶仍然由动作自己做（保持"评估"与"选靶"分离，这是本项目的既有约定）。
    /// </summary>
    public float MaxPressureFrom(int slot, float radius)
    {
        if (!_config.Enabled) { return 0f; }

        float radiusSq = radius * radius;
        int[] slots = _sim.Agents.LiveSlotsRaw(out int liveCount);
        float peak = 0f;

        int x = _sim.Agents.XOf(slot);
        int y = _sim.Agents.YOf(slot);

        for (int k = 0; k < liveCount; k++)
        {
            int other = slots[k];
            if (other == slot) { continue; }

            float dx = _sim.Agents.XOf(other) - x;
            float dy = _sim.Agents.YOf(other) - y;
            if ((dx * dx) + (dy * dy) > radiusSq) { continue; }

            float p = PressureOf(slot, other);
            if (p > peak) { peak = p; }
        }

        return peak;
    }
}

/// <summary>冲突压力参数（M8）。</summary>
public sealed class ConflictConfig
{
    public bool Enabled = true;

    /// <summary>"够吃"的食物量：达到它就不算资源短缺。</summary>
    public float EnoughFood = 20f;

    /// <summary>领土冲突的作用半径（格）：超过它的人打不起来。</summary>
    public float TerritoryRadius = 12f;

    /// <summary>压力上限（防止各项叠加后无限增长）。</summary>
    public float MaxPressure = 1f;

    // # 权重必须**归一到 1.0**，否则压力会长期贴着上限
    //
    // 第一版取的是 1.0 / 0.5 / 0.8 / 1.2（四项"推"加起来 3.5），
    // 后果是只要资源冲突拉满，压力就必然被夹到上限 1.0 ——
    // 实测三对完全不同的关系（挚友 / 陌生人 / 仇敌）算出来**都是 1**，
    // 于是"关系是拉项"这件事在数值上根本看不出来。
    //
    // 现在四项"推"的和正好是 1.0：**于是 `WarPressure = 1.0` 有了明确含义 ——
    // "所有推力都拉满、且没有任何拉力"**。这样阈值就不再是一个拍脑袋的数，
    // 而"压力 0.7"也变成一句可以解释的话。

    // 四项"推"的权重（和 = 1.0）
    public float ResourceWeight = 0.35f;
    public float TerritoryWeight = 0.20f;
    public float AggressionWeight = 0.20f;
    public float HostilityWeight = 0.25f;

    // 两项"拉"的权重（它们只减，所以不需要与"推"同尺度，但同样要克制）
    public float TradeWeight = 0.30f;
    public float RelationshipWeight = 0.35f;
}
