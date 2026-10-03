using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Agents;

/// <summary>
/// 野生动物存储（M2；第 41 节的生态链前半段）。
///
/// 与 Agent 一样用 SoA：动物数量可能上百，而它们的更新频率远高于人，
/// 因此"没有对象、只有数组"在这里更重要。
///
/// 与人的关键区别：动物**不做效用决策**，只有几条极简规则
/// （吃、逃、繁殖、自然死亡）。这是刻意的：
/// 生态系统的观察价值来自"数量随植被波动"，而不是来自"每只鹿都在深思"。
/// 用最低的复杂度换到"伐木会减少猎物"这条因果链，性价比最高。
/// </summary>
public sealed class WildlifeStore : ISimEntitySet
{
    private bool[] _alive = System.Array.Empty<bool>();
    private int[] _x = System.Array.Empty<int>();
    private int[] _y = System.Array.Empty<int>();
    private float[] _energy = System.Array.Empty<float>();
    private short[] _ageDays = System.Array.Empty<short>();
    private int[] _generation = System.Array.Empty<int>();

    /// <summary>
    /// 存活个体的索引列表（无空洞）。
    ///
    /// 为什么需要它：动物每 tick 都要全部更新一次，如果每次都遍历整个容量数组，
    /// 那么"种群只剩 10 只、容量还有 512"的时候仍然要扫 512 个槽位。
    /// 动物的更新频率远高于人，这个常数直接体现在帧率上（实测：加动物后测试耗时从 1.5 秒涨到 3.8 秒，
    /// 其中大部分就是反复扫描空槽位）。
    ///
    /// 删除用"与末尾交换再缩短"（O(1)），代价是遍历顺序不再等于槽位顺序。
    /// 对动物这是可以接受的：它们只做贪心移动，不参与确定性平局判定 ——
    /// 顺序变化只影响"同一 tick 内谁先动"，而这一步本身就是随机的（抖动）。
    /// **人不能用这种结构**，因为人的决策顺序必须完全确定。
    /// </summary>
    private int[] _live = System.Array.Empty<int>();
    private int _liveCount;

    /// <summary>槽位 → 在 <see cref="_live"/> 中的位置（-1 表示槽位空闲）。删除时用它做 O(1) 修正。</summary>
    private int[] _liveIndexOfSlot = System.Array.Empty<int>();
    private int _nextFreeHint;

    /// <summary>累计出生与死亡（报告用）。</summary>
    public int TotalBorn { get; private set; }
    public int TotalDied { get; private set; }

    /// <summary>累计被猎杀的数量 —— "人类活动影响生态"的直接证据。</summary>
    public int TotalHunted { get; private set; }

    public int Capacity => _alive.Length;
    public int EntityCount => _liveCount;
    public int LiveCount => _liveCount;

    public WildlifeStore(int capacity = 256)
    {
        Resize(capacity > 0 ? capacity : 256);
    }

    private void Resize(int capacity)
    {
        if (capacity < 1) { capacity = 1; }
        System.Array.Resize(ref _alive, capacity);
        System.Array.Resize(ref _x, capacity);
        System.Array.Resize(ref _y, capacity);
        System.Array.Resize(ref _energy, capacity);
        System.Array.Resize(ref _ageDays, capacity);
        System.Array.Resize(ref _generation, capacity);
        System.Array.Resize(ref _live, capacity);
        System.Array.Resize(ref _liveIndexOfSlot, capacity);
        for (int i = 0; i < capacity; i++) { _liveIndexOfSlot[i] = -1; }
    }

    public void EnsureCapacity(int required)
    {
        if (required <= _alive.Length) { return; }
        int next = _alive.Length;
        while (next < required) { next *= 2; }
        Resize(next);
    }

    public bool IsAlive(int index) => index >= 0 && index < _alive.Length && _alive[index];

    public int XOf(int index) => _x[index];
    public int YOf(int index) => _y[index];
    public Int2 PositionOf(int index) => new Int2(_x[index], _y[index]);
    public float EnergyOf(int index) => _energy[index];
    public int AgeDaysOf(int index) => _ageDays[index];
    public int GenerationOf(int index) => _generation[index];

    /// <summary>
    /// 在指定位置附近生成一只动物。返回索引（-1 表示地图上没有可站的地方）。
    /// </summary>
    public int Add(Environment.World world, int preferredX, int preferredY, int searchRadius, DeterministicRandom rng)
    {
        EnsureCapacity(_liveCount + 1);

        int slot = AllocateSlot();
        if (slot < 0) { return -1; }

        if (!TryFindWalkable(world, preferredX, preferredY, searchRadius, out int x, out int y))
        {
            _alive[slot] = false;
            _generation[slot]++;
            return -1;
        }

        _x[slot] = x;
        _y[slot] = y;
        _energy[slot] = 0.5f + (float)rng.NextDouble() * 0.5f;
        _ageDays[slot] = 0;
        TotalBorn++;
        return slot;
    }

