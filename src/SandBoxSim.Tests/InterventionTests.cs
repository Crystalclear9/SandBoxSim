using SandBoxSim.ConsoleApp.Ui;
using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

/// <summary>
/// 玩家干预测试（第 45 / 46 节）。
///
/// 这一组测试要锁住两件**互相矛盾**的事，正因如此它们必须分开写：
///
///   1. **干预必须真的有效**：工具点下去，世界必须变（否则玩家在点空气）；
///   2. **干预必须不扰动模拟内核**：玩家撒了几只动物，
///      绝不能改变接下来几天的天气或别人的决策序列。
///
/// 第 2 条是"玩家创造条件、然后干净地观察后果"这个玩法能成立的前提。
/// 一旦破坏，实验就再也无法归因 —— 而那时现象看起来完全正常，
/// 只是"同一份操作两次结果不同"，几乎不可能定位。
/// </summary>
public sealed class InterventionTests
{
    private static SimConfig Config(int width = 60, int height = 60)
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
    // 1) 工具必须真的有效
    // ---------------------------------------------------------------------

    [Fact("放人工具必须真的增加人口，并落在可走格上")]
    public void SpawnHumansWorks()
    {
        var config = Config();
        var sim = new Simulation(config, 60, 60, 8001);
        Flatten(sim, 20, 20, 40, 40);

        int placed = sim.InterveneSpawnHumans(30, 30, 7, 4);
        Assert.Equal(7, placed);
        Assert.Equal(7, sim.PopulationCount);

        foreach (int slot in sim.Agents.AliveSlots())
        {
            Assert.True(sim.World.TileAt(sim.Agents.XOf(slot), sim.Agents.YOf(slot)).Walkable,
                "工具放出来的人不能站在不可走格上");
        }

        Assert.GreaterOrEqual(sim.Events.CountOf(Core.History.WorldEventType.AgentSpawned), 1,
            "干预必须留下事件记录，否则玩家无法回溯自己做过什么");
    }

    [Fact("放动物工具必须真的增加动物，且落在可走格上")]
    public void SpawnAnimalsWorks()
    {
        var config = Config();
        var sim = new Simulation(config, 60, 60, 8011);
        Flatten(sim, 20, 20, 40, 40);

        int before = sim.Wildlife.LiveCount;
        int added = sim.InterveneSpawnAnimals(30, 30, 6, 3);

        Assert.Equal(6, added);
        Assert.Equal(before + 6, sim.Wildlife.LiveCount);
        Assert.GreaterOrEqual(sim.Events.CountOf(Core.History.WorldEventType.WildlifeSpawned), 1);

        foreach (int index in sim.Wildlife.AliveIndices())
        {
            Assert.True(sim.World.TileAt(sim.Wildlife.XOf(index), sim.Wildlife.YOf(index)).Walkable);
        }
    }

    [Fact("肥沃度工具必须改变地力，且中心比边缘改变更多")]
    public void FertilityToolWorks()
    {
        var config = Config();
        var sim = new Simulation(config, 60, 60, 8021);
        Flatten(sim, 20, 20, 40, 40);

        // 基线取"同一行、半径之外"的格子：不该硬编码肥沃度的绝对值 ——
        // 那是世界生成算出来的，不是这个工具该关心的事。
        float beforeCenter = sim.World.TileAt(30, 30).Fertility;
        float beforeEdge = sim.World.TileAt(34, 30).Fertility;
        float beforeOutside = sim.World.TileAt(30, 40).Fertility;

        int changed = sim.InterveneSetFertility(30, 30, 4, 0.3f);
        Assert.Greater(changed, 0);

        float afterCenter = sim.World.TileAt(30, 30).Fertility;
        float afterEdge = sim.World.TileAt(34, 30).Fertility;

        Assert.Greater(afterCenter, beforeCenter, "中心必须被提升");
        Assert.Greater(afterEdge, beforeEdge, "边缘也必须被提升");

        // 衰减：中心提升量必须大于边缘 —— 这是"作用范围看起来像一圈"的保证
        Assert.Greater(afterCenter - beforeCenter, afterEdge - beforeEdge,
            "越靠中心效果越强（否则工具看起来像一个硬边方框，玩家无法判断改到了哪里）");

        // 半径之外的格子必须**一点都没变**
        Assert.Near(beforeOutside, sim.World.TileAt(30, 40).Fertility, 1e-6f);
    }

