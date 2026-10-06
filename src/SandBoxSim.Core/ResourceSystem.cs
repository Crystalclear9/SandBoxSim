using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Core;

/// <summary>
/// 资源系统（第 9 / 11 / 62 / 63 节）。
///
/// 职责边界：
///   * 只负责"资源存量如何随时间变化"（再生/消耗结算）；
///   * **不负责**决定谁去采集（那是 AI 与 Action 的事），也不负责库存（那是 Agent/Settlement 的事）。
///
/// 这样切分是有意为之：再生是环境过程（与谁在场无关），采集是决策过程。
/// 混在一起会导致"因为 AI 改动而破坏环境不变量"这类 bug。
/// </summary>
public sealed class ResourceSystem
{
    private readonly Environment.World _world;
    private readonly SimConfig _config;

    /// <summary>本小时全图再生的资源总量（统计/报告用）。</summary>
    public float LastHourRegenerated { get; private set; }

    /// <summary>累计再生总量。</summary>
    public double TotalRegenerated { get; private set; }

    /// <summary>累计被采集走的资源总量（含玩家工具注入，另行统计）。</summary>
    public double TotalHarvested { get; private set; }

    /// <summary>累计枯竭（存量降到 0）的格子次数 —— "过度采集"的直接证据。</summary>
    public long DepletionEvents { get; private set; }

    public ResourceSystem(SandBoxSim.Core.Environment.World world, SimConfig config)
    {
        _world = world;
        _config = config;
    }

    /// <summary>是否按小时再生（配置可切到按天）。</summary>
    public bool RegeneratesHourly => _config.Resources.RegenerateHourly;

    /// <summary>
    /// 再生一次。days 是本次再生覆盖的"游戏天数份额"（按小时调用时 = 1/24）。
    ///
    /// 公式（第 62 / 63 条）：
    ///   A ← clamp(A + r·A·(1 − A/K)·Δt, 0, K)
    /// 注意每格的再生率取自它自己的 ResourceNode：矿产为 0（不可再生），
    /// 木材与野生食物为正。这样"砍光森林"是真的不可逆的损伤，
    /// 而不是等一会儿就满血复活 —— 那会让资源压力完全失效。
    /// </summary>
    public void Regenerate(double days)
    {
        if (days <= 0.0) { return; }

        Tile[] tiles = _world.Tiles;
        float regenerated = 0f;
        bool changed = false;

        for (int i = 0; i < tiles.Length; i++)
        {
            ref Tile tile = ref tiles[i];
            ResourceNode node = tile.Resource;
            if (node.Kind == ResourceKind.None || node.Capacity <= 0f || node.RegenerationRate <= 0f)
            {
                continue;
            }

            float before = node.Amount;
            float after = ResourceNode.RegenerateLogistic(before, node.Capacity, node.RegenerationRate, days);
            if (after != before)
            {
                regenerated += after - before;
                tile.Resource.Amount = after;

                // 资源量变化会影响空间索引里缓存的 chunk 资源总量。
                changed = true;
                _world.MarkDirtyAt(i % _world.Width, i / _world.Width);
            }
        }

        LastHourRegenerated = regenerated;
        TotalRegenerated += regenerated;
        if (!changed)
        {
            LastHourRegenerated = 0f;
        }
    }

    /// <summary>
    /// 从一格采集资源。返回实际采到的数量（可能少于请求量，甚至为 0）。
    /// 这是**唯一**允许扣减 Tile 资源存量的方法（玩家工具除外），
    /// 以保证枯竭统计与空间索引维护不会被绕过。
    /// </summary>
    public float Harvest(int x, int y, ResourceKind kind, float wanted)
    {
        if (!_world.IsInBounds(x, y)) { return 0f; }
        int index = _world.IndexOf(x, y);
        ref Tile tile = ref _world.Tiles[index];

        if (tile.Resource.Kind != kind || tile.Resource.Amount <= 0f) { return 0f; }

        float before = tile.Resource.Amount;
        float taken = tile.Resource.Harvest(wanted);
        if (taken <= 0f) { return 0f; }

        TotalHarvested += taken;

        // 采伐森林会降低植被量：这是"森林退化"的数据来源，
        // 也是火灾风险下降（可燃物变少）与动物栖息地下降的输入（第 41 节链条）。
        if (kind == ResourceKind.Wood && tile.Terrain == TerrainKind.Forest)
        {
            float vegetationLoss = taken / System.Math.Max(1f, tile.Resource.Capacity);
            _world.SetVegetation(x, y, tile.Vegetation - vegetationLoss);
        }

        if (before > 0f && tile.Resource.Amount <= 0f)
        {
            DepletionEvents++;
        }

        _world.MarkDirtyAt(x, y);
        return taken;
    }

