using System.Runtime.CompilerServices;

namespace SandBoxSim.Core.Foundation;

/// <summary>
/// Perlin 梯度噪声 + fBm（分形叠加）。用于世界生成的高度图与湿度图。
///
/// 为什么用 Perlin 而不是简单随机：随机噪点生成的地图没有空间相关性，
/// 形不成大陆、山脉、河流的"地理感"，后续寻路与聚落选址也会变得没有意义。
///
/// 确定性：置换表与梯度表都由调用方给定的种子确定性洗牌生成，
/// 不依赖任何库的默认随机源，因此同 seed 必然逐格一致（验收标准 2）。
/// </summary>
public sealed class PerlinNoise
{
    private const int TableSize = 256;
    private const int TableMask = 255;

    /// <summary>梯度向量分量（12 个经典 Perlin 方向的 x 分量）。</summary>
    private static readonly int[] GradX = { 1, -1, 1, -1, 1, -1, 1, -1, 0, 0, 0, 0 };
    private static readonly int[] GradY = { 1, 1, -1, -1, 0, 0, 0, 0, 1, -1, 1, -1 };

    private readonly int[] _permutation = new int[TableSize];
    private readonly int[] _gradientIndex = new int[TableSize * 2];

    public ulong Seed { get; }

    public PerlinNoise(ulong seed)
    {
        Seed = seed;

        // 用同一个确定性 RNG（独立实例，不占用 SimRandom 的流）构造置换表。
        var rng = new DeterministicRandom(seed);
        for (int i = 0; i < TableSize; i++) { _permutation[i] = i; }
        rng.Shuffle(_permutation, TableSize);

        for (int i = 0; i < TableSize * 2; i++)
        {
            _gradientIndex[i] = _permutation[i & TableMask] % 12;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Fade(float t) => t * t * t * ((t * ((t * 6f) - 15f)) + 10f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Grad(int hash, float x, float y)
    {
        int g = hash % 12;
        return (GradX[g] * x) + (GradY[g] * y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Perm(int index) => _permutation[index & TableMask];

    /// <summary>
    /// 经典二维 Perlin 噪声，输出范围经验上落在约 [-1,1]（不保证严格边界）。
    /// </summary>
    public float Sample(float x, float y)
    {
        int xi = (int)System.Math.Floor(x);
        int yi = (int)System.Math.Floor(y);
        float xf = x - xi;
        float yf = y - yi;

        // 用位与代替取模：坐标可能为负，& 255 与 Math.Floor 语义一致（补码性质）。
        int xw = xi & TableMask;
        int yw = yi & TableMask;

        int a = Perm(xw);
        int b = Perm(xw + 1);

        int aa = Perm(a + yw);
        int ab = Perm(a + yw + 1);
        int ba = Perm(b + yw);
        int bb = Perm(b + yw + 1);

        float u = Fade(xf);
        float v = Fade(yf);

        float x1 = SimMath.Lerp(Grad(_gradientIndex[aa], xf, yf), Grad(_gradientIndex[ba], xf - 1f, yf), u);
        float x2 = SimMath.Lerp(Grad(_gradientIndex[ab], xf, yf - 1f), Grad(_gradientIndex[bb], xf - 1f, yf - 1f), u);
        return SimMath.Lerp(x1, x2, v);
    }

    /// <summary>把 Perlin 输出映射到 [0,1]（模拟里绝大多数场合需要这个）。</summary>
    public float Sample01(float x, float y) => SimMath.Clamp01((Sample(x, y) * 0.5f) + 0.5f);

    /// <summary>
    /// 分形布朗运动（fBm）：多倍频叠加，得到自然起伏。
    /// </summary>
    /// <param name="x">采样坐标（已乘以基频）。</param>
    /// <param name="y">采样坐标（已乘以基频）。</param>
    /// <param name="octaves">倍频层数，1 表示退化为单层 Perlin。</param>
    /// <param name="persistence">每层振幅衰减（0.5 是经典值，越小越平滑）。</param>
    /// <param name="lacunarity">每层频率放大倍数。</param>
    /// <returns>归一化到 [0,1] 的结果。</returns>
    public float Fbm01(float x, float y, int octaves, float persistence, float lacunarity = 2f)
    {
        if (octaves < 1) { octaves = 1; }

        float sum = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        float totalAmplitude = 0f;

        for (int o = 0; o < octaves; o++)
        {
            sum += Sample(x * frequency, y * frequency) * amplitude;
            totalAmplitude += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        if (totalAmplitude <= 0f) { return 0.5f; }
        float normalized = sum / totalAmplitude;
        return SimMath.Clamp01((normalized * 0.5f) + 0.5f);
    }
}
