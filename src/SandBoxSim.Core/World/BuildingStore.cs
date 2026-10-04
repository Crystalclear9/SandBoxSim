using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Environment;

/// <summary>建筑种类（M3）。</summary>
public enum BuildingKind : byte
{
    None = 0,

    /// <summary>住房：提供床位。没有床位的成年人不会生孩子（M4）。</summary>
    House = 1,

    /// <summary>仓库：聚落级共享库存。存放/取回优先走它（而不是地面堆）。</summary>
    Storage = 2,

    /// <summary>农田（M4）：开垦后可被耕种。</summary>
    Farm = 3,

    /// <summary>矿场（M3 占位）：在山地附近提升开采效率。</summary>
    Mine = 4,
}

/// <summary>建筑施工状态。</summary>
public enum BuildingState : byte
{
    /// <summary>已定型但还没动工（扣料发生在定型时）。</summary>
    Planned = 0,

    /// <summary>施工中（按每 10 tick 推进一次）。</summary>
    UnderConstruction = 1,

    /// <summary>已完工（可以住人/存货）。</summary>
    Complete = 2,
}

/// <summary>
/// 建筑配方（第 35 节）：造价、占地、建造耗时、提供的容量。
///
/// **全部数据驱动**：新增一种建筑只需要加一条配方 + 一个枚举值，
/// 建造动作、建造系统、UI、报告都不需要改。
/// 这是"最小规则 + 强耦合"的具体做法：规则数量少，但每条规则都被很多系统复用。
/// </summary>
public readonly struct BuildingRecipe
{
    public readonly BuildingKind Kind;
    public readonly string DisplayName;

    /// <summary>木材造价。</summary>
    public readonly float WoodCost;

    /// <summary>石料造价。</summary>
    public readonly float StoneCost;

    /// <summary>需要的施工 tick 数（每 10 tick 推进一次，因此实际是 tick/10 次）。</summary>
    public readonly int BuildWorkTicks;

    /// <summary>提供的床位（House）。</summary>
    public readonly int Beds;

    /// <summary>提供的库存容量（Storage，每种资源）。</summary>
    public readonly float StorageCapacity;

    /// <summary>可建造的地形集合；null 表示"任意可建造地形"。</summary>
    public readonly TerrainKind[]? AllowedTerrains;

    /// <summary>是否需要临水（农田对灌溉的需求）。</summary>
    public readonly bool RequiresWaterAccess;

    public BuildingRecipe(
        BuildingKind kind,
        string displayName,
        float woodCost,
        float stoneCost,
        int buildWorkTicks,
        int beds = 0,
        float storageCapacity = 0f,
        TerrainKind[]? allowedTerrains = null,
        bool requiresWaterAccess = false)
    {
        Kind = kind;
        DisplayName = displayName;
        WoodCost = woodCost;
        StoneCost = stoneCost;
        BuildWorkTicks = buildWorkTicks;
        Beds = beds;
        StorageCapacity = storageCapacity;
        AllowedTerrains = allowedTerrains;
        RequiresWaterAccess = requiresWaterAccess;
    }

    /// <summary>这一格的地形是否允许建这种建筑。</summary>
    public bool AllowsTerrain(TerrainKind terrain)
    {
        if (AllowedTerrains == null || AllowedTerrains.Length == 0) { return true; }
        for (int i = 0; i < AllowedTerrains.Length; i++)
        {
            if (AllowedTerrains[i] == terrain) { return true; }
        }
        return false;
    }

    public float TotalCost => WoodCost + StoneCost;
}

/// <summary>建筑配方的查表（第 35 节的具体数值）。</summary>
public static class BuildingRegistry
{
    private static readonly BuildingRecipe[] Recipes = BuildRecipes();

