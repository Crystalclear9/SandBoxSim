using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Systems;

/// <summary>
/// 建造系统（M3）。
///
/// 职责边界刻意收得很窄：**它只负责"把已经在施工的建筑往前推"**。
///   * 决定"要不要建、建在哪"是 Utility AI 的事（`BuildHouseAction` 等）；
///   * 决定"扣不扣料"发生在动作完成的那一刻（由动作调用 <see cref="BuildingStore"/>）；
///   * 它只做"每 10 tick 推进一次施工进度、够了就完工"。
///
/// 为什么施工要跨多个 tick（而不是"放下就完工"）：
///   1. 它让"建造"成为一个**可见的过程**（玩家能看到工地逐步成型，而不是凭空出现）；
///   2. 它给"施工被打断"留出了空间（M5 的火灾、M8 的战争都会打断工地）；
///   3. 它让"建造需要劳动力"这件事在数据上成立 —— 而不是一句设定。
/// </summary>
public sealed class BuildingSystem
{
    private readonly Simulation _sim;
    private readonly BuildingStore _store;

    /// <summary>本 tick 完工的建筑数。</summary>
    public int CompletedThisTick { get; private set; }

    /// <summary>本 tick 推进的施工点数。</summary>
    public int WorkThisTick { get; private set; }

