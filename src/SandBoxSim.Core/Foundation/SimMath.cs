namespace SandBoxSim.Core.Foundation;

/// <summary>
/// 模拟内核的数学工具。
///
/// 设计约束：性能敏感路径用 float，状态摘要/统计用 double。
/// 所有 Clamp/Lerp 都是"确定性安全"的（无文化依赖、无随机、无查找表近似）。
/// </summary>
public static class SimMath
{
    public const float Epsilon = 1e-6f;

    public static int Clamp(int v, int min, int max)
    {
        if (v < min) { return min; }
        if (v > max) { return max; }
        return v;
    }

    public static long Clamp(long v, long min, long max)
    {
        if (v < min) { return min; }
        if (v > max) { return max; }
        return v;
    }

    public static float Clamp(float v, float min, float max)
    {
        if (v < min) { return min; }
        if (v > max) { return max; }
        return v;
    }

    public static double Clamp(double v, double min, double max)
    {
        if (v < min) { return min; }
        if (v > max) { return max; }
        return v;
    }

    /// <summary>把值限制在 [0,1] —— Utility 系统的标准归一化（第 16 条）。</summary>
    public static float Clamp01(float v) => Clamp(v, 0f, 1f);

    public static double Clamp01(double v) => Clamp(v, 0d, 1d);

    public static float Lerp(float a, float b, float t) => a + ((b - a) * t);
    public static double Lerp(double a, double b, double t) => a + ((b - a) * t);

    public static float InverseLerp(float a, float b, float v)
    {
        if (System.Math.Abs(b - a) < Epsilon) { return 0f; }
        return Clamp01((v - a) / (b - a));
    }

    public static double InverseLerp(double a, double b, double v)
    {
        if (System.Math.Abs(b - a) < 1e-12) { return 0d; }
        return Clamp01((v - a) / (b - a));
    }

    /// <summary>返回 [0,1) 范围内的同一值（0→0，1→1，负数与超界都折回）。</summary>
    public static float Fract(float v) => v - (float)System.Math.Floor(v);
    public static double Fract(double v) => v - System.Math.Floor(v);

    public static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = InverseLerp(edge0, edge1, x);
        return t * t * (3f - (2f * t));
    }

    /// <summary>线性映射：把 [inMin,inMax] 映射到 [outMin,outMax] 并裁剪。</summary>
    public static float MapClamped(float v, float inMin, float inMax, float outMin, float outMax)
        => Lerp(outMin, outMax, InverseLerp(inMin, inMax, v));

    public static double MapClamped(double v, double inMin, double inMax, double outMin, double outMax)
        => Lerp(outMin, outMax, InverseLerp(inMin, inMax, v));

    public static bool NearlyEqual(float a, float b, float tolerance = 1e-4f)
        => System.Math.Abs(a - b) <= tolerance;

    public static bool IsFinite(float v) => !(float.IsNaN(v) || float.IsInfinity(v));
    public static bool IsFinite(double v) => !(double.IsNaN(v) || double.IsInfinity(v));

    /// <summary>平方（Utility 曲线用得极多，避免 Math.Pow 的开销与平台差异）。</summary>
    public static float Square(float v) => v * v;
    public static double Square(double v) => v * v;
}
