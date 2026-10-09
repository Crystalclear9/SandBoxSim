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
            if (kind == BuildingKind.Storage)
            {
                _sim.Storage.EnsureCapacity(_store.Capacity);
                _sim.Storage.ClearSlot(index);
                _sim.Storage.SetCapacity(index, BuildingRegistry.Of(kind).StorageCapacity);
            }

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

            if (d <= radius && (d < distance || (d == distance && (index < 0 || candidate < index))))
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
        FoodProducedThisDay = 0f;
        FoodSpoiledThisDay = 0f;
        DemolishedThisDay = 0;
        TotalFoodProduced = 0f;
        TotalDemolished = 0;
        _decayBuffer.Clear();
    }

    // ---------------------------------------------------------------------
    // M4：农业产出、建筑衰减、住所
    // ---------------------------------------------------------------------

    /// <summary>本日农田产出的食物总量。</summary>
    public float FoodProducedThisDay { get; private set; }

    /// <summary>
    /// 累计农田产出（M4）。
    ///
    /// 为什么要有累计值而不只是"本日产出"：验收判据是"**有农田的世界食物增速更高**"，
    /// 而那是一个**对照实验**。只暴露瞬时值的话，测试与报告都只能拿"当前库存"猜 ——
    /// 库存同时受采集、消耗、搬运影响，根本分不清哪一部分是农业贡献的。
    /// 累计产出把"农业到底产了多少"变成一个可以直接读出来的数字。
    /// </summary>
    public float TotalFoodProduced { get; private set; }

    /// <summary>本日因衰减归零而被拆除的建筑数。</summary>
    public int DemolishedThisDay { get; private set; }

    /// <summary>累计拆除数。</summary>
    public int TotalDemolished { get; private set; }

    /// <summary>每日一次的农田结算与衰减评估（由 <c>Simulation.TickDay</c> 调用）。</summary>
    public void TickDay(long tick)
    {
        FoodProducedThisDay = 0f;
        DemolishedThisDay = 0;

        TickDecay(tick);
        FoodSpoiledThisDay=LivingAgriculture.SpoilGroundFood(_sim,_organicSlots);
        ProduceFarmYield(tick);
    }
    private readonly System.Collections.Generic.List<int> _organicSlots=new();
    public float FoodSpoiledThisDay {get;private set;}

    /// <summary>
    /// 农田产出（M4）。
    ///
    /// `Yield = Base × 地力 × 湿度 × 天气 × 劳动力`
    ///
    /// 其中**劳动力是关键的一项**：它让"有田"和"有人种田"成为两件不同的事。
    /// 没有它，农田就会变成一台不需要人的自动售货机，
    /// 而任务书要的因果链是"人 → 耕种 → 食物 → 人口"。
    ///
    /// 劳动力由 `Farm` 动作累积（每人每次动作加 `FarmWorkPerAction`），
    /// 每日结算后清零 —— 因此"今天没人下地"这件事是有后果的。
    /// </summary>
    private void ProduceFarmYield(long tick)
    {
        float baseYield = _sim.Config.Buildings.FarmBaseYieldPerDay;
        if (baseYield <= 0f)
        {
            for (int k = 0; k < _store.LiveCount; k++) { _store.ClearLabor(_store.LiveAt(k)); }
            return;
        }

        float unattended = _sim.Config.Buildings.FarmUnattendedFactor;
        float laborBonusMax = _sim.Config.Buildings.FarmLaborBonusMax;
        float laborCap = System.Math.Max(0.01f, _sim.Config.Buildings.FarmLaborPerDayCap);

        for (int k = 0; k < _store.LiveCount; k++)
        {
            int index = _store.LiveAt(k);
            if (!_store.IsAlive(index)) { continue; }
            if (_store.KindOf(index) != BuildingKind.Farm) { continue; }
            if (_store.StateOf(index) != BuildingState.Complete) { continue; }

            Int2 position = _store.PositionOf(index);
            Tile tile = _sim.World.TileAt(position.X, position.Y);

            float labor = _store.LaborOf(index);
            float laborFactor;
            if (labor <= 0f)
            {
                laborFactor = unattended;
            }
            else
            {
                float capped = labor > laborCap ? laborCap : labor;
                laborFactor = 1f + (laborBonusMax * (capped / laborCap));
            }

            float fertility = SimMath.Clamp01(tile.Fertility);
            float moisture = SimMath.Clamp01(tile.Moisture);
            float weather = WeatherFactor();
            int phase=LivingAgriculture.Phase(System.Math.Max(0,tick-1),_sim.World.Calendar.TicksPerDay);
            var crop=LivingAgriculture.CropAt(_sim.World.Seed,position.X,position.Y);
            bool living=_sim.Config.Buildings.LivingAgricultureEnabled;
            if(living&&crop==CropKind.Roots&&_sim.World.Weather.Kind==WeatherKind.Drought)weather=(weather+1)/2;
            float cycle=living?LivingAgriculture.YieldFactor(crop,phase):1;

            // 地力与湿度都取 [0.25, 1] 区间：农田不该因为"这格地力 0.05"而颗粒无收 ——
            // 那会让玩家看到一块田却永远没有产出，无法从界面上理解原因。
            float soil = 0.25f + (0.75f * fertility);
            float water = 0.25f + (0.75f * moisture);

            float yield = baseYield * soil * water * weather * laborFactor * cycle * _sim.Civilizations.ProductionMultiplier(position.X, position.Y);
            LivingAgriculture.UpdateSoil(_sim,index,labor,phase,crop);
            if (yield <= 0f) { _store.ClearLabor(index); continue; }

            yield=DepositYield(position, yield);
            FoodProducedThisDay += yield;
            TotalFoodProduced += yield;

            _store.ClearLabor(index);
        }
    }

    /// <summary>
    /// 天气对农田的影响。
    ///
    /// 干旱与洪涝都压低产量（一个是缺水、一个是淹了），
    /// 而"下雨"略微增产。这样 M5 的灾害工具一接入，
    /// "玩家制造干旱 → 食物下降 → 人口下降"这条链就自动成立了。
    /// </summary>
    private float WeatherFactor()
    {
        WeatherKind kind = _sim.World.Weather.Kind;
        switch (kind)
        {
            case WeatherKind.Drought: return _sim.Config.Buildings.FarmBadWeatherFactor;
            case WeatherKind.Storm: return _sim.Config.Buildings.FarmBadWeatherFactor;
            case WeatherKind.Rain: return 1.1f;
            default: return 1f;
        }
    }

    /// <summary>
    /// 把农田产出放进最近的仓库；没有仓库就放地面物资堆。
    ///
    /// 这个降级顺序是刻意的：它让"还没盖仓库"的早期聚落也能靠农业活下去，
    /// 同时让"盖了仓库"立刻带来好处（不再有堆料损耗与距离成本）。
    /// </summary>
    private float DepositYield(Int2 position, float amount)
    {
        float accepted=0;
        int storage = FindStorageNear(position.X, position.Y);
        if (storage >= 0 && _sim.Storage.CapacityOf(storage) > 0f)
        {
            accepted=_sim.Storage.Deposit(storage, ResourceKind.Food, amount);amount-=accepted;
        }

        return accepted+_sim.GroundStocks.Deposit(position.X, position.Y, ResourceKind.Food, amount, _sim.Config.GroundStocks);
    }

    private int FindStorageNear(int x, int y)
    {
        int radius = _sim.Config.Buildings.StorageSearchRadius;
        int best = -1;
        int bestDistance = int.MaxValue;

        for (int k = 0; k < _store.LiveCount; k++)
        {
            int index = _store.LiveAt(k);
            if (!_store.IsAlive(index)) { continue; }
            if (_store.KindOf(index) != BuildingKind.Storage) { continue; }
            if (_store.StateOf(index) != BuildingState.Complete) { continue; }

            int distance = System.Math.Max(
                System.Math.Abs(_store.XOf(index) - x),
                System.Math.Abs(_store.YOf(index) - y));
            if (distance > radius) { continue; }
            if (distance < bestDistance) { bestDistance = distance; best = index; }
        }

        return best;
    }

    /// <summary>
    /// 找一处有空床的住房（出生时给孩子与父母安排住所）。找不到返回 -1。
    ///
    /// 按"离 (x,y) 最近"排序，因此孩子会住进**父母附近**的房子 ——
    /// 而不是地图另一头，那会立刻把一家人生生分开。
    /// </summary>
    public int FindHouseWithFreeBed(int x, int y)
    {
        int best = -1;
        int bestDistance = int.MaxValue;

        for (int k = 0; k < _store.LiveCount; k++)
        {
            int index = _store.LiveAt(k);
            if (!_store.IsAlive(index)) { continue; }
            if (_store.KindOf(index) != BuildingKind.House) { continue; }
            if (_store.StateOf(index) != BuildingState.Complete) { continue; }
            if (!_store.HasFreeBed(index)) { continue; }

            int distance = System.Math.Max(
                System.Math.Abs(_store.XOf(index) - x),
                System.Math.Abs(_store.YOf(index) - y));
            if (distance < bestDistance) { bestDistance = distance; best = index; }
        }

        return best;
    }

    /// <summary>
    /// 衰减与拆除（M4）。
    ///
    /// 这是给"建造"补上的**负反馈**：M3 的建筑只会单调增加，
    /// 饱和点只是掩盖了"没有维护成本"这件事。
    /// 现在"空房子会烂掉"让"人走了"这件事第一次产生真实代价。
    /// </summary>
    private void TickDecay(long tick)
    {
        BuildingConfig config = _sim.Config.Buildings;
        if (config.DecayPerDay <= 0f) { return; }

        DemolishedThisDay = _store.TickDecay(
            tick,
            config.DecayPerDay,
            config.DecayGraceDays,
            _decayBuffer,
            _sim.World.Calendar.TicksPerDay);

        if (!config.DemolishWhenDecayed) { return; }

        for (int i = 0; i < _decayBuffer.Count; i++)
        {
            int index = _decayBuffer[i];
            if (!_store.IsAlive(index)) { continue; }

            BuildingKind kind = _store.KindOf(index);
            Int2 position = _store.PositionOf(index);

            _store.Demolish(_sim.World, index);
            _sim.Storage.ClearSlot(index);
            TotalDemolished++;

            // 农田被拆掉时地表要恢复成草地 —— 否则会留下一块不能建、也不产出的"死田"
            if (kind == BuildingKind.Farm)
            {
                _sim.World.SetTerrain(position.X, position.Y, TerrainKind.Grass);
            }

            _sim.Events.Record(
                tick,
                History.WorldEventType.BuildingDestroyed,
                BuildingRegistry.NameOf(kind) + "因长期无人维护而倒塌 @ " + position,
                History.EventImportance.Normal,
                position,
                -1,
                index,
                "衰减归零");
        }

        _decayBuffer.Clear();
    }

    private readonly System.Collections.Generic.List<int> _decayBuffer =
        new System.Collections.Generic.List<int>();

    /// <summary>灾害摧毁建筑，同时清理实际住房和库存，避免幽灵床位。</summary>
    public void Destroy(int index, string cause)
    {
        if (!_store.IsAlive(index)) { return; }
        var position = _store.PositionOf(index);
        var kind = _store.KindOf(index);
        foreach (int slot in _sim.Agents.AliveSlots())
            if (_sim.Agents.DwellingOf(slot) == index) { _sim.Agents.SetDwelling(slot, -1); }
        _store.Demolish(_sim.World, index);
        _sim.Storage.ClearSlot(index);
        TotalDemolished++; DemolishedThisDay++;
        _sim.Events.Record(_sim.Clock, History.WorldEventType.BuildingDestroyed,
            BuildingRegistry.NameOf(kind) + "被摧毁 @ " + position,
            History.EventImportance.Important, position, -1, index, cause);
    }
}
