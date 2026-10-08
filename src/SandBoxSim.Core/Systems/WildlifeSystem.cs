using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Systems;

/// <summary>
/// 野生动物系统（M2；第 41 节）。
///
/// 规则只有四条，但它们的组合足以产生"种群随植被波动"的可观察现象：
///   1. **吃**：所在格的植被越多，恢复的能量越多（植被是食物）；
///   2. **逃**：附近有人就往外跑（因此打猎会变难，而不是"点一下就拿到肉"）；
///   3. **繁殖**：能量充足 + 没超过环境容量（由植被总量决定）时按概率产仔；
///   4. **死**：能量归零饿死，或按自然死亡率老死。
///
/// 与环境层的连接是**双向**的：
///   植被（环境） → 动物能量 → 种群规模 → 猎人产出 → 人的食物来源
/// 因此"玩家砍光森林"会沿着这条链一路传到人口（第 41 / 94 条）。
/// </summary>
public sealed class WildlifeSystem
{
    private readonly Simulation _sim;
    private readonly WildlifeStore _store;
    private readonly WildlifeConfig _config;

    /// <summary>本 tick 的繁殖与死亡数（统计与调试用）。</summary>
    public int BirthsThisTick { get; private set; }
    public int DeathsThisTick { get; private set; }

    /// <summary>当前环境容量（由植被总量决定）。</summary>
    public int EnvironmentCapacity { get; private set; }

    /// <summary>最近一次统计时全图植被总量（一天更新一次，避免每 tick 扫图）。</summary>
    public float TotalVegetation { get; private set; }

    public WildlifeSystem(Simulation sim, WildlifeStore store)
    {
        _sim = sim ?? throw new System.ArgumentNullException(nameof(sim));
        _store = store ?? throw new System.ArgumentNullException(nameof(store));
        _config = sim.Config.Wildlife;
    }

    /// <summary>
    /// 初始种群：按植被分布撒一遍。
    /// 只在世界生成后调用一次（由 Simulation.RegenerateWorld 触发）。
    /// </summary>
    public void SeedInitialPopulation()
    {
        World world = _sim.World;
        DeterministicRandom rng = _sim.Random.Get(RngStream.Events);

        float vegetation = CountVegetation(world);
        TotalVegetation = vegetation;
        EnvironmentCapacity = (int)System.Math.Min(_config.InitialPopulationCap, vegetation * _config.CapacityPerVegetationTile);
        if (EnvironmentCapacity < 4) { EnvironmentCapacity = 4; }

        // 在地图上随机撒，落点优先选植被多的格子（用拒绝采样：最多尝试 4 次）
        int target = EnvironmentCapacity / 2;
        for (int i = 0; i < target; i++)
        {
            int bestX = -1;
            int bestY = -1;
            float bestVegetation = -1f;

            for (int attempt = 0; attempt < 4; attempt++)
            {
                int x = rng.NextInt(world.Width);
                int y = rng.NextInt(world.Height);
                Tile tile = world.TileAt(x, y);
                if (!tile.Walkable) { continue; }
                if (tile.Vegetation <= bestVegetation) { continue; }
                bestVegetation = tile.Vegetation;
                bestX = x;
                bestY = y;
            }

            if (bestX < 0) { continue; }
            if (bestVegetation < 0.15f) { continue; }   // 荒地不产动物

            _store.Add(world, bestX, bestY, 6, rng);
        }
    }

    private static float CountVegetation(World world)
    {
        float sum = 0f;
        Tile[] tiles = world.Tiles;
        for (int i = 0; i < tiles.Length; i++)
        {
            if (!TerrainInfo.IsVegetation(tiles[i].Terrain)) { continue; }
            sum += tiles[i].Vegetation;
        }
        return sum;
    }

