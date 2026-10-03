using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Save;

/// <summary>
/// 存档的**读取**部分（写与读拆成两个文件，见 <see cref="SaveFile"/> 的说明）。
///
/// # 恢复顺序是硬约束，不能随意调整
///
/// ```text
/// 1. 校验版本（不匹配直接拒绝，不做静默降级）
/// 2. 重建世界：seed + 尺寸 → 生成 → 用存档覆盖地形/资源/湿度/温度/肥沃度/植被/建筑锚点
/// 3. 恢复时钟（tick）与天气
/// 4. 导入全部随机流状态
/// 5. 恢复实体：人 → 动物 → 建筑 → 共享库存 → 地面物资堆
/// 6. 恢复累计统计
/// 7. 刷新空间索引 + 校验不变量
/// ```
///
/// 其中两条顺序**必须**成立，否则会出现"看起来读进来了但其实坏了"的状态：
///
///   * **地形先于建筑**：建筑通过 <c>Tile.BuildingId</c> 锚定在格子上，
///     先恢复建筑再恢复地形，锚点会被地形覆盖清掉（表现为建筑凭空消失）。
///   * **地形先于空间索引刷新**：`RefreshSpatialIndex` 会重算 chunk 聚合统计，
///     早于地形恢复调用会让 AI 依据过期的资源分布做决定。
///
/// 这类"顺序敏感"的地方最容易在重构时被无意打乱，因此在这里写成清单而不是散在代码里。
/// </summary>
public static class SaveLoader
{
    /// <summary>解析并恢复一个存档到**已存在的** <see cref="Simulation"/> 实例。</summary>
    public static SaveFile.LoadResult Load(Simulation sim, string json)
    {
        var result = new SaveFile.LoadResult();

        JsonValue root;
        try
        {
            root = JsonParser.Parse(json);
        }
        catch (System.Exception ex)
        {
            result.Error = "存档不是合法 JSON：" + ex.Message;
            return result;
        }

        if (!root.IsObject)
        {
            result.Error = "存档根节点必须是对象";
            return result;
        }

        int version = root.GetInt("version", -1);
        if (version != SaveFile.CurrentVersion)
        {
            // 刻意不做向后兼容：静默降级会让"确定性验收"变成假通过 ——
            // 载入一个缺字段的档案，之后发现演化分叉，却不知道该怀疑格式还是模拟。
            result.Error = "存档版本不匹配：文件是 v" + version + "，本程序需要 v" + SaveFile.CurrentVersion;
            return result;
        }

        int width = root.GetInt("width", 0);
        int height = root.GetInt("height", 0);
        if (width <= 0 || height <= 0)
        {
            result.Error = "存档缺少合法的世界尺寸";
            return result;
        }

        if (width != sim.World.Width || height != sim.World.Height)
        {
            result.Error = "存档的世界尺寸（" + width + "×" + height + "）与当前世界（"
                + sim.World.Width + "×" + sim.World.Height + "）不一致；请用相同尺寸启动";
            return result;
        }

        // ---- 1) 地形与天气（必须在实体之前） ----
        JsonValue tiles = root.Get("tiles");
        if (!tiles.IsObject)
        {
            result.Error = "存档缺少 tiles 段";
            return result;
        }

        RestoreTiles(sim.World, tiles);
        RestoreWeather(sim.World, root.Get("weather"));

        // seed 必须一起恢复。
        //
        // 坑在这里：读档的常规做法是"先用命令行给的 seed 造一个空世界，
        // 再用存档覆盖逐格数据"。只覆盖地形而忘了改 Seed，世界里就会留下
        // "地形属于 seed 555、Seed 字段却写着 839102"的自相矛盾状态 ——
        // 而 seed 参与状态摘要，于是读档瞬间摘要就不一致。
        // 只要调用方恰好传了与存档相同的 seed，这个 bug 会完全隐藏（见 RestoreSeed 的注释）。
        int seed = root.GetInt("seed", sim.World.Seed);
        sim.World.RestoreSeed(seed);

        long tick = root.GetLong("tick", 0);
        sim.World.RestoreTick(tick);

        // ---- 2) 随机流（必须在任何实体恢复之前） ----
        // 为什么必须早于实体：恢复个体的决策相位时会读决策间隔，
        // 虽然目前不消耗随机数，但把"随机源就位"放在最前面能杜绝
        // "恢复过程中不小心抽了一次随机数"这一类极难发现的偏移。
        RestoreRng(sim, root.Get("rng"));

        // ---- 3) 实体 ----
        RestoreAgents(sim, root.Get("agents"));
        RestoreWildlife(sim, root.Get("wildlife"));
        RestoreBuildings(sim, root.Get("buildings"));
        RestoreStorage(sim, root.Get("storage"));
        RestoreGroundStocks(sim, root.Get("groundStocks"));

        // ---- 4) 累计统计 ----
        RestoreStats(sim, root.Get("stats"));

        // ---- 5) 收尾 ----
        sim.World.RefreshSpatialIndex();
        sim.NotifyAfterLoad();

        // ---- 6) chunk 聚合统计 ----
        // **必须放在所有 RefreshSpatialIndex 之后**：那两次刷新是"从头重算"，
        // 会把恢复出来的增量统计覆盖掉。而增量统计与从头重算**不一定相等**，
        // 且 AI 会读它（ActionSearch.TryFindWaterAccess 看 WaterTiles）——
        // 顺序弄反的表现就是"读档后第 31 tick 突然分叉"（见 EncodeChunks 的注释）。
        RestoreChunks(sim.World, root.Get("chunks"));

        result.Success = true;
        result.Seed = seed;
        result.Tick = tick;
        result.Day = root.GetInt("day", 0);

        // 自校验：把读档后的摘要与写档时写进文件的那个比对。
        //
        // 注意这里**不因此把 Success 置为 false**：载入本身是成功的，
        // 而"摘要不一致"是另一类问题（漏了状态）。分开报能让调用方
        // 决定是"拒绝启动"还是"带着警告继续" —— 但对确定性验收而言，
        // DigestMatches=false 就是失败。由调用方（测试 / CLI）来判断。
        result.ExpectedDigest = root.GetString("digest", string.Empty);
        result.ActualDigest = StateHash.ComputeDigest(sim);
        result.ExpectedSegments = root.GetString("segments", string.Empty);
        result.ActualSegments = StateHash.DescribeSegments(sim);
        return result;
    }