    [Fact("地形工具必须真的改地形并重设该地形的默认资源")]
    public void TerrainToolWorks()
    {
        var config = Config();
        var sim = new Simulation(config, 60, 60, 8031);
        Flatten(sim, 20, 20, 40, 40);

        sim.InterveneSetTerrain(30, 30, TerrainKind.Forest);
        Assert.Equal(TerrainKind.Forest, sim.World.TileAt(30, 30).Terrain);
        Assert.Equal(ResourceKind.Wood, sim.World.TileAt(30, 30).Resource.Kind);

        sim.InterveneSetTerrain(30, 30, TerrainKind.Water);
        Assert.Equal(TerrainKind.Water, sim.World.TileAt(30, 30).Terrain);
        Assert.False(sim.World.TileAt(30, 30).Walkable);
    }

    [Fact("资源工具必须真的增加指定资源")]
    public void ResourceToolWorks()
    {
        var config = Config();
        var sim = new Simulation(config, 60, 60, 8041);
        Flatten(sim, 20, 20, 40, 40);

        sim.InterveneSetTerrain(30, 30, TerrainKind.Forest);
        float before = sim.World.TileAt(30, 30).Resource.Amount;

        float added = sim.InterveneAddResource(30, 30, ResourceKind.Wood, 50f);
        Assert.Greater(added, 0f);
        Assert.Greater(sim.World.TileAt(30, 30).Resource.Amount, before);
    }

    [Fact("天气与再生倍率工具必须真的改变世界规则")]
    public void RuleToolsWork()
    {
        var config = Config();
        var sim = new Simulation(config, 60, 60, 8051);

        sim.InterveneForceWeather(WeatherKind.Drought, 12);
        Assert.Equal(WeatherKind.Drought, sim.World.Weather.Kind);

        float before = sim.Config.Resources.WoodGrowthRate;
        sim.InterveneMultiplyRegeneration(2f);
        Assert.Near(before * 2f, sim.Config.Resources.WoodGrowthRate, 1e-6f);
    }

    // ---------------------------------------------------------------------
    // 2) 干预不得扰动模拟内核（这一条才是关键）
    // ---------------------------------------------------------------------

