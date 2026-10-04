using System;
using Godot;

namespace SandBoxSim.Client;

public partial class StatisticsView : Control
{
    public MainGame Game { get; set; } = null!;
    private int _metric;
    private Vector2 _pointer;
    private bool _hover;
    private static readonly string[] Names = { "人口", "食物", "木材", "石料", "出生", "死亡", "迁移", "聚落" };
    public override void _Ready()
    {
        var picker = new OptionButton();
        picker.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide); picker.OffsetBottom = 38;
        HudStyle.Button(picker); picker.AddThemeStyleboxOverride("normal", HudStyle.Box(new Color(.13f, .19f, .16f), 8, 10, false));
        foreach (string name in Names) { picker.AddItem(name); }
        AddChild(picker); picker.ItemSelected += index => { _metric = (int)index; QueueRedraw(); };
        MouseExited += () => { _hover = false; QueueRedraw(); };
        Resized += QueueRedraw;
    }
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseMotion motion) { _pointer = motion.Position; _hover = true; QueueRedraw(); }
    }
    private float Value(SandBoxSim.Core.DailySample sample) => _metric switch
    {
        0 => sample.Population, 1 => sample.Food, 2 => sample.Wood, 3 => sample.Stone,
        4 => sample.Births, 5 => sample.Deaths, 6 => sample.Migrations, _ => sample.SettlementCount
    };
    public override void _Draw()
    {
        var font = GetThemeFont("font", "Label"); var samples = Game.Sim.Stats.Daily;
        void Caption(Vector2 p, string text, int size = 12, bool muted = true)
            => DrawString(font, p, text, fontSize: size, modulate: muted ? HudStyle.Muted : HudStyle.Text);
        if (samples.Count == 0)
        {
            Caption(new Vector2(0, 86), "等待第一天的记录", 19, false);
            Caption(new Vector2(0, 116), "世界每过一天，会留下新的观察样本。"); return;
        }
        int selected = samples.Count - 1;
        float left = 38, right = MathF.Max(left + 20, Size.X - 8), top = 154, bottom = MathF.Max(top + 80, MathF.Min(Size.Y - 105, 435));
        if (_hover && _pointer.Y >= top && _pointer.Y <= bottom)
            selected = Math.Clamp((int)MathF.Round((_pointer.X - left) / (right - left) * (samples.Count - 1)), 0, samples.Count - 1);
        Caption(new Vector2(0, 75), $"第 {samples[selected].Day} 天 · {Names[_metric]}");
        Caption(new Vector2(0, 114), Value(samples[selected]).ToString("0.#"), 32, false);
        float peak = 1; foreach (var s in samples) { peak = MathF.Max(peak, Value(s)); }
        peak = MathF.Ceiling(peak / 4) * 4;
        for (int row = 0; row <= 4; row++)
        {
            float y = bottom - (bottom - top) * row / 4;
            DrawLine(new Vector2(left, y), new Vector2(right, y), new Color(.64f, .72f, .65f, .12f), 1);
            Caption(new Vector2(0, y + 4), (peak * row / 4).ToString("0.#"), 10);
        }
        Vector2 Point(int index) => new(left + (right - left) * index / Math.Max(1, samples.Count - 1), bottom - Value(samples[index]) / peak * (bottom - top));
        if (samples.Count > 1)
        {
            var area = new Vector2[samples.Count + 2]; area[0] = new Vector2(left, bottom);
            for (int i = 0; i < samples.Count; i++) { area[i + 1] = Point(i); }
            area[^1] = new Vector2(right, bottom);
            DrawColoredPolygon(area, new Color(HudStyle.Accent, .10f));
            for (int i = 1; i < samples.Count; i++) { DrawLine(Point(i - 1), Point(i), HudStyle.Accent, 2, true); }
        }
        Vector2 point = Point(selected);
        DrawLine(new Vector2(point.X, top), new Vector2(point.X, bottom), new Color(HudStyle.Accent, .3f), 1);
        DrawCircle(point, 5, HudStyle.Ink); DrawCircle(point, 3, HudStyle.Accent);
        Caption(new Vector2(left, bottom + 24), $"第 {samples[0].Day} 天");
        string lastDay = $"第 {samples[^1].Day} 天";
        Caption(new Vector2(MathF.Max(left, right - font.GetStringSize(lastDay, fontSize: 12).X), bottom + 24), lastDay);
        Caption(new Vector2(0, bottom + 62), $"累计出生 {Game.Sim.Stats.TotalBirths}  ·  死亡 {Game.Sim.Stats.TotalDeaths}");
        Caption(new Vector2(0, bottom + 85), "移到曲线上，回看那一天的世界。", 11);
    }
}
