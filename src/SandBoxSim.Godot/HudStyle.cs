using Godot;

namespace SandBoxSim.Client;

/// <summary>One HUD palette and spacing scale. Floating surfaces communicate depth without framing the whole world.</summary>
internal static class HudStyle
{
    public static bool MotionEnabled { get; set; } = true;
    public static readonly Color Ink = new("#171b20"), Surface = new(.095f, .111f, .132f, .97f), Text = new("#f0f1f2"),
        Muted = new("#aab2bc"), Accent = new("#d4b995"), Border = new("#414a55"), Wash = new("#2d3540");
    public static StyleBox Frame(int padding)
    {
        var surface = Box(Surface, 10, padding);
        surface.BorderColor = new Color(Border, .72f);
        surface.ShadowSize = 16; surface.ShadowOffset = new(0, 5);
        return surface;
    }
    private static readonly SystemFont DisplayFont = new() { FontNames = new[] { "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", "sans-serif" } };
    public static Label Heading(string text, int size)
    { var label = Label(text, size); label.AddThemeFontOverride("font", DisplayFont); return label; }
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
        button.AddThemeFontSizeOverride("font_size", 14);
        button.AddThemeColorOverride("font_color", prominent ? Ink : Text);
        button.AddThemeColorOverride("font_hover_color", prominent ? Ink : Accent);
        button.AddThemeColorOverride("font_pressed_color", Ink);
        button.AddThemeColorOverride("font_disabled_color", Muted);
        button.AddThemeColorOverride("icon_normal_color", Text);
        button.AddThemeColorOverride("icon_hover_color", Accent);
        button.AddThemeColorOverride("icon_pressed_color", Ink);
        button.AddThemeStyleboxOverride("normal", Box(prominent ? Accent : new Color(0, 0, 0, 0), 6, 9, false));
        button.AddThemeStyleboxOverride("hover", Box(prominent ? Accent.Lightened(.08f) : Wash, 6, 9, false));
        var pressed = Box(new Color("#3c4652"), 6, 9, false);
        button.AddThemeStyleboxOverride("pressed", pressed);
        button.AddThemeColorOverride("font_pressed_color", Accent); button.AddThemeColorOverride("icon_pressed_color", Accent);
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