    private int AllocateSlot()
    {
        for (int i = _nextFreeHint; i < _alive.Length; i++)
        {
            if (_alive[i]) { continue; }
            _alive[i] = true;
            _live[_liveCount] = i;
            _liveIndexOfSlot[i] = _liveCount;
            _liveCount++;
            _nextFreeHint = i;
            return i;
        }
        for (int i = 0; i < _alive.Length; i++)
        {
            if (_alive[i]) { continue; }
            _alive[i] = true;
            _live[_liveCount] = i;
            _liveIndexOfSlot[i] = _liveCount;
            _liveCount++;
            _nextFreeHint = i;
            return i;
        }
        return -1;
    }

    public void Kill(int index, bool hunted)
    {
        if (!IsAlive(index)) { return; }

        _alive[index] = false;
        _generation[index]++;
        TotalDied++;
        if (hunted) { TotalHunted++; }

        // O(1) 从存活列表里摘掉：把末尾元素搬到被删位置，并修正它的反向索引
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

    public void SetPosition(int index, int x, int y)
    {
        _x[index] = x;
        _y[index] = y;
    }

    public void SetEnergy(int index, float value) => _energy[index] = SimMath.Clamp01(value);
    public void AddEnergy(int index, float delta) => SetEnergy(index, _energy[index] + delta);
    public void SetAgeDays(int index, int days) => _ageDays[index] = (short)SimMath.Clamp(days, 0, 32000);

    public static bool TryFindWalkable(Environment.World world, int preferredX, int preferredY, int radius, out int x, out int y)
    {
        x = preferredX;
        y = preferredY;

        if (world.IsInBounds(x, y) && world.TileAt(x, y).Walkable) { return true; }

        for (int r = 1; r <= radius; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    int cheb = System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy));
                    if (cheb != r) { continue; }

                    int cx = preferredX + dx;
                    int cy = preferredY + dy;
                    if (!world.IsInBounds(cx, cy)) { continue; }
                    if (!world.TileAt(cx, cy).Walkable) { continue; }

                    x = cx;
                    y = cy;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 找出距离 (x,y) 最近的动物，并返回切比雪夫距离。
    /// 用于"猎人找猎物"与"动物找最近的威胁"。
    /// </summary>
    public bool TryFindNearest(int x, int y, int maxRadius, out int index, out int distance)
    {
        index = -1;
        distance = int.MaxValue;

        for (int k = 0; k < _liveCount; k++)
        {
            int i = _live[k];
            int d = System.Math.Max(System.Math.Abs(_x[i] - x), System.Math.Abs(_y[i] - y));
            if (d < distance && d <= maxRadius)
            {
                distance = d;
                index = i;
            }
        }

        return index >= 0;
    }

    /// <summary>附近是否有动物（狩猎动作的效用与选靶都读它）。</summary>
    public bool HasPreyNearby(int x, int y, int radius) => TryFindNearest(x, y, radius, out int _, out int _);

    /// <summary>按**槽位顺序**遍历存活个体（确定性；用于 UI 与需要稳定顺序的地方）。</summary>
    public System.Collections.Generic.IEnumerable<int> AliveIndices()
    {
        for (int i = 0; i < _alive.Length; i++)
        {
            if (_alive[i]) { yield return i; }
        }
    }

    /// <summary>按存活列表顺序遍历（更快，但顺序会随删除变化；用于每 tick 的批量更新）。</summary>
    public int LiveAt(int liveIndex) => _live[liveIndex];

    /// <summary>统计某个矩形范围内的动物数量（热力图与生态评估用）。</summary>
    public int CountInRect(int minX, int minY, int maxX, int maxY)
    {
        int count = 0;
        for (int k = 0; k < _liveCount; k++)
        {
            int i = _live[k];
            if (_x[i] >= minX && _x[i] <= maxX && _y[i] >= minY && _y[i] <= maxY) { count++; }
        }
        return count;
    }

    public float AverageEnergy()
    {
        if (_liveCount == 0) { return 0f; }
        float sum = 0f;
        for (int k = 0; k < _liveCount; k++) { sum += _energy[_live[k]]; }
        return sum / _liveCount;
    }

    /// <summary>
    /// 混入状态摘要。
    ///
    /// **必须按槽位顺序遍历**，不能用存活列表：存活列表的顺序会随删除（末尾交换）变化，
    /// 于是"同样的世界状态"可能算出不同的摘要 —— 这会直接破坏确定性校验，
    /// 而且现象非常隐蔽（同样的 seed 偶尔摘要不同，且只在有动物死亡时出现）。
    /// </summary>
    public ulong HashInto(ulong hash)
    {
        hash = Hash64.Combine(hash, _liveCount);
        for (int i = 0; i < _alive.Length; i++)
        {
            if (!_alive[i]) { continue; }
            hash = Hash64.Combine(hash, i);
            hash = Hash64.Combine(hash, _x[i]);
            hash = Hash64.Combine(hash, _y[i]);
            hash = Hash64.Combine(hash, (int)(_energy[i] * 1000f));
            hash = Hash64.Combine(hash, _ageDays[i]);
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
        }
        _liveCount = 0;
        _nextFreeHint = 0;
        TotalBorn = 0;
        TotalDied = 0;
        TotalHunted = 0;
    }
}
