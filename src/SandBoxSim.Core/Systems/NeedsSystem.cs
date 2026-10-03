using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Systems;

/// <summary>一次死亡记录（供上层写事件日志；避免上层为了"找到谁死了"而全表扫描）。</summary>
public readonly struct DeathRecord
{
    public readonly int Slot;
    public readonly int X;
    public readonly int Y;
    public readonly int AgeDays;
    public readonly DeathCause Cause;

    public DeathRecord(int slot, int x, int y, int ageDays, DeathCause cause)
    {
        Slot = slot;
        X = x;
        Y = y;
        AgeDays = ageDays;
        Cause = cause;
    }
}

/// <summary>
/// 需求系统（第 13 节）。
///
/// 职责：让"活着"本身需要经营。
///   * 每 tick 累积饥饿 / 疲劳 / 干渴 / 社交（全部归一化到 [0,1]）；
///   * 睡眠时反解疲劳；
///   * 长期饥饿与脱水开始掉血；
///   * 每天增长年龄，老年有死亡风险。
///
/// 它**不负责**决定个体去吃什么（那是 Utility AI 的事）。
/// 这样切分让"需求"成为纯粹的状态量：测试可以直接断言
/// "跑 N tick 后 hunger 必须上升"，不需要构造任何行为。
/// </summary>
public sealed class NeedsSystem
{
    private readonly SimConfig _config;

    /// <summary>本 tick 的死亡记录（用列表避免上层全表扫描 —— 这正是"看起来无害的 O(全容量)"的典型来源）。</summary>
    private readonly System.Collections.Generic.List<DeathRecord> _deaths = new System.Collections.Generic.List<DeathRecord>(8);

    /// <summary>本 tick 因饥饿/脱水/老年而死亡的人数（统计与报告用）。</summary>
    public int DeathsThisTick => _deaths.Count;

    /// <summary>本 tick 的死亡明细（只读；上层写事件日志与统计用）。</summary>
    public System.Collections.Generic.IReadOnlyList<DeathRecord> Deaths => _deaths;

    /// <summary>累计死亡分类计数：下标 = (int)DeathCause。</summary>
    public int[] DeathsByCause { get; } = new int[16];