    private static BuildingRecipe[] BuildRecipes()
    {
        var recipes = new BuildingRecipe[8];

        // 数值设计原则（第 88 条：什么都不给免费）：
        //   * 一间房子 = 约 2.5 次采伐（每次 8 木材）⇒ 一天以内能盖起来，但不至于随手就盖；
        //   * 仓库比房子贵（因为它带来的是"共享"这种结构性收益）；
        //   * 石料只用在仓库上，让"采石"在 M3 就有明确用途，而不是纯占位。
        //
        // 允许的地形是**显式列举**而不是"排除法"：
        // 山地是可走也可建造的（矿场需要它），因此不能用"可建造"当住房的判据 ——
        // 那会得到"房子盖在山顶上"。这类"规则表里的格子其实是给别的用途的"情况，
        // 只能靠显式列举来表达意图。
        TerrainKind[] flat = { TerrainKind.Grass, TerrainKind.Forest, TerrainKind.Sand };
        TerrainKind[] mountain = { TerrainKind.Mountain };
        TerrainKind[] farm = { TerrainKind.Grass };

        recipes[(int)BuildingKind.House] = new BuildingRecipe(
            BuildingKind.House, "住房",
            woodCost: 20f, stoneCost: 0f, buildWorkTicks: 200,
            beds: 2,
            allowedTerrains: flat);

        recipes[(int)BuildingKind.Storage] = new BuildingRecipe(
            BuildingKind.Storage, "仓库",
            woodCost: 40f, stoneCost: 10f, buildWorkTicks: 300,
            storageCapacity: 600f,
            allowedTerrains: flat);

        recipes[(int)BuildingKind.Farm] = new BuildingRecipe(
            BuildingKind.Farm, "农田",
            woodCost: 10f, stoneCost: 0f, buildWorkTicks: 150,
            allowedTerrains: farm,
            requiresWaterAccess: true);

        recipes[(int)BuildingKind.Mine] = new BuildingRecipe(
            BuildingKind.Mine, "矿场",
            woodCost: 15f, stoneCost: 5f, buildWorkTicks: 250,
            allowedTerrains: mountain);

        return recipes;
    }

    public static BuildingRecipe Of(BuildingKind kind)
    {
        int index = (int)kind;
        if (index <= 0 || index >= Recipes.Length) { return default; }
        return Recipes[index];
    }

    public static string NameOf(BuildingKind kind)
    {
        BuildingRecipe recipe = Of(kind);
        return string.IsNullOrEmpty(recipe.DisplayName) ? kind.ToString() : recipe.DisplayName;
    }

    /// <summary>所有可建造的建筑（决定 UI 里的建造选项顺序）。</summary>
    public static readonly BuildingKind[] Buildable =
    {
        BuildingKind.House,
        BuildingKind.Storage,
        BuildingKind.Farm,
        BuildingKind.Mine,
    };
}

/// <summary>
/// 建筑存储（M3）。
///
/// 与个体/动物一样用 SoA：建筑数量会到几百，而且每 10 tick 要遍历一次推进施工，
/// 因此"没有对象、只有数组"在这里同样重要。
///
/// 地形侧的连接：每个建筑通过 <see cref="Tile.BuildingId"/> 锚定在格子上。
/// 为什么不直接在 Tile 里存建筑数据：Tile 是 10 万格级别的热数据，
/// 每个格子都带一份建筑字段（哪怕绝大多数是空的）会浪费内存与带宽。
/// **稀疏数据用稀疏表示，Tile 里只留一个索引。**
/// </summary>
public sealed class BuildingStore : ISimEntitySet
{
    private bool[] _alive = System.Array.Empty<bool>();
    private byte[] _kind = System.Array.Empty<byte>();
    private byte[] _state = System.Array.Empty<byte>();
    private int[] _x = System.Array.Empty<int>();
    private int[] _y = System.Array.Empty<int>();
    private int[] _workDone = System.Array.Empty<int>();
    private int[] _workRequired = System.Array.Empty<int>();
    private int[] _generation = System.Array.Empty<int>();
    private long[] _builtTick = System.Array.Empty<long>();

    // ---- M4：床位占用、农田劳动量、完整度 ----
    //
    // 三者都进状态摘要：床位是出生的硬门、劳动量决定农田产量、完整度决定拆除时机。
    // 判据与 Phase 0 那六个"隐形状态"相同：**它会不会影响未来的行为**。

    /// <summary>该建筑当前占用了几张床（只有住房用）。</summary>
    private int[] _occupiedBeds = System.Array.Empty<int>();

    /// <summary>该建筑累积的劳动量（只有农田用，每日清零）。</summary>
    private float[] _labor = System.Array.Empty<float>();

