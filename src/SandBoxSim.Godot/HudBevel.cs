using Godot;

namespace SandBoxSim.Client;

internal partial class HudBevel : Control
{
    public int Margin { get; set; }
    public override void _Ready() { MouseFilter = MouseFilterEnum.Ignore; Resized += QueueRedraw; }
    public override void _Draw()
    {
        var outer = new Rect2(new Vector2(-Margin + 1, -Margin + 1), Size + Vector2.One * (Margin * 2 - 2));
        DrawRect(outer, new Color(HudStyle.Accent, .28f), false, 1);
        DrawRect(outer.Grow(-3), new Color(HudStyle.Accent, .08f), false, 1);
        DrawLine(outer.Position + new Vector2(6, 2), outer.Position + new Vector2(outer.Size.X - 6, 2), new Color(1, 1, 1, .08f));
        // A fine central inlay gives large surfaces a crafted edge without filling their content.
        if (outer.Size.X > 240)
        {
            var center = new Vector2(outer.GetCenter().X, outer.Position.Y + 3);
            DrawLine(center - new Vector2(38, 0), center - new Vector2(9, 0), new Color(HudStyle.Accent, .3f), 1, true);
            DrawLine(center + new Vector2(9, 0), center + new Vector2(38, 0), new Color(HudStyle.Accent, .3f), 1, true);
            DrawPolyline(new[] { center - new Vector2(4, 0), center + new Vector2(0, 2), center + new Vector2(4, 0), center - new Vector2(4, 0) }, new Color(HudStyle.Accent, .5f), 1, true);
        }
        foreach (var corner in new[] { outer.Position, new Vector2(outer.End.X, outer.Position.Y), outer.End, new Vector2(outer.Position.X, outer.End.Y) })
        {
            float x = corner.X == outer.Position.X ? 1 : -1, y = corner.Y == outer.Position.Y ? 1 : -1;
            DrawLine(corner + new Vector2(x * 3, y * 3), corner + new Vector2(x * 13, y * 3), new Color(HudStyle.Accent, .65f));
            DrawLine(corner + new Vector2(x * 3, y * 3), corner + new Vector2(x * 3, y * 13), new Color(HudStyle.Accent, .65f));
        }
    }
}
