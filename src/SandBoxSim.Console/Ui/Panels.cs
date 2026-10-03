using System.Text;
using SandBoxSim.ConsoleApp.Render;
using SandBoxSim.ConsoleApp.Tui;
using SandBoxSim.Core;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.History;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.ConsoleApp.Ui;

/// <summary>状态栏 + 信息面板 + 操作提示栏的绘制。</summary>
public static class Panels
{
    /// <summary>顶栏高度（行）。</summary>
    public const int TopBarHeight = 2;

    /// <summary>底栏高度（行）。</summary>
    public const int BottomBarHeight = 2;

    /// <summary>右侧信息面板宽度（列）。</summary>
    public const int SidePanelWidth = 36;

    /// <summary>
    /// 顶栏：时间 / 速度 / 人口 / 资源汇总 / 天气。
    /// 一屏之内回答玩家三个问题：现在第几天、世界有多少人、资源够不够。
    /// </summary>
    public static void DrawTopBar(
        RenderBuffer buffer, Simulation sim, int speedMultiplier, string seedText, double fps)
    {
        Rgb back = Palette.UiPanel;
        buffer.FillRect(0, 0, buffer.Width, TopBarHeight, back);

        Core.Environment.Calendar cal = sim.World.Calendar;
        DailySample latest = sim.LastDailySample;

        // 第一行：时间 + 速度 + FPS
        string timeText = "Day " + cal.Day + "  "
            + cal.Hour.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + ":"
            + cal.Minute.ToString("00", System.Globalization.CultureInfo.InvariantCulture)
            + (cal.IsDay ? " 日" : " 夜");
        buffer.WriteText(1, 0, timeText, Palette.UiText, back, bold: true);

        string speedText = speedMultiplier == 0 ? "[暂停]" : "[×" + speedMultiplier + "]";
        Rgb speedColor = speedMultiplier == 0 ? Palette.UiWarn : Palette.UiAccent;
        buffer.WriteText(22, 0, speedText, speedColor, back, bold: true);

        string fpsText = fps.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " fps";
        buffer.WriteText(43, 0, "Ticks/s=" + (speedMultiplier * sim.Config.Clock.TicksPerSecondAt1x)
            + "  " + fpsText, Palette.UiTextDim, back);

        // 第二行：人口 + 建筑
        string weather = WeatherInfo.DisplayNameOf(sim.World.Weather.Kind);
        string popText = "人口 " + sim.PopulationCount
            + "  床位 " + sim.Buildings.TotalBeds
            + "  建筑 " + sim.BuildingCount
            + "  天气 " + weather;

        buffer.WriteText(1, 1, popText, Palette.UiText, back);

        float food = sim.World.TotalResource(ResourceKind.Food);
        float wood = sim.World.TotalResource(ResourceKind.Wood);
        float stone = sim.World.TotalResource(ResourceKind.Stone);
        float iron = sim.World.TotalResource(ResourceKind.Iron);

        // 资源显示"存量 + 剩余比例"，只给绝对数字玩家无法判断紧张程度。
        string resText = "粮 " + food.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
            + " (" + Percent(sim.World, ResourceKind.Food) + ")"
            + "  木 " + wood.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
            + " (" + Percent(sim.World, ResourceKind.Wood) + ")"
            + "  石 " + stone.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
            + "  铁 " + iron.ToString("0", System.Globalization.CultureInfo.InvariantCulture);

        Rgb resColor = sim.ResourceSystem.GlobalScarcity() > 0.6f ? Palette.UiWarn : Palette.UiText;
        buffer.WriteText(46, 1, resText, resColor, back);

        // 种子放在最右（复现实验必须能看到种子）
        string seedLabel = "seed " + seedText;
        int seedCol = buffer.Width - RenderBuffer.DisplayWidth(seedLabel) - 2;
        if (seedCol > 90) { buffer.WriteText(seedCol, 0, seedLabel, Palette.UiTextDim, back); }

        // 饥荒告警：把"统计口径的结果"变成玩家能立刻看到的提示（第 91 条）
        if (sim.Stats.FamineActive)
        {
            buffer.WriteText(buffer.Width - 14, 1, " 饥荒中!", Palette.UiDanger, back, bold: true);
        }
    }