    /// <summary>完整度 [0,1]：无人使用时每日下降，归零即拆除。</summary>
    private float[] _decay = System.Array.Empty<float>();

    /// <summary>存活索引列表（无空洞；建筑只增不减，但拆除会用到删除）。</summary>
    private int[] _live = System.Array.Empty<int>();
    private int _liveCount;
    private int[] _liveIndexOfSlot = System.Array.Empty<int>();
    private int _nextFreeHint;

    /// <summary>槽位 → 该建筑占据的 Tile 索引序列（用于拆除时清理 Tile.BuildingId）。</summary>
    private System.Collections.Generic.List<int>[] _tiles;

    public int Capacity => _alive.Length;
    public int EntityCount => _liveCount;
    public int LiveCount => _liveCount;

    // ---- 建筑带来的容量汇总（每次完工/拆除时增量维护，避免每 tick 全量统计） ----

    /// <summary>已完成住房提供的总床位。</summary>
    public int TotalBeds { get; private set; }

    /// <summary>完工的仓库数量。</summary>
    public int CompletedStorages { get; private set; }

    /// <summary>按种类统计的**已完工**建筑数。</summary>
    private readonly int[] _completedByKind = new int[8];

    /// <summary>累计建成与拆除数量。</summary>
    public int TotalBuilt { get; private set; }
    public int TotalDemolished { get; private set; }

    public BuildingStore(int capacity = 256)
    {
        int size = capacity > 0 ? capacity : 256;
        Resize(size);
        _tiles = new System.Collections.Generic.List<int>[size];
        for (int i = 0; i < size; i++) { _tiles[i] = new System.Collections.Generic.List<int>(4); }
    }

    private void Resize(int capacity)
    {
        if (capacity < 1) { capacity = 1; }
        System.Array.Resize(ref _alive, capacity);
        System.Array.Resize(ref _kind, capacity);
        System.Array.Resize(ref _state, capacity);
        System.Array.Resize(ref _x, capacity);
        System.Array.Resize(ref _y, capacity);
        System.Array.Resize(ref _workDone, capacity);
        System.Array.Resize(ref _workRequired, capacity);
        System.Array.Resize(ref _generation, capacity);
        System.Array.Resize(ref _builtTick, capacity);
        System.Array.Resize(ref _live, capacity);
        System.Array.Resize(ref _liveIndexOfSlot, capacity);

        // M4 新数组：扩容出来的部分是 0，而 `_decay` 的"满耐久"是 1 ——
        // 必须显式填 1，否则新建的建筑会一上来就处于"已腐烂"状态。
        int previous = _occupiedBeds.Length;
        System.Array.Resize(ref _occupiedBeds, capacity);
        System.Array.Resize(ref _labor, capacity);
        System.Array.Resize(ref _decay, capacity);
        for (int i = previous; i < capacity; i++) { _decay[i] = 1f; }

        for (int i = 0; i < capacity; i++) { _liveIndexOfSlot[i] = -1; }
    }

    public void EnsureCapacity(int required)
    {
        if (required <= _alive.Length) { return; }
        int next = _alive.Length;
        while (next < required) { next *= 2; }
        Resize(next);

        // 注意：扩容必须同步扩容"占据格子列表"数组，否则会 IndexOutOfRange
        // （这是一个只有建筑数量超过初始容量时才会出现的问题，很容易漏测）。
        System.Array.Resize(ref _tiles, next);
        for (int i = 0; i < next; i++)
        {
            if (_tiles[i] == null) { _tiles[i] = new System.Collections.Generic.List<int>(4); }
        }
    }

    public bool IsAlive(int index) => index >= 0 && index < _alive.Length && _alive[index];
    public BuildingKind KindOf(int index) => (BuildingKind)_kind[index];
    public BuildingState StateOf(int index) => (BuildingState)_state[index];
    public Int2 PositionOf(int index) => new Int2(_x[index], _y[index]);
    public int XOf(int index) => _x[index];
    public int YOf(int index) => _y[index];
    public int WorkDoneOf(int index) => _workDone[index];
    public int WorkRequiredOf(int index) => _workRequired[index];
    public long BuiltTickOf(int index) => _builtTick[index];
    public int GenerationOf(int index) => _generation[index];

