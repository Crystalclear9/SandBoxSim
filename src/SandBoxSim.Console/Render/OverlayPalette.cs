using SandBoxSim.ConsoleApp.Tui;
using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.ConsoleApp.Render;

/// <summary>
/// 叠加层配色（第 79 节 Debug Overlay）。
///
/// **为什么把这件事单独抽出来**：TUI 与 PNG 快照必须给出**逐像素一致**的颜色，
/// 否则"报告里的图"和"屏幕上的图"会讲两个故事，观察结论就无法互证 ——
/// 而观察结论的可复现性正是这个项目的核心承诺（第 77 / 91 条）。
/// 抽成一份实现之后，"两边一致"不再依赖人记得同步改两处。
///
/// 命名约定：`Resolve` 只负责**底图**（地形或热力值），实体覆盖由调用方负责 ——
/// 因为 TUI 用半方块逐像素采样，而 PNG 按格子放大，两者的覆盖方式不同。
/// </summary>
public static class OverlayPalette
{
    /// <summary>没有叠加层（或未知叠加层）时的底图色。</summary>
    public static Rgb Terrain(SandBoxSim.Core.Environment.World world, int x, int y, uint noiseSeed, float lightLevel)
    {
        ref readonly Tile tile = ref world.TileAt(x, y);
        return Palette.Shade(in tile, x, y, noiseSeed, lightLevel);
    }

    /// <summary>
    /// 叠加层配色，**但只处理"读 Tile/World 就能决定"的那些层**。
    ///
    /// 为什么留出这一层：`Population` / `AiState` / `Buildings` 三层要么需要
    /// 逐格人口统计、要么需要一份"实体在哪、什么状态"的索引表。
    /// TUI 用 <see cref="WorldViewRenderer"/> 的实体索引表（O(1) 查表）；
    /// PNG 是每格调用一次，用 <see cref="Resolve"/> 的朴素实现就够。
    /// 把"两者必须逐像素一致"的部分（地形/资源/热力值）收敛到这里，
    /// 把"性能路径不同"的部分留给各自实现 —— 这样既不会出现两套配色，
    /// 也不用为了统一而牺牲 TUI 的帧率。
    /// </summary>
    public static bool TryResolveTileOverlay(
        ref readonly Tile tile,
        MapOverlay overlay,
        out Rgb color)
    {
        switch (overlay)
        {
            case MapOverlay.Fertility:
                color = Gradient(tile.Fertility);
                return true;

            case MapOverlay.Moisture:
                color = Gradient(tile.Moisture);
                return true;

            case MapOverlay.Temperature:
                color = Gradient(tile.Temperature);
                return true;

            case MapOverlay.Wood:
                color = tile.Resource.Kind == ResourceKind.Wood ? Gradient(tile.Resource.Fraction) : EmptyCell;
                return true;

            case MapOverlay.Food:
                color = tile.Resource.Kind == ResourceKind.Food ? Gradient(tile.Resource.Fraction) : EmptyCell;
                return true;

            case MapOverlay.Vegetation:
                color = Gradient(tile.Vegetation);
                return true;

            case MapOverlay.FireRisk:
            {
                // 火险 = 干燥 × 植被 × 天气系数（第 43 节公式的即时可视化版本）
                float dryness = 1f - tile.Moisture;
                float fuel = TerrainInfo.IsVegetation(tile.Terrain) ? tile.Vegetation : 0f;
                color = Gradient(dryness * fuel);
                return true;
            }

            case MapOverlay.Walkable:
                if (tile.Terrain == TerrainKind.Water) { color = new Rgb(20, 30, 60); return true; }
                color = tile.Walkable ? new Rgb(40, 120, 70) : new Rgb(120, 40, 40);
                return true;

            default:
                color = default;
                return false;
        }
    }

