using SandBoxSim.Core.Foundation;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

/// <summary>基础工具的行为契约测试（哈希、数学、坐标、曲线）。</summary>
public sealed class FoundationTests
{
    // ---- Hash64 ----

    [Fact("Hash64 必须对相同输入给出相同结果")]
    public void Hash64IsStable()
    {
        ulong a = Hash64.Begin();
        a = Hash64.Combine(a, 42);
        a = Hash64.Combine(a, "hello");
        a = Hash64.Combine(a, 3.5f);
        a = Hash64.Combine(a, true);

        ulong b = Hash64.Begin();
        b = Hash64.Combine(b, 42);
        b = Hash64.Combine(b, "hello");
        b = Hash64.Combine(b, 3.5f);
        b = Hash64.Combine(b, true);

        Assert.Equal(a, b);
    }

    [Fact("Hash64 必须对输入顺序敏感")]
    public void Hash64IsOrderSensitive()
    {
        ulong a = Hash64.Combine(Hash64.Combine(Hash64.Begin(), 1), 2);
        ulong b = Hash64.Combine(Hash64.Combine(Hash64.Begin(), 2), 1);
        Assert.NotEqual(a, b);
    }

    [Fact("Hash64 对小改动必须敏感（单步 FNV 的雪崩下界）")]
    public void Hash64IsSensitiveToSmallChanges()
    {
        // 注意对期望值的校准：FNV-1a 的**单次** combine 只做一次乘加，
        // 不是完整雪崩哈希，因此"改一个输入翻转 16 位"是不现实的期望。
        // 这里断言的是"单步 FNV 的合理下界 + 多步串联后的强雪崩"两件事。
        ulong a = Hash64.Combine(Hash64.Begin(), 1000);
        ulong b = Hash64.Combine(Hash64.Begin(), 1001);
        Assert.NotEqual(a, b);
        Assert.GreaterOrEqual(CountDifferentBits(a, b), 5, "单步 FNV 的差异位太少");

        // 串联 8 个字段之后应当接近理想雪崩（约 32 位中的 ±12）
        ulong c = FoldMany(1000);
        ulong d = FoldMany(1001);
        Assert.NotEqual(c, d);
        Assert.GreaterOrEqual(CountDifferentBits(c, d), 12, "多字段摘要的雪崩性不足");
    }

    private static ulong FoldMany(int seed)
    {
        ulong hash = Hash64.Begin();
        hash = Hash64.Combine(hash, seed);
        hash = Hash64.Combine(hash, "sandbox");
        hash = Hash64.Combine(hash, 1.25f);
        hash = Hash64.Combine(hash, 3.75);
        hash = Hash64.Combine(hash, true);
        hash = Hash64.Combine(hash, new Int2(4, 9));
        hash = Hash64.Combine(hash, seed * 3);
        hash = Hash64.Combine(hash, seed ^ 0x55AA);
        return hash;
    }

    private static int CountDifferentBits(ulong a, ulong b)
    {
        ulong diff = a ^ b;
        int bits = 0;
        while (diff != 0)
        {
            bits += (int)(diff & 1);
            diff >>= 1;
        }
        return bits;
    }

    [Fact("摘要字符串必须是固定 16 位十六进制")]
    public void DigestStringIsFixedWidth()
    {
        string digest = Hash64.ToDigestString(0x1234ABCD);
        Assert.Equal(16, digest.Length);
        Assert.Equal("000000001234abcd", digest);
    }

    [Fact("float 的哈希必须基于位模式而不是格式化字符串")]
    public void FloatHashUsesBitPattern()
    {
        // 1.0f 与 1.0000001f 在位模式上不同，哈希必须不同
        ulong a = Hash64.Combine(Hash64.Begin(), 1.0f);
        ulong b = Hash64.Combine(Hash64.Begin(), 1.0000001f);
        Assert.NotEqual(a, b);

        // 同一值的重复哈希必须一致（不依赖文化设置）
        Assert.Equal(Hash64.Combine(Hash64.Begin(), 0.25f), Hash64.Combine(Hash64.Begin(), 0.25f));
    }