    /// <summary>施工进度 [0,1]。</summary>
    public float ProgressOf(int index)
    {
        int required = _workRequired[index];
        if (required <= 0) { return 1f; }
        return SimMath.Clamp01((float)_workDone[index] / required);
    }

    public int CountOf(BuildingKind kind)
    {
        int i = (int)kind;
        return i >= 0 && i < _completedByKind.Length ? _completedByKind[i] : 0;
    }

    public int TotalCompleted
    {
        get
        {
            int total = 0;
            for (int i = 0; i < _completedByKind.Length; i++) { total += _completedByKind[i]; }
            return total;
        }
    }

    public System.Collections.Generic.IReadOnlyList<int> TilesOf(int index)
        => index >= 0 && index < _tiles.Length && _tiles[index] != null
            ? _tiles[index]
            : System.Array.Empty<int>();

    /// <summary>按存活列表顺序遍历（建筑只增不减，顺序稳定）。</summary>
    public int LiveAt(int liveIndex) => _live[liveIndex];

    /// <summary>所有存活建筑的索引（槽位升序；确定性）。</summary>
    public int[] AliveIndices()
    {
        int[] result = new int[_liveCount];
        for (int i = 0; i < _liveCount; i++) { result[i] = _live[i]; }
        System.Array.Sort(result);
        return result;
    }

    /// <summary>
    /// 放置一个建筑（扣料由调用方负责 —— 建筑存储不管经济，只管空间）。
    /// 返回槽位索引，-1 表示失败（容量不足或地形不允许）。
    /// </summary>
    public int Place(
        Environment.World world,
        BuildingKind kind,
        int x,
        int y,
        int workRequired)
    {
        BuildingRecipe recipe = BuildingRegistry.Of(kind);
        if (recipe.Kind == BuildingKind.None) { return -1; }
        if (!CanPlaceAt(world, kind, x, y)) { return -1; }

        EnsureCapacity(_liveCount + 1);

        int slot = AllocateSlot();
        if (slot < 0) { return -1; }

        _alive[slot] = true;
        _kind[slot] = (byte)kind;
        _state[slot] = (byte)BuildingState.UnderConstruction;
        _x[slot] = x;
        _y[slot] = y;
        _workDone[slot] = 0;
        _workRequired[slot] = workRequired > 0 ? workRequired : recipe.BuildWorkTicks;
        _builtTick[slot] = 0;

        // 占地：M3 只有 1×1 建筑（多格建筑留到需要时再加）。
        // 用一个显式的列表而不是硬编码"只有一格"，是为了让"多格建筑"将来只是一次循环改动。
        _tiles[slot].Clear();
        _tiles[slot].Add((y * world.Width) + x);
        world.Tiles[(y * world.Width) + x].BuildingId = slot + 1;
        world.NotifyNavigationChanged();

        return slot;
    }

    private int AllocateSlot()
    {
        for (int i = _nextFreeHint; i < _alive.Length; i++)
        {
            if (_alive[i]) { continue; }
            AddLive(i);
            _nextFreeHint = i;
            return i;
        }
        for (int i = 0; i < _alive.Length; i++)
        {
            if (_alive[i]) { continue; }
            AddLive(i);
            _nextFreeHint = i;
            return i;
        }
        return -1;
    }

    private void AddLive(int index)
    {
        _live[_liveCount] = index;
        _liveIndexOfSlot[index] = _liveCount;
        _liveCount++;
    }

    /// <summary>推进施工。返回 true 表示本 tick 完工。</summary>
    public bool AdvanceWork(int index, int amount)
    {
        if (!IsAlive(index)) { return false; }
        if (StateOf(index) != BuildingState.UnderConstruction) { return false; }

        _workDone[index] += amount;
        if (_workDone[index] < _workRequired[index]) { return false; }

        _workDone[index] = _workRequired[index];
        return true;
    }

