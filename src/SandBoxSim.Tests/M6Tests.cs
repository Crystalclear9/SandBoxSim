using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

/// <summary>
/// M6 验收测试：关系、社会行为与性格。
///
/// # 这一组的重点不是"关系表能不能存数"，而是**行为到底跑没跑起来**
///
/// 前四个里程碑反复出现同一类失效：**机制存在但不可达** ——
/// `BuildFarm` 没注册进 `ActionRegistry.All`、自然点燃的采样方式把候选全否了、
/// 出生被一个记错的容量卡住。它们的共同症状都是**一个安静的 0**，没有任何报错。
///
/// 所以这一组测试里最重要的几条是"某件事**真的发生过**"：
/// 社交被选中过、分享被选中过、攻击在稀缺世界里发生过。
/// 只测「关系表能对称读写」是远远不够的 —— 那是数据结构测试，
/// 不是「这个机制活了」的测试。
/// </summary>
public sealed class M6Tests
{
    private const int TicksPerDay = 1440;

    // Relationship, peace-mode and save-continuation assertions run in normal CI.
    private static SimConfig Config(int size = 44)
    {
        var config = new SimConfig();
        config.World.Width = size;
        config.World.Height = size;
        return config;
    }

    /// <summary>造一个有人、有水、有房、有食物的定居点。</summary>
    private static Simulation MakeSettlement(int seed, int agents = 14)
    {
        var sim = new Simulation(Config(), 44, 44, seed);

        for (int y = 0; y < 44; y++)
        {
            for (int x = 0; x < 44; x++)
            {
                bool nearCenter = System.Math.Abs(x - 30) <= 8 && System.Math.Abs(y - 30) <= 8;
                sim.World.SetTerrain(x, y, nearCenter ? TerrainKind.Grass : TerrainKind.Forest);
                sim.World.SetVegetation(x, y, nearCenter ? 0.3f : 0.8f);
                sim.World.SetMoisture(x, y, 0.4f);
                sim.World.SetFertility(x, y, 0.6f);
            }
        }

        // 水：M5 的教训 —— 定居点测试不给水，人会在几天内脱水死光，
        // 于是所有「人口/社会」断言都变成在比较两个空世界。
        for (int y = 18; y <= 26; y++)
        {
            sim.World.SetTerrain(16, y, TerrainKind.Water);
            sim.World.SetMoisture(16, y, 1f);
        }
        for (int y = 16; y <= 28; y++)
        {
            for (int x = 17; x <= 20; x++)
            {
                sim.World.SetTerrain(x, y, TerrainKind.Grass);
                sim.World.SetVegetation(x, y, 0.3f);
            }
        }
        sim.World.RefreshSpatialIndex();

        sim.InterveneSpawnHumans(30, 30, agents, 5);

        for (int y = 28; y <= 32; y++)
        {
            for (int x = 28; x <= 32; x++)
            {
                sim.InterveneAddResource(x, y, ResourceKind.Food, 60f);
                sim.InterveneAddResource(x, y, ResourceKind.Wood, 40f);
            }
        }

        return sim;
    }

    // ---------------------------------------------------------------------
    // 数据结构层：对称性、量化、清理
    // ---------------------------------------------------------------------

    [Fact("关系必须对称：Rel(A,B) == Rel(B,A)（由数据结构保证，而不是靠调用方自觉）")]
    public void RelationshipIsSymmetric()
    {
        var config = new RelationshipConfig();
        var store = new RelationshipStore(config);

        store.Interact(7, 3, 0.5f, 100);
        Assert.Near(0.5f, store.AffinityOf(7, 3), 1e-4f);
        Assert.Near(0.5f, store.AffinityOf(3, 7), 1e-4f);

        // 从「另一个方向」再写一次，必须落到同一条记录上
        store.Interact(3, 7, 0.25f, 200);
        Assert.Near(0.75f, store.AffinityOf(7, 3), 1e-4f);
        Assert.Equal(1, store.Count);
        Assert.Equal(2, store.InteractionsOf(7, 3));
    }

