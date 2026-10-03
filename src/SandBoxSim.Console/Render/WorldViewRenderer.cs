using SandBoxSim.ConsoleApp.Tui;
using SandBoxSim.Core;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.ConsoleApp.Render;

/// <summary>地图上的可选叠加层（第 79 节 Debug Overlay 的第一批）。</summary>
public enum MapOverlay
{
    None = 0,
    Fertility = 1,
    Moisture = 2,
    Temperature = 3,
    Wood = 4,
    Food = 5,
    Vegetation = 6,
    FireRisk = 7,
    Walkable = 8,
}

/// <summary>
/// 世界渲染器：把相机视野内的世界像素画进 <see cref="RenderBuffer"/>。
///
/// 关键技术是"半方块（half-block）"技巧：终端一个字符格纵向塞两个像素。
///   字符 '▀' 的上半用前景色、下半用背景色 ⇒ 一格显示上下两色；
///   上下同色时退化为空格 + 背景色（更省转义序列）。
/// 这样 80×40 的终端窗口实际能显示 80×80 个世界像素 —— 在 100×100 的地图上
/// 已经可以一眼看到大半张图，这对"观察性"非常关键。
/// </summary>
public sealed class WorldViewRenderer
{
    private const char HalfBlockUpper = '\u2580';

    private readonly Camera _camera;
    private uint _noiseSeed = 0x9E3779B9u;

    public MapOverlay Overlay { get; set; } = MapOverlay.None;

    /// <summary>当前帧的全局光照 [0,1]（由日历决定）。</summary>
    public float LightLevel { get; set; } = 1f;

    /// <summary>被选中的格子（-1 表示无）。</summary>
    public int SelectedX { get; set; } = -1;
    public int SelectedY { get; set; } = -1;

    /// <summary>是否画选中框。</summary>
    public bool ShowSelection { get; set; } = true;

    /// <summary>高亮闪烁相位（每帧变化，用于让选中框"呼吸"）。</summary>
    public float HighlightPhase { get; set; }

    /// <summary>最近一帧实际绘制的像素数（性能观测用）。</summary>
    public long PixelsDrawn { get; private set; }

    public WorldViewRenderer(Camera camera)
    {
        _camera = camera;
    }

    public void SetNoiseSeed(int worldSeed) => _noiseSeed = unchecked((uint)worldSeed * 2654435761u);

    /// <summary>
    /// 把地图画进缓冲的指定矩形区域。
    /// 返回实际绘制的字符格数（性能观测）。
    /// </summary>
    public int Render(RenderBuffer buffer, SandBoxSim.Core.Environment.World world, int originX, int originY, int cellsWide, int cellsHigh)
    {
        int pixelsWide = cellsWide;
        int pixelsHigh = cellsHigh * 2;
        _camera.SetViewport(pixelsWide, pixelsHigh);

        long drawn = 0;

        for (int cy = 0; cy < cellsHigh; cy++)
        {
            int bufferY = originY + cy;
            if (bufferY >= buffer.Height) { break; }

            for (int cx = 0; cx < cellsWide; cx++)
            {
                int bufferX = originX + cx;
                if (bufferX >= buffer.Width) { break; }

                int topPixelY = cy * 2;
                int bottomPixelY = topPixelY + 1;

                bool topValid = _camera.ScreenToWorld(cx, topPixelY, out int topWorldX, out int topWorldY);
                bool bottomValid = _camera.ScreenToWorld(cx, bottomPixelY, out int bottomWorldX, out int bottomWorldY);

                if (!topValid && !bottomValid)
                {
                    buffer.SetBackground(bufferX, bufferY, Rgb.Black);
                    continue;
                }

                Rgb topColor = topValid ? SamplePixel(world, topWorldX, topWorldY) : Rgb.Black;
                Rgb bottomColor = bottomValid ? SamplePixel(world, bottomWorldX, bottomWorldY) : Rgb.Black;

                if (topColor.Equals(bottomColor))
                {
                    buffer.SetBackground(bufferX, bufferY, topColor);
                }
                else
                {
                    buffer.SetGlyph(bufferX, bufferY, HalfBlockUpper, topColor, bottomColor);
                }

                drawn++;
            }
        }

        // 选中框：在格子外围画一圈高亮，方便玩家确认"我在看哪一块"。
        if (ShowSelection && SelectedX >= 0 && SelectedY >= 0)
        {
            DrawSelectionBox(buffer, world, originX, originY, cellsWide, cellsHigh);
        }

        PixelsDrawn = drawn;
        return (int)drawn;
    }

    /// <summary>
    /// 采样一个世界像素的最终颜色：地形色 + 叠加层。
    /// 越界返回纯黑（地图外的"世界边缘"）。
    /// </summary>
    private Rgb SamplePixel(SandBoxSim.Core.Environment.World world, int x, int y)
    {
        if (!world.IsInBounds(x, y)) { return Rgb.Black; }

        ref readonly Tile tile = ref world.TileAt(x, y);

        if (Overlay == MapOverlay.None)
        {
            return Palette.Shade(in tile, x, y, _noiseSeed, LightLevel);
        }

        return OverlayColor(in tile, x, y);
    }

