using Godot;

namespace SandBoxSim.Client;

internal partial class HudBevel : Control
{
    public int Margin { get; set; }
    public override void _Ready() { MouseFilter = MouseFilterEnum.Ignore; Resized += QueueRedraw; }
    public override void _Draw()
    {
        var outer = new Rect2(new Vector2(-Margin + 1, -Margin + 1), Size + Vector2.One * (Margin * 2 - 2));
        DrawRect(outer, new Color(HudStyle.Accent, .32f), false, 1);
        DrawLine(outer.Position + new Vector2(5, 2), outer.Position + new Vector2(outer.Size.X - 5, 2), new Color(1, 1, 1, .065f));
    }
}