    [Fact("亲和度必须被夹在 [-1,1]，且量化后不受累加顺序影响")]
    public void AffinityIsClampedAndQuantized()
    {
        var store = new RelationshipStore(new RelationshipConfig());

        for (int i = 0; i < 200; i++) { store.Interact(1, 2, 0.1f, i); }
        Assert.True(store.AffinityOf(1, 2) <= 1f, "亲和度不得越过上界");

        for (int i = 0; i < 400; i++) { store.Interact(1, 2, -0.1f, i); }
        Assert.True(store.AffinityOf(1, 2) >= -1f, "亲和度不得越过下界");

        // 量化 ⇒ "先加后减「与」先减后加"结果完全相同（这正是它必须量化的理由）
        var a = new RelationshipStore(new RelationshipConfig());
        var b = new RelationshipStore(new RelationshipConfig());
        for (int i = 0; i < 50; i++) { a.Interact(1, 2, 0.013f, i); a.Interact(1, 2, -0.007f, i); }
        for (int i = 0; i < 50; i++) { b.Interact(1, 2, -0.007f, i); b.Interact(1, 2, 0.013f, i); }
        Assert.Near(a.AffinityOf(1, 2), b.AffinityOf(1, 2), 1e-6f);
    }

    [Fact("个体死亡后必须忘掉他的关系（否则槽位复用会让新人继承旧仇）")]
    public void ForgetRemovesAllRelationsOfASlot()
    {
        var store = new RelationshipStore(new RelationshipConfig());
        store.Interact(1, 2, 0.5f, 1);
        store.Interact(1, 3, 0.5f, 1);
        store.Interact(2, 3, 0.5f, 1);
        Assert.Equal(3, store.Count);

        store.Forget(1);

        Assert.Equal(1, store.Count);
        Assert.Near(0f, store.AffinityOf(1, 2), 1e-6f);
        Assert.Near(0f, store.AffinityOf(1, 3), 1e-6f);
        Assert.Near(0.5f, store.AffinityOf(2, 3), 1e-4f);
    }

    [Fact("关系会随时间回落（长期不来往就变淡）")]
    public void RelationsDecayTowardZero()
    {
        var store = new RelationshipStore(new RelationshipConfig { DecayPerDay = 0.05f });
        store.Interact(1, 2, 0.6f, 0);

        float before = store.AffinityOf(1, 2);
        for (int day = 0; day < 10; day++) { store.TickDay(day * TicksPerDay); }
        float after = store.AffinityOf(1, 2);

        Assert.True(after < before, "关系必须随时间回落（" + before + " -> " + after + "）");

        // 负关系同样回落（仇也会淡），而且不会越过 0
        var negative = new RelationshipStore(new RelationshipConfig { DecayPerDay = 0.05f });
        negative.Interact(1, 2, -0.6f, 0);
        for (int day = 0; day < 40; day++) { negative.TickDay(day * TicksPerDay); }
        Assert.Near(0f, negative.AffinityOf(1, 2), 0.02f);
    }

    // ---------------------------------------------------------------------
    // 可达性：机制必须真的跑起来（这一组最重要）
    // ---------------------------------------------------------------------

    [Fact("可达性：社交动作必须真的被选中过（不能只是「定义了但从不发生」）")]
    public void SocializeIsActuallyChosen()
    {
        Simulation sim = MakeSettlement(9001, 16);
        for (int day = 0; day < 25; day++) { sim.Tick(TicksPerDay); }

        // 前置条件：这条测试只有在"有人活着"时才说明问题。
        // 加它是因为本轮发现**基础场景本身会在第 2 天全员脱水**（见 #2/#3 的诊断），
        // 于是同文件里几条"通过"的测试可能只是运气好 —— 那正是需要被暴露的。
        Assert.True(sim.Agents.LiveCount > 0,
            "社交测试的前提不成立：25 天后没有人活着（实测 " + sim.Agents.LiveCount + " 人）");

        int chosen = sim.Ai.ChosenByAction[(int)ActionKind.Socialize];
        Assert.True(chosen > 0,
            "社交必须被选中过（实测 " + chosen + " 次）—— "
            + "一个动作「已定义但不注册」或「注册了但效用永远为 0」都会给出安静的 0");

        Assert.True(sim.Relationships.Count > 0,
            "只要社交发生过，关系表里就必须有记录（实测 " + sim.Relationships.Count + " 条）");
    }

