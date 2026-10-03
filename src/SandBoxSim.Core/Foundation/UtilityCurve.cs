namespace SandBoxSim.Core.Foundation;

/// <summary>
/// Utility 曲线（第 17 条）。
///
/// 设计意图：**输入永远是 [0,1] 的归一化需求强度，输出永远是 [0,1] 的效用**。
/// 曲线形状决定"行为的紧迫感从哪里开始"：
///   Linear    —— 中性，随需求平缓上升
///   Quadratic —— 不饿时完全不想吃，接近极限时急剧上升（最常用的"生存紧迫感"）
///   Logistic  —— 阈值型：低于阈值几乎不做，高于阈值立刻想做
///   InverseLogistic —— 反向阈值：条件好时优先，条件变差迅速放弃
///   Step      —— 硬条件（只在确实可用时才考虑）
///
/// 禁止在动作代码里直接写裸的线性乘法：所有效用都必须经过曲线对象，
/// 这样调参只需要改配置，而不用改动作逻辑。
/// </summary>
public sealed class UtilityCurve
{
    public enum Shape
    {
        /// <summary>恒等：U(x)=x。</summary>
        Linear = 0,

        /// <summary>平方：U(x)=x²。低需求时几乎不动，高需求时爆发。</summary>
        Quadratic = 1,

        /// <summary>平方根：U(x)=√x。稍有需求就有明显倾向（温和/勤劳型行为）。</summary>
        Sqrt = 2,

        /// <summary>Logistic 阈值：U(x)=1/(1+e^(-k(x-x0)))，输出已归一化到 [0,1]。</summary>
        Logistic = 3,

        /// <summary>反向 Logistic：条件越好效用越高，用于"机会型"行为。</summary>
        InverseLogistic = 4,

        /// <summary>硬阈值：x ≥ threshold 输出 1，否则 0。</summary>
        Step = 5,

        /// <summary>SmoothStep：平滑的 S 曲线，介于 Linear 与 Logistic 之间。</summary>
        SmoothStep = 6,

        /// <summary>常数：与输入无关（用于"纯权重"考虑项）。</summary>
        Constant = 7,

        /// <summary>生存曲线：U(x)=1−(1−x)²。低需求区就快速抬升，用于生存类动作。</summary>
        Survival = 8,
    }

    public Shape Kind { get; }
    public float Steepness { get; }      // Logistic 的 k
    public float Midpoint { get; }       // Logistic 的 x0 / Step 的阈值
    public float Edge0 { get; }          // SmoothStep 下沿
    public float Edge1 { get; }          // SmoothStep 上沿
    public float ConstantValue { get; }  // Constant 的输出

    public UtilityCurve(
        Shape kind,
        float steepness = 10f,
        float midpoint = 0.5f,
        float edge0 = 0f,
        float edge1 = 1f,
        float constantValue = 1f)
    {
        Kind = kind;
        Steepness = steepness;
        Midpoint = midpoint;
        Edge0 = edge0;
        Edge1 = edge1;
        ConstantValue = constantValue;
    }

    public static UtilityCurve Linear => new UtilityCurve(Shape.Linear);
    public static UtilityCurve Quadratic => new UtilityCurve(Shape.Quadratic);
    public static UtilityCurve Sqrt => new UtilityCurve(Shape.Sqrt);
    public static UtilityCurve Constant(float value) => new UtilityCurve(Shape.Constant, constantValue: value);

    /// <summary>
    /// 生存曲线：U(x) = 1 − (1−x)²。
    ///
    /// 为什么生存类动作必须要它（而不是 Linear / Quadratic）：
    ///   效用合成器里带了补偿因子（否则行为会僵硬），这带来一个副作用 ——
    ///   **"被门挡住"的动作其效用会停留在一个下界**（等于折中后的补偿项）。
    ///   如果食物采集在"有点饿"时打分低于"顺手砍柴"，个体就会一直砍柴直到饿死
    ///   （实测确实出现过：40 人全饿死，采集统计里食物为 0）。
    ///   1−(1−x)² 在低输入区就快速抬升（x=0.3 ⇒ 0.51），于是"开始饿"就能压过非生存行为；
    ///   同时它仍然是单调递增且有界的，符合效用系统的硬性要求。
    /// </summary>
    public static UtilityCurve Survival => new UtilityCurve(Shape.Survival);

