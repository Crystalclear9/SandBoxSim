using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Systems;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

/// <summary>
/// M8 验收测试（第二批）：冲突压力。
///
/// # 这一组守住的判据
///
/// 任务书第 74 条：
/// ```text
/// WarPressure = 资源冲突 + 领土冲突 + 侵略性 + 历史敌意 − 贸易收益 − 关系
/// ```
/// 六项里前四项是"推「、后两项是」拉"。这组测试的核心不是"公式有没有照抄对"，
/// 而是**每一项的方向都对** —— 尤其是两个「拉」项：
/// 一个把贸易收益写成加号的实现，会让"越富越爱打"，而那正是这条公式要避免的。
///
/// # 为什么方向性断言在这里特别重要
///
/// 上一批（价格）已经演示过一次：一个「下标写错」的 bug 让价格与供给的关系**整体反向**，
/// 而所有「值是正数、也在上下限内」的断言都通过了。
/// **方向性断言是唯一能抓住「整体反向」这类 bug 的东西。**
/// </summary>
public sealed class M8ConflictTests
{
    private const int TicksPerDay = 1440;

    private static SimConfig Config(int size = 44)
    {
        var config = new SimConfig();
        config.World.Width = size;
        config.World.Height = size;
        return config;
    }

    /// <summary>造两个相邻的人，便于精确控制「它们之间」的压力输入。</summary>
    private static Simulation MakePair(int seed)
    {
        var sim = new Simulation(Config(), 44, 44, seed);
        for (int y = 0; y < 44; y++)
        {
            for (int x = 0; x < 44; x++)
            {
                sim.World.SetTerrain(x, y, TerrainKind.Grass);
                sim.World.SetVegetation(x, y, 0.3f);
            }
        }
        for (int y = 26; y <= 34; y++) { sim.World.SetTerrain(24, y, TerrainKind.Water); }
        sim.World.RefreshSpatialIndex();
        sim.InterveneSpawnHumans(30, 30, 2, 1);
        return sim;
    }

    private static void Pair(Simulation sim, out int a, out int b)
    {
        a = -1;
        b = -1;
        foreach (int slot in sim.Agents.AliveSlots())
        {
            if (a < 0) { a = slot; } else if (b < 0) { b = slot; }
        }
    }

    /// <summary>给两个人足够食物，把"资源冲突"固定为 0 —— 好让其它项的影响可见。</summary>
    private static void Feed(Simulation sim, int a, int b)
    {
        sim.Agents.AddInventory(a, ResourceKind.Food, 100f);
        sim.Agents.AddInventory(b, ResourceKind.Food, 100f);
    }

    [Fact("验收1：双方都缺食物时压力更高（资源冲突是「推」项）")]
    public void ResourceScarcityRaisesPressure()
    {
        Simulation fed = MakePair(14001);
        Pair(fed, out int fa, out int fb);
        fed.Agents.AddInventory(fa, ResourceKind.Food, 60f);
        fed.Agents.AddInventory(fb, ResourceKind.Food, 60f);
        float fedPressure = fed.Conflict.PressureOf(fa, fb);

        Simulation hungry = MakePair(14001);
        Pair(hungry, out int ha, out int hb);
        hungry.Agents.AddInventory(ha, ResourceKind.Food, 0f);
        hungry.Agents.AddInventory(hb, ResourceKind.Food, 0f);
        float hungryPressure = hungry.Conflict.PressureOf(ha, hb);

        Assert.True(hungryPressure > fedPressure,
            "双方都缺食物时冲突压力必须更高（" + hungryPressure.ToString("0.###")
            + " vs " + fedPressure.ToString("0.###") + "）—— "
            + "注意「双方都缺」才是资源冲突；一边富一边穷属于 M6 的「怨恨」");
    }

    [Fact("验收2：贸易收益是「拉」项 —— 两边都有余粮时压力必须更低，而不是更高")]
    public void TradeBenefitLowersPressure()
    {
        Simulation poor = MakePair(14002);
        Pair(poor, out int pa, out int pb);
        poor.Agents.SetHunger(pa, 0.9f);
        poor.Agents.SetHunger(pb, 0.9f);
        float poorPressure = poor.Conflict.PressureOf(pa, pb);

        Simulation rich = MakePair(14002);
        Pair(rich, out int ra, out int rb);
        rich.Agents.AddInventory(ra, ResourceKind.Food, 200f);
        rich.Agents.AddInventory(rb, ResourceKind.Food, 200f);
        float richPressure = rich.Conflict.PressureOf(ra, rb);

        // 这一条是本组最关键的断言：把 trade 写成加号的实现会让它失败
        Assert.True(richPressure <= poorPressure,
            "贸易收益必须**降低**压力（余粮丰富 " + richPressure.ToString("0.###")
            + " vs 匮乏 " + poorPressure.ToString("0.###") + "）—— "
            + "如果富的时候压力更高，说明贸易项被写成了加号");
    }