    /// <summary>标记完工（由建筑系统在扣减后调用，负责汇总容量的增量维护）。</summary>
    public void MarkComplete(int index, long tick)
    {
        if (!IsAlive(index)) { return; }
        if (StateOf(index) == BuildingState.Complete) { return; }

        _state[index] = (byte)BuildingState.Complete;
        _builtTick[index] = tick;
        TotalBuilt++;

        BuildingKind kind = KindOf(index);
        int kindIndex = (int)kind;
        if (kindIndex >= 0 && kindIndex < _completedByKind.Length) { _completedByKind[kindIndex]++; }

        BuildingRecipe recipe = BuildingRegistry.Of(kind);
        TotalBeds += recipe.Beds;
        if (kind == BuildingKind.Storage) { CompletedStorages++; }
    }

    /// <summary>拆除（M5 的灾害/玩家工具会用到）。</summary>
    public void Demolish(Environment.World world, int index)
    {
        if (!IsAlive(index)) { return; }

        BuildingKind kind = KindOf(index);
        BuildingRecipe recipe = BuildingRegistry.Of(kind);
        bool wasComplete = StateOf(index) == BuildingState.Complete;

        OccupiedBeds = System.Math.Max(0, OccupiedBeds - _occupiedBeds[index]);
        _occupiedBeds[index] = 0;

        if (wasComplete)
        {
            TotalBeds -= recipe.Beds;
            if (kind == BuildingKind.Storage) { CompletedStorages--; }
            int kindIndex = (int)kind;
            if (kindIndex >= 0 && kindIndex < _completedByKind.Length) { _completedByKind[kindIndex]--; }
        }

        // 清掉地形上的锚点
        System.Collections.Generic.List<int> tiles = _tiles[index];
        for (int i = 0; i < tiles.Count; i++)
        {
            int flat = tiles[i];
            if (flat < 0 || flat >= world.Tiles.Length) { continue; }
            if (world.Tiles[flat].BuildingId == index + 1) { world.Tiles[flat].BuildingId = 0; }
        }
        tiles.Clear();
        world.NotifyNavigationChanged();

        _alive[index] = false;
        _generation[index]++;
        TotalDemolished++;

        int liveIndex = _liveIndexOfSlot[index];
        if (liveIndex >= 0 && liveIndex < _liveCount)
        {
            int last = _live[_liveCount - 1];
            _live[liveIndex] = last;
            _liveIndexOfSlot[last] = liveIndex;
            _liveCount--;
        }
        _liveIndexOfSlot[index] = -1;

        if (index < _nextFreeHint) { _nextFreeHint = index; }
    }

    /// <summary>
    /// 判断某个位置能不能建。这是**唯一的选址规则入口** ——
    /// 动作层、UI、测试都必须走它，否则会出现"玩家能建而 AI 不能建"这类不一致。
    /// </summary>
    public static bool CanPlaceAt(Environment.World world, BuildingKind kind, int x, int y)
    {
        if (!world.IsInBounds(x, y)) { return false; }

        Tile tile = world.TileAt(x, y);
        BuildingRecipe recipe = BuildingRegistry.Of(kind);
        if (recipe.Kind == BuildingKind.None) { return false; }

        if (tile.BuildingId != 0) { return false; }              // 已被占用
        if (!tile.Walkable || !tile.Buildable) { return false; } // Respect persistent per-tile overrides.

        // 道路/农田上不建（避免把已有的功能性格子覆盖掉）
        if (tile.Terrain == TerrainKind.Road || tile.Terrain == TerrainKind.Farmland) { return false; }

        if (!recipe.AllowsTerrain(tile.Terrain)) { return false; }

        if (recipe.RequiresWaterAccess && !HasWaterAccess(world, x, y)) { return false; }

        return true;
    }

    /// <summary>四邻是否有水（农田的灌溉需求）。</summary>
    public static bool HasWaterAccess(Environment.World world, int x, int y)
    {
        for (int d = 0; d < 4; d++)
        {
            int nx = x;
            int ny = y;
            switch (d)
            {
                case 0: nx++; break;
                case 1: nx--; break;
                case 2: ny++; break;
                default: ny--; break;
            }

            if (!world.IsInBounds(nx, ny)) { continue; }
            if (world.TileAt(nx, ny).Terrain == TerrainKind.Water) { return true; }
        }
        return false;
    }

