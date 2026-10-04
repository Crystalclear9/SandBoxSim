using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Core.Pathing;

/// <summary>
/// 可复用的格子数组池：避免每次寻路都 new 一个同尺寸数组。
///
/// 为什么值得专门做这件事：100 个个体每 6 tick 决策一次 ⇒ 每秒约 160 次寻路。
/// 每次寻路都要 3 个 int/float[10000]（约 120 KB），一秒就是 20 MB 的分配 ——
/// 在长期运行的模拟里，这类"看不见的分配"是 GC 停顿的主要来源。
/// </summary>
public sealed class GridPool
{
    private readonly System.Collections.Generic.Stack<int[]> _intArrays = new System.Collections.Generic.Stack<int[]>();
    private readonly System.Collections.Generic.Stack<float[]> _floatArrays = new System.Collections.Generic.Stack<float[]>();
    private readonly int _size;

    public GridPool(int size)
    {
        _size = size > 0 ? size : 1;
    }

    public int Size => _size;

    public int[] RentInt()
    {
        if (_intArrays.Count > 0)
        {
            int[] rented = _intArrays.Pop();
            System.Array.Clear(rented, 0, rented.Length);
            return rented;
        }
        return new int[_size];
    }

    public float[] RentFloat()
    {
        if (_floatArrays.Count > 0)
        {
            float[] rented = _floatArrays.Pop();
            System.Array.Clear(rented, 0, rented.Length);
            return rented;
        }
        return new float[_size];
    }

    public void Return(int[] array)
    {
        if (array == null || array.Length != _size) { return; }
        _intArrays.Push(array);
    }

    public void Return(float[] array)
    {
        if (array == null || array.Length != _size) { return; }
        _floatArrays.Push(array);
    }
}

/// <summary>
/// 数组实现的二叉最小堆（按 f 值排序）。
///
/// 为什么不用 SortedSet / PriorityQueue：.NET 的 PriorityQueue 在 .NET 6+ 才有，
/// 而本仓库要在"只有 Roslyn csc + 8.0 运行时"的降级通道下也能编译 —— 自己写几十行更省心。
/// 平局用"格子索引小的优先"打破，保证寻路结果的**确定性**。
/// </summary>
public sealed class BinaryHeap
{
    private int[] _node = System.Array.Empty<int>();
    private float[] _priority = System.Array.Empty<float>();
    private int _count;

    public int Count => _count;
    public bool IsEmpty => _count == 0;

    public void Clear() => _count = 0;

    public void EnsureCapacity(int capacity)
    {
        if (_node.Length >= capacity) { return; }
        int next = _node.Length == 0 ? 64 : _node.Length;
        while (next < capacity) { next *= 2; }
        System.Array.Resize(ref _node, next);
        System.Array.Resize(ref _priority, next);
    }

    public void Push(int node, float priority)
    {
        EnsureCapacity(_count + 1);
        _node[_count] = node;
        _priority[_count] = priority;
        _count++;
        SiftUp(_count - 1);
    }

    public int Pop()
    {
        int top = _node[0];
        _count--;
        if (_count > 0)
        {
            _node[0] = _node[_count];
            _priority[0] = _priority[_count];
            SiftDown(0);
        }
        return top;
    }

    private void SiftUp(int index)
    {
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (!Less(index, parent)) { break; }
            Swap(index, parent);
            index = parent;
        }
    }

    private void SiftDown(int index)
    {
        while (true)
        {
            int left = (index * 2) + 1;
            int right = left + 1;
            int smallest = index;

            if (left < _count && Less(left, smallest)) { smallest = left; }
            if (right < _count && Less(right, smallest)) { smallest = right; }
            if (smallest == index) { break; }

            Swap(index, smallest);
            index = smallest;
        }
    }

    /// <summary>比较：先比 f 值，f 相同时比格子索引（小者优先 ⇒ 确定性平局）。</summary>
    private bool Less(int a, int b)
    {
        float pa = _priority[a];
        float pb = _priority[b];
        if (pa < pb) { return true; }
        if (pa > pb) { return false; }
        return _node[a] < _node[b];
    }

    private void Swap(int a, int b)
    {
        int n = _node[a];
        _node[a] = _node[b];
        _node[b] = n;

        float p = _priority[a];
        _priority[a] = _priority[b];
        _priority[b] = p;
    }
}