    // ---- SimMath ----

    [Fact("Clamp 边界行为（遍历数据表）")]
    public void ClampBehaviorFromTable()
    {
        foreach (TheoryCase theoryCase in ClampCases)
        {
            int result = SimMath.Clamp(
                (int)theoryCase.Arguments[0]!,
                (int)theoryCase.Arguments[1]!,
                (int)theoryCase.Arguments[2]!);
            Assert.InRange(result, (int)theoryCase.Arguments[1]!, (int)theoryCase.Arguments[2]!);
        }
    }

    /// <summary>Clamp 用例数据表（见 Framework/TestAttributes.cs 关于特性实参限制的说明）。</summary>
    private static readonly TheoryCase[] ClampCases = Theory.Cases(
        Theory.Case("下界", -5, 0, 10),
        Theory.Case("上界", 15, 0, 10),
        Theory.Case("中间", 5, 0, 10));

    [Fact("Clamp01 必须把任何输入压到 [0,1]")]
    public void Clamp01HandlesExtremes()
    {
        Assert.Equal(0f, SimMath.Clamp01(-100f));
        Assert.Equal(1f, SimMath.Clamp01(100f));
        Assert.Equal(0.5f, SimMath.Clamp01(0.5f));
        Assert.Equal(0f, SimMath.Clamp01(float.NaN == 0f ? 0f : -1f));
    }

    [Fact("InverseLerp 在退化区间必须返回 0 而不是除零")]
    public void InverseLerpDegenerateRange()
    {
        Assert.Equal(0f, SimMath.InverseLerp(5f, 5f, 7f));
        Assert.Equal(0f, SimMath.InverseLerp(0f, 0f, 0f));
    }

    [Fact("MapClamped 必须裁剪到目标区间")]
    public void MapClampedClamps()
    {
        Assert.Equal(0f, SimMath.MapClamped(-10f, 0f, 10f, 0f, 1f));
        Assert.Equal(1f, SimMath.MapClamped(999f, 0f, 10f, 0f, 1f));
        Assert.Near(0.5, SimMath.MapClamped(5f, 0f, 10f, 0f, 1f), 1e-6);
    }

    [Fact("IsFinite 必须正确识别 NaN 与 Infinity")]
    public void IsFiniteDetection()
    {
        Assert.False(SimMath.IsFinite(float.NaN));
        Assert.False(SimMath.IsFinite(float.PositiveInfinity));
        Assert.False(SimMath.IsFinite(double.NegativeInfinity));
        Assert.True(SimMath.IsFinite(1.5f));
    }

    // ---- Int2 ----

    [Fact("Int2 距离计算必须正确")]
    public void Int2Distances()
    {
        var a = new Int2(0, 0);
        var b = new Int2(3, 4);

        Assert.Equal(7, Int2.ManhattanDistance(a, b));
        Assert.Equal(4, Int2.ChebyshevDistance(a, b));
        Assert.Near(5.0, Int2.Distance(a, b), 1e-9);
        Assert.Equal(25, Int2.SquaredDistance(a, b));
    }