    public static UtilityCurve Logistic(float steepness = 10f, float midpoint = 0.5f)
        => new UtilityCurve(Shape.Logistic, steepness, midpoint);

    public static UtilityCurve Threshold(float threshold)
        => new UtilityCurve(Shape.Step, midpoint: threshold);

    /// <summary>求值。输入会被裁剪到 [0,1]，输出保证在 [0,1] 且有限（NaN 兜底为 0）。</summary>
    public float Evaluate(float x)
    {
        float input = SimMath.Clamp01(x);
        float result;

        switch (Kind)
        {
            case Shape.Linear:
                result = input;
                break;

            case Shape.Quadratic:
                result = input * input;
                break;

            case Shape.Sqrt:
                result = (float)System.Math.Sqrt(input);
                break;

            case Shape.Logistic:
            {
                // 归一化：把 logistic 的原始值映射回 [0,1]，使 x=0→0 与 x=1→1 近似成立。
                double raw = 1.0 / (1.0 + System.Math.Exp(-Steepness * (input - Midpoint)));
                double lo = 1.0 / (1.0 + System.Math.Exp(-Steepness * (0.0 - Midpoint)));
                double hi = 1.0 / (1.0 + System.Math.Exp(-Steepness * (1.0 - Midpoint)));
                double span = hi - lo;
                result = span > 1e-9 ? (float)((raw - lo) / span) : input;
                break;
            }

            case Shape.InverseLogistic:
                // 单调**递增**的"反向 S 形"：条件越好（x 越大）效用越高。
                // 实现方式是先把输入翻转再走 Logistic，最后再翻转回来：
                //   U(x) = 1 − L(1 − x)
                // 这样 U(0)=0、U(1)=1，且满足 Utility 体系对"单调不减"的硬要求。
                // （早期写成 1−L(x) 会得到单调递减曲线，在大量动作里会静默反转行为倾向，
                //   甚至让"条件越好越不想做"这种荒诞行为出现。）
                result = 1f - Logistic(Steepness, 1f - Midpoint).Evaluate(1f - input);
                break;

            case Shape.Step:
                result = input >= Midpoint ? 1f : 0f;
                break;

            case Shape.SmoothStep:
                result = SimMath.SmoothStep(Edge0, Edge1, input);
                break;

            case Shape.Constant:
                result = ConstantValue;
                break;

            case Shape.Survival:
            {
                float complement = 1f - input;
                result = 1f - (complement * complement);
                break;
            }

            default:
                result = input;
                break;
        }

        if (!SimMath.IsFinite(result)) { return 0f; }
        return SimMath.Clamp01(result);
    }

    public override string ToString()
    {
        switch (Kind)
        {
            case Shape.Logistic:
                return "logistic(k=" + Steepness.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                     + ",x0=" + Midpoint.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + ")";
            case Shape.Step:
                return "step(>=" + Midpoint.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + ")";
            case Shape.SmoothStep:
                return "smoothstep";
            case Shape.Constant:
                return "const(" + ConstantValue.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + ")";
            default:
                return Kind.ToString().ToLowerInvariant();
        }
    }

    /// <summary>从配置文件里的字符串名解析曲线形状（大小写不敏感）。</summary>
    public static Shape ParseShape(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) { return Shape.Linear; }
        switch (name.Trim().ToLowerInvariant())
        {
            case "linear": return Shape.Linear;
            case "quadratic":
            case "square": return Shape.Quadratic;
            case "sqrt": return Shape.Sqrt;
            case "logistic": return Shape.Logistic;
            case "inverselogistic": return Shape.InverseLogistic;
            case "step":
            case "threshold": return Shape.Step;
            case "smoothstep": return Shape.SmoothStep;
            case "constant": return Shape.Constant;
            case "survival": return Shape.Survival;
            default: return Shape.Linear;
        }
    }
}
