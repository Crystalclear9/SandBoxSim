using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Environment;

/// <summary>
/// 世界状态容器：持有 Tile 数组、空间索引、天气与日历，并对外提供**唯一**的修改入口。
///
/// 为什么要"唯一入口"：所有对 Tile 的写入都必须同步通知空间索引（MarkDirty），
/// 否则 chunk 统计会悄悄过期，AI 就会去一片已经不存在的森林里砍树 —— 这类 bug
/// 在模拟游戏里极难定位。把写入口收窄到这一个类，是防止这类 bug 的结构性手段。
/// </summary>
public sealed class World
{
    public SimConfig Config { get; }
    public int Width { get; }
    public int Height { get; }

    /// <summary>地形与资源真相（行主序，index = y * Width + x）。</summary>
    public Tile[] Tiles { get; }

    /// <summary>空间索引缓存。</summary>
    public ChunkGrid Chunks { get; }

    /// <summary>天气状态（每游戏小时推进）。</summary>
    public Weather Weather { get; private set; } = new Weather();

    /// <summary>日历（模拟唯一时间基准）。</summary>
    public Calendar Calendar { get; }

    /// <summary>本世界生成时使用的种子（存档必须保存，否则无法复现）。</summary>
    public int Seed { get; private set; }

    /// <summary>创造/毁灭类的版本号：地形整体重建时 +1，表现层据此丢弃缓存。</summary>
    public int Revision { get; private set; }
    private int[]? _waterDistance;
    private bool _waterDistanceDirty = true;

    public long Tick => Calendar.Tick;

    public World(SimConfig config, int width, int height, int seed)
    {
        Config = config ?? new SimConfig();
        WorldConfig worldConfig = Config.World ?? new WorldConfig();
        ClockConfig clockConfig = Config.Clock ?? new ClockConfig();

        Width = width > 0 ? width : worldConfig.Width;
        Height = height > 0 ? height : worldConfig.Height;
        Seed = seed;

        Tiles = new Tile[Width * Height];
        Chunks = new ChunkGrid(Width, Height, worldConfig.ChunkSize > 0 ? worldConfig.ChunkSize : 16);
        Calendar = new Calendar(clockConfig.TicksPerHour, clockConfig.HoursPerDay);
    }

    public int TileCount => Width * Height;

    public bool IsInBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
    public bool IsInBounds(Int2 p) => IsInBounds(p.X, p.Y);

    public int IndexOf(int x, int y) => (y * Width) + x;
    public int IndexOf(Int2 p) => (p.Y * Width) + p.X;

    public Int2 PositionOf(int index) => new Int2(index % Width, index / Width);

    // ---------------------------------------------------------------------
    // 读取
    // ---------------------------------------------------------------------

    /// <summary>按索引读（调用方保证索引有效，热路径用）。</summary>
    public ref readonly Tile TileAt(int index) => ref Tiles[index];

    public ref readonly Tile TileAt(int x, int y) => ref Tiles[(y * Width) + x];

    public ref readonly Tile TileAt(Int2 p) => ref Tiles[(p.Y * Width) + p.X];

    /// <summary>
    /// 越界安全的读取。越界时返回一个"深水"式的空 Tile，
    /// 这样查询类代码（渲染、邻域扫描）不必到处写边界判断，且不会误判为可走地形。
    /// </summary>
    public Tile TileAtClamped(int x, int y)
    {
        if (!IsInBounds(x, y))
        {
            return Tile.CreateDefault(TerrainKind.Water, 0f, 1f, Config.World.AmbientTemperature);
        }
        return Tiles[(y * Width) + x];
    }

    /// <summary>一次实际跨格/作业接触，不使用表现帧或随机数。</summary>
    public void RecordFootfall(int x,int y,float pressure=1f)
    {
        if(!IsInBounds(x,y) || !float.IsFinite(pressure) || pressure<=0)return;
        pressure=System.Math.Min(pressure,2f);
        ref Tile tile=ref Tiles[IndexOf(x,y)];
        if(!tile.Walkable || tile.Terrain==TerrainKind.Road || tile.BuildingId!=0)return;
        float before=tile.FootTraffic;
        tile.FootTraffic=SimMath.Clamp01(before+.0015f*System.Math.Min(pressure,2f)*(1-before));
        tile.Height-=.006f*(tile.FootTraffic-before);
        tile.Vegetation=SimMath.Clamp01(tile.Vegetation-.00012f*pressure*(.5f+tile.Moisture));
        MarkDirtyAt(x,y);
    }
    public void RecoverFootTraffic()
    {
        for(int i=0;i<Tiles.Length;i++)
        {
            ref Tile tile=ref Tiles[i];if(tile.FootTraffic<=0)continue;
            float before=tile.FootTraffic;tile.FootTraffic*=.996f;
            if(tile.FootTraffic<.000001f)tile.FootTraffic=0;
            tile.Height+=.006f*(before-tile.FootTraffic);
            MarkDirtyAt(i%Width,i/Width);
        }
    }

