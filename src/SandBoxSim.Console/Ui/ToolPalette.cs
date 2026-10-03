using SandBoxSim.ConsoleApp.Render;
using SandBoxSim.ConsoleApp.Tui;
using SandBoxSim.Core;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.ConsoleApp.Ui;

/// <summary>工具的所属类别（决定面板分组，也决定玩家怎么找到它）。</summary>
public enum ToolCategory
{
    Create = 0,
    Terrain = 1,
    Resource = 2,
    Blessing = 3,
}

/// <summary>
/// 一个上帝工具。
///
/// **设计约束（第 46 条）**：工具只能改"条件"，不能直接产出"结果"。
/// 因此这里没有"造一座城""开始一场战争""让某人变富"这类条目 ——
/// 只有地形、资源、地力、居民、动物。
///
/// 每个工具都是**数据**而不是代码分支：类目、名字、作用半径的默认值、
/// 以及一个"在这个位置执行"的委托。新增工具只需要往表里加一行，
/// 面板绘制、按键处理、半径调节、状态回显全都不用改。
/// </summary>
public sealed class WorldTool
{
    public readonly ToolCategory Category;
    public readonly string Name;

    /// <summary>作用半径的默认值（格）。1 = 单格。</summary>
    public readonly int DefaultRadius;

    /// <summary>是否使用玩家的半径设置（false = 永远是单格）。</summary>
    public readonly bool UsesRadius;

    /// <summary>执行工具，返回"做了什么"的可读描述（用于状态栏回显）。</summary>
    public readonly System.Func<Simulation, int, int, int, string> Apply;

    public WorldTool(
        ToolCategory category,
        string name,
        System.Func<Simulation, int, int, int, string> apply,
        int defaultRadius = 1,
        bool usesRadius = false)
    {
        Category = category;
        Name = name;
        Apply = apply;
        DefaultRadius = defaultRadius < 1 ? 1 : defaultRadius;
        UsesRadius = usesRadius;
    }
}

/// <summary>
/// 玩家干预工具集（第 45 / 46 节的落地）。
///
/// 这是"玩家创造条件，而不是创造结果"这句话**唯一能被玩家实际操作到的界面** ——
/// 在此之前，所有 <c>Intervene*</c> API 都只有测试在调用。
///
/// 工具的效果分两类，界面上刻意不区分（但文档里写清）：
///   * **立刻可见**：地形、资源、放人、放动物 —— 玩家点下去就能看到变化；
///   * **延迟生效**：地力、再生倍率、天气 —— 点下去只看到颜色/数值变化，
///     真正的后果要等模拟把它放大出来。
/// 后者才是这个游戏的核心乐趣（第 94 条），因此它们不能被做成"没用"的样子。
/// </summary>
public static class ToolPalette
{
    /// <summary>全部工具（顺序 = 面板显示顺序 = 平级时的默认选择顺序）。</summary>
    public static readonly WorldTool[] Tools = BuildTools();

