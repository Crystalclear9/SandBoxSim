using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Environment;

/// <summary>Chunk 统计字段下标。用枚举 + 数组而非结构体字段，便于"按字段名取统计"的通用 UI。</summary>
public enum ChunkField
{
    /// <summary>肥沃度求和（除以 CellCount 得均值）。</summary>
    FertilitySum = 0,
    MoistureSum = 1,
    TemperatureSum = 2,
    VegetationSum = 3,

    ForestCount = 4,
    WaterCount = 5,
    MountainCount = 6,
    GrassCount = 7,
    FarmlandCount = 8,

    WalkableCount = 9,
    BuildableCount = 10,

    WoodAmount = 11,
    FoodAmount = 12,
    StoneAmount = 13,
    IronAmount = 14,
    BurningCount = 15,
}

/// <summary>
/// 单个 chunk 的聚合统计（第 74 条空间索引的存储单元）。
/// 这是**缓存**，不是真相：真相永远在 Tile 数组里。任何"缓存过期"的怀疑都通过 MarkDirty 解决。
/// </summary>
public struct ChunkStats
{
    public double[] Fields;

    /// <summary>本块的格子数（边缘块可能不满），用于把求和转成均值。</summary>
    public int CellCount;

    /// <summary>需要重算？</summary>
    public bool Dirty;

    public static ChunkStats Create() => new ChunkStats
    {
        Fields = new double[ChunkGrid.FieldCount],
        CellCount = 0,
        Dirty = true,
    };

    public void Clear()
    {
        if (Fields == null) { Fields = new double[ChunkGrid.FieldCount]; }
        for (int i = 0; i < ChunkGrid.FieldCount; i++) { Fields[i] = 0.0; }
        CellCount = 0;
    }

    public double Get(ChunkField field) => Fields[(int)field];
}

/// <summary>
/// 只读 chunk 统计视图：AI 与 UI 只能通过它读聚合数据（写入口只对 World 开放）。
/// </summary>
public readonly struct ChunkStatsReadOnly
{
    private readonly ChunkStats _stats;
    private readonly bool _valid;

    public readonly int ChunkX;
    public readonly int ChunkY;

    public ChunkStatsReadOnly(ChunkStats stats, int chunkX, int chunkY)
    {
        _stats = stats;
        ChunkX = chunkX;
        ChunkY = chunkY;
        _valid = stats.Fields != null;
    }

    public bool IsValid => _valid;
    public int CellCount => _valid ? _stats.CellCount : 0;

    public double Raw(ChunkField field) => _valid ? _stats.Get(field) : 0.0;

    public double Average(ChunkField field)
    {
        int cells = CellCount;
        return cells > 0 ? _stats.Get(field) / cells : 0.0;
    }

    public int ForestTiles => (int)Raw(ChunkField.ForestCount);
    public int WaterTiles => (int)Raw(ChunkField.WaterCount);
    public int MountainTiles => (int)Raw(ChunkField.MountainCount);
    public int GrassTiles => (int)Raw(ChunkField.GrassCount);
    public int FarmlandTiles => (int)Raw(ChunkField.FarmlandCount);
    public int WalkableTiles => (int)Raw(ChunkField.WalkableCount);
    public int BuildableTiles => (int)Raw(ChunkField.BuildableCount);
    public int BurningTiles => (int)Raw(ChunkField.BurningCount);

    public float WoodAmount => (float)Raw(ChunkField.WoodAmount);
    public float FoodAmount => (float)Raw(ChunkField.FoodAmount);
    public float StoneAmount => (float)Raw(ChunkField.StoneAmount);
    public float IronAmount => (float)Raw(ChunkField.IronAmount);

    public float AverageFertility => (float)Average(ChunkField.FertilitySum);
    public float AverageMoisture => (float)Average(ChunkField.MoistureSum);
    public float AverageTemperature => (float)Average(ChunkField.TemperatureSum);
    public float AverageVegetation => (float)Average(ChunkField.VegetationSum);

    /// <summary>按资源种类读总量（AI 选靶时按需求类型查询）。</summary>
    public float AmountOf(ResourceKind kind)
    {
        switch (kind)
        {
            case ResourceKind.Wood: return WoodAmount;
            case ResourceKind.Food: return FoodAmount;
            case ResourceKind.Stone: return StoneAmount;
            case ResourceKind.Iron: return IronAmount;
            default: return 0f;
        }
    }

    /// <summary>该 chunk 是否"看起来完全用不上"（快速剪枝）。</summary>
    public bool IsEmpty => CellCount == 0 || (WalkableTiles == 0 && WaterTiles == 0 && MountainTiles == 0);
}

