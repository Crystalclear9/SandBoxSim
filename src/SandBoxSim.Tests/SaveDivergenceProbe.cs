using SandBoxSim.Core;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Save;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

/// <summary>
/// 存档确定性诊断工具（可复用，不是一次性的）。
///
/// # 为什么它值得保留而不是用完就删
///
/// 定位"读档后世界悄悄变成另一个"这类问题，靠的不是灵光一闪，而是一套**固定流程**：
///
///   1. 复刻出问题的规模，逐步 tick 找出**第一个分叉的 tick**；
///   2. 打印该 tick 的**分段摘要差异** —— 把"一个大数字对不上"缩小到"是哪一段"；
///   3. 若差异在实体段，逐个字段对比；若在 tiles 段，**逐个 tile** 对比；
///   4. 比对 8 条随机流的状态 —— 用来区分"漏了状态"与"随机源被扰动"。
///
/// 这套流程在 M4b 里定位出了**五个**"不进摘要但影响未来行为"的字段；
/// 每定位一个，分叉点就往后推。所以这个工具是**收敛过程的量具**，
/// 删掉它等于下次遇到同类问题要从头再写一遍。
///
/// 用法（**默认跳过**，因为它要跑 20 天 + 逐 tick 比对，约 30 秒）：
/// <code>
/// $env:SBOX_SIM_PROBE = '1'
/// dotnet src/SandBoxSim.Tests/bin/Debug/net8.0/SandBoxSim.Tests.dll --filter SaveDivergenceProbe
/// </code>
///
/// 为什么不直接用 <see cref="SkipAttribute"/>：被 Skip 的用例连 `--filter` 也跑不起来，
/// 那这个工具就等于被删掉了。用环境变量开关既保持"默认套件快"，
/// 又保持"需要时一条命令就能跑"。
/// </summary>
public sealed class SaveDivergenceProbe
{
    /// <summary>复刻出问题的那一组规模（与 CLI 默认一致）。</summary>
    private const int MapSize = 100;
    private const int Seed = 555;
    private const int Agents = 30;
    private const int HalfDays = 20;
    private const int TicksPerDay = 1440;

    /// <summary>被追踪的格（扁平下标）。由探针第一次运行时的"首个不同格"确定。</summary>
    private const int ProbeIndex = 2;   // (2,0)

    private static long traceFrom = -1;

    private static int CountDifferingTiles(Simulation a, Simulation b)
    {
        Tile[] ta = a.World.Tiles;
        Tile[] tb = b.World.Tiles;
        int count = 0;
        for (int i = 0; i < ta.Length; i++)
        {
            if (ta[i].Resource.Amount != tb[i].Resource.Amount) { count++; }
        }
        return count;
    }

