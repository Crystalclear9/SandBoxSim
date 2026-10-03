using System.Diagnostics;
using SandBoxSim.ConsoleApp.Cli;
using SandBoxSim.ConsoleApp.Png;
using SandBoxSim.ConsoleApp.Render;
using SandBoxSim.ConsoleApp.Reporting;
using SandBoxSim.ConsoleApp.Tui;
using SandBoxSim.Core;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.ConsoleApp;

/// <summary>
/// 程序入口与运行模式分发（第 15 节提交计划的 M0 部分）。
///
/// 模式：
///   play     交互 TUI（默认）
///   headless 长跑并产出报告（观察证据）
///   digest   同 seed 跑两遍比对状态摘要（确定性验收）
///   batch    多 seed 批量长跑（鲁棒性与涌现性验收）
///   snapshot 长跑 + 若干天数导出世界 PNG（可归档的视觉证据）
/// </summary>
public static class Program
{
    public static int Main(string[] rawArgs)
    {
        var args = new Args(rawArgs);

        if (args.Flag("--help"))
        {
            System.Console.WriteLine(Args.HelpText());
            return 0;
        }

        if (args.HasErrors)
        {
            System.Console.Error.WriteLine("参数错误：");
            for (int i = 0; i < args.Errors.Count; i++)
            {
                System.Console.Error.WriteLine("  - " + args.Errors[i]);
            }
            System.Console.Error.WriteLine();
            System.Console.Error.WriteLine(Args.HelpText());
            return 2;
        }

        try
        {
            if (args.Flag("--digest")) { return RunDigest(args); }
            if (args.Flag("--batch")) { return RunBatch(args); }
            if (args.Flag("--headless") || args.Flag("--snapshot")) { return RunHeadless(args); }
            return RunInteractive(args);
        }
        catch (System.Exception ex)
        {
            System.Console.Error.WriteLine("运行失败：" + ex.GetType().Name + ": " + ex.Message);
            System.Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    // ---------------------------------------------------------------------
    // 配置装载
    // ---------------------------------------------------------------------

    /// <summary>默认配置文件路径：优先仓库内的 config/，其次可执行文件旁的 config/。</summary>
    private static string ResolveDefaultConfigPath()
    {
        string cwdCandidate = System.IO.Path.Combine(System.Environment.CurrentDirectory, "config", "sim.default.json");
        if (System.IO.File.Exists(cwdCandidate)) { return cwdCandidate; }

        string exeDir = System.AppContext.BaseDirectory;
        string exeCandidate = System.IO.Path.Combine(exeDir, "config", "sim.default.json");
        if (System.IO.File.Exists(exeCandidate)) { return exeCandidate; }

        return cwdCandidate;
    }

    /// <summary>装载配置并把命令行覆盖项应用上去。</summary>
    private static SimConfig LoadConfig(Args args, out string configPath, out string[] warnings)
    {
        configPath = args.String("--config", ResolveDefaultConfigPath());
        ConfigLoadResult<SimConfig> result = ConfigLoader.Load<SimConfig>(configPath);

        if (!result.Ok)
        {
            throw new System.InvalidOperationException("配置加载失败：" + result.Error);
        }

        SimConfig config = result.Value;

        // 命令行覆盖（第 96.7 条：关键数值必须可实时调整）
        int width = args.Int("--width", config.World.Width);
        int height = args.Int("--height", config.World.Height);
        config.World.Width = width > 0 ? width : config.World.Width;
        config.World.Height = height > 0 ? height : config.World.Height;

        if (args.Flag("--no-invariants")) { config.Debug.AssertInvariants = false; }
        if (args.Flag("--invariants")) { config.Debug.AssertInvariants = true; }

        int seed = args.Int("--seed", config.WorldGen.Seed);
        config.WorldGen.Seed = seed;

        warnings = new string[result.Warnings.Count];
        for (int i = 0; i < result.Warnings.Count; i++) { warnings[i] = result.Warnings[i]; }
        return config;
    }

    private static Simulation CreateSimulation(Args args, out SimConfig config, out string configPath, out string[] configWarnings)
    {
        config = LoadConfig(args, out configPath, out configWarnings);
        int seed = args.Int("--seed", config.WorldGen.Seed);
        return new Simulation(config, config.World.Width, config.World.Height, seed);
    }

    private static MapOverlay ParseOverlay(Args args)
    {
        string name = args.String("--overlay", "none");
        if (string.IsNullOrEmpty(name)) { return MapOverlay.None; }

        if (System.Enum.TryParse(name, ignoreCase: true, out MapOverlay overlay))
        {
            return overlay;
        }

        // 容忍 firerisk / fire-risk 之类写法
        string normalized = name.Replace("-", string.Empty).Replace("_", string.Empty);
        foreach (MapOverlay candidate in System.Enum.GetValues<MapOverlay>())
        {
            if (string.Equals(candidate.ToString(), normalized, System.StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }
        return MapOverlay.None;
    }

    // ---------------------------------------------------------------------
    // 模式：交互
    // ---------------------------------------------------------------------

    private static int RunInteractive(Args args)
    {
        Simulation sim = CreateSimulation(args, out SimConfig _, out string configPath, out string[] warnings);

        using var terminal = new Terminal(args.Flag("--no-color"), args.Flag("--no-alternate-screen"));

        System.Console.WriteLine("SandBoxSim 启动中 ...");
        System.Console.WriteLine("  配置文件：" + configPath);
        for (int i = 0; i < warnings.Length; i++)
        {
            System.Console.WriteLine("  配置提示：" + warnings[i]);
        }
        if (sim.GenerationInfo != null)
        {
            System.Console.WriteLine("  " + sim.GenerationInfo);
        }
        System.Console.WriteLine("  终端颜色： " + terminal.Profile);
        System.Console.WriteLine("  按 H 查看操作帮助，按 Q 退出。");
        System.Console.WriteLine();

        var loop = new GameLoop(sim, terminal, ParseOverlay(args));
        int code = loop.Run();

        // 退出后回到普通屏幕，输出一段"这次看了什么"，避免玩家一退出就丢失上下文。
        System.Console.WriteLine();
        System.Console.WriteLine("=== 本次观察结束 ===");
        System.Console.WriteLine("  seed      : " + sim.World.Seed);
        System.Console.WriteLine("  游戏天数  : " + sim.World.Calendar.Day);
        System.Console.WriteLine("  推进 tick : " + sim.TickCount);
        System.Console.WriteLine("  状态摘要  : " + sim.StateDigestString());
        System.Console.WriteLine("  复现命令  : .\\tools\\run.ps1 -Seed " + sim.World.Seed
            + " -Width " + sim.World.Width + " -Height " + sim.World.Height);
        System.Console.WriteLine();
        return code;
    }

    // ---------------------------------------------------------------------
    // 模式：headless / snapshot
    // ---------------------------------------------------------------------

    private static int RunHeadless(Args args)
    {
        Simulation sim = CreateSimulation(args, out SimConfig config, out string configPath, out string[] warnings);

        long ticks = ResolveTickCount(args, config);
        bool wantSnapshots = args.Flag("--snapshot");
        int snapshotDays = args.Int("--snapshot-days", 0);

        string outRoot = args.String("--out", System.IO.Path.Combine(System.Environment.CurrentDirectory, "runs"));
        string runId = RunReport.BuildRunId(sim.World.Seed, sim.World.Calendar.Day + (int)(ticks / sim.World.Calendar.TicksPerDay), ticks);
        string runDir = RunReport.EnsureDirectory(System.IO.Path.Combine(outRoot, runId));

        for (int i = 0; i < warnings.Length; i++)
        {
            System.Console.WriteLine("配置提示：" + warnings[i]);
        }
        System.Console.WriteLine("headless 运行：seed=" + sim.World.Seed
            + " 尺寸=" + sim.World.Width + "×" + sim.World.Height
            + " tick=" + ticks + "（≈" + ticks / sim.World.Calendar.TicksPerDay + " 天）");

        var stopwatch = Stopwatch.StartNew();

        // 快照节点：0 表示起点，随后按 snapshotDays 间隔，最后一天。
        // 用升序数组 + 游标推进，避免"批量 tick 跨过多个节点"时漏拍或重复拍。
        long[] snapshotTicks = System.Array.Empty<long>();
        if (wantSnapshots)
        {
            var targets = new System.Collections.Generic.SortedSet<long> { 0 };
            if (snapshotDays > 0)
            {
                long interval = (long)snapshotDays * sim.World.Calendar.TicksPerDay;
                for (long t = interval; t < ticks; t += interval) { targets.Add(t); }
            }
            targets.Add(ticks);

            snapshotTicks = new long[targets.Count];
            targets.CopyTo(snapshotTicks);
        }

        int snapshotCursor = 0;
        long executed = 0;
        var snapshotFiles = new System.Collections.Generic.List<string>();

        // 起点快照（tick 0）
        while (snapshotCursor < snapshotTicks.Length && snapshotTicks[snapshotCursor] <= executed)
        {
            snapshotFiles.Add(WriteSnapshot(sim, runDir, snapshotTicks[snapshotCursor]));
            snapshotCursor++;
        }

        while (executed < ticks)
        {
            long batch = System.Math.Min(ticks - executed, 1000);
            sim.Tick((int)batch);
            executed += batch;

            // 每 10% 输出一次进度（这样长跑不会看起来像卡死）
            if (args.Flag("--verbose") && executed % 20000 == 0)
            {
                System.Console.WriteLine("  ... tick " + executed + "/" + ticks
                    + " pop=" + sim.PopulationCount
                    + " food=" + sim.World.TotalResource(ResourceKind.Food).ToString("0", System.Globalization.CultureInfo.InvariantCulture));
            }

            while (snapshotCursor < snapshotTicks.Length && snapshotTicks[snapshotCursor] <= executed)
            {
                snapshotFiles.Add(WriteSnapshot(sim, runDir, snapshotTicks[snapshotCursor]));
                snapshotCursor++;
            }
        }

        stopwatch.Stop();

        // ---- 产出 ----
        string statsPath = System.IO.Path.Combine(runDir, "stats.json");
        RunReport.WriteTextFile(statsPath, RunReport.WriteStatsJson(sim, runId, ticks));

        string eventsPath = System.IO.Path.Combine(runDir, "events.md");
        RunReport.WriteTextFile(eventsPath, RunReport.BuildEventsMarkdown(sim));

        string digestPath = System.IO.Path.Combine(runDir, "digest.txt");
        RunReport.WriteTextFile(digestPath, "seed=" + sim.World.Seed + "\nticks=" + ticks
            + "\ndigest=" + sim.StateDigestString() + "\n");

        string repro = ".\\tools\\run.ps1 -Mode " + (wantSnapshots ? "snapshot" : "headless")
            + " -Seed " + sim.World.Seed + " -Ticks " + ticks
            + (sim.World.Width != 100 ? " -Width " + sim.World.Width : string.Empty)
            + (sim.World.Height != 100 ? " -Height " + sim.World.Height : string.Empty);

        string reportPath = System.IO.Path.Combine(runDir, "report.md");
        RunReport.WriteTextFile(reportPath, RunReport.BuildMarkdownReport(sim, runId, ticks, repro));

        // 生效参数快照：报告里必须能查到"这次跑的是什么参数"。
        string configSnapshotPath = System.IO.Path.Combine(runDir, "config.effective.json");
        RunReport.WriteTextFile(configSnapshotPath, ConfigLoader.ToJson(config));

        System.Console.WriteLine();
        System.Console.WriteLine("=== 运行完成 ===");
        System.Console.WriteLine("  用时      : " + stopwatch.Elapsed.TotalSeconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " 秒");
        System.Console.WriteLine("  tick 速度 : " + (ticks / System.Math.Max(0.001, stopwatch.Elapsed.TotalSeconds)).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " tick/秒");
        System.Console.WriteLine("  游戏天数  : " + sim.World.Calendar.Day);
        System.Console.WriteLine("  状态摘要  : " + sim.StateDigestString());
        System.Console.WriteLine("  事件记录  : " + sim.Events.TotalRecorded);
        System.Console.WriteLine("  产出目录  : " + runDir);
        System.Console.WriteLine("    - " + System.IO.Path.GetFileName(reportPath));
        System.Console.WriteLine("    - " + System.IO.Path.GetFileName(statsPath));
        System.Console.WriteLine("    - " + System.IO.Path.GetFileName(eventsPath));
        for (int i = 0; i < snapshotFiles.Count; i++)
        {
            System.Console.WriteLine("    - " + System.IO.Path.GetFileName(snapshotFiles[i]));
        }

        return 0;
    }

    /// <summary>
    /// 导出一张世界快照。
    /// targetTick 是**目标快照 tick**，而不是"当前 tick"：长跑按 1000 tick 一批推进，
    /// 实际落盘时刻会略微超过目标点；若用当前 tick 命名，文件名里的天数会与快照序列
    /// 的预期（day 1 / 3 / 5…）错位，报告与文件对不上号。
    /// </summary>
    private static string WriteSnapshot(Simulation sim, string runDir, long targetTick)
    {
        int day = (int)(targetTick / sim.World.Calendar.TicksPerDay) + 1;
        string fileName = "World_" + sim.World.Seed + "_day" + day.ToString("000", System.Globalization.CultureInfo.InvariantCulture) + ".png";
        string path = System.IO.Path.Combine(runDir, fileName);

        var options = new WorldSnapshot.Options
        {
            Width = 800,
            HeaderHeight = 12,
            DrawGrid = false,
            LightLevel = 1f,
        };

        PngImage image = WorldSnapshot.Render(sim, options);
        int bytes = PngWriter.WriteFile(path, image);
        System.Console.WriteLine("  快照 day " + day + " → " + fileName + "（" + (bytes / 1024) + " KB）");
        return path;
    }

    private static long ResolveTickCount(Args args, SimConfig config)
    {
        int ticks = args.Int("--ticks", 0);
        if (ticks > 0) { return ticks; }

        int days = args.Int("--days", 100);
        if (days <= 0) { days = 1; }
        return (long)days * config.Clock.TicksPerDay;
    }

    // ---------------------------------------------------------------------
    // 模式：digest（确定性验收）
    // ---------------------------------------------------------------------

    private static int RunDigest(Args args)
    {
        long ticks = ResolveTickCount(args, LoadConfig(args, out string _, out string[] _));

        System.Console.WriteLine("确定性校验：同 seed 连跑两遍，比对状态摘要");
        System.Console.WriteLine("  tick 数 = " + ticks);

        var first = RunOnceForDigest(args, ticks, 1);
        var second = RunOnceForDigest(args, ticks, 2);

        System.Console.WriteLine("  第 1 次：digest=" + first.Digest + "  用时=" + first.Seconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "s");
        System.Console.WriteLine("  第 2 次：digest=" + second.Digest + "  用时=" + second.Seconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "s");

        if (first.Digest != second.Digest)
        {
            System.Console.Error.WriteLine("确定性校验失败：两次运行的状态摘要不一致");
            System.Console.Error.WriteLine("  这通常意味着：某处用了 System.Random 或依赖了字典迭代顺序/真实时钟。");
            return 1;
        }

        // 不同的 seed 必须产出不同摘要，否则说明种子根本没生效。
        int otherSeed = args.Int("--seed", 839102) + 1;
        var alt = RunOnceForDigestWithSeed(args, ticks, otherSeed);
        if (alt.Digest == first.Digest)
        {
            System.Console.Error.WriteLine("确定性校验失败：不同 seed 得到相同摘要（种子未生效）");
            return 1;
        }

        System.Console.WriteLine("  对照 seed=" + otherSeed + "：digest=" + alt.Digest + "（与基准不同，符合预期）");
        System.Console.WriteLine("确定性校验通过");
        return 0;
    }

    private readonly struct DigestResult
    {
        public readonly string Digest;
        public readonly double Seconds;

        public DigestResult(string digest, double seconds)
        {
            Digest = digest;
            Seconds = seconds;
        }
    }

    private static DigestResult RunOnceForDigest(Args args, long ticks, int pass)
    {
        int seed = args.Int("--seed", 839102);
        return RunOnceForDigestWithSeed(args, ticks, seed, pass);
    }

    private static DigestResult RunOnceForDigestWithSeed(Args args, long ticks, int seed, int pass = 0)
    {
        Simulation sim = CreateSimulation(args, out SimConfig _, out string _, out string[] _);
        sim.RegenerateWorld(seed);

        var stopwatch = Stopwatch.StartNew();
        long executed = 0;
        while (executed < ticks)
        {
            long batch = System.Math.Min(ticks - executed, 1000);
            sim.Tick((int)batch);
            executed += batch;
        }
        stopwatch.Stop();

        _ = pass;
        return new DigestResult(sim.StateDigestString(), stopwatch.Elapsed.TotalSeconds);
    }

    // ---------------------------------------------------------------------
    // 模式：batch（多 seed 鲁棒性与涌现性体检）
    // ---------------------------------------------------------------------

    private static int RunBatch(Args args)
    {
        int[] seeds = args.IntList("--seeds");
        if (seeds.Length == 0)
        {
            seeds = new int[] { 1, 2, 3, 4, 5 };
        }

        SimConfig baseConfig = LoadConfig(args, out string _, out string[] _);
        long ticks = ResolveTickCount(args, baseConfig);

        string outRoot = args.String("--out", System.IO.Path.Combine(System.Environment.CurrentDirectory, "runs"));
        string batchDir = RunReport.EnsureDirectory(System.IO.Path.Combine(outRoot, "batch-" + System.DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)));

        System.Console.WriteLine("批量运行：seed 数 " + seeds.Length + "，每局 " + ticks + " tick（"
            + (ticks / baseConfig.Clock.TicksPerDay) + " 天）");

        var summary = new System.Text.StringBuilder(4096);
        summary.Append("| seed | 山脉 | 森林 | 草地 | 水域 | 摘要 | 用时(s) | tick/s |\n|---|---|---|---|---|---|---|---|\n");

        int failures = 0;
        for (int i = 0; i < seeds.Length; i++)
        {
            try
            {
                Simulation sim = CreateSimulation(args, out SimConfig _, out string _, out string[] _);
                sim.RegenerateWorld(seeds[i]);

                var stopwatch = Stopwatch.StartNew();
                long executed = 0;
                while (executed < ticks)
                {
                    long batch = System.Math.Min(ticks - executed, 1000);
                    sim.Tick((int)batch);
                    executed += batch;
                }
                stopwatch.Stop();

                WorldGenerator.Result info = sim.GenerationInfo!;
                double speed = ticks / System.Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
                summary.Append("| ").Append(seeds[i])
                       .Append(" | ").Append(info.MountainTiles)
                       .Append(" | ").Append(info.ForestTiles)
                       .Append(" | ").Append(info.GrassTiles)
                       .Append(" | ").Append(info.WaterTiles)
                       .Append(" | `").Append(sim.StateDigestString())
                       .Append("` | ").Append(stopwatch.Elapsed.TotalSeconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
                       .Append(" | ").Append(speed.ToString("0", System.Globalization.CultureInfo.InvariantCulture))
                       .Append(" |\n");

                System.Console.WriteLine("  seed " + seeds[i] + " 完成：森林=" + info.ForestTiles
                    + " 水域=" + info.WaterTiles
                    + " digest=" + sim.StateDigestString()
                    + " (" + stopwatch.Elapsed.TotalSeconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "s)");
            }
            catch (System.Exception ex)
            {
                failures++;
                System.Console.Error.WriteLine("  seed " + seeds[i] + " 失败：" + ex.Message);
                summary.Append("| ").Append(seeds[i]).Append(" | - | - | - | - | 失败: ")
                       .Append(ex.Message.Replace("|", "/")).Append(" | - | - |\n");
            }
        }

        string path = System.IO.Path.Combine(batchDir, "batch-report.md");
        RunReport.WriteTextFile(path, "# 批量运行报告\n\n" + summary.ToString());
        System.Console.WriteLine();
        System.Console.WriteLine("批量报告：" + path);
        System.Console.WriteLine(failures == 0 ? "全部 seed 通过" : (failures + " 个 seed 失败"));
        return failures == 0 ? 0 : 1;
    }
}