    /// <summary>
    /// M4：把一条存档里的建筑精确恢复到指定槽位（读档专用）。
    ///
    /// 建筑通过 <c>Tile.BuildingId</c> 锚定在格子上，因此读档顺序必须是
    /// **先恢复地形（含 BuildingId）再恢复建筑** —— 否则锚点会指向还不存在的槽位，
    /// 表现为"读档后建筑消失但 Tile 上还写着有建筑"。
    /// 这个顺序由 <c>SaveFile.Load</c> 保证，并在那里写了注释。
    /// </summary>
    public void RestoreBuilding(
        int slot,
        BuildingKind kind,
        BuildingState state,
        int x,
        int y,
        int workDone,
        int workRequired,
        long builtTick)
        => RestoreBuilding(slot, kind, state, x, y, workDone, workRequired, builtTick,
            0, 0f, 0f);

    /// <summary>
    /// M4 版：额外恢复床位占用、劳动量与衰减。
    ///
    /// 这三项都**影响未来行为**（床位是出生的硬门、劳动量决定农田产量、衰减决定拆除），
    /// 因此既进摘要也进存档 —— 与 Phase 0 那六个"隐形状态"同一个判据。
    /// </summary>
    public void RestoreBuilding(
        int slot,
        BuildingKind kind,
        BuildingState state,
        int x,
        int y,
        int workDone,
        int workRequired,
        long builtTick,
        int occupiedBeds,
        float labor,
        float decay)
    {
        EnsureCapacity(slot + 1);

        _alive[slot] = true;
        _kind[slot] = (byte)kind;
        _state[slot] = (byte)state;
        _x[slot] = x;
        _y[slot] = y;
        _workDone[slot] = workDone;
        _workRequired[slot] = workRequired > 0 ? workRequired : 1;
        _builtTick[slot] = builtTick;

        _occupiedBeds[slot] = occupiedBeds < 0 ? 0 : occupiedBeds;
        _labor[slot] = labor < 0f ? 0f : labor;
        _decay[slot] = SimMath.Clamp01(decay);

        _tiles[slot].Clear();
        _tiles[slot].Add((y * _lastKnownWidth) + x);

        AddLive(slot);

        // 汇总统计必须跟着一起恢复，否则"床位 / 仓库数"会在读档后归零，
        // 而它们驱动着建造决策 —— 表现为"读档后突然开始疯狂盖房子"。
        if (state == BuildingState.Complete)
        {
            TotalBuilt++;
            int kindIndex = (int)kind;
            if (kindIndex >= 0 && kindIndex < _completedByKind.Length) { _completedByKind[kindIndex]++; }

            BuildingRecipe recipe = BuildingRegistry.Of(kind);
            TotalBeds += recipe.Beds;
            if (kind == BuildingKind.Storage) { CompletedStorages++; }
        }
    }

    // ---------------------------------------------------------------------
    // M4：床位占用、农田劳动量、建筑衰减
    //
    // 三者都是**新的持久状态**，因此都有对应的存档字段与摘要字段。
    // ---------------------------------------------------------------------

    /// <summary>已占用的床位数。</summary>
    public int OccupiedBeds { get; private set; }

    /// <summary>空闲床位数 —— 它是出生的**硬门**（没床位就生不了孩子）。</summary>
    public int FreeBeds => TotalBeds - OccupiedBeds;

    /// <summary>占用一个床位。返回 false 表示已满。</summary>
    public bool TryOccupyBed()
    {
        if (OccupiedBeds >= TotalBeds) { return false; }
        OccupiedBeds++;
        return true;
    }

    /// <summary>释放一个床位（个体死亡或迁出时调用）。</summary>
    public void ReleaseBed()
    {
        if (OccupiedBeds > 0) { OccupiedBeds--; }
    }

    /// <summary>某块农田累积的劳动量（M4）。</summary>
    public float LaborOf(int index) => index >= 0 && index < _labor.Length ? _labor[index] : 0f;

    /// <summary>
    /// 槽位分配提示（**M4 起必须进存档**）。
    ///
    /// 与 `AgentStore.NextFreeHint` / `WildlifeStore.NextFreeHint` 是同一类东西：
    /// 在 M4 之前建筑只增不减，因此"下一个空槽在哪"不影响模拟；
    /// M4 引入**衰减拆除**之后，空槽会重新出现，
    /// 于是它决定"下一栋建筑落在哪个槽位"，而槽位进状态摘要 ⇒ 读档后会分叉。
    ///
    /// 这已经是第三个同类字段了（野生动物、个体、建筑各一个）。
    /// 它们的共同特征是：**看起来只是分配优化，实际决定了持久标识**。
    /// </summary>
    public int NextFreeHint
    {
        get => _nextFreeHint;
        set => _nextFreeHint = value < 0 ? 0 : value;
    }

