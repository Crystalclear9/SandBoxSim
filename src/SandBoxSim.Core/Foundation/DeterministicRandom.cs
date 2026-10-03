using System.Runtime.CompilerServices;

namespace SandBoxSim.Core.Foundation;

/// <summary>
/// 分隔随机流的用途标签（第 77 条：不要把 Random.Range() 散落在代码里）。
///
/// 为什么必须分流：如果用同一条 RNG，那么"多生成一个 NPC"或"多记录一条事件"
/// 就会改变天气序列，整个世界的历史被无关改动污染，确定性与可复现性直接失效。
/// 分流后每个系统只吃自己的随机序列，互不干扰。
/// </summary>
public enum RngStream
{
    /// <summary>世界生成：地形、资源节点初始分布。只在地图创建时使用。</summary>
    WorldGen = 0,

    /// <summary>天气与气候演变。</summary>
    Weather = 1,

    /// <summary>Agent 决策抖动、出生性别与名字、性格抽样。</summary>
    Agents = 2,

    /// <summary>事件系统：火灾点燃、灾害发生、稀有事件。</summary>
    Events = 3,

    /// <summary>战斗与冲突判定（M8 用）。</summary>
    Combat = 4,

    /// <summary>杂项：UI 无关的模拟用途都放这里并注明注释。</summary>
    Misc = 5,

    /// <summary>预留：供后续系统扩展，避免改动已有流的语义而破坏旧存档。</summary>
    Reserve = 6,
}

/// <summary>
/// 确定性伪随机数发生器：xoshiro256** + SplitMix64 初始化。
///
/// 选它的理由：
///   1. 纯整数运算，跨平台/跨运行时结果完全一致（不依赖浮点实现或库版本）。
///   2. 周期 2^256-1，统计质量足够模拟使用。
///   3. 状态只有 4 个 ulong，存档里能直接写出，读回来精确续跑。
///
/// 这是模拟内核里**唯一**允许的随机源。禁止使用 System.Random。
/// 状态可保存/恢复，因此 save-load 之后随机序列不会错位（见 docs/13）。
/// </summary>
public sealed class DeterministicRandom
{
    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    // 调用计数仅用于调试与统计（不参与随机数生成，不影响确定性）。
    private long _drawCount;

    public DeterministicRandom(ulong seed) => Reset(seed);

    public long DrawCount => _drawCount;

