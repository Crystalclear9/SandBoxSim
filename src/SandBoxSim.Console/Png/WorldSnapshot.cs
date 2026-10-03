using SandBoxSim.ConsoleApp.Render;
using SandBoxSim.ConsoleApp.Tui;
using SandBoxSim.Core;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.ConsoleApp.Png;

/// <summary>
/// 把当前世界渲染成一张 PNG 快照（验收标准 5）。
///
/// 为什么这是"必需功能"而不是"锦上添花"：
///   TUI 只能"活着看"，无法在 CI 里留下证据，也无法贴进 PR 让人复核。
///   快照把"世界长什么样、聚落分布如何、森林是否被砍秃"变成可归档的产物，
///   而且与 TUI 共用同一套配色（<see cref="Palette"/>），所以两边看到的颜色一致，
///   不会出现"控制台里是绿的、图上却是褐的"这种令人不信任的偏差。
///
/// 缩放用最近邻：模拟游戏里"每个格子代表一个确定位置"比平滑插值更重要。
/// </summary>
public static class WorldSnapshot
{
    /// <summary>渲染选项。</summary>
    public sealed class Options
    {
        /// <summary>输出图像宽（像素）。高度按世界长宽比自动计算。</summary>
        public int Width = 800;

        /// <summary>每格放大的倍数；≤0 表示按 Width 自动。</summary>
        public int CellScale = 0;

        /// <summary>光照（用于夜间快照）；1 = 白天正午。</summary>
        public float LightLevel = 1f;

        /// <summary>是否画网格线（放大倍数足够时才有意义）。</summary>
        public bool DrawGrid = false;

        /// <summary>是否用叠加层配色（与 TUI 的 O 键一致）。</summary>
        public MapOverlay Overlay = MapOverlay.None;

        /// <summary>标题栏高度（像素），0 = 不画。</summary>
        public int HeaderHeight = 0;
    }

    /// <summary>渲染世界快照。</summary>
    public static PngImage Render(SandBoxSim.Core.Simulation sim, Options? options = null)
    {
        Options opt = options ?? new Options();
        SandBoxSim.Core.Environment.World world = sim.World;

        int scale = opt.CellScale > 0
            ? opt.CellScale
            : System.Math.Max(1, opt.Width / System.Math.Max(1, world.Width));

        int imageWidth = world.Width * scale;
        int imageHeight = (world.Height * scale) + opt.HeaderHeight;
        var image = new PngImage(imageWidth, imageHeight);
        image.Fill(Rgb.Black);

        uint noiseSeed = unchecked((uint)world.Seed * 2654435761u);

        // 标题栏先画：地图从 HeaderHeight 开始，因此不会互相覆盖。
        if (opt.HeaderHeight > 0)
        {
            // 标题栏用一条深色带 + 刻度占位：真正的文字渲染需要字体位图，属于美术投入，
            // 第一版刻意不做（第 98 条）。刻度每 50 像素一格，配合报告里的坐标说明足够定位。
            for (int hy = 0; hy < opt.HeaderHeight; hy++)
            {
                for (int hx = 0; hx < imageWidth; hx++)
                {
                    bool tick = (hx % 50) == 0 && hy >= opt.HeaderHeight - 3;
                    image.SetPixel(hx, hy, tick ? new Rgb(90, 100, 120) : new Rgb(18, 22, 28));
                }
            }
        }

        for (int y = 0; y < world.Height; y++)
        {
            for (int x = 0; x < world.Width; x++)
            {
                ref readonly Tile tile = ref world.TileAt(x, y);
                Rgb color = opt.Overlay == MapOverlay.None
                    ? Palette.Shade(in tile, x, y, noiseSeed, opt.LightLevel)
                    : OverlayColor(in tile, opt.Overlay);

                int baseX = x * scale;
                int baseY = (y * scale) + opt.HeaderHeight;

                for (int dy = 0; dy < scale; dy++)
                {
                    for (int dx = 0; dx < scale; dx++)
                    {
                        // 网格线：只在格子边界画，帮助人眼数格子（调试空间索引时很有用）
                        bool gridLine = opt.DrawGrid && scale >= 4 && (dx == 0 || dy == 0);
                        image.SetPixel(baseX + dx, baseY + dy, gridLine ? Rgb.DarkGray : color);
                    }
                }
            }
        }

        return image;
    }

    /// <summary>与 TUI 完全一致的叠加层配色（两边共用同一映射规则，避免观察结论不一致）。</summary>
    private static Rgb OverlayColor(ref readonly Tile tile, MapOverlay overlay)
    {
        switch (overlay)
        {
            case MapOverlay.Fertility: return Gradient(tile.Fertility);
            case MapOverlay.Moisture: return Gradient(tile.Moisture);
            case MapOverlay.Temperature: return Gradient(tile.Temperature);
            case MapOverlay.Wood: return tile.Resource.Kind == ResourceKind.Wood ? Gradient(tile.Resource.Fraction) : new Rgb(20, 20, 24);
            case MapOverlay.Food: return tile.Resource.Kind == ResourceKind.Food ? Gradient(tile.Resource.Fraction) : new Rgb(20, 20, 24);
            case MapOverlay.Vegetation: return Gradient(tile.Vegetation);
            case MapOverlay.FireRisk:
            {
                float dryness = 1f - tile.Moisture;
                float fuel = TerrainInfo.IsVegetation(tile.Terrain) ? tile.Vegetation : 0f;
                return Gradient(dryness * fuel);
            }
            case MapOverlay.Walkable:
                if (tile.Terrain == TerrainKind.Water) { return new Rgb(20, 30, 60); }
                return tile.Walkable ? new Rgb(40, 120, 70) : new Rgb(120, 40, 40);
            default:
                return new Rgb(120, 120, 120);
        }
    }

    private static Rgb Gradient(float value01)
    {
        float v = SimMath.Clamp01(value01);
        Rgb low = new Rgb(24, 40, 96);
        Rgb mid = new Rgb(40, 130, 96);
        Rgb high = new Rgb(228, 88, 64);
        if (v < 0.5f) { return Rgb.Lerp(low, mid, v * 2f); }
        return Rgb.Lerp(mid, high, (v - 0.5f) * 2f);
    }
}
