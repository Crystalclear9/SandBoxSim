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

    /// <summary>加入一条考虑项。</summary>
    public void Consider(string name, float input, UtilityCurve curve, float weight)
    {
        if (_count >= MaxConsiderations) { return; }
        _items[_count] = new Consideration(name, input, curve, weight);
        _count++;
    }

    /// <summary>加入一条"已经是分数"的考虑项（用 Linear 曲线传递）。</summary>
    public void ConsiderScore(string name, float input, float score, float weight)
    {
        if (_count >= MaxConsiderations) { return; }
        _items[_count] = Consideration.FromScore(name, input, score, weight);
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