    /// <summary>
    /// 叠加层配色：把"看不见的变量"变成"看得见的颜色"（第 79 条）。
    /// 每个叠加层都用固定的蓝(低)→红(高)渐变，玩家不需要记图例也能看出相对高低。
    /// </summary>
    private Rgb OverlayColor(ref readonly Tile tile, int x, int y)
    {
        switch (Overlay)
        {
            case MapOverlay.Fertility:
                return Gradient(tile.Fertility);

            case MapOverlay.Moisture:
                return Gradient(tile.Moisture);

            case MapOverlay.Temperature:
                return Gradient(tile.Temperature);

            case MapOverlay.Wood:
                return tile.Resource.Kind == ResourceKind.Wood ? Gradient(tile.Resource.Fraction) : new Rgb(20, 20, 24);

            case MapOverlay.Food:
                return tile.Resource.Kind == ResourceKind.Food ? Gradient(tile.Resource.Fraction) : new Rgb(20, 20, 24);

            case MapOverlay.Vegetation:
                return Gradient(tile.Vegetation);

            case MapOverlay.FireRisk:
            {
                // 火险 = 干燥 × 植被 × 天气系数（第 43 节的公式的即时可视化版本）
                float dryness = 1f - tile.Moisture;
                float fuel = Core.Environment.TerrainInfo.IsVegetation(tile.Terrain) ? tile.Vegetation : 0f;
                return Gradient(dryness * fuel);
            }

            case MapOverlay.Walkable:
                if (tile.Terrain == TerrainKind.Water) { return new Rgb(20, 30, 60); }
                return tile.Walkable ? new Rgb(40, 120, 70) : new Rgb(120, 40, 40);

            default:
                return Palette.Shade(in tile, x, y, _noiseSeed, LightLevel);
        }
    }

    /// <summary>通用的"低=蓝 → 中=绿 → 高=红"渐变，供所有热力图复用。</summary>
    private static Rgb Gradient(float value01)
    {
        float v = SimMath.Clamp01(value01);
        Rgb low = new Rgb(24, 40, 96);
        Rgb mid = new Rgb(40, 130, 96);
        Rgb high = new Rgb(228, 88, 64);

        if (v < 0.5f) { return Rgb.Lerp(low, mid, v * 2f); }
        return Rgb.Lerp(mid, high, (v - 0.5f) * 2f);
    }

    /// <summary>
    /// 把世界坐标格画成屏幕上的高亮框（4 个角点 + 呼吸闪烁）。
    /// 只画"角"，不画完整边框：完整边框在 1× 缩放下会盖住整格的细节。
    /// </summary>
    private void DrawSelectionBox(RenderBuffer buffer, SandBoxSim.Core.Environment.World world, int originX, int originY, int cellsWide, int cellsHigh)
    {
        int zoom = _camera.Zoom;

        // 世界像素 → 屏幕像素 → 屏幕格子（整数运算，与渲染路径完全一致，保证框对准格子）
        int screenPixelX = SelectedX - _camera.ViewLeft;
        int screenPixelY = (SelectedY * 2) - _camera.ViewTop;

        if (zoom <= 0) { return; }
        int cellX = originX + (screenPixelX / zoom);
        int cellY = originY + (screenPixelY / zoom / 2);

        if (cellX < originX || cellX >= originX + cellsWide) { return; }
        if (cellY < originY || cellY >= originY + cellsHigh) { return; }

        Rgb color = Rgb.Lerp(Palette.SelectionColor, Palette.CursorColor, HighlightPhase);

        // 角标：在格子的四个角画小十字
        buffer.SetGlyph(cellX, cellY, '\u253c', color, Palette.UiBackground, bold: true);

        if (zoom > 3)
        {
            // 放大到一定程度后把整格轮廓描出来，让玩家看清"这一格覆盖了多大范围"。
            int halfWide = System.Math.Max(1, zoom / 2);
            int halfHigh = System.Math.Max(1, zoom / 4);
            for (int i = -halfWide; i <= halfWide; i++)
            {
                buffer.SetGlyph(cellX + i, cellY - halfHigh, '\u2500', color, Palette.UiBackground);
                buffer.SetGlyph(cellX + i, cellY + halfHigh, '\u2500', color, Palette.UiBackground);
            }
            for (int i = -halfHigh; i <= halfHigh; i++)
            {
                buffer.SetGlyph(cellX - halfWide, cellY + i, '\u2502', color, Palette.UiBackground);
                buffer.SetGlyph(cellX + halfWide, cellY + i, '\u2502', color, Palette.UiBackground);
            }
        }
    }
}
