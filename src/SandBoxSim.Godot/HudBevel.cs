using Godot;

namespace SandBoxSim.Client;

internal partial class HudBevel : Control
{
    public int Margin { get; set; }
    public override void _Ready() { MouseFilter = MouseFilterEnum.Ignore; Resized += QueueRedraw; }
    public override void _Draw()
    {
        var outer = new Rect2(new Vector2(-Margin + 1, -Margin + 1), Size + Vector2.One * (Margin * 2 - 2));
        // A lit upper lip and a fading cool interior give floating surfaces depth.
        // No perpetual redraw or full-screen post-processing is needed.
        for(int row=0;row<24;row++)
        {
            float inset=row<8?12-row:4;
            DrawLine(outer.Position+new Vector2(inset,row+1),new Vector2(outer.End.X-inset,outer.Position.Y+row+1),new Color(HudStyle.Accent,.043f*(1-row/24f)),1);
        }
        DrawLine(outer.Position+new Vector2(12,1),new Vector2(outer.End.X-12,outer.Position.Y+1),new Color(Colors.White,.13f),1,true);
        DrawLine(new Vector2(outer.Position.X+12,outer.End.Y-1),outer.End-new Vector2(12,1),new Color(Colors.Black,.24f),1,true);
        // Small end marks give the HUD the same restrained brass detail as its icons.
        foreach(float x in new[]{outer.Position.X+8,outer.End.X-8})
            DrawCircle(new Vector2(x,outer.Position.Y+7),.8f,new Color(HudStyle.Accent,.28f));
    }
}
