namespace SandBoxSim.Core.Environment;

/// <summary>
/// 资源种类（第 9 节第一阶段：Food / Wood / Stone，Iron 在 M3 一并加入，
/// 因为它决定了 M8 的"武器/工具"链条，留出接口比之后再改枚举更省事）。
/// </summary>
public enum ResourceKind : byte
{
    None = 0,
    Food = 1,
    Wood = 2,
    Stone = 3,
    Iron = 4,
}

/// <summary>资源种类的静态信息与工具方法。</summary>
public static class ResourceInfo
{
    /// <summary>参与库存/统计的资源数量（不含 None）。</summary>
    public const int Count = 4;

    /// <summary>把资源种类映射到库存数组下标（None 视为 0，调用方必须先排除 None）。</summary>
    public static int ToIndex(ResourceKind kind) => (int)kind - 1;

    public static ResourceKind FromIndex(int index) => (ResourceKind)(index + 1);

    public static string NameOf(ResourceKind kind)
    {
        switch (kind)
        {
            case ResourceKind.Food: return "food";
            case ResourceKind.Wood: return "wood";
            case ResourceKind.Stone: return "stone";
            case ResourceKind.Iron: return "iron";
            default: return "none";
        }
    }

    public static bool TryParse(string? name, out ResourceKind kind)
    {
        kind = ResourceKind.None;
        if (string.IsNullOrWhiteSpace(name)) { return false; }

        switch (name.Trim().ToLowerInvariant())
        {
            case "food": kind = ResourceKind.Food; return true;
            case "wood": kind = ResourceKind.Wood; return true;
            case "stone": kind = ResourceKind.Stone; return true;
            case "iron": kind = ResourceKind.Iron; return true;
            case "none": kind = ResourceKind.None; return true;
            default: return false;
        }
    }

    /// <summary>全部可交易/可统计资源（固定顺序：Food, Wood, Stone, Iron）。</summary>
    public static readonly ResourceKind[] All =
    {
        ResourceKind.Food,
        ResourceKind.Wood,
        ResourceKind.Stone,
        ResourceKind.Iron,
    };
}

/// <summary>
/// Tile 上的资源节点（第 11 节）。刻意做成"局部资源"而不是全局数字：
/// 只有局部资源才会出现"这片林子被砍光了"这种可观察、可传播的现象。
/// </summary>
public struct ResourceNode
{
    public ResourceKind Kind;

    /// <summary>当前存量。</summary>
    public float Amount;

    /// <summary>环境容量 K，Logistic 再生的上限（第 63 条）。</summary>
    public float Capacity;

    /// <summary>基础再生率 r（按天计，实际按小时 tick 分摊）。</summary>
    public float RegenerationRate;

    public bool IsEmpty => Kind == ResourceKind.None || Capacity <= 0f;

    /// <summary>存量占容量的比例 [0,1]，UI 与 AI 判断"这块地还剩多少"都用它。</summary>
    public float Fraction
    {
        get
        {
            if (Capacity <= 0f) { return 0f; }
            float f = Amount / Capacity;
            if (f < 0f) { return 0f; }
            if (f > 1f) { return 1f; }
            return f;
        }
    }

    /// <summary>
    /// 离散 Logistic 再生（第 63 条）：
    /// A ← min(K, A + hours·(r·A·(1 − A/K)))
    /// 相比固定 +2/天，它在资源接近枯竭时再生也慢，因此"过度采集"会造成真实且难以立刻恢复的损伤。
    /// </summary>
    public static float RegenerateLogistic(float amount, float capacity, float growthRatePerDay, double days)
    {
        if (capacity <= 0f) { return 0f; }
        if (amount <= 0f) { return 0f; }

        double ratio = amount / capacity;
        double growth = growthRatePerDay * amount * (1.0 - ratio) * days;
        double next = amount + growth;
        if (next < 0.0) { next = 0.0; }
        if (next > capacity) { next = capacity; }
        return (float)next;
    }

    /// <summary>按 cap 封顶地扣除 amount，返回实际扣掉的数量（用于采集结算）。</summary>
    public float Harvest(float wanted)
    {
        if (wanted <= 0f || Amount <= 0f) { return 0f; }
        float taken = wanted < Amount ? wanted : Amount;
        Amount -= taken;
        if (Amount < 0f) { Amount = 0f; }
        return taken;
    }
}

/// <summary>库存：固定长度数组，顺序由 <see cref="ResourceInfo"/> 决定，无字典（保证确定性）。</summary>
public struct ResourceStock
{
    public const int Size = ResourceInfo.Count;

    public float Food;
    public float Wood;
    public float Stone;
    public float Iron;

    public static ResourceStock Empty => default;

    public float Get(ResourceKind kind)
    {
        switch (kind)
        {
            case ResourceKind.Food: return Food;
            case ResourceKind.Wood: return Wood;
            case ResourceKind.Stone: return Stone;
            case ResourceKind.Iron: return Iron;
            default: return 0f;
        }
    }

    public void Set(ResourceKind kind, float value)
    {
        if (value < 0f) { value = 0f; }
        switch (kind)
        {
            case ResourceKind.Food: Food = value; break;
            case ResourceKind.Wood: Wood = value; break;
            case ResourceKind.Stone: Stone = value; break;
            case ResourceKind.Iron: Iron = value; break;
            default: break;
        }
    }

    public void Add(ResourceKind kind, float delta)
    {
        if (delta == 0f) { return; }
        Set(kind, Get(kind) + delta);
    }

    /// <summary>总物量（统计与"是否富裕"判定用）。</summary>
    public float Total => Food + Wood + Stone + Iron;

    public override string ToString()
        => "food=" + Food.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
         + " wood=" + Wood.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
         + " stone=" + Stone.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
         + " iron=" + Iron.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
}