    private static void RestoreTiles(World world, JsonValue tiles)
    {
        Tile[] target = world.Tiles;
        int count = System.Math.Min(target.Length, tiles.Get("terrain").Count);

        JsonValue terrain = tiles.Get("terrain");
        JsonValue fire = tiles.Get("fire");
        JsonValue kind = tiles.Get("resourceKind");
        JsonValue amount = tiles.Get("resourceAmount");
        JsonValue capacity = tiles.Get("resourceCapacity");
        JsonValue moisture = tiles.Get("moisture");
        JsonValue temperature = tiles.Get("temperature");
        JsonValue fertility = tiles.Get("fertility");
        JsonValue vegetation = tiles.Get("vegetation");
        JsonValue buildingId = tiles.Get("buildingId");

        for (int i = 0; i < count; i++)
        {
            ref Tile tile = ref target[i];

            tile.Terrain = (TerrainKind)NumberAt(terrain, i);
            tile.Fire = (FireState)NumberAt(fire, i);
            tile.Resource.Kind = (ResourceKind)NumberAt(kind, i);
            tile.Resource.Amount = (float)NumberAtFloat(amount, i);
            tile.Resource.Capacity = (float)NumberAtFloat(capacity, i);
            tile.Moisture = SimMath.Clamp01((float)NumberAtFloat(moisture, i));
            tile.Temperature = SimMath.Clamp01((float)NumberAtFloat(temperature, i));
            tile.Fertility = SimMath.Clamp01((float)NumberAtFloat(fertility, i));
            tile.Vegetation = SimMath.Clamp01((float)NumberAtFloat(vegetation, i));
            tile.BuildingId = NumberAt(buildingId, i);

            // Walkable / Buildable 是地形的**派生值**，不入档，这里重算。
            // 「派生值不入档」是一条通用规则：两个真相来源迟早会不一致。
            tile.ApplyTerrainRules();
        }
    }

    private static void RestoreWeather(World world, JsonValue weather)
    {
        if (!weather.IsObject) { return; }

        world.Weather.RestoreFromSave(
            (WeatherKind)weather.GetInt("kind", (int)WeatherKind.Clear),
            weather.GetInt("durationHours", 0),
            weather.GetInt("hoursUntilChange", 0),
            weather.GetFloat("accumulatedRain", 0f),
            weather.GetInt("droughtHours", 0));
    }