/// <summary>
/// 空间索引（第 74 条）：把地图切成 ChunkSize×ChunkSize 的块，每块缓存聚合信息。
///
/// 为什么必须有它：NPC 的"附近有没有食物/木材/可建地"如果每次扫全图，
/// 100×100 地图 × 100 个 NPC × 每 tick 决策 = 每秒上千万次检查，模拟直接崩掉。
/// 有了 chunk 聚合，一次查询只需扫 3×3 = 9 个 chunk 的**统计值**，
/// 只有真正需要挑具体格子时才对少量候选格求值。
///
/// 维护策略：脏标记 + 惰性重算。世界生成后一次性全量计算；
/// 运行期地形/资源变化只把相关 chunk 标脏，Simulation 每 tick 调一次 Flush。
/// </summary>
public sealed class ChunkGrid
{
    public const int FieldCount = 16;

    private readonly ChunkStats[] _chunks;

    public int Width { get; }
    public int Height { get; }
    public int ChunkSize { get; }
    public int ChunkCols { get; }
    public int ChunkRows { get; }
    public int ChunkCount => ChunkCols * ChunkRows;

    private int _dirtyCount;

    public ChunkGrid(int width, int height, int chunkSize)
    {
        Width = width > 0 ? width : 1;
        Height = height > 0 ? height : 1;
        ChunkSize = chunkSize > 0 ? chunkSize : 16;

        ChunkCols = (Width + ChunkSize - 1) / ChunkSize;
        ChunkRows = (Height + ChunkSize - 1) / ChunkSize;
        _chunks = new ChunkStats[ChunkCount];
        for (int i = 0; i < _chunks.Length; i++) { _chunks[i] = ChunkStats.Create(); }
        _dirtyCount = ChunkCount;
    }

    public int DirtyCount => _dirtyCount;

    public int ChunkIndexAt(int tileX, int tileY) => ((tileY / ChunkSize) * ChunkCols) + (tileX / ChunkSize);

    public int ChunkIndexAtClamped(int tileX, int tileY)
    {
        int cx = SimMath.Clamp(tileX / ChunkSize, 0, ChunkCols - 1);
        int cy = SimMath.Clamp(tileY / ChunkSize, 0, ChunkRows - 1);
        return (cy * ChunkCols) + cx;
    }

    public int ChunkIndex(int chunkX, int chunkY) => (chunkY * ChunkCols) + chunkX;

    public bool IsValidChunk(int chunkX, int chunkY)
        => chunkX >= 0 && chunkY >= 0 && chunkX < ChunkCols && chunkY < ChunkRows;

    /// <summary>chunk 的格子范围（含端点，已裁剪到地图内）。</summary>
    public void GetBounds(int chunkX, int chunkY, out int minX, out int minY, out int maxX, out int maxY)
    {
        minX = chunkX * ChunkSize;
        minY = chunkY * ChunkSize;
        maxX = SimMath.Clamp(minX + ChunkSize - 1, 0, Width - 1);
        maxY = SimMath.Clamp(minY + ChunkSize - 1, 0, Height - 1);
    }

    public ChunkStatsReadOnly Read(int chunkX, int chunkY)
    {
        if (!IsValidChunk(chunkX, chunkY)) { return new ChunkStatsReadOnly(default, -1, -1); }
        return new ChunkStatsReadOnly(_chunks[ChunkIndex(chunkX, chunkY)], chunkX, chunkY);
    }

    public ChunkStatsReadOnly ReadIndex(int chunkIndex)
    {
        if (chunkIndex < 0 || chunkIndex >= ChunkCount) { return new ChunkStatsReadOnly(default, -1, -1); }
        return new ChunkStatsReadOnly(_chunks[chunkIndex], chunkIndex % ChunkCols, chunkIndex / ChunkCols);
    }

    /// <summary>按格子坐标直接读所在的 chunk（AI 邻域查询最常用的入口）。</summary>
    public ChunkStatsReadOnly ReadAt(int tileX, int tileY) => ReadIndex(ChunkIndexAtClamped(tileX, tileY));

    public void MarkDirty(int chunkIndex)
    {
        if (chunkIndex < 0 || chunkIndex >= ChunkCount) { return; }
        ChunkStats s = _chunks[chunkIndex];
        if (!s.Dirty)
        {
            s.Dirty = true;
            _chunks[chunkIndex] = s;
            _dirtyCount++;
        }
    }

    public void MarkAtDirty(int tileX, int tileY) => MarkDirty(ChunkIndexAtClamped(tileX, tileY));

