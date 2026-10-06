using Godot;

namespace SandBoxSim.Client;

internal partial class HudBevel : Control
{
    public int Margin { get; set; }
    public override void _Ready() { MouseFilter = MouseFilterEnum.Ignore; Resized += QueueRedraw; }
    public override void _Draw()
    {
        var outer = new Rect2(new Vector2(-Margin + 1, -Margin + 1), Size + Vector2.One * (Margin * 2 - 2));
        DrawRect(outer,new Color(HudStyle.Border,.6f),false,1);
        DrawLine(outer.Position+new Vector2(1,1),new Vector2(outer.End.X-1,outer.Position.Y+1),new Color(HudStyle.Accent,.25f),1,true);
        DrawLine(new Vector2(outer.Position.X+8,outer.End.Y-1),outer.End-new Vector2(8,1),new Color(0,0,0,.25f),1,true);
    }
}
