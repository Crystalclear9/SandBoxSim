using System.Collections.Generic;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Agents;

/// <summary>
/// 个体之间的关系（M6）。
///
/// # 为什么关系必须是对称的
///
/// 任务书第 55 条的要求是 <c>Rel(A,B) == Rel(B,A)</c>。这不只是"看起来更自然"：
/// 如果允许不对称，就会出现"A 认为他们是朋友、B 认为他们是敌人"这种状态，
/// 而它**没有单一的真值** —— 任何读取关系的地方（分享、结仇、结伴迁移）
/// 都要额外回答"以谁的视角为准"。久而久之每个调用点都会给出不同的答案，
/// 于是"关系"这个机制在系统之间行为不一致，且无法被测试锁定。
///
/// 实现方式：把 <c>(min(slotA,slotB), max(slotA,slotB))</c> 打包成一个 long 作为键。
/// 于是"写入 A→B"与"读取 B→A"必然命中同一个条目 —— 对称性由**数据结构**保证，
/// 而不是靠调用方自觉。
///
/// # 为什么是稀疏的 Dictionary 而不是二维数组
///
/// N 个个体两两配对是 O(N²)。100 人就是 4950 对、300 人接近 45000 对 ——
/// 而实际上每个人真正交互过的只有几个人。稀疏存储让内存与**遍历成本**
/// 都只与"真实发生过的关系"成正比。
///
/// # 确定性约束（这一条最容易踩坑）
///
/// `Dictionary` 的遍历顺序**不保证稳定**。因此：
///   * 任何影响行为的遍历都必须先按 key 排序（见 <see cref="PairsAscending"/>）；
///   * 存档与状态摘要同样按 key 升序写出。
///
/// 换句话说：**字典只用来查，不用来枚举。** 需要枚举时先排序。
/// </summary>
public sealed class RelationshipStore : ISimEntitySet
{
    /// <summary>关系变化的量化刻度（亲和度按此取整存储）。</summary>
    //
    // 为什么存定点而不是 float：关系会被频繁地 +0.01 / -0.02 微调，
    // 浮点累加顺序一旦不同（例如字典枚举顺序不同）就会产生末位差异，
    // 而关系**进状态摘要** ⇒ 整个世界的确定性就被破坏了。
    // 量化到 1/1000 之后，"先加后减"与"先减后加"给出完全相同的结果。
    private const float Quantum = 1000f;

    private readonly System.Collections.Generic.Dictionary<long, Entry> _relations = new();

    /// <summary>关系随时间的自然回落速度（每天，趋向 0）。</summary>
    private readonly RelationshipConfig _config;

    public RelationshipStore(RelationshipConfig config)
    {
        _config = config ?? throw new System.ArgumentNullException(nameof(config));
    }

    /// <summary>当前记录的关系条目数（观测与测试用）。</summary>
    public int Count => _relations.Count;

    /// <summary>`ISimEntitySet` 要求：实体数量为 0 时摘要可以跳过整段。</summary>
    public int EntityCount => _relations.Count;

    /// <summary>`ISimEntitySet` 要求：重置世界时清空。</summary>
    public void Reset() => _relations.Clear();

    /// <summary>累计发生的互动次数（观测用）。</summary>
    public long TotalInteractions { get; private set; }

    private struct Entry
    {
        public int Affinity;          // 量化后的亲和度：-1000..1000
        public int Interactions;      // 互动次数
        public long LastTick;         // 最近一次互动时刻
        public RelationshipKind Kind; // 关系类型（由亲和度与互动史推导并缓存）
    }

    /// <summary>把两个槽位打包成规范键（小的在前）—— 对称性由此保证。</summary>
    private static long KeyOf(int slotA, int slotB)
    {
        int low = slotA < slotB ? slotA : slotB;
        int high = slotA < slotB ? slotB : slotA;
        return ((long)low << 32) | (uint)high;
    }

    public static int LowOf(long key) => (int)(key >> 32);
    public static int HighOf(long key) => (int)(key & 0xFFFFFFFFL);

    /// <summary>读取亲和度（[-1,1]）。没有记录时返回 0（陌生人）。</summary>
    public float AffinityOf(int slotA, int slotB)
    {
        if (slotA == slotB) { return 1f; }
        return _relations.TryGetValue(KeyOf(slotA, slotB), out Entry entry)
            ? entry.Affinity / Quantum
            : 0f;
    }

    /// <summary>互动次数。</summary>
    public int InteractionsOf(int slotA, int slotB)
        => _relations.TryGetValue(KeyOf(slotA, slotB), out Entry entry) ? entry.Interactions : 0;

    public long LastInteractionTickOf(int slotA, int slotB)
        => _relations.TryGetValue(KeyOf(slotA, slotB), out Entry entry) ? entry.LastTick : -1;

