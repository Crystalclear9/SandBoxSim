using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Ai;

/// <summary>
/// 一条"考虑项"（第 15 节的 Consideration）：
/// 把某个输入（需求强度、资源可得性、危险度……）经过一条曲线映射成 [0,1] 的效用分。
///
/// 它是 Utility AI 可解释性的最小单元：玩家看到的"为什么他想吃饭"就是
/// 若干条 Consideration 的输入值 × 曲线 × 权重。
/// </summary>
public readonly struct Consideration
{
    /// <summary>显示名（UI 与调试输出用；只影响可读性，不影响判定）。</summary>
    public readonly string Name;

    /// <summary>输入值 [0,1]。由各动作在计算利用率时计算出来。</summary>
    public readonly float Input;

    /// <summary>曲线（决定"紧迫感从哪里开始"）。</summary>
    public readonly UtilityCurve Curve;

    /// <summary>权重（可正可负；负数用于"惩罚项"，例如危险度）。</summary>
    public readonly float Weight;

    /// <summary>曲线求值结果 [0,1]。</summary>
    public readonly float Score;

    /// <summary>本项的加权贡献（Weight × Score），可以是负数。</summary>
    public readonly float Contribution;

    public Consideration(string name, float input, UtilityCurve curve, float weight)
    {
        Name = name;
        Input = SimMath.Clamp01(input);
        Curve = curve;
        Weight = weight;
        Score = curve.Evaluate(Input);
        Contribution = weight * Score;
    }

    /// <summary>直接指定曲线输出（用于"输入本身就是分数"的场合）。</summary>
    public static Consideration FromScore(string name, float input, float score, float weight)
        => new Consideration(name, input, UtilityCurve.Linear, weight, score);

    private Consideration(string name, float input, UtilityCurve curve, float weight, float score)
    {
        Name = name;
        Input = SimMath.Clamp01(input);
        Curve = curve;
        Weight = weight;
        Score = SimMath.Clamp01(score);
        Contribution = weight * Score;
    }

    public override string ToString()
        => Name + " in=" + Input.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
         + " " + Curve + " -> " + Score.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
         + " × " + Weight.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
         + " = " + Contribution.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// 一个动作的效用评估结果，含**全部**考虑项的分解。
///
/// 这是"可解释模拟"（第 92 条）的核心数据结构：
///   * 检查器直接渲染它的 Consideration 列表 ⇒ 玩家能看懂"为什么选了这个";
///   * 单元测试可以直接断言"需求升高 ⇒ 对应动作效用升高";
///   * 排障时不用猜，能一眼看出是哪一项把效用推上去/压下来的。
/// </summary>
public readonly struct ActionScore
{
    public readonly ActionKind Action;

    /// <summary>最终效用 [0,1]。</summary>
    public readonly float Utility;

    /// <summary>考虑项明细（按传入顺序）。</summary>
    public readonly Consideration[] Considerations;

    /// <summary>加权算术平均（调试用：与几何均值对比可以看出"是否被某一项拖死"）。</summary>
    public readonly float WeightedAverage;

    /// <summary>加权几何均值（可能有 0 项 ⇒ 结果为 0，这正是"硬门"语义）。</summary>
    public readonly float GeometricMean;

    /// <summary>是否有硬性门（score == 0 的高权重项）把效用压成 0。</summary>
    public readonly bool Blocked;

    public ActionScore(
        ActionKind action,
        Consideration[] considerations,
        float utility,
        float weightedAverage,
        float geometricMean,
        bool blocked)
    {
        Action = action;
        Considerations = considerations;
        Utility = utility;
        WeightedAverage = weightedAverage;
        GeometricMean = geometricMean;
        Blocked = blocked;
    }

    public override string ToString()
        => Action + " = " + Utility.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
         + (Blocked ? " (blocked)" : string.Empty);
}

/// <summary>
/// 一次决策的完整结果：所有候选动作的分数 + 最终选择。
/// 存在 AgentStore 里，供检查器显示与测试断言。
/// </summary>
public readonly struct UtilityBreakdown
{
    public readonly ActionKind Chosen;

    /// <summary>候选动作按效用降序排列（只保留前 N 个，避免存档与显示爆炸）。</summary>
    public readonly ActionScore[] Scores;

    public readonly float ChosenUtility;

    /// <summary>本次决策发生在哪个 tick（用于 UI 显示"多久没重新决策了"）。</summary>
    public readonly long Tick;

    public static readonly UtilityBreakdown Empty = new UtilityBreakdown(ActionKind.None, System.Array.Empty<ActionScore>(), 0f, 0);

    public UtilityBreakdown(ActionKind chosen, ActionScore[] scores, float chosenUtility, long tick)
    {
        Chosen = chosen;
        Scores = scores;
        ChosenUtility = chosenUtility;
        Tick = tick;
    }

    /// <summary>取某个动作的分数（找不到返回 null）。</summary>
    public ActionScore? Find(ActionKind action)
    {
        for (int i = 0; i < Scores.Length; i++)
        {
            if (Scores[i].Action == action) { return Scores[i]; }
        }
        return null;
    }
}

/// <summary>
/// 效用组合器：把若干 Consideration 合成为一个 [0,1] 的效用分。
///
/// 公式（含补偿因子，避免"乘法一票否决"带来的僵硬行为）：
///
///     linear = Σ(wᵢ · sᵢ) / Σ|wᵢ|                       （加权算术平均）
///     geom   = exp( Σ(wᵢ · ln(sᵢ + ε)) / Σ|wᵢ| )          （加权几何均值，带 ε 兜底）
///     U      = linear^0.5 × geom^0.5                       （两者折中）
///
/// 为什么要折中：
///   * 纯算术平均：任一条件差都能被其他条件补偿 ⇒ 会出现"其实不能做但硬要做"的行为；
///   * 纯几何均值：任一项为 0 则整体为 0 ⇒ 行为僵硬，且权重为负时数学不成立；
///   * 折中之后：高权重项接近 0 会把效用压得很低（接近"硬门"），但不至于完全锁死。
///
/// 权重为负的项按"惩罚"处理：先把负权重搬到正区间（w' = -w），对 score 取补（1 - s），
/// 于是它在数学上等价于一个正权重项 —— 这样几何均值仍然有定义。
/// </summary>
public static class UtilityCombiner
{
    /// <summary>几何均值的 ε 下限：防止 ln(0) 与"任意项为 0 就整体为 0"的僵硬行为。</summary>
    public const float Epsilon = 0.02f;

    public static float Combine(Consideration[] considerations, int count, out float linear, out float geometric, out bool blocked)
    {
        linear = 0f;
        geometric = 0f;
        blocked = false;

        if (considerations == null || count <= 0)
        {
            return 0f;
        }

        float positiveWeightSum = 0f;
        float linearSum = 0f;
        double logSum = 0.0;

        for (int i = 0; i < count; i++)
        {
            Consideration c = considerations[i];

            float weight = c.Weight >= 0f ? c.Weight : -c.Weight;
            float score = c.Weight >= 0f ? c.Score : 1f - c.Score;

            if (weight <= 0f) { continue; }

            positiveWeightSum += weight;
            linearSum += weight * score;
            logSum += weight * System.Math.Log(score + Epsilon);
        }

        if (positiveWeightSum <= 0f)
        {
            return 0f;
        }

        linear = linearSum / positiveWeightSum;
        geometric = (float)System.Math.Exp(logSum / positiveWeightSum);

        // "被门挡住"的判定：某个正权重项得分极低（≤ 0.05），说明前置条件基本不成立。
        for (int i = 0; i < count; i++)
        {
            if (considerations[i].Weight > 0f && considerations[i].Score <= 0.05f)
            {
                blocked = true;
                break;
            }
        }

        // 折中：算术平均与几何均值的几何平均
        float combined = (float)System.Math.Sqrt(System.Math.Max(0f, linear) * System.Math.Max(0f, geometric));
        return SimMath.Clamp01(combined);
    }
}