    /// <summary>
    /// 每 tick 更新全部动物。
    /// 注意顺序：先感知威胁决定移动方向，再移动，最后结算能量。
    /// 反过来会出现"刚吃饱就被抓到"的突兀感（而且逃跑会失效）。
    /// </summary>
    public void Tick(long tick)
    {
        BirthsThisTick = 0;
        DeathsThisTick = 0;

        World world = _sim.World;
        DeterministicRandom rng = _sim.Random.Get(RngStream.Events);
        AgentStore humans = _sim.Agents;

        float hungerPerTick = 1f / world.Calendar.TicksPerDay;

        // "附近有没有人"的扫描降频：每 N tick 才重新感知一次威胁。
        //
        // 为什么必须降频（这是被实测数据抓出来的）：
        // 每只动物每 tick 都要问一次"最近的人在哪"，而这个问题要遍历全部人口。
        // 50 只动物 × 40 个人 × 每 tick，在 150 天长跑里就是数亿次比较 ——
        // 实测把 100 天长跑的吞吐从约 42,000 tick/秒压到了约 5,800 tick/秒。
        //
        // 降频为什么是安全的：动物的逃跑方向不需要每 tick 精确更新
        // （它们一次只走一格，人也不会在一 tick 内跨过 6 格）。
        // 每 3 tick 重新感知一次，行为上看不出差别，代价降到 1/3。
        const int SenseInterval = 3;
        bool senseThisTick = (tick % SenseInterval) == 0;

        for (int k = 0; k < _store.LiveCount; k++)
        {
            int index = _store.LiveAt(k);
            if (!_store.IsAlive(index)) { continue; }

            // 注意：本循环里可能会 Kill（末尾交换删除）。删除会让"原本排在末尾的个体"
            // 落到当前下标 k 上，因此每轮都要自查 IsAlive 并把 k 回退一格，
            // 否则会跳过一只动物（表现为"种群数量偶尔莫名少一只"）。
            int x = _store.XOf(index);
            int y = _store.YOf(index);
            float thirst = SimMath.Clamp01(_store.ThirstOf(index) + hungerPerTick * 0.2f);
            float fatigue = SimMath.Clamp01(_store.FatigueOf(index) + hungerPerTick * 0.3f);
            float eatUtility = 1 - _store.EnergyOf(index);
            float drinkUtility = thirst * thirst;
            float sleepUtility = fatigue * fatigue;
            ActionKind action = drinkUtility > eatUtility && drinkUtility > sleepUtility ? ActionKind.Drink
                : sleepUtility > eatUtility ? ActionKind.Sleep : ActionKind.Eat;

            // ---- 1) 逃跑：附近有人就往外跑（感知降频，见上面的说明） ----
            // 注意 `hunterSlot` / `hunterDistance` 必须先给出确定值：
            // 短路求值 `senseThisTick && TryFindNearest(..., out slot, ...)` 在
            // senseThisTick 为 false 时**不会执行 out 赋值**，直接使用就会报 CS0165
            // （这是个好错误：它挡住了"用了未定义的值"这类更难查的问题）。
            int hunterSlot = -1;
            int hunterDistance = int.MaxValue;
            bool threatened = senseThisTick
                && humans.TryFindNearest(x, y, _config.FleeRadius, out hunterSlot, out hunterDistance);
            Int2 predator = default;
            bool predatorThreat = senseThisTick && _sim.Predators.Nearest(x, y, _config.FleeRadius, out predator, out int _);
            int stepX = 0;
            int stepY = 0;

            if (threatened || predatorThreat)
            {
                action = ActionKind.Flee;
                int hx = predatorThreat ? predator.X : humans.XOf(hunterSlot);
                int hy = predatorThreat ? predator.Y : humans.YOf(hunterSlot);

                // 远离猎人（优先沿差距更大的那个轴）
                int dx = x - hx;
                int dy = y - hy;
                if (System.Math.Abs(dx) >= System.Math.Abs(dy)) { stepX = dx > 0 ? 1 : -1; }
                else { stepY = dy > 0 ? 1 : -1; }

                _ = hunterDistance;
            }
            else if (action == ActionKind.Eat || action == ActionKind.Drink)
            {
                // ---- 2) 觅食：朝植被更多的邻格走（贪心，不需要寻路） ----
                // 贪心在这里是合适的：动物只需要"大致往草多的地方去"，
                // 不需要最优路径；这也让动物的行为明显比人"笨"，符合直觉。
                float bestVegetation = action == ActionKind.Drink ? world.TileAtClamped(x, y).Moisture : world.TileAtClamped(x, y).Vegetation;
                for (int direction = 0; direction < 4; direction++)
                {
                    int nx = x;
                    int ny = y;
                    switch (direction)
                    {
                        case 0: nx++; break;
                        case 1: nx--; break;
                        case 2: ny++; break;
                        default: ny--; break;
                    }

                    Tile tile = world.TileAtClamped(nx, ny);
                    if (!tile.Walkable) { continue; }
                    float value = action == ActionKind.Drink ? tile.Moisture : tile.Vegetation;
                    if (value <= bestVegetation) { continue; }

                    bestVegetation = value;
                    stepX = nx - x;
                    stepY = ny - y;
                }
            }

            // ---- 3) 移动（带一点随机抖动，避免整群动物排成一条线） ----
            if (action != ActionKind.Sleep && stepX == 0 && stepY == 0 && rng.NextDouble() < 0.25)
            {
                int direction = rng.NextInt(4);
                switch (direction)
                {
                    case 0: stepX = 1; break;
                    case 1: stepX = -1; break;
                    case 2: stepY = 1; break;
                    default: stepY = -1; break;
                }
            }

            if (stepX != 0 || stepY != 0)
            {
                int nx = x + stepX;
                int ny = y + stepY;
                if (world.IsInBounds(nx, ny) && world.TileAt(nx, ny).Walkable)
                {
                    _store.SetPosition(index, nx, ny);
                    x = nx;
                    y = ny;
                }
            }

            // ---- 4) 进食：把所在格的植被转化为能量 ----
            Tile here = world.TileAtClamped(x, y);
            float available = TerrainInfo.IsVegetation(here.Terrain) ? here.Vegetation : 0f;
            if (available > 0f && action == ActionKind.Eat)
            {
                // 吃得越多恢复越快，但消耗植被（因此动物多的地方会把自己吃穷）
                float eaten = _store.EnergyOf(index) < 0.9f ? System.Math.Min(available, hungerPerTick * 2f) : 0;
                _store.AddEnergy(index, eaten * 0.25f);
                if (eaten > 0) { world.SetVegetation(x, y, available - eaten); }
            }

            // ---- 5) 能量衰减 ----
            _store.AddEnergy(index, -hungerPerTick * 0.35f);
            if (action == ActionKind.Sleep) { fatigue = SimMath.Clamp01(fatigue - hungerPerTick * 2f); }
            if (action == ActionKind.Drink && CanDrinkAt(world, x, y)) { thirst = SimMath.Clamp01(thirst - hungerPerTick * 2f); }
            if (thirst >= 0.95f) { _store.AddEnergy(index, -hungerPerTick); }
            _store.SetBehavior(index, thirst, fatigue, action);

            // ---- 6) 饿死 ----
            if (_store.EnergyOf(index) <= 0f)
            {
                _store.Kill(index, hunted: false);
                DeathsThisTick++;
                k--;   // 末尾个体被换到了 k 上，回退一格以免跳过它
            }
        }
    }

