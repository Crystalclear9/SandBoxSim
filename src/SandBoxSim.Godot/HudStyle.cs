using Godot;

namespace SandBoxSim.Client;

/// <summary>One HUD palette and spacing scale. Floating surfaces communicate depth without framing the whole world.</summary>
internal static class HudStyle
{
    public static bool MotionEnabled { get; set; } = true;
    public static readonly Color Ink = new("#1b201f"), Surface = new(.075f, .092f, .09f, .97f), Text = new("#e9e3d6"),
        Muted = new("#b5b4a9"), Accent = new("#bda984"), Border = new("#48534e"), Wash = new("#30382f");
    private static Texture2D? _leather;
    private static ShaderMaterial? _frameMaterial;
    public static ShaderMaterial FrameMaterial => _frameMaterial ??= new ShaderMaterial { Shader = new Shader { Code =
        "shader_type canvas_item; void fragment(){ vec4 c = texture(TEXTURE,UV)*COLOR; vec3 base=mix(vec3(.078,.092,.093),vec3(.145,.157,.150),1.0-UV.y); float edge=pow(1.0-UV.y,8.0)*.014; COLOR=vec4(base+c.rgb*.025+edge,c.a); }" } };
    public static StyleBox Frame(int padding)
    {
        if (_leather == null)
        {
            var atlas = GD.Load<Texture2D>("res://assets/textures/natural-materials.png");
            float cell = atlas.GetWidth() / 4f;
            _leather = new AtlasTexture { Atlas = atlas, Region = new Rect2(cell * 2, cell * 2, cell, cell) };
        }
        return new StyleBoxTexture { Texture = _leather, ModulateColor = new Color(.26f, .25f, .23f, .98f),
            TextureMarginLeft = 8, TextureMarginRight = 8, TextureMarginTop = 8, TextureMarginBottom = 8,
            ContentMarginLeft = padding, ContentMarginRight = padding, ContentMarginTop = padding, ContentMarginBottom = padding };
    }
    private static readonly SystemFont DisplayFont = new() { FontNames = new[] { "Noto Serif CJK SC", "SimSun", "Songti SC", "serif" } };
    public static Label Heading(string text, int size)
    { var label = Label(text, size); label.AddThemeFontOverride("font", DisplayFont); return label; }
    public static StyleBoxFlat Box(Color color, int radius = 12, int padding = 16, bool border = true)
        => new() { BgColor = color, BorderColor = Border, BorderWidthBottom = border ? 1 : 0, BorderWidthTop = border ? 1 : 0,
            BorderWidthLeft = border ? 1 : 0, BorderWidthRight = border ? 1 : 0, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, ContentMarginLeft = padding, ContentMarginRight = padding,
            ContentMarginTop = padding, ContentMarginBottom = padding, ShadowColor = new Color(.05f, .10f, .07f, .22f), ShadowSize = border ? 10 : 0, ShadowOffset = new Vector2(0, 3) };
    public static Label Label(string text, int size = 15, bool muted = false)
    {
        var label = new Label { Text = text }; label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", muted ? Muted : Text); return label;
    }
    public static void Button(Godot.Button button, bool prominent = false)
    {
        button.AddThemeFontSizeOverride("font_size", 14);
        button.AddThemeColorOverride("font_color", prominent ? Ink : Text);
        button.AddThemeColorOverride("font_hover_color", prominent ? Ink : Accent);
        button.AddThemeColorOverride("font_pressed_color", Ink);
        button.AddThemeColorOverride("font_disabled_color", Muted);
        button.AddThemeColorOverride("icon_normal_color", Text);
        button.AddThemeColorOverride("icon_hover_color", Accent);
        button.AddThemeColorOverride("icon_pressed_color", Ink);
        button.AddThemeStyleboxOverride("normal", Box(prominent ? Accent : new Color(0, 0, 0, 0), 3, 9, false));
        button.AddThemeStyleboxOverride("hover", Box(prominent ? Accent.Lightened(.08f) : new Color(.25f, .27f, .22f, .55f), 3, 9, false));
        var pressed = Box(new Color(.24f, .25f, .19f, .72f), 3, 9, false);
        button.AddThemeStyleboxOverride("pressed", pressed);
        button.AddThemeColorOverride("font_pressed_color", Accent); button.AddThemeColorOverride("icon_pressed_color", Accent);
        var focus = Box(new Color(0, 0, 0, 0), 4, 2); focus.BorderColor = Accent; button.AddThemeStyleboxOverride("focus", focus);
        Tween? feedback = null;
        void Feedback(float value) { if (!button.IsInsideTree() || button.Disabled) { return; } feedback?.Kill(); feedback = button.CreateTween(); feedback.TweenProperty(button, "modulate", new Color(value, value, value), MotionEnabled ? .12 : 0).SetTrans(Tween.TransitionType.Sine); }
        button.MouseEntered += () => Feedback(1.07f); button.MouseExited += () => Feedback(1f);
        button.ButtonDown += () => Feedback(.9f); button.ButtonUp += () => Feedback(1f);
        button.AddChild(new HudButtonDetail());
    }
    public static void Float(Control item, Vector2 anchor, Vector2 offset, Vector2 size)
    {
        item.AnchorLeft = item.AnchorRight = anchor.X; item.AnchorTop = item.AnchorBottom = anchor.Y;
        item.OffsetLeft = offset.X; item.OffsetRight = offset.X + size.X; item.OffsetTop = offset.Y; item.OffsetBottom = offset.Y + size.Y;
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