    private static void RestoreRng(Simulation sim, JsonValue rng)
    {
        if (!rng.IsArray || rng.Items.Count == 0) { return; }

        var state = new ulong[rng.Items.Count][];
        for (int s = 0; s < rng.Items.Count; s++)
        {
            JsonValue stream = rng.Items[s];
            var values = new ulong[stream.Items.Count];
            for (int i = 0; i < stream.Items.Count; i++)
            {
                // 用字符串读写 ulong：JSON 的数字是 double，超过 2^53 会丢精度，
                // 而 xoshiro 的状态就是 64 位 —— 直接写数字会埋下"偶尔分叉"的隐患。
                values[i] = stream.Items[i].AsULong();
            }
            state[s] = values;
        }

        sim.Random.ImportState(state);
    }

    private static ulong ParseULong(JsonValue value)
    {
        if (value.IsNumber) { return (ulong)value.NumberValue; }

        string text = value.ValueKind == JsonValue.Kind.String ? value.StringValue : string.Empty;
        return ulong.TryParse(text, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out ulong parsed) ? parsed : 0UL;
    }

    private static void RestoreAgents(Simulation sim, JsonValue agents)
    {
        if (!agents.IsObject) { return; }

        AgentStore store = sim.Agents;
        JsonValue list = agents.Get("list");

        store.ClearAllKeepCapacity();
        store.EnsureCapacity(list.Items.Count + 1);

        // 迁移冷却也要先清空再逐条恢复：它是"不进摘要但影响未来行为"的状态，
        // 残留旧值会让读档后的人莫名其妙不能搬家（或反过来，立刻集体搬家）。
        sim.Migration.ClearAllCooldowns();

        // 小数步进度同理：它决定"下一 tick 会不会跨到下一格"。
        sim.Actions.ClearAllMoveProgress();

        for (int i = 0; i < list.Items.Count; i++)
        {
            JsonValue a = list.Items[i];
            int slot = a.GetInt("slot", -1);
            if (slot < 0) { continue; }

            var personality = new Personality
            {
                Aggression = a.GetFloat("aggression", 0.5f),
                Greed = a.GetFloat("greed", 0.5f),
                Kindness = a.GetFloat("kindness", 0.5f),
                Bravery = a.GetFloat("bravery", 0.5f),
                Industriousness = a.GetFloat("industriousness", 0.5f),
                Sociability = a.GetFloat("sociability", 0.5f),
            };

            store.RestoreAgent(
                slot,
                a.GetInt("generation", 0),
                a.GetInt("x", 0), a.GetInt("y", 0),
                a.GetInt("homeX", 0), a.GetInt("homeY", 0),
                (byte)a.GetInt("facing", 0),
                a.GetFloat("hunger", 0f), a.GetFloat("fatigue", 0f),
                a.GetFloat("thirst", 0f), a.GetFloat("social", 0f),
                a.GetFloat("health", 1f),
                a.GetInt("ageDays", 0),
                (LifeStage)a.GetInt("lifeStage", (int)LifeStage.Adult),
                (JobType)a.GetInt("job", 0),
                a.GetFloat("invFood", 0f), a.GetFloat("invWood", 0f),
                a.GetFloat("invStone", 0f), a.GetFloat("invIron", 0f),
                personality,
                (AgentState)a.GetInt("state", (int)AgentState.Idle),
                (ActionKind)a.GetInt("action", 0),
                (ActionPhase)a.GetInt("phase", (int)ActionPhase.Idle),
                a.GetInt("targetX", 0), a.GetInt("targetY", 0),
                a.GetInt("actionTicks", 0),
                a.GetBool("hasPathStep", false),
                a.GetInt("pathStepX", 0), a.GetInt("pathStepY", 0),
                a.GetInt("decisionPhase", 0),
                a.GetLong("nextDecisionTick", 0),
                a.GetLong("migrateUntil", 0),
                a.GetLong("birthTick", 0));

            string name = a.GetString("name", string.Empty);
            if (!string.IsNullOrEmpty(name)) { store.SetNameOverride(slot, name); }

            sim.Migration.RestoreCooldown(slot, a.GetLong("migrationCooldownUntil", 0));
            sim.Actions.RestoreMoveProgress(slot, a.GetFloat("moveProgress", 0f));
        }

        store.RestoreCounters(agents.GetInt("totalBorn", 0), agents.GetInt("totalDied", 0));
    }