    /// <summary>用 SplitMix64 把任意 seed 展开成 4 个状态字（避免弱种子导致开头质量差）。</summary>
    public void Reset(ulong seed)
    {
        ulong x = seed;
        _s0 = SplitMix64(ref x);
        _s1 = SplitMix64(ref x);
        _s2 = SplitMix64(ref x);
        _s3 = SplitMix64(ref x);

        // 全零状态是 xoshiro 的退化点，必须规避。
        if ((_s0 | _s1 | _s2 | _s3) == 0UL)
        {
            _s0 = 0x9E3779B97F4A7C15UL;
            _s1 = 0xBF58476D1CE4E5B9UL;
            _s2 = 0x94D049BB133111EBUL;
            _s3 = 0x2545F4914F6CDD1DUL;
        }
        _drawCount = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong SplitMix64(ref ulong x)
    {
        unchecked
        {
            x += 0x9E3779B97F4A7C15UL;
            ulong z = x;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong RotateLeft(ulong x, int k) => (x << k) | (x >> (64 - k));

    /// <summary>下一个 64 位无符号随机数（核心生成器）。</summary>
    public ulong NextULong()
    {
        unchecked
        {
            _drawCount++;

            ulong result = RotateLeft(_s1 * 5UL, 7) * 9UL;
            ulong t = _s1 << 17;

            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = RotateLeft(_s3, 45);

            return result;
        }
    }

    /// <summary>[0, bound) 内的均匀整数。bound &lt;= 0 时返回 0。</summary>
    public int NextInt(int bound)
    {
        if (bound <= 0) { return 0; }
        if (bound == 1) { return 0; }

        // Lemire 无偏拒绝采样，纯整数实现以保证跨平台一致。
        ulong range = (ulong)bound;
        ulong limit = ulong.MaxValue - (ulong.MaxValue % range);
        ulong draw;
        do
        {
            draw = NextULong();
        }
        while (draw >= limit);

        return (int)(draw % range);
    }

    /// <summary>[minInclusive, maxExclusive) 内的整数。</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive) { return minInclusive; }
        return minInclusive + NextInt(maxExclusive - minInclusive);
    }

    /// <summary>[0,1) 的 double。用 53 位尾数，避免精度抖动影响分支判定。</summary>
    public double NextDouble()
    {
        ulong bits = NextULong() >> 11; // 保留 53 位
        return bits * (1.0 / 9007199254740992.0); // 1/2^53
    }

    /// <summary>[min,max) 的 double。</summary>
    public double NextDouble(double min, double max) => min + (NextDouble() * (max - min));

    /// <summary>[0,1) 的 float。</summary>
    public float NextFloat() => (float)NextDouble();

    /// <summary>[0,1] 的 float。</summary>
    public float NextFloatInclusive()
    {
        float v = NextFloat();
        return v >= 1.0f ? 0.9999999f : v;
    }

    /// <summary>以 probability 概率返回 true（probability &lt;= 0 恒 false，&gt;= 1 恒 true）。</summary>
    public bool Chance(double probability)
    {
        if (probability <= 0.0) { return false; }
        if (probability >= 1.0) { return true; }
        return NextDouble() < probability;
    }

    /// <summary>中心化抖动：返回 [-magnitude, +magnitude] 的 double。</summary>
    public double Jitter(double magnitude) => (NextDouble() * 2.0 - 1.0) * magnitude;

    /// <summary>从权重数组里按权重抽样，返回下标。权重必须非负；总和为 0 时返回 -1。</summary>
    public int SampleWeightedIndex(double[] weights, int count)
    {
        double total = 0.0;
        for (int i = 0; i < count; i++)
        {
            if (weights[i] > 0.0) { total += weights[i]; }
        }
        if (total <= 0.0) { return -1; }

        double pick = NextDouble() * total;
        double acc = 0.0;
        for (int i = 0; i < count; i++)
        {
            double w = weights[i];
            if (w <= 0.0) { continue; }
            acc += w;
            if (pick < acc) { return i; }
        }
        // 浮点误差兜底：返回最后一个正权重项。
        for (int i = count - 1; i >= 0; i--)
        {
            if (weights[i] > 0.0) { return i; }
        }
        return -1;
    }

    /// <summary>原地 Fisher-Yates 洗牌（确定性：用本流自己的随机数）。</summary>
    public void Shuffle<T>(T[] array, int count)
    {
        for (int i = count - 1; i > 0; i--)
        {
            int j = NextInt(i + 1);
            T tmp = array[i];
            array[i] = array[j];
            array[j] = tmp;
        }
    }

    /// <summary>导出状态（存档用）。顺序固定：s0,s1,s2,s3,drawCount。</summary>
    public ulong[] ExportState() => new[] { _s0, _s1, _s2, _s3, unchecked((ulong)_drawCount) };

    /// <summary>导入状态（读档用）。长度不足时忽略多余部分，保证向前兼容。</summary>
    public void ImportState(ulong[] state)
    {
        if (state == null || state.Length < 4) { return; }
        _s0 = state[0];
        _s1 = state[1];
        _s2 = state[2];
        _s3 = state[3];
        _drawCount = state.Length > 4 ? unchecked((long)state[4]) : 0;
    }
}

/// <summary>
/// 按用途分流的随机源集合。模拟系统通过它拿随机数，而不是共享一个全局实例。
/// </summary>
public sealed class SimRandom
{
    private const int StreamCount = 7;
    private readonly DeterministicRandom[] _streams;

    public ulong Seed { get; private set; }

    public SimRandom(ulong seed)
    {
        Seed = seed;
        _streams = new DeterministicRandom[StreamCount];
        for (int i = 0; i < StreamCount; i++)
        {
            // 每个流用不同的派生种子，保证流之间不相关。
            ulong streamSeed = Mix(seed, (ulong)i);
            _streams[i] = new DeterministicRandom(streamSeed);
        }
    }

    /// <summary>用种子与"管道 id"混合出独立流种子（SplitMix 风格 finalizer）。</summary>
    private static ulong Mix(ulong seed, ulong pipe)
    {
        unchecked
        {
            ulong z = seed + (pipe * 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    public DeterministicRandom Get(RngStream stream) => _streams[(int)stream];

    /// <summary>所有流的抽取次数总和。测试里用来确认"同一 seed 两跑消耗一致"。</summary>
    public long TotalDrawCount
    {
        get
        {
            long total = 0;
            for (int i = 0; i < StreamCount; i++) { total += _streams[i].DrawCount; }
            return total;
        }
    }

    /// <summary>导出全部流状态（存档用）。</summary>
    public ulong[][] ExportState()
    {
        var result = new ulong[StreamCount][];
        for (int i = 0; i < StreamCount; i++) { result[i] = _streams[i].ExportState(); }
        return result;
    }

    /// <summary>导入全部流状态（读档用）。</summary>
    public void ImportState(ulong[][] state)
    {
        if (state == null) { return; }
        for (int i = 0; i < StreamCount && i < state.Length; i++)
        {
            _streams[i].ImportState(state[i]);
        }
    }

    /// <summary>重新派生全部流（换 seed 时用）。</summary>
    public void Reseed(ulong seed)
    {
        Seed = seed;
        for (int i = 0; i < StreamCount; i++)
        {
            _streams[i].Reset(Mix(seed, (ulong)i));
        }
    }
}
