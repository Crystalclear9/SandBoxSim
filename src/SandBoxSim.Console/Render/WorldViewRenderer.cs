using SandBoxSim.ConsoleApp.Tui;
using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.ConsoleApp.Render;

/// <summary>地图上的可选叠加层（第 79 节 Debug Overlay）。</summary>
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

    /// <summary>人口热力图：每 chunk 的存活人数（M4 新增，对应第 79 节的 Population Heatmap）。</summary>
    Population = 9,

    /// <summary>AI 状态：每 chunk 的主导行为（工作/移动/睡眠/空闲）。</summary>
    AiState = 10,

    /// <summary>建筑与共享库存：仓库饱和度 / 工地。</summary>
    Buildings = 11,
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

    /// <summary>最近一帧绘制的实体数（人 + 动物 + 建筑；性能观测用）。</summary>
    public int EntitiesDrawn { get; private set; }

    /// <summary>被选中的个体槽位（-1 = 无）。由 GameLoop 设置，用于给它加高亮圈。</summary>
    public int HighlightedAgentSlot { get; set; } = -1;

    // ---------------------------------------------------------------------
    // 实体索引表（每帧重建一次）
    // ---------------------------------------------------------------------
    //
    // 为什么需要这张表：渲染是**逐屏幕像素**采样的（半方块技巧），
    // 如果每个像素都去问"这一格有没有人/动物/建筑"，那就是
    // O(屏幕像素数 × 实体数)，200 个实体时每帧几十万次查询。
    //
    // 正确做法：每帧先算可见世界范围，遍历一次存活实体把可见的盖进这张小表，
    // 之后像素采样退化成一次 O(1) 查表。开销与**存活实体数**成正比，与缩放级别无关。

    private int _entityMinX;
    private int _entityMinY;
    private int _entityWidth;
    private int _entityHeight;
    private byte[] _entityLayer = System.Array.Empty<byte>();   // 0=空 1=动物 2=人 3=建筑
    private Rgb[] _entityColor = System.Array.Empty<Rgb>();
    private int[] _entitySlot = System.Array.Empty<int>();      // 人所在的槽位（选中高亮用）
    private byte[] _entityAgentState = System.Array.Empty<byte>(); // 该格上人的 AgentState

    private const byte LayerNone = 0;
    private const byte LayerWildlife = 1;
    private const byte LayerAgent = 2;
    private const byte LayerBuilding = 3;

    public WorldViewRenderer(Camera camera)
    {
        _camera = camera;
    }

    public void SetNoiseSeed(int worldSeed) => _noiseSeed = unchecked((uint)worldSeed * 2654435761u);

    /// <summary>
    /// 把地图画进缓冲的指定矩形区域。
    /// 返回实际绘制的字符格数（性能观测）。
    /// </summary>
    public int Render(RenderBuffer buffer, Simulation sim, int originX, int originY, int cellsWide, int cellsHigh)
    {
        SandBoxSim.Core.Environment.World world = sim.World;

        int pixelsWide = cellsWide;
        int pixelsHigh = cellsHigh * 2;
        _camera.SetViewport(pixelsWide, pixelsHigh);

        BuildEntityIndex(sim);

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

                Rgb topColor = topValid ? SamplePixel(sim, topWorldX, topWorldY) : Rgb.Black;
                Rgb bottomColor = bottomValid ? SamplePixel(sim, bottomWorldX, bottomWorldY) : Rgb.Black;

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

        // 被选中的个体：额外描一个环，让"我在看谁"在密集人群里也能认出来。
        if (HighlightedAgentSlot >= 0)
        {
            DrawAgentMarker(buffer, sim, originX, originY, cellsWide, cellsHigh);
        }

        PixelsDrawn = drawn;
        return (int)drawn;
    }

    /// <summary>
    /// 每帧重建可见实体的索引表。
    ///
    /// 覆盖优先级**建筑 &gt; 人 &gt; 动物**：房子不会走，人站在房子上时
    /// 玩家关心的是人（他能动、有状态），因此人盖住建筑？—— 不，反了。
    /// 这里的选择是：**人盖住建筑**。理由是玩家在地图上主要追踪"人在做什么"，
    /// 而且建筑是静态的、位置不变，被短暂盖住不影响理解。
    /// 动物最底层：它们是背景生态，不该盖住任何"有意图的东西"。
    /// </summary>
    private void BuildEntityIndex(Simulation sim)
    {
        _camera.GetVisibleWorldBounds(out _entityMinX, out _entityMinY, out int maxX, out int maxY);

        _entityWidth = maxX - _entityMinX;
        _entityHeight = maxY - _entityMinY;
        EntitiesDrawn = 0;

        if (_entityWidth <= 0 || _entityHeight <= 0)
        {
            _entityWidth = 0;
            _entityHeight = 0;
            return;
        }

        int size = _entityWidth * _entityHeight;
        if (_entityLayer.Length < size)
        {
            _entityLayer = new byte[size];
            _entityColor = new Rgb[size];
            _entitySlot = new int[size];
            _entityAgentState = new byte[size];
        }
        System.Array.Clear(_entityLayer, 0, size);

        SandBoxSim.Core.Environment.World world = sim.World;

        // ---- 1) 建筑（最底层，会被上面两层覆盖）----
        BuildingStore buildings = sim.Buildings;
        for (int k = 0; k < buildings.LiveCount; k++)
        {
            int index = buildings.LiveAt(k);
            if (!buildings.IsAlive(index)) { continue; }

            Rgb color = buildings.StateOf(index) == BuildingState.Complete
                ? (buildings.KindOf(index) == BuildingKind.Farm ? Palette.FarmPlot : Palette.BuildingBase)
                : Palette.BuildingSite;

            if (SetEntity(world, buildings.XOf(index), buildings.YOf(index), LayerBuilding, color, -1))
            {
                EntitiesDrawn++;
            }
        }

        // ---- 2) 动物 ----
        WildlifeStore wildlife = sim.Wildlife;
        for (int k = 0; k < wildlife.LiveCount; k++)
        {
            int index = wildlife.LiveAt(k);
            if (!wildlife.IsAlive(index)) { continue; }
            if (SetEntity(world, wildlife.XOf(index), wildlife.YOf(index), LayerWildlife, Palette.WildlifeColor, -1))
            {
                EntitiesDrawn++;
            }
        }
        // ---- 3) 人（最上层）----
        AgentStore agents = sim.Agents;
        int[] slots = agents.LiveSlotsRaw(out int liveCount);
        for (int k = 0; k < liveCount; k++)
        {
            int slot = slots[k];
            Rgb color = AgentColorOf(agents, slot);
            if (SetEntity(world, agents.XOf(slot), agents.YOf(slot), LayerAgent, color, slot))
            {
                // 顺手记下状态：AiState 叠加层直接读它，
                // 于是那个叠加层也变成 O(1) 查表，而不是每像素扫一遍人口。
                int index = ((agents.YOf(slot) - _entityMinY) * _entityWidth) + (agents.XOf(slot) - _entityMinX);
                _entityAgentState[index] = (byte)agents.StateOf(slot);
                EntitiesDrawn++;
            }
        }
    }

    /// <summary>写入一个实体；返回它是否落在可见范围内。</summary>
    private bool SetEntity(
        SandBoxSim.Core.Environment.World world,
        int x,
        int y,
        byte layer,
        Rgb color,
        int slot)
    {
        if (x < _entityMinX || y < _entityMinY) { return false; }
        if (x >= _entityMinX + _entityWidth || y >= _entityMinY + _entityHeight) { return false; }
        if (!world.IsInBounds(x, y)) { return false; }

        int index = ((y - _entityMinY) * _entityWidth) + (x - _entityMinX);

        // 层号越大越靠上；同层后者覆盖前者（遍历顺序确定，因此结果确定）
        if (layer < _entityLayer[index]) { return false; }

        _entityLayer[index] = layer;
        _entityColor[index] = color;
        _entitySlot[index] = slot;
        return true;
    }

    /// <summary>
    /// 人的颜色编码**当前状态**。玩家要能一眼看出"谁在干活、谁在睡、谁快渴死"。
    /// 身份信息（名字/性格/需求数值）交给检查器面板 —— 那里有空间写清楚。
    /// </summary>
    private static Rgb AgentColorOf(AgentStore agents, int slot)
        => OverlayPalette.AgentColor(agents, slot);

    /// <summary>查表取实体颜色；没有实体时返回 null（用 bool 而不是可空 Rgb，避免装箱）。</summary>
    private bool TryGetEntityColor(SandBoxSim.Core.Environment.World world, int x, int y, out Rgb color)
    {
        color = default;
        if (_entityWidth <= 0 || _entityHeight <= 0) { return false; }
        if (x < _entityMinX || y < _entityMinY) { return false; }
        if (x >= _entityMinX + _entityWidth || y >= _entityMinY + _entityHeight) { return false; }

        int index = ((y - _entityMinY) * _entityWidth) + (x - _entityMinX);
        if (_entityLayer[index] == LayerNone) { return false; }

        color = _entityLayer[index] == LayerAgent && _entitySlot[index] == HighlightedAgentSlot
            ? Palette.AgentElite
            : _entityColor[index];
        return true;
    }

    /// <summary>
    /// 采样一个世界像素的最终颜色：地形色 + 叠加层 + 实体。
    /// 越界返回纯黑（地图外的"世界边缘"）。
    /// </summary>
    private Rgb SamplePixel(Simulation sim, int x, int y)
    {
        SandBoxSim.Core.Environment.World world = sim.World;
        if (!world.IsInBounds(x, y)) { return Rgb.Black; }

        ref readonly Tile tile = ref world.TileAt(x, y);

        Rgb color = Overlay == MapOverlay.None
            ? Palette.Shade(in tile, x, y, _noiseSeed, LightLevel)
            : OverlayColor(sim, in tile, x, y);

        // 实体盖在底色上。**这一步必须放在叠加层之后**，否则切到热力图时人就消失了 ——
        // 而"边看热力图边看人在哪"正是热力图最有用的用法。
        if (TryGetEntityColor(world, x, y, out Rgb entityColor))
        {
            color = entityColor;
        }

        return color;
    }

    /// <summary>给被选中的个体画一圈角标（呼吸闪烁，便于在人群中定位）。</summary>
    private void DrawAgentMarker(
        RenderBuffer buffer,
        Simulation sim,
        int originX,
        int originY,
        int cellsWide,
        int cellsHigh)
    {
        AgentStore agents = sim.Agents;
        int slot = HighlightedAgentSlot;
        if (!agents.IsSlotAlive(slot)) { return; }

        int screenPixelX = agents.XOf(slot) - _camera.ViewLeft;
        int screenPixelY = (agents.YOf(slot) * 2) - _camera.ViewTop;
        int zoom = _camera.Zoom;
        if (zoom <= 0) { return; }

        int cellX = originX + (screenPixelX / zoom);
        int cellY = originY + (screenPixelY / zoom / 2);
        if (cellX < originX || cellX >= originX + cellsWide) { return; }
        if (cellY < originY || cellY >= originY + cellsHigh) { return; }

        Rgb color = Rgb.Lerp(Palette.AgentElite, Palette.SelectionColor, HighlightPhase);
        buffer.SetGlyph(cellX, cellY, '\u25c9', color, Palette.UiBackground, bold: true);
    }

    /// <summary>
    /// 叠加层配色：把"看不见的变量"变成"看得见的颜色"（第 79 条）。
    /// 每个叠加层都用固定的蓝(低)→红(高)渐变，玩家不需要记图例也能看出相对高低。
    ///
    /// 注意这一层**不处理实体**：实体由 <see cref="SamplePixel"/> 统一盖在上面，
    /// 因此任何叠加层下都能同时看到"人在哪"。
    /// </summary>
    private Rgb OverlayColor(Simulation sim, ref readonly Tile tile, int x, int y)
    {
        // 1) "读 Tile 就能决定"的层：与 PNG 快照**共用同一份实现**，
        //    保证报告里的图和屏幕上的图使用完全相同的配色。
        if (OverlayPalette.TryResolveTileOverlay(in tile, Overlay, out Rgb shared))
        {
            return shared;
        }

        // 2) 需要"实体在哪、什么状态"的层：走本渲染器的实体索引表（O(1) 查表）。
        //    这些层的性能路径与 PNG 不同（PNG 每格调用一次，TUI 每像素调用一次），
        //    因此不强行共用，但颜色常量仍然取自 OverlayPalette / Palette。
        switch (Overlay)
        {
            case MapOverlay.Population:
                // 每 chunk 的存活人数。用范围为单格的矩形查询，
                // 因此"这一格上有几个人"这件事与实体索引表无关，可以直接问。
                int people = sim.Agents.CountInRect(x, y, x, y);
                return people <= 0 ? OverlayPalette.EmptyCell : OverlayPalette.Gradient(SimMath.Clamp01(people / 6f));

            case MapOverlay.AiState:
            {
                // 状态来自 BuildEntityIndex 顺手记下的那一字节。
                // （早期写法是"每像素扫一遍人口"，那会让这个叠加层直接把帧率打下去。）
                if (!TryGetEntityIndex(x, y, out int index)) { return OverlayPalette.EmptyCell; }
                if (_entityLayer[index] != LayerAgent) { return OverlayPalette.EmptyCell; }
                return OverlayPalette.AgentStateColor((AgentState)_entityAgentState[index]);
            }

            case MapOverlay.Buildings:
            {
                if (tile.BuildingId <= 0) { return OverlayPalette.EmptyCell; }

                int index = tile.BuildingId - 1;
                if (!sim.Buildings.IsAlive(index)) { return OverlayPalette.EmptyCell; }

                // 已完工的仓库按"装满程度"着色：越满越红，让"仓库快爆了"看得见。
                if (sim.Buildings.StateOf(index) == BuildingState.Complete
                    && sim.Buildings.KindOf(index) == BuildingKind.Storage)
                {
                    float capacity = sim.Storage.CapacityOf(index);
                    float used = sim.Storage.TotalOf(index);
                    float saturation = capacity <= 0f ? 0f : SimMath.Clamp01(used / (capacity * 4f));
                    return OverlayPalette.Gradient(saturation);
                }

                return OverlayPalette.BuildingColor(sim.Buildings, index);
            }

            default:
                return Palette.Shade(in tile, x, y, _noiseSeed, LightLevel);
        }
    }

    /// <summary>把世界坐标换算成实体索引表下标；不可用时返回 false。</summary>
    private bool TryGetEntityIndex(int x, int y, out int index)
    {
        index = 0;
        if (_entityWidth <= 0 || _entityHeight <= 0) { return false; }
        if (x < _entityMinX || y < _entityMinY) { return false; }
        if (x >= _entityMinX + _entityWidth || y >= _entityMinY + _entityHeight) { return false; }

        index = ((y - _entityMinY) * _entityWidth) + (x - _entityMinX);
        return true;
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