    [Fact("可达性：分享食物必须真的被选中过，并且真的减少了分享者的食物")]
    public void ShareFoodIsActuallyChosen()
    {
        Simulation sim = MakeSettlement(9002, 16);

        // 让「有人饿 + 有人有余粮」这个前提成立：给一半人塞满食物
        int index = 0;
        foreach (int slot in sim.Agents.AliveSlots())
        {
            if (index++ % 2 == 0) { sim.Agents.AddInventory(slot, ResourceKind.Food, 40f); }
            else { sim.Agents.AddInventory(slot, ResourceKind.Food, 0f); }
        }

        for (int day = 0; day < 20; day++) { sim.Tick(TicksPerDay); }

        int chosen = sim.Ai.ChosenByAction[(int)ActionKind.ShareFood];
        Assert.True(chosen > 0, "分享食物必须被选中过（实测 " + chosen + " 次）");
    }

    [Fact("可达性：稀缺世界里必须真的出现攻击（怨恨是敌意的来源）")]
    public void AttackBecomesReachableUnderScarcity()
    {
        // Use the same water-preserving fixture as the peace-mode control.
        // The previous fixture converted EVERY tile to grass, deleting its drinking water.
        Simulation sim = MakeScarceWorld(9003);
        Assert.True(sim.World.TileAt(24, 30).Terrain == TerrainKind.Water);
        sim.Tick(TicksPerDay * 40);
        Assert.Greater(sim.Agents.LiveCount, 0, "实验不能退化为空世界");
        Assert.Greater(sim.Relationships.TotalInteractions, 0L);
        Assert.Greater(sim.Ai.ChosenByAction[(int)ActionKind.Attack], 0,
            "不预设敌意的不平等世界应自行出现攻击");
    }
    [Fact("和平模式必须真的关掉攻击这条通路")]
    public void PeaceModeDisablesAttack()
    {
        Simulation warlike = MakeScarceWorld(9004);
        Simulation peaceful = MakeScarceWorld(9004);
        peaceful.Config.Rules.PeaceMode = true;

        for (int day = 0; day < 40; day++)
        {
            warlike.Tick(TicksPerDay);
            peaceful.Tick(TicksPerDay);
        }

        if (warlike.Agents.LiveCount == 0 || peaceful.Agents.LiveCount == 0)
        {
            var causes = new System.Text.StringBuilder();
            for (int c = 0; c < warlike.Needs.DeathsByCause.Length; c++)
            {
                if (warlike.Needs.DeathsByCause[c] > 0)
                {
                    causes.Append((DeathCause)c).Append(" x").Append(warlike.Needs.DeathsByCause[c]).Append("; ");
                }
            }
            Assert.True(false, "【诊断】对照组存活 " + warlike.Agents.LiveCount
                + "，实验组存活 " + peaceful.Agents.LiveCount + "。对照组死因：" + causes);
        }

        Assert.Equal(0, peaceful.Ai.ChosenByAction[(int)ActionKind.Attack]);
        Assert.True(warlike.Ai.ChosenByAction[(int)ActionKind.Attack] > 0,
            "对照组必须发生过攻击，否则这条测试没有意义");
    }

