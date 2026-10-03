using SandBoxSim.ConsoleApp.Tui;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.ConsoleApp.Render;

/// <summary>
/// 地形配色（第 98 条视觉原则：第一阶段只投入纯色块，不投入美术）。
///
/// 配色目标不是"好看"，而是**可读**：玩家必须在 0.2 秒内分辨出
/// 草地/森林/水域/山地/沙滩/农田，以及"这块森林快被砍光了"。
/// 因此：
///   * 色相之间拉开距离（绿/蓝/灰/黄/褐）；
///   * 同一地形的贫富差异用**明度**表达（亮=资源多），而不是换色相；
///   * 所有颜色都经过一次亮度缩放，保证昼夜与选中高亮能统一作用于整幅图。
/// </summary>
public static class Palette
{
    // ---- 地形基色 ----
    public static readonly Rgb GrassBase = new Rgb(78, 140, 66);
    public static readonly Rgb ForestBase = new Rgb(38, 96, 48);
    public static readonly Rgb WaterDeep = new Rgb(26, 60, 130);
    public static readonly Rgb WaterShallow = new Rgb(48, 108, 176);
    public static readonly Rgb MountainBase = new Rgb(112, 110, 112);
    public static readonly Rgb MountainLow = new Rgb(84, 86, 92);
    public static readonly Rgb SandBase = new Rgb(198, 182, 126);
    public static readonly Rgb FarmlandBase = new Rgb(176, 152, 72);
    public static readonly Rgb RoadBase = new Rgb(126, 110, 92);

    /// <summary>建筑基色：偏暖的砖色，与任何地形色都不会混淆。</summary>
    public static readonly Rgb BuildingBase = new Rgb(196, 138, 106);

    /// <summary>未完工工地：比建成建筑暗一档，让"工地 / 房子"一眼可辨。</summary>
    public static readonly Rgb BuildingSite = new Rgb(126, 100, 80);

    /// <summary>农田建筑：偏金绿，与裸土地形区分。</summary>
    public static readonly Rgb FarmPlot = new Rgb(206, 186, 86);

    public static readonly Rgb BurntBase = new Rgb(58, 44, 38);

    // ---- 实体（人 / 动物）：状态色 ----
    //
    // 人的颜色编码**当前状态**而不是身份 —— 玩家要能一眼看出"谁在干活、谁在睡、谁快饿死"。
    // 身份信息（名字/性格/需求）交给检查器面板，那里有空间写清楚。
    public static readonly Rgb AgentWorking = new Rgb(250, 236, 132);   // 工作/采集/建造
    public static readonly Rgb AgentMoving = new Rgb(140, 210, 255);    // 在途
    public static readonly Rgb AgentEating = new Rgb(150, 240, 150);    // 进食/饮水
    public static readonly Rgb AgentSleeping = new Rgb(150, 150, 210);  // 睡眠
    public static readonly Rgb AgentIdle = new Rgb(210, 210, 210);      // 空闲/漫游
    public static readonly Rgb AgentStarving = new Rgb(255, 96, 72);    // 饥饿/脱水告急
    public static readonly Rgb AgentElite = new Rgb(255, 196, 92);      // 被选中的个体

    /// <summary>
    /// 野生动物：**偏紫的褐色**。
    ///
    /// 为什么不用自然的棕色：沙地底色是 (198,182,126)，棕色动物落在沙滩上会完全看不见。
    /// 往紫色偏一点能同时避开草地绿、水蓝、山灰、沙黄 —— 这是"配色必须可读"的直接后果，
    /// 而不是审美选择（第 98 条）。
    /// </summary>
    public static readonly Rgb WildlifeColor = new Rgb(186, 116, 168);

    // ---- 资源与状态 ----
    public static readonly Rgb WoodTint = new Rgb(96, 60, 30);
    public static readonly Rgb FoodTint = new Rgb(226, 108, 92);
    public static readonly Rgb StoneTint = new Rgb(170, 170, 178);
    public static readonly Rgb IronTint = new Rgb(158, 152, 190);
    public static readonly Rgb FireColor = new Rgb(255, 138, 32);
    public static readonly Rgb FireCore = new Rgb(255, 232, 120);

    // ---- UI ----
    public static readonly Rgb UiBackground = new Rgb(14, 16, 20);
    public static readonly Rgb UiPanel = new Rgb(22, 26, 34);
    public static readonly Rgb UiPanelAlt = new Rgb(30, 36, 46);
    public static readonly Rgb UiBorder = new Rgb(72, 84, 104);
    public static readonly Rgb UiText = new Rgb(224, 230, 238);
    public static readonly Rgb UiTextDim = new Rgb(148, 158, 172);
    public static readonly Rgb UiAccent = new Rgb(122, 196, 255);
    public static readonly Rgb UiWarn = new Rgb(255, 196, 92);
    public static readonly Rgb UiDanger = new Rgb(255, 110, 110);
    public static readonly Rgb UiGood = new Rgb(140, 226, 140);
    public static readonly Rgb SelectionColor = new Rgb(255, 255, 160);
    public static readonly Rgb CursorColor = new Rgb(255, 240, 200);

