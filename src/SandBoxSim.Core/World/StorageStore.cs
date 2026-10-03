using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Environment;

/// <summary>
/// 共享库存（M3）：每个仓库建筑一份，按建筑槽位对齐。
///
/// 与个体的背包、地面的物资堆一起，构成三层物资存储：
///
/// | 层 | 载体 | 谁用 | 空间性 |
/// |---|---|---|---|
/// | 随身 | `AgentStore._inv*` | 一个人 | 跟着人走 |
/// | 地面堆 | `GroundStockStore` | 附近的人 | 在地上可见 |
/// | 仓库 | 本类 | **整个聚落** | 固定位置、容量更大 |
///
/// 为什么要三层而不是一层：三层对应三种**社会关系** ——
/// 私人物资、临时共用、公共财产。M8 的贸易（聚落与聚落之间）会成为第四层。
/// 如果只有一层，"贸易"与"共享"就没有可区分的载体。
///
/// 实现上用"按建筑槽位对齐的平行数组"，而不是字典：
/// 字典的迭代顺序不保证，而这里的状态要进存档与摘要（见第 13 节的确定性约束）。
/// </summary>
public sealed class StorageStore
{
    private float[] _food = System.Array.Empty<float>();
    private float[] _wood = System.Array.Empty<float>();
    private float[] _stone = System.Array.Empty<float>();
    private float[] _iron = System.Array.Empty<float>();
    private float[] _capacity = System.Array.Empty<float>();

    /// <summary>累计存入/取出量（用于报告与"仓库是否真的在被使用"的判断）。</summary>
    public float TotalDeposited { get; private set; }
    public float TotalWithdrawn { get; private set; }

    public StorageStore(int capacity = 256)
    {
        Resize(capacity > 0 ? capacity : 256);
    }

    public void EnsureCapacity(int required)
    {
        if (required <= _food.Length) { return; }
        int next = _food.Length;
        while (next < required) { next *= 2; }
        Resize(next);
    }

    private void Resize(int capacity)
    {
        if (capacity < 1) { capacity = 1; }
        System.Array.Resize(ref _food, capacity);
        System.Array.Resize(ref _wood, capacity);
        System.Array.Resize(ref _stone, capacity);
        System.Array.Resize(ref _iron, capacity);
        System.Array.Resize(ref _capacity, capacity);
    }

    /// <summary>给某个仓库槽位设定容量（建筑完工时调用）。</summary>
    public void SetCapacity(int index, float capacity)
    {
        if (index < 0 || index >= _capacity.Length) { return; }
        _capacity[index] = capacity > 0f ? capacity : 0f;
    }

    public float CapacityOf(int index)
        => index >= 0 && index < _capacity.Length ? _capacity[index] : 0f;

    public float AmountOf(int index, ResourceKind kind)
    {
        if (index < 0 || index >= _food.Length) { return 0f; }
        switch (kind)
        {
            case ResourceKind.Food: return _food[index];
            case ResourceKind.Wood: return _wood[index];
            case ResourceKind.Stone: return _stone[index];
            case ResourceKind.Iron: return _iron[index];
            default: return 0f;
        }
    }

    /// <summary>某个仓库的总物量（判断"这个仓库有多满"）。</summary>
    public float TotalOf(int index)
        => AmountOf(index, ResourceKind.Food) + AmountOf(index, ResourceKind.Wood)
         + AmountOf(index, ResourceKind.Stone) + AmountOf(index, ResourceKind.Iron);

    /// <summary>存入，返回实际接受量（容量满了会少于请求量，绝不覆盖已有内容）。</summary>
    public float Deposit(int index, ResourceKind kind, float amount)
    {
        if (amount <= 0f || kind == ResourceKind.None) { return 0f; }
        if (index < 0 || index >= _food.Length) { return 0f; }

        float capacity = _capacity[index];
        if (capacity <= 0f) { return 0f; }

        float current = AmountOf(index, kind);
        float room = capacity - current;
        if (room <= 0f) { return 0f; }

        float accepted = amount < room ? amount : room;
        Add(index, kind, accepted);
        TotalDeposited += accepted;
        return accepted;
    }

    /// <summary>取出，返回实际取到的量。</summary>
    public float Withdraw(int index, ResourceKind kind, float amount)
    {
        if (amount <= 0f || kind == ResourceKind.None) { return 0f; }
        if (index < 0 || index >= _food.Length) { return 0f; }

        float current = AmountOf(index, kind);
        if (current <= 0f) { return 0f; }

        float taken = amount < current ? amount : current;
        Add(index, kind, -taken);
        TotalWithdrawn += taken;
        return taken;
    }

    /// <summary>
    /// 读档时直接写入某个槽位的资源量（**不走 Deposit**）。
    ///
    /// 为什么与地面物资堆不同（那边走正常 Deposit 路径）：
    /// 仓库的容量是先恢复的，而存档里的量必然在容量之内；
    /// 更重要的是仓库的量参与**状态摘要**，走 Deposit 会被容量夹取 ——
    /// 如果存档因为任何原因超了容量，夹取会让摘要与直接跑不一致，
    /// 从而把一个"存档损坏"问题伪装成"模拟不确定"。这里宁可原样恢复，
    /// 让不变量校验去报错。
    /// </summary>
    public void RestoreResource(int index, ResourceKind kind, float amount)
    {
        if (index < 0 || index >= _food.Length) { return; }
        Add(index, kind, amount < 0f ? 0f : amount);
    }

    private void Add(int index, ResourceKind kind, float delta)
    {
        switch (kind)
        {
            case ResourceKind.Food: _food[index] += delta; break;
            case ResourceKind.Wood: _wood[index] += delta; break;
            case ResourceKind.Stone: _stone[index] += delta; break;
            case ResourceKind.Iron: _iron[index] += delta; break;
        }
    }

    /// <summary>
    /// 混入状态摘要（按槽位升序，不用存活列表 —— 见 WildlifeStore/BuildingStore 的同一教训）。
    /// </summary>
    public ulong HashInto(ulong hash)
    {
        for (int i = 0; i < _food.Length; i++)
        {
            // 只有"有容量的槽位"才有意义（没建仓库的槽位容量为 0）
            if (_capacity[i] <= 0f) { continue; }
            hash = Hash64.Combine(hash, i);
            hash = Hash64.Combine(hash, (int)(_food[i] * 100f));
            hash = Hash64.Combine(hash, (int)(_wood[i] * 100f));
            hash = Hash64.Combine(hash, (int)(_stone[i] * 100f));
            hash = Hash64.Combine(hash, (int)(_iron[i] * 100f));
        }
        return hash;
    }

    public void Reset()
    {
        System.Array.Clear(_food, 0, _food.Length);
        System.Array.Clear(_wood, 0, _wood.Length);
        System.Array.Clear(_stone, 0, _stone.Length);
        System.Array.Clear(_iron, 0, _iron.Length);
        System.Array.Clear(_capacity, 0, _capacity.Length);
        TotalDeposited = 0f;
        TotalWithdrawn = 0f;
    }
}