    private static WorldTool[] BuildTools()
    {
        return new[]
        {
            // ---- Create：创造 ----
            new WorldTool(ToolCategory.Create, "放 1 名居民",
                (sim, x, y, r) => "放置 " + sim.InterveneSpawnHumans(x, y, 1, 1) + " 人"),

            new WorldTool(ToolCategory.Create, "放 5 名居民",
                (sim, x, y, r) => "放置 " + sim.InterveneSpawnHumans(x, y, 5, 3) + " 人", defaultRadius: 3),

            new WorldTool(ToolCategory.Create, "放 5 只动物",
                (sim, x, y, r) => "放入 " + sim.InterveneSpawnAnimals(x, y, 5, 4) + " 只动物", defaultRadius: 4),

            new WorldTool(ToolCategory.Create, "催生森林",
                (sim, x, y, r) => "催生森林 " + sim.InterveneGrowForest(x, y, r, 0.7) + " 格",
                defaultRadius: 4, usesRadius: true),

            // ---- Terrain：地形 ----
            new WorldTool(ToolCategory.Terrain, "草地",
                (sim, x, y, r) => Terrain(sim, x, y, r, TerrainKind.Grass), defaultRadius: 2, usesRadius: true),

            new WorldTool(ToolCategory.Terrain, "森林",
                (sim, x, y, r) => Terrain(sim, x, y, r, TerrainKind.Forest), defaultRadius: 2, usesRadius: true),

            new WorldTool(ToolCategory.Terrain, "水域",
                (sim, x, y, r) => Terrain(sim, x, y, r, TerrainKind.Water), defaultRadius: 2, usesRadius: true),

            new WorldTool(ToolCategory.Terrain, "山地",
                (sim, x, y, r) => Terrain(sim, x, y, r, TerrainKind.Mountain), defaultRadius: 2, usesRadius: true),

            new WorldTool(ToolCategory.Terrain, "沙地",
                (sim, x, y, r) => Terrain(sim, x, y, r, TerrainKind.Sand), defaultRadius: 2, usesRadius: true),

            // ---- Resource：资源 ----
            new WorldTool(ToolCategory.Resource, "注入食物",
                (sim, x, y, r) => Resource(sim, x, y, r, ResourceKind.Food, 60f), defaultRadius: 2, usesRadius: true),

            new WorldTool(ToolCategory.Resource, "注入木材",
                (sim, x, y, r) => Resource(sim, x, y, r, ResourceKind.Wood, 60f), defaultRadius: 2, usesRadius: true),

            new WorldTool(ToolCategory.Resource, "注入石料",
                (sim, x, y, r) => Resource(sim, x, y, r, ResourceKind.Stone, 60f), defaultRadius: 2, usesRadius: true),

            new WorldTool(ToolCategory.Resource, "注入铁矿",
                (sim, x, y, r) => Resource(sim, x, y, r, ResourceKind.Iron, 40f), defaultRadius: 2, usesRadius: true),

            // ---- Blessing：恩惠（延迟生效的一类） ----
            new WorldTool(ToolCategory.Blessing, "肥沃度 +0.3",
                (sim, x, y, r) => "肥沃度提升 " + sim.InterveneSetFertility(x, y, r, 0.3f) + " 格",
                defaultRadius: 4, usesRadius: true),

            new WorldTool(ToolCategory.Blessing, "肥沃度 -0.3",
                (sim, x, y, r) => "肥沃度降低 " + sim.InterveneSetFertility(x, y, r, -0.3f) + " 格",
                defaultRadius: 4, usesRadius: true),

            new WorldTool(ToolCategory.Blessing, "再生 ×2",
                (sim, x, y, r) => { sim.InterveneMultiplyRegeneration(2f); return "资源再生倍率 ×2"; }),

            new WorldTool(ToolCategory.Blessing, "再生 ×0.5",
                (sim, x, y, r) => { sim.InterveneMultiplyRegeneration(0.5f); return "资源再生倍率 ×0.5"; }),

            new WorldTool(ToolCategory.Blessing, "降雨（24 小时）",
                (sim, x, y, r) => { sim.InterveneForceWeather(WeatherKind.Rain, 24); return "强制降雨 24 小时"; }),

            new WorldTool(ToolCategory.Blessing, "干旱（24 小时）",
                (sim, x, y, r) => { sim.InterveneForceWeather(WeatherKind.Drought, 24); return "强制干旱 24 小时"; }),
        };
    }