    public void MarkAllDirty()
    {
        for (int i = 0; i < _chunks.Length; i++)
        {
            ChunkStats s = _chunks[i];
            s.Dirty = true;
            _chunks[i] = s;
        }
        _dirtyCount = ChunkCount;
    }

    public void Clear()
    {
        for (int i = 0; i < _chunks.Length; i++)
        {
            ChunkStats s = _chunks[i];
            s.Clear();
            s.Dirty = true;
            _chunks[i] = s;
        }
        _dirtyCount = ChunkCount;
    }

    /// <summary>
    /// 重算所有脏 chunk。调用频率由 Simulation 控制（每 tick 一次即可；脏块通常是个位数）。
    /// </summary>
    public void Refresh(Tile[] tiles)
    {
        if (_dirtyCount == 0) { return; }

        for (int cy = 0; cy < ChunkRows; cy++)
        {
            for (int cx = 0; cx < ChunkCols; cx++)
            {
                int idx = ChunkIndex(cx, cy);
                ChunkStats s = _chunks[idx];
                if (!s.Dirty) { continue; }

                s.Clear();
                GetBounds(cx, cy, out int minX, out int minY, out int maxX, out int maxY);

                int cells = 0;
                for (int y = minY; y <= maxY; y++)
                {
                    int rowBase = y * Width;
                    for (int x = minX; x <= maxX; x++)
                    {
                        ref Tile t = ref tiles[rowBase + x];
                        cells++;

                        s.Fields[(int)ChunkField.FertilitySum] += t.Fertility;
                        s.Fields[(int)ChunkField.MoistureSum] += t.Moisture;
                        s.Fields[(int)ChunkField.TemperatureSum] += t.Temperature;
                        s.Fields[(int)ChunkField.VegetationSum] += t.Vegetation;

                        switch (t.Terrain)
                        {
                            case TerrainKind.Forest: s.Fields[(int)ChunkField.ForestCount]++; break;
                            case TerrainKind.Water: s.Fields[(int)ChunkField.WaterCount]++; break;
                            case TerrainKind.Mountain: s.Fields[(int)ChunkField.MountainCount]++; break;
                            case TerrainKind.Grass: s.Fields[(int)ChunkField.GrassCount]++; break;
                            case TerrainKind.Farmland: s.Fields[(int)ChunkField.FarmlandCount]++; break;
                            default: break;
                        }

                        if (t.Walkable) { s.Fields[(int)ChunkField.WalkableCount]++; }
                        if (t.Buildable) { s.Fields[(int)ChunkField.BuildableCount]++; }
                        if (t.Fire == FireState.Burning) { s.Fields[(int)ChunkField.BurningCount]++; }

                        if (t.HasResource)
                        {
                            float amount = t.Resource.Amount;
                            switch (t.Resource.Kind)
                            {
                                case ResourceKind.Wood: s.Fields[(int)ChunkField.WoodAmount] += amount; break;
                                case ResourceKind.Food: s.Fields[(int)ChunkField.FoodAmount] += amount; break;
                                case ResourceKind.Stone: s.Fields[(int)ChunkField.StoneAmount] += amount; break;
                                case ResourceKind.Iron: s.Fields[(int)ChunkField.IronAmount] += amount; break;
                                default: break;
                            }
                        }
                    }
                }

                s.CellCount = cells;
                s.Dirty = false;
                _chunks[idx] = s;
                _dirtyCount--;
            }
        }
    }

    // ---- 存档 ----

    public void ExportState(out double[][] fields, out int[] cellCounts, out bool[] dirty)
    {
        fields = new double[ChunkCount][];
        cellCounts = new int[ChunkCount];
        dirty = new bool[ChunkCount];
        for (int i = 0; i < ChunkCount; i++)
        {
            fields[i] = _chunks[i].Fields;
            cellCounts[i] = _chunks[i].CellCount;
            dirty[i] = _chunks[i].Dirty;
        }
    }

    public void RestoreState(double[][]? fields, int[]? cellCounts, bool[]? dirty)
    {
        _dirtyCount = 0;
        for (int i = 0; i < ChunkCount; i++)
        {
            ChunkStats s = _chunks[i];
            if (s.Fields == null) { s.Fields = new double[FieldCount]; }
            if (fields != null && i < fields.Length && fields[i] != null && fields[i].Length == FieldCount)
            {
                System.Array.Copy(fields[i], s.Fields, FieldCount);
            }
            s.CellCount = cellCounts != null && i < cellCounts.Length ? cellCounts[i] : 0;
            s.Dirty = dirty != null && i < dirty.Length && dirty[i];
            if (s.Dirty) { _dirtyCount++; }
            _chunks[i] = s;
        }
    }
}
