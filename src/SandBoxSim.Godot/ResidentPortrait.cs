using System;
using System.Linq;
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
    public event Action? ExpandRequested;
    private Godot.Button _expandButton=null!;
    private HBoxContainer _expandedControls=null!;
    private Label _gestureHint=null!;
    private Tween? _hintTransition;
    private void Hint(float alpha)
    {
        _hintTransition?.Kill();
        if(!HudStyle.MotionEnabled){_gestureHint.Modulate=new Color(1,1,1,alpha);return;}
        _hintTransition=_gestureHint.CreateTween();_hintTransition.TweenProperty(_gestureHint,"modulate:a",alpha,.16);
    }
    internal void SettleMotion()
    {
        _hintTransition?.Kill();_gestureHint.Modulate=new Color(1,1,1,0);
        _viewDistance=_distance;
        if(_resident!=null)_resident.Rotation=new(0,_yaw,0);
        Aim();
    }
    internal bool Expanded {get;private set;}
    internal void SetExpanded(bool expanded)
    {
        Expanded=expanded;_drag=false;
        CustomMinimumSize=expanded?new Vector2(270,400):Compact?new Vector2(0,256):new Vector2(270,224);
        SizeFlagsVertical=expanded?SizeFlags.ExpandFill:SizeFlags.Fill;
        _expandButton.Visible=!expanded;
        _expandedControls.Visible=expanded;
    }
    private readonly NatureModels _models = new();
    private SubViewport _viewport = null!;
    private Node3D _stage = null!;
    private MeshInstance3D _portraitGround=null!;
    private Camera3D _camera = null!;
    private DirectionalLight3D _keyLight=null!;
    private ResidentRig? _resident;
    private long _identity = -1;
    private JobType _job;
    private bool _child, _drag, _fullBody, _worldPaused;
    private ActionKind _action;
    private ActionPhase _actionPhase;
    private float _yaw = -.28f, _distance = FaceDistance;
    public override void _Ready()
    {
        Stretch = true; CustomMinimumSize = Compact ? new Vector2(0, 256) : new Vector2(270, 224);
        TooltipText = "拖动左右旋转与上下俯仰 · 滚轮缩放 · 双击复位";
        MouseFilter = MouseFilterEnum.Stop;FocusMode=FocusModeEnum.All;
        _viewport = new SubViewport { Size = Compact ? new Vector2I(624, 512) : new Vector2I(540, 448), OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa4X,
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible, HandleInputLocally = false };
        AddChild(_viewport);
        _stage = new Node3D(); _viewport.AddChild(_stage);
        var environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color("#202933"), AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color("#cbd0d5"), AmbientLightEnergy = .36f, TonemapMode = Godot.Environment.ToneMapper.Aces };
        _stage.AddChild(new WorldEnvironment { Environment = environment });
        var backdrop=new ShaderMaterial {Shader=new Shader {Code=@"shader_type spatial;
render_mode unshaded,cull_disabled,fog_disabled;
void fragment(){float halo=exp(-dot((UV-vec2(.48,.56))*vec2(1.4,1.0),(UV-vec2(.48,.56))*vec2(1.4,1.0))*30.0);
ALBEDO=mix(vec3(.035,.047,.065),vec3(.135,.17,.20),halo);}"}};
        _stage.AddChild(new MeshInstance3D {Mesh=new QuadMesh {Size=new(9,9)},Position=new(0,1.1f,2),MaterialOverride=backdrop,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        _keyLight=new DirectionalLight3D { RotationDegrees = new Vector3(-32, -38, 0), LightColor = new Color("#f1e5d6"), LightEnergy = .85f,
            ShadowEnabled=false,DirectionalShadowMode=DirectionalLight3D.ShadowMode.Orthogonal,DirectionalShadowMaxDistance=6,ShadowBias=.05f,ShadowNormalBias=.2f };
        _stage.AddChild(_keyLight);
        _stage.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-8, 70, 0), LightColor = new Color("#c5d3df"), LightEnergy = .30f });
        _stage.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-28, -150, 0), LightColor = new Color("#d7e6ef"), LightEnergy = .28f });
        _portraitGround=new MeshInstance3D { Mesh = new CylinderMesh {TopRadius=.62f,BottomRadius=.62f,Height=.004f,RadialSegments=64},Position=new(0,-.006f,0),
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,MaterialOverride = new StandardMaterial3D { AlbedoColor = new("#293930"), Roughness = 1 },Visible=false };_stage.AddChild(_portraitGround);
        _camera = new Camera3D { Current = true, Fov = 34 }; _stage.AddChild(_camera); Aim();
        var controlPlate=new PanelContainer();
        controlPlate.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        controlPlate.OffsetLeft=10;controlPlate.OffsetRight=-10;controlPlate.OffsetTop=-57;controlPlate.OffsetBottom=-10;
        controlPlate.AddThemeStyleboxOverride("panel",HudStyle.Box(new Color(HudStyle.Ink,.96f),8,6));AddChild(controlPlate);
        var controls=new HBoxContainer();controls.AddThemeConstantOverride("separation",3);controlPlate.AddChild(controls);
        Godot.Button StudioButton(string title,string hint,Action action,int width=42)
        {
            var button=new Godot.Button {Text=title,CustomMinimumSize=new(width,32)};HudStyle.Button(button);
            button.AddThemeFontSizeOverride("font_size",12);button.TooltipText=hint;controls.AddChild(button);button.Pressed+=action;return button;
        }
        foreach(var (mode,title) in new[]{(Framing.Face,"面部"),(Framing.Hands,"手部"),(Framing.Body,"全身")})
        {
            var button=StudioButton(title,"聚焦"+title+" · 保留当前旋转角度",()=>SetFraming(mode));
            button.ToggleMode=true;_frameButtons[mode]=button;
        }
        _frameButtons[Framing.Face].SetPressedNoSignal(true);
        controls.AddChild(new Control {SizeFlagsHorizontal=SizeFlags.ExpandFill,MouseFilter=MouseFilterEnum.Ignore});
        _expandedControls=new HBoxContainer {Visible=Expanded};_expandedControls.AddThemeConstantOverride("separation",3);controls.AddChild(_expandedControls);
        foreach(var (title,angle) in new[]{("正面",0f),("三刻",-.65f),("背面",MathF.PI)})
        {
            var preset=new Godot.Button {Text=title,CustomMinimumSize=new(46,32)};HudStyle.Button(preset);preset.AddThemeFontSizeOverride("font_size",12);
            preset.TooltipText="切换观察角度，保留聚焦和缩放";_expandedControls.AddChild(preset);preset.Pressed+=()=>SetAngle(angle);
        }
        foreach(var (title,step) in new[]{("−",.15f),("+",-.15f)})
        {
            var zoom=new Godot.Button {Text=title,CustomMinimumSize=new(32,32)};HudStyle.Button(zoom);zoom.TooltipText=step<0?"拉近模型":"拉远模型";
            _expandedControls.AddChild(zoom);zoom.Pressed+=()=>ZoomBy(step);
        }
        StudioButton("复位","恢复当前构图 · 也可双击画面",ResetView);
        _expandButton=StudioButton("放大","打开独立人物观察窗",()=>ExpandRequested?.Invoke());_expandButton.Visible=!Expanded;
        var caption=_gestureHint=HudStyle.Label("拖动旋转 · 滚轮缩放 · 双击复位",11,true);
        caption.MouseFilter=MouseFilterEnum.Ignore;caption.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        caption.OffsetTop=12;caption.OffsetBottom=34;caption.OffsetLeft=14;AddChild(caption);caption.Modulate=new Color(1,1,1,0);
        MouseEntered+=()=>Hint(1);MouseExited+=()=>Hint(0);
    }
    private void ZoomBy(float amount){_distance=Math.Clamp(_distance+amount,.50f,3.8f);Aim();}
    private void SetAngle(float angle)
    {
        _yaw=angle;_pitch=0;_drag=false;
        if(!HudStyle.MotionEnabled&&_resident!=null)_resident.Rotation=new(0,_yaw,0);
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
    internal object RenderQualityState()
    {
        var environment=_stage.GetChildren().OfType<WorldEnvironment>().Single().Environment;
        return new {studio="graphite-softbox-v3",backdrop="radial-unlit-v1",msaa=_viewport.Msaa3D.ToString(),resolution=new[]{_viewport.Size.X,_viewport.Size.Y},
            background=environment.BackgroundColor.ToHtml(),ambient=environment.AmbientLightColor.ToHtml(),ambientEnergy=environment.AmbientLightEnergy,
            lights=_stage.GetChildren().OfType<DirectionalLight3D>().Select(light=>new {
                color=light.LightColor.ToHtml(),energy=light.LightEnergy,rotation=new[]{light.RotationDegrees.X,light.RotationDegrees.Y,light.RotationDegrees.Z},shadows=light.ShadowEnabled,
                shadowMode=light.DirectionalShadowMode.ToString(),shadowDistance=light.DirectionalShadowMaxDistance,bias=light.ShadowBias,normalBias=light.ShadowNormalBias}).ToArray()};
    }
    internal void EvaluationFraming(string mode)=>SetFraming(mode=="hands"?Framing.Hands:mode=="body"?Framing.Body:Framing.Face);
    internal void EvaluationReset()=>ResetView();
    public void FocusHands()=>SetFraming(Framing.Hands);
    private void SetFraming(Framing mode)
    {
        _framing=mode;_fullBody=mode==Framing.Body;_portraitGround.Visible=_fullBody;
        // Close detail framing avoids unstable submillimetre shadow-map facets;
        // the full-body studio retains the ground contact shadow.
        _keyLight.ShadowEnabled=_fullBody;
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
        var center = _fullBody ? new Vector3(0,_child?.55f:.88f,0) : _resident != null ? _resident.Head.GlobalPosition - new Vector3(0, Compact ? .055f : .19f, 0) : new Vector3(0, 1.4f, 0);
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
            { ZoomBy(button.ButtonIndex == MouseButton.WheelUp ? -.2f : .2f); }
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
        bool motion=HudStyle.MotionEnabled;
        try
        {
            HudStyle.MotionEnabled=true;Hint(1);_hintTransition!.CustomStep(.08);
            HudStyle.MotionEnabled=false;SettleMotion();
            if(_hintTransition.IsRunning()||_gestureHint.Modulate.A!=0||_viewDistance!=_distance)
                throw new InvalidOperationException("Reduced motion left portrait interpolation or hint running");
        }
        finally{HudStyle.MotionEnabled=motion;}
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
        if(_keyLight.ShadowEnabled)throw new InvalidOperationException("Detail studio left coarse self-shadows enabled");
        SetFraming(Framing.Body);if(!_keyLight.ShadowEnabled)throw new InvalidOperationException("Body studio lost its contact shadow");SetFraming(Framing.Hands);
        float handDistance=_distance;SetAngle(MathF.PI);
        if(_distance!=handDistance||_framing!=Framing.Hands||_drag||_yaw!=MathF.PI)throw new InvalidOperationException("Angle preset reset the inspected detail or continued a gesture");
        _GuiInput(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=true,DoubleClick=true});
        if(_pitch!=0||_drag)throw new InvalidOperationException("Portrait reset did not release orbit");
        SetFraming(Framing.Face);_viewDistance=FaceDistance;
        _distance = FaceDistance; _yaw = -.28f; _resident.Rotation = new Vector3(0, _yaw, 0); Aim();
    }
}
