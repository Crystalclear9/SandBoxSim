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
        var icon=HudStyle.IconWell(Glyph,38);row.AddChild(icon);
        var body=new VBoxContainer {MouseFilter=MouseFilterEnum.Ignore,SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ShrinkCenter};body.AddThemeConstantOverride("separation",3);row.AddChild(body);
        var title=HudStyle.Heading(Title,15);title.ClipText=true;title.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;title.MouseFilter=MouseFilterEnum.Ignore;body.AddChild(title);
        var description=HudStyle.Label(Description,12,true);description.AutowrapMode=TextServer.AutowrapMode.WordSmart;description.MaxLinesVisible=2;
        description.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;description.MouseFilter=MouseFilterEnum.Ignore;body.AddChild(description);
        var selected=HudStyle.Label("✓",14);selected.AddThemeColorOverride("font_color",HudStyle.Accent);selected.CustomMinimumSize=new(16,0);selected.MouseFilter=MouseFilterEnum.Ignore;row.AddChild(selected);
        var inset=(StyleBoxFlat)icon.GetThemeStylebox("panel");
        // Wrapped copy must enlarge the hit rectangle with its content, rather
        // than painting a second line into the next tool's row.
        void FitHeight()
        {
            float height=Mathf.Max(66,margin.GetCombinedMinimumSize().Y);
            if(Mathf.Abs(CustomMinimumSize.Y-height)>.5f)CustomMinimumSize=new(CustomMinimumSize.X,height);
        }
        margin.MinimumSizeChanged+=FitHeight;Callable.From(FitHeight).CallDeferred();
        bool? previousSelection=null;
        void Selection()
        {
            if(previousSelection==ButtonPressed)return;
            previousSelection=ButtonPressed;
            selected.Modulate=ButtonPressed?Colors.White:Colors.Transparent;
            title.AddThemeColorOverride("font_color",ButtonPressed?HudStyle.Accent:HudStyle.Text);
            inset.BgColor=ButtonPressed?HudStyle.Wash:HudStyle.Ink;
            inset.BorderColor=ButtonPressed?new Color(HudStyle.Accent,.55f):new Color(HudStyle.Border,.65f);
        }
        Toggled+=_=>Selection();Draw+=Selection;Selection();
        MouseDefaultCursorShape=CursorShape.PointingHand;
    }
}