    private static void RestoreWildlife(Simulation sim, JsonValue wildlife)
    {
        if (!wildlife.IsObject) { return; }

        WildlifeStore store = sim.Wildlife;
        JsonValue list = wildlife.Get("list");

        store.ClearAllKeepCapacity();
        store.EnsureCapacity(list.Items.Count + 1);

        for (int i = 0; i < list.Items.Count; i++)
        {
            JsonValue w = list.Items[i];

            // 必须恢复到**存档里的槽位**，不能按列表顺序重新紧凑排列。
            // WildlifeStore.HashInto 会把槽位下标混进摘要，重排会让摘要不一致 ——
            // 世界其实是对的，却看起来像"读档把世界改坏了"。
            int slot = w.GetInt("slot", -1);
            if (slot < 0) { continue; }

            store.RestoreAnimal(slot, w.GetInt("x", 0), w.GetInt("y", 0),
                w.GetFloat("energy", 0.5f), w.GetInt("ageDays", 0));
        }

        store.RestoreCounters(
            wildlife.GetInt("totalBorn", 0),
            wildlife.GetInt("totalDied", 0),
            wildlife.GetInt("totalHunted", 0));

        // 存活列表的**顺序**是状态的一部分：WildlifeSystem 按这个顺序遍历并消耗随机数，
        // 恢复成升序会让读档后第一 tick 就把随机数分给不同的动物（摘要却完全一致）。
        JsonValue order = wildlife.Get("liveOrder");
        if (order.IsArray)
        {
            var liveOrder = new int[order.Items.Count];
            for (int i = 0; i < order.Items.Count; i++) { liveOrder[i] = order.Items[i].AsInt(); }
            store.RestoreLiveOrder(liveOrder);
        }

        // 槽位分配提示：决定下一只新生的动物落在哪个槽位，而槽位进摘要。
        store.NextFreeHint = wildlife.GetInt("nextFreeHint", 0);
    }

    private static void RestoreBuildings(Simulation sim, JsonValue buildings)
    {
        if (!buildings.IsObject) { return; }

        BuildingStore store = sim.Buildings;
        JsonValue list = buildings.Get("list");

        store.SetWorldWidth(sim.World.Width);
        store.ClearAllKeepCapacity();
        store.EnsureCapacity(list.Items.Count + 1);

        for (int i = 0; i < list.Items.Count; i++)
        {
            JsonValue b = list.Items[i];
            int slot = b.GetInt("slot", -1);
            if (slot < 0) { continue; }

            store.RestoreBuilding(
                slot,
                (BuildingKind)b.GetInt("kind", 0),
                (BuildingState)b.GetInt("state", 0),
                b.GetInt("x", 0), b.GetInt("y", 0),
                b.GetInt("workDone", 0),
                b.GetInt("workRequired", 1),
                b.GetLong("builtTick", 0));
        }
    }

    private static void RestoreStorage(Simulation sim, JsonValue storage)
    {
        if (!storage.IsArray) { return; }

        sim.Storage.Reset();
        sim.Storage.EnsureCapacity(sim.Buildings.Capacity);

        for (int i = 0; i < storage.Items.Count; i++)
        {
            JsonValue s = storage.Items[i];
            int slot = s.GetInt("slot", -1);
            if (slot < 0) { continue; }

            sim.Storage.SetCapacity(slot, s.GetFloat("capacity", 0f));
            sim.Storage.RestoreResource(slot, ResourceKind.Food, s.GetFloat("food", 0f));
            sim.Storage.RestoreResource(slot, ResourceKind.Wood, s.GetFloat("wood", 0f));
            sim.Storage.RestoreResource(slot, ResourceKind.Stone, s.GetFloat("stone", 0f));
            sim.Storage.RestoreResource(slot, ResourceKind.Iron, s.GetFloat("iron", 0f));
        }
    }

