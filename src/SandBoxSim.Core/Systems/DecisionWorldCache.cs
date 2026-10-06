using System;
using System.Collections.Generic;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Core.Systems;

/// <summary>Pure results shared only within one AI stage, after movement and inventory writes finish.</summary>
public sealed class DecisionWorldCache
{
    private readonly Dictionary<int, (float Wood, float Stone)> _materials = new();
    private readonly Dictionary<int, int> _occupancy = new();
    public float? StorageDemand;
    private readonly List<int> _farmSites = new();
    private long _farmRevision = -1;
    private Environment.World? _farmWorld;
    public IReadOnlyList<int> FarmSites(in ActionContext ctx)
    {
        if (ReferenceEquals(_farmWorld, ctx.World) && _farmRevision == ctx.World.Revision) { return _farmSites; }
        _farmWorld = ctx.World; _farmRevision = ctx.World.Revision; _farmSites.Clear();
        for (int y = 0; y < ctx.World.Height; y++) for (int x = 0; x < ctx.World.Width; x++)
            if (BuildingStore.CanPlaceAt(ctx.World, BuildingKind.Farm, x, y)) { _farmSites.Add(y * ctx.World.Width + x); }
        return _farmSites;
    }
    public void Reset(Agents.AgentStore agents, int width)
    {
        _materials.Clear(); _occupancy.Clear(); StorageDemand = null;
        foreach (int slot in agents.AliveSlots())
        {
            int tile = agents.YOf(slot) * width + agents.XOf(slot);
            _occupancy.TryGetValue(tile, out int count); _occupancy[tile] = count + 1;
        }
    }
    public bool Occupied(in ActionContext ctx, int x, int y)
    {
        _occupancy.TryGetValue(y * ctx.World.Width + x, out int count);
        if (ctx.X == x && ctx.Y == y) { count--; }
        return count > 0;
    }
    public bool TryMaterials(int tile, out (float Wood, float Stone) stock) => _materials.TryGetValue(tile, out stock);
    public void StoreMaterials(int tile, float wood, float stone) => _materials[tile] = (wood, stone);
    public float Materials(int tile, ResourceKind kind)
    {
        var stock = _materials[tile];
        return kind == ResourceKind.Wood ? stock.Wood : stock.Stone;
    }
}