    public RelationshipKind KindOf(int slotA, int slotB)
        => _relations.TryGetValue(KeyOf(slotA, slotB), out Entry entry)
            ? entry.Kind
            : RelationshipKind.Stranger;

    /// <summary>
    /// 调整亲和度（<paramref name="delta"/> 可正可负），并记录一次互动。
    ///
    /// 这是关系的**唯一写入口**：所有互动（社交、分享、攻击）都走它。
    /// 一个写入口的好处是"关系永远是对称的、永远是量化的、永远会更新
    /// 互动次数与时刻"这三件事只需要在一处保证。
    /// </summary>
    public void Interact(int slotA, int slotB, float delta, long tick)
    {
        if (slotA == slotB) { return; }
        if (slotA < 0 || slotB < 0) { return; }

        long key = KeyOf(slotA, slotB);
        _relations.TryGetValue(key, out Entry entry);

        // 量化：先换算成整数刻度再相加，避免浮点累加顺序带来的末位差异。
        int quantized = (int)System.Math.Round(delta * Quantum);
        int next = entry.Affinity + quantized;
        if (next > (int)Quantum) { next = (int)Quantum; }
        if (next < -(int)Quantum) { next = -(int)Quantum; }

        entry.Affinity = next;
        entry.Interactions++;
        entry.LastTick = tick;
        entry.Kind = Classify(entry.Affinity / Quantum, entry.Interactions);

        _relations[key] = entry;
        TotalInteractions++;
    }

    /// <summary>由亲和度与互动次数推导关系类型。</summary>
    private static RelationshipKind Classify(float affinity, int interactions)
    {
        if (interactions == 0) { return RelationshipKind.Stranger; }
        if (affinity >= 0.6f) { return RelationshipKind.Close; }
        if (affinity >= 0.25f) { return RelationshipKind.Friend; }
        if (affinity <= -0.6f) { return RelationshipKind.Enemy; }
        if (affinity <= -0.25f) { return RelationshipKind.Dislike; }
        return RelationshipKind.Acquaintance;
    }

    /// <summary>
    /// 怨恨：饿着的人会怨恨旁边有粮却不分的人（M6）。
    ///
    /// # 为什么必须有一个"不依赖攻击"的敌意来源
    ///
    /// `Attack` 与 `Flee` 都以"亲和度低于敌对阈值"为门。
    /// 但亲和度初始为 0（陌生人），而**唯一让它变负的机制就是攻击本身** ——
    /// 于是形成死循环：没有敌意 ⇒ 没人攻击 ⇒ 没有敌意。
    /// 这两个动作会**永远不被选中**，而症状只是一个安静的 0
    /// （与 M4 的 `BuildFarm`、M5 的自然点燃完全同类）。
    ///
    /// 所以必须有一个不依赖攻击的敌意来源。这里选最自然的一个：
    /// **不平等产生怨恨**。一个饥饿的人如果身边有人带着余粮却没分给他，
    /// 他对那个人的亲和度就会下降。久而久之，资源稀缺的世界自己长出仇敌。
    ///
    /// 这让整条链可达且**涌现**：
    /// ```text
    /// 食物稀缺 → 有人挨饿 + 有人有余粮 → 怨恨 → 敌意 → 攻击 → 关系恶化 → 逃跑/结仇
    /// ```
    /// 玩家不会"制造仇恨"，他只会改变食物条件 —— 仇恨是条件的结果。
    /// </summary>
    public void TickResentment(
        AgentStore store, float hungerThreshold, float surplusThreshold,
        float resentment, float radius, long tick)
    {
        if (resentment <= 0f) { return; }

        int[] slots = store.LiveSlotsRaw(out int liveCount);
        if (liveCount < 2) { return; }

        float radiusSq = radius * radius;

        // 按槽位升序两两检查：顺序固定 ⇒ 确定性。
        for (int i = 0; i < liveCount; i++)
        {
            int hungry = slots[i];
            if (store.HungerOf(hungry) < hungerThreshold) { continue; }

            int hx = store.XOf(hungry);
            int hy = store.YOf(hungry);

            for (int j = 0; j < liveCount; j++)
            {
                if (i == j) { continue; }
                int holder = slots[j];

                // 只怨恨"明明有粮却没分"的人。
                float carried = store.InventoryOf(holder, ResourceKind.Food);
                if (carried < surplusThreshold) { continue; }

                // # 这里改过一次，原因很实际
                //
                // 第一版要求"对方自己不饿"（`holderHunger < hungerThreshold`）才算他的错。
                // 看起来更讲道理，但**前提活不下来**：需求系统每 tick 都在推进饥饿度，
                // 于是几十天之后**所有人都是饿的**，"有余粮且不饿的人"这个角色消失了，
                // 怨恨再也没有触发过（实测关系表恒为空 ⇒ 攻击永远不可达）。
                //
                // 现在改成**归咎程度**：对方越饿，越不算他的错，但**不是零**。
                // 这既更贴近"不平等产生怨恨"的本意（重要的是别人有、我没有，
                // 而不是别人此刻饿不饿），也让这条机制在长期运行里始终有效。
                float blame = 1f - SimMath.Clamp01(store.HungerOf(holder));
                if (blame <= 0.1f) { continue; }

                float dx = store.XOf(holder) - hx;
                float dy = store.YOf(holder) - hy;
                if ((dx * dx) + (dy * dy) > radiusSq) { continue; }

                // 怨恨的强度还取决于**挨饿有多严重**：
                // 一个快饿死的人的怨恨，比一个只是有点饿的人强得多。
                //
                // 这不只是"让数字变大"：实测中发现了一个真实的动力学竞争 ——
                // 怨恨需要若干天才能把亲和度压到敌对阈值（-0.2）以下，
                // 而在这个贫瘠的测试世界里，人**饿死得更快**；
                // 一旦有人死，`Forget` 会把他的关系全部清掉，怨恨从头开始算。
                // 结果是"机制跑过了，但永远来不及产生影响"。
                //
                // 让怨恨随饥饿程度加速，既符合直觉，也让这条链在时间上真的走得通。
                float severity = SimMath.Clamp01((store.HungerOf(hungry) - hungerThreshold)
                    / System.Math.Max(0.05f, 1f - hungerThreshold));
                Interact(hungry, holder, -resentment * blame * (0.35f + (0.65f * severity)), tick);
            }
        }
    }