    /// <summary>该建筑当前占用了几张床（只有住房用）。</summary>
    public int OccupiedBedsOf(int index) => index >= 0 && index < _occupiedBeds.Length ? _occupiedBeds[index] : 0;

    /// <summary>占用该建筑的一个床位（个体搬入时调用）。返回 false 表示没有空床。</summary>
    public bool TryOccupyBedOf(int index)
    {
        if (index < 0 || index >= _occupiedBeds.Length) { return false; }
        BuildingRecipe recipe = BuildingRegistry.Of((BuildingKind)_kind[index]);
        if (recipe.Beds <= 0) { return false; }
        if (_occupiedBeds[index] >= recipe.Beds) { return false; }
        _occupiedBeds[index]++;
        OccupiedBeds++;
        return true;
    }

    /// <summary>释放该建筑的一个床位（个体死亡或迁出时调用）。</summary>
    public void ReleaseBedOf(int index)
    {
        if (index < 0 || index >= _occupiedBeds.Length) { return; }
        if (_occupiedBeds[index] <= 0) { return; }
        _occupiedBeds[index]--;
        if (OccupiedBeds > 0) { OccupiedBeds--; }
    }

    /// <summary>该建筑还有没有空床。</summary>
    public bool HasFreeBed(int index)
    {
        if (index < 0 || index >= _occupiedBeds.Length) { return false; }
        return _occupiedBeds[index] < BuildingRegistry.Of((BuildingKind)_kind[index]).Beds;
    }

    /// <summary>把劳动量记到某块农田上（Farm 动作完成时调用）。</summary>
    public void AddLabor(int index, float amount)
    {
        if (index < 0 || index >= _labor.Length) { return; }
        _labor[index] += amount < 0f ? 0f : amount;
    }

    /// <summary>清空劳动量（每日结算后调用）。</summary>
    public void ClearLabor(int index)
    {
        if (index < 0 || index >= _labor.Length) { return; }
        _labor[index] = 0f;
    }

    /// <summary>
    /// 推进一次衰减评估（每日调用），把"衰减到 0、该拆了"的建筑索引填进 <paramref name="outDemolish"/>。
    ///
    /// 为什么**不**在这里直接拆：拆除需要清掉 `Tile.BuildingId` 锚点，也就是需要 `World`。
    /// 让 <see cref="BuildingStore"/> 持有 World 引用会破坏分层
    /// （见 docs/02 的依赖方向），所以这里只产出"待拆清单"，
    /// 由持有 World 的 <c>BuildingSystem</c> 调用既有的 <see cref="Demolish"/> 完成。
    ///
    /// 规则：**只有"被使用"的建筑才不掉耐久**。M4 里"被使用"的判据是
    /// "有住户"（住房）或"有劳动量"（农田）。这条规则刻意选得朴素：
    /// 它让"房子空着就会坏"变成玩家能一眼理解、也能干预（往里放人）的机制。
    /// </summary>
    /// <returns>本次进入"待拆"状态的建筑数。</returns>
    public int TickDecay(
        long tick,
        float decayPerDay,
        float graceDays,
        System.Collections.Generic.List<int> outDemolish,
        int ticksPerDay)
    {
        if (outDemolish == null) { return 0; }

        int decayed = 0;
        long graceTicks = (long)(graceDays * ticksPerDay);

        for (int k = 0; k < _liveCount; k++)
        {
            int index = _live[k];
            if (index < 0 || !_alive[index]) { continue; }
            if (_state[index] != (byte)BuildingState.Complete) { continue; }

            bool used = _occupiedBeds[index] > 0 || _labor[index] > 0f;
            if (used)
            {
                // 被使用的建筑会**缓慢自我修复**（有人住就会顺手修）
                if (_decay[index] < 1f)
                {
                    _decay[index] += decayPerDay * 2f;
                    if (_decay[index] > 1f) { _decay[index] = 1f; }
                }
                continue;
            }

            // 宽限期：刚建成（或刚被腾空）的建筑不会立刻开始掉耐久，
            // 否则"刚盖好就开始烂"会让建造显得毫无意义。
            if (tick - _builtTick[index] < graceTicks) { continue; }

            _decay[index] -= decayPerDay;
            if (_decay[index] > 0f) { continue; }

            _decay[index] = 0f;
            outDemolish.Add(index);
            decayed++;
        }

        return decayed;
    }

