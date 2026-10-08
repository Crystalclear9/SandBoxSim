using Godot;

namespace SandBoxSim.Client;

/// <summary>Non-interactive engraved feedback; never changes the button's hit area or container layout.</summary>
internal partial class HudButtonDetail : Control
{
    private BaseButton _button = null!;
    private Tween? _transition;
    private float _light;
    public float Light { get => _light; set { _light = value; QueueRedraw(); } }
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _button = GetParent<BaseButton>();
        _button.MouseEntered += () => Illuminate(1);
        _button.MouseExited += () => Illuminate(0);
        _button.FocusEntered+=()=>Illuminate(1);_button.FocusExited+=()=>Illuminate(_button.IsHovered()?1:0);
        _button.Toggled += _ => QueueRedraw();
        _button.Draw += QueueRedraw; // SetPressedNoSignal also invalidates the parent's drawing.
        Resized += QueueRedraw;
    }
    private void Illuminate(float light)
    {
        if (_button.Disabled) { light = 0; } else if(_button.HasFocus()){light=1;}
        _transition?.Kill();
        _transition = CreateTween().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        _transition.TweenMethod(Callable.From<float>(value => Light = value), Light, light, HudStyle.MotionEnabled ? .16 : 0);
    }
    public override void _Draw()
    {
        if (_button == null || _button.Disabled || Size.X < 12 || Size.Y < 12) { return; }
        bool selected = _button.ToggleMode && _button.ButtonPressed;
        float strength = selected ? .85f : _light * .55f;
        if (strength < .01f) { return; }
        Color accent=_button.HasMeta("notebook") ? new Color("#bda984") : HudStyle.Accent;
        DrawRect(new Rect2(new Vector2(3,3),Size-new Vector2(6,6)),new Color(accent,selected?.16f:_light*.07f));
        float half = (Size.X - 16) * (selected ? .5f : .16f + .34f * _light);
        var center = new Vector2(Size.X / 2, Size.Y - 3);
        DrawLine(center - new Vector2(half, 0), center + new Vector2(half, 0), new Color(accent, strength), 1, true);

    }
    public override void _ExitTree() => _transition?.Kill();
}
