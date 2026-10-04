using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Environment;

/// <summary>
/// 地面物资堆（M2）。
///
/// 为什么需要它：M1 的个体只把东西塞进自己口袋，玩家看不到任何"经济"。
/// 一旦有了地面物资堆，"攒物资"就变成**空间上可见的现象**：
/// 某个地方会逐渐堆起食物与木料，那是聚落仓库的前身（M3 的 Storage 就是把它变成建筑）。
///
/// 实现选择：固定容量的数组 + 线性搜索 + 槽位复用。
/// 为什么不用"每个格子一个堆"：那样 10 万格每格都要带一个结构体（哪怕空着），
/// 而实际同时存在的堆只会有几十个。稀疏表示在这里明显更划算。
/// </summary>
public sealed class GroundStockStore : ISimEntitySet
{
    private struct Pile
    {
        public bool Alive;
        public int X;
        public int Y;
        public ResourceStock Stock;
    }

    private Pile[] _piles;
    private int _liveCount;

    /// <summary>
    /// 存活堆的索引列表（无空洞）。
    ///
    /// 为什么要它：存/取动作每次选靶都会问"附近有没有堆"。
    /// 堆的数量很少（几十个）但容量可能有几百，遍历整个数组会让每次选靶都做几百次无用比较 ——
    /// 在几十个个体、每天上千次决策的规模下，这就是可观的开销。
    /// （与 WildlifeStore 同一个教训：**稀疏数据不要用稠密数组遍历**。）
    /// </summary>
    private int[] _liveIndices = System.Array.Empty<int>();

    /// <summary>槽位 → 在 _liveIndices 中的位置（-1 = 空闲），用于 O(1) 删除。</summary>
    private int[] _liveIndexOfSlot = System.Array.Empty<int>();

    /// <summary>每种资源在全图地面上的总量（报告与 UI 用）。</summary>
    private readonly float[] _totals = new float[8];
    private readonly bool[] _totalsDirty = new bool[8];

    public int Capacity => _piles.Length;
    public int EntityCount => _liveCount;
    public int LiveCount => _liveCount;

    /// <summary>累计放下（存入）的资源量。</summary>
    public float TotalDeposited { get; private set; }

    /// <summary>累计取回的资源量。</summary>
    public float TotalWithdrawn { get; private set; }

    public GroundStockStore(int capacity = 96)
    {
        int size = capacity > 0 ? capacity : 96;
        _piles = new Pile[size];
        _liveIndices = new int[size];
        _liveIndexOfSlot = new int[size];
        for (int i = 0; i < size; i++) { _liveIndexOfSlot[i] = -1; }
    }

    public bool IsAlive(int index) => index >= 0 && index < _piles.Length && _piles[index].Alive;

    public Int2 PositionOf(int index) => new Int2(_piles[index].X, _piles[index].Y);

    public float AmountOf(int index, ResourceKind kind) => _piles[index].Stock.Get(kind);

    public float TotalOf(ResourceKind kind)
    {
        int i = (int)kind;
        if (i < 0 || i >= _totals.Length) { return 0f; }
        if (_totalsDirty[i])
        {
            double total = 0;
            for (int slot = 0; slot < _piles.Length; slot++)
            { if (_piles[slot].Alive) { total += _piles[slot].Stock.Get(kind); } }
            _totals[i] = (float)total;
            _totalsDirty[i] = false;
        }
        return _totals[i];
    }

    public ResourceStock StockOf(int index) => _piles[index].Stock;

    /// <summary>所有物资堆的索引（**按槽位升序** ⇒ 确定性；用于摘要与 UI）。</summary>
    public int[] AliveIndices()
    {
        int[] result = new int[_liveCount];
        for (int i = 0; i < _liveCount; i++) { result[i] = _liveIndices[i]; }
        System.Array.Sort(result);
        return result;
    }

    private int Allocate(int x, int y)
    {
        for (int i = 0; i < _piles.Length; i++)
        {
            if (_piles[i].Alive) { continue; }
            _piles[i] = new Pile { Alive = true, X = x, Y = y };
            AddLive(i);
            return i;
        }

        // 满了：扩容。物资堆的数量天然有限（受玩家与个体行为约束），
        // 但扩容逻辑必须有，否则会出现"偶发地放不下东西"这种极难复现的行为空洞。
        int old = _piles.Length;
        System.Array.Resize(ref _piles, old * 2);
        System.Array.Resize(ref _liveIndices, old * 2);
        System.Array.Resize(ref _liveIndexOfSlot, old * 2);
        for (int i = old; i < _piles.Length; i++) { _liveIndexOfSlot[i] = -1; }

        _piles[old] = new Pile { Alive = true, X = x, Y = y };
        AddLive(old);
        return old;
    }

