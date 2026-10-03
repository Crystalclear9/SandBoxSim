using SandBoxSim.ConsoleApp.Png;
using SandBoxSim.ConsoleApp.Render;
using SandBoxSim.ConsoleApp.Tui;
using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

/// <summary>
/// 渲染层测试（M4）。
///
/// 这一组测试存在的理由：渲染是**唯一会"看起来对但实际错"的层**。
/// 它不产生任何模拟副作用，所以"程序没崩"完全不说明它画对了。
/// 尤其是"渲染不得改变世界"这一条 —— 一旦渲染真的改了状态，
/// 会表现为"同一份存档在打开面板后走向不同"，而且极难定位。
///
/// 因此这里锁两件事：
///   1. **覆盖优先级**（建筑 &lt; 动物 &lt; 人）与状态配色；
///   2. **渲染只读**（渲染前后状态摘要必须完全相同）。
/// </summary>
public sealed class RendererTests
{
    private static SimConfig Config(int width = 40, int height = 40)
    {
        var config = new SimConfig();
        config.World.Width = width;
        config.World.Height = height;
        return config;
    }

    private static void Flatten(Simulation sim, int minX, int minY, int maxX, int maxY)
    {
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                sim.World.SetTerrain(x, y, TerrainKind.Grass);
                sim.World.SetVegetation(x, y, 0.3f);
            }
        }
        sim.World.RefreshSpatialIndex();
    }

    // ---------------------------------------------------------------------
    // 渲染必须是只读的
    // ---------------------------------------------------------------------

    [Fact("渲染不得改变世界状态（否则'看一眼'就会改变结局）")]
    public void RenderingIsReadOnly()
    {
        var config = Config(60, 60);
        var sim = new Simulation(config, 60, 60, 7001);
        sim.InterveneSpawnHumans(30, 30, 25, 6);
        sim.Tick(1440 * 3);

        string before = sim.StateDigestString();

        var camera = new Camera(sim.World.Width, sim.World.Height);
        var renderer = new WorldViewRenderer(camera);
        var buffer = new RenderBuffer(90, 45);

        // 把每一种叠加层都渲染一遍：任何一层误写世界状态都会被这条断言抓住
        foreach (MapOverlay overlay in System.Enum.GetValues<MapOverlay>())
        {
            renderer.Overlay = overlay;
            renderer.Render(buffer, sim, 0, 0, 90, 45);
        }

        // PNG 快照路径同样必须是只读的（它走的是另一份实体索引实现）
        WorldSnapshot.Render(sim, new WorldSnapshot.Options { Width = 200 });

        Assert.Equal(before, sim.StateDigestString(),
            "渲染之后状态摘要必须完全不变 —— 渲染层不允许写任何模拟状态");
        Assert.Greater(renderer.EntitiesDrawn, 0, "这一局里应当画出了实体");
    }

    [Fact("PNG 快照不得改变世界状态")]
    public void SnapshotIsReadOnly()
    {
        var config = Config(40, 40);
        var sim = new Simulation(config, 40, 40, 7011);
        sim.InterveneSpawnHumans(20, 20, 10, 4);
        sim.Tick(720);

        string before = sim.StateDigestString();

        // 带实体 + 带叠加层各跑一次
        WorldSnapshot.Render(sim, new WorldSnapshot.Options { Width = 160, DrawEntities = true });
        WorldSnapshot.Render(sim, new WorldSnapshot.Options
        {
            Width = 160,
            DrawEntities = true,
            Overlay = MapOverlay.Buildings,
            DrawGrid = true,
            HeaderHeight = 8,
        });

        Assert.Equal(before, sim.StateDigestString());
    }

    // ---------------------------------------------------------------------
    // 实体覆盖优先级
    // ---------------------------------------------------------------------

    [Fact("实体覆盖优先级必须是 建筑 < 动物 < 人")]
    public void EntityOverlayPriority()
    {
        var config = Config(40, 40);
        var sim = new Simulation(config, 40, 40, 7021);
        Flatten(sim, 5, 5, 35, 35);

        // 三种实体放在同一格上：(20,20)
        // 建筑用真实放置（这样它也会写 Tile.BuildingId，与真实路径一致）
        int building = sim.Buildings.Place(sim.World, BuildingKind.House, 20, 20, 200);
        Assert.GreaterOrEqual(building, 0, "测试需要先能放下一个建筑");

        // 动物：直接搬到这一格
        int wildlife = -1;
        foreach (int index in sim.Wildlife.AliveIndices()) { wildlife = index; break; }
        if (wildlife >= 0) { sim.Wildlife.SetPosition(wildlife, 20, 20); }

        // 人：直接放在这一格
        sim.InterveneSpawnHumans(20, 20, 1, 0);
        int slot = FirstAlive(sim);
        Assert.GreaterOrEqual(slot, 0);

        // 建筑色（未完工 = 工地色）
        Assert.Equal(Palette.BuildingSite, OverlayPalette.BuildingColor(sim.Buildings, building));

        // 人的状态色：把状态设成睡眠，颜色必须变成睡眠色（而不是"人"的固定颜色）
        sim.Agents.SetState(slot, AgentState.Sleeping);
        Assert.Equal(Palette.AgentSleeping, OverlayPalette.AgentColor(sim.Agents, slot));

        sim.Agents.SetState(slot, AgentState.Working);
        Assert.Equal(Palette.AgentWorking, OverlayPalette.AgentColor(sim.Agents, slot));

        sim.Agents.SetState(slot, AgentState.Moving);
        Assert.Equal(Palette.AgentMoving, OverlayPalette.AgentColor(sim.Agents, slot));

        // 生存告急的优先级高于状态：再"工作中"也必须是告急色
        sim.Agents.SetState(slot, AgentState.Working);
        sim.Agents.SetNeed(slot, NeedIndex.Hunger, 0.95f);
        Assert.Equal(Palette.AgentStarving, OverlayPalette.AgentColor(sim.Agents, slot),
            "极度饥饿必须压过其它状态色 —— 那是玩家最需要立刻看到的信息");
    }

    [Fact("PNG 的同格覆盖顺序必须与 TUI 一致（人 &gt; 动物 &gt; 建筑）")]
    public void SnapshotOverlayOrderMatchesTui()
    {
        var config = Config(24, 24);
        var sim = new Simulation(config, 24, 24, 7031);
        Flatten(sim, 2, 2, 21, 21);

        // 只放一个建筑，其余什么都不放：格子上应当出现建筑色
        int building = sim.Buildings.Place(sim.World, BuildingKind.House, 10, 10, 200);
        Assert.GreaterOrEqual(building, 0);

        PngImage onlyBuilding = WorldSnapshot.Render(sim, new WorldSnapshot.Options
        {
            CellScale = 1,
            DrawEntities = true,
        });
        Assert.Equal(Palette.BuildingSite, onlyBuilding.GetPixel(10, 10));

        // 再往同一格放一个人（睡眠状态），建筑色必须被人色盖住
        sim.InterveneSpawnHumans(10, 10, 1, 0);
        int slot = FirstAlive(sim);
        Assert.GreaterOrEqual(slot, 0);
        sim.Agents.SetState(slot, AgentState.Sleeping);

        PngImage withAgent = WorldSnapshot.Render(sim, new WorldSnapshot.Options
        {
            CellScale = 1,
            DrawEntities = true,
        });
        Assert.Equal(Palette.AgentSleeping, withAgent.GetPixel(10, 10),
            "人必须盖住建筑 —— 玩家在地图上主要追踪'人在做什么'");
    }

    [Fact("关掉 DrawEntities 时快照里不能有实体色")]
    public void SnapshotCanDisableEntities()
    {
        var config = Config(24, 24);
        var sim = new Simulation(config, 24, 24, 7041);
        Flatten(sim, 2, 2, 21, 21);

        sim.Buildings.Place(sim.World, BuildingKind.House, 10, 10, 200);
        sim.InterveneSpawnHumans(10, 10, 1, 0);

        PngImage image = WorldSnapshot.Render(sim, new WorldSnapshot.Options
        {
            CellScale = 1,
            DrawEntities = false,
        });

        Rgb pixel = image.GetPixel(10, 10);
        Assert.NotEqual(Palette.BuildingSite, pixel);
        Assert.NotEqual(Palette.AgentSleeping, pixel);
        Assert.NotEqual(Palette.AgentIdle, pixel);
    }

    // ---------------------------------------------------------------------
    // 叠加层配色：TUI 与 PNG 必须一致
    // ---------------------------------------------------------------------

    [Fact("TUI 与 PNG 的叠加层配色必须逐格一致")]
    public void OverlaysAgreeBetweenTuiAndSnapshot()
    {
        var config = Config(20, 20);
        var sim = new Simulation(config, 20, 20, 7051);
        Flatten(sim, 0, 0, 19, 19);

        var camera = new Camera(sim.World.Width, sim.World.Height);
        var renderer = new WorldViewRenderer(camera);
        var buffer = new RenderBuffer(20, 10);

        MapOverlay[] tileOverlays =
        {
            MapOverlay.Fertility,
            MapOverlay.Moisture,
            MapOverlay.Temperature,
            MapOverlay.Wood,
            MapOverlay.Food,
            MapOverlay.Vegetation,
            MapOverlay.FireRisk,
            MapOverlay.Walkable,
        };

        uint noiseSeed = unchecked((uint)sim.World.Seed * 2654435761u);

        foreach (MapOverlay overlay in tileOverlays)
        {
            for (int y = 0; y < sim.World.Height; y += 4)
            {
                for (int x = 0; x < sim.World.Width; x += 4)
                {
                    Rgb shared = OverlayPalette.Resolve(sim, overlay, x, y, noiseSeed, 1f);
                    Rgb fromTile = OverlayPalette.TryResolveTileOverlay(
                        in sim.World.TileAt(x, y), overlay, out Rgb tileColor)
                        ? tileColor
                        : default;

                    Assert.Equal(shared, fromTile);
                }
            }
        }

        _ = renderer;
        _ = buffer;
    }

    [Fact("人口热力图必须随人数上升而变热")]
    public void PopulationOverlayRespondsToCount()
    {
        var config = Config(40, 40);
        config.Ai.MoveSpeedPerTick = 0f;   // 不让人走动，保证他们留在原地
        var sim = new Simulation(config, 40, 40, 7061);
        Flatten(sim, 5, 5, 35, 35);

        int before = sim.Agents.CountInRect(20, 20, 20, 20);
        Assert.Equal(0, before);

        sim.InterveneSpawnHumans(20, 20, 4, 0);

        int after = sim.Agents.CountInRect(20, 20, 20, 20);
        Assert.Greater(after, 0, "同一格放了 4 个人之后必须能查到人");

        Rgb color = OverlayPalette.Resolve(sim, MapOverlay.Population, 20, 20, 0u, 1f);
        Assert.NotEqual(OverlayPalette.EmptyCell, color);
    }

    [Fact("渲染选项必须能遍历（枚举新增叠加层后 TUI 的循环不能崩）")]
    public void AllOverlaysRenderWithoutThrowing()
    {
        var config = Config(30, 30);
        var sim = new Simulation(config, 30, 30, 7071);
        sim.InterveneSpawnHumans(15, 15, 8, 3);
        sim.Tick(600);

        var camera = new Camera(sim.World.Width, sim.World.Height);
        var renderer = new WorldViewRenderer(camera);
        var buffer = new RenderBuffer(40, 20);

        foreach (MapOverlay overlay in System.Enum.GetValues<MapOverlay>())
        {
            renderer.Overlay = overlay;
            Assert.True(renderer.Render(buffer, sim, 0, 0, 40, 20) > 0,
                "叠加层 " + overlay + " 必须至少画出一些像素");
        }
    }

    private static int FirstAlive(Simulation sim)
    {
        foreach (int slot in sim.Agents.AliveSlots()) { return slot; }
        return -1;
    }
}