    /// <summary>把一片区域改成某种地形（并重设该地形的默认资源）。</summary>
    private static string Terrain(Simulation sim, int centerX, int centerY, int radius, TerrainKind kind)
    {
        int changed = 0;
        for (int y = centerY - radius; y <= centerY + radius; y++)
        {
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                if (!sim.World.IsInBounds(x, y)) { continue; }

                float dist = System.Math.Max(System.Math.Abs(x - centerX), System.Math.Abs(y - centerY));
                if (dist > radius) { continue; }
                if (sim.World.TileAt(x, y).Terrain == kind) { continue; }

                sim.InterveneSetTerrain(x, y, kind);
                changed++;
            }
        }
        return "改为" + TerrainInfo.NameOf(kind) + " " + changed + " 格";
    }

    /// <summary>往一片区域注入资源。</summary>
    private static string Resource(Simulation sim, int centerX, int centerY, int radius, ResourceKind kind, float amount)
    {
        float added = 0f;
        for (int y = centerY - radius; y <= centerY + radius; y++)
        {
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                if (!sim.World.IsInBounds(x, y)) { continue; }

                float dist = System.Math.Max(System.Math.Abs(x - centerX), System.Math.Abs(y - centerY));
                if (dist > radius) { continue; }

                added += sim.InterveneAddResource(x, y, kind, amount);
            }
        }
        return ResourceInfo.NameOf(kind) + " +" + added.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>某个类别下的工具索引（面板按类别分组显示）。</summary>
    public static int[] IndicesOf(ToolCategory category)
    {
        int count = 0;
        for (int i = 0; i < Tools.Length; i++)
        {
            if (Tools[i].Category == category) { count++; }
        }

        var result = new int[count];
        int k = 0;
        for (int i = 0; i < Tools.Length; i++)
        {
            if (Tools[i].Category == category) { result[k++] = i; }
        }
        return result;
    }

    public static string DisplayNameOf(ToolCategory category)
    {
        switch (category)
        {
            case ToolCategory.Create: return "创造";
            case ToolCategory.Terrain: return "地形";
            case ToolCategory.Resource: return "资源";
            case ToolCategory.Blessing: return "恩惠";
            default: return category.ToString();
        }
    }

    /// <summary>
    /// 面板状态：当前类别、当前工具下标、作用半径、是否展开。
    ///
    /// 独立成一个类而不是塞进 GameLoop 的字段：它有自己的不变量
    /// （下标必须在范围内、半径必须在 1..MaxRadius），集中在一处更容易保证正确。
    /// </summary>
    public sealed class State
    {
        public const int MaxRadius = 10;

        public ToolCategory Category { get; private set; } = ToolCategory.Create;
        public int ToolIndex { get; private set; }
        public int Radius { get; private set; }
        public bool IsOpen { get; set; }

        public State()
        {
            Radius = Tools[0].DefaultRadius;
        }

        public WorldTool Current => Tools[ToolIndex];

        /// <summary>切到下一个类别（循环）。切换时把半径恢复成新工具的默认值 —— 否则会出现"选了大范围地形工具、再切到单格放人工具却一次改一片"。</summary>
        public void NextCategory()
        {
            int value = ((int)Category + 1) % 4;
            SetCategory((ToolCategory)value);
        }

        public void PreviousCategory()
        {
            int value = ((int)Category + 3) % 4;
            SetCategory((ToolCategory)value);
        }

        public void SetCategory(ToolCategory category)
        {
            Category = category;
            int[] indices = IndicesOf(category);
            if (indices.Length > 0) { ToolIndex = indices[0]; }
            Radius = Current.DefaultRadius;
        }

        /// <summary>在当前类别内选择上一个/下一个工具。</summary>
        public void CycleTool(int direction)
        {
            int[] indices = IndicesOf(Category);
            if (indices.Length == 0) { return; }

            int position = 0;
            for (int i = 0; i < indices.Length; i++)
            {
                if (indices[i] == ToolIndex) { position = i; break; }
            }

            position = ((position + direction) % indices.Length + indices.Length) % indices.Length;
            ToolIndex = indices[position];
            Radius = Current.DefaultRadius;
        }

        /// <summary>调整作用半径（只有支持半径的工具才响应）。</summary>
        public void AdjustRadius(int delta)
        {
            if (!Current.UsesRadius) { return; }
            Radius = SimMath.Clamp(Radius + delta, 1, MaxRadius);
        }

        /// <summary>半径在面板上的显示（不支持半径的工具显示 "—"）。</summary>
        public string RadiusText => Current.UsesRadius ? Radius.ToString(System.Globalization.CultureInfo.InvariantCulture) : "—";
    }
}