    private void AddLive(int index)
    {
        _liveIndices[_liveCount] = index;
        _liveIndexOfSlot[index] = _liveCount;
        _liveCount++;
    }

    /// <summary>
    /// 把一条存档里的物资堆精确恢复到指定槽位（读档专用）。
    ///
    /// **槽位必须原样恢复，不能重新紧凑排列** —— 这是一个踩过的坑：
    /// <see cref="HashInto"/> 会把槽位下标混进摘要，所以"把 5 个堆恢复成槽位 0..4"
    /// 与"它们原本在槽位 3/7/8/12/19"会算出**不同的摘要**，
    /// 于是读档后立刻摘要不一致，看起来像"读档把世界改坏了"，
    /// 实际上世界完全正确、只是槽位身份丢了。
    /// 凡是进摘要的字段都必须在存档里显式保存，槽位也是其中之一。
    /// </summary>
    public void RestorePile(int slot, int x, int y, ResourceStock stock)
    {
        EnsureCapacity(slot + 1);

        _piles[slot] = new Pile
        {
            Alive = true,
            X = x,
            Y = y,
            Stock = stock,
        };

        // 总量是**派生缓存**（它由各堆求和得出），在存档里不进摘要，
        // 但读档后必须立刻正确，否则 UI 与需求判定会依据 0 做决定。
        _totals[(int)ResourceKind.Food] += stock.Food;
        _totals[(int)ResourceKind.Wood] += stock.Wood;
        _totals[(int)ResourceKind.Stone] += stock.Stone;
        _totals[(int)ResourceKind.Iron] += stock.Iron;
        for (int i = 0; i < _totalsDirty.Length; i++) { _totalsDirty[i] = true; }

        AddLive(slot);
    }

    /// <summary>确保至少能容纳 <paramref name="size"/> 个堆（读档时存档可能比默认容量大）。</summary>
    public void EnsureCapacity(int size)
    {
        if (size <= _piles.Length) { return; }

        int old = _piles.Length;
        int next = old;
        while (next < size) { next *= 2; }

        System.Array.Resize(ref _piles, next);
        System.Array.Resize(ref _liveIndices, next);
        System.Array.Resize(ref _liveIndexOfSlot, next);
        for (int i = old; i < next; i++) { _liveIndexOfSlot[i] = -1; }
    }

    /// <summary>清空全部物资堆但保留容量（读档前调用）。</summary>
    public void ClearAllKeepCapacity()
    {
        for (int i = 0; i < _piles.Length; i++)
        {
            _piles[i].Alive = false;
            _liveIndexOfSlot[i] = -1;
        }
        _liveCount = 0;
        for (int i = 0; i < _totals.Length; i++) { _totals[i] = 0f; }
        TotalDeposited = 0f;
        TotalWithdrawn = 0f;
    }

    /// <summary>读档时恢复累计存入/取回（不影响演化，只有报告读它们）。</summary>
    public void RestoreCounters(float totalDeposited, float totalWithdrawn)
    {
        TotalDeposited = totalDeposited;
        TotalWithdrawn = totalWithdrawn;
    }

    private void RemoveLive(int index)
    {
        int slot = _liveIndexOfSlot[index];
        if (slot < 0 || slot >= _liveCount) { return; }

        int last = _liveIndices[_liveCount - 1];
        _liveIndices[slot] = last;
        _liveIndexOfSlot[last] = slot;
        _liveCount--;
        _liveIndices[_liveCount] = -1;
        _liveIndexOfSlot[index] = -1;
    }

    /// <summary>
    /// 在 (x,y) 放下资源。会合并到同格的已有堆里。
    /// 返回**实际**放下的数量（容量满了会少于请求量）。
    /// </summary>
    public float Deposit(int x, int y, ResourceKind kind, float amount, GroundStockConfig config)
    {
        if (amount <= 0f || kind == ResourceKind.None) { return 0f; }

        int index = FindOrCreate(x, y);
        if (index < 0) { return 0f; }

        float capacity = config.CapacityPerKind;
        float current = _piles[index].Stock.Get(kind);
        float room = capacity - current;
        if (room <= 0f) { return 0f; }

        float accepted = amount < room ? amount : room;
        _piles[index].Stock.Add(kind, accepted);
        AddTotal(kind, accepted);
        TotalDeposited += accepted;
        return accepted;
    }