/// <summary>寻路结果。</summary>
public struct PathResult
{
    /// <summary>是否成功找到路径。</summary>
    public bool Success;

    /// <summary>路径长度（含终点）。</summary>
    public int Length;

    /// <summary>路径总代价（用于"这条路值不值得走"的比较）。</summary>
    public float Cost;

    /// <summary>访问过的节点数（性能观测与调试）。</summary>
    public int ExpandedNodes;

    public static PathResult Failed(int expandedNodes) => new PathResult
    {
        Success = false,
        Length = 0,
        Cost = 0f,
        ExpandedNodes = expandedNodes,
    };
}

/// <summary>
/// A\* 寻路（第 20 节）。
///
/// 关键设计：
///   1. **八方向 + 对角代价 √2**：只用四方向会让个体走出"直角楼梯"式的僵硬路线，
///      而八方向 + 对角惩罚可以产生近似直线的自然移动；
///   2. **不允许"贴着两块不可走地形斜穿"**：对角移动要求两个正交邻格也都能走，
///      否则个体会从两堵墙的缝里穿过去（图形学里经典的 corner-cutting 问题）；
///   3. **确定性平局**：f 值相同时优先扩展格子索引小的，保证同 seed 同路径；
///   4. **代价来自 TerrainInfo**：地形规则只有一份，寻路不会与建造/AI 判断分叉。
/// </summary>
public sealed class AStarPathfinder
{
    private readonly SandBoxSim.Core.Environment.World _world;
    private readonly int _width;
    private readonly int _height;
    private readonly int _size;

    // 复用缓冲：gScore / came / state
    private readonly float[] _gScore;
    private readonly int[] _came;
    private readonly byte[] _state;    // 0 = 未访问, 1 = 在开放集里, 2 = 已关闭

    /// <summary>
    /// "本次搜索"标记（惰性清空用）。
    ///
    /// 为什么需要它（这是一个被实测数据抓出来的性能问题）：
    /// 每 tick 有几十个个体在移动，而移动是**逐格重算**的 ——
    /// 于是每次寻路都要 <c>Array.Clear</c> 三个长度 = 地图格数的数组。
    /// 100×100 的地图就是每次 3 × 10000 个元素的清零，一秒上万次，
    /// 绝大多数格子其实根本没被访问过。这就是"清零比搜索本身还贵"。
    ///
    /// 做法：给每个格子记一个"最后被哪次搜索写过"的编号，
    /// 读取时先看编号是不是本次搜索；不是就当作未访问（不需要清零）。
    /// 这样每次搜索的实际代价只与**访问到的格子数**成正比。
    /// </summary>
    private readonly int[] _gVersion;
    private readonly int[] _cameVersion;
    private readonly int[] _stateVersion;
    private int _searchId;

    /// <summary>
    /// 本次搜索压入过开放集的节点（用于在搜索结束时清空堆）。
    /// 遍历时可能有重复压入，但清空是幂等的（第二次 Pop 出来的节点我们不再处理）。
    /// </summary>
    private readonly int[] _pushed;
    private int _pushedCount;
    private readonly BinaryHeap _open = new BinaryHeap();

    /// <summary>诊断计数：累计寻路次数与失败次数（报告里反映"世界是否可达"）。</summary>
    public long TotalSearches { get; private set; }
    public long FailedSearches { get; private set; }
    public long CacheHits { get; private set; }
    public bool UseMemoization { get; set; } = true;
    private int _memoRevision = -1;
    private readonly System.Collections.Generic.Dictionary<(int X, int Y, int GX, int GY, int Limit), (PathResult Result, Int2 Step)> _steps = new();

    /// <summary>累计扩展的节点数（A\* 的真实工作量指标；比"搜索次数"更能说明问题）。</summary>
    public long TotalExpandedNodes { get; private set; }

    /// <summary>
    /// 按调用方分类的寻路次数（诊断用）。
    ///
    /// 为什么要分类统计："总共 14 万次寻路"这个数字本身没有行动价值 ——
    /// 必须知道是**移动**在算、还是**选靶**在算、还是**迁移探路**在算，
    /// 才能决定优化哪里。这类"按调用来源分解"的计数在性能排障里几乎总是第一步。
    /// </summary>
    public long SearchesFromMovement { get; private set; }
    public long SearchesFromTargeting { get; private set; }
    public long SearchesFromOther { get; private set; }

