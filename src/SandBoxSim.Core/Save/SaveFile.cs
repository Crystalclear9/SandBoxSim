using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Save;

/// <summary>
/// 存档的读写（第 76 节）。
///
/// # 格式选择的理由
///
/// 用**手写的 JSON 对象图**，而不是反射自动序列化。看起来更啰嗦，但换来两件事：
///
///   1. **漏字段是可见的**。反射自动序列化时，新增一个字段而忘记加进存档，
///      表现为"读档后这个字段变成默认值"，而且**只在读档路径上出错** ——
///      这是最难查的一类 bug。手写清单时，"忘记"表现为代码里没有那一行。
///   2. **零依赖**。本仓库不允许引入任何 NuGet 包（见 docs/12 的双通道约定），
///      而 `System.Text.Json` 虽然随 .NET 提供，但它的对象图与反射行为
///      在通道 B（csc 直编 BCL-only 源码）下需要额外的引用配置。
///
/// # 确定性契约（本文件最重要的一节）
///
/// 存档必须做到"读档后精确续跑"，因此**凡参与状态摘要的状态都必须被保存**。
/// 具体包括三类容易被漏掉的：
///
///   * **全部 8 条随机流的状态**（不只是"当前种子"）——
///     漏一条就会让读档后的天气/决策序列与直接跑分叉；
///   * **个体的决策相位与下次决策时刻** —— 它们是分批决策的调度状态，
///     漏掉会让所有人在读档瞬间同时决策（世界观感突变 + 演化分叉）；
///   * **迁移意愿（`migrateUntil`）** —— 它决定"这个人现在是不是在搬家路上"，
///     漏掉会让他停下来，几万 tick 后表现为人口分布完全不同。
///
/// 判据不是"这个字段看起来重不重要"，而是"**它会不会影响未来的行为**"。
///
/// # 刻意不保存的东西
///
///   * `UtilityBreakdown`（决策解释）：它是"解释"而不是"状态"，下一次决策会重算。
///     保存它反而危险 —— 读档后会显示一个过期 tick 的解释，看起来像世界卡住了。
///   * 逐日统计曲线：它只影响报告，不影响演化。读档后曲线从空开始，
///     并在文件里显式记录"曲线被重置"这件事（见 <see cref="LoadResult"/>）。
/// </summary>
public static class SaveFile
{
    /// <summary>
    /// 存档格式版本。
    ///
    /// 版本不匹配时**明确拒绝载入**，不做静默降级 ——
    /// 静默降级会让"确定性验收"变成假通过：载入了一个缺字段的档案，
    /// 之后发现演化分叉，却不知道该怀疑格式还是模拟。
    ///
    /// 版本历史：
    ///   * v1 —— 初版（M4b）
    ///   * v2 —— 补上 `Tile.Resource.RegenerationRate`。
    ///     它是"按格写死、读档时不会重算"的字段，漏掉会导致读档后
    ///     第一个小时边界上大面积再生量跑偏（详见 EncodeTiles 的说明）。
    /// </summary>
    public const int CurrentVersion = 2;

    /// <summary>载入结果。</summary>
    public sealed class LoadResult
    {
        public bool Success;
        public string Error = string.Empty;

        /// <summary>存档里的世界种子（用于核对"是不是你以为的那个世界"）。</summary>
        public int Seed;

        /// <summary>存档里的 tick 数。</summary>
        public long Tick;

        /// <summary>存档里的日期（人类可读）。</summary>
        public int Day;

        /// <summary>写档瞬间的状态摘要（用于自校验）。</summary>
        public string ExpectedDigest = string.Empty;

        /// <summary>读档之后算出来的状态摘要。</summary>
        public string ActualDigest = string.Empty;

        /// <summary>写档瞬间的分段摘要（用于把不一致定位到具体段落）。</summary>
        public string ExpectedSegments = string.Empty;