    /// <summary>
    /// 按叠加层取底图色（PNG 快照用；每格调用一次）。
    ///
    /// 注意这里**不处理实体**：调用方负责把实体盖在返回值之上，
    /// 于是"在任何热力图下都能同时看到人在哪"这件事对 TUI 与 PNG 同时成立。
    /// </summary>
    public static Rgb Resolve(
        Simulation sim,
        MapOverlay overlay,
        int x,
        int y,
        uint noiseSeed,
        float lightLevel)
    {
        SandBoxSim.Core.Environment.World world = sim.World;
        if (!world.IsInBounds(x, y)) { return Rgb.Black; }

        ref readonly Tile tile = ref world.TileAt(x, y);

        if (overlay == MapOverlay.None)
        {
            return Palette.Shade(in tile, x, y, noiseSeed, lightLevel);
        }

        if (TryResolveTileOverlay(in tile, overlay, out Rgb color)) { return color; }

        switch (overlay)
        {
            case MapOverlay.Population:
            {
                int people = sim.Agents.CountInRect(x, y, x, y);
                return people <= 0 ? EmptyCell : Gradient(SimMath.Clamp01(people / 6f));
            }

            case MapOverlay.AiState:
            {
                // 逐格线性找"这一格上的人"。
                //
                // PNG 是**每格调用一次**（不是逐像素），人口规模下线性查找完全够用。
                // 走 LiveSlotsRaw 而不是 AliveSlots()：避免每格分配一个迭代器状态机。
                int[] slots = sim.Agents.LiveSlotsRaw(out int liveCount);
                for (int k = 0; k < liveCount; k++)
                {
                    int slot = slots[k];
                    if (sim.Agents.XOf(slot) != x || sim.Agents.YOf(slot) != y) { continue; }
                    return AgentStateColor(sim.Agents.StateOf(slot));
                }
                return EmptyCell;
            }

            case MapOverlay.Buildings:
            {
                if (tile.BuildingId <= 0) { return EmptyCell; }

                int index = tile.BuildingId - 1;
                if (!sim.Buildings.IsAlive(index)) { return EmptyCell; }

                if (sim.Buildings.StateOf(index) != BuildingState.Complete) { return Palette.BuildingSite; }

                if (sim.Buildings.KindOf(index) == BuildingKind.Storage)
                {
                    float capacity = sim.Storage.CapacityOf(index);
                    float used = sim.Storage.TotalOf(index);
                    float saturation = capacity <= 0f ? 0f : SimMath.Clamp01(used / (capacity * 4f));
                    return Gradient(saturation);
                }

                if (sim.Buildings.KindOf(index) == BuildingKind.Farm) { return Palette.FarmPlot; }
                return Palette.BuildingBase;
            }

            default:
                return Palette.Shade(in tile, x, y, noiseSeed, lightLevel);
        }
    }

    /// <summary>无内容的叠加层底色（近黑，让"有东西"的格子跳出来）。</summary>
    public static readonly Rgb EmptyCell = new Rgb(18, 20, 26);

    /// <summary>人的状态色（TUI 与 PNG 共用；PNG 里没有半方块，因此用实心格表示）。</summary>
    public static Rgb AgentStateColor(AgentState state)
    {
        switch (state)
        {
            case AgentState.Working: return new Rgb(230, 200, 90);
            case AgentState.Moving: return new Rgb(90, 170, 240);
            case AgentState.Sleeping: return new Rgb(120, 110, 190);
            case AgentState.Eating: return new Rgb(110, 210, 120);
            default: return new Rgb(170, 170, 170);
        }
    }

    /// <summary>通用的"低=蓝 → 中=绿 → 高=红"渐变，供所有热力图复用。</summary>
    public static Rgb Gradient(float value01)
    {
        float v = SimMath.Clamp01(value01);
        Rgb low = new Rgb(24, 40, 96);
        Rgb mid = new Rgb(40, 130, 96);
        Rgb high = new Rgb(228, 88, 64);
        if (v < 0.5f) { return Rgb.Lerp(low, mid, v * 2f); }
        return Rgb.Lerp(mid, high, (v - 0.5f) * 2f);
    }

    /// <summary>
    /// 人的颜色编码**当前状态**（不是身份）。
    /// 玩家要能一眼看出"谁在干活、谁在睡、谁快渴死"；
    /// 身份信息（名字/性格/需求数值）交给检查器面板 —— 那里有空间写清楚。
    ///
    /// 生存告急的优先级高于一切：那是玩家最需要立刻看到的信息。
    /// </summary>
    public static Rgb AgentColor(AgentStore agents, int slot)
    {
        if (agents.HungerOf(slot) >= 0.85f || agents.ThirstOf(slot) >= 0.9f) { return Palette.AgentStarving; }

        switch (agents.StateOf(slot))
        {
            case AgentState.Sleeping: return Palette.AgentSleeping;
            case AgentState.Eating: return Palette.AgentEating;
            case AgentState.Moving: return Palette.AgentMoving;
            case AgentState.Working: return Palette.AgentWorking;
            default: return Palette.AgentIdle;
        }
    }

    /// <summary>建筑的颜色（建成 / 工地 / 农田 / 仓库饱和度）。</summary>
    public static Rgb BuildingColor(BuildingStore buildings, int index)
    {
        if (buildings.StateOf(index) != BuildingState.Complete) { return Palette.BuildingSite; }
        if (buildings.KindOf(index) == BuildingKind.Farm) { return Palette.FarmPlot; }
        return Palette.BuildingBase;
    }
}