    [Fact("诊断：读档续跑的第一个分叉 tick（100×100 / 20 天 + 20 天，需 SBOX_SIM_PROBE=1）")]
    public void Probe()
    {
        if (System.Environment.GetEnvironmentVariable("SBOX_SIM_PROBE") != "1")
        {
            System.Console.WriteLine("  [探针] 已跳过（设 SBOX_SIM_PROBE=1 可运行；它约需 30 秒）。");
            System.Console.WriteLine("  [探针] 常规回归由 SaveLoadTests.TilesRoundTripBitExactUnderDifferentSeed 把关（秒级）。");
            return;
        }

        var directConfig = MakeConfig();
        var direct = new Simulation(directConfig, MapSize, MapSize, Seed);
        direct.InterveneSpawnHumans(50, 50, Agents, 8);
        direct.Tick(TicksPerDay * HalfDays);

        var sourceConfig = MakeConfig();
        var source = new Simulation(sourceConfig, MapSize, MapSize, Seed);
        source.InterveneSpawnHumans(50, 50, Agents, 8);
        source.Tick(TicksPerDay * HalfDays);

        Assert.Equal(direct.StateDigestString(), source.StateDigestString());

        string json = source.SaveToText();

        // 刻意用一个**不同**的构造 seed：复刻 CLI 里"没传 --seed"的情形，
        // 这一类 bug 只在输入与期望值不同的时候才暴露（M4b 的教训）。
        var targetConfig = MakeConfig();
        var restored = Simulation.CreateForRestore(targetConfig, MapSize, MapSize, 839102);

        SaveFile.LoadResult load = restored.LoadFromText(json);
        Assert.True(load.Success, load.Error);

        System.Console.WriteLine("  [探针] 读档自校验：" + (load.DigestMatches ? "一致" : "不一致 -> " + load.SegmentDifference));
        System.Console.WriteLine("  [探针] 读档点分段：" + load.ActualSegments);

        // ---- 关键分辨：漂移是"存档序列化"造成的，还是"续跑过程"造成的？----
        // 在**推进任何 tick 之前**逐格做精确（非量化）比对。
        // 这一步决定后面往哪个方向查：前者是 JSON 精度问题，后者是漏了状态。
        DumpExactTileDeltaAtLoad(direct, restored);

        long firstDiff = -1;
        for (int i = 0; i < TicksPerDay * HalfDays; i++)
        {
            direct.Tick(1);
            restored.Tick(1);

            // 逐 tick 记录那个"首个不同格"的资源量。
            // 目的：看清它是**突然**变化（一次写入造成）还是**逐渐**拉开（累积放大）。
            // 这两者的根因完全不同 —— 前者是"某个系统多跑了一次"，后者才是"数值精度/顺序"。
            float va = direct.World.Tiles[ProbeIndex].Resource.Amount;
            float vb = restored.World.Tiles[ProbeIndex].Resource.Amount;
            if (va != vb && traceFrom < 0) { traceFrom = i + 1; }

            if (i >= 50 && i < 62)
            {
                System.Console.WriteLine("  [探针] tick+" + (i + 1)
                    + " 格(" + (ProbeIndex % MapSize) + "," + (ProbeIndex / MapSize) + ")"
                    + " direct=" + va.ToString("R") + " restored=" + vb.ToString("R")
                    + (va == vb ? " 同" : " **不同**"));
            }

            if (direct.StateDigestString() != restored.StateDigestString())
            {
                firstDiff = i + 1;
                break;
            }
        }

        System.Console.WriteLine("  [探针] 该格开始不同的 tick+" + traceFrom);
        System.Console.WriteLine("  [探针] 最终不同的格子总数："
            + CountDifferingTiles(direct, restored) + " / " + direct.World.Tiles.Length);

        System.Console.WriteLine("  [探针] 第一个分叉 tick（相对读档点）=" + firstDiff
            + "（绝对 tick " + (TicksPerDay * HalfDays + firstDiff) + "）");

        if (firstDiff < 0)
        {
            System.Console.WriteLine("  [探针] 20 天内完全一致 —— 该规模下已收敛。");
            return;
        }

        System.Console.WriteLine("  [探针] 分段差异：" + StateHash.FirstSegmentDifference(
            StateHash.DescribeSegments(direct), StateHash.DescribeSegments(restored)));

        DumpRngComparison(direct, restored);
        DumpFirstTileDifference(direct, restored);
        DumpCounts(direct, restored);
    }

    /// <summary>
    /// 读档瞬间的**精确**逐格比对（不做摘要量化）。
    ///
    /// 为什么必须单独做这一步：状态摘要把资源量量化到 0.01（`(int)(Amount*100f)`），
    /// 于是一个 0.004 的差异在摘要里**完全看不见** —— 它是被整除截断吞掉的。
    /// 不先做这次精确比对，就会把"序列化丢精度"误判成"漏了状态"，
    /// 然后去翻一堆本来正确的字段。
    /// </summary>
    private static void DumpExactTileDeltaAtLoad(Simulation a, Simulation b)
    {
        Tile[] ta = a.World.Tiles;
        Tile[] tb = b.World.Tiles;

        int amountDiff = 0;
        int moistureDiff = 0;
        int vegetationDiff = 0;
        int capacityDiff = 0;
        float maxAmountDelta = 0f;
        int firstIndex = -1;

        for (int i = 0; i < ta.Length; i++)
        {
            float da = ta[i].Resource.Amount - tb[i].Resource.Amount;
            float dc = ta[i].Resource.Capacity - tb[i].Resource.Capacity;
            float dm = ta[i].Moisture - tb[i].Moisture;
            float dv = ta[i].Vegetation - tb[i].Vegetation;

            if (da != 0f)
            {
                amountDiff++;
                if (System.Math.Abs(da) > System.Math.Abs(maxAmountDelta)) { maxAmountDelta = da; }
                if (firstIndex < 0) { firstIndex = i; }
            }
            if (dc != 0f) { capacityDiff++; }
            if (dm != 0f) { moistureDiff++; }
            if (dv != 0f) { vegetationDiff++; }
        }

        System.Console.WriteLine("  [探针] 读档瞬间精确比对：资源量差异 " + amountDiff + " 格"
            + "（最大 " + maxAmountDelta.ToString("R") + "）"
            + " | 容量 " + capacityDiff + " | 湿度 " + moistureDiff + " | 植被 " + vegetationDiff);

        if (firstIndex >= 0)
        {
            int tx = firstIndex % a.World.Width;
            int ty = firstIndex / a.World.Width;
            System.Console.WriteLine("  [探针]   首格 (" + tx + "," + ty + ") 资源 "
                + ta[firstIndex].Resource.Kind
                + " direct=" + ta[firstIndex].Resource.Amount.ToString("R")
                + " restored=" + tb[firstIndex].Resource.Amount.ToString("R"));
        }
    }

