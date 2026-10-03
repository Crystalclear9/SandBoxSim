using System.Text;
using SandBoxSim.Core;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.ConsoleApp.Reporting;

/// <summary>
/// headless 长跑的产出：stats.json / report.md / digest.txt / events.md（第 10、59、91 节）。
///
/// 这些文件的存在理由：模拟游戏的核心产品是"值得观察的行为"，
/// 而"值得观察"是个模糊说法，必须落到可复核的产物上（验收标准 5）。
/// 任何人拿到 runs/&lt;runId&gt;/ 就能自己判断"这 100 天里到底发生了什么"。
/// </summary>
public static class RunReport
{
    /// <summary>一次运行的身份标识：seed + 天数 + tick 数。</summary>
    public static string BuildRunId(int seed, int days, long ticks)
        => seed + "-" + days + "d-" + ticks + "t";

    /// <summary>取趋势曲线的 Unicode 迷你图（▁▂▃▄▅▆▇█）。</summary>
    public static string Sparkline(System.Collections.Generic.IReadOnlyList<float> values, int width = 60)
    {
        if (values == null || values.Count == 0) { return "(无数据)"; }
        char[] blocks = { '\u2581', '\u2582', '\u2583', '\u2584', '\u2585', '\u2586', '\u2587', '\u2588' };

        float min = float.MaxValue;
        float max = float.MinValue;
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] < min) { min = values[i]; }
            if (values[i] > max) { max = values[i]; }
        }

        var sb = new StringBuilder(width + 16);
        float span = max - min;
        int step = System.Math.Max(1, values.Count / System.Math.Max(1, width));

        for (int i = 0; i < values.Count; i += step)
        {
            float normalized = span <= 0f ? 0.5f : (values[i] - min) / span;
            int index = (int)System.Math.Round(normalized * (blocks.Length - 1));
            if (index < 0) { index = 0; }
            if (index >= blocks.Length) { index = blocks.Length - 1; }
            sb.Append(blocks[index]);
        }

        sb.Append("  最小 ").Append(min.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        sb.Append("  最大 ").Append(max.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    /// <summary>写 stats.json：逐日样本的机器可读形式。</summary>
    public static string WriteStatsJson(Simulation sim, string runId, long ticks)
    {
        var root = JsonValue.Object();
        root.Set("runId", JsonValue.From(runId));
        root.Set("seed", JsonValue.From(sim.World.Seed));
        root.Set("ticks", JsonValue.From(ticks));
        root.Set("days", JsonValue.From(sim.World.Calendar.Day));
        root.Set("worldWidth", JsonValue.From(sim.World.Width));
        root.Set("worldHeight", JsonValue.From(sim.World.Height));
        root.Set("digest", JsonValue.From(sim.StateDigestString()));

        var counters = JsonValue.Object();
        counters.Set("births", JsonValue.From(sim.Stats.TotalBirths));
        counters.Set("deaths", JsonValue.From(sim.Stats.TotalDeaths));
        counters.Set("migrations", JsonValue.From(sim.Stats.TotalMigrations));
        counters.Set("famineEpisodes", JsonValue.From(sim.Stats.FamineEpisodeCount));
        counters.Set("depletionEvents", JsonValue.From(sim.ResourceSystem.DepletionEvents));
        counters.Set("eventsRecorded", JsonValue.From(sim.Events.TotalRecorded));
        root.Set("counters", counters);

        var totals = JsonValue.Object();
        totals.Set("food", JsonValue.From((double)sim.World.TotalResource(Core.Environment.ResourceKind.Food)));
        totals.Set("wood", JsonValue.From((double)sim.World.TotalResource(Core.Environment.ResourceKind.Wood)));
        totals.Set("stone", JsonValue.From((double)sim.World.TotalResource(Core.Environment.ResourceKind.Stone)));
        totals.Set("iron", JsonValue.From((double)sim.World.TotalResource(Core.Environment.ResourceKind.Iron)));
        totals.Set("harvested", JsonValue.From(sim.ResourceSystem.TotalHarvested));
        totals.Set("regenerated", JsonValue.From(sim.ResourceSystem.TotalRegenerated));
        root.Set("totals", totals);

        var terrain = JsonValue.Object();
        int[] counts = sim.World.CountTerrain();
        for (int i = 0; i < counts.Length; i++)
        {
            terrain.Set(Core.Environment.TerrainInfo.NameOf((Core.Environment.TerrainKind)i), JsonValue.From(counts[i]));
        }
        root.Set("terrain", terrain);

        var daily = JsonValue.Array();
        System.Collections.Generic.IReadOnlyList<DailySample> samples = sim.Stats.Daily;
        for (int i = 0; i < samples.Count; i++)
        {
            DailySample s = samples[i];
            var day = JsonValue.Object();
            day.Set("day", JsonValue.From(s.Day));
            day.Set("population", JsonValue.From(s.Population));
            day.Set("food", JsonValue.From((double)s.Food));
            day.Set("wood", JsonValue.From((double)s.Wood));
            day.Set("stone", JsonValue.From((double)s.Stone));
            day.Set("iron", JsonValue.From((double)s.Iron));
            day.Set("forestTiles", JsonValue.From(s.ForestTiles));
            day.Set("farmlandTiles", JsonValue.From(s.FarmlandTiles));
            day.Set("buildings", JsonValue.From(s.BuildingCount));
            day.Set("settlements", JsonValue.From(s.SettlementCount));
            day.Set("moisture", JsonValue.From((double)s.AverageMoisture));
            day.Set("temperature", JsonValue.From((double)s.AverageTemperature));
            day.Set("scarcity", JsonValue.From((double)s.Scarcity));
            day.Set("births", JsonValue.From(s.Births));
            day.Set("deaths", JsonValue.From(s.Deaths));
            day.Set("migrations", JsonValue.From(s.Migrations));
            day.Set("burningTiles", JsonValue.From(s.BurningTiles));
            daily.Add(day);
        }
        root.Set("daily", daily);

        return root.ToJson(indented: true);
    }

    /// <summary>写人类可读报告：趋势曲线 + 事件摘录 + 本次运行的可复现命令。</summary>
    public static string BuildMarkdownReport(Simulation sim, string runId, long ticks, string reproCommand)
    {
        var sb = new StringBuilder(8192);
        Core.Environment.Calendar cal = sim.World.Calendar;

        sb.Append("# 运行报告 ").Append(runId).Append("\n\n");
        sb.Append("| 项目 | 值 |\n|---|---|\n");
        sb.Append("| 种子 | ").Append(sim.World.Seed).Append(" |\n");
        sb.Append("| 世界尺寸 | ").Append(sim.World.Width).Append("×").Append(sim.World.Height).Append(" |\n");
        sb.Append("| 推进 tick | ").Append(ticks).Append(" |\n");
        sb.Append("| 游戏天数 | ").Append(cal.Day).Append(" |\n");
        sb.Append("| 状态摘要 | `").Append(sim.StateDigestString()).Append("` |\n");
        sb.Append("| 记录事件 | ").Append(sim.Events.TotalRecorded).Append(" |\n");
        sb.Append("| 人口 | ").Append(sim.PopulationCount).Append(" |\n");
        sb.Append("| 建筑 | ").Append(sim.BuildingCount).Append(" |\n\n");

        sb.Append("## 复现命令\n\n```powershell\n").Append(reproCommand).Append("\n```\n\n");

        sb.Append("## 趋势\n\n");
        System.Collections.Generic.IReadOnlyList<DailySample> daily = sim.Stats.Daily;

        var population = new System.Collections.Generic.List<float>();
        var food = new System.Collections.Generic.List<float>();
        var wood = new System.Collections.Generic.List<float>();
        var forest = new System.Collections.Generic.List<float>();
        var moisture = new System.Collections.Generic.List<float>();
        var scarcity = new System.Collections.Generic.List<float>();

        for (int i = 0; i < daily.Count; i++)
        {
            population.Add(daily[i].Population);
            food.Add(daily[i].Food);
            wood.Add(daily[i].Wood);
            forest.Add(daily[i].ForestTiles);
            moisture.Add(daily[i].AverageMoisture);
            scarcity.Add(daily[i].Scarcity);
        }

        sb.Append("- 人口：").Append(Sparkline(population)).Append('\n');
        sb.Append("- 食物存量：").Append(Sparkline(food)).Append('\n');
        sb.Append("- 木材存量：").Append(Sparkline(wood)).Append('\n');
        sb.Append("- 森林格数：").Append(Sparkline(forest)).Append('\n');
        sb.Append("- 平均湿度：").Append(Sparkline(moisture)).Append('\n');
        sb.Append("- 资源紧张度：").Append(Sparkline(scarcity)).Append("\n\n");

        sb.Append("## 地形分布（结束时刻）\n\n| 地形 | 格数 |\n|---|---|\n");
        int[] counts = sim.World.CountTerrain();
        for (int i = 0; i < counts.Length; i++)
        {
            Core.Environment.TerrainKind kind = (Core.Environment.TerrainKind)i;
            string name = Core.Environment.TerrainInfo.NameOf(kind);
            if (name == "unknown") { continue; }
            sb.Append("| ").Append(name).Append(" | ").Append(counts[i]).Append(" |\n");
        }
        sb.Append('\n');

        sb.Append("## 关键事件（最近 ").Append(System.Math.Min(40, sim.Events.Count)).Append(" 条）\n\n");
        Core.History.WorldEvent[] events = sim.Events.Recent(40, Core.History.EventImportance.Minor);
        if (events.Length == 0)
        {
            sb.Append("_本次运行没有记录到重要事件。_\n\n");
        }
        else
        {
            sb.Append("| tick | 天 | 事件 | 说明 |\n|---|---|---|---|\n");
            int tpd = sim.World.Calendar.TicksPerDay;
            for (int i = 0; i < events.Length; i++)
            {
                Core.History.WorldEvent ev = events[i];
                sb.Append("| ").Append(ev.Tick)
                  .Append(" | ").Append((ev.Tick / tpd) + 1)
                  .Append(" | ").Append(ev.Type)
                  .Append(" | ").Append(ev.Description.Replace("|", "/"))
                  .Append(" |\n");
            }
            sb.Append('\n');
        }

        sb.Append("## 本轮验收自检\n\n");
        sb.Append("- [").Append(population.Count > 0 ? "x" : " ").Append("] 产生了逐日统计样本\n");
        sb.Append("- [").Append(sim.Events.TotalRecorded > 0 ? "x" : " ").Append("] 记录了世界事件\n");
        sb.Append("- [ ] 出生 / 死亡 / 建造 / 迁移 / 第二聚落 / 火灾（需要 M2–M8 的系统接入，见 docs/12-Milestones.md）\n");

        return sb.ToString();
    }

    /// <summary>写事件清单（Markdown 表格）。</summary>
    public static string BuildEventsMarkdown(Simulation sim, int max = 500)
    {
        var sb = new StringBuilder(4096);
        sb.Append("# 事件清单\n\n");
        Core.History.WorldEvent[] events = sim.Events.Recent(max, Core.History.EventImportance.Trivial);
        sb.Append("共记录 ").Append(sim.Events.TotalRecorded).Append(" 条，保留窗口 ").Append(sim.Events.Count)
          .Append(" 条，下面是最新的 ").Append(events.Length).Append(" 条。\n\n");
        sb.Append("| tick | 天 | 类型 | 重要度 | 地点 | 说明 | 因果 |\n|---|---|---|---|---|---|---|\n");
        int tpd = sim.World.Calendar.TicksPerDay;
        for (int i = 0; i < events.Length; i++)
        {
            Core.History.WorldEvent ev = events[i];
            string location = ev.HasLocation ? ev.Location.X + "," + ev.Location.Y : "-";
            sb.Append("| ").Append(ev.Tick)
              .Append(" | ").Append((ev.Tick / tpd) + 1)
              .Append(" | ").Append(ev.Type)
              .Append(" | ").Append(ev.Importance)
              .Append(" | ").Append(location)
              .Append(" | ").Append(ev.Description.Replace("|", "/"))
              .Append(" | ").Append(string.IsNullOrEmpty(ev.Cause) ? "-" : ev.Cause.Replace("|", "/"))
              .Append(" |\n");
        }
        return sb.ToString();
    }

    /// <summary>确保目录存在并返回规范化路径。</summary>
    public static string EnsureDirectory(string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        if (!System.IO.Directory.Exists(full))
        {
            System.IO.Directory.CreateDirectory(full);
        }
        return full;
    }

    public static string WriteTextFile(string path, string content)
    {
        string? dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            EnsureDirectory(dir);
        }
        System.IO.File.WriteAllText(path, content);
        return path;
    }
}