    /// <summary>
    /// 地形基色（不含噪声与光照）。资源状态已经折进来：
    /// 森林越挖越亮褐、草地食物越少越暗，玩家一眼能看出"哪里被采空了"（第 91 条观察价值）。
    /// </summary>
    public static Rgb TerrainColor(ref readonly Tile tile)
    {
        switch (tile.Terrain)
        {
            case TerrainKind.Grass:
            {
                // 肥沃/食物越少越暗、越偏黄（"被吃秃的草地"）
                float lushness = SimMath.Clamp01((tile.Vegetation * 0.5f) + (tile.Resource.Fraction * 0.5f));
                Rgb dry = new Rgb(132, 128, 74);
                return Rgb.Lerp(dry, GrassBase, lushness);
            }

            case TerrainKind.Forest:
            {
                float woodFraction = tile.Resource.Kind == ResourceKind.Wood ? tile.Resource.Fraction : 1f;
                Rgb thinned = new Rgb(96, 84, 50);   // 被砍过的疏林（偏黄褐）
                Rgb full = ForestBase;
                Rgb color = Rgb.Lerp(thinned, full, SimMath.Clamp01((woodFraction * 0.7f) + (tile.Vegetation * 0.3f)));
                if (woodFraction <= 0.05f)
                {
                    color = new Rgb(104, 96, 72);    // 彻底砍光：接近荒地的颜色
                }
                return color;
            }

            case TerrainKind.Water:
                // 用湿度/温度做一个很轻的色调差异，避免整片水域死平。
                return Rgb.Lerp(WaterDeep, WaterShallow, SimMath.Clamp01(0.35f + (tile.Temperature * 0.3f)));

            case TerrainKind.Mountain:
            {
                // 山体的明度随"剩余矿产比例"变化：矿被采空的山会从浅灰变成深灰。
                // 这让"这片山快被挖空了"成为肉眼可见的事实（第 91 条观察价值）。
                float mineral = tile.Resource.Capacity > 0f ? tile.Resource.Fraction : 0.5f;
                Rgb baseColor = Rgb.Lerp(MountainLow, MountainBase, SimMath.Clamp01(0.25f + (mineral * 0.75f)));

                // 铁矿露头在地表可见：这是玩家发现矿产的唯一途径，因此必须能被看见。
                if (tile.Resource.Kind == ResourceKind.Iron && tile.Resource.Amount > 0f)
                {
                    return Rgb.Lerp(baseColor, IronTint, 0.55f);
                }
                return baseColor;
            }

            case TerrainKind.Sand:
                return SandBase;

            case TerrainKind.Farmland:
            {
                // 农田的颜色随肥沃度变化：贫瘠的田发白，肥沃的田发金。
                return Rgb.Lerp(new Rgb(150, 136, 96), FarmlandBase, tile.Fertility);
            }

            case TerrainKind.Road:
                return RoadBase;

            case TerrainKind.Snow:
                return new Rgb(228, 236, 244);

            case TerrainKind.Swamp:
                return new Rgb(70, 92, 66);

            case TerrainKind.Desert:
                return new Rgb(206, 178, 118);

            case TerrainKind.Lava:
                return new Rgb(210, 82, 30);

            default:
                return new Rgb(120, 120, 120);
        }
    }

    /// <summary>
    /// 最终像素色 = 地形色 × 每格固定噪声 × 全局光照 × 城市光。
    /// 噪声只改变明度且幅度很小（±4%），只是为了让大片同色地形看起来有质感，
    /// 绝不会让玩家误判地形种类。
    /// </summary>
    public static Rgb Shade(ref readonly Tile tile, int x, int y, uint seed, float lightLevel)
    {
        Rgb color = TerrainColor(in tile);

        // 建筑覆盖一层可辨识的色调：玩家要能一眼看出"这里有人造物"。
        // 这里只改色调不改明度，因此夜间光照逻辑（下面那一步）仍然统一生效。
        if (tile.BuildingId > 0)
        {
            color = Rgb.Lerp(color, BuildingBase, 0.55f);
        }

        float noise = 1f + NoiseHash.VisualJitter(x, y, seed, 0.045f);
        color = color.Scale(noise);

        // 火焰覆盖一切：燃烧中的格子必须最显眼（第 43 节：火灾是最有戏剧性的观察对象）。
        if (tile.Fire == FireState.Burning)
        {
            float flicker = 0.75f + ((float)NoiseHash.Value01(x, y, seed ^ 0x51EDu, 7) * 0.25f);
            return Rgb.Lerp(FireColor, FireCore, flicker);
        }
        if (tile.Fire == FireState.Burnt)
        {
            return Rgb.Lerp(BurntBase, color, 0.15f);
        }

        // 昼夜光照：夜间压暗到约 45%，但保留最低可见度，避免玩家什么都看不见。
        float light = 0.45f + (SimMath.Clamp01(lightLevel) * 0.55f);
        return color.Scale(light);
    }

    /// <summary>把 RGB 转成 16 色近似字符（无颜色模式下的地形字符表）。</summary>
    public static char GlyphFor(ref readonly Tile tile)
    {
        // 建筑优先于地形：玩家最需要一眼看到的是"这里有东西"，
        // 而不是"这里本来是草地还是森林"。未完工的工地用小写字母区分。
        if (tile.BuildingId > 0)
        {
            return tile.Terrain == SandBoxSim.Core.Environment.TerrainKind.Farmland ? '≡' : 'A';
        }

        switch (tile.Terrain)
        {
            case TerrainKind.Grass: return '.';
            case TerrainKind.Forest: return 'T';
            case TerrainKind.Water: return '~';
            case TerrainKind.Mountain: return '^';
            case TerrainKind.Sand: return ':';
            case TerrainKind.Farmland: return '=';
            case TerrainKind.Road: return '#';
            case TerrainKind.Snow: return '*';
            case TerrainKind.Swamp: return '%';
            case TerrainKind.Desert: return '"';
            case TerrainKind.Lava: return '!';
            default: return '?';
        }
    }
}
