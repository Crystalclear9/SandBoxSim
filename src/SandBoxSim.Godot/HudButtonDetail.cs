using Godot;

namespace SandBoxSim.Client;

/// <summary>Pointer light, a short travelling highlight and a bounded press ripple.
/// All animation is drawn inside the original button; input rectangles never move.</summary>
internal partial class HudButtonDetail : Control
{
    private BaseButton _button = null!;
    private Tween? _transition;
    private float _light;
    private float _ripple=1, _sweep=1;
    private Vector2 _pressPoint;
    public float Light { get => _light; set { _light = value; QueueRedraw(); } }
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddToGroup("hud_button_motion");
        ClipContents=true;SetProcess(false);
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _button = GetParent<BaseButton>();
        _button.MouseEntered += () => Illuminate(1);
        _button.MouseExited += () => Illuminate(0);
        _button.FocusEntered+=()=>Illuminate(1);_button.FocusExited+=()=>Illuminate(_button.IsHovered()?1:0);
        _button.ButtonDown+=()=>
        {
            if(_button.Disabled||!HudStyle.MotionEnabled)return;
            _pressPoint=_button.IsHovered()?_button.GetLocalMousePosition():Size*.5f;
            _ripple=0;SetProcess(true);
        };
        _button.Toggled += _ => QueueRedraw();
        _button.Draw += QueueRedraw; // SetPressedNoSignal also invalidates the parent's drawing.
        Resized += QueueRedraw;
    }
    internal void Settle()
    {
        _transition?.Kill();_sweep=_ripple=1;SetProcess(false);
        Light=_button.IsHovered()||_button.HasFocus()?1:0;
    }
    private void Illuminate(float light)
    {
        if (_button.Disabled) { light = 0; } else if(_button.HasFocus()){light=1;}
        _transition?.Kill();
        if(!HudStyle.MotionEnabled){Light=light;_sweep=_ripple=1;SetProcess(false);return;}
        if(light>0){_sweep=0;SetProcess(true);}
        _transition = CreateTween().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        _transition.TweenMethod(Callable.From<float>(value => Light = value), Light, light, HudStyle.MotionEnabled ? .16 : 0);
    }
    public override void _Process(double delta)
    {
        if(!HudStyle.MotionEnabled){_sweep=_ripple=1;_transition?.Kill();Light=_button.IsHovered()||_button.HasFocus()?1:0;SetProcess(false);return;}
        _sweep=Mathf.Min(1,_sweep+(float)delta/.42f);_ripple=Mathf.Min(1,_ripple+(float)delta/.38f);
        QueueRedraw();if(_sweep>=1&&_ripple>=1)SetProcess(false);
    }
    public override void _Draw()
    {
        if (_button == null || _button.Disabled || Size.X < 12 || Size.Y < 12) { return; }
        bool selected = _button.ToggleMode && _button.ButtonPressed;
        float strength = selected ? .85f : _light * .55f;
        Color accent=selected&&_button.HasMeta("filled_action")&&_button.GetMeta("filled_action").AsBool()?HudStyle.Ink:HudStyle.Accent;
        if(_button.HasMeta("notebook"))accent=HudStyle.PaperAccent;
        if(_ripple<1)
            DrawArc(_pressPoint,4+_ripple*Size.Length(),0,Mathf.Tau,48,new Color(accent,(1-_ripple)*.20f),2,true);
        if(_sweep<1&&_light>.01f)
        {
            float x=Mathf.Lerp(-Size.X*.4f,Size.X*1.3f,_sweep);
            DrawColoredPolygon(new[]{new Vector2(x,3),new Vector2(x+9,3),new Vector2(x-10,Size.Y-3),new Vector2(x-19,Size.Y-3)},new Color(accent,.035f*Mathf.Sin(_sweep*Mathf.Pi)));
        }
        if (strength < .01f) { return; }
        float half = (Size.X - 16) * (selected ? .5f : .16f + .34f * _light);
        var center = new Vector2(Size.X / 2, Size.Y - 3);
        DrawLine(center - new Vector2(half, 0), center + new Vector2(half, 0), new Color(accent, strength*.55f), 1, true);

    }
    public override void _ExitTree() => _transition?.Kill();
}
