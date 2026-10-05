using Godot;

namespace SandBoxSim.Client;

/// <summary>One HUD palette and spacing scale. Floating surfaces communicate depth without framing the whole world.</summary>
internal static class HudStyle
{
    public static readonly Color Ink = new("#263b32"), Surface = new("#f3efe3"), Text = new("#263b32"),
        Muted = new("#70776a"), Accent = new("#526f53"), Border = new("#d6d2c3"), Wash = new("#e7e7d9");
    public static StyleBoxFlat Box(Color color, int radius = 12, int padding = 16, bool border = true)
        => new() { BgColor = color, BorderColor = Border, BorderWidthBottom = border ? 1 : 0, BorderWidthTop = border ? 1 : 0,
            BorderWidthLeft = border ? 1 : 0, BorderWidthRight = border ? 1 : 0, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, ContentMarginLeft = padding, ContentMarginRight = padding,
            ContentMarginTop = padding, ContentMarginBottom = padding, ShadowColor = new Color(.04f, .06f, .035f, .18f), ShadowSize = border ? 6 : 0, ShadowOffset = new Vector2(0, 3) };
    public static Label Label(string text, int size = 15, bool muted = false)
    {
        var label = new Label { Text = text }; label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", muted ? Muted : Text); return label;
    }
    public static void Button(Godot.Button button, bool prominent = false)
    {
        button.AddThemeFontSizeOverride("font_size", 14);
        button.AddThemeColorOverride("font_color", prominent ? Surface : Text);
        button.AddThemeColorOverride("font_hover_color", prominent ? Surface : Accent);
        button.AddThemeColorOverride("font_pressed_color", Surface);
        button.AddThemeColorOverride("font_disabled_color", Muted);
        button.AddThemeColorOverride("icon_normal_color", Ink);
        button.AddThemeColorOverride("icon_hover_color", Accent);
        button.AddThemeColorOverride("icon_pressed_color", Surface);
        button.AddThemeStyleboxOverride("normal", Box(prominent ? Accent : new Color(0, 0, 0, 0), 4, 8, false));
        button.AddThemeStyleboxOverride("hover", Box(prominent ? Accent.Lightened(.08f) : Wash, 4, 8, false));
        button.AddThemeStyleboxOverride("pressed", Box(Accent, 4, 8, false));
        var focus = Box(new Color(0, 0, 0, 0), 4, 2); focus.BorderColor = Accent; button.AddThemeStyleboxOverride("focus", focus);
    }
    public static void Float(Control item, Vector2 anchor, Vector2 offset, Vector2 size)
    {
        item.AnchorLeft = item.AnchorRight = anchor.X; item.AnchorTop = item.AnchorBottom = anchor.Y;
        item.OffsetLeft = offset.X; item.OffsetRight = offset.X + size.X; item.OffsetTop = offset.Y; item.OffsetBottom = offset.Y + size.Y;
    }
}
