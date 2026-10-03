using SandBoxSim.ConsoleApp.Render;
using SandBoxSim.ConsoleApp.Tui;
using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
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

        /// <summary>
        /// 是否把实体（人/动物/建筑）画进快照。
        ///
        /// 默认开启：归档快照的价值主要在于"能看出这个世界有人、有房子、在干什么"，
        /// 只有方块地形的话它和 M0 的空世界截图没有区别。
        /// </summary>
        public bool DrawEntities = true;
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

        // 实体索引表：快照是**每格**查询"这一格上有没有人/动物/建筑"，
        // 如果每格都线性扫一遍存活实体，整张图的成本是 O(格子数 × 实体数)。
        // 这里先把两者各建一张稀疏索引（key = y * width + x），
        // 于是每个格子退化成几次 O(1) 查表。
        //
        // 为什么不复用 TUI 的实体索引表：那一张是**按可见范围**建的（并随相机变化），
        // 快照要覆盖整张地图，尺寸与生命周期都不同。这里用最朴素的稀疏索引即可，
        // 因为它只跑一次、不进帧循环。
        System.Collections.Generic.Dictionary<int, Rgb>? agentLookup = null;
        System.Collections.Generic.Dictionary<int, Rgb>? wildlifeLookup = null;
        System.Collections.Generic.Dictionary<int, Rgb>? buildingLookup = null;

        if (opt.DrawEntities)
        {
            agentLookup = BuildAgentLookup(sim);
            wildlifeLookup = BuildWildlifeLookup(sim);
            buildingLookup = BuildBuildingLookup(sim, world);
        }

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
                // 底图与叠加层都走 OverlayPalette —— 与 TUI 是**同一份实现**，
                // 因此"报告里的图"和"屏幕上的图"不会讲两个故事。
                Rgb baseColor = OverlayPalette.Resolve(sim, opt.Overlay, x, y, noiseSeed, opt.LightLevel);
                Rgb color = baseColor;
                bool isEntity = false;

                // 实体盖在底图之上（与 TUI 相同的优先级：建筑 < 动物 < 人）。
                // 顺序反过来写，于是"最后写入的优先级最高"这件事只在一处表达。
                if (opt.DrawEntities)
                {
                    int key = (y * world.Width) + x;
                    if (buildingLookup!.TryGetValue(key, out Rgb buildingColor)) { color = buildingColor; isEntity = true; }
                    if (wildlifeLookup!.TryGetValue(key, out Rgb wildlifeColor)) { color = wildlifeColor; isEntity = true; }
                    if (agentLookup!.TryGetValue(key, out Rgb agentColor)) { color = agentColor; isEntity = true; }
                }

                int baseX = x * scale;
                int baseY = (y * scale) + opt.HeaderHeight;

                // 实体用**居中的方块**而不是铺满整格。
                //
                // 尺寸取值踩过两次：铺满整格让人和地形一样大（看起来像"地形变了"），
                // 缩到 1/3 格又小到几乎看不见（实测 8px 格子上 2px 的点在缩略图里消失）。
                // 取半格宽是"能一眼看到"与"不遮住地形"之间实际可用的折中点。
                int dotSize = System.Math.Max(2, scale / 2);
                if (dotSize > scale) { dotSize = scale; }
                int dotStart = (scale - dotSize) / 2;

                for (int dy = 0; dy < scale; dy++)
                {
                    for (int dx = 0; dx < scale; dx++)
                    {
                        // 实体只在中心区域内画成实体色，边缘仍是底图色 —— 于是方块是"贴"在地形上的
                        Rgb pixelColor = color;
                        if (isEntity && scale >= 3)
                        {
                            bool inDot = dx >= dotStart && dx < dotStart + dotSize
                                      && dy >= dotStart && dy < dotStart + dotSize;
                            if (!inDot) { pixelColor = baseColor; }
                        }

                        // 网格线：只在格子边界画，帮助人眼数格子（调试空间索引时很有用）
                        bool gridLine = opt.DrawGrid && scale >= 4 && (dx == 0 || dy == 0);
                        image.SetPixel(baseX + dx, baseY + dy, gridLine ? Rgb.DarkGray : pixelColor);
                    }
                }
            }
        }

        return image;
    }

    /// <summary>这一格上有没有建筑（含工地）。</summary>
    private static System.Collections.Generic.Dictionary<int, Rgb> BuildBuildingLookup(
        Simulation sim,
        SandBoxSim.Core.Environment.World world)
    {
        var lookup = new System.Collections.Generic.Dictionary<int, Rgb>();
        BuildingStore buildings = sim.Buildings;

        // 走 Slot 顺序而不是存活列表顺序：**结果确定**（同一状态必得同一张图），
        // 而字典的写入顺序会影响"同格冲突时谁赢"。虽然建筑不会同格重叠，
        // 但把"确定性"写成不依赖巧合更安全。
        for (int index = 0; index < buildings.Capacity; index++)
        {
            if (!buildings.IsAlive(index)) { continue; }
            int key = (buildings.YOf(index) * world.Width) + buildings.XOf(index);
            lookup[key] = OverlayPalette.BuildingColor(buildings, index);
        }
        return lookup;
    }

    /// <summary>人在哪、什么状态色（与 TUI 共用 <see cref="OverlayPalette.AgentColor"/>）。</summary>
    private static System.Collections.Generic.Dictionary<int, Rgb> BuildAgentLookup(Simulation sim)
    {
        var lookup = new System.Collections.Generic.Dictionary<int, Rgb>();
        AgentStore agents = sim.Agents;
        int[] slots = agents.LiveSlotsRaw(out int liveCount);
        int width = sim.World.Width;

        for (int k = 0; k < liveCount; k++)
        {
            int slot = slots[k];
            int key = (agents.YOf(slot) * width) + agents.XOf(slot);
            lookup[key] = OverlayPalette.AgentColor(agents, slot);
        }
        return lookup;
    }

    /// <summary>动物在哪（统一颜色：动物没有状态可看，玩家关心的是"猎场在哪"）。</summary>
    private static System.Collections.Generic.Dictionary<int, Rgb> BuildWildlifeLookup(Simulation sim)
    {
        var lookup = new System.Collections.Generic.Dictionary<int, Rgb>();
        WildlifeStore wildlife = sim.Wildlife;
        int width = sim.World.Width;

        for (int k = 0; k < wildlife.LiveCount; k++)
        {
            int index = wildlife.LiveAt(k);
            if (!wildlife.IsAlive(index)) { continue; }
            int key = (wildlife.YOf(index) * width) + wildlife.XOf(index);
            lookup[key] = Palette.WildlifeColor;
        }
        return lookup;
    }
}