    /// <summary>
    /// 每天的种群更新：繁殖、自然死亡、环境容量重算。
    /// 放在日边界而不是每 tick —— 种群规模是慢变量，没有理由让它每 tick 都算。
    /// </summary>
    public void TickDay(long tick)
    {
        World world = _sim.World;
        DeterministicRandom rng = _sim.Random.Get(RngStream.Events);
        for (int i = 0; i < world.Tiles.Length; i++)
        {
            ref Tile tile = ref world.Tiles[i];
            if (!TerrainInfo.IsVegetation(tile.Terrain) || tile.Fire != FireState.None) { continue; }
            float growth = _config.VegetationGrowthPerDay * System.Math.Max(0.02f, tile.Vegetation)
                * (1 - tile.Vegetation) * (1-.8f*tile.FootTraffic) * tile.Fertility * (0.25f + tile.Moisture * 0.75f);
            if (growth <= 0) { continue; }
            tile.Vegetation = SimMath.Clamp01(tile.Vegetation + growth);
            world.MarkDirtyAt(i % world.Width, i / world.Width);
        }

        TotalVegetation = CountVegetation(world);
        EnvironmentCapacity = (int)System.Math.Min(2000, TotalVegetation * _config.CapacityPerVegetationTile);
        if (EnvironmentCapacity < 4) { EnvironmentCapacity = 4; }

        int population = _store.LiveCount;

        // 拥挤惩罚：种群越接近环境容量，繁殖越难（Logistic 的离散版本）
        float crowding = EnvironmentCapacity > 0 ? (float)population / EnvironmentCapacity : 1f;
        float birthChance = _config.ReproductionChancePerDay * SimMath.Clamp01(1f - crowding);
        float deathChance = _config.NaturalDeathChancePerDay;

        // 同样遍历"存活列表"而不是整个容量数组。
        // 注意末尾交换删除的影响：Kill 之后 k 要回退一格，否则会跳过一个个体。
        for (int k = 0; k < _store.LiveCount; k++)
        {
            int index = _store.LiveAt(k);
            if (!_store.IsAlive(index)) { continue; }

            int age = _store.AgeDaysOf(index) + 1;
            _store.SetAgeDays(index, age);

            // 自然死亡（年龄越大越容易）
            float ageFactor = 1f + (age / 60f);
            if (rng.Chance(deathChance * ageFactor))
            {
                _store.Kill(index, hunted: false);
                DeathsThisTick++;
                k--;
                continue;
            }

            // 繁殖：能量要够（有存粮才生），且未超容量。
            // Add 只占用空闲槽位、不移动已有元素，因此新增个体不会打乱本次遍历
            // （它会落在 LiveCount 之后，明天才参与更新）。
            if (population + BirthsThisTick >= EnvironmentCapacity) { continue; }
            if (_store.EnergyOf(index) < 0.55f) { continue; }
            if (!rng.Chance(birthChance)) { continue; }

            int born = _store.Add(world, _store.XOf(index), _store.YOf(index), 4, rng);
            if (born >= 0)
            {
                // 繁殖消耗一部分能量：否则"生到容量上限"会变成零成本行为
                _store.AddEnergy(index, -0.25f);
                BirthsThisTick++;
            }
        }

        _ = tick;
    }