        /// <summary>读档之后的分段摘要。</summary>
        public string ActualSegments = string.Empty;

        /// <summary>存档里的配置指纹。</summary>
        public string ExpectedConfigDigest = string.Empty;

        /// <summary>当前生效配置的指纹。</summary>
        public string ActualConfigDigest = string.Empty;

        /// <summary>
        /// 当前配置是否与存档里的配置一致。
        ///
        /// <c>false</c> **不是错误** —— "同一世界换一套规则再跑"是一个正当的实验。
        /// 但它是**必须被告知**的一件事，因为状态摘要不覆盖配置：
        /// 换了规则之后摘要依然可能一致，玩家会以为自己在复现原来的实验。
        /// </summary>
        public bool ConfigMatches
            => ExpectedConfigDigest.Length == 0 || ExpectedConfigDigest == ActualConfigDigest;

        /// <summary>
        /// 第一处不一致的段落描述（全同则空）。这是读档失败时最有用的信息。
        /// </summary>
        public string SegmentDifference
            => StateHash.FirstSegmentDifference(ExpectedSegments, ActualSegments);

        /// <summary>
        /// 读档后的摘要是否与写档时一致。
        ///
        /// <c>false</c> 意味着"漏了某个影响状态的状态" —— 这是一条**比 Success 更强的**判据：
        /// 载入过程可以完全"成功"（没抛异常、字段都读到了），却仍然恢复出了一个不同的世界。
        /// </summary>
        public bool DigestMatches => ExpectedDigest.Length > 0 && ExpectedDigest == ActualDigest;
    }

    // ---------------------------------------------------------------------
    // 写入
    // ---------------------------------------------------------------------

    /// <summary>把一次模拟的全部状态编码成一个 JSON 字符串。</summary>
    public static string Encode(Simulation sim)
    {
        World world = sim.World;

        JsonValue root = JsonValue.Object()
            .Set("version", JsonValue.From(CurrentVersion))
            .Set("seed", JsonValue.From(world.Seed))
            .Set("width", JsonValue.From(world.Width))
            .Set("height", JsonValue.From(world.Height))
            .Set("tick", JsonValue.From(world.Tick))
            .Set("day", JsonValue.From(world.Calendar.Day))
            .Set("weather", EncodeWeather(world))
            .Set("tiles", EncodeTiles(world))
            .Set("rng", EncodeRng(sim))
            .Set("agents", EncodeAgents(sim))
            .Set("wildlife", EncodeWildlife(sim))
            .Set("buildings", EncodeBuildings(sim))
            .Set("storage", EncodeStorage(sim))
            .Set("groundStocks", EncodeGroundStocks(sim))
            .Set("stats", EncodeStats(sim))
            .Set("relationships", EncodeRelationships(sim))
            .Set("chunks", EncodeChunks(world))
            .Set("config", JsonBinder.ToJson(sim.Config));

        // **自校验摘要**：把写档瞬间的状态摘要也写进文件。
        //
        // 为什么值得占这点体积：读档的正确判据是"摘要与写档时一致"，
        // 而如果这个期望值只存在于写档进程的内存里，那么任何一次读档失败
        // 都只能靠"再跑一遍对照"来发现 —— 我们在 CLI 端到端验证时就正是这么被坑的：
        // 存档进程打印了一个摘要，读档进程打印了另一个，但**没有任何一处把两者对上**。
        // 写进文件之后，读档可以自己判断，并且能报出"差在哪个方向"。
        root.Set("digest", JsonValue.From(StateHash.ComputeDigest(sim)));

        // 分段摘要：读档失败时能直接指出"是哪一段漏了状态"，
        // 而不是只报一个对不上的大数字（见 StateHash.DescribeSegments 的注释）。
        root.Set("segments", JsonValue.From(StateHash.DescribeSegments(sim)));

        // 配置指纹。**为什么需要它**：状态摘要（StateHash）**不包含配置** ——
        // 它只覆盖世界状态。于是"用一套不同的规则去读同一份存档"会**通过自校验**
        // （摘要一致），然后跑出一个不同的世界。这本身可以是**有意**的实验
        // （同一世界、换规则），但绝不能是静默的：玩家必须知道这次读档换了规则。
        root.Set("configDigest", JsonValue.From(ConfigFingerprint(sim.Config)));

        return root.ToJson(indented: true);
    }