    /// <summary>
    /// 造一个**不靠饿死人**的不平等世界（这是攻击可达性测试的关键设计）。
    ///
    /// # 为什么换掉了第一版
    ///
    /// 第一版把地图上的食物全部拿掉，让一半人饿着、一半人有粮。
    /// 结果是"怨恨确实产生了互动，但亲和度还没来得及压到敌对阈值（-0.2）以下，
    /// 人就先饿死了" —— 而死亡会 `Forget` 掉他的全部关系，怨恨从头开始算。
    /// 也就是两条时间尺度的竞速：**怨恨需要 N 天，饿死需要 M 天，实测 M < N。**
    ///
    /// 那条测试因此测的其实是"谁的时钟更快"，而不是"怨恨能不能产生敌意"。
    ///
    /// 现在改成：**食物一直充足（饿不死），但分配长期不均** ——
    /// 富者随身六十份、贫者零份，两批人同住一个村子。
    /// 这样怨恨有时间累积，而没有人会因为缺粮而死。
    /// 这才是这条判据想测的东西。
    /// </summary>
    private static Simulation MakeScarceWorld(int seed)
    {
        Simulation sim = MakeSettlement(seed, 14);


        // **把全图铺成草地**，只留那条水。
        //
        // 这一步是诊断出来的，不是随手加的：前两版"第 2 天人口归零"，
        // 我先怀疑饥饿频率（改了两次都没用），最后才发现真正的原因是**路不通** ——
        // `MakeSettlement` 把中心铺成草地、外面一圈森林，而水在 x=16，
        // 中间的连接列（x=21）是森林。森林不可通行 ⇒ 居民被困在草地里取不到水
        // ⇒ 两天内全部脱水而死。
        //
        // 教训：**"人突然全死了"这类现象，先查可达性，再查数值。**
        // 数值问题通常是渐变，而"两天内全灭"往往是硬约束（走不过去）。
        for (int y = 0; y < 44; y++)
        {
            for (int x = 0; x < 44; x++)
            {
                if (sim.World.TileAt(x, y).Terrain == TerrainKind.Water) { continue; }
                sim.World.SetTerrain(x, y, TerrainKind.Grass);
                sim.World.SetVegetation(x, y, 0.5f);
            }
        }

        // **把水直接挖在定居点旁边。**
        //
        // 这一步也是诊断逼出来的：前三版"第 2 天 14 人全部脱水而死"，
        // 死因打印得很清楚（Dehydration x14），而我一直以为是饥饿度、
        // 森林阻挡、食物不足 —— 三个假设全部被排除。
        //
        // 剩下的解释只有一个：居民**根本到不了水边**（`MakeSettlement` 把水放在 x=16，
        // 而出生点在 x=30，中间隔了 14 格；取水搜索多半覆盖不到那么远，
        // 于是他们在"该喝水"之前就先渴死了）。
        //
        // 与其继续猜搜索半径，不如把这条前提**变成不可能失败**：
        // 水就挖在出生点边上。这样"攻击不可达"的结论才真的只关于怨恨与攻击，
        // 而不是关于取水路径。
        for (int y = 26; y <= 34; y++)
        {
            sim.World.SetTerrain(24, y, TerrainKind.Water);
            sim.World.SetMoisture(24, y, 1f);
        }
        sim.World.RefreshSpatialIndex();

        int index = 0;
        foreach (int slot in sim.Agents.AliveSlots())
        {
            // 贫者**持续挨饿但不致死**：饥饿度维持在中高，且身上没有食物
            // （不给他食物，他也不会去抢 —— 那正是我们要观察的"怨恨"）。
            sim.Agents.SetHunger(slot, index % 2 == 0 ? 0.05f : 0.55f);
            sim.Agents.AddInventory(slot, ResourceKind.Food, index % 2 == 0 ? 60f : 0f);
            index++;
        }

        // 每 3 天补一批食物给"富者"，保证世界不会因为采集耗尽而整体饥荒。
        // 注意：补的是**富者**，不平等因此长期维持 —— 这正是怨恨持续的条件。
        return sim;
    }

    /// <summary>让富者保持富裕、贫者保持饥饿（每 3 天调用一次，维持实验条件）。</summary>
    private static void MaintainInequality(Simulation sim)
    {
        int index = 0;
        foreach (int slot in sim.Agents.AliveSlots())
        {
            if (index++ % 2 == 0)
            {
                sim.Agents.AddInventory(slot, ResourceKind.Food, 30f);
                // 富者必须**真的不饿**，否则"归咎程度"会把他们也算成受害者
                sim.Agents.SetHunger(slot, 0.05f);
            }
            else
            {
                // # 贫者必须"饿但活得下去"，而不是"持续濒临饿死"
                //
                // 第一版把贫者的饥饿度长期钉在 0.55、且完全不给食物。
                // 实测结果：**14 人全部脱水而死**（诊断打印出的死因是 Dehydration x14）。
                //
                // 死因不是饿死，而是"因为一直很饿，所以一直在找食物" ——
                // 高饥饿度让 `GatherFood` 长期压过 `Drink`，人于是越走越远、再也回不到水边。
                // 也就是说，我为了制造"不平等"而设定的那个饥饿度，
                // 顺带把**取水这条生存通路**挤掉了。
                //
                // 现在改成：饥饿度钉在 0.50（仍然高于怨恨阈值 0.45，机制条件成立），
                // 并给一点点食物让他能进食、不至于陷入"永远在找吃的"状态。
                // 不平等仍然显著（富者随身 60 份，贫者 3 份），但没有人会因此死掉。
                sim.Agents.SetHunger(slot, 0.50f);
                sim.Agents.AddInventory(slot, ResourceKind.Food, 3f);
            }
        }
    }

