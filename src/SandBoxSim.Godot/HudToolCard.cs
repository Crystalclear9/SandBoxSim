using Godot;

namespace SandBoxSim.Client;

/// <summary>Native button semantics with a two-level label and a fixed optical icon size.</summary>
internal partial class HudToolCard : Button
{
    public string Title {get;set;}="";
    public string Description {get;set;}="";
    public Texture2D Glyph {get;set;}=null!;
    private Label? _description;
    private bool _compact;
    public bool Compact
    {
        get=>_compact;
        set{if(_compact==value)return;_compact=value;CustomMinimumSize=new(158,value?48:86);if(_description!=null)_description.Visible=!value;}
    }
    public override void _Ready()
    {
        var margin=new MarginContainer {MouseFilter=MouseFilterEnum.Ignore};margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach(string side in new[]{"left","right","top","bottom"})margin.AddThemeConstantOverride("margin_"+side,10);
        AddChild(margin);
        var body=new VBoxContainer {MouseFilter=MouseFilterEnum.Ignore};body.AddThemeConstantOverride("separation",6);margin.AddChild(body);
        var heading=new HBoxContainer {MouseFilter=MouseFilterEnum.Ignore};heading.AddThemeConstantOverride("separation",8);body.AddChild(heading);
        heading.AddChild(new TextureRect {Texture=Glyph,CustomMinimumSize=new(22,22),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,Modulate=HudStyle.Accent,MouseFilter=MouseFilterEnum.Ignore});
        var title=HudStyle.Label(Title,14);title.MouseFilter=MouseFilterEnum.Ignore;heading.AddChild(title);
        var description=HudStyle.Label(Description,12,true);_description=description;description.Visible=!_compact;description.AutowrapMode=TextServer.AutowrapMode.WordSmart;description.MouseFilter=MouseFilterEnum.Ignore;body.AddChild(description);
        MouseDefaultCursorShape=CursorShape.PointingHand;
    }
}