    /// <summary>从 (x,y) 的堆里取资源。返回实际取到的数量。</summary>
    public float Withdraw(int x, int y, ResourceKind kind, float amount)
    {
        if (amount <= 0f || kind == ResourceKind.None) { return 0f; }

        int index = FindAt(x, y);
        if (index < 0) { return 0f; }

        float current = _piles[index].Stock.Get(kind);
        if (current <= 0f) { return 0f; }

        float taken = amount < current ? amount : current;
        _piles[index].Stock.Add(kind, -taken);
        AddTotal(kind, -taken);
        TotalWithdrawn += taken;

        // 空了就释放槽位：否则"取空之后再也没人用的堆"会长期占位
        if (_piles[index].Stock.Total <= 0.001f)
        {
            _piles[index].Alive = false;
            RemoveLive(index);
            // 释放时也会丢弃其他资源的微量残余，所有总量缓存均须失效。
            for (int i = 0; i < _totalsDirty.Length; i++) { _totalsDirty[i] = true; }
        }

        return taken;
    }

    private int FindOrCreate(int x, int y)
    {
        int existing = FindAt(x, y);
        return existing >= 0 ? existing : Allocate(x, y);
    }

    public int FindAt(int x, int y)
    {
        for (int k = 0; k < _liveCount; k++)
        {
            int i = _liveIndices[k];
            if (_piles[i].X == x && _piles[i].Y == y) { return i; }
        }
        return -1;
    }

    /// <summary>
    /// 找出附近"含有指定资源"的最近物资堆。
    /// 与资源搜索同样的策略：限定半径，避免全图扫描（第 19 条的精神）。
    /// </summary>
    public bool TryFindNearby(int x, int y, ResourceKind kind, int radius, out int index, out int distance)
    {
        index = -1;
        distance = int.MaxValue;

        for (int k = 0; k < _liveCount; k++)
        {
            int i = _liveIndices[k];
            float available = _piles[i].Stock.Get(kind);
            if (available <= 0.01f) { continue; }

            int d = System.Math.Max(System.Math.Abs(_piles[i].X - x), System.Math.Abs(_piles[i].Y - y));
            if (d <= radius && (d < distance || (d == distance && (index < 0 || i < index))))
            {
                distance = d;
                index = i;
            }
        }

        return index >= 0;
    }

    private void AddTotal(ResourceKind kind, float delta)
    {
        int i = (int)kind;
        if (i < 0 || i >= _totals.Length) { return; }
        _totals[i] += delta;
        _totalsDirty[i] = true;
        if (_totals[i] < 0f) { _totals[i] = 0f; }
    }

    /// <summary>
    /// 混入状态摘要。
    /// **必须按槽位顺序遍历**（不能用存活列表）：存活列表的顺序会随删除（末尾交换）变化，
    /// 于是"同样的世界状态"可能算出不同摘要，从而破坏确定性校验 —— 而且只在有堆被取空时才出现。
    /// </summary>
    public ulong HashInto(ulong hash)
    {
        hash = Hash64.Combine(hash, _liveCount);
        for (int i = 0; i < _piles.Length; i++)
        {
            if (!_piles[i].Alive) { continue; }
            hash = Hash64.Combine(hash, i);
            hash = Hash64.Combine(hash, _piles[i].X);
            hash = Hash64.Combine(hash, _piles[i].Y);
            hash = Hash64.Combine(hash, (int)(_piles[i].Stock.Food * 100f));
            hash = Hash64.Combine(hash, (int)(_piles[i].Stock.Wood * 100f));
            hash = Hash64.Combine(hash, (int)(_piles[i].Stock.Stone * 100f));
            hash = Hash64.Combine(hash, (int)(_piles[i].Stock.Iron * 100f));
        }
        return hash;
    }

    public void Reset()
    {
        for (int i = 0; i < _piles.Length; i++)
        {
            _piles[i].Alive = false;
            _liveIndexOfSlot[i] = -1;
        }
        _liveCount = 0;
        TotalDeposited = 0f;
        TotalWithdrawn = 0f;
        for (int i = 0; i < _totals.Length; i++) { _totals[i] = 0f; }
    }
}