    /// <summary>
    /// 逐日向 0 回落。
    ///
    /// 为什么必须有：没有回落的话，关系只会单调累积 ——
    /// 一起住久了的人永远是"挚友"，仇人永远是"死敌"，
    /// 而"长期不来往就变淡"是关系最直观的性质。
    /// 回落同时也是**有界性**的保证：亲和度不会因为反复微调而漂到荒谬的值。
    /// </summary>
    public void TickDay(long tick)
    {
        float decay = _config.DecayPerDay;
        if (decay <= 0f || _relations.Count == 0) { return; }

        int step = (int)System.Math.Round(decay * Quantum);
        if (step < 1) { step = 1; }

        // 注意：这里**就地修改**而不重建字典 —— 但修改过程中不新增/删除键，
        // 因此不存在"遍历时修改集合"的问题，也不依赖遍历顺序（每个条目独立处理）。
        foreach (long key in KeysSnapshot())
        {
            Entry entry = _relations[key];
            if (entry.Affinity == 0) { continue; }

            int magnitude = entry.Affinity < 0 ? -entry.Affinity : entry.Affinity;
            int reduce = magnitude < step ? magnitude : step;
            entry.Affinity += entry.Affinity < 0 ? reduce : -reduce;
            entry.Kind = Classify(entry.Affinity / Quantum, entry.Interactions);
            _relations[key] = entry;
        }
    }

    /// <summary>
    /// 忘记与某个槽位有关的一切（该个体死亡时调用）。
    ///
    /// 不清理会有两个后果：条目无限增长；以及**槽位被复用时**
    /// 新个体凭空继承前一个人的关系（"我刚出生就有一个死敌"）。
    /// 后者是那种"看起来只是数据没清干净、实际改变行为"的典型问题。
    /// </summary>
    public void Forget(int slot)
    {
        if (_relations.Count == 0) { return; }

        var remove = new System.Collections.Generic.List<long>(4);
        foreach (var pair in _relations)
        {
            if (LowOf(pair.Key) == slot || HighOf(pair.Key) == slot) { remove.Add(pair.Key); }
        }
        for (int i = 0; i < remove.Count; i++) { _relations.Remove(remove[i]); }
    }

    /// <summary>清空（读档前的复位）。</summary>
    public void Clear() => _relations.Clear();

    /// <summary>
    /// 按 key **升序**返回全部关系对。
    ///
    /// 所有需要"枚举关系"的地方（影响行为的查询、存档、摘要）都必须走这里，
    /// 不能直接用 `_relations` 枚举 —— 字典的枚举顺序不保证稳定。
    /// </summary>
    public System.Collections.Generic.List<KeyValuePair<long, (float Affinity, int Interactions, long LastTick, RelationshipKind Kind)>>
        PairsAscending()
    {
        var keys = KeysSnapshot();
        System.Array.Sort(keys);

        var result = new List<KeyValuePair<long, (float, int, long, RelationshipKind)>>(keys.Length);
        for (int i = 0; i < keys.Length; i++)
        {
            Entry entry = _relations[keys[i]];
            result.Add(new KeyValuePair<long, (float, int, long, RelationshipKind)>(
                keys[i],
                (entry.Affinity / Quantum, entry.Interactions, entry.LastTick, entry.Kind)));
        }
        return result;
    }

