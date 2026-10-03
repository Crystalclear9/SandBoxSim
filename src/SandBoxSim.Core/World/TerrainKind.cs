namespace SandBoxSim.Core.Environment;

/// <summary>
/// 地形种类。M0 只生成 Grass / Forest / Water / Mountain / Sand 五种；
/// Farmland / Road 由模拟过程（开垦、铺路）产生，不是世界生成的初始结果；
/// Snow / Swamp / Desert / Lava 属于后续阶段（第 6 节）。
/// </summary>
public enum TerrainKind : byte
{
    Grass = 0,
    Forest = 1,
    Water = 2,
    Mountain = 3,
    Sand = 4,

    /// <summary>农田：由居民开垦 Grass 得到，产出食物。</summary>
    Farmland = 5,

    /// <summary>道路：M3+ 由居民铺设，降低移动成本。</summary>
    Road = 6,

    // ---- 预留（世界生成暂不产出，避免 MVP 范围膨胀）----
    Snow = 7,
    Swamp = 8,
    Desert = 9,
    Lava = 10,
}

/// <summary>
/// 地形的静态规则表（第 7 节）。
///
/// 为什么集中在一张表里：任何系统问"这块地能不能走/能不能建/走起来多贵"，
/// 答案必须唯一。如果让寻路、建造、AI 各自写一份判断，三处很快就会不一致，
/// 表现就是"小人穿墙"或"建筑盖在水里"这类难查的 bug。
/// </summary>
public static class TerrainInfo
{
    public const int TerrainKindCount = 11;

    private static readonly bool[] WalkableTable =
    {
        true,   // Grass
        true,   // Forest
        false,  // Water（M0 无水路；未来的渡口/桥会改变这一点）
        true,   // Mountain（可走，但代价高）
        true,   // Sand
        true,   // Farmland
        true,   // Road
        true,   // Snow
        true,   // Swamp
        true,   // Desert
        false,  // Lava
    };

    private static readonly bool[] BuildableTable =
    {
        true,   // Grass
        true,   // Forest（可建，但需要先清理，代价体现在建造时间上）
        false,  // Water
        true,   // Mountain（可建矿场类）
        true,   // Sand
        false,  // Farmland（不能在农田上盖房，否则人口增长会吞掉食物）
        true,   // Road
        true,   // Snow
        true,   // Swamp
        true,   // Desert
        false,  // Lava
    };

    private static readonly float[] MoveCostTable =
    {
        1.0f,   // Grass
        1.5f,   // Forest
        99.0f,  // Water（不可走，给一个很大的代价而不是 0，避免误用时产生零成本路径）
        3.0f,   // Mountain
        1.2f,   // Sand
        1.1f,   // Farmland
        0.6f,   // Road
        1.6f,   // Snow
        2.2f,   // Swamp
        1.3f,   // Desert
        99.0f,  // Lava
    };

    /// <summary>该地形是否适合开垦为农田（Grass 最优，Sand 勉强）。</summary>
    private static readonly bool[] FarmableTable =
    {
        true,   // Grass
        false,  // Forest（需先伐木，M3 处理）
        false,  // Water
        false,  // Mountain
        false,  // Sand
        false,  // Farmland（已开垦）
        false,  // Road
        false,  // Snow
        false,  // Swamp
        false,  // Desert
        false,  // Lava
    };

    /// <summary>可通行？</summary>
    public static bool IsWalkable(TerrainKind terrain) => WalkableTable[(int)terrain];

    /// <summary>可建造？</summary>
    public static bool IsBuildable(TerrainKind terrain) => BuildableTable[(int)terrain];

    /// <summary>可开垦为农田？</summary>
    public static bool IsFarmable(TerrainKind terrain) => FarmableTable[(int)terrain];

    /// <summary>A* 的进入代价。不可走地形返回一个很大的值（用于"明知不可走也要算代价"的场景）。</summary>
    public static float MoveCost(TerrainKind terrain) => MoveCostTable[(int)terrain];

    /// <summary>是否提供水源（饮水、灌溉湿度加成）。</summary>
    public static bool ProvidesWater(TerrainKind terrain) => terrain == TerrainKind.Water;

    /// <summary>是否是植被地形（火灾可燃、木材来源、动物栖息）。</summary>
    public static bool IsVegetation(TerrainKind terrain)
        => terrain == TerrainKind.Forest || terrain == TerrainKind.Grass;

    /// <summary>给 UI 用的稳定标识名（不参与模拟逻辑）。</summary>
    public static string NameOf(TerrainKind terrain)
    {
        switch (terrain)
        {
            case TerrainKind.Grass: return "grass";
            case TerrainKind.Forest: return "forest";
            case TerrainKind.Water: return "water";
            case TerrainKind.Mountain: return "mountain";
            case TerrainKind.Sand: return "sand";
            case TerrainKind.Farmland: return "farmland";
            case TerrainKind.Road: return "road";
            case TerrainKind.Snow: return "snow";
            case TerrainKind.Swamp: return "swamp";
            case TerrainKind.Desert: return "desert";
            case TerrainKind.Lava: return "lava";
            default: return "unknown";
        }
    }

    /// <summary>从字符串解析地形（配置/存档用）。未知名称回退 Grass 并返回 false。</summary>
    public static bool TryParse(string? name, out TerrainKind terrain)
    {
        terrain = TerrainKind.Grass;
        if (string.IsNullOrWhiteSpace(name)) { return false; }

        switch (name.Trim().ToLowerInvariant())
        {
            case "grass": terrain = TerrainKind.Grass; return true;
            case "forest": terrain = TerrainKind.Forest; return true;
            case "water": terrain = TerrainKind.Water; return true;
            case "mountain": terrain = TerrainKind.Mountain; return true;
            case "sand": terrain = TerrainKind.Sand; return true;
            case "farmland": terrain = TerrainKind.Farmland; return true;
            case "road": terrain = TerrainKind.Road; return true;
            case "snow": terrain = TerrainKind.Snow; return true;
            case "swamp": terrain = TerrainKind.Swamp; return true;
            case "desert": terrain = TerrainKind.Desert; return true;
            case "lava": terrain = TerrainKind.Lava; return true;
            default: return false;
        }
    }
}
