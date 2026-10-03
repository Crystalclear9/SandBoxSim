using SandBoxSim.Core.Foundation;

namespace SandBoxSim.ConsoleApp.Tui;

/// <summary>
/// 世界相机：负责"世界像素 ↔ 屏幕格子"的换算与平移缩放。
///
/// 坐标系统一说明（很容易搞混，因此固定下来）：
///   * 世界像素坐标：1 格世界 = 1 像素。x ∈ [0, worldWidth)，y ∈ [0, worldHeight)。
///   * 屏幕格子坐标：终端的一个字符格 = 两个上下堆叠的像素（半方块技巧）。
///   * 视图像素范围：worldPixelX ∈ [ViewLeft, ViewLeft + PixelWidth)，
///     其中 PixelWidth = bufferWidth × zoom，PixelHeight = bufferHeight × 2 × zoom。
///
/// 缩放必须是整数倍：非整数倍会让"哪些像素被画出来"依赖浮点舍入，
/// 相同种子在不同机器上可能渲染出不同画面，观察结论就无法复现。
/// </summary>
public sealed class Camera
{
    /// <summary>视图左上角对应的世界像素坐标。</summary>
    public int ViewLeft { get; private set; }
    public int ViewTop { get; private set; }

    /// <summary>每个屏幕像素对应多少世界像素（≥1）。</summary>
    public int Zoom { get; private set; } = 1;

    public int MinZoom { get; private set; } = 1;
    public int MaxZoom { get; private set; } = 8;

    public int WorldWidth { get; private set; }
    public int WorldHeight { get; private set; }

    /// <summary>视图内可见的像素宽/高（由渲染层每帧设置）。</summary>
    public int PixelWidth { get; private set; } = 1;
    public int PixelHeight { get; private set; } = 1;

    public Camera(int worldWidth, int worldHeight)
    {
        WorldWidth = worldWidth > 0 ? worldWidth : 1;
        WorldHeight = worldHeight > 0 ? worldHeight : 1;
        CenterOn(WorldWidth / 2, WorldHeight / 2);
    }

    public void SetWorldSize(int width, int height)
    {
        WorldWidth = width > 0 ? width : 1;
        WorldHeight = height > 0 ? height : 1;
        ClampView();
    }

    /// <summary>告知渲染层本帧的可见像素尺寸（格子数 × 2 = 像素高）。</summary>
    public void SetViewport(int pixelWidth, int pixelHeight)
    {
        PixelWidth = pixelWidth > 0 ? pixelWidth : 1;
        PixelHeight = pixelHeight > 0 ? pixelHeight : 1;
        ClampView();
    }

    /// <summary>把视图中心对准世界坐标。</summary>
    public void CenterOn(int worldX, int worldY)
    {
        ViewLeft = worldX - (PixelWidth / 2);
        ViewTop = worldY - (PixelHeight / 2);
        ClampView();
    }

    /// <summary>平移（屏幕像素量）。正 dx 表示视图向右移动（看到更右边的内容）。</summary>
    public void Pan(int dxPixels, int dyPixels)
    {
        ViewLeft += dxPixels;
        ViewTop += dyPixels;
        ClampView();
    }

    /// <summary>以视图中心为锚点缩放。</summary>
    public void ZoomIn()
    {
        SetZoom(Zoom + 1);
    }

    public void ZoomOut()
    {
        SetZoom(Zoom - 1);
    }

    /// <summary>
    /// 设置缩放。缩放时会保持"视图中心的世界坐标"不变，
    /// 否则玩家每次缩放都会被扔到地图另一头 —— 这是相机手感的关键细节。
    /// </summary>
    public void SetZoom(int zoom)
    {
        int clamped = SimMath.Clamp(zoom, MinZoom, MaxZoom);
        if (clamped == Zoom) { return; }

        int centerX = ViewLeft + (PixelWidth / 2);
        int centerY = ViewTop + (PixelHeight / 2);

        Zoom = clamped;
        ViewLeft = centerX - (PixelWidth / 2);
        ViewTop = centerY - (PixelHeight / 2);
        ClampView();
    }

    /// <summary>根据视口与世界尺寸设置最小缩放：保证整张地图能放进视图。</summary>
    public void ApplyFitZoom()
    {
        int fitX = 1;
        int fitY = 1;

        while ((fitX * PixelWidth) < WorldWidth && fitX < MaxZoom) { fitX++; }
        while ((fitY * PixelHeight) < WorldHeight && fitY < MaxZoom) { fitY++; }

        MinZoom = System.Math.Max(fitX, fitY);
        if (MinZoom < 1) { MinZoom = 1; }
        if (Zoom < MinZoom) { SetZoom(MinZoom); }
    }

    public void SetZoomLimits(int min, int max)
    {
        MinZoom = min < 1 ? 1 : min;
        MaxZoom = max < MinZoom ? MinZoom : max;
        if (Zoom < MinZoom) { SetZoom(MinZoom); }
        if (Zoom > MaxZoom) { SetZoom(MaxZoom); }
    }

    /// <summary>把视图限制在世界范围内（世界比视图小时居中）。</summary>
    private void ClampView()
    {
        int viewSpanX = Zoom * PixelWidth;
        int viewSpanY = Zoom * PixelHeight;

        if (viewSpanX >= WorldWidth)
        {
            ViewLeft = -(viewSpanX - WorldWidth) / 2;
        }
        else
        {
            ViewLeft = SimMath.Clamp(ViewLeft, 0, WorldWidth - viewSpanX);
        }

        if (viewSpanY >= WorldHeight)
        {
            ViewTop = -(viewSpanY - WorldHeight) / 2;
        }
        else
        {
            ViewTop = SimMath.Clamp(ViewTop, 0, WorldHeight - viewSpanY);
        }
    }

    /// <summary>视图中心对应的世界坐标。</summary>
    public void GetCenter(out int worldX, out int worldY)
    {
        worldX = ViewLeft + (PixelWidth / 2);
        worldY = ViewTop + (PixelHeight / 2);
    }

    /// <summary>
    /// 屏幕像素 → 世界像素。越界返回 false（画成黑边）。
    /// </summary>
    public bool ScreenToWorld(int screenPixelX, int screenPixelY, out int worldX, out int worldY)
    {
        worldX = ViewLeft + (screenPixelX * Zoom);
        worldY = ViewTop + (screenPixelY * Zoom);
        return worldX >= 0 && worldY >= 0 && worldX < WorldWidth && worldY < WorldHeight;
    }

    /// <summary>屏幕格子 → 世界像素（左上角那个像素）。</summary>
    public bool CellToWorld(int cellX, int cellY, out int worldX, out int worldY)
        => ScreenToWorld(cellX, cellY * 2, out worldX, out worldY);

    /// <summary>
    /// 屏幕格子中心（两像素之间的中点）→ 世界像素。
    /// 拾取（点击选格）用这个比用左上角更符合玩家预期。
    /// </summary>
    public bool CellCenterToWorld(int cellX, int cellY, out int worldX, out int worldY)
        => ScreenToWorld(cellX, (cellY * 2) + 0, out worldX, out worldY);
}