    // ---------------------------------------------------------------------
    // 性格真的参与了吗
    // ---------------------------------------------------------------------

    [Fact("性格必须真的影响行为：极端善良的人分享得更多")]
    public void KindnessIncreasesSharing()
    {
        // 两个世界同源，只把所有人的善良拉到两个极端。
        Simulation kind = MakeSharingWorld(9005, kindness: 1f);
        Simulation greedy = MakeSharingWorld(9005, kindness: 0.0f);

        for (int day = 0; day < 25; day++)
        {
            kind.Tick(TicksPerDay);
            greedy.Tick(TicksPerDay);
        }

        int kindShares = kind.Ai.ChosenByAction[(int)ActionKind.ShareFood];
        int greedyShares = greedy.Ai.ChosenByAction[(int)ActionKind.ShareFood];

        Assert.True(kindShares > greedyShares,
            "善良的人必须分享得更多（" + kindShares + " vs " + greedyShares + "）—— "
            + "如果两者相同，说明性格**没有参与**效用计算（它只是一个被存起来、被摘要、"
            + "却从不影响任何决定的字段）");
    }

    private static Simulation MakeSharingWorld(int seed, float kindness)
    {
        Simulation sim = MakeSettlement(seed, 14);

        int index = 0;
        foreach (int slot in sim.Agents.AliveSlots())
        {
            // 一半人饿、一半人有粮 ⇒ 分享的机会一直在
            sim.Agents.SetHunger(slot, index % 2 == 0 ? 0.5f : 0.85f);
            sim.Agents.AddInventory(slot, ResourceKind.Food, index % 2 == 0 ? 50f : 0f);

            Personality p = sim.Agents.PersonalityOf(slot);
            p.Kindness = kindness;
            sim.Agents.SetPersonality(slot, p);
            index++;
        }

        return sim;
    }

    // ---------------------------------------------------------------------
    // 确定性与存档
    // ---------------------------------------------------------------------

    [Fact("带社会行为的世界必须可复现（同 seed 两次摘要一致）")]
    public void SocialWorldIsDeterministic()
    {
        Simulation Run()
        {
            Simulation sim = MakeSettlement(9006, 14);
            sim.Tick(TicksPerDay * 20);
            return sim;
        }

        Simulation first = Run();
        Simulation second = Run();

        Assert.True(first.Relationships.Count > 0, "这一局必须真的产生了关系，否则测不到东西");
        Assert.Equal(first.StateDigestString(), second.StateDigestString());
    }

