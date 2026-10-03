namespace SandBoxSim.Core.Environment;

/// <summary>Tile 的火灾状态（第 43 节）。M0 只保留字段，M5 才接入完整火灾模拟。</summary>
public enum FireState : byte
{
    None = 0,

    /// <summary>正在燃烧：持续损失植被，并按概率向邻居传播。</summary>
    Burning = 1,

    /// <summary>已烧毁：植被清零，需要很长时间才能恢复（天然形成"焦土"）。</summary>
    Burnt = 2,
}

/// <summary>
/// 单个格子的状态（第 6 节）。
///
/// 内存布局刻意保持紧凑（约 44 字节）：100×100 地图 1 万个格子，
/// 未来放大到 500×500 时也只占几百 MB 的零头。
/// 用 struct + 数组而非对象，是"大量 agent/格子"场景的基本功（第 73 / 75 条）。
/// </summary>
public struct Tile
{
    /// <summary>地形。</summary>
    public TerrainKind Terrain;

    /// <summary>火灾状态。</summary>
    public FireState Fire;

    /// <summary>
    /// 土地上现有的建筑物索引 + 1（**0 表示没有建筑**）。存索引而非引用，方便数组化与存档。
    ///
    /// 为什么用"索引 + 1"而不是直接用索引、以 -1 表示空：
    /// Tile 是 10 万格级别的热数据，而它经常被**批量初始化**（世界生成、地形工具）。
    /// 用 0 表示空可以让"默认值就是合法值" —— 新建的 Tile 数组不需要逐格写 -1，
    /// 而 -1 需要显式初始化，一旦漏掉就会得到"指向第 -1 号建筑"这种静默错误。
    /// </summary>
    public int BuildingId;

    /// <summary>肥沃度 [0,1]：农场产量、采集食物产出、植被恢复速度都读它。</summary>
    public float Fertility;

    /// <summary>湿度 [0,1]：影响作物、火灾风险、植被生长。</summary>
    public float Moisture;

    /// <summary>温度 [0,1]（归一化的"冷暖"，不是摄氏度）：影响蒸发、作物、未来取暖需求。</summary>
    public float Temperature;

    /// <summary>植被量 [0,1]：森林的可燃物与栖息地，被砍伐/烧毁后下降。</summary>
    public float Vegetation;

    /// <summary>本格上的资源节点。</summary>
    public ResourceNode Resource;

    /// <summary>是否可通行。默认由地形决定，但河流改道、桥梁等会让它偏离"地形默认值"。</summary>
    public bool Walkable;

    /// <summary>是否可建造。</summary>
    public bool Buildable;

    public static Tile CreateDefault(TerrainKind terrain, float fertility, float moisture, float temperature)
    {
        return new Tile
        {
            Terrain = terrain,
            Fire = FireState.None,
            BuildingId = 0,
            Fertility = fertility,
            Moisture = moisture,
            Temperature = temperature,
            Vegetation = terrain == TerrainKind.Forest ? 1f : (terrain == TerrainKind.Grass ? 0.5f : 0f),
            Resource = default,
            Walkable = TerrainInfo.IsWalkable(terrain),
            Buildable = TerrainInfo.IsBuildable(terrain),
        };
    }

    /// <summary>地形变化后的规则同步：通行/可建造/默认植被。</summary>
    public void ApplyTerrainRules()
    {
        Walkable = TerrainInfo.IsWalkable(Terrain);
        Buildable = TerrainInfo.IsBuildable(Terrain);
    }

    public bool HasResource => Resource.Kind != ResourceKind.None && Resource.Capacity > 0f;

    /// <summary>是否被火烧过的焦土。</summary>
    public bool IsBurnt => Fire == FireState.Burnt;

    public override string ToString()
        => TerrainInfo.NameOf(Terrain) + " fert=" + Fertility.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
         + " moist=" + Moisture.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}
