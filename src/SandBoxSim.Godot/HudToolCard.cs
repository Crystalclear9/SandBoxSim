using Godot;

namespace SandBoxSim.Client;

/// <summary>Native button semantics with a two-level label and a fixed optical icon size.</summary>
internal partial class HudToolCard : Button
{
    public string Title {get;set;}="";
    public string Description {get;set;}="";
    public Texture2D Glyph {get;set;}=null!;
    public override void _Ready()
    {
        var margin=new MarginContainer {MouseFilter=MouseFilterEnum.Ignore};margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach(string side in new[]{"left","right","top","bottom"})margin.AddThemeConstantOverride("margin_"+side,10);
        AddChild(margin);
        var row=new HBoxContainer {MouseFilter=MouseFilterEnum.Ignore};row.AddThemeConstantOverride("separation",12);margin.AddChild(row);
        row.AddChild(new TextureRect {Texture=Glyph,CustomMinimumSize=new(28,28),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,MouseFilter=MouseFilterEnum.Ignore});
        var body=new VBoxContainer {MouseFilter=MouseFilterEnum.Ignore,SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ShrinkCenter};body.AddThemeConstantOverride("separation",3);row.AddChild(body);
        var title=HudStyle.Label(Title,14);title.MouseFilter=MouseFilterEnum.Ignore;body.AddChild(title);
        var description=HudStyle.Label(Description,12,true);description.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;description.MouseFilter=MouseFilterEnum.Ignore;body.AddChild(description);
        var selected=HudStyle.Label("✓",14);selected.AddThemeColorOverride("font_color",HudStyle.Accent);selected.CustomMinimumSize=new(16,0);selected.MouseFilter=MouseFilterEnum.Ignore;row.AddChild(selected);
        void Selection(){selected.Modulate=ButtonPressed?Colors.White:Colors.Transparent;}
        Toggled+=_=>Selection();Draw+=Selection;Selection();
        MouseDefaultCursorShape=CursorShape.PointingHand;
    }
}