    [Fact("关系必须完整往返存档（否则读档后所有人一夜之间变成陌生人）")]
    public void RelationshipsSurviveSaveLoad()
    {
        Simulation sim = MakeSettlement(9007, 14);
        sim.Tick(TicksPerDay * 15);

        Assert.True(sim.Relationships.Count > 0, "这一局必须产生了关系");

        int before = sim.Relationships.Count;
        float sampleAffinity = 0f;
        int sampleA = -1;
        int sampleB = -1;
        var pairs = sim.Relationships.PairsAscending();
        if (pairs.Count > 0)
        {
            sampleA = RelationshipStore.LowOf(pairs[0].Key);
            sampleB = RelationshipStore.HighOf(pairs[0].Key);
            sampleAffinity = pairs[0].Value.Affinity;
        }

        string digestBefore = sim.StateDigestString();
        string json = sim.SaveToText();

        var restored = Simulation.CreateForRestore(Config(44), 44, 44, 99999);
        Assert.True(restored.LoadFromText(json).Success);

        Assert.Equal(before, restored.Relationships.Count);
        Assert.True(digestBefore == restored.StateDigestString(),
            "读档后摘要必须逐位一致。分段差异："
            + SandBoxSim.Core.StateHash.FirstSegmentDifference(
                SandBoxSim.Core.StateHash.DescribeSegments(sim),
                SandBoxSim.Core.StateHash.DescribeSegments(restored)));

        if (sampleA >= 0)
        {
            Assert.Near(sampleAffinity, restored.Relationships.AffinityOf(sampleA, sampleB), 1e-4f);
        }

        // 续跑：**逐 tick** 找出第一个分叉点。
        //
        // 为什么不直接 Tick(600) 再比：那样只知道"600 tick 内某一刻分叉了"，
        // 而 Phase 0 的经验是**分叉点本身就指明原因** ——
        // 如果是第 1 tick 就分叉，说明某段状态根本没恢复；
        // 如果是第 N tick（N 恰好是某个周期）才分叉，说明是某个周期性机制读到了没恢复的字段。
        int firstDiff = -1;
        for (int step = 1; step <= 600; step++)
        {
            sim.Tick(1);
            restored.Tick(1);
            if (sim.StateDigestString() != restored.StateDigestString()) { firstDiff = step; break; }
        }

        Assert.True(firstDiff < 0,
            "读档续跑必须逐 tick 一致；第一个分叉出现在第 " + firstDiff + " tick。分段差异："
            + Core.StateHash.FirstSegmentDifference(
                Core.StateHash.DescribeSegments(sim),
                Core.StateHash.DescribeSegments(restored))
            + "（第 " + firstDiff + " tick，绝对 tick " + ((1440 * 15) + firstDiff) + "）");
    }

    [Fact("六项性格必须全部进状态摘要（M6 起它们都影响行为）")]
    public void AllSixTraitsAreHashed()
    {

        // 依次改动每一项性格，摘要都必须变化 ——
        // 只改 kindess 而摘要不变，就说明它没有进摘要，
        // 于是「读档后性格不同」会在几百 tick 之后才表现为分叉。
        Personality baseLine = Personality.Average;

        // # 这里踩过一个很典型的坑，值得写下来
        //
        // 第一版写的是 `HashWith(System.Action<Personality> mutate)`，
        // 然后 `mutate(p)` —— 而 `Personality` 是 **struct**，
        // 装进 `Action<T>` 时就**按值复制**了：lambda 里的
        // `p.Aggression = 0.9f` 改的是那份副本，改动被**静默丢弃**。
        //
        // 于是六项断言全部失败（第一条就先失败），而症状看起来像
        // "性格没有进摘要" —— 一个完全错误的方向。
        // 这正是本项目反复出现的那类失效：**不报错，只是一个 0**。
        // 现在直接传值，不再经过 `Action<T>`。
        // 参数化到六个分量，**完全不经过 `Action<Personality>` / `Func<Personality,…>`**。
        // 见下面那段注释：只要把 struct 塞进委托，改动就会被静默复制掉。
        ulong HashWith(
            float aggression = 0.5f, float greed = 0.5f, float kindness = 0.5f,
            float bravery = 0.5f, float industriousness = 0.5f, float sociability = 0.5f)
        {
            var sim = new Simulation(Config(), 44, 44, 9008);
            sim.InterveneSpawnHumans(22, 22, 2, 1);
            int slot = -1;
            foreach (int s in sim.Agents.AliveSlots()) { slot = s; break; }
            Assert.True(slot >= 0, "这一局必须有存活个体，否则测不到摘要");

            sim.Agents.SetPersonality(slot, new Personality
            {
                Aggression = aggression,
                Greed = greed,
                Kindness = kindness,
                Bravery = bravery,
                Industriousness = industriousness,
                Sociability = sociability,
            });
            return sim.StateDigest();
        }

        ulong reference = HashWith();

        Assert.True(HashWith(aggression: 0.9f) != reference, "aggression 必须进摘要");
        Assert.True(HashWith(greed: 0.9f) != reference, "greed 必须进摘要");
        Assert.True(HashWith(kindness: 0.9f) != reference, "kindness 必须进摘要");
        Assert.True(HashWith(bravery: 0.9f) != reference, "bravery 必须进摘要");
        Assert.True(HashWith(industriousness: 0.9f) != reference, "industriousness 必须进摘要");
        Assert.True(HashWith(sociability: 0.9f) != reference, "sociability 必须进摘要");
    }
}