    public TerrainKind TerrainAt(int x, int y) => TileAtClamped(x, y).Terrain;

    /// <summary>索引 → 是否可走（AI 邻域扫描用）。</summary>
    public bool IsWalkableAt(int index) => Tiles[index].Walkable;

    public bool IsBuildableAt(int x, int y) => IsInBounds(x, y) && Tiles[(y * Width) + x].Buildable;

    // ---------------------------------------------------------------------
    // 写入（唯一入口，全部同步空间索引）
    // ---------------------------------------------------------------------

    /// <summary>替换整张地图（世界生成与重置用）。会通知索引全量重算。</summary>
    public void ReplaceAllTiles(Tile[] tiles, int seed)
    {
        if (tiles == null || tiles.Length != Tiles.Length)
        {
            throw new System.ArgumentException("Tile 数组长度与世界尺寸不匹配", nameof(tiles));
        }
        System.Array.Copy(tiles, Tiles, Tiles.Length);
        _waterDistanceDirty = true;
        Seed = seed;
        Chunks.MarkAllDirty();
        Revision++;
    }

    /// <summary>设置地形，并同步通行/可建造规则与所在 chunk 的统计。</summary>
    public void SetTerrain(int x, int y, TerrainKind terrain)
    {
        if (!IsInBounds(x, y)) { return; }
        int idx = (y * Width) + x;
        if ((Tiles[idx].Terrain == TerrainKind.Water) != (terrain == TerrainKind.Water)) { _waterDistanceDirty = true; }
        if(Tiles[idx].Terrain!=terrain){Tiles[idx].Height+=.006f*Tiles[idx].FootTraffic;Tiles[idx].FootTraffic=0;}
        Tiles[idx].Terrain = terrain;
        Tiles[idx].ApplyTerrainRules();
        Revision++;
        if (terrain != TerrainKind.Forest) { Tiles[idx].Fire = FireState.None; }
        Chunks.MarkAtDirty(x, y);
    }

    public void SetFertility(int x, int y, float fertility)
    {
        if (!IsInBounds(x, y)) { return; }
        Tiles[(y * Width) + x].Fertility = SimMath.Clamp01(fertility);
        Chunks.MarkAtDirty(x, y);
    }

    public void SetMoisture(int x, int y, float moisture)
    {
        if (!IsInBounds(x, y)) { return; }
        Tiles[(y * Width) + x].Moisture = SimMath.Clamp01(moisture);
        Chunks.MarkAtDirty(x, y);
    }

    public void SetTemperature(int x, int y, float temperature)
    {
        if (!IsInBounds(x, y)) { return; }
        Tiles[(y * Width) + x].Temperature = SimMath.Clamp01(temperature);
        NotifyNavigationChanged();
        Chunks.MarkAtDirty(x, y);
    }

    /// <summary>批量修改通行成本后使派生寻路缓存失效。</summary>
    public void NotifyNavigationChanged() => Revision++;

    public void SetVegetation(int x, int y, float vegetation)
    {
        if (!IsInBounds(x, y)) { return; }
        Tiles[(y * Width) + x].Vegetation = SimMath.Clamp01(vegetation);
        Chunks.MarkAtDirty(x, y);
    }

    public void SetWalkable(int x, int y, bool walkable)
    {
        if (!IsInBounds(x, y)) { return; }
        Tiles[(y * Width) + x].Walkable = walkable;
        Revision++;
        Chunks.MarkAtDirty(x, y);
    }

    public void SetBuildable(int x, int y, bool buildable)
    {
        if (!IsInBounds(x, y)) { return; }
        Tiles[(y * Width) + x].Buildable = buildable;
        Chunks.MarkAtDirty(x, y);
    }

    public void SetFire(int x, int y, FireState state)
    {
        if (!IsInBounds(x, y)) { return; }
        Tiles[(y * Width) + x].Fire = state;
        Chunks.MarkAtDirty(x, y);
    }

    public void SetBuilding(int x, int y, int buildingId)
    {
        if (!IsInBounds(x, y)) { return; }
        Tiles[(y * Width) + x].BuildingId = buildingId;
        Chunks.MarkAtDirty(x, y);
    }