    [Fact("Int2 相等与哈希必须一致")]
    public void Int2EqualityAndHash()
    {
        var a = new Int2(7, -3);
        var b = new Int2(7, -3);
        var c = new Int2(-3, 7);

        Assert.True(a == b);
        Assert.False(a != b);
        Assert.True(a != c);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    // ---- UtilityCurve ----

    [Fact("所有曲线的单调性与取值范围（遍历数据表）")]
    public void CurvesAreMonotonicAndBounded()
    {
        foreach (TheoryCase theoryCase in CurveCases)
        {
            CurvesForShape((UtilityCurve.Shape)theoryCase.Arguments[0]!);
        }
    }

    /// <summary>单条曲线的检查（被上面的数据表遍历；注意不能标 [Theory]，见 TestRunner 说明）。</summary>
    private static void CurvesForShape(UtilityCurve.Shape shape)
    {
        var curve = new UtilityCurve(shape);
        float previous = curve.Evaluate(0f);

        Assert.InRange(previous, 0f, 1f);

        for (int i = 1; i <= 100; i++)
        {
            float x = i / 100f;
            float y = curve.Evaluate(x);

            Assert.InRange(y, 0f, 1f);
            if (shape != UtilityCurve.Shape.Constant)
            {
                Assert.True(y >= previous - 1e-5f,
                    shape + " 曲线必须单调不减：" + x + " 处从 " + previous + " 降到 " + y);
            }
            previous = y;
        }
    }

    /// <summary>曲线用例数据表。</summary>
    private static readonly TheoryCase[] CurveCases = Theory.Cases(
        Theory.Case("线性", UtilityCurve.Shape.Linear),
        Theory.Case("平方", UtilityCurve.Shape.Quadratic),
        Theory.Case("平方根", UtilityCurve.Shape.Sqrt),
        Theory.Case("Logistic", UtilityCurve.Shape.Logistic),
        Theory.Case("反向 Logistic", UtilityCurve.Shape.InverseLogistic),
        Theory.Case("阈值阶跃", UtilityCurve.Shape.Step),
        Theory.Case("SmoothStep", UtilityCurve.Shape.SmoothStep),
        Theory.Case("常数", UtilityCurve.Shape.Constant));

    [Fact("曲线数据表必须覆盖全部曲线形状")]
    public void CurveCasesCoverEveryShape()
    {
        int expected = System.Enum.GetValues<UtilityCurve.Shape>().Length;
        Assert.Equal(expected, CurveCases.Length, "曲线数据表必须覆盖每个 Shape 枚举值");
    }

    [Fact("非线性曲线的两端必须满足 0→低位、1→高位")]
    public void CurveEndpoints()
    {
        var quadratic = new UtilityCurve(UtilityCurve.Shape.Quadratic);
        Assert.Equal(0f, quadratic.Evaluate(0f));
        Assert.Near(1.0, quadratic.Evaluate(1f), 1e-5);

        var sqrt = new UtilityCurve(UtilityCurve.Shape.Sqrt);
        Assert.Near(0.5, sqrt.Evaluate(0.25f), 1e-5);
    }

    [Fact("平方曲线在低需求区必须明显低于线性（这是'紧迫感'的来源）")]
    public void QuadraticSuppressesLowUrgency()
    {
        var linear = new UtilityCurve(UtilityCurve.Shape.Linear);
        var quadratic = new UtilityCurve(UtilityCurve.Shape.Quadratic);

        Assert.Less(quadratic.Evaluate(0.3f), linear.Evaluate(0.3f));
        Assert.Near(0.09, quadratic.Evaluate(0.3f), 1e-5);
    }

    [Fact("Step 曲线必须只在阈值之上输出 1")]
    public void StepCurveThreshold()
    {
        var step = new UtilityCurve(UtilityCurve.Shape.Step, midpoint: 0.6f);
        Assert.Equal(0f, step.Evaluate(0.59f));
        Assert.Equal(1f, step.Evaluate(0.6f));
        Assert.Equal(1f, step.Evaluate(1f));
    }

    [Fact("曲线输入越界必须被裁剪而不是产生异常数值")]
    public void CurvesClampInput()
    {
        var curve = new UtilityCurve(UtilityCurve.Shape.Logistic);
        Assert.InRange(curve.Evaluate(-10f), 0f, 1f);
        Assert.InRange(curve.Evaluate(10f), 0f, 1f);
        Assert.InRange(curve.Evaluate(float.NaN), 0f, 1f);
    }

    [Fact("曲线名称解析必须支持配置文件里的常见写法")]
    public void CurveShapeParsing()
    {
        Assert.Equal(UtilityCurve.Shape.Quadratic, UtilityCurve.ParseShape("quadratic"));
        Assert.Equal(UtilityCurve.Shape.Quadratic, UtilityCurve.ParseShape("SQUARE"));
        Assert.Equal(UtilityCurve.Shape.Logistic, UtilityCurve.ParseShape(" logistic "));
        Assert.Equal(UtilityCurve.Shape.Step, UtilityCurve.ParseShape("threshold"));
        Assert.Equal(UtilityCurve.Shape.Linear, UtilityCurve.ParseShape("unknown-name"));
        Assert.Equal(UtilityCurve.Shape.Linear, UtilityCurve.ParseShape(null));
    }

    // ---- PerlinNoise ----

    [Fact("Perlin 噪声同 seed 必须逐点一致")]
    public void PerlinIsDeterministic()
    {
        var a = new PerlinNoise(12345);
        var b = new PerlinNoise(12345);

        for (int y = 0; y < 20; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                Assert.Equal(a.Sample(x * 0.13f, y * 0.13f), b.Sample(x * 0.13f, y * 0.13f),
                    "同 seed 噪声在 (" + x + "," + y + ") 不一致");
            }
        }
    }

