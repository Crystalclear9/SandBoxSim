using Godot;

namespace SandBoxSim.Client;

/// <summary>Native button semantics with a two-level label and a fixed optical icon size.</summary>
internal partial class HudToolCard : Button
{
    public string Title {get;set;}="";
    public string Description {get;set;}="";
    public Texture2D Glyph {get;set;}=null!;
    public override void _Draw()
    {
        bool selected=ButtonPressed;var color=selected?HudStyle.PaperAccent:HudStyle.PaperInk;
        var soft=HudStyle.PaperMuted;
        DrawTextureRect(Glyph,new Rect2(12,(Size.Y-26)/2,26,26),false,HudStyle.PaperAccent);
        var font=GetThemeFont("font");
        DrawString(font,new(48,Size.Y*.5f-1),Title,HorizontalAlignment.Left,Size.X-56,14,color);
        DrawString(font,new(48,Size.Y*.5f+17),Description,HorizontalAlignment.Left,Size.X-56,10,soft);
    }
}
