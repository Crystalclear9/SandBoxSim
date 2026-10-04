using System.Collections.Generic;

namespace SandBoxSim.Tests.Framework;

/// <summary>
/// 断言库。刻意保持很小：只提供测试真正需要的断言，
/// 每个断言失败都要给出**可读的差异信息**（模拟测试里常见"两个 0.83 不相等"这种无信息失败）。
/// </summary>
public static class Assert
{
    public sealed class SkippedException : System.Exception
    {
        public SkippedException(string reason) : base(reason) { }
    }

    public static void Skip(string reason) => throw new SkippedException(reason);

    public static void True(bool condition, string? message = null)
    {
        if (!condition) { FailInternal("期望为 true，实际为 false" + Suffix(message)); }
    }

    public static void False(bool condition, string? message = null)
    {
        if (condition) { FailInternal("期望为 false，实际为 true" + Suffix(message)); }
    }

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            FailInternal("期望 " + Format(expected) + "，实际 " + Format(actual) + Suffix(message));
        }
    }

    public static void NotEqual<T>(T unexpected, T actual, string? message = null)
    {
        if (EqualityComparer<T>.Default.Equals(unexpected, actual))
        {
            FailInternal("期望不等于 " + Format(unexpected) + "，但实际相等" + Suffix(message));
        }
    }

    public static void Null(object? value, string? message = null)
    {
        if (value != null) { FailInternal("期望为 null，实际 " + Format(value) + Suffix(message)); }
    }

    public static void NotNull(object? value, string? message = null)
    {
        if (value == null) { FailInternal("期望非 null" + Suffix(message)); }
    }

    public static void Greater<T>(T actual, T threshold, string? message = null) where T : System.IComparable<T>
    {
        if (actual.CompareTo(threshold) <= 0)
        {
            FailInternal("期望 " + Format(actual) + " > " + Format(threshold) + Suffix(message));
        }
    }

    public static void GreaterOrEqual<T>(T actual, T threshold, string? message = null) where T : System.IComparable<T>
    {
        if (actual.CompareTo(threshold) < 0)
        {
            FailInternal("期望 " + Format(actual) + " >= " + Format(threshold) + Suffix(message));
        }
    }

    public static void Less<T>(T actual, T threshold, string? message = null) where T : System.IComparable<T>
    {
        if (actual.CompareTo(threshold) >= 0)
        {
            FailInternal("期望 " + Format(actual) + " < " + Format(threshold) + Suffix(message));
        }
    }

    public static void LessOrEqual<T>(T actual, T threshold, string? message = null) where T : System.IComparable<T>
    {
        if (actual.CompareTo(threshold) > 0)
        {
            FailInternal("期望 " + Format(actual) + " <= " + Format(threshold) + Suffix(message));
        }
    }

    public static void InRange(double actual, double min, double max, string? message = null)
    {
        if (double.IsNaN(actual) || double.IsNaN(min) || double.IsNaN(max) || min > max || actual < min || actual > max)
        {
            FailInternal("期望 " + Format(actual) + " 落在 [" + Format(min) + ", " + Format(max) + "]" + Suffix(message));
        }
    }

    public static void InRange(float actual, float min, float max, string? message = null)
        => InRange((double)actual, min, max, message);

    public static void Near(double expected, double actual, double tolerance = 1e-6, string? message = null)
    {
        if (!double.IsFinite(expected) || !double.IsFinite(actual) || !double.IsFinite(tolerance)
            || tolerance < 0 || System.Math.Abs(expected - actual) > tolerance)
        {
            FailInternal("期望 " + Format(actual) + " 接近 " + Format(expected)
                + "（容差 " + Format(tolerance) + "，实际偏差 " + Format(System.Math.Abs(expected - actual)) + "）" + Suffix(message));
        }
    }

    public static void SequenceEqual<T>(System.Collections.Generic.IReadOnlyList<T> expected,
        System.Collections.Generic.IReadOnlyList<T> actual, string? message = null)
    {
        if (expected.Count != actual.Count)
        {
            FailInternal("序列长度不同：期望 " + expected.Count + "，实际 " + actual.Count + Suffix(message));
        }
        for (int i = 0; i < expected.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(expected[i], actual[i]))
            {
                FailInternal("序列第 " + i + " 项不同：期望 " + Format(expected[i]) + "，实际 " + Format(actual[i]) + Suffix(message));
            }
        }
    }

    public static TException Throws<TException>(System.Action action, string? message = null)
        where TException : System.Exception
    {
        try
        {
            action();
        }
        catch (TException expected)
        {
            return expected;
        }
        catch (System.Exception other)
        {
            FailInternal("期望抛出 " + typeof(TException).Name + "，实际抛出 " + other.GetType().Name
                + "：" + other.Message + Suffix(message));
            throw;   // 不可达
        }

        FailInternal("期望抛出 " + typeof(TException).Name + "，但没有抛出任何异常" + Suffix(message));
        throw new System.InvalidOperationException("不可达");
    }

    public static void DoesNotThrow(System.Action action, string? message = null)
    {
        try
        {
            action();
        }
        catch (System.Exception ex)
        {
            FailInternal("期望不抛异常，实际抛出 " + ex.GetType().Name + "：" + ex.Message + Suffix(message));
        }
    }

    /// <summary>
    /// 无条件失败。用于"这里不该发生这种事"的兜底断言
    /// （比 <c>Assert.True(false, ...)</c> 更直白，也避免被误读成条件判断）。
    /// </summary>
    /// <summary>无条件失败（用于"这里不该发生这种事"的兜底断言）。</summary>
    public static void Fail(string message) => FailInternal(message);

    private static string Suffix(string? message)
        => string.IsNullOrEmpty(message) ? string.Empty : "；" + message;

    private static string Format(object? value)
    {
        if (value == null) { return "null"; }
        if (value is float f) { return f.ToString("R", System.Globalization.CultureInfo.InvariantCulture); }
        if (value is double d) { return d.ToString("R", System.Globalization.CultureInfo.InvariantCulture); }
        if (value is bool b) { return b ? "true" : "false"; }
        return value.ToString() ?? "null";
    }

    /// <summary>断言失败时抛这个，运行器据此定位是"测试失败"而不是"测试崩溃"。</summary>
    public sealed class AssertionException : System.Exception
    {
        public AssertionException(string message) : base(message) { }
    }

    private static void FailInternal(string message) => throw new AssertionException(message);
}