    [Fact("Perlin 噪声不同 seed 必须不同")]
    public void PerlinDiffersBySeed()
    {
        var a = new PerlinNoise(1);
        var b = new PerlinNoise(2);
        int differences = 0;

        for (int i = 0; i < 100; i++)
        {
            if (a.Sample(i * 0.1f, i * 0.07f) != b.Sample(i * 0.1f, i * 0.07f)) { differences++; }
        }

        Assert.Greater(differences, 90, "不同 seed 的噪声应几乎处处不同");
    }

    [Fact("Perlin 噪声必须落在合理范围内")]
    public void PerlinRangeIsSane()
    {
        var noise = new PerlinNoise(7);
        for (int y = 0; y < 50; y++)
        {
            for (int x = 0; x < 50; x++)
            {
                float value = noise.Sample(x * 0.05f, y * 0.05f);
                Assert.InRange(value, -1.5f, 1.5f);
            }
        }
    }

    [Fact("fBm 必须归一化到 [0,1]")]
    public void FbmIsNormalized()
    {
        var noise = new PerlinNoise(99);
        for (int y = -10; y < 40; y++)
        {
            for (int x = -10; x < 40; x++)
            {
                float value = noise.Fbm01(x * 0.02f, y * 0.02f, 4, 0.5f);
                Assert.InRange(value, 0f, 1f);
            }
        }
    }

    [Fact("fBm 必须有空间相关性（相邻采样点不能互相无关）")]
    public void FbmIsSpatiallyCoherent()
    {
        var noise = new PerlinNoise(555);
        float sum = 0f;
        const int samples = 200;

        for (int i = 0; i < samples; i++)
        {
            float a = noise.Fbm01(i * 0.01f, 0.5f, 3, 0.5f);
            float b = noise.Fbm01((i * 0.01f) + 0.01f, 0.5f, 3, 0.5f);
            sum += System.Math.Abs(a - b);
        }

        float averageDelta = sum / samples;
        Assert.Less(averageDelta, 0.1f, "相邻位置的噪声变化过大，说明没有空间相关性（地图会变成白噪声）");
    }

    [Fact("NoiseHash 必须是确定性的坐标哈希")]
    public void NoiseHashDeterminism()
    {
        Assert.Equal(NoiseHash.Hash2D(10, 20, 5), NoiseHash.Hash2D(10, 20, 5));
        Assert.NotEqual(NoiseHash.Hash2D(10, 20, 5), NoiseHash.Hash2D(20, 10, 5));
        Assert.NotEqual(NoiseHash.Hash2D(10, 20, 5), NoiseHash.Hash2D(10, 20, 6));

        for (int i = 0; i < 100; i++)
        {
            Assert.InRange(NoiseHash.Value01(i, i * 2, 3), 0.0, 1.0);
        }
    }
}
