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
        for(int row=0;row<18;row++)
        {
            float inset=row<8?12-row:4;
            DrawLine(outer.Position+new Vector2(inset,row+1),new Vector2(outer.End.X-inset,outer.Position.Y+row+1),new Color(new Color("#bccce0"),.035f*(1-row/18f)),1);
        }
        DrawLine(outer.Position+new Vector2(12,1),new Vector2(outer.End.X-12,outer.Position.Y+1),new Color(Colors.White,.13f),1,true);
    }
}