    private static string Percent(SandBoxSim.Core.Environment.World world, ResourceKind kind)
    {
        float capacity = world.TotalCapacity(kind);
        if (capacity <= 0f) { return "n/a"; }
        float fraction = world.TotalResource(kind) / capacity;
        return (fraction * 100f).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>底栏：快捷键提示 + 当前 Overlay。</summary>
    public static void DrawBottomBar(RenderBuffer buffer, Simulation sim, MapOverlay overlay, string statusMessage)
    {
        int top = buffer.Height - BottomBarHeight;
        Rgb back = Palette.UiPanel;
        buffer.FillRect(0, top, buffer.Width, BottomBarHeight, back);

        buffer.DrawHorizontalLine(0, top, buffer.Width, '\u2500', Palette.UiBorder, back);

        string line1 = " 方向键/WASD 移动    +/- 缩放    SPACE 暂停    1/2/4/8 速度    "
            + "O 切换叠加层    H 帮助    N 新世界    Q 退出";
        buffer.WriteText(0, top + 1, RenderBuffer.Truncate(line1, buffer.Width), Palette.UiText, back);

        string overlayName = overlay == MapOverlay.None ? "地形" : overlay.ToString();
        string right = "叠加层: " + overlayName;
        int col = buffer.Width - RenderBuffer.DisplayWidth(right) - 2;
        if (col > 0)
        {
            buffer.WriteText(col, top + 1, right, Palette.UiAccent, back);
        }

        if (!string.IsNullOrEmpty(statusMessage))
        {
            buffer.WriteText(0, top, RenderBuffer.Truncate(" " + statusMessage, buffer.Width - 2), Palette.UiWarn, back);
        }
    }

    /// <summary>
    /// 右侧信息面板：世界概况 / 天气与光照 / 选中格详情 / 最近事件。
    /// 这是"观察性"的主要载体（第 91 节），因此信息密度刻意做得高，但用颜色分组。
    /// </summary>
    public static void DrawSidePanel(RenderBuffer buffer, Simulation sim, int originX, int width, int originY, int height, int selectedX, int selectedY)
    {
        if (width < 20 || height < 8) { return; }

        Rgb back = Palette.UiPanelAlt;
        buffer.FillRect(originX, originY, width, height, back);
        for (int y = originY; y < originY + height; y++)
        {
            buffer.SetGlyph(originX, y, '\u2502', Palette.UiBorder, back);
        }

        int x = originX + 2;
        int innerWidth = width - 4;
        int y2 = originY + 1;

        y2 += WriteSection(buffer, x, y2, innerWidth, "世界", back);
        y2 = WriteLine(buffer, x, y2, innerWidth, " 种子 " + sim.World.Seed + "   尺寸 " + sim.World.Width + "×" + sim.World.Height, Palette.UiText, back);

        int[] terrain = sim.World.CountTerrain();
        y2 = WriteLine(buffer, x, y2, innerWidth, " 森林 " + terrain[(int)TerrainKind.Forest]
            + "  草地 " + terrain[(int)TerrainKind.Grass]
            + "  水域 " + terrain[(int)TerrainKind.Water], Palette.UiTextDim, back);
        y2 = WriteLine(buffer, x, y2, innerWidth, " 山地 " + terrain[(int)TerrainKind.Mountain]
            + "  沙滩 " + terrain[(int)TerrainKind.Sand]
            + "  农田 " + terrain[(int)TerrainKind.Farmland]
            + "  道路 " + terrain[(int)TerrainKind.Road], Palette.UiTextDim, back);
        y2++;

        y2 += WriteSection(buffer, x, y2, innerWidth, "气候", back);
        float moisture = sim.World.AverageMoisture();
        float temperature = sim.World.AverageTemperature();
        y2 = WriteLine(buffer, x, y2, innerWidth,
            " 湿度 " + Bar(moisture, 10), Palette.UiText, back);
        y2 = WriteLine(buffer, x, y2, innerWidth,
            " 气温 " + Bar(temperature, 10), Palette.UiText, back);
        y2 = WriteLine(buffer, x, y2, innerWidth,
            " 光照 " + Bar(sim.World.Calendar.LightLevel, 10), Palette.UiText, back);
        y2 = WriteLine(buffer, x, y2, innerWidth,
            " 天气 " + WeatherInfo.DisplayNameOf(sim.World.Weather.Kind)
            + "（已持续 " + sim.World.Weather.DurationHours + "h）", Palette.UiText, back);
        y2++;

        y2 += WriteSection(buffer, x, y2, innerWidth, "资源（剩余比例）", back);
        y2 = WriteLine(buffer, x, y2, innerWidth, " 粮食 " + Bar(sim.ResourceSystem.FractionOf(ResourceKind.Food), 10)
            + " " + sim.World.TotalResource(ResourceKind.Food).ToString("0", System.Globalization.CultureInfo.InvariantCulture), Palette.UiText, back);
        y2 = WriteLine(buffer, x, y2, innerWidth, " 木材 " + Bar(sim.ResourceSystem.FractionOf(ResourceKind.Wood), 10)
            + " " + sim.World.TotalResource(ResourceKind.Wood).ToString("0", System.Globalization.CultureInfo.InvariantCulture), Palette.UiText, back);
        y2 = WriteLine(buffer, x, y2, innerWidth, " 石头 " + Bar(sim.ResourceSystem.FractionOf(ResourceKind.Stone), 10)
            + " " + sim.World.TotalResource(ResourceKind.Stone).ToString("0", System.Globalization.CultureInfo.InvariantCulture), Palette.UiText, back);
        y2 = WriteLine(buffer, x, y2, innerWidth, " 采集累计 " + sim.ResourceSystem.TotalHarvested.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
            + "   枯竭次数 " + sim.ResourceSystem.DepletionEvents, Palette.UiTextDim, back);
        y2++;

        // ---- 选中格详情 ----
        if (sim.World.IsInBounds(selectedX, selectedY))
        {
            y2 += WriteSection(buffer, x, y2, innerWidth, "选中格 " + selectedX + "," + selectedY, back);
            ref readonly Tile tile = ref sim.World.TileAt(selectedX, selectedY);
            y2 = WriteLine(buffer, x, y2, innerWidth, " 地形 " + TerrainInfo.NameOf(tile.Terrain), Palette.UiText, back);
            y2 = WriteLine(buffer, x, y2, innerWidth, " 肥沃 " + Bar(tile.Fertility, 8)
                + "  湿度 " + Bar(tile.Moisture, 8), Palette.UiTextDim, back);
            y2 = WriteLine(buffer, x, y2, innerWidth, " 植被 " + Bar(tile.Vegetation, 8)
                + "  温度 " + Bar(tile.Temperature, 8), Palette.UiTextDim, back);

            if (tile.HasResource)
            {
                y2 = WriteLine(buffer, x, y2, innerWidth,
                    " 资源 " + ResourceInfo.NameOf(tile.Resource.Kind)
                    + " " + tile.Resource.Amount.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
                    + "/" + tile.Resource.Capacity.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                    + " (" + Percent(tile.Resource.Fraction) + ")", Palette.UiText, back);
            }
            else
            {
                y2 = WriteLine(buffer, x, y2, innerWidth, " 资源 无", Palette.UiTextDim, back);
            }

            if (tile.Fire != FireState.None)
            {
                y2 = WriteLine(buffer, x, y2, innerWidth, " 火灾状态 " + tile.Fire, Palette.UiDanger, back, bold: true);
            }
            y2++;
        }

        // ---- 事件日志（第 55 / 91 节） ----
        y2 += WriteSection(buffer, x, y2, innerWidth, "最近事件", back);
        WorldEvent[] events = sim.Events.Recent(8, EventImportance.Trivial);
        int remaining = height - (y2 - originY) - 2;
        for (int i = events.Length - 1; i >= 0 && remaining > 0; i--)
        {
            WorldEvent ev = events[i];
            string line = " " + ev.Description;
            Rgb color = ev.Importance >= EventImportance.Important ? Palette.UiWarn : Palette.UiTextDim;
            y2 = WriteLine(buffer, x, y2, innerWidth, line, color, back);
            remaining--;
        }
    }

    private static string Percent(float fraction01)
        => (SimMath.Clamp01(fraction01) * 100f).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";

    /// <summary>用 Unicode 方块画一个 0..1 的进度条（比数字更快被眼睛捕捉）。</summary>
    public static string Bar(float value01, int width)
    {
        float v = SimMath.Clamp01(value01);
        int filled = (int)System.Math.Round(v * width);
        var sb = new StringBuilder(width + 2);
        sb.Append('[');
        for (int i = 0; i < width; i++)
        {
            sb.Append(i < filled ? '#' : '.');
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static int WriteSection(RenderBuffer buffer, int x, int y, int width, string title, Rgb back)
    {
        buffer.WriteText(x, y, title, Palette.UiAccent, back, bold: true);
        return 1;
    }

    private static int WriteLine(RenderBuffer buffer, int x, int y, int width, string text, Rgb fore, Rgb back, bool bold = false)
    {
        if (y >= buffer.Height) { return 0; }
        buffer.WriteText(x, y, RenderBuffer.Truncate(text, width), fore, back, bold);
        return 1;
    }

    /// <summary>
    /// 帮助浮层（H 键）。把控制、叠加层含义、当前已知限制都写在一屏里，
    /// 这样"玩家必须查 wiki 才能知道怎么玩"的问题不会在第一版就出现。
    /// </summary>
    public static void DrawHelpOverlay(RenderBuffer buffer, MapOverlay currentOverlay)
    {
        int width = System.Math.Min(76, buffer.Width - 4);
        int height = System.Math.Min(28, buffer.Height - 4);
        int x = (buffer.Width - width) / 2;
        int y = (buffer.Height - height) / 2;

        Rgb back = Palette.UiPanel;
        buffer.FillRect(x, y, width, height, back);
        buffer.DrawHorizontalLine(x, y, width, '\u2500', Palette.UiBorder, back);
        buffer.DrawHorizontalLine(x, y + height - 1, width, '\u2500', Palette.UiBorder, back);

        int cx = x + 2;
        int cy = y + 1;
        buffer.WriteText(cx, cy++, "SandBoxSim — 帮助", Palette.UiAccent, back, bold: true);
        cy++;

        string[] lines =
        {
            "相机      方向键 / WASD 平移    +/- 缩放    Home 复位到地图中心",
            "时间      SPACE 暂停    1 / 2 / 4 / 8 切换速度",
            "世界      N 用新种子重生成世界    R 回到当前种子的初始状态",
            "观察      O 循环切换叠加层：地形 / 肥沃 / 湿度 / 温度 / 木材 / 食物 / 植被 / 火险 / 可通行",
            "选格      鼠标左键点击（若终端支持）或 Enter 选中视野中心格",
            "退出      Q 或 ESC",
            "",
            "本版本（M0）验证的是：世界可生成、可复现、可观察、Tick 架构与空间索引工作正常。",
            "居民/资源采集/建造/人口/历史会在后续 Milestone 逐步接入（见 docs/12-Milestones.md）。",
            "",
            "当前叠加层：" + (currentOverlay == MapOverlay.None ? "地形" : currentOverlay.ToString()),
        };

        for (int i = 0; i < lines.Length && cy < y + height - 1; i++)
        {
            if (lines[i].Length == 0) { cy++; continue; }
            cy += WriteLine(buffer, cx, cy, width - 4, lines[i], Palette.UiText, back);
        }
    }
}