    /// <summary>建筑完整度 [0,1]（M4：无人维护会衰减）。</summary>
    public float DecayOf(int index) => index >= 0 && index < _decay.Length ? _decay[index] : 1f;

    /// <summary>读档时直接设定完整度。</summary>
    public void SetDecay(int index, float value)
    {
        if (index >= 0 && index < _decay.Length) { _decay[index] = SimMath.Clamp01(value); }
    }

    /// <summary>读档时直接设定床位占用。</summary>
    public void RestoreOccupiedBeds(int value) => OccupiedBeds = value < 0 ? 0 : value;

    /// <summary>
    /// 读档时需要知道地图宽度才能算出占据格的扁平索引。
    /// 由一个显式的设置方法传入，而不是让 BuildingStore 持有 World 引用 ——
    /// 后者会让"建筑存储"与"世界"互相依赖，破坏分层（见 docs/02 的依赖方向）。
    /// </summary>
    private int _lastKnownWidth = 1;

    public void SetWorldWidth(int width) => _lastKnownWidth = width > 0 ? width : 1;

    /// <summary>清空全部建筑但保留容量（读档前调用）。</summary>
    public void ClearAllKeepCapacity()
    {
        for (int i = 0; i < _alive.Length; i++)
        {
            _alive[i] = false;
            _liveIndexOfSlot[i] = -1;
            _tiles[i]?.Clear();
            _occupiedBeds[i] = 0;
            _labor[i] = 0f;
            _decay[i] = 1f;
        }
        _liveCount = 0;
        _nextFreeHint = 0;
        OccupiedBeds = 0;
        TotalBeds = 0;
        CompletedStorages = 0;
        TotalBuilt = 0;
        TotalDemolished = 0;
        for (int i = 0; i < _completedByKind.Length; i++) { _completedByKind[i] = 0; }
    }

    public ulong HashInto(ulong hash)
    {
        hash = Hash64.Combine(hash, _liveCount);
        // 床位占用是汇总值，但它决定"还有没有空床"，而空床是出生的硬门 ⇒ 必须进摘要。
        hash = Hash64.Combine(hash, OccupiedBeds);
        // 按槽位升序（不是存活列表顺序）：存活列表的末尾交换会让顺序变化，
        // 从而破坏"同状态同摘要"（与野生动物/物资堆同一个教训）。
        for (int i = 0; i < _alive.Length; i++)
        {
            if (!_alive[i]) { continue; }
            hash = Hash64.Combine(hash, i);
            hash = Hash64.Combine(hash, _kind[i]);
            hash = Hash64.Combine(hash, _state[i]);
            hash = Hash64.Combine(hash, _x[i]);
            hash = Hash64.Combine(hash, _y[i]);
            hash = Hash64.Combine(hash, _workDone[i]);

            // M4：床位占用、农田劳动量、完整度 —— 三者都影响未来行为
            hash = Hash64.Combine(hash, _occupiedBeds[i]);
            hash = Hash64.Combine(hash, (int)(_labor[i] * 100f));
            hash = Hash64.Combine(hash, (int)(_decay[i] * 1000f));
        }
        return hash;
    }

    public void Reset()
    {
        for (int i = 0; i < _alive.Length; i++)
        {
            if (_alive[i]) { _generation[i]++; }
            _alive[i] = false;
            _liveIndexOfSlot[i] = -1;
            _tiles[i]?.Clear();
        }
        _liveCount = 0;
        _nextFreeHint = 0;
        TotalBeds = 0;
        CompletedStorages = 0;
        TotalBuilt = 0;
        TotalDemolished = 0;
        for (int i = 0; i < _completedByKind.Length; i++) { _completedByKind[i] = 0; }
    }
}