    public BuildingSystem(Simulation sim, BuildingStore store)
    {
        _sim = sim ?? throw new System.ArgumentNullException(nameof(sim));
        _store = store ?? throw new System.ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// 每 10 tick 推进一次施工（由 <c>Simulation.TickFast</c> 调用）。
    ///
    /// 为什么挂在 FastTick 而不是每 tick：施工是慢变量，
    /// 而"每 tick 遍历所有工地"会让建筑数量直接乘进每 tick 的开销里。
    /// 这与决策分批、野生动物种群按天更新是同一个思路（第 73 条）。
    /// </summary>
    public void TickFast(long tick)
    {
        CompletedThisTick = 0;
        WorkThisTick = 0;

        int progressPerTick = _sim.Config.Buildings.WorkPerFastTick;

        for (int k = 0; k < _store.LiveCount; k++)
        {
            int index = _store.LiveAt(k);
            if (!_store.IsAlive(index)) { continue; }
            if (_store.StateOf(index) != BuildingState.UnderConstruction) { continue; }

            if (!_store.AdvanceWork(index, progressPerTick))
            {
                WorkThisTick += progressPerTick;
                continue;
            }

            // 完工
            _store.MarkComplete(index, tick);
            CompletedThisTick++;

            BuildingKind kind = _store.KindOf(index);
            Int2 position = _store.PositionOf(index);

            _sim.Events.Record(
                tick,
                History.WorldEventType.BuildingCompleted,
                BuildingRegistry.NameOf(kind) + "建成 @ " + position,
                History.EventImportance.Important,
                position,
                -1,
                index,
                "建造完成");

            // 农田建成时地表要变成农田 —— 这是"建筑改变地形"的第一个例子，
            // 也是"人口 → 开垦 → 森林减少"这条链条的起点（第 33 / 97 条）。
            if (kind == BuildingKind.Farm)
            {
                _sim.World.SetTerrain(position.X, position.Y, TerrainKind.Farmland);
            }
        }
    }

    /// <summary>
    /// 尝试在指定位置开始建造。
    ///
    /// 这是一个**事务性**操作：要么"扣料 + 放下工地"都成功，要么什么都不变。
    /// 为什么必须这样：如果先扣料再发现位置不可用，玩家会看到"资源没了但房子也没起来"，
    /// 而这种状态无法从任何 UI 上解释。事务性是让"经济操作"可信的最低要求。
    /// </summary>
    public bool TryStartBuilding(
        AgentStore store,
        int slot,
        BuildingKind kind,
        int x,
        int y,
        out int buildingIndex,
        out string failure)
    {
        buildingIndex = -1;
        failure = string.Empty;

        BuildingRecipe recipe = BuildingRegistry.Of(kind);
        if (recipe.Kind == BuildingKind.None) { failure = "未知建筑"; return false; }

        if (!BuildingStore.CanPlaceAt(_sim.World, kind, x, y)) { failure = "位置不可建造"; return false; }

        // 材料的三个来源，按优先级：
        //   1) 个体随身（他刚采集回来的）
        //   2) 附近仓库（聚落共享库存，M3 建成的 Storage）
        //   3) 附近地面物资堆（M2 的堆料点）
        // 这个优先级本身就是一条设计规则：**先用自己的，再用公共的**。
        float woodAvailable = store.InventoryOf(slot, ResourceKind.Wood)
                            + AvailableFromStorage(ResourceKind.Wood, x, y)
                            + AvailableFromPiles(ResourceKind.Wood, x, y);
        float stoneAvailable = store.InventoryOf(slot, ResourceKind.Stone)
                             + AvailableFromStorage(ResourceKind.Stone, x, y)
                             + AvailableFromPiles(ResourceKind.Stone, x, y);

        if (woodAvailable + 1e-3f < recipe.WoodCost || stoneAvailable + 1e-3f < recipe.StoneCost)
        {
            failure = "材料不足（需 木" + recipe.WoodCost.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                    + " 石" + recipe.StoneCost.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "）";
            return false;
        }

        int index = _store.Place(_sim.World, kind, x, y, recipe.BuildWorkTicks);
        if (index < 0) { failure = "无法放置（容量或地形）"; return false; }

        // 扣料（此时位置已确定，扣料不会白扣）
        Consume(store, slot, ResourceKind.Wood, recipe.WoodCost, x, y);
        Consume(store, slot, ResourceKind.Stone, recipe.StoneCost, x, y);

        _sim.Events.Record(
            _sim.Clock,
            History.WorldEventType.BuildingStarted,
            store.NameOrOverride(slot) + " 开始建造" + BuildingRegistry.NameOf(kind) + " @ " + new Int2(x, y),
            History.EventImportance.Normal,
            new Int2(x, y),
            slot,
            index,
            "造价 木" + recipe.WoodCost.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                + " 石" + recipe.StoneCost.ToString("0", System.Globalization.CultureInfo.InvariantCulture));

        buildingIndex = index;
        return true;
    }

    private float AvailableFromPiles(ResourceKind kind, int x, int y)
    {
        GroundStockConfig config = _sim.Config.GroundStocks;
        if (!_sim.GroundStocks.TryFindNearby(x, y, kind, config.SearchRadius, out int pile, out int _)) { return 0f; }
        return _sim.GroundStocks.AmountOf(pile, kind);
    }

    private float AvailableFromStorage(ResourceKind kind, int x, int y)
    {
        if (!TryFindNearestStorage(x, y, out int index, out int _)) { return 0f; }
        return _sim.Storage.AmountOf(index, kind);
    }

    /// <summary>找出附近的仓库建筑（M3 的共享库存载体）。</summary>
    public bool TryFindNearestStorage(int x, int y, out int index, out int distance)
    {
        index = -1;
        distance = int.MaxValue;

        int radius = _sim.Config.Buildings.StorageSearchRadius;
        for (int k = 0; k < _store.LiveCount; k++)
        {
            int candidate = _store.LiveAt(k);
            if (!_store.IsAlive(candidate)) { continue; }
            if (_store.KindOf(candidate) != BuildingKind.Storage) { continue; }
            if (_store.StateOf(candidate) != BuildingState.Complete) { continue; }

            int d = System.Math.Max(
                System.Math.Abs(_store.XOf(candidate) - x),
                System.Math.Abs(_store.YOf(candidate) - y));

            if (d < distance && d <= radius)
            {
                distance = d;
                index = candidate;
            }
        }

        return index >= 0;
    }

    /// <summary>
    /// 从"随身 → 仓库 → 地面堆"按顺序扣掉指定数量。
    /// 返回值是实际扣掉的总量（正常情况下等于请求量，因为调用方已经先检查过）。
    /// </summary>
    private float Consume(AgentStore store, int slot, ResourceKind kind, float amount, int x, int y)
    {
        float remaining = amount;

        float carried = store.InventoryOf(slot, kind);
        if (carried > 0f)
        {
            float take = carried < remaining ? carried : remaining;
            store.AddInventory(slot, kind, -take);
            remaining -= take;
        }

        if (remaining > 1e-4f && TryFindNearestStorage(x, y, out int storageIndex, out int _))
        {
            float taken = _sim.Storage.Withdraw(storageIndex, kind, remaining);
            remaining -= taken;
        }

        if (remaining > 1e-4f)
        {
            GroundStockConfig config = _sim.Config.GroundStocks;
            if (_sim.GroundStocks.TryFindNearby(x, y, kind, config.SearchRadius, out int pile, out int _))
            {
                Int2 pilePosition = _sim.GroundStocks.PositionOf(pile);
                float taken = _sim.GroundStocks.Withdraw(pilePosition.X, pilePosition.Y, kind, remaining);
                remaining -= taken;
            }
        }

        return amount - remaining;
    }

    /// <summary>重置统计（世界重建时）。</summary>
    public void ResetStatistics()
    {
        CompletedThisTick = 0;
        WorkThisTick = 0;
    }
}