    /// <summary>
    /// 猎杀一只动物。返回获得的食物量（0 表示没杀到）。
    /// 由 <see cref="ActionSystem"/> 在 Hunt 动作完成时调用。
    /// </summary>
    public float Hunt(int hunterX, int hunterY)
    {
        if (!_store.TryFindNearest(hunterX, hunterY, _config.HuntRange, out int index, out int _))
        {
            return 0f;
        }

        _store.Kill(index, hunted: true);
        return _config.FoodPerKill;
    }

    /// <summary>附近是否有可猎杀的动物（Hunt 动作的效用与选靶都读它）。</summary>
    public bool HasPreyNearby(int x, int y, int radius)
        => _store.TryFindNearest(x, y, radius, out int _, out int _);

    public void ResetStatistics()
    {
        BirthsThisTick = 0;
        DeathsThisTick = 0;
    }

    /// <summary>水边或湿润植被提供饮水；持续干旱会阻断露水来源。</summary>
    public static bool CanDrinkAt(World world, int x, int y)
    {
        var tile = world.TileAtClamped(x, y);
        if (tile.Moisture >= 0.3f && tile.Vegetation > 0.05f) { return true; }
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (world.TileAtClamped(x + dx, y + dy).Terrain == TerrainKind.Water) { return true; }
        return false;
    }
}