    /// <summary>标记下一次寻路的来源类别（由调用方设置，仅用于统计）。</summary>
    public enum SearchOrigin
    {
        Other = 0,
        Movement = 1,
        Targeting = 2,
    }

    private SearchOrigin _nextOrigin = SearchOrigin.Other;

    /// <summary>由调用方声明"接下来这次寻路是谁发起的"（只影响统计）。</summary>
    public void MarkOrigin(SearchOrigin origin) => _nextOrigin = origin;
    public int LastExpandedNodes { get; private set; }
    public int LastPathLength { get; private set; }

    /// <summary>单次寻路的扩展节点上限：防止在"几乎不可达"的地图上卡住整个 tick。</summary>
    public int MaxExpandedNodes { get; set; } = 4000;

    public AStarPathfinder(SandBoxSim.Core.Environment.World world)
    {
        _world = world ?? throw new System.ArgumentNullException(nameof(world));
        _width = world.Width;
        _height = world.Height;
        _size = _width * _height;
        _gScore = new float[_size];
        _came = new int[_size];
        _state = new byte[_size];
        _gVersion = new int[_size];
        _cameVersion = new int[_size];
        _stateVersion = new int[_size];
        _pushed = new int[_size];
        _open.EnsureCapacity(1024);
    }

    /// <summary>
    /// 寻路。结果写入 <paramref name="outPath"/>（调用方提供的缓冲，长度需 ≥ 结果长度）。
    /// </summary>
    /// <param name="startX">起点 X（必须可走）。</param>
    /// <param name="startY">起点 Y。</param>
    /// <param name="goalX">目标 X（必须可走，否则直接失败）。</param>
    /// <param name="goalY">目标 Y。</param>
    /// <param name="outPath">输出路径（含起点与终点）。</param>
    /// <param name="outPath">输出路径（含起点与终点）。</param>
    public PathResult FindPath(int startX, int startY, int goalX, int goalY, Int2[] outPath)
    {
        PathResult result = FindPathCore(startX, startY, goalX, goalY);
        if (!result.Success || outPath == null) { return result; }

        // 回溯路径。result.Length 可能超过 outPath.Length，此时截断到缓冲大小（调用方应保证足够大）。
        int count = result.Length;
        if (count > outPath.Length) { count = outPath.Length; }

        int node = (goalY * _width) + goalX;
        for (int i = count - 1; i >= 0; i--)
        {
            outPath[i] = new Int2(node % _width, node / _width);
            node = CameOf(node);
            if (node < 0 && i > 0) { break; }
        }

        result.Length = count;
        return result;
    }

    /// <summary>
    /// 只求"是否可达 + 下一跳"。
    /// 移动系统逐格前进时只需要下一跳，不需要完整路径 ——
    /// 这样路径可以随环境变化自然重算，而不是死抱一条旧路线走到撞墙。
    /// </summary>
    public PathResult FindNextStep(int startX, int startY, int goalX, int goalY, out Int2 nextStep)
    {
        if (_memoRevision != _world.Revision) { _steps.Clear(); _memoRevision = _world.Revision; }
        var key = (startX, startY, goalX, goalY, MaxExpandedNodes);
        if (UseMemoization && _steps.TryGetValue(key, out var cached))
        {
            CacheHits++; TotalSearches++;
            switch (_nextOrigin)
            {
                case SearchOrigin.Movement: SearchesFromMovement++; break;
                case SearchOrigin.Targeting: SearchesFromTargeting++; break;
                default: SearchesFromOther++; break;
            }
            _nextOrigin = SearchOrigin.Other;
            nextStep = cached.Step;
            var answer = cached.Result; answer.ExpandedNodes = 0;
            LastExpandedNodes = 0; LastPathLength = answer.Length;
            if (!answer.Success) { FailedSearches++; }
            return answer;
        }
        nextStep = new Int2(startX, startY);
        PathResult result = FindPathCore(startX, startY, goalX, goalY);
        if (!result.Success) { RememberStep(key, result, nextStep); return result; }

        if (result.Length <= 1)
        {
            nextStep = new Int2(goalX, goalY);
            RememberStep(key, result, nextStep);
            return result;
        }

        // 终点沿着 came 指针回溯到"起点的下一跳"
        int node = (goalY * _width) + goalX;
        int previous = CameOf(node);
        while (previous >= 0 && previous != (startY * _width) + startX)
        {
            node = previous;
            previous = CameOf(node);
        }

        nextStep = new Int2(node % _width, node / _width);
        RememberStep(key, result, nextStep);
        return result;
    }
    private void RememberStep((int X, int Y, int GX, int GY, int Limit) key, PathResult result, Int2 step)
    {
        if (!UseMemoization) { return; }
        if (_steps.Count >= 8192) { _steps.Clear(); }
        _steps[key] = (result, step);
    }