    public NeedsSystem(SimConfig config)
    {
        _config = config ?? throw new System.ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// 每 tick 的需求推进。
    /// 注意顺序：先累积 → 再结算伤害 → 最后判定死亡。
    /// 反过来会出现"这一 tick 刚饿到极限就立刻死"的突兀感。
    /// </summary>
    /// <param name="store">个体存储。</param>
    /// <param name="tick">当前 tick。</param>
    /// <param name="ticksPerDay">一天多少 tick（用于把"每天速率"换算成"每 tick 增量"）。</param>
    /// <param name="isNight">是否夜晚（影响疲劳累积：夜间更困）。</param>
    public void TickNeeds(AgentStore store, long tick, int ticksPerDay, bool isNight)
    {
        _deaths.Clear();
        if (ticksPerDay <= 0) { ticksPerDay = 1440; }

        NeedsConfig needs = _config.Needs;
        float perTick = 1f / ticksPerDay;

        float hungerRate = needs.HungerPerDay * perTick;
        float thirstRate = needs.ThirstPerDay * perTick;
        float socialRate = needs.SocialPerDay * perTick;
        float fatigueRate = needs.FatiguePerDay * perTick * (isNight ? 1.25f : 1f);
        float sleepRecovery = needs.SleepRecoveryPerDay * perTick;
        float starvationDamage = needs.StarvationDamagePerDay * perTick;
        float dehydrationDamage = needs.DehydrationDamagePerDay * perTick;
        float healthRecovery = needs.HealthRecoveryPerDay * perTick;

        // 只遍历存活槽位（前缀数组）。原因同 ActionSystem：
        // 容量按峰值人口预留，人口回落后遍历空槽位是纯粹浪费。
        // 存活前缀数组的顺序始终是槽位升序，因此遍历顺序仍然确定。
        int[] slots = store.LiveSlotsRaw(out int liveCount);
        for (int k = 0; k < liveCount; k++)
        {
            int slot = slots[k];

            bool sleeping = store.ActionOf(slot) == ActionKind.Sleep
                            && store.PhaseOf(slot) == ActionPhase.Executing;

            // ---- 累积 ----
            store.AddNeed(slot, NeedIndex.Hunger, hungerRate);
            store.AddNeed(slot, NeedIndex.Thirst, thirstRate);
            store.AddNeed(slot, NeedIndex.Social, socialRate);

            if (sleeping)
            {
                store.AddNeed(slot, NeedIndex.Fatigue, -sleepRecovery);
            }
            else
            {
                store.AddNeed(slot, NeedIndex.Fatigue, fatigueRate);
            }

            // ---- 伤害/恢复结算 ----
            float hunger = store.HungerOf(slot);
            float thirst = store.ThirstOf(slot);
            float healthDelta = 0f;
            DeathCause lethalCause = DeathCause.None;

            if (hunger >= needs.StarvationDamageThreshold)
            {
                healthDelta -= starvationDamage;
                lethalCause = DeathCause.Starvation;
            }

            if (thirst >= needs.DehydrationDamageThreshold)
            {
                healthDelta -= dehydrationDamage;
                // 脱水优先作为死因：它致命更快，也更符合直觉
                if (lethalCause == DeathCause.None) { lethalCause = DeathCause.Dehydration; }
            }

            if (healthDelta < 0f)
            {
                store.AddHealth(slot, healthDelta);
            }
            else if (hunger < 0.5f && thirst < 0.5f && store.HealthOf(slot) < 1f)
            {
                // 需求都满足时才自然回血
                store.AddHealth(slot, healthRecovery);
            }

            // ---- 死亡判定 ----
            if (store.HealthOf(slot) <= 0f)
            {
                Kill(store, slot, lethalCause == DeathCause.None ? DeathCause.Injury : lethalCause, tick);
                continue;
            }

            // 生活阶段（年龄在 DailyTick 更新，这里只做派生）
            UpdateLifeStage(store, slot, needs);
        }
    }

    /// <summary>
    /// 每 tick 更新"生活阶段"。分开写是因为它会同时影响行为（M4 之后儿童不能工作）。
    /// </summary>
    private static void UpdateLifeStage(AgentStore store, int slot, NeedsConfig needs)
    {
        int age = store.AgeDaysOf(slot);
        LifeStage stage;
        if (age >= needs.ElderDays) { stage = LifeStage.Elder; }
        else if (age < needs.AdulthoodDays) { stage = LifeStage.Child; }
        else { stage = LifeStage.Adult; }

        if (store.LifeStageOf(slot) != stage) { store.SetLifeStage(slot, stage); }
    }

    /// <summary>
    /// 每日的年龄推进与老年死亡判定。
    ///
    /// 老年死亡率随年龄线性上升，并在 MaxLifespanDays 处变成必死 ——
    /// 这是"防止极端长寿个体堆积"的硬兜底：没有它，世界会逐渐被不死的老人占满，
    /// 人口结构失去流动性，也就没有故事可以看。
    /// </summary>
    public void TickAging(AgentStore store, DeterministicRandom rng, long tick)
    {
        NeedsConfig needs = _config.Needs;

        int[] slots = store.LiveSlotsRaw(out int liveCount);
        for (int k = 0; k < liveCount; k++)
        {
            int slot = slots[k];

            int age = store.AgeDaysOf(slot) + 1;
            store.SetAgeDays(slot, age);

            if (age >= needs.MaxLifespanDays)
            {
                Kill(store, slot, DeathCause.OldAge, tick);
                continue;
            }

            if (age >= needs.ElderDays)
            {
                // 死亡率随超出老年线的天数线性上升，最高到 1（必死）
                float overshoot = (age - needs.ElderDays) / (float)System.Math.Max(1, needs.MaxLifespanDays - needs.ElderDays);
                float chance = needs.ElderMortalityPerDay * (1f + (overshoot * 2f));
                if (rng.Chance(chance))
                {
                    Kill(store, slot, DeathCause.OldAge, tick);
                }
            }
        }
    }

    /// <summary>杀死个体并登记死因。所有死亡路径都必须经过这里（保证统计一致）。</summary>
    public void Kill(AgentStore store, int slot, DeathCause cause, long tick)
    {
        if (!store.IsSlotAlive(slot)) { return; }

        // 先取快照再 MarkDead：MarkDead 只改存活标志与代次，但把这些信息集中取一次更安全
        int x = store.XOf(slot);
        int y = store.YOf(slot);
        int age = store.AgeDaysOf(slot);

        store.MarkDead(slot, cause, tick);

        int index = (int)cause;
        if (index >= 0 && index < DeathsByCause.Length) { DeathsByCause[index]++; }
        _deaths.Add(new DeathRecord(slot, x, y, age, cause));
    }

    /// <summary>平均饥饿度（报告与热力图用）。</summary>
    public static float AverageHunger(AgentStore store)
    {
        if (store.LiveCount == 0) { return 0f; }
        return store.TotalHunger() / store.LiveCount;
    }

    /// <summary>平均疲劳度。</summary>
    public static float AverageFatigue(AgentStore store)
    {
        if (store.LiveCount == 0) { return 0f; }
        return store.TotalFatigue() / store.LiveCount;
    }

    public void ResetStatistics()
    {
        _deaths.Clear();
        for (int i = 0; i < DeathsByCause.Length; i++) { DeathsByCause[i] = 0; }
    }

    /// <summary>把死因转成中文/英文可读标签（事件日志用）。</summary>
    public static string DescribeCause(DeathCause cause)
    {
        switch (cause)
        {
            case DeathCause.Starvation: return "饥饿";
            case DeathCause.Dehydration: return "脱水";
            case DeathCause.OldAge: return "衰老";
            case DeathCause.Illness: return "疾病";
            case DeathCause.Injury: return "受伤";
            case DeathCause.Fire: return "火灾";
            case DeathCause.Combat: return "战斗";
            case DeathCause.Disaster: return "灾害";
            default: return "未知";
        }
    }
}