    /// <summary>
    /// 读取档：直接写入量化值（不走 <see cref="Interact"/>，避免"恢复"被当成一次互动）。
    /// </summary>
    public void Restore(int slotA, int slotB, float affinity, int interactions, long lastTick)
    {
        if (slotA == slotB || slotA < 0 || slotB < 0) { return; }

        int quantized = (int)System.Math.Round(SimMath.Clamp(affinity, -1f, 1f) * Quantum);
        _relations[KeyOf(slotA, slotB)] = new Entry
        {
            Affinity = quantized,
            Interactions = interactions,
            LastTick = lastTick,
            Kind = Classify(quantized / Quantum, interactions),
        };
    }

    /// <summary>
    /// 状态摘要。
    ///
    /// 只哈希**会影响未来行为**的字段：亲和度与互动次数。
    /// `LastTick` 不进摘要 —— 它目前只用于报告与 UI 展示，不参与任何判定
    /// （如果将来有"多久没来往了"这类逻辑用到它，就必须加进来）。
    /// 判据始终是那一句：**它会不会影响未来的行为**。
    /// </summary>
    public ulong HashInto(ulong hash)
    {
        var pairs = PairsAscending();
        hash = Hash64.Combine(hash, pairs.Count);
        for (int i = 0; i < pairs.Count; i++)
        {
            hash = Hash64.Combine(hash, pairs[i].Key);
            hash = Hash64.Combine(hash, (int)System.Math.Round(pairs[i].Value.Affinity * Quantum));
            hash = Hash64.Combine(hash, pairs[i].Value.Interactions);
        }
        return hash;
    }

    private long[] KeysSnapshot()
    {
        var keys = new long[_relations.Count];
        int i = 0;
        foreach (long key in _relations.Keys) { keys[i++] = key; }
        return keys;
    }
}

/// <summary>关系类型（由亲和度与互动史推导，不单独存储为可写状态）。</summary>
public enum RelationshipKind : byte
{
    Stranger = 0,
    Acquaintance = 1,
    Friend = 2,
    Close = 3,
    Dislike = 4,
    Enemy = 5,
}

/// <summary>关系系统的行为参数（M6）。</summary>
public sealed class RelationshipConfig
{
    /// <summary>关系每天向 0 回落的幅度（"长期不来往就变淡"）。</summary>
    public float DecayPerDay = 0.01f;

    /// <summary>一次社交对亲和度的提升。</summary>
    public float SocializeGain = 0.06f;

    /// <summary>一次分享食物对亲和度的提升（比社交更"重"，因为它有实际代价）。</summary>
    public float ShareFoodGain = 0.12f;

    /// <summary>一次攻击对亲和度的降低。</summary>
    public float AttackLoss = 0.35f;

    /// <summary>亲近到多少才愿意分享食物（低于它只会在"顺手"时分享）。</summary>
    public float ShareAffinityThreshold = 0.15f;

    /// <summary>亲近到多少算"家人级"，分享与保护会优先照顾。</summary>
    public float KinAffinityThreshold = 0.5f;

    /// <summary>陌生到什么程度会考虑攻击。</summary>
    public float HostileAffinityThreshold = -0.2f;

    /// <summary>一次分享给多少食物。</summary>
    public float ShareFoodAmount = 6f;

    /// <summary>社交的影响半径（格）。</summary>
    public float SocializeRadius = 6f;

    /// <summary>
    /// 攻击的作用半径（格）。
    ///
    /// **必须与 `SocializeRadius` / `ResentmentRadius` 是同一量级。**
    /// 实测踩过一次：怨恨在 10 格内累积、社交在 6 格内发生，
    /// 而攻击的门却写死在 4 格 —— 于是"关系已经坏透了，但两个人从没同时靠近到 4 格内"，
    /// 攻击恒为 0 次。**三个半径不一致时，最短的那个会成为整条链的隐形瓶颈。**
    /// </summary>
    public float AttackRange = 6f;

    /// <summary>一次攻击造成多少健康损失。</summary>
    public float AttackDamage = 0.12f;

    /// <summary>攻击者是否也受伤（"打赢了也要掉血"）—— 让暴力有代价。</summary>
    public bool AttackRaisesAggression = true;

    // ---- 怨恨（M6 的"负面关系来源"）----

    /// <summary>饿到什么程度开始怨恨旁边有粮的人。</summary>
    public float ResentmentHungerThreshold = 0.45f;

    /// <summary>对方带多少食物才算"有余粮"。</summary>
    public float ResentmentSurplusThreshold = 15f;

    /// <summary>每天因怨恨降低多少亲和度（累积到敌对阈值之下就会出事）。</summary>
    public float ResentmentPerDay = 0.12f;

    /// <summary>怨恨的作用半径（格）。</summary>
    public float ResentmentRadius = 10f;


}