    private PathResult FindPathCore(int startX, int startY, int goalX, int goalY)
    {
        TotalSearches++;

        switch (_nextOrigin)
        {
            case SearchOrigin.Movement: SearchesFromMovement++; break;
            case SearchOrigin.Targeting: SearchesFromTargeting++; break;
            default: SearchesFromOther++; break;
        }
        _nextOrigin = SearchOrigin.Other;

        if (!_world.IsInBounds(startX, startY) || !_world.IsInBounds(goalX, goalY))
        {
            FailedSearches++;
            return PathResult.Failed(0);
        }

        // 目标不可走 ⇒ 直接失败。这里刻意不搜索"目标附近的可走格"：
        // 那个策略会让"采集不可达资源"这种错误被掩盖成"他站在旁边发呆"，更难排查。
        if (!_world.TileAt(goalX, goalY).Walkable)
        {
            FailedSearches++;
            return PathResult.Failed(0);
        }

        int start = (startY * _width) + startX;
        int goal = (goalY * _width) + goalX;

        if (start == goal)
        {
            SetCame(start, -1);
            LastExpandedNodes = 0;
            LastPathLength = 1;
            return new PathResult { Success = true, Length = 1, Cost = 0f, ExpandedNodes = 0 };
        }

        // 开始一次新搜索：只递增编号，**不清空数组**（见 _gVersion 的说明）。
        BeginSearch();

        Tile[] tiles = _world.Tiles;

        SetCame(start, -1);
        SetG(start, 0f);
        SetState(start, 1);

        _open.Push(start, Heuristic(startX, startY, goalX, goalY));
        PushTrack(start);

        int expanded = 0;
        int guard = 0;
        int guardLimit = _size * 8;

        while (!_open.IsEmpty)
        {
            int current = _open.Pop();
            if (StateOf(current) == 2) { continue; }
            SetState(current, 2);
            expanded++;

            if (current == goal)
            {
                int length = ReconstructLength(goal);
                LastExpandedNodes = expanded;
                LastPathLength = length;
                TotalExpandedNodes += expanded;
                DrainOpen();
                return new PathResult
                {
                    Success = true,
                    Length = length,
                    Cost = GOf(goal),
                    ExpandedNodes = expanded,
                };
            }

            if (expanded >= MaxExpandedNodes || ++guard > guardLimit)
            {
                // 超过预算：视为不可达。宁可让个体放弃，也不要让整个 tick 卡死在这里。
                break;
            }

            int cx = current % _width;
            int cy = current / _width;

            for (int direction = 0; direction < 8; direction++)
            {
                Neighbour(direction, out int dx, out int dy);

                int nx = cx + dx;
                int ny = cy + dy;
                if (nx < 0 || ny < 0 || nx >= _width || ny >= _height) { continue; }

                int neighbour = (ny * _width) + nx;
                if (StateOf(neighbour) == 2) { continue; }

                ref readonly Tile tile = ref tiles[neighbour];
                if (!tile.Walkable) { continue; }

                bool diagonal = dx != 0 && dy != 0;

                // 禁止"贴着墙角斜穿"：对角移动要求两个正交邻格也可走。
                // 否则个体会从两堵墙/两片水的缝隙里穿过去，看起来像穿墙。
                if (diagonal)
                {
                    int sideA = (cy * _width) + nx;   // (cx+dx, cy)
                    int sideB = (ny * _width) + cx;   // (cx, cy+dy)
                    if (!tiles[sideA].Walkable || !tiles[sideB].Walkable) { continue; }
                }

                // 进入代价：地形代价 × 对角系数 × 从低地爬坡到高地的额外代价。
                float stepCost = TerrainInfo.MoveCost(tile.Terrain);
                if (diagonal) { stepCost *= 1.4142f; }

                float elevationDelta = tile.Temperature - tiles[current].Temperature;
                if (elevationDelta > 0f) { stepCost += elevationDelta * 0.5f; }

                if (stepCost <= 0f) { stepCost = 0.01f; }

                float tentative = GOf(current) + stepCost;

                // 注意判据：先看"本次搜索是否访问过"（版本号），再看 g 值。
                // 直接用 `_state[neighbour] == 0 ||` 是错的 ——
                // 惰性清空之后，那个 0 可能来自**很久以前的某次搜索**。
                if (StateOf(neighbour) == 0 || tentative < GOf(neighbour))
                {
                    SetG(neighbour, tentative);
                    SetCame(neighbour, current);
                    SetState(neighbour, 1);

                    float f = tentative + Heuristic(nx, ny, goalX, goalY);
                    _open.Push(neighbour, f);
                    PushTrack(neighbour);
                }
            }
        }

        FailedSearches++;
        LastExpandedNodes = expanded;
        LastPathLength = 0;
        TotalExpandedNodes += expanded;
        DrainOpen();
        return PathResult.Failed(expanded);
    }