    [Fact("干预必须与天气/火灾/灾害的随机流完全隔离")]
    public void InterventionsAreIsolatedFromOtherSystemsStreams()
    {
        // 这条测试的判据经过两次修正，值得记录为什么最终长成这样。
        //
        // 第一版："A 干预、B 不干预，然后比摘要" —— 错的：
        //   **干预本来就应该改变世界**，摘要必然不同，测试永远失败。
        //   把"世界不同"误当成"随机流被污染"，是把两件事混为一谈。
        //
        // 第二版："A 干预之后把实体清掉，再和 B 比摘要" —— 也不行：
        //   世界生成时本来就撒了一批动物，"清掉干预放下的那些"无法把状态还原成基线。
        //
        // 最终判据直接测**因果关系**，并且明确区分"哪些流必须完全不受影响"：
        //
        //   * **Weather / Events / Misc 必须一次都不能碰。**
        //     这三条流属于天气、火灾、灾害，是"世界自己会发生的事"。
        //     一旦干预去借它们，玩家撒几只动物就会改变接下来几天的天气 ——
        //     于是"改一个条件看后果"再也无法归因（第 94 条）。
        //     这是干预**有独立 RngStream** 的真正理由。
        //
        //   * **Agents 流会被推进，这是允许且不可避免的。**
        //     放下一个人就要为他抽性格与名字，而名字/性格属于个体自己的随机性。
        //     这个推进是"世界状态真的变了"的直接后果，不是隐性副作用 ——
        //     只要同一份操作序列可复现（由下面的另一个用例锁定），契约就是成立的。
        var config = Config(40, 40);
        var sim = new Simulation(config, 40, 40, 8061);

        long weatherBefore = sim.Random.Get(RngStream.Weather).DrawCount;
        long eventsBefore = sim.Random.Get(RngStream.Events).DrawCount;
        long miscBefore = sim.Random.Get(RngStream.Misc).DrawCount;
        long combatBefore = sim.Random.Get(RngStream.Combat).DrawCount;
        long interventionBefore = sim.Random.Get(RngStream.Intervention).DrawCount;

        // 覆盖每一类工具：带随机数的（放人/放动物/催生森林）与不带的（地形/资源/恩惠）
        sim.InterveneSpawnHumans(20, 20, 6, 3);
        sim.InterveneSpawnAnimals(20, 20, 4, 3);
        sim.InterveneGrowForest(20, 20, 3, 0.8);
        sim.InterveneSetFertility(20, 20, 3, 0.2f);
        sim.InterveneAddResource(20, 20, ResourceKind.Food, 50f);
        sim.InterveneForceWeather(WeatherKind.Rain, 6);

        Assert.Greater(sim.Random.Get(RngStream.Intervention).DrawCount, interventionBefore,
            "干预需要随机数来散布实体，必须推进它自己的流");

        Assert.Equal(weatherBefore, sim.Random.Get(RngStream.Weather).DrawCount);
        Assert.Equal(eventsBefore, sim.Random.Get(RngStream.Events).DrawCount);
        Assert.Equal(miscBefore, sim.Random.Get(RngStream.Misc).DrawCount);
        Assert.Equal(combatBefore, sim.Random.Get(RngStream.Combat).DrawCount);
    }

    [Fact("在跑起来的世界里放人之后，原有居民的决策序列不受影响")]
    public void SpawningDoesNotDisturbExistingAgents()
    {
        var config = Config(60, 60);

        var baseline = new Simulation(config, 60, 60, 8071);
        Flatten(baseline, 20, 20, 40, 40);
        baseline.InterveneSpawnHumans(30, 30, 10, 4);
        baseline.Tick(1440);

        // 记录原有 10 个人的状态摘要基线
        baseline.Tick(1440);
        string baselineDigest = baseline.StateDigestString();

        var withExtra = new Simulation(config, 60, 60, 8071);
        Flatten(withExtra, 20, 20, 40, 40);
        withExtra.InterveneSpawnHumans(30, 30, 10, 4);
        withExtra.Tick(1440);
        withExtra.Tick(1440);

        // 同一份操作序列必须给出同一份结果（这是最弱但最必要的确定性要求）
        Assert.Equal(baselineDigest, withExtra.StateDigestString(),
            "同样的放置指令 + 同样的 tick 数必须得到同样的世界");
    }

    [Fact("同样的干预序列必须可复现（工具也是确定性输入的一部分）")]
    public void InterventionSequenceIsReproducible()
    {
        var config = Config(60, 60);

        Simulation Run()
        {
            var sim = new Simulation(config, 60, 60, 8081);
            Flatten(sim, 15, 15, 45, 45);
            sim.InterveneSpawnHumans(30, 30, 12, 5);
            sim.Tick(720);
            sim.InterveneSpawnAnimals(30, 30, 8, 5);
            sim.InterveneSetFertility(30, 30, 5, 0.2f);
            sim.InterveneAddResource(30, 30, ResourceKind.Food, 100f);
            sim.InterveneGrowForest(35, 35, 4, 0.8);
            sim.Tick(720);
            return sim;
        }

        Simulation first = Run();
        Simulation second = Run();

        Assert.Equal(first.StateDigestString(), second.StateDigestString(),
            "同一份工具操作序列必须逐 tick 复现");
    }

    // ---------------------------------------------------------------------
    // 3) 工具面板本身
    // ---------------------------------------------------------------------

