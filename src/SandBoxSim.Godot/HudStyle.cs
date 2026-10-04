using Godot;

namespace SandBoxSim.Client;

/// <summary>One HUD palette and spacing scale. Floating surfaces communicate depth without framing the whole world.</summary>
internal static class HudStyle
{
    public static readonly Color Ink = new("#101b1a"), Surface = new(.065f, .11f, .10f, .96f), Text = new("#edf1e8"),
        Muted = new("#a2b1a5"), Accent = new("#d8bb84"), Border = new(.62f, .70f, .61f, .18f);
    public static StyleBoxFlat Box(Color color, int radius = 12, int padding = 16, bool border = true)
        => new() { BgColor = color, BorderColor = Border, BorderWidthBottom = border ? 1 : 0, BorderWidthTop = border ? 1 : 0,
            BorderWidthLeft = border ? 1 : 0, BorderWidthRight = border ? 1 : 0, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, ContentMarginLeft = padding, ContentMarginRight = padding,
            ContentMarginTop = padding, ContentMarginBottom = padding, ShadowColor = new Color(.01f, .025f, .02f, .25f), ShadowSize = border ? 10 : 0, ShadowOffset = new Vector2(0, 4) };
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
        button.AddThemeStyleboxOverride("normal", Box(prominent ? Accent : new Color(0, 0, 0, 0), 8, 10, false));
        button.AddThemeStyleboxOverride("hover", Box(prominent ? Accent.Lightened(.1f) : new Color(.20f, .28f, .23f, .8f), 8, 10, false));
        button.AddThemeStyleboxOverride("pressed", Box(Accent, 8, 10, false));
        var focus = Box(new Color(0, 0, 0, 0), 8, 2); focus.BorderColor = Accent; button.AddThemeStyleboxOverride("focus", focus);
    }
    public static void Float(Control item, Vector2 anchor, Vector2 offset, Vector2 size)
    {
        item.AnchorLeft = item.AnchorRight = anchor.X; item.AnchorTop = item.AnchorBottom = anchor.Y;
        item.OffsetLeft = offset.X; item.OffsetRight = offset.X + size.X; item.OffsetTop = offset.Y; item.OffsetBottom = offset.Y + size.Y;
    }
}
