using SandBoxSim.Core.Foundation;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

/// <summary>
/// 确定性随机源测试。
///
/// 这是整个项目最重要的测试组：如果随机源不确定，后面所有"同 seed 可复现"的承诺都是空话。
/// </summary>
public sealed class RandomTests
{
    [Fact("同 seed 必须产出完全相同的前 N 个值")]
    public void SameSeedSameSequence()
    {
        var a = new DeterministicRandom(12345);
        var b = new DeterministicRandom(12345);

        for (int i = 0; i < 200; i++)
        {
            Assert.Equal(a.NextULong(), b.NextULong(), "第 " + i + " 个值不同");
        }
    }

    [Fact("不同 seed 必须产出不同序列")]
    public void DifferentSeedDifferentSequence()
    {
        var a = new DeterministicRandom(1);
        var b = new DeterministicRandom(2);

        int equalCount = 0;
        for (int i = 0; i < 50; i++)
        {
            if (a.NextULong() == b.NextULong()) { equalCount++; }
        }

        // 64 位随机数碰撞概率可忽略，因此这里要求零碰撞。
        Assert.Equal(0, equalCount);
    }

    [Fact("导出/导入状态后必须能精确续跑")]
    public void StateRoundTripResumesExactly()
    {
        var original = new DeterministicRandom(987654321);
        for (int i = 0; i < 37; i++) { original.NextULong(); }

        ulong[] state = original.ExportState();

        ulong[] expected = new ulong[20];
        for (int i = 0; i < expected.Length; i++) { expected[i] = original.NextULong(); }

        var restored = new DeterministicRandom(0);
        restored.ImportState(state);

        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], restored.NextULong(), "续跑第 " + i + " 个值不同");
        }
    }

    [Fact("种子 0 不能退化成全零状态")]
    public void ZeroSeedIsNotDegenerate()
    {
        var rng = new DeterministicRandom(0);
        var seen = new System.Collections.Generic.HashSet<ulong>();
        for (int i = 0; i < 100; i++) { seen.Add(rng.NextULong()); }

        Assert.Greater(seen.Count, 95, "种子 0 的随机数重复率过高，疑似退化");
    }

    [Fact("NextInt 必须落在 [0, bound) 内")]
    public void NextIntRespectsBound()
    {
        var rng = new DeterministicRandom(42);
        for (int i = 0; i < 5000; i++)
        {
            int value = rng.NextInt(7);
            Assert.InRange(value, 0, 6);
        }
    }

    [Fact("NextInt 的分布必须大致均匀")]
    public void NextIntIsRoughlyUniform()
    {
        var rng = new DeterministicRandom(777);
        var buckets = new int[10];
        const int draws = 100000;

        for (int i = 0; i < draws; i++) { buckets[rng.NextInt(10)]++; }

        int expected = draws / 10;
        int tolerance = expected / 5;   // ±20%
        for (int i = 0; i < buckets.Length; i++)
        {
            Assert.InRange(buckets[i], expected - tolerance, expected + tolerance, "桶 " + i + " 分布异常");
        }
    }

    [Fact("NextDouble 必须落在 [0,1) 内")]
    public void NextDoubleInUnitInterval()
    {
        var rng = new DeterministicRandom(2024);
        for (int i = 0; i < 10000; i++)
        {
            double value = rng.NextDouble();
            Assert.InRange(value, 0.0, 1.0);
            Assert.True(value < 1.0, "NextDouble 不应返回 1.0");
        }
    }

    [Fact("Chance(0) 恒假，Chance(1) 恒真")]
    public void ChanceEdgeCases()
    {
        var rng = new DeterministicRandom(5);
        for (int i = 0; i < 1000; i++)
        {
            Assert.False(rng.Chance(0.0));
            Assert.True(rng.Chance(1.0));
        }
    }

    [Fact("Chance 的长期频率必须接近给定概率")]
    public void ChanceFrequencyMatchesProbability()
    {
        var rng = new DeterministicRandom(31337);
        const int draws = 100000;
        int hits = 0;
        for (int i = 0; i < draws; i++)
        {
            if (rng.Chance(0.25)) { hits++; }
        }

        double frequency = hits / (double)draws;
        Assert.Near(0.25, frequency, 0.01, "Chance 频率偏差过大");
    }

    [Fact("SampleWeightedIndex 必须按权重分配")]
    public void WeightedSamplingFollowsWeights()
    {
        var rng = new DeterministicRandom(11);
        double[] weights = { 1.0, 3.0, 0.0 };
        var counts = new int[3];

        for (int i = 0; i < 40000; i++) { counts[rng.SampleWeightedIndex(weights, 3)]++; }

        Assert.Equal(0, counts[2], "权重为 0 的项不应被抽中");
        Assert.Greater(counts[1], counts[0], "权重 3 的项应明显多于权重 1 的项");

        double ratio = counts[1] / (double)System.Math.Max(1, counts[0]);
        Assert.Near(3.0, ratio, 0.2, "抽样比例应接近权重比");
    }

    [Fact("SampleWeightedIndex 在总权重为 0 时返回 -1")]
    public void WeightedSamplingWithZeroTotal()
    {
        var rng = new DeterministicRandom(12);
        double[] weights = { 0.0, 0.0 };
        Assert.Equal(-1, rng.SampleWeightedIndex(weights, 2));
    }

    [Fact("Shuffle 必须是确定性的且保持元素集合不变")]
    public void ShuffleIsDeterministicAndLossless()
    {
        int[] a = new int[50];
        int[] b = new int[50];
        for (int i = 0; i < 50; i++) { a[i] = i; b[i] = i; }

        new DeterministicRandom(99).Shuffle(a, a.Length);
        new DeterministicRandom(99).Shuffle(b, b.Length);

        for (int i = 0; i < a.Length; i++)
        {
            Assert.Equal(a[i], b[i], "同 seed 的洗牌结果必须一致");
        }

        var seen = new System.Collections.Generic.HashSet<int>(a);
        Assert.Equal(50, seen.Count, "洗牌后元素集合必须完整");
    }

    [Fact("SimRandom 的每个流必须相互独立")]
    public void StreamsAreIndependent()
    {
        var sim = new SimRandom(424242);
        ulong first = sim.Get(RngStream.WorldGen).NextULong();
        ulong second = sim.Get(RngStream.Weather).NextULong();
        ulong third = sim.Get(RngStream.Agents).NextULong();

        Assert.NotEqual(first, second);
        Assert.NotEqual(second, third);
        Assert.NotEqual(first, third);
    }

    [Fact("抽取一个流不会影响其他流（分流的核心保证）")]
    public void DrawingFromOneStreamDoesNotDisturbOthers()
    {
        var a = new SimRandom(1234);
        var b = new SimRandom(1234);

        // a 在 WorldGen 流上多消耗 100 个随机数
        for (int i = 0; i < 100; i++) { a.Get(RngStream.WorldGen).NextULong(); }

        // 两个实例的 Weather 流第一个值必须相同
        Assert.Equal(b.Get(RngStream.Weather).NextULong(), a.Get(RngStream.Weather).NextULong(),
            "分流失败：一个流的消耗影响到了另一个流");
    }

    [Fact("SimRandom 状态导出/导入必须完整")]
    public void SimRandomStateRoundTrip()
    {
        var original = new SimRandom(555);
        for (int i = 0; i < 10; i++)
        {
            original.Get(RngStream.Agents).NextULong();
            original.Get(RngStream.Weather).NextULong();
        }

        ulong[][] state = original.ExportState();

        ulong[] expectedAgents = new ulong[5];
        ulong[] expectedWeather = new ulong[5];
        for (int i = 0; i < 5; i++)
        {
            expectedAgents[i] = original.Get(RngStream.Agents).NextULong();
            expectedWeather[i] = original.Get(RngStream.Weather).NextULong();
        }

        var restored = new SimRandom(0);
        restored.ImportState(state);

        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(expectedAgents[i], restored.Get(RngStream.Agents).NextULong());
            Assert.Equal(expectedWeather[i], restored.Get(RngStream.Weather).NextULong());
        }
    }
}
