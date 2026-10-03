namespace SandBoxSim.Core.Foundation;

/// <summary>
/// 确定性哈希。两个用途：
///   1. 状态摘要（state digest）：同 seed 两跑必须得到同一个 64 位值 —— 验收标准 2 的判据。
///   2. 纯视觉扰动（例如地形色块微噪），不允许影响任何模拟逻辑。
///
/// 选用 FNV-1a 64 位：实现简单、跨平台逐位一致、无需依赖 System.HashCode
/// （后者在不同运行时会加随机化种子，绝不可用于确定性场景）。
/// </summary>
public static class Hash64
{
    public const ulong FnvOffsetBasis = 14695981039346656037UL;
    public const ulong FnvPrime = 1099511628211UL;

    public static ulong Begin() => FnvOffsetBasis;

    public static ulong Combine(ulong hash, ulong value)
    {
        unchecked
        {
            hash ^= value;
            hash *= FnvPrime;
            return hash;
        }
    }

    public static ulong Combine(ulong hash, int value) => Combine(hash, unchecked((ulong)(uint)value));

    /// <summary>
    /// float → 位模式再哈希。刻意不用 Math.Round 或格式化字符串：
    /// 任何依赖文化/区域设置的转换都会破坏跨机器一致性。
    /// </summary>
    public static ulong Combine(ulong hash, float value)
        => Combine(hash, unchecked((ulong)(uint)System.BitConverter.SingleToInt32Bits(value)));

    public static ulong Combine(ulong hash, double value)
        => Combine(hash, unchecked((ulong)System.BitConverter.DoubleToInt64Bits(value)));

    public static ulong Combine(ulong hash, bool value) => Combine(hash, value ? 1UL : 0UL);

    public static ulong Combine(ulong hash, string? value)
    {
        if (value == null) { return Combine(hash, 0xDEADBEEFUL); }
        for (int i = 0; i < value.Length; i++)
        {
            hash = Combine(hash, value[i]);
        }
        return hash;
    }

    public static ulong Combine(ulong hash, Int2 value)
    {
        hash = Combine(hash, value.X);
        return Combine(hash, value.Y);
    }

    /// <summary>一次性哈希一个 64 位值。</summary>
    public static ulong Of(ulong value) => Combine(Begin(), value);
    public static ulong Of(int value) => Combine(Begin(), value);
    public static ulong Of(string value) => Combine(Begin(), value);

    /// <summary>16 进制摘要字符串（日志/报告用），固定 16 字符宽度。</summary>
    public static string ToDigestString(ulong hash) => hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
}