    /// <summary>直接改资源节点（世界生成与玩家工具用；采集走 ResourceSystem 的结算路径）。</summary>
    public void SetResource(int x, int y, ResourceNode node)
    {
        if (!IsInBounds(x, y)) { return; }
        Tiles[(y * Width) + x].Resource = node;
        Chunks.MarkAtDirty(x, y);
    }

    public void ClearResource(int x, int y)
    {
        SetResource(x, y, default);
    }

    /// <summary>
    /// 根据地形给出该格的"默认资源节点"（世界生成与地形改造共用同一张表）。
    /// 返回 None 表示此地不适合放资源节点（水域 / 农田 / 道路等）。
    ///
    /// 之所以要把 K/r/初始比例按地形分开：矿产不可再生、木材可再生、
    /// 野生食物容量随肥沃度变化 —— 这些差异必须集中在**一处**定义，
    /// 否则生成器、玩家工具、资源系统会各写一份并逐渐分叉。
    /// </summary>
    public ResourceNode DefaultResourceFor(TerrainKind terrain, float fertility)
    {
        ResourceConfig rc = Config.Resources;

        switch (terrain)
        {
            case TerrainKind.Forest:
                return new ResourceNode
                {
                    Kind = ResourceKind.Wood,
                    Capacity = rc.WoodCapacityPerForestTile,
                    Amount = rc.WoodCapacityPerForestTile * SimMath.Clamp01(rc.WoodInitialFraction),
                    RegenerationRate = rc.WoodGrowthRate,
                };

            case TerrainKind.Mountain:
                // 石头默认，铁矿由世界生成单独铺（矿脉是稀有资源，不能"每座山都有铁"）。
                return new ResourceNode
                {
                    Kind = ResourceKind.Stone,
                    Capacity = rc.StoneCapacityPerMountainTile,
                    Amount = rc.StoneCapacityPerMountainTile * SimMath.Clamp01(rc.StoneInitialFraction),
                    RegenerationRate = 0f,
                };

            case TerrainKind.Grass:
            case TerrainKind.Sand:
            {
                // 野生食物容量随肥沃度变化：贫地长不出多少可采的食物。
                float capacity = rc.FoodCapacityPerGrassTile * (0.4f + (SimMath.Clamp01(fertility) * 0.6f));
                return new ResourceNode
                {
                    Kind = ResourceKind.Food,
                    Capacity = capacity,
                    Amount = capacity * SimMath.Clamp01(rc.FoodInitialFraction),
                    RegenerationRate = rc.FoodGrowthRate,
                };
            }

            default:
                return default;
        }
    }

    /// <summary>
    /// 给一格按地形重新配置默认资源（开垦农田、地形改造后调用）。
    /// </summary>
    public void ApplyDefaultResource(int x, int y)
    {
        if (!IsInBounds(x, y)) { return; }
        int idx = (y * Width) + x;
        Tiles[idx].Resource = DefaultResourceFor(Tiles[idx].Terrain, Tiles[idx].Fertility);
        Chunks.MarkAtDirty(x, y);
    }

    /// <summary>显式标脏（批量修改后调用，避免每格都触发一次索引维护）。</summary>
    public void MarkDirtyAt(int x, int y) => Chunks.MarkAtDirty(x, y);

    /// <summary>刷新空间索引缓存。由 Simulation 每 tick 调用一次。</summary>
    public void RefreshSpatialIndex() => Chunks.Refresh(Tiles);

    // ---------------------------------------------------------------------
    // 统计（全图尺度，供 UI/报告用；不要放进每 tick 的热循环）
    // ---------------------------------------------------------------------

    /// <summary>统计当前地形分布，写入传入数组（下标 = TerrainKind）。</summary>
    public int[] CountTerrain()
    {
        int[] counts = new int[TerrainInfo.TerrainKindCount];
        for (int i = 0; i < Tiles.Length; i++)
        {
            counts[(int)Tiles[i].Terrain]++;
        }
        return counts;
    }

    /// <summary>全图某资源的存量总和。</summary>
    public float TotalResource(ResourceKind kind)
    {
        float total = 0f;
        for (int i = 0; i < Tiles.Length; i++)
        {
            if (Tiles[i].Resource.Kind == kind) { total += Tiles[i].Resource.Amount; }
        }
        return total;
    }

    /// <summary>全图某资源的容量总容量（用于计算"剩余比例"）。</summary>
    public float TotalCapacity(ResourceKind kind)
    {
        float total = 0f;
        for (int i = 0; i < Tiles.Length; i++)
        {
            if (Tiles[i].Resource.Kind == kind) { total += Tiles[i].Resource.Capacity; }
        }
        return total;
    }