    [Fact("验收3：关系是「拉」项 —— 正亲和度必须降低压力")]
    public void RelationshipLowersPressure()
    {
        // 给足食物把"资源冲突"这一项固定为 0，否则压力会贴着上限、看不出关系的影响
        Simulation strangers = MakePair(14003);
        Pair(strangers, out int sa, out int sb);
        Feed(strangers, sa, sb);
        float strangerPressure = strangers.Conflict.PressureOf(sa, sb);

        Simulation friends = MakePair(14003);
        Pair(friends, out int xa, out int xb);
        Feed(friends, xa, xb);
        // 直接建立一段很好的关系
        friends.Relationships.Interact(xa, xb, 0.9f, 0);
        float friendPressure = friends.Conflict.PressureOf(xa, xb);

        Assert.True(friendPressure < strangerPressure,
            "关系好的两个人压力必须更低（" + friendPressure.ToString("0.###")
            + " vs " + strangerPressure.ToString("0.###") + "）");
    }

    [Fact("验收4：历史敌意是「推」项 —— 负亲和度必须抬高压力")]
    public void HistoricalHostilityRaisesPressure()
    {
        Simulation strangers = MakePair(14004);
        Pair(strangers, out int sa, out int sb);
        Feed(strangers, sa, sb);
        float strangerPressure = strangers.Conflict.PressureOf(sa, sb);

        Simulation enemies = MakePair(14004);
        Pair(enemies, out int ea, out int eb);
        Feed(enemies, ea, eb);
        enemies.Relationships.Interact(ea, eb, -0.9f, 0);
        float enemyPressure = enemies.Conflict.PressureOf(ea, eb);

        Assert.True(enemyPressure > strangerPressure,
            "有过节的两个人压力必须更高（" + enemyPressure.ToString("0.###")
            + " vs " + strangerPressure.ToString("0.###") + "）");
    }

    [Fact("验收5：领土冲突随距离衰减 —— 离得远的人打不起来")]
    public void TerritoryConflictDecaysWithDistance()
    {
        Simulation sim = MakePair(14005);
        Pair(sim, out int a, out int b);
        Feed(sim, a, b);

        float nearPressure = sim.Conflict.PressureOf(a, b);

        // 把 b 挪到很远的地方（远超 TerritoryRadius）
        sim.Agents.SetPosition(b, 2, 2);
        float farPressure = sim.Conflict.PressureOf(a, b);

        Assert.True(farPressure < nearPressure,
            "离得远的人领土冲突必须更低（" + farPressure.ToString("0.###")
            + " vs " + nearPressure.ToString("0.###") + "）");
    }

    [Fact("验收6：压力必须被夹在 [0, 上限] 内（六项叠加不能溢出）")]
    public void PressureIsBounded()
    {
        Simulation sim = MakePair(14006);
        Pair(sim, out int a, out int b);

        // 把所有「推」项拉满、所有「拉」项清零
        var p = sim.Agents.PersonalityOf(a);
        p.Aggression = 1f;
        sim.Agents.SetPersonality(a, p);
        p = sim.Agents.PersonalityOf(b);
        p.Aggression = 1f;
        sim.Agents.SetPersonality(b, p);
        sim.Relationships.Interact(a, b, -1f, 0);
        sim.Agents.AddInventory(a, ResourceKind.Food, 0f);
        sim.Agents.AddInventory(b, ResourceKind.Food, 0f);

        float pressure = sim.Conflict.PressureOf(a, b);
        Assert.True(SimMath.IsFinite(pressure), "压力必须是有限值");
        Assert.True(pressure >= 0f && pressure <= sim.Config.Conflict.MaxPressure + 1e-4f,
            "压力必须被夹在 [0, " + sim.Config.Conflict.MaxPressure + "]（实测 " + pressure + "）");
    }

    [Fact("验收7：压力是纯派生量 —— 不进摘要，读档后可一模一样重算")]
    public void PressureIsDerivedNotSaved()
    {
        Simulation sim = MakePair(14007);
        Pair(sim, out int a, out int b);
        sim.Relationships.Interact(a, b, -0.5f, 0);
        sim.Tick(TicksPerDay * 2);

        float before = sim.Conflict.PressureOf(a, b);
        string digestBefore = sim.StateDigestString();

        string json = sim.SaveToText();
        var restored = Simulation.CreateForRestore(Config(44), 44, 44, 99999);
        Assert.True(restored.LoadFromText(json).Success);

        Assert.Equal(digestBefore, restored.StateDigestString());

        Pair(restored, out int ra, out int rb);
        if (ra == a && rb == b)
        {
            Assert.Near(before, restored.Conflict.PressureOf(ra, rb), 1e-4f);
        }

        for (int step = 1; step <= 200; step++)
        {
            sim.Tick(1);
            restored.Tick(1);
            Assert.True(sim.StateDigestString() == restored.StateDigestString(),
                "读档续跑在第 " + step + " tick 分叉");
        }
    }
}