    /// <summary>
    /// 配置指纹（FNV-1a64 over 紧凑 JSON）。
    ///
    /// 依赖"同一份配置产出同一串紧凑 JSON"这一点：`JsonBinder` 的字段顺序
    /// 由插入顺序决定，因此同一版本的代码对同一份配置是稳定的。
    /// 跨版本可能变化 —— 那时指纹不同会**多报一次警告**，
    /// 而这个方向的误报是安全的一侧（宁可提醒，不可静默）。
    /// </summary>
    public static string ConfigFingerprint(SimConfig config)
    {
        string compact = JsonBinder.ToJson(config).ToJson(indented: false);
        return Hash64.ToDigestString(Hash64.Combine(Hash64.Begin(), compact));
    }

    private static JsonValue EncodeWeather(World world)
    {
        Weather weather = world.Weather;
        return JsonValue.Object()
            .Set("kind", JsonValue.From((int)weather.Kind))
            .Set("durationHours", JsonValue.From(weather.DurationHours))
            .Set("hoursUntilChange", JsonValue.From(weather.HoursUntilChange))
            .Set("accumulatedRain", JsonValue.From(weather.AccumulatedRain))
            .Set("droughtHours", JsonValue.From(weather.DroughtHours));
    }

    /// <summary>
    /// 逐格编码。10 万格时这会是一个很大的数组，因此**逐字段用扁平数组**而不是
    /// "每格一个对象"：后者会产生 10 万个 JSON 对象与同样多的键名，文件体积大约十倍。
    ///
    /// 只存"会变"的字段：地形、火灾、资源（种类/量/容量/再生率）、湿度、温度、肥沃度、植被、建筑锚点。
    /// 不存 `Walkable` / `Buildable` —— 它们是地形的派生值，读档时由 `ApplyTerrainRules` 重算。
    /// **派生值不入档**是一条通用规则：两个真相来源迟早会不一致。
    ///
    /// # 字段清单的判据（这里踩过一个很贵的坑）
    ///
    /// 判据是"**它会不会影响未来的行为**"，而不是"它看起来像不像地形的一部分"。
    /// `Resource.RegenerationRate` 看起来像"配置的派生值"（毕竟 `resources` 段里
    /// 有 `foodGrowthRate` / `woodGrowthRate`），但它是**按格存在 Tile 里**的，
    /// 而且**读档时不会被重算** —— 它是世界生成时按格写死的。
    ///
    /// 漏掉它的后果非常隐蔽：读档时用命令行 seed 生成的地形会**留下自己的再生率**，
    /// 于是同一格上出现"种类是食物（r=0.03）、再生率却是木材的 0.02"这种自相矛盾的状态。
    /// 表现是读档瞬间**逐位完全一致**（再生率不进摘要），
    /// 到第一个小时边界（读档后第 60 tick）再生一次，3637/10000 格同时跑偏。
    /// 详见 <see cref="SaveLoader"/> 与 `SaveLoadTests` 的对应用例。
    /// </summary>
    private static JsonValue EncodeTiles(World world)
    {
        Tile[] tiles = world.Tiles;
        int count = tiles.Length;

        var terrain = JsonValue.Array();
        var fire = JsonValue.Array();
        var resourceKind = JsonValue.Array();
        var resourceAmount = JsonValue.Array();
        var resourceCapacity = JsonValue.Array();
        var resourceRegenRate = JsonValue.Array();
        var moisture = JsonValue.Array();
        var temperature = JsonValue.Array();
        var fertility = JsonValue.Array();
        var vegetation = JsonValue.Array();
        var buildingId = JsonValue.Array();

        for (int i = 0; i < count; i++)
        {
            ref readonly Tile tile = ref tiles[i];
            terrain.Add(JsonValue.From((int)tile.Terrain));
            fire.Add(JsonValue.From((int)tile.Fire));
            resourceKind.Add(JsonValue.From((int)tile.Resource.Kind));
            resourceAmount.Add(JsonValue.From(tile.Resource.Amount));
            resourceCapacity.Add(JsonValue.From(tile.Resource.Capacity));
            resourceRegenRate.Add(JsonValue.From(tile.Resource.RegenerationRate));
            moisture.Add(JsonValue.From(tile.Moisture));
            temperature.Add(JsonValue.From(tile.Temperature));
            fertility.Add(JsonValue.From(tile.Fertility));
            vegetation.Add(JsonValue.From(tile.Vegetation));
            buildingId.Add(JsonValue.From(tile.BuildingId));
        }

        return JsonValue.Object()
            .Set("terrain", terrain)
            .Set("fire", fire)
            .Set("resourceKind", resourceKind)
            .Set("resourceAmount", resourceAmount)
            .Set("resourceCapacity", resourceCapacity)
            .Set("resourceRegenerationRate", resourceRegenRate)
            .Set("moisture", moisture)
            .Set("temperature", temperature)
            .Set("fertility", fertility)
            .Set("vegetation", vegetation)
            .Set("buildingId", buildingId);
    }

