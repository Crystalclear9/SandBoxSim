using System;
using System.Collections.Generic;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Systems;

/// <summary>Opt-in player session. Advances on simulation days; saved by the client beside the core snapshot.</summary>
public sealed class WorldTrial
{
    public static readonly string[] Names = { "守住绿洲", "火线之后", "疫病来临" };
    public static readonly string[] Briefs = {
        "两轮旱季将消耗当地粮食。保护水与食物，让最初的居民度过缺粮期。",
        "林缘将出现两轮火情。用降雨或防火带保住居民与家园。",
        "两轮疫病即将进入社区。安排治愈，兼顾食物与住房。" };
    public int Kind { get; private set; } = -1;
    public bool Running { get; private set; }
    public bool Won { get; private set; }
    public bool Assisted { get; private set; }
    public float Influence { get; private set; } = 120;
    public int Days { get; private set; }
    public int StableDays { get; private set; }
    public int Interventions { get; private set; }
    public int Survivors { get; private set; }
    public int Hungry { get; private set; }
    public int Housed { get; private set; }
    public int Sick { get; private set; }
    public int OriginalCount => _cohort.Count;
    public int TargetSurvivors => Math.Max(1, (int)Math.Ceiling(OriginalCount * .8));
    public Int2 Location { get; private set; }
    public string Notice { get; private set; } = "选择一场试炼，改变条件，看看居民能否渡过难关。";
    private long _startDay, _lastDay;
    private readonly List<long> _cohort = new();