    private static SimConfig MakeConfig()
    {
        var config = new SimConfig();
        config.World.Width = MapSize;
        config.World.Height = MapSize;
        return config;
    }

    /// <summary>8 条随机流逐一比对 —— 用来区分"漏了状态"与"随机源被扰动"。</summary>
    private static void DumpRngComparison(Simulation a, Simulation b)
    {
        ulong[][] ra = a.Random.ExportState();
        ulong[][] rb = b.Random.ExportState();

        for (int s = 0; s < ra.Length && s < rb.Length; s++)
        {
            bool same = ra[s].Length == rb[s].Length;
            if (same)
            {
                for (int k = 0; k < ra[s].Length; k++)
                {
                    if (ra[s][k] != rb[s][k]) { same = false; break; }
                }
            }
            if (!same)
            {
                System.Console.WriteLine("  [探针] 随机流不同：" + (RngStream)s);
            }
        }
    }

    /// <summary>找出第一个不同的 Tile，并打印它的**全部**字段。</summary>
    private static void DumpFirstTileDifference(Simulation a, Simulation b)
    {
        Tile[] ta = a.World.Tiles;
        Tile[] tb = b.World.Tiles;

        for (int i = 0; i < ta.Length; i++)
        {
            ref readonly Tile x = ref ta[i];
            ref readonly Tile y = ref tb[i];
            bool same = x.Terrain == y.Terrain
                && x.Fire == y.Fire
                && x.Resource.Kind == y.Resource.Kind
                && x.Resource.Amount == y.Resource.Amount
                && x.Resource.Capacity == y.Resource.Capacity
                && x.Moisture == y.Moisture
                && x.Temperature == y.Temperature
                && x.Fertility == y.Fertility
                && x.Vegetation == y.Vegetation
                && x.BuildingId == y.BuildingId;
            if (same) { continue; }

            int tx = i % a.World.Width;
            int ty = i / a.World.Width;
            System.Console.WriteLine("  [探针] 首个不同的格子 (" + tx + "," + ty + ")");
            System.Console.WriteLine("  [探针]   direct   地形" + x.Terrain + " 火" + x.Fire
                + " 资源" + x.Resource.Kind + " 量" + x.Resource.Amount.ToString("R")
                + " 容" + x.Resource.Capacity.ToString("R")
                + " 湿" + x.Moisture.ToString("R") + " 温" + x.Temperature.ToString("R")
                + " 肥" + x.Fertility.ToString("R") + " 植" + x.Vegetation.ToString("R")
                + " 建筑" + x.BuildingId);
            System.Console.WriteLine("  [探针]   restored 地形" + y.Terrain + " 火" + y.Fire
                + " 资源" + y.Resource.Kind + " 量" + y.Resource.Amount.ToString("R")
                + " 容" + y.Resource.Capacity.ToString("R")
                + " 湿" + y.Moisture.ToString("R") + " 温" + y.Temperature.ToString("R")
                + " 肥" + y.Fertility.ToString("R") + " 植" + y.Vegetation.ToString("R")
                + " 建筑" + y.BuildingId);
            return;
        }

        System.Console.WriteLine("  [探针] 没有任何 tile 不同（差异在实体或统计段）");
    }

    private static void DumpCounts(Simulation a, Simulation b)
    {
        System.Console.WriteLine("  [探针] 人口 " + a.Agents.LiveCount + " vs " + b.Agents.LiveCount
            + " | 动物 " + a.Wildlife.LiveCount + " vs " + b.Wildlife.LiveCount
            + " | 建筑 " + a.Buildings.LiveCount + " vs " + b.Buildings.LiveCount
            + " | 堆 " + a.GroundStocks.LiveCount + " vs " + b.GroundStocks.LiveCount);
        System.Console.WriteLine("  [探针] 统计 出生" + a.Stats.TotalBirths + "/" + b.Stats.TotalBirths
            + " 死亡" + a.Stats.TotalDeaths + "/" + b.Stats.TotalDeaths
            + " 迁移" + a.Stats.TotalMigrations + "/" + b.Stats.TotalMigrations
            + " 枯竭" + a.ResourceSystem.DepletionEvents + "/" + b.ResourceSystem.DepletionEvents);
        System.Console.WriteLine("  [探针] 天气 direct " + a.World.Weather.Kind + "/" + a.World.Weather.DurationHours
            + "/" + a.World.Weather.HoursUntilChange
            + " vs restored " + b.World.Weather.Kind + "/" + b.World.Weather.DurationHours
            + "/" + b.World.Weather.HoursUntilChange);
        System.Console.WriteLine("  [探针] 脏块 direct " + a.World.Chunks.DirtyCount
            + " vs restored " + b.World.Chunks.DirtyCount);
    }
}
