using Godot;

namespace SandBoxSim.Client;

/// <summary>One HUD palette and spacing scale. Floating surfaces communicate depth without framing the whole world.</summary>
internal static class HudStyle
{
    public static bool MotionEnabled { get; set; } = true;
    public static readonly Color Ink = new("#20231f"), Surface = new("#292d28"), Text = new("#eeeade"),
        Muted = new("#b8bbad"), Accent = new("#d6c294"), Border = new("#4f574b"), Wash = new("#373e34");
    public static readonly Color Paper=Surface,PaperInk=Text,PaperMuted=Muted,
        PaperLine=Border,PaperAccent=Accent,PaperWash=Wash;
    public static StyleBox Frame(int padding)
    {
        var surface = Box(Surface, 9, padding);
        surface.BorderColor = Border;
        surface.ShadowSize = 8; surface.ShadowOffset = new(0, 3);
        return surface;
    }
    private static readonly SystemFont DisplayFont = new() { FontNames = new[] { "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", "sans-serif" } };
    private static readonly SystemFont HeadingFont = new() { FontNames = new[] { "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", "sans-serif" }, FontWeight=600 };
    internal static readonly SystemFont NumericFont=new() {FontNames=new[]{"Bahnschrift","Inter","Segoe UI","sans-serif"}};
    public static Label Heading(string text, int size)
    { var label = Label(text, size); label.AddThemeFontOverride("font", HeadingFont); return label; }
    public static PanelContainer IconWell(Texture2D texture,int size=36)
    {
        var plate=new PanelContainer {CustomMinimumSize=new(size,size),SizeFlagsVertical=Control.SizeFlags.ShrinkCenter,MouseFilter=Control.MouseFilterEnum.Ignore};
        var box=Box(Ink,8,7);box.BorderColor=new Color(Border,.65f);box.ShadowSize=0;plate.AddThemeStyleboxOverride("panel",box);
        plate.AddChild(new TextureRect {Texture=texture,TextureFilter=CanvasItem.TextureFilterEnum.LinearWithMipmaps,ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,MouseFilter=Control.MouseFilterEnum.Ignore});
        return plate;
    }
    public static PanelContainer Keycap(string text)
    {
        var plate=new PanelContainer {SizeFlagsVertical=Control.SizeFlags.ShrinkCenter,MouseFilter=Control.MouseFilterEnum.Ignore};
        var box=Box(Ink,4,5);box.ShadowSize=0;plate.AddThemeStyleboxOverride("panel",box);
        var label=Label(text,11,true);label.MouseFilter=Control.MouseFilterEnum.Ignore;plate.AddChild(label);return plate;
    }
    public static StyleBoxFlat Box(Color color, int radius = 12, int padding = 16, bool border = true)
        => new() { BgColor = color, BorderColor = Border, BorderWidthBottom = border ? 1 : 0, BorderWidthTop = border ? 1 : 0,
            BorderWidthLeft = border ? 1 : 0, BorderWidthRight = border ? 1 : 0, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, ContentMarginLeft = padding, ContentMarginRight = padding,
            ContentMarginTop = padding, ContentMarginBottom = padding, ShadowColor = new Color(.025f, .035f, .05f, .30f), ShadowSize = border ? 10 : 0, ShadowOffset = new Vector2(0, 3) };
    public static Label Label(string text, int size = 15, bool muted = false)
    {
        var label = new Label { Text = text }; label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", muted ? Muted : Text); return label;
    }
    public static void Button(Godot.Button button, bool prominent = false)
    {
        button.SetMeta("filled_action",prominent);
        button.TextureFilter=CanvasItem.TextureFilterEnum.LinearWithMipmaps;
        button.AddThemeFontSizeOverride("font_size", 14);
        button.CustomMinimumSize=new Vector2(button.CustomMinimumSize.X,36);
        button.MouseDefaultCursorShape=Control.CursorShape.PointingHand;
        button.AddThemeColorOverride("font_color", prominent ? Ink : Text);
        button.AddThemeColorOverride("font_hover_color", prominent ? Ink : Accent);
        button.AddThemeColorOverride("font_pressed_color", prominent ? Ink : Accent);
        button.AddThemeColorOverride("font_disabled_color", Muted);
        button.AddThemeColorOverride("icon_normal_color", Text);
        button.AddThemeColorOverride("icon_hover_color", Accent);
        button.AddThemeColorOverride("icon_pressed_color", prominent ? Ink : Accent);
        button.AddThemeStyleboxOverride("normal", Box(prominent ? Accent : new Color(0, 0, 0, 0), 6, 9, false));
        button.AddThemeStyleboxOverride("hover", Box(prominent ? Accent.Lightened(.08f) : Wash, 5, 9, false));
        var pressed = Box(prominent ? Accent : new Color("#495040"), 5, 9, false);
        button.AddThemeStyleboxOverride("pressed", pressed);
        button.AddThemeStyleboxOverride("hover_pressed",Box(prominent ? Accent.Lightened(.05f) : Wash.Lightened(.06f),5,9,false));
        button.AddThemeStyleboxOverride("disabled",Box(new Color(Wash,.25f),9,9,false));
        var focus = Box(new Color(0, 0, 0, 0), 4, 2); focus.BorderColor = Accent; button.AddThemeStyleboxOverride("focus", focus);
        Tween? feedback = null;
        void Feedback(float value) { if (!button.IsInsideTree() || button.Disabled) { return; } feedback?.Kill(); feedback = button.CreateTween(); feedback.TweenProperty(button, "modulate", new Color(value, value, value), MotionEnabled ? .12 : 0).SetTrans(Tween.TransitionType.Sine); }
        button.MouseEntered += () => Feedback(1.025f); button.MouseExited += () => Feedback(1f);
        button.ButtonDown += () => Feedback(.96f); button.ButtonUp += () => Feedback(1f);
        button.AddChild(new HudButtonDetail());
    }
    public static void Float(Control item, Vector2 anchor, Vector2 offset, Vector2 size)
    {
        item.AnchorLeft = item.AnchorRight = anchor.X; item.AnchorTop = item.AnchorBottom = anchor.Y;
        item.OffsetLeft = offset.X; item.OffsetRight = offset.X + size.X; item.OffsetTop = offset.Y; item.OffsetBottom = offset.Y + size.Y;
    }
    public static void LightButton(Godot.Button button)
    {
        button.SetMeta("notebook",true);
        foreach(string state in new[]{"font_color","icon_normal_color"})button.AddThemeColorOverride(state,PaperInk);
        foreach(string state in new[]{"font_hover_color","font_pressed_color","icon_hover_color","icon_pressed_color"})button.AddThemeColorOverride(state,PaperAccent);
        button.AddThemeStyleboxOverride("normal",Box(Colors.Transparent,6,9,false));
        button.AddThemeStyleboxOverride("hover",Box(PaperWash,6,9,false));
        button.AddThemeStyleboxOverride("pressed",Box(new Color("#495040"),6,9,false));
        button.AddThemeStyleboxOverride("hover_pressed",Box(new Color("#505746"),6,9,false));
        var focus=Box(Colors.Transparent,6,9);focus.BorderColor=PaperAccent;focus.ShadowSize=0;
        button.AddThemeStyleboxOverride("focus",focus);
    }
    public static void LightSurface(PanelContainer panel)
    {
        var surface=Box(Paper,9,14);surface.BorderColor=PaperLine;surface.ShadowSize=12;
        panel.AddThemeStyleboxOverride("panel",surface);
        void Apply(Node node)
        {
            if(node is HudBevel bevel)bevel.Visible=false;
            if(node is Godot.Button button)LightButton(button);
            if(node is Label label)
            {
                bool muted=label.GetThemeColor("font_color")==Muted||label.GetThemeColor("font_color")==PaperMuted;
                label.AddThemeColorOverride("font_color",muted?PaperMuted:PaperInk);
            }
            foreach(Node child in node.GetChildren())Apply(child);
        }
        Apply(panel);
    }
    public static Theme InterfaceTheme()
    {
        var theme=new Theme {DefaultFont=DisplayFont,DefaultFontSize=14};
        foreach(string type in new[]{"Label","Button","OptionButton","PopupMenu","LineEdit","CheckBox","SpinBox"})
        {theme.SetColor("font_color",type,Text);theme.SetColor("font_disabled_color",type,Muted);}
        var popup=Box(Surface,7,12);popup.BorderColor=Border;
        theme.SetStylebox("panel","PopupMenu",popup);
        theme.SetStylebox("hover","PopupMenu",Box(Wash,4,8,false));
        theme.SetColor("font_hover_color","PopupMenu",Accent);
        theme.SetConstant("v_separation","PopupMenu",12);
        var tooltip=Box(Surface,6,12);tooltip.BorderColor=Border;
        theme.SetStylebox("panel","TooltipPanel",tooltip);
        theme.SetColor("font_color","TooltipLabel",Colors.White);theme.SetColor("font_shadow_color","TooltipLabel",Colors.Transparent);
        theme.SetFontSize("font_size","TooltipLabel",13);
        var field=Box(Ink,5,8);field.BorderColor=Border;field.ShadowSize=0;
        theme.SetStylebox("normal","LineEdit",field);
        var focus=(StyleBoxFlat)field.Duplicate();focus.BorderColor=Accent;theme.SetStylebox("focus","LineEdit",focus);
        theme.SetColor("font_placeholder_color","LineEdit",Muted);theme.SetColor("caret_color","LineEdit",Accent);
        theme.SetColor("font_selected_color","LineEdit",Text);theme.SetColor("selection_color","LineEdit",new Color("#59644c"));
        theme.SetConstant("modulate_arrow","OptionButton",1);
        foreach(string direction in new[]{"up","down"})
        {
            theme.SetColor(direction+"_icon_modulate","SpinBox",Text);
            theme.SetColor(direction+"_hover_icon_modulate","SpinBox",Accent);
            theme.SetColor(direction+"_pressed_icon_modulate","SpinBox",Accent);
            theme.SetColor(direction+"_disabled_icon_modulate","SpinBox",new Color(Muted,.4f));
        }
        theme.SetConstant("buttons_width","SpinBox",24);
        theme.SetStylebox("separator","VSeparator",new StyleBoxLine {Color=Border,Thickness=1,Vertical=true});
        var track=Box(Ink,2,0,false);track.ContentMarginTop=2;track.ContentMarginBottom=2;
        theme.SetStylebox("slider","HSlider",track);
        foreach(string state in new[]{"grabber_area","grabber_area_highlight"})
        {var fill=Box(state=="grabber_area"?Accent.Darkened(.2f):Accent,2,0,false);fill.ContentMarginTop=2;fill.ContentMarginBottom=2;theme.SetStylebox(state,"HSlider",fill);}
        foreach(string state in new[]{"normal","hover","pressed","focus"})
            theme.SetStylebox(state,"OptionButton",state=="focus"?focus:Box(state=="normal"?Colors.Transparent:Wash,4,8,false));
        var rail=Box(new Color(Border,.35f),2,0,false);rail.ContentMarginLeft=3;rail.ContentMarginRight=3;
        theme.SetStylebox("scroll","VScrollBar",rail);
        foreach(string state in new[]{"grabber","grabber_highlight","grabber_pressed"})theme.SetStylebox(state,"VScrollBar",Box(state=="grabber"?Border:Muted,2,0,false));
        var rule=Box(new Color(Border,.55f),0,0,false);rule.ContentMarginTop=1;
        theme.SetStylebox("separator","HSeparator",rule);
        return theme;
    }
    private static Texture2D? _unchecked, _checked;
    public static void Rule(CheckBox control)
    {
        Button(control);
        _unchecked ??= RuleIcon(false); _checked ??= RuleIcon(true);
        control.AddThemeIconOverride("unchecked", _unchecked);
        control.AddThemeIconOverride("checked", _checked);
        control.AddThemeConstantOverride("h_separation", 10);
    }
    private static Texture2D RuleIcon(bool selected)
    {
        var image = new Image();
        string shape = selected
            ? "<rect x='1' y='1' width='16' height='16' rx='3' fill='#bda572'/><path d='M5 9l3 3 5-6' fill='none' stroke='#211f19' stroke-width='2'/>"
            : "<rect x='1' y='1' width='16' height='16' rx='3' fill='none' stroke='#b2aa97' stroke-width='1.5'/>";
        image.LoadSvgFromString("<svg xmlns='http://www.w3.org/2000/svg' width='18' height='18'>" + shape + "</svg>");
        return ImageTexture.CreateFromImage(image);
    }
}