    // ---------------------------------------------------------------------
    // 惰性清空的访问器（见 _gVersion 的说明）
    // ---------------------------------------------------------------------

    private void BeginSearch()
    {
        _searchId++;
        _pushedCount = 0;
        _open.Clear();
    }

    private int StateOf(int node) => _stateVersion[node] == _searchId ? _state[node] : 0;

    private void SetState(int node, byte value)
    {
        _state[node] = value;
        _stateVersion[node] = _searchId;
    }

    private float GOf(int node) => _gVersion[node] == _searchId ? _gScore[node] : float.MaxValue;

    private void SetG(int node, float value)
    {
        _gScore[node] = value;
        _gVersion[node] = _searchId;
    }

    private int CameOf(int node) => _cameVersion[node] == _searchId ? _came[node] : -1;

    private void SetCame(int node, int value)
    {
        _came[node] = value;
        _cameVersion[node] = _searchId;
    }

    private void PushTrack(int node)
    {
        if (_pushedCount >= _pushed.Length) { return; }
        _pushed[_pushedCount++] = node;
    }

    /// <summary>把本次搜索残留的堆元素吐出来（节点本身的状态已经是最终值，不会再被处理）。</summary>
    private void DrainOpen()
    {
        while (!_open.IsEmpty) { _open.Pop(); }
        _pushedCount = 0;
    }

    /// <summary>
    /// 启发函数：八方向下的**八分距离**（octile distance）。
    ///
    /// 不用欧氏距离，因为欧氏距离在八方向网格上是低估的（导致扩展更多节点）；
    /// 用 octile 距离既不高估也不低估，在"移动代价基本均匀"的地图上是最优启发。
    /// 地形代价差异会让它变成略微低估（仍然 admissible，因此 A\* 结果仍最优）。
    /// </summary>
    private static float Heuristic(int fromX, int fromY, int toX, int toY)
    {
        int dx = System.Math.Abs(toX - fromX);
        int dy = System.Math.Abs(toY - fromY);
        int diagonal = dx < dy ? dx : dy;
        int straight = (dx + dy) - (diagonal * 2);
        return (diagonal * 1.4142f) + straight;
    }

    private static void Neighbour(int direction, out int dx, out int dy)
    {
        switch (direction)
        {
            case 0: dx = 1; dy = 0; break;
            case 1: dx = -1; dy = 0; break;
            case 2: dx = 0; dy = 1; break;
            case 3: dx = 0; dy = -1; break;
            case 4: dx = 1; dy = 1; break;
            case 5: dx = 1; dy = -1; break;
            case 6: dx = -1; dy = 1; break;
            default: dx = -1; dy = -1; break;
        }
    }

    private int ReconstructLength(int goal)
    {
        int length = 0;
        int node = goal;
        while (node >= 0 && length <= _size)
        {
            length++;
            node = CameOf(node);
            if (node == -1) { break; }
        }
        // CameOf(起点) = -1，因此循环里多算了一次
        return length;
    }
}