    private static JsonValue EncodeRng(Simulation sim)
    {
        ulong[][] state = sim.Random.ExportState();
        var streams = JsonValue.Array();

        for (int s = 0; s < state.Length; s++)
        {
            var values = JsonValue.Array();
            for (int i = 0; i < state[s].Length; i++)
            {
                // 用字符串写 ulong：JSON 的数字是 double，超过 2^53 会丢精度，
                // 而 xoshiro 的状态就是 64 位。**这是最容易埋下"看起来对但偶尔分叉"的地方。**
                values.Add(JsonValue.From(state[s][i].ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
            streams.Add(values);
        }

        return streams;
    }

    /// <summary>
    /// 个体编码：**按槽位升序**（不是存活列表顺序）。
    /// 顺序必须确定，否则"同一状态两次存档"会产生不同文件（不利于人工比对差异）。
    /// </summary>
    /// <summary>
    /// 关系表（M6）。
    ///
    /// **必须按 key 升序写出**：`RelationshipStore` 内部是 `Dictionary`，
    /// 而字典的枚举顺序不保证稳定 —— 直接枚举会让"同一局游戏"存出两份不同的文件，
    /// 进而让摘要校验在毫不相关的地方失败。
    /// </summary>
    private static JsonValue EncodeRelationships(Simulation sim)
    {
        var list = JsonValue.Array();
        var pairs = sim.Relationships.PairsAscending();

        for (int i = 0; i < pairs.Count; i++)
        {
            long key = pairs[i].Key;
            list.Add(JsonValue.Object()
                .Set("a", JsonValue.From(RelationshipStore.LowOf(key)))
                .Set("b", JsonValue.From(RelationshipStore.HighOf(key)))
                .Set("affinity", JsonValue.From(pairs[i].Value.Affinity))
                .Set("interactions", JsonValue.From(pairs[i].Value.Interactions))
                .Set("lastTick", JsonValue.From(pairs[i].Value.LastTick)));
        }

        return JsonValue.Object().Set("list", list);
    }

    private static JsonValue EncodeAgents(Simulation sim)
    {
        AgentStore agents = sim.Agents;
        var list = JsonValue.Array();

        for (int slot = 0; slot < agents.Capacity; slot++)
        {
            if (!agents.IsSlotAlive(slot)) { continue; }

            Personality personality = agents.PersonalityOf(slot);
            Int2 target = agents.TargetOf(slot);

            list.Add(JsonValue.Object()
                .Set("slot", JsonValue.From(slot))
                .Set("generation", JsonValue.From(agents.GenerationOf(slot)))
                .Set("name", JsonValue.From(agents.NameOf(slot)))
                .Set("x", JsonValue.From(agents.XOf(slot)))
                .Set("y", JsonValue.From(agents.YOf(slot)))
                .Set("homeX", JsonValue.From(agents.HomeXOf(slot)))
                .Set("homeY", JsonValue.From(agents.HomeYOf(slot)))
                .Set("facing", JsonValue.From((int)agents.FacingOf(slot)))
                .Set("hunger", JsonValue.From(agents.HungerOf(slot)))
                .Set("fatigue", JsonValue.From(agents.FatigueOf(slot)))
                .Set("thirst", JsonValue.From(agents.ThirstOf(slot)))
                .Set("social", JsonValue.From(agents.SocialOf(slot)))
                .Set("health", JsonValue.From(agents.HealthOf(slot)))
                .Set("ageDays", JsonValue.From(agents.AgeDaysOf(slot)))
                .Set("lifeStage", JsonValue.From((int)agents.LifeStageOf(slot)))
                .Set("job", JsonValue.From((int)agents.JobOf(slot)))
                .Set("invFood", JsonValue.From(agents.InventoryOf(slot, ResourceKind.Food)))
                .Set("invWood", JsonValue.From(agents.InventoryOf(slot, ResourceKind.Wood)))
                .Set("invStone", JsonValue.From(agents.InventoryOf(slot, ResourceKind.Stone)))
                .Set("invIron", JsonValue.From(agents.InventoryOf(slot, ResourceKind.Iron)))
                .Set("aggression", JsonValue.From(personality.Aggression))
                .Set("greed", JsonValue.From(personality.Greed))
                .Set("kindness", JsonValue.From(personality.Kindness))
                .Set("bravery", JsonValue.From(personality.Bravery))
                .Set("industriousness", JsonValue.From(personality.Industriousness))
                .Set("sociability", JsonValue.From(personality.Sociability))
                .Set("state", JsonValue.From((int)agents.StateOf(slot)))
                .Set("action", JsonValue.From((int)agents.ActionOf(slot)))
                .Set("phase", JsonValue.From((int)agents.PhaseOf(slot)))
                .Set("targetX", JsonValue.From(target.X))
                .Set("targetY", JsonValue.From(target.Y))
                .Set("actionTicks", JsonValue.From(agents.ActionTicksOf(slot)))
                .Set("hasPathStep", JsonValue.From(agents.HasPathStep(slot)))
                .Set("pathStepX", JsonValue.From(agents.PathStepOf(slot).X))
                .Set("pathStepY", JsonValue.From(agents.PathStepOf(slot).Y))
                .Set("moveProgress", JsonValue.From(sim.Actions.MoveProgressOf(slot)))
                .Set("decisionPhase", JsonValue.From(agents.DecisionPhaseOf(slot)))
                .Set("nextDecisionTick", JsonValue.From(agents.NextDecisionTickOf(slot)))
                .Set("migrateUntil", JsonValue.From(agents.MigrateUntilOf(slot)))
                .Set("migrationCooldownUntil", JsonValue.From(sim.Migration.CooldownUntilOf(slot)))
                // M4 家庭与住所：都影响未来行为，因此既进摘要也进存档
                .Set("partnerSlot", JsonValue.From(agents.PartnerOf(slot)))
                .Set("motherSlot", JsonValue.From(agents.MotherOf(slot)))
                .Set("fatherSlot", JsonValue.From(agents.FatherOf(slot)))
                .Set("childCount", JsonValue.From(agents.ChildCountOf(slot)))
                .Set("lastBirthTick", JsonValue.From(agents.LastBirthTickOf(slot)))
                .Set("dwelling", JsonValue.From(agents.DwellingOf(slot)))
                .Set("birthTick", JsonValue.From(agents.BirthTickOf(slot))));
        }

        // 全部槽位的代次（含已死槽位）。
        //
        // 必须存：代次会被下一个占用该槽位的人继承，而"死槽位的代次"
        // 在编码与摘要里都看不见 —— 于是它成为一个只能靠"某天有人复用该槽位"
        // 才暴露的漏状态。详见 AgentStore.ExportGenerations 的注释。
        int[] generations = agents.ExportGenerations();
        var generationList = JsonValue.Array();
        for (int i = 0; i < generations.Length; i++) { generationList.Add(JsonValue.From(generations[i])); }

        return JsonValue.Object()
            .Set("totalBorn", JsonValue.From(agents.TotalBorn))
            .Set("totalDied", JsonValue.From(agents.TotalDied))
            .Set("peakPopulation", JsonValue.From(agents.PeakPopulation))
            // M4 起必须存：出生会新增个体，而"下一个空槽在哪"决定新生儿落在哪个槽位，
            // 槽位又进摘要 ⇒ 不存就会在第一次出生之后分叉。
            .Set("nextFreeHint", JsonValue.From(agents.NextFreeHint))
            .Set("generations", generationList)
            .Set("list", list);
    }

    private static JsonValue EncodeWildlife(Simulation sim)
    {
        WildlifeStore wildlife = sim.Wildlife;
        var list = JsonValue.Array();

        // 按**存活列表顺序**编码，并且单独存一份顺序数组。
        //
        // 为什么顺序也要存（这是个反直觉但很关键的坑）：存活列表的顺序会随删除
        // （末尾交换填补空位）变化，而 WildlifeSystem 是按这个顺序遍历并消耗随机数的。
        // 只存"哪些槽位活着"、恢复成升序的话，摘要会完全一致（摘要按槽位升序算），
        // 但下一 tick 起随机数会被分配给不同的动物 —— 立刻分叉。
        // 见 WildlifeStore.RestoreLiveOrder 的详细说明。
        int[] liveOrder = wildlife.ExportLiveOrder();
        var order = JsonValue.Array();
        for (int i = 0; i < liveOrder.Length; i++) { order.Add(JsonValue.From(liveOrder[i])); }

        for (int index = 0; index < wildlife.Capacity; index++)
        {
            if (!wildlife.IsAlive(index)) { continue; }

            // **槽位必须存**：WildlifeStore.HashInto 会把槽位下标混进摘要，
            // 因此"把它恢复成紧凑的 0..N-1"会算出不同的摘要。
            list.Add(JsonValue.Object()
                .Set("slot", JsonValue.From(index))
                .Set("x", JsonValue.From(wildlife.XOf(index)))
                .Set("y", JsonValue.From(wildlife.YOf(index)))
                .Set("energy", JsonValue.From(wildlife.EnergyOf(index)))
                .Set("ageDays", JsonValue.From(wildlife.AgeDaysOf(index))));
        }

        return JsonValue.Object()
            .Set("liveOrder", order)
            .Set("nextFreeHint", JsonValue.From(wildlife.NextFreeHint))
            .Set("totalBorn", JsonValue.From(wildlife.TotalBorn))
            .Set("totalDied", JsonValue.From(wildlife.TotalDied))
            .Set("totalHunted", JsonValue.From(wildlife.TotalHunted))
            .Set("list", list);
    }

    private static JsonValue EncodeBuildings(Simulation sim)
    {
        BuildingStore buildings = sim.Buildings;
        var list = JsonValue.Array();

        for (int index = 0; index < buildings.Capacity; index++)
        {
            if (!buildings.IsAlive(index)) { continue; }

            list.Add(JsonValue.Object()
                .Set("slot", JsonValue.From(index))
                .Set("kind", JsonValue.From((int)buildings.KindOf(index)))
                .Set("state", JsonValue.From((int)buildings.StateOf(index)))
                .Set("x", JsonValue.From(buildings.XOf(index)))
                .Set("y", JsonValue.From(buildings.YOf(index)))
                .Set("workDone", JsonValue.From(buildings.WorkDoneOf(index)))
                .Set("workRequired", JsonValue.From(buildings.WorkRequiredOf(index)))
                .Set("builtTick", JsonValue.From(buildings.BuiltTickOf(index)))
                // M4：床位占用、农田劳动量、完整度（都进摘要，都影响未来行为）
                .Set("occupiedBeds", JsonValue.From(buildings.OccupiedBedsOf(index)))
                .Set("labor", JsonValue.From(buildings.LaborOf(index)))
                .Set("decay", JsonValue.From(buildings.DecayOf(index))));
        }

        return JsonValue.Object()
            .Set("totalBuilt", JsonValue.From(buildings.TotalBuilt))
            .Set("totalDemolished", JsonValue.From(buildings.TotalDemolished))
            .Set("occupiedBeds", JsonValue.From(buildings.OccupiedBeds))
            .Set("nextFreeHint", JsonValue.From(buildings.NextFreeHint))
            .Set("list", list);
    }

    private static JsonValue EncodeStorage(Simulation sim)
    {
        StorageStore storage = sim.Storage;
        var list = JsonValue.Array();

        for (int index = 0; index < sim.Buildings.Capacity; index++)
        {
            float capacity = storage.CapacityOf(index);
            if (capacity <= 0f) { continue; }

            list.Add(JsonValue.Object()
                .Set("slot", JsonValue.From(index))
                .Set("capacity", JsonValue.From(capacity))
                .Set("food", JsonValue.From(storage.AmountOf(index, ResourceKind.Food)))
                .Set("wood", JsonValue.From(storage.AmountOf(index, ResourceKind.Wood)))
                .Set("stone", JsonValue.From(storage.AmountOf(index, ResourceKind.Stone)))
                .Set("iron", JsonValue.From(storage.AmountOf(index, ResourceKind.Iron))));
        }

        return list;
    }

    private static JsonValue EncodeGroundStocks(Simulation sim)
    {
        GroundStockStore stocks = sim.GroundStocks;
        var list = JsonValue.Array();

        foreach (int index in stocks.AliveIndices())
        {
            Int2 position = stocks.PositionOf(index);
            list.Add(JsonValue.Object()
                .Set("slot", JsonValue.From(index))
                .Set("x", JsonValue.From(position.X))
                .Set("y", JsonValue.From(position.Y))
                .Set("food", JsonValue.From(stocks.AmountOf(index, ResourceKind.Food)))
                .Set("wood", JsonValue.From(stocks.AmountOf(index, ResourceKind.Wood)))
                .Set("stone", JsonValue.From(stocks.AmountOf(index, ResourceKind.Stone)))
                .Set("iron", JsonValue.From(stocks.AmountOf(index, ResourceKind.Iron))));
        }

        return JsonValue.Object()
            .Set("totalDeposited", JsonValue.From(stocks.TotalDeposited))
            .Set("totalWithdrawn", JsonValue.From(stocks.TotalWithdrawn))
            .Set("list", list);
    }

    /// <summary>
    /// chunk 聚合统计（第 60 节的 ChunkGrid）。
    ///
    /// # 为什么这也必须进存档（一个花了很久才定位到的问题）
    ///
    /// 直觉上 chunk 统计是**派生数据**（由地形聚合而来），不该进存档 —— 重算一遍就好。
    /// 但它是**增量维护**的：某个格子变了就顺手更新所在 chunk 的计数。
    /// 而 AI 真的会读它：`ActionSearch.TryFindWaterAccess` 先看
    /// `stats.WaterTiles == 0` 决定要不要跳过这个 chunk。
    ///
    /// 于是出现了这样一条极难查的分叉链：
    ///   * 直接跑 → chunk 统计是增量累积的；
    ///   * 读档   → chunk 统计是从地形**从头重算**的；
    ///   * 两者只要有一个数不同 → 某个人"找得到水"/"找不到水"不同 →
    ///     他在 tick 31 选了 Drink 而不是 Eat → 世界从此走上另一条路。
    /// 实测就是这个：读档瞬间摘要**完全一致**（chunk 统计不进摘要），
    /// 续跑到第 31 tick 才分叉，且所有随机流状态逐位相同。
    ///
    /// 所以判据仍然是"会不会影响未来的行为"：会，因此必须存。
    /// （`ChunkGrid.ExportState/RestoreState` 早就为此准备好了，只是没人调用。）
    /// </summary>
    private static JsonValue EncodeChunks(World world)
    {
        world.Chunks.ExportState(out double[][] fields, out int[] cellCounts, out bool[] dirty);

        var fieldRows = JsonValue.Array();
        for (int c = 0; c < fields.Length; c++)
        {
            var row = JsonValue.Array();
            for (int f = 0; f < fields[c].Length; f++)
            {
                // 用 "R" 往返格式写 double（见 JsonValue.FormatNumber），因此这里不会有精度损失
                row.Add(JsonValue.From(fields[c][f]));
            }
            fieldRows.Add(row);
        }

        var counts = JsonValue.Array();
        for (int c = 0; c < cellCounts.Length; c++) { counts.Add(JsonValue.From(cellCounts[c])); }

        var dirtyFlags = JsonValue.Array();
        for (int c = 0; c < dirty.Length; c++) { dirtyFlags.Add(JsonValue.From(dirty[c])); }

        return JsonValue.Object()
            .Set("fields", fieldRows)
            .Set("cellCounts", counts)
            .Set("dirty", dirtyFlags);
    }

    private static JsonValue EncodeStats(Simulation sim)
    {
        // 注意这里的原则：**存进去的必须正好是摘要会读的那些值**。
        //
        // 踩过一次：迁移次数有两个计数器 —— `Stats.TotalMigrations`（摘要读它）
        // 与 `Migration.TotalMigrations`（迁移系统自己的统计）。
        // 一开始存的是后者、摘要读的是前者，于是 CLI 端到端读档时
        // stats 段永远对不上（而小规模测试里两个计数恰好相等，所以没暴露）。
        // 现在两个都存、各恢复各的。
        return JsonValue.Object()
            .Set("totalBirths", JsonValue.From(sim.Stats.TotalBirths))
            .Set("totalDeaths", JsonValue.From(sim.Stats.TotalDeaths))
            .Set("totalMigrations", JsonValue.From(sim.Stats.TotalMigrations))
            .Set("migrationSystemMigrations", JsonValue.From(sim.Migration.TotalMigrations))
            .Set("totalHarvested", JsonValue.From(sim.ResourceSystem.TotalHarvested))
            .Set("depletionEvents", JsonValue.From(sim.ResourceSystem.DepletionEvents))
            .Set("totalFoodEaten", JsonValue.From(sim.Actions.TotalFoodEaten))
            .Set("totalHunted", JsonValue.From(sim.Wildlife.TotalHunted))

            // M4c：分批数是**行为状态**（它决定每个相位在哪一 tick 决策）。
            // 不存它、而是在读档时"重新算一遍"，会在分批数发生变化时
            // 顺手重排所有人的决策相位 —— 见 AiSystem.AdoptBatchCountAfterLoad。
            .Set("aiBatchCount", JsonValue.From(sim.Ai.BatchCount))
            .Set("aiLastPhaseChangeTick", JsonValue.From(sim.Ai.LastPhaseChangeTick));
    }
}
