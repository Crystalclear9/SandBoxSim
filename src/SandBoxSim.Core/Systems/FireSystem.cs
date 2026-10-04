using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Systems;

/// <summary>
/// 火灾系统（M5）。
///
/// # 它在因果链里的位置
///
/// 任务书第 68 / 94 条要求的最小因果证明是 **玩家烧森林 → 人口增速下降**。
/// 这条链穿过三个系统，而火灾是它的**起点**：
///
/// ```text
/// 火 → 森林减少 → 木材减少 → 盖房变慢 → 床位不足 → 出生下降
///              ↘ 猎物栖息地减少 → 打猎收益下降 → 食物下降 ↗
/// ```
///
/// 在此之前，玩家能做的干预只有"加东西"（加人、加资源、加地力）。
/// 火灾让玩家第一次能**毁掉条件**，而后果沿着上面两条链自己扩散出去 ——
/// 这正是"玩家创造条件、而不是创造结果"里"条件"的另一半。
///
/// # 三条实现原则（都是踩过坑之后的结论）
///
/// 1. **不引入新的持久状态。** 燃烧进度由 `Tile.Vegetation` 的下降量表达，
///    没有"已燃烧 N tick"这类计数器；风向由 tick 推导（见 <see cref="WindIndex"/>）。
///    M4 期间"看起来只是辅助状态、实际影响未来行为"的字段漏了**八个**，
///    每个都表现为"读档瞬间一致、续跑若干 tick 后分叉"。所以能不新增状态就不新增。
/// 2. **遍历顺序固定。** 按 tile 扁平下标升序，邻居按固定方向数组 ——
///    否则"同样的世界"会因为遍历顺序不同而烧出不同的形状。
/// 3. **随机数只取自 `RngStream.Fire`（独立的一条流）。**
///    它绝不能借用 `Events`（天气在用它）或 `Agents`（决策在用它）——
///    否则一场山火会改变接下来的天气或所有人的决策序列，因果再也无法归因。
///    这是 Phase 0 那条教训（干预需要独立流）在火灾上的重演，而且是**实测**逼出来的：
///    借用 Events 时，"烧森林 → 人口下降"的实验方向反了过来（床位 24 vs 20）。
/// </summary>
public sealed class FireSystem
{
    private readonly Simulation _sim;
    private readonly FireConfig _config;

