using SandBoxSim.Core.Ai;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Systems;

/// <summary>
/// 效用打分的流式构造器。
///
/// 存在的唯一理由：让每个动作的效用函数读起来像一张"考虑项清单"，而不是一坨代数。
/// 每个 <c>Consider(...)</c> 同时完成三件事：记录输入值、套曲线、按权重累加 ——
/// 并且把这一项留在最终结果里，供检查器显示（第 92 条可解释性）。
///
/// 用法：
/// <code>
/// var b = new ScoreBuilder(ActionKind.Eat);
/// b.Consider("饥饿", hunger, UtilityCurve.Quadratic, w.Hunger);      // 越饿越想
/// b.Consider("手上有食物", foodGate, UtilityCurve.Threshold(0.5f), w.FoodGate);  // 硬门
/// return b.Build();
/// </code>
/// </summary>
public struct ScoreBuilder
{
    private const int MaxConsiderations = 8;

    private readonly ActionKind _action;
    private readonly Consideration[] _items;
    private int _count;

    /// <summary>被"硬门"挡住时的效用乘数（来自配置，默认 0.15）。</summary>
    public float BlockedMultiplier { get; set; }

    public ScoreBuilder(ActionKind action, float blockedMultiplier = 0.15f)
    {
        _action = action;
        _items = new Consideration[MaxConsiderations];
        _count = 0;
        BlockedMultiplier = blockedMultiplier;
    }

    /// <summary>
    /// 加入一条考虑项。
    /// </summary>
    /// <param name="isBonus">
    /// true = "锦上添花"（缺席只是没那么想做，**不**触发"被门挡住"的惩罚）；
    /// false = 前置条件（缺席就意味着这件事的前提不成立）。
    ///
    /// 用法约定：**"附近有没有 X"这类可得性/机会项绝大多数应该是 bonus**，
    /// 因为"不可得"不等于"不该做" —— 后者会让动作在找不到目标时被额外打一次折，
    /// 从而静默地再也不被选中（实测踩过一次，详见 Consideration.IsBonus）。
    /// </param>
    public void Consider(string name, float input, UtilityCurve curve, float weight, bool isBonus = false)
    {
        if (_count >= MaxConsiderations) { return; }
        _items[_count] = new Consideration(name, input, curve, weight, isBonus);
        _count++;
    }

    /// <summary>加入一条"已经是分数"的考虑项（用 Linear 曲线传递）。</summary>
    public void ConsiderScore(string name, float input, float score, float weight, bool isBonus = false)
    {
        if (_count >= MaxConsiderations) { return; }
        _items[_count] = Consideration.FromScore(name, input, score, weight, isBonus);
        _count++;
    }

    /// <summary>完成打分：返回含完整分解的 <see cref="ActionScore"/>。</summary>
    public ActionScore Build()
    {
        float utility = UtilityCombiner.Combine(_items, _count, out float linear, out float geometric, out bool blocked);

        // 被硬门挡住时按配置进一步压低效用：
        // 注意**不能**直接归零 —— 否则"手上一粒粮食都没有"的人会连"去找吃的"都不做。
        // （真正的互斥应该由动作之间的竞争解决，而不是由一条硬门把整个动作删掉。）
        if (blocked) { utility *= BlockedMultiplier; }

        // 只复制实际用到的部分，避免把 8 个空槽也带进存档/检查器
        var taken = new Consideration[_count];
        System.Array.Copy(_items, taken, _count);
        return new ActionScore(_action, taken, utility, linear, geometric, blocked);
    }
}