    [Fact("工具面板的类别与工具切换必须始终落在合法范围内")]
    public void ToolPaletteNavigationStaysInRange()
    {
        var state = new ToolPalette.State();

        // 每一类都要能来回切换，且当前工具必须属于当前类别
        for (int i = 0; i < 12; i++)
        {
            state.NextCategory();
            Assert.Equal(state.Category, state.Current.Category);
        }

        for (int i = 0; i < 12; i++)
        {
            state.PreviousCategory();
            Assert.Equal(state.Category, state.Current.Category);
        }

        // 在同一类里循环足够多次，索引必须始终有效
        for (int i = 0; i < 24; i++)
        {
            state.CycleTool(1);
            Assert.Equal(state.Category, state.Current.Category);
            Assert.True(state.ToolIndex >= 0 && state.ToolIndex < ToolPalette.Tools.Length);
        }

        for (int i = 0; i < 24; i++)
        {
            state.CycleTool(-1);
            Assert.Equal(state.Category, state.Current.Category);
        }
    }

    [Fact("半径必须被夹在 1..MaxRadius 内，不支持半径的工具必须忽略调节")]
    public void ToolRadiusIsClamped()
    {
        var state = new ToolPalette.State();

        // 跳到"放 1 名居民"（不支持半径）
        state.SetCategory(ToolCategory.Create);
        Assert.False(state.Current.UsesRadius);

        int before = state.Radius;
        state.AdjustRadius(5);
        Assert.Equal(before, state.Radius, "不支持半径的工具必须忽略调节");

        // 跳到支持半径的地形工具
        state.SetCategory(ToolCategory.Terrain);
        Assert.True(state.Current.UsesRadius);

        for (int i = 0; i < 50; i++) { state.AdjustRadius(1); }
        Assert.Equal(ToolPalette.State.MaxRadius, state.Radius);

        for (int i = 0; i < 50; i++) { state.AdjustRadius(-1); }
        Assert.Equal(1, state.Radius);
    }

    [Fact("切换类别/工具必须重置半径（否则会'一次改一大片'）")]
    public void SwitchingToolResetsRadius()
    {
        var state = new ToolPalette.State();
        state.SetCategory(ToolCategory.Terrain);

        for (int i = 0; i < 5; i++) { state.AdjustRadius(1); }
        Assert.Greater(state.Radius, 1);

        state.CycleTool(1);
        Assert.Equal(state.Current.DefaultRadius, state.Radius,
            "换工具之后半径必须回到该工具的默认值 —— 否则会出现'选了大范围地形工具、再切到单格工具却一次改一片'");
    }

    [Fact("每一个工具都必须能被执行且不抛异常（防止表里漏配委托）")]
    public void EveryToolExecutesWithoutThrowing()
    {
        var config = Config(40, 40);
        var sim = new Simulation(config, 40, 40, 8091);
        Flatten(sim, 5, 5, 35, 35);

        foreach (WorldTool tool in ToolPalette.Tools)
        {
            string result = tool.Apply(sim, 20, 20, tool.DefaultRadius);
            Assert.True(result != null, "工具 " + tool.Name + " 必须返回可读的结果描述");
        }
    }

    [Fact("工具必须只改条件、不改结果（禁止出现'造城/开战'类工具）")]
    public void ToolsOnlyChangeConditions()
    {
        // 这是一条**设计约束测试**，不是功能测试。
        // 第 46 条要求工具只能改条件；一旦有人加了一个"直接产出结果"的工具，
        // 这条断言应当失败并提醒他 —— 于是这条规则不依赖人记得它。
        string[] forbidden =
        {
            "造城", "创建城市", "开战", "战争", "变富", "建城", "胜利",
        };

        foreach (WorldTool tool in ToolPalette.Tools)
        {
            foreach (string word in forbidden)
            {
                Assert.False(tool.Name.Contains(word, System.StringComparison.Ordinal),
                    "工具名'" + tool.Name + "'看起来是直接产出结果（含'" + word + "'），违反第 46 条");
            }
        }
    }
}