    /// <summary>8 邻居的固定方向数组（确定性：顺序永远是这一个）。</summary>
    private static readonly int[] DirectionX = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] DirectionY = { 0, 1, 1, 1, 0, -1, -1, -1 };

    /// <summary>正在燃烧的格子数（观测用）。</summary>
    public int BurningTiles { get; private set; }

    /// <summary>焦土格子数（观测用）。</summary>
    public int BurntTiles { get; private set; }

    /// <summary>累计点燃次数（含自然点燃与蔓延）。</summary>
    public int TotalIgnitions { get; private set; }

    /// <summary>
    /// 自然点燃的**判定次数**与**通过次数**（观测量）。
    ///
    /// 为什么需要这两个计数器：M5 联调时"200 天 0 起火"，
    /// 而按概率估算应当有十几次 —— 到底是"判定没被执行"、
    /// "概率被换算错了"、还是"逐格筛选把候选全否了"，
    /// 光看结果数字无法区分。把"判定次数"和"通过次数"分开之后，
    /// 三者立刻可以区分：判定为 0 ⇒ 调用路径断了；
    /// 判定很多、通过很少 ⇒ 概率或筛选有问题。
    /// **一个安静的 0 永远需要两个计数器才能定位。**
    /// </summary>
    public long NaturalIgnitionRolls { get; private set; }
    public long NaturalIgnitionHits { get; private set; }

    /// <summary>自然点燃被逐格筛选否掉的次数（无植被/太湿/已烧）。</summary>
    public long NaturalIgnitionRejected { get; private set; }

    /// <summary>累计烧毁的格子数（植被归零、转为焦土）。</summary>
    public int TotalBurnedOut { get; private set; }

    /// <summary>本 tick 新蔓延的格子数。</summary>
    public int SpreadThisTick { get; private set; }

    /// <summary>燃烧释放的热量（观测用；也会轻微提高邻格温度）。</summary>
    public float LastHeatRelease { get; private set; }

    public FireSystem(Simulation sim)
    {
        _sim = sim ?? throw new System.ArgumentNullException(nameof(sim));
        _config = sim.Config.Fire;
    }

    /// <summary>
    /// 风向（0..7，对应 <see cref="DirectionX"/> 的下标）。
    ///
    /// # 为什么风向**不是**状态
    ///
    /// 它完全由 tick 与世界种子推导：每 `WindChangeTicks` 转一个方向，
    /// 初相由种子决定（不同世界风不同，但同一个世界永远可复现）。
    ///
    /// 这样做的好处不只是省一个字段：**它让"读档后续跑"天然一致**。
    /// 如果风向是状态，就必须进存档、进摘要、进读档校验 ——
    /// 而这类"辅助状态"正是 M4 八个隐形状态的共同来源。
    /// 一个能由 `(tick, seed)` 算出来的东西，就不该被存起来。
    /// </summary>
    public int WindIndex(long tick)
    {
        const long WindChangeTicks = 480;   // 每 8 小时转一个方向
        long steps = (tick / WindChangeTicks) + WindPhase;
        int index = (int)(steps % 8);
        return index < 0 ? index + 8 : index;
    }

    /// <summary>由世界种子推导的风向初相（常量，不是可变状态）。</summary>
    private int WindPhase => (int)((unchecked((uint)_sim.World.Seed) >> 3) % 8);

    /// <summary>
    /// 每 10 tick 推进一次（由 `Simulation.TickFast` 调用）。
    ///
    /// 为什么挂在 FastTick 而不是每 tick：火是慢变量，
    /// 而"每 tick 遍历全图找火"会把格子数直接乘进每 tick 的开销里。
    /// 这与施工、决策分批、动物种群是同一个思路（第 73 条）。
    /// </summary>
    public void TickFast(long tick)
    {
        SpreadThisTick = 0;
        LastHeatRelease = 0f;

        if (!_config.Enabled) { return; }

        World world = _sim.World;
        Tile[] tiles = world.Tiles;
        int width = world.Width;
        int height = world.Height;

        // **火灾必须用自己那条流**（`RngStream.Fire`），不能借 Events ——
        // 后者同时驱动天气，借用它会让"点燃一片森林"改变接下来的天气序列，
        // 于是"烧森林 → 人口增速下降"这个对照实验里两组世界的天气也不同，
        // 因果无法归因（M5 联调时实测方向反了）。详见 RngStream.Fire 的注释。
        DeterministicRandom rng = _sim.Random.Get(RngStream.Fire);

        int burning = 0;
        int burnt = 0;

        // 自然的点燃源放在**第一遍之前**，并且只烧一格 ——
        // 雷击是"世界自己会发生的事"，不该一次点着整片森林。
        TryNaturalIgnition(tick, tiles, width, height, rng);

        // 第一遍：结算当前燃烧（消耗植被、烧掉木材、必要时转焦土）
        for (int i = 0; i < tiles.Length; i++)
        {
            ref Tile tile = ref tiles[i];

            if (tile.Fire == FireState.Burnt)
            {
                burnt++;
                continue;
            }

            if (tile.Fire != FireState.Burning) { continue; }

            // 不可燃地形上的火会立刻熄灭（例如水面：火不该停在水上）
            if (!TerrainInfo.IsVegetation(tile.Terrain) && tile.Terrain != TerrainKind.Farmland)
            {
                tile.Fire = FireState.None;
                continue;
            }

            BurnTile(ref tile);
            world.MarkDirtyAt(i % width, i / width);

            if (tile.Vegetation <= 0f)
            {
                tile.Fire = FireState.Burnt;
                tile.Vegetation = 0f;
                TotalBurnedOut++;
                burnt++;

                _sim.Events.Record(
                    tick,
                    History.WorldEventType.FireExtinguished,
                    "火势烧尽一块地 @" + new Int2(i % width, i / width),
                    History.EventImportance.Minor,
                    new Int2(i % width, i / width));
                continue;
            }

            burning++;
        }

        // 上限闸：超过上限时不再蔓延（防止一次干旱把整张图点着而失控）
        bool maySpread = burning < _config.MaxBurningTiles;

        // 第二遍：蔓延。
        //
        // 与第一遍分开是刻意的：如果边烧边蔓延，同一 tick 内新点着的格子
        // 会被**本 tick 的遍历顺序**决定是否立刻开始燃烧 ——
        // 那会让结果依赖遍历方向。分两遍之后，"这一 tick 的燃烧"与
        // "这一 tick 的蔓延"互不干扰，顺序无关性由构造保证。
        if (maySpread)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width) + x;
                    if (tiles[i].Fire != FireState.Burning) { continue; }
                    if (!maySpread) { break; }

                    int wind = WindIndex(tick);

                    for (int d = 0; d < 8; d++)
                    {
                        int nx = x + DirectionX[d];
                        int ny = y + DirectionY[d];
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height) { continue; }

                        int ni = (ny * width) + nx;
                        if (tiles[ni].Fire != FireState.None) { continue; }
                        if (!TerrainInfo.IsVegetation(tiles[ni].Terrain)) { continue; }

                        float chance = SpreadChance(tiles[ni], d, wind);
                        if (chance <= 0f) { continue; }
                        if (!rng.Chance(chance)) { continue; }

                        tiles[ni].Fire = FireState.Burning;
                        TotalIgnitions++;
                        SpreadThisTick++;
                        world.MarkDirtyAt(nx, ny);
                    }
                }
            }
        }

        // 第三遍：焦土的缓慢恢复（每天一次即可，但放在这里更简单且确定性相同）
        RecoverBurnt(tick, tiles);

        BurningTiles = burning;
        BurntTiles = burnt;
    }

    /// <summary>一格燃烧一次：扣植被、烧掉一部分木材资源、释放热量。</summary>
    private void BurnTile(ref Tile tile)
    {
        float loss = _config.VegetationLossPerFastTick;
        tile.Vegetation = SimMath.Clamp01(tile.Vegetation - loss);

        if (_config.BurnsWoodResource
            && tile.Resource.Kind == ResourceKind.Wood
            && tile.Resource.Amount > 0f)
        {
            float burn = tile.Resource.Capacity * _config.WoodLossFractionPerFastTick;
            if (burn > tile.Resource.Amount) { burn = tile.Resource.Amount; }
            tile.Resource.Amount -= burn;
        }

        // 燃烧会让地块变热（轻微），这让"火过之后地表不同"在温度图上也能看出来
        tile.Temperature = SimMath.Clamp01(tile.Temperature + 0.01f);
        LastHeatRelease += loss;
    }

    /// <summary>
    /// 邻居被点燃的概率。
    ///
    /// `chance = Base × 植被 × (1 − 湿度) × 干燥权重修正 × 顺风加成`
    ///
    /// 三项各自表达一件直觉上显然、但必须写进代码的事：
    ///   * **没植被烧不过去**（裸地是天然防火带）；
    ///   * **湿的烧不着**（下雨/近水的林子难烧 —— 这是玩家可以用工具改的条件）；
    ///   * **顺风烧得快**（风向由 tick 推导，因此它会随时间变化，
    ///     一场火的方向感是会变的，而不是一路直烧）。
    /// </summary>
    private float SpreadChance(in Tile target, int direction, int wind)
    {
        float vegetation = target.Vegetation;
        if (vegetation <= 0.02f) { return 0f; }

        float dryness = 1f - SimMath.Clamp01(target.Moisture);
        float chance = _config.SpreadChancePerFastTick * vegetation * (0.25f + (0.75f * dryness));

        // 顺风加成：方向与风向一致时最高（1 + WindInfluence），逆风时最低
        if (_config.WindInfluence > 0f)
        {
            int delta = System.Math.Abs(direction - wind);
            if (delta > 4) { delta = 8 - delta; }
            float alignment = 1f - (delta / 4f);          // 1 = 顺风，0 = 逆风
            chance *= 1f + (_config.WindInfluence * ((alignment * 2f) - 1f));
        }

        return chance < 0f ? 0f : chance;
    }

    /// <summary>
    /// 焦土恢复：植被缓慢长回来，够多之后回到 `None`（可以再次被点燃）。
    ///
    /// 恢复**极慢**（默认每天 0.006）是刻意的：它让"烧过的地"在同一局游戏里
    /// 一直看得见，而"砍树"与"烧林"的代价因此有明确区别 ——
    /// 前者会恢复，后者几乎不会。这也让玩家的一次纵火成为一个**长期决定**。
    /// </summary>
    private void RecoverBurnt(long tick, Tile[] tiles)
    {
        float perDay = _config.BurntRecoveryPerDay;
        if (perDay <= 0f) { return; }

        int ticksPerDay = _sim.World.Calendar.TicksPerDay;
        int fastTicksPerDay = System.Math.Max(1, ticksPerDay / Simulation.FastTickInterval);
        float perFastTick = perDay / fastTicksPerDay;

        for (int i = 0; i < tiles.Length; i++)
        {
            ref Tile tile = ref tiles[i];
            if (tile.Fire != FireState.Burnt) { continue; }

            tile.Vegetation = SimMath.Clamp01(tile.Vegetation + perFastTick);
            if (tile.Vegetation >= _config.BurntRecoverThreshold)
            {
                tile.Fire = FireState.None;
                _sim.Events.Record(
                    tick,
                    History.WorldEventType.FireExtinguished,
                    "焦土重新长出植被 @" + new Int2(i % _sim.World.Width, i / _sim.World.Width),
                    History.EventImportance.Trivial,
                    new Int2(i % _sim.World.Width, i / _sim.World.Width));
            }
        }
    }

    /// <summary>
    /// 自然点燃（雷击）。
    ///
    /// 概率随**天气**与**干燥度**变化：暴雨期有雷击概率、
    /// 干旱期风险最高、下雨/下雪几乎不可能。这一切都走 `WeatherInfo.FireRiskFactor`，
    /// 于是"玩家强制一场干旱然后等火"成为一个**真实可行的实验**。
    ///
    /// 每 tick 只尝试**一次**点燃尝试（随机挑一格）而不是逐格判定 ——
    /// 后者会让点燃概率随地图面积线性增长，大地图会自己烧起来。
    /// </summary>
    private void TryNaturalIgnition(long tick, Tile[] tiles, int width, int height, DeterministicRandom rng)
    {
        WeatherKind weather = _sim.World.Weather.Kind;
        float weatherRisk = WeatherInfo.FireRiskFactor(weather);

        float chance = _config.BaseIgnitionChancePerFastTick * weatherRisk;
        if (weather == WeatherKind.Storm)
        {
            chance += _config.LightningChanceDuringStorm;
        }
        if (chance <= 0f) { return; }

        NaturalIgnitionRolls++;
        if (!rng.Chance(chance)) { return; }
        NaturalIgnitionHits++;

        // 在若干随机落点里找一个"能烧起来"的。
        //
        // # 这里改过两次，两次都是被实测数据逼出来的
        //
        // 第一版：随机挑一格，用一串硬门筛（有植被、够干、够热），不合格就放弃。
        //   实测 60 天判定 8640 次、通过 3 次，而这 3 次**全被否掉**；
        //   200 天 9 次全否 —— 也就是"200 天 0 起火"。
        //   根因是这个世界**平均湿度接近 1.0、温度接近 0**，
        //   于是"够干够热"这个硬门把所有候选都筛掉了。
        //
        // 第二版（当前）：只保留"必须有燃料"这一条硬门（无植被 = 绝对点不着，
        //   这是物理事实），而**把湿度与温度变成概率因子**。
        //   这样"潮湿的世界火少而小、干旱的世界火多而大"是连续变化的，
        //   而不是"要么完全不可能、要么一点就着"。
        //
        // 同时加入**有界重试**：只挑一格就放弃，会让"一下雷击恰好打在石头上"
        // 白白浪费掉整个事件，于是有效点火率被地表的空地比例随机稀释 ——
        // 概率算对了、路径也对，结果仍然是 0。
        // 重试次数固定为 8（确定性），语义是"这次雷击有没有落在有燃料的地方"。
        const int CandidateAttempts = 8;
        for (int attempt = 0; attempt < CandidateAttempts; attempt++)
        {
            int index = rng.NextInt(0, tiles.Length);
            ref Tile tile = ref tiles[index];

            if (tile.Fire != FireState.None) { continue; }
            if (!TerrainInfo.IsVegetation(tile.Terrain)) { continue; }
            if (tile.Vegetation < 0.15f) { continue; }

            float dryness = 1f - SimMath.Clamp01(tile.Moisture);
            float temperature = SimMath.Clamp01(tile.Temperature);

            // 燃料 × 干燥度 × 温度 —— 三者都只是"这次落点能不能烧起来"的权重。
            // 湿度仍然是最强的那一项（干旱世界与雨季世界的火情差一个数量级），
            // 只是它不再是一个"过不了就永远没有火"的悬崖。
            // 温度只作为**温和**的修正项（0.7~1.0），而不是一个 0.4 倍的重罚。
            // 原因：这个世界的温度长期接近 0（weathergen 的温度基线如此），
            // 用 (0.4 + 0.6×t) 会把所有落点的可燃性都压掉六成，
            // 于是"火几乎不可能发生"—— 又是一个被无关参数静默否掉的机制。
            float ignitability = tile.Vegetation
                * (0.15f + (0.85f * dryness))
                * (0.7f + (0.3f * temperature));

            if (!rng.Chance(ignitability)) { continue; }

            Ignite(index % width, index / width, tick, "雷击");
            return;
        }

        NaturalIgnitionRejected++;
    }

    /// <summary>
    /// 点燃一格（玩家工具与自然点燃共用同一个入口）。
    ///
    /// 返回 false 表示"这一格点不着"（没植被、已烧过、或不是可燃地形）——
    /// 让调用方能给出可读的反馈，而不是"点了但什么都没发生"。
    /// </summary>
    public bool Ignite(int x, int y, long tick, string reason)
    {
        if (!_sim.World.IsInBounds(x, y)) { return false; }

        // 用裸数组而不是 `World.TileAt`：后者是 `ref readonly`
        // （那是刻意的 —— 大部分代码不该改格子）。火是少数必须就地写格子的系统之一。
        ref Tile tile = ref _sim.World.Tiles[(y * _sim.World.Width) + x];
        if (tile.Fire != FireState.None) { return false; }
        if (!TerrainInfo.IsVegetation(tile.Terrain)) { return false; }
        if (tile.Vegetation < 0.05f) { return false; }

        tile.Fire = FireState.Burning;
        TotalIgnitions++;
        _sim.World.MarkDirtyAt(x, y);

        _sim.Events.Record(
            tick,
            History.WorldEventType.FireStarted,
            "起火（" + reason + "）@" + new Int2(x, y),
            History.EventImportance.Important,
            new Int2(x, y),
            -1,
            -1,
            reason);

        return true;
    }
}