    public float AverageMoisture()
    {
        if (Tiles.Length == 0) { return 0f; }
        double sum = 0.0;
        for (int i = 0; i < Tiles.Length; i++) { sum += Tiles[i].Moisture; }
        return (float)(sum / Tiles.Length);
    }

    public float AverageTemperature()
    {
        if (Tiles.Length == 0) { return 0f; }
        double sum = 0.0;
        for (int i = 0; i < Tiles.Length; i++) { sum += Tiles[i].Temperature; }
        return (float)(sum / Tiles.Length);
    }

    /// <summary>
    /// 把 chunk 的"燃料/湿度/植被"聚合信息保留下来，供火灾系统快速判断
    /// （M5 使用；M0 只提供接口，避免火灾系统自己扫图）。
    /// </summary>
    public float AverageForestCover(int chunkIndex)
    {
        ChunkStatsReadOnly view = Chunks.ReadIndex(chunkIndex);
        if (view.CellCount <= 0) { return 0f; }
        return view.ForestTiles / (float)view.CellCount;
    }

    /// <summary>重置天气（换种子重建世界时调用）。</summary>
    public void ResetWeather() => Weather = new Weather();

    /// <summary>存档恢复：直接替换天气对象。</summary>
    public void RestoreWeather(Weather weather) => Weather = weather ?? new Weather();

    /// <summary>存档恢复：直接把日历推进到某个 tick（不触发任何周期事件）。</summary>
    public void RestoreTick(long tick) => Calendar.RestoreFromSave(tick);

    /// <summary>
    /// 存档恢复：把世界的 seed 改回存档里记录的那个。
    ///
    /// **为什么必须有这个方法**（这是一个真实踩到的坑）：读档时一般是
    /// "先用命令行给的 seed 造一个空世界，再用存档覆盖逐格数据"。
    /// 但如果只覆盖地形而忘了改 <see cref="Seed"/>，世界里就会留下
    /// "地形属于 seed 555、而 Seed 字段写着 839102"这种自相矛盾的状态。
    ///
    /// 而 seed **参与状态摘要**，所以表现是"读档瞬间摘要就不一致"。
    /// 更坏的是：只要调用方恰好传了与存档相同的 seed（很常见，
    /// 比如自己存自己读的测试），这个 bug 就会**完全隐藏**。
    /// 因此 <c>SaveLoadTests.LoadedWorldAdoptsSavedSeed</c> 专门用**不同**的 seed 去读档。
    /// </summary>
    public void RestoreSeed(int seed)
    {
        Seed = seed;
        _waterDistanceDirty = true;
        Revision++;
    }

    /// <summary>精确切比雪夫水距；水域变化后双向扫描重建，查询不消耗随机数。</summary>
    public int DistanceToWater(int x, int y, int maximum)
    {
        if (!IsInBounds(x, y)) { return maximum + 1; }
        if (_waterDistanceDirty || _waterDistance == null)
        {
            _waterDistance ??= new int[Tiles.Length];
            int far = Width + Height;
            for (int i = 0; i < Tiles.Length; i++) { _waterDistance[i] = Tiles[i].Terrain == TerrainKind.Water ? 0 : far; }
            for (int py = 0; py < Height; py++)
                for (int px = 0; px < Width; px++)
                {
                    int i = py * Width + px;
                    if (px > 0) { _waterDistance[i] = System.Math.Min(_waterDistance[i], _waterDistance[i - 1] + 1); }
                    if (py > 0)
                        for (int dx = -1; dx <= 1; dx++)
                            if (px + dx >= 0 && px + dx < Width) { _waterDistance[i] = System.Math.Min(_waterDistance[i], _waterDistance[i - Width + dx] + 1); }
                }
            for (int py = Height - 1; py >= 0; py--)
                for (int px = Width - 1; px >= 0; px--)
                {
                    int i = py * Width + px;
                    if (px + 1 < Width) { _waterDistance[i] = System.Math.Min(_waterDistance[i], _waterDistance[i + 1] + 1); }
                    if (py + 1 < Height)
                        for (int dx = -1; dx <= 1; dx++)
                            if (px + dx >= 0 && px + dx < Width) { _waterDistance[i] = System.Math.Min(_waterDistance[i], _waterDistance[i + Width + dx] + 1); }
                }
            _waterDistanceDirty = false;
        }
        return System.Math.Min(maximum + 1, _waterDistance[y * Width + x]);
    }
}