    /// <summary>Additional living-node growth, included in regeneration totals; never replenishes minerals.</summary>
    public float ReplenishLivingNode(int x, int y, ResourceKind kind, float amount)
    {
        if (kind is not (ResourceKind.Food or ResourceKind.Wood)) return 0;
        float grown = Inject(x, y, kind, amount);
        TotalRegenerated += grown;
        return grown;
    }

    /// <summary>
    /// 注入资源（玩家工具、事件奖励）。
    ///
    /// 规则（刻意写得保守，避免"注入"变成一种隐形的作弊通道）：
    ///   1. 目标格的资源种类不同时**不覆盖**，返回 0 —— 玩家想在山地上放食物，
    ///      得到的是"没反应"，而不是一个混在一起的、语义不明的格子；
    ///   2. 空资源的格子按地形创建默认节点（用与世界生成同一张表）；
    ///   3. 注入量以节点容量为上限，超出部分不生效。
    ///
    /// 返回值是**实际写入**的数量，调用方据此在事件里记录真实发生的事。
    /// </summary>
    public float Inject(int x, int y, ResourceKind kind, float amount)
    {
        if (!_world.IsInBounds(x, y) || amount <= 0f) { return 0f; }
        if (kind == ResourceKind.None) { return 0f; }

        int index = _world.IndexOf(x, y);
        ref Tile tile = ref _world.Tiles[index];

        if (tile.Resource.Kind != kind)
        {
            // 已经有别的东西（无论是石头还是木头），不覆盖。
            if (tile.Resource.Kind != ResourceKind.None) { return 0f; }

            _world.ApplyDefaultResource(x, y);
            tile = ref _world.Tiles[index];

            if (tile.Resource.Kind != kind)
            {
                // 地形本身不产这种资源（例如水域、农田）：不凭空造节点。
                return 0f;
            }
        }

        float before = tile.Resource.Amount;
        float room = tile.Resource.Capacity - before;
        if (room <= 0f) { return 0f; }

        float accepted = amount < room ? amount : room;
        tile.Resource.Amount = before + accepted;
        _world.MarkDirtyAt(x, y);
        return accepted;
    }

    /// <summary>
    /// 全图资源紧张度 [0,1]：1 表示所有资源都逼近枯竭。
    /// 这是"迁移效用"与"事件压力"的输入之一（第 54 / 65 条）。
    /// 注意它扫全图，只应在统计 tick（每小时）调用，不要放进每 tick 路径。
    /// </summary>
    public float GlobalScarcity()
    {
        float woodFraction = FractionOf(ResourceKind.Wood);
        float foodFraction = FractionOf(ResourceKind.Food);
        // 食物权重更高：先饿死，才轮到没木头盖房。
        float combined = (foodFraction * 0.6f) + (woodFraction * 0.4f);
        return SimMath.Clamp01(1f - combined);
    }

    /// <summary>某资源"当前存量 / 总容量"。</summary>
    public float FractionOf(ResourceKind kind)
    {
        float capacity = _world.TotalCapacity(kind);
        if (capacity <= 0f) { return 1f; }
        return SimMath.Clamp01(_world.TotalResource(kind) / capacity);
    }

    /// <summary>读档时恢复累计统计（它们不影响演化，但报告与"资源紧张度"曲线要用）。</summary>
    public void RestoreCounters(double totalHarvested, long depletionEvents)
    {
        TotalHarvested = totalHarvested;
        DepletionEvents = depletionEvents;
    }

    public void ResetStatistics()
    {
        LastHourRegenerated = 0f;
        TotalRegenerated = 0.0;
        TotalHarvested = 0.0;
        DepletionEvents = 0;
    }
}