    public bool Start(Simulation sim, int kind)
    {
        if (kind < 0 || kind >= Names.Length || sim.Agents.LiveCount < 5) { Notice = "至少需要五位居民才能开始试炼。"; return false; }
        Kind = kind; Running = true; Won = false; Assisted = sim.Config.Rules.NoDeath;
        Influence = 120; Days = StableDays = Interventions = 0;
        _startDay = _lastDay = sim.Clock / sim.Config.Clock.TicksPerDay;
        _cohort.Clear(); foreach (int slot in sim.Agents.AliveSlots()) { _cohort.Add(sim.Society.Identity(slot)); }
        Refresh(sim); Notice = "试炼开始 · 第 4、7 天发生危机。干预额度每天恢复 8 点。";
        sim.InterveneRecordAuxiliary("开始试炼：" + Names[Kind]); return true;
    }
    public void Leave() { Running = false; Kind = -1; Notice = "已返回自由沙盒，世界保留试炼造成的变化。"; }
    public void MarkAssisted() { if (Running) { Assisted = true; } }
    public static int Cost(PlayerTool tool, int radius, float strength)
    {
        if (tool == PlayerTool.Inspect) { return 0; }
        radius = Math.Clamp(radius, 0, 20); strength = Math.Clamp(strength, 0, 100);
        float area = Math.Max(1, radius * radius);
        float unit = tool is PlayerTool.Human or PlayerTool.Animal ? Math.Max(1, strength) * 3
            : tool == PlayerTool.Wolf ? 12 : 2 + area * Math.Max(1, strength) / 180;
        return Math.Max(1, (int)Math.Ceiling(unit));
    }
    public bool TrySpend(PlayerTool tool, int radius, float strength)
    {
        if (!Running || tool == PlayerTool.Inspect) { return true; }
        return TrySpendPoints(Cost(tool, radius, strength));
    }
    public bool TrySpendPoints(int cost)
    {
        if (!Running) { return true; }
        cost = Math.Max(0, cost);
        if (Influence < cost) { Notice = $"额度不足：需要 {cost} 点，现有 {(int)Influence} 点。缩小范围或等待恢复。"; return false; }
        Influence -= cost; Interventions++; return true;
    }
    public bool TryWeather()
    {
        if (!Running) { return true; }
        if (Influence < 35) { Notice = "改变全球天气需要 35 点；局部降雨通常更便宜。"; return false; }
        Influence -= 35; Interventions++; return true;
    }
    public void Refresh(Simulation sim)
    {
        Survivors = Hungry = Housed = Sick = 0; int sx = 0, sy = 0;
        foreach (long id in _cohort)
        {
            var person = sim.Society.Find(id);
            if (person?.Alive != true) { continue; }
            int slot = person.Slot; Survivors++; sx += sim.Agents.XOf(slot); sy += sim.Agents.YOf(slot);
            if (sim.Agents.HungerOf(slot) > .7f) { Hungry++; }
            if (sim.Agents.DwellingOf(slot) >= 0) { Housed++; }
            if (sim.Diseases.OfSlot(slot)?.Active == true) { Sick++; }
        }
        Location = Survivors > 0 ? new Int2(sx / Survivors, sy / Survivors) : new Int2(sim.World.Width / 2, sim.World.Height / 2);
    }
    public void Advance(Simulation sim)
    {
        if (!Running) { return; }
        long day = sim.Clock / sim.Config.Clock.TicksPerDay;
        if (day <= _lastDay) { Refresh(sim); return; }
        // Client advances at each day boundary, avoiding missed crises at high speed.
        while (_lastDay < day && Running)
        {
            _lastDay++; Days = (int)(_lastDay - _startDay); Influence = Math.Min(200, Influence + 8); Refresh(sim);
            if (Days == 2) { Notice = "预警 · 第 4 天发生第一轮危机，第 7 天出现第二轮。现在可以准备。"; }
            if (Days is 4 or 7) { Crisis(sim); }
            bool ready = Survivors >= TargetSurvivors && Hungry <= Survivors / 4 && Housed >= (int)Math.Ceiling(Survivors * .3)
                && (Kind != 2 || Sick <= Survivors / 10);
            StableDays = Days >= 8 && ready ? StableDays + 1 : 0;
            if (StableDays >= 2) { Finish(sim, true); }
            else if (Survivors < TargetSurvivors || Days >= 14) { Finish(sim, false); }
        }
    }
    private void Crisis(Simulation sim)
    {
        int x = Location.X, y = Location.Y;
        if (Kind == 0)
        {
            sim.InterveneForceWeather(WeatherKind.Drought, 48);
            PlayerTools.Apply(sim, PlayerTool.Drought, x, y, 14, 100);
            for (int py = Math.Max(0, y - 12); py <= Math.Min(sim.World.Height - 1, y + 12); py++)
                for (int px = Math.Max(0, x - 12); px <= Math.Min(sim.World.Width - 1, x + 12); px++)
                {
                    var node = sim.World.TileAt(px, py).Resource;
                    if (node.Kind == ResourceKind.Food) { node.Amount *= .55f; sim.World.SetResource(px, py, node); }
                }
            sim.World.RefreshSpatialIndex(); Notice = "旱季到达 · 周边粮食减产。引水、补充食物或改善生产。";
        }
        else if (Kind == 1)
        {
            sim.InterveneForceWeather(WeatherKind.Drought, 24);
            int lit = 0;
            for (int r = 3; r <= 12 && lit < 4; r++)
                for (int py = y - r; py <= y + r && lit < 4; py++)
                    for (int px = x - r; px <= x + r && lit < 4; px++)
                        if (Math.Max(Math.Abs(px - x), Math.Abs(py - y)) == r && sim.Fire.Ignite(px, py, sim.Clock, "试炼火情")) { lit++; }
            Notice = lit > 0 ? "火情发生 · 降雨能灭火；水域和清除植被能切断蔓延。" : "防火准备生效 · 居民附近没有可点燃的植被。";
        }
        else
        {
            int infected = 0, target = Math.Max(1, Survivors / 5);
            foreach (long id in _cohort)
            {
                var person = sim.Society.Find(id);
                if (person?.Alive == true && infected < target) { sim.Diseases.Infect(person.Slot); infected++; }
            }
            Notice = "疫病进入社区 · 定位患病居民并治愈，同时保住食物供应。";
        }
        Refresh(sim); sim.InterveneRecordAuxiliary(Notice);
    }
    private void Finish(Simulation sim, bool won)
    {
        Won = won; Running = false;
        Notice = (won ? "试炼完成" : "试炼结束") + $" · 原居民存活 {Survivors}/{OriginalCount}，安居 {Housed}，饥饿 {Hungry}。干预 {Interventions} 次。"
            + (Assisted ? " 使用过辅助规则。" : won ? " 条件连续两天达成。" : "可以回溯起点，尝试另一种方案。");
        sim.InterveneRecordAuxiliary(Notice);
    }
    public string Forecast => Days < 4 ? $"第一轮危机：还有 {4 - Days} 天" : Days < 7 ? $"第二轮危机：还有 {7 - Days} 天"
        : $"恢复期：连续两天稳定 · 截止还有 {Math.Max(0, 14 - Days)} 天";
    public JsonValue Encode()
    {
        var ids = JsonValue.Array(); foreach (long id in _cohort) { ids.Add(JsonValue.From(id.ToString(System.Globalization.CultureInfo.InvariantCulture))); }
        return JsonValue.Object().Set("kind", JsonValue.From(Kind)).Set("running", JsonValue.From(Running)).Set("won", JsonValue.From(Won))
            .Set("assisted", JsonValue.From(Assisted)).Set("influence", JsonValue.From(Influence)).Set("days", JsonValue.From(Days))
            .Set("stable", JsonValue.From(StableDays)).Set("interventions", JsonValue.From(Interventions)).Set("startDay", JsonValue.From(_startDay))
            .Set("lastDay", JsonValue.From(_lastDay)).Set("notice", JsonValue.From(Notice)).Set("cohort", ids);
    }
    public static WorldTrial Decode(JsonValue value)
    {
        var trial = new WorldTrial(); if (!value.IsObject) { return trial; }
        trial.Kind = Math.Clamp(value.GetInt("kind", -1), -1, 2); trial.Running = trial.Kind >= 0 && value.GetBool("running");
        trial.Won = value.GetBool("won"); trial.Assisted = value.GetBool("assisted");
        trial.Influence = Math.Clamp(value.GetFloat("influence", 120), 0, 200);
        trial.Days = Math.Max(0, value.GetInt("days")); trial.StableDays = Math.Max(0, value.GetInt("stable"));
        trial.Interventions = Math.Max(0, value.GetInt("interventions")); trial._startDay = value.GetLong("startDay"); trial._lastDay = value.GetLong("lastDay");
        trial.Notice = value.GetString("notice", trial.Notice);
        foreach (var id in value.Get("cohort").Items) { if (long.TryParse(id.AsString(), out long parsed)) { trial._cohort.Add(parsed); } }
        if (trial._cohort.Count == 0) { trial.Running = false; }
        return trial;
    }
}