    private static void RestoreGroundStocks(Simulation sim, JsonValue stocks)
    {
        if (!stocks.IsObject) { return; }

        sim.GroundStocks.ClearAllKeepCapacity();

        JsonValue list = stocks.Get("list");
        sim.GroundStocks.EnsureCapacity(list.Items.Count + 1);

        for (int i = 0; i < list.Items.Count; i++)
        {
            JsonValue pile = list.Items[i];

            // 同野生动物：槽位进摘要，必须原样恢复，不能重新紧凑排列。
            int slot = pile.GetInt("slot", -1);
            if (slot < 0) { continue; }

            var stock = new ResourceStock
            {
                Food = pile.GetFloat("food", 0f),
                Wood = pile.GetFloat("wood", 0f),
                Stone = pile.GetFloat("stone", 0f),
                Iron = pile.GetFloat("iron", 0f),
            };

            sim.GroundStocks.RestorePile(slot, pile.GetInt("x", 0), pile.GetInt("y", 0), stock);
        }

        sim.GroundStocks.RestoreCounters(
            stocks.GetFloat("totalDeposited", 0f),
            stocks.GetFloat("totalWithdrawn", 0f));
    }

    /// <summary>
    /// 恢复 chunk 聚合统计。
    ///
    /// 这些值**不进状态摘要**（摘要是逐格算的），但 AI 会读它们，
    /// 因此"读档时重算一遍"与"直接跑的增量维护"只要有一个数不同，
    /// 世界就会在若干 tick 后走上另一条路。详见 <see cref="SaveFile"/> 里 EncodeChunks 的说明。
    /// </summary>
    private static void RestoreChunks(World world, JsonValue chunks)
    {
        if (!chunks.IsObject) { return; }

        JsonValue fieldRows = chunks.Get("fields");
        JsonValue counts = chunks.Get("cellCounts");
        JsonValue dirty = chunks.Get("dirty");

        int chunkCount = fieldRows.Items.Count;
        if (chunkCount == 0) { return; }

        var fields = new double[chunkCount][];
        for (int c = 0; c < chunkCount; c++)
        {
            JsonValue row = fieldRows.Items[c];
            fields[c] = new double[row.Items.Count];
            for (int f = 0; f < row.Items.Count; f++) { fields[c][f] = row.Items[f].AsDouble(); }
        }

        var cellCounts = new int[counts.Items.Count];
        for (int c = 0; c < counts.Items.Count; c++) { cellCounts[c] = counts.Items[c].AsInt(); }

        var dirtyFlags = new bool[dirty.Items.Count];
        for (int c = 0; c < dirty.Items.Count; c++) { dirtyFlags[c] = dirty.Items[c].AsInt() != 0; }

        world.Chunks.RestoreState(fields, cellCounts, dirtyFlags);
    }

    private static void RestoreStats(Simulation sim, JsonValue stats)
    {
        if (!stats.IsObject) { return; }

        // 这两个计数参与状态摘要，必须恢复（见 SimulationStats.RestoreCounters 的注释）
        sim.Stats.RestoreCounters(
            stats.GetInt("totalBirths", 0),
            stats.GetInt("totalDeaths", 0),
            stats.GetInt("totalMigrations", 0));

        // 迁移系统还有自己的一个计数器（只用于报告），与 Stats 的**不是同一个**。
        // 两者都要恢复：漏掉任意一个，报告或摘要就会对不上。
        sim.Migration.RestoreCounters(stats.GetInt("migrationSystemMigrations", 0));

        sim.ResourceSystem.RestoreCounters(
            stats.GetDouble("totalHarvested", 0d),
            stats.GetLong("depletionEvents", 0L));

        sim.Actions.RestoreCounters(
            stats.GetFloat("totalFoodEaten", 0f),
            stats.GetFloat("totalDeposited", 0f),
            stats.GetFloat("totalTaken", 0f));
    }

    // ---------------------------------------------------------------------
    // 数组读取辅助
    // ---------------------------------------------------------------------

    private static int NumberAt(JsonValue array, int index)
    {
        if (!array.IsArray || index < 0 || index >= array.Items.Count) { return 0; }
        JsonValue v = array.Items[index];
        return v.IsNumber ? (int)System.Math.Round(v.NumberValue, System.MidpointRounding.AwayFromZero) : 0;
    }

    private static double NumberAtFloat(JsonValue array, int index)
    {
        if (!array.IsArray || index < 0 || index >= array.Items.Count) { return 0d; }
        JsonValue v = array.Items[index];
        return v.AsDouble();
    }
}
