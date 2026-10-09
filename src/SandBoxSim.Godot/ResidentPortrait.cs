using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

/// <summary>Isolated studio viewport. Portrait interactions never reach the world camera.</summary>
internal partial class ResidentPortrait : SubViewportContainer
{
    private enum Framing { Face, Hands, Body }
    private Framing _framing;
    private readonly System.Collections.Generic.Dictionary<Framing,Godot.Button> _frameButtons=new();
    private const float FaceDistance=.68f;
    private float _pitch,_viewDistance=FaceDistance;
    private Vector3 _viewCenter;
    private bool _centerReady;
    public bool Compact { get; set; }
    private readonly NatureModels _models = new();
    private SubViewport _viewport = null!;
    private Node3D _stage = null!;
    private MeshInstance3D _portraitGround=null!;
    private Camera3D _camera = null!;
    private ResidentRig? _resident;
    private long _identity = -1;
    private JobType _job;
    private bool _child, _drag, _fullBody, _worldPaused;
    private ActionKind _action;
    private ActionPhase _actionPhase;
    private float _yaw = -.28f, _distance = FaceDistance;
    public override void _Ready()
    {
        Stretch = true; CustomMinimumSize = Compact ? new Vector2(0, 204) : new Vector2(270, 196);
        TooltipText = "拖动左右旋转与上下俯仰 · 滚轮缩放 · 双击复位";
        MouseFilter = MouseFilterEnum.Stop;
        _viewport = new SubViewport { Size = Compact ? new Vector2I(624, 408) : new Vector2I(540, 392), OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa4X,
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible, HandleInputLocally = false };
        AddChild(_viewport);
        _stage = new Node3D(); _viewport.AddChild(_stage);
        var environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color("#252d36"), AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color("#d6dce3"), AmbientLightEnergy = .32f, TonemapMode = Godot.Environment.ToneMapper.Aces };
        _stage.AddChild(new WorldEnvironment { Environment = environment });
        _stage.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-35, -35, 0), LightColor = new Color("#f0f3ed"), LightEnergy = .78f });
        _stage.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-15, 145, 0), LightColor = new Color("#b9cadd"), LightEnergy = .36f });
        _portraitGround=new MeshInstance3D { Mesh = new PlaneMesh { Size = new(20,20) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new("#252d36"), Roughness = 1 },Visible=false };_stage.AddChild(_portraitGround);
        _camera = new Camera3D { Current = true, Fov = 34 }; _stage.AddChild(_camera); Aim();
        var controls=new HBoxContainer();controls.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);controls.Position=new(8,8);controls.AddThemeConstantOverride("separation",2);AddChild(controls);
        foreach(var (mode,title) in new[]{(Framing.Face,"面部"),(Framing.Hands,"手部"),(Framing.Body,"全身")})
        {
            var button=new Godot.Button {Text=title,ToggleMode=true,CustomMinimumSize=new(44,28)};HudStyle.Button(button);button.AddThemeFontSizeOverride("font_size",13);
            button.TooltipText="聚焦"+title+" · 保留当前旋转角度";controls.AddChild(button);_frameButtons[mode]=button;
            button.Pressed+=()=>SetFraming(mode);
        }
        _frameButtons[Framing.Face].SetPressedNoSignal(true);
        var reset=new Godot.Button {Text="复位",CustomMinimumSize=new(44,28)};HudStyle.Button(reset);reset.AddThemeFontSizeOverride("font_size",13);reset.TooltipText="恢复当前构图 · 也可双击画面";
        reset.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);reset.OffsetLeft=-52;reset.OffsetRight=-8;reset.OffsetTop=8;reset.OffsetBottom=36;reset.Pressed+=ResetView;AddChild(reset);
        var caption = HudStyle.Label("拖动旋转与俯仰 · 滚轮缩放 · 双击复位", 10); caption.Visible=!Compact;
        caption.AddThemeColorOverride("font_color", new Color("#aab2bc")); caption.MouseFilter = MouseFilterEnum.Ignore;
        caption.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide); caption.OffsetTop = -22; caption.OffsetLeft = 10; AddChild(caption);
    }
    public void ShowResident(long identity, int slot, bool child, JobType job)
    {
        if (identity == _identity && child == _child && job == _job) { return; }
        _resident?.QueueFree(); _identity = identity; _child = child; _job = job;
        _resident = _models.Resident(slot, child, job); _stage.AddChild(_resident);
        _resident.Rotation = new Vector3(0, _yaw, 0); Aim();
    }
    public bool FinishingGesture=>_resident?.CompletionVisible==true;
    public void ShowCargo(bool hasCargo, SandBoxSim.Core.Environment.ResourceKind resource) => _resident?.ShowCargo(hasCargo,resource);
    public void ShowActivity(ActionKind action,ActionPhase phase,bool paused) { _action=action;_actionPhase=phase;_worldPaused=paused; }
    internal object EvaluationState()=>new {framing=_framing.ToString().ToLowerInvariant(),yaw=_yaw,pitch=_pitch,distance=_distance,dragging=_drag};
    internal void EvaluationFraming(string mode)=>SetFraming(mode=="hands"?Framing.Hands:mode=="body"?Framing.Body:Framing.Face);
    internal void EvaluationReset()=>ResetView();
    public void FocusHands()=>SetFraming(Framing.Hands);
    private void SetFraming(Framing mode)
    {
        _framing=mode;_fullBody=mode==Framing.Body;_portraitGround.Visible=_fullBody;
        _distance=mode==Framing.Body?3.5f:mode==Framing.Hands?.68f:FaceDistance;
        foreach(var pair in _frameButtons)pair.Value.SetPressedNoSignal(pair.Key==mode);
        if(!HudStyle.MotionEnabled)_viewDistance=_distance;
    }
    private void ResetView()
    {
        _yaw=-.28f;_pitch=0;_drag=false;SetFraming(_framing);
        if(!HudStyle.MotionEnabled&&_resident!=null)_resident.Rotation=new(0,_yaw,0);
        Aim();
    }
    private void Aim(float delta=0)
    {
        var center = _fullBody ? new Vector3(0,_child?.62f:1.0f,0) : _resident != null ? _resident.Head.GlobalPosition - new Vector3(0, Compact ? .055f : .19f, 0) : new Vector3(0, 1.4f, 0);
        if(_framing==Framing.Hands&&_resident!=null)center=(_resident.Hands[0].GlobalPosition+_resident.Hands[1].GlobalPosition)*.5f+new Vector3(0,-.03f,0);
        _viewCenter=!_centerReady||delta==0||!HudStyle.MotionEnabled?center:_viewCenter.Lerp(center,1-MathF.Exp(-delta*9));_centerReady=true;
        _camera.Position = _viewCenter + new Vector3(0, .035f+MathF.Sin(_pitch)*_viewDistance, -MathF.Cos(_pitch)*_viewDistance);
        _camera.LookAt(_viewCenter);
    }
    public override void _Process(double delta)
    {
        delta=EvaluationClock.Delta(delta);
        // Mirrors the selected real action; this isolated model never advances the simulation.
        if (IsVisibleInTree() && _resident != null)
        {
            _resident.PresentAction((float)delta, _action, _actionPhase, _worldPaused, (float)(_identity % 11));
            _resident.Rotation = new Vector3(0, HudStyle.MotionEnabled?Mathf.LerpAngle(_resident.Rotation.Y, _yaw, 1 - MathF.Exp(-(float)delta * 12)):_yaw, 0);
            _viewDistance=HudStyle.MotionEnabled?Mathf.Lerp(_viewDistance,_distance,1-MathF.Exp(-(float)delta*10)):_distance;Aim((float)delta);
        }
    }
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton button)
        {
            if(button.ButtonIndex==MouseButton.Left&&button.Pressed&&button.DoubleClick){ResetView();AcceptEvent();return;}
            if (button.ButtonIndex == MouseButton.Left) { _drag = button.Pressed; }
            if (button.Pressed && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            { _distance = Math.Clamp(_distance + (button.ButtonIndex == MouseButton.WheelUp ? -.2f : .2f), .50f, 3.8f); Aim(); }
            AcceptEvent();
        }
        if (input is InputEventMouseMotion motion && _drag && _resident != null)
        { _yaw += motion.Relative.X * .012f;_pitch=Math.Clamp(_pitch+motion.Relative.Y*.008f,-.45f,.60f); AcceptEvent(); }
    }
    public override void _Input(InputEvent input)
    { if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } || input is InputEventMouseMotion { ButtonMask: 0 }) { _drag = false; } }
    public override void _Notification(int what)
    { if (what == NotificationApplicationFocusOut) { _drag = false; } }
    public void ValidatePresentation()
    {
        if (_resident == null) { throw new InvalidOperationException("Selected resident has no portrait"); }
        var original = _resident;
        ShowResident(_identity, (int)(_identity & uint.MaxValue), _child, _job);
        if (!ReferenceEquals(original, _resident)) { throw new InvalidOperationException("Unchanged portrait recreated its model"); }
        _resident.Pose(.1f, true, false, false, false, 0);
        Transform3D frozen = _resident.Legs[0].Transform;
        _resident.Pose(.5f, false, true, false, true, 0);
        if (_resident.Legs[0].Transform != frozen) { throw new InvalidOperationException("Paused world pose changed"); }
        _GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        _GuiInput(new InputEventMouseMotion { Relative = new Vector2(20, 0) });
        float turned = _yaw;
        _Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        _GuiInput(new InputEventMouseMotion { Relative = new Vector2(20, 0) });
        if (_yaw != turned) { throw new InvalidOperationException("Portrait drag continued after global release"); }
        float beforeZoom=_distance;
        _GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true });
        if (_distance >= beforeZoom) { throw new InvalidOperationException("Portrait zoom failed"); }
        _GuiInput(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=true});
        _GuiInput(new InputEventMouseMotion {Relative=new(0,30)});
        if(_pitch<=0)throw new InvalidOperationException("Portrait pitch failed");
        SetFraming(Framing.Hands);if(_fullBody||_distance>=.85f)throw new InvalidOperationException("Hand framing failed");
        _GuiInput(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=true,DoubleClick=true});
        if(_pitch!=0||_drag)throw new InvalidOperationException("Portrait reset did not release orbit");
        SetFraming(Framing.Face);_viewDistance=FaceDistance;
        _distance = FaceDistance; _yaw = -.28f; _resident.Rotation = new Vector3(0, _yaw, 0); Aim();
    }
}
