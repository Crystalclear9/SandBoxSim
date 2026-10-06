using System.Collections.Generic;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Core.Systems;

public sealed class WorldAlert
{
    public string Key = "", Title = "", Detail = "";
    public Int2 Location;
    public PlayerTool Tool;
    public int Count;
    public long Person;
}

/// <summary>Pure observations for the operations UI; never runs NPC decisions or changes history.</summary>
public static class WorldAlerts
{
    public static IReadOnlyList<WorldAlert> Observe(Simulation sim)
    {
        var alerts = new List<WorldAlert>(); int burning = 0, sick = 0, hungry = 0, thirsty = 0, homeless = 0;
        int patient = -1, starving = -1, dry = -1, unhoused = -1; Int2 fire = default;
        for (int i = 0; i < sim.World.Tiles.Length; i++)
            if (sim.World.Tiles[i].Fire == FireState.Burning)
            { if (burning == 0) { fire = new Int2(i % sim.World.Width, i / sim.World.Width); } burning++; }
        foreach (int slot in sim.Agents.AliveSlots())
        {
            if (sim.Diseases.OfSlot(slot)?.Active == true) { if (patient < 0) { patient = slot; } sick++; }
            if (sim.Agents.HungerOf(slot) > .7f)
            { if (starving < 0 || sim.Agents.HungerOf(slot) > sim.Agents.HungerOf(starving)) { starving = slot; } hungry++; }
            if (sim.Agents.ThirstOf(slot) > .7f)
            { if (dry < 0 || sim.Agents.ThirstOf(slot) > sim.Agents.ThirstOf(dry)) { dry = slot; } thirsty++; }
            if (sim.Agents.DwellingOf(slot) < 0) { if (unhoused < 0) { unhoused = slot; } homeless++; }
        }
        void Add(string key, string title, string hint, PlayerTool tool, int count, int slot)
        {
            if (count == 0) { return; }
            var person = sim.Society.OfSlot(slot);
            alerts.Add(new WorldAlert { Key = key, Title = title, Count = count, Tool = tool, Person = person?.Id ?? 0,
                Location = sim.Agents.PositionOf(slot), Detail = (person?.Name ?? "居民") + " · " + hint });
        }
        if (burning > 0) { alerts.Add(new WorldAlert { Key = "fire", Title = "发现火情", Detail = "降雨扑救，或提前建设防火走廊", Count = burning, Location = fire, Tool = PlayerTool.Rain }); }
        Add("disease", "居民需要救治", "治愈并恢复劳动能力", PlayerTool.Heal, sick, patient);
        Add("food", "粮食告急", "先救急，再规划持续粮食供给", PlayerTool.Food, hungry, starving);
        Add("water", "寻找饮用水", "土壤湿度不能替代可抵达的水源", PlayerTool.River, thirsty, dry);
        Add("home", "居民尚未安居", "补充木石，保留可建造的空地", PlayerTool.Wood, homeless, unhoused);
        return alerts;
    }
}
