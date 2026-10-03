using System.Runtime.CompilerServices;

namespace SandBoxSim.Core.Foundation;

/// <summary>
/// 确定性坐标散列：把坐标（+可选种子）映射成稳定的伪随机值。
///
/// 用途：地形色块微噪、装饰性抖动、以及"每格固定不变的小参数"
/// （比如此格的路况差异）。**绝不允许**用 System.Random 或时间做这类事，
/// 否则同一张地图每次渲染都不一样，观察性直接崩掉。
/// </summary>
public static class NoiseHash
{
    /// <summary>二维坐标 → 32 位无符号伪随机（与种子混合）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Hash2D(int x, int y, uint seed)
    {
        unchecked
        {
            uint h = seed + 0x9E3779B9u;
            h ^= (uint)x * 0x85EBCA6Bu;
            h = (h << 13) | (h >> 19);
            h ^= (uint)y * 0xC2B2AE35u;
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;
            return h;
        }
    }

    /// <summary>二维坐标 → [0,1) 的 double 噪点。</summary>
    public static double Value01(int x, int y, uint seed)
        => Hash2D(x, y, seed) * (1.0 / 4294967296.0);

    /// <summary>二维坐标 → [-1,1) 的 double 噪点。</summary>
    public static double Value11(int x, int y, uint seed)
        => (Hash2D(x, y, seed) * (1.0 / 4294967296.0)) * 2.0 - 1.0;

    /// <summary>二维坐标 + 额外盐值 → [0,1)，用于"同一格多个独立属性"。</summary>
    public static double Value01(int x, int y, uint seed, int salt)
        => Value01(x, y, unchecked(seed + ((uint)salt * 0x9E3779B9u)));

    /// <summary>二维坐标 → 由取值决定的小幅视觉扰动（例如 ±0.03 的色差）。</summary>
    public static float VisualJitter(int x, int y, uint seed, float magnitude)
        => (float)(Value01(x, y, seed) * 2.0 - 1.0) * magnitude;
}
