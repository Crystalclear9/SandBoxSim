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
    private const float FaceDistance=1.0f;
    private const float ThumbnailYaw=-.28f;
    private float _pitch,_viewDistance=FaceDistance;
    private Vector3 _viewCenter;
    private bool _centerReady;
    public bool Compact { get; set; }
    internal bool NotebookThumbnail { get; set; }
    private bool IsThumbnail=>Compact&&NotebookThumbnail&&!Expanded;
    private PanelContainer _controlPlate=null!;
    public event Action? ExpandRequested;
    private Godot.Button _expandButton=null!;
    private Label _gestureHint=null!;
    private Tween? _hintTransition;
    private void Hint(float alpha)
    {
        _hintTransition?.Kill();
        if(IsThumbnail){_gestureHint.Modulate=new Color(1,1,1,0);return;}
        if(!HudStyle.MotionEnabled){_gestureHint.Modulate=new Color(1,1,1,alpha);return;}
        _hintTransition=_gestureHint.CreateTween();_hintTransition.TweenProperty(_gestureHint,"modulate:a",alpha,.16);
    }
    internal void SettleMotion()
    {
        SetOrbiting(false);
        _hintTransition?.Kill();_gestureHint.Modulate=new Color(1,1,1,0);
        _viewDistance=_distance;
        if(_resident!=null)_resident.Rotation=new(0,IsThumbnail?ThumbnailYaw:_yaw,0);
        Aim();
    }
    internal bool Expanded {get;private set;}
    internal void SetExpanded(bool expanded)
    {
        Expanded=expanded;_drag=false;if(!expanded)SetOrbiting(false);
        CustomMinimumSize=expanded?new Vector2(270,400):Compact?new Vector2(0,IsThumbnail?138:224):new Vector2(270,224);
        SizeFlagsVertical=expanded?SizeFlags.ExpandFill:SizeFlags.Fill;
        _expandButton.Visible=!expanded&&!IsThumbnail;
        SetStudioControlsExpanded(expanded);
        _controlPlate.Visible=!IsThumbnail;
        _gestureHint.Visible=!IsThumbnail;
        FocusMode=IsThumbnail?FocusModeEnum.None:FocusModeEnum.All;
        MouseFilter=IsThumbnail?MouseFilterEnum.Ignore:MouseFilterEnum.Stop;
        if(_camera!=null)
        {
            ApplyFramingDisplay();
            if(_resident!=null)_resident.Rotation=new(0,IsThumbnail?ThumbnailYaw:_yaw,0);
            Aim();
        }
    }
    private readonly NatureModels _models = new();
    private SubViewport _viewport = null!;
    private Node3D _stage = null!;
    private MeshInstance3D _portraitGround=null!;
    private Camera3D _camera = null!;
    private DirectionalLight3D _keyLight=null!;
    private DirectionalLight3D _fillLight=null!,_rimLight=null!;
    internal int Lighting {get;private set;}
    private ResidentRig? _resident;
    private long _identity = -1;
    private JobType _job;
    private bool _child, _drag, _fullBody, _worldPaused;
    private ActionKind _action;
    private ActionPhase _actionPhase;
    private float _yaw = -.28f, _distance = FaceDistance;
    public override void _Ready()
    {
        Stretch = true; CustomMinimumSize = Compact ? new Vector2(0,IsThumbnail?138:224) : new Vector2(270, 224);
        TooltipText = "拖动旋转与俯仰 · 滚轮缩放 · Shift + 滚轮精调 · 双击复位";
        MouseFilter = MouseFilterEnum.Stop;FocusMode=FocusModeEnum.All;
        _viewport = new SubViewport { Size = Compact ? new Vector2I(624, 512) : new Vector2I(540, 448), OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa4X,
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible, HandleInputLocally = false };
        AddChild(_viewport);
        _stage = new Node3D(); _viewport.AddChild(_stage);
        var environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color("#252d2d"), AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color("#d0d2d3"), AmbientLightEnergy = .25f, TonemapMode = Godot.Environment.ToneMapper.Aces };
        _stage.AddChild(new WorldEnvironment { Environment = environment });
        var backdrop=new ShaderMaterial {Shader=new Shader {Code=@"shader_type spatial;
render_mode unshaded,cull_disabled,fog_disabled;
void fragment(){float halo=exp(-dot((UV-vec2(.48,.56))*vec2(1.4,1.0),(UV-vec2(.48,.56))*vec2(1.4,1.0))*30.0);
ALBEDO=mix(vec3(.075,.092,.095),vec3(.27,.305,.29),halo);}"}};
        _stage.AddChild(new MeshInstance3D {Mesh=new QuadMesh {Size=new(9,9)},Position=new(0,1.1f,2),MaterialOverride=backdrop,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        _keyLight=new DirectionalLight3D { RotationDegrees = new Vector3(-32, -38, 0), LightColor = new Color("#f3efe8"), LightEnergy = .72f,
            ShadowEnabled=false,DirectionalShadowMode=DirectionalLight3D.ShadowMode.Orthogonal,DirectionalShadowMaxDistance=6,ShadowBias=.05f,ShadowNormalBias=.2f };
        _stage.AddChild(_keyLight);
        _fillLight=new DirectionalLight3D { RotationDegrees = new Vector3(-8, 70, 0), LightColor = new Color("#c5d3df"), LightEnergy = .42f };_stage.AddChild(_fillLight);
        _rimLight=new DirectionalLight3D { RotationDegrees = new Vector3(-28, -150, 0), LightColor = new Color("#d7e6ef"), LightEnergy = .32f };_stage.AddChild(_rimLight);
        SetLighting(0);
        _portraitGround=new MeshInstance3D { Mesh = new CylinderMesh {TopRadius=.62f,BottomRadius=.62f,Height=.004f,RadialSegments=64},Position=new(0,-.006f,0),
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,MaterialOverride = new StandardMaterial3D { AlbedoColor = new("#293930"), Roughness = 1 },Visible=false };_stage.AddChild(_portraitGround);
        _camera = new Camera3D { Current = true, Fov = 26 }; _stage.AddChild(_camera); Aim();
        BuildStudioControls();
        var caption=_gestureHint=HudStyle.Label("拖动旋转 · 滚轮缩放 · Shift 精调 · 双击复位",11,true);
        caption.MouseFilter=MouseFilterEnum.Ignore;caption.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        caption.OffsetTop=12;caption.OffsetBottom=34;caption.OffsetLeft=14;AddChild(caption);caption.Modulate=new Color(1,1,1,0);
        MouseEntered+=()=>Hint(1);MouseExited+=()=>Hint(0);
        SetExpanded(Expanded);
    }
    private void ZoomBy(float amount)=>SetDistance(_distance*MathF.Exp(amount));
    private void SetAngle(float angle)
    {
        SetOrbiting(false);_yaw=angle;_pitch=0;_drag=false;UpdateStudioReadout();
        if(!HudStyle.MotionEnabled&&_resident!=null)_resident.Rotation=new(0,_yaw,0);
    }
    public void ShowResident(long identity, int slot, bool child, JobType job)
    {
        if (identity == _identity && child == _child && job == _job) { return; }
        SetOrbiting(false);
        _resident?.QueueFree(); _identity = identity; _child = child; _job = job;
        _resident = _models.Resident(slot, child, job); _stage.AddChild(_resident);
        _resident.Rotation = new Vector3(0, IsThumbnail?ThumbnailYaw:_yaw, 0); Aim();
    }
    public bool FinishingGesture=>_resident?.CompletionVisible==true;
    public void ShowCargo(bool hasCargo, SandBoxSim.Core.Environment.ResourceKind resource) => _resident?.ShowCargo(hasCargo,resource);
    public void ShowActivity(ActionKind action,ActionPhase phase,bool paused) { _action=action;_actionPhase=phase;_worldPaused=paused; }
    internal object EvaluationState()=>new {framing=_framing.ToString().ToLowerInvariant(),yaw=_yaw,pitch=_pitch,distance=_distance,dragging=_drag,orbiting=_orbiting,lighting=Lighting==0?"soft":"raking",thumbnail=IsThumbnail};
    internal object InspectionState()=>new {framing=_framing.ToString().ToLowerInvariant(),yaw=_yaw,pitch=_pitch,distance=_distance,dragging=_drag,orbiting=_orbiting,lighting=Lighting==0?"soft":"raking"};
    internal void SetLighting(int mode)
    {
        Lighting=Math.Clamp(mode,0,1);
        _keyLight.RotationDegrees=Lighting==0?new(-28,-38,0):new(-18,-66,0);
        _keyLight.LightEnergy=Lighting==0?.76f:.88f;
        _fillLight.LightEnergy=Lighting==0?.23f:.15f;_rimLight.LightEnergy=Lighting==0?.24f:.30f;
    }
    internal object RenderQualityState()
    {
        var environment=_stage.GetChildren().OfType<WorldEnvironment>().Single().Environment;
        return new {studio="continuous-inspector-v9",backdrop="natural-radial-v2",thumbnail=IsThumbnail,thumbnailFraming=IsThumbnail?"face":null,lensFov=_camera.Fov,msaa=_viewport.Msaa3D.ToString(),resolution=new[]{_viewport.Size.X,_viewport.Size.Y},
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
        _framing=mode;_fullBody=mode==Framing.Body;
        // Close detail framing avoids unstable submillimetre shadow-map facets;
        // the full-body studio retains the ground contact shadow.
        // Leave room below the soles for the grouped camera toolbar, including
        // the minimum supported window and the transition from a close detail.
        _distance=mode==Framing.Body?4.1f:mode==Framing.Hands?.68f:FaceDistance;
        ApplyFramingDisplay();
        foreach(var pair in _frameButtons)pair.Value.SetPressedNoSignal(pair.Key==mode);
        if(!HudStyle.MotionEnabled)_viewDistance=_distance;
        UpdateStudioReadout();
    }
    private void ApplyFramingDisplay()
    {
        // The notebook entry identifies a person. Inspecting their hands or
        // back must not turn that entry into a crop of their waist or hair.
        // Keep the user's inspection state and override only its presentation.
        _portraitGround.Visible=!IsThumbnail&&_fullBody;
        _keyLight.ShadowEnabled=!IsThumbnail&&_fullBody;
        _camera.Fov=IsThumbnail||_framing==Framing.Face?26:34;
    }
    private void ResetView()
    {
        SetOrbiting(false);
        _yaw=-.28f;_pitch=0;_drag=false;SetFraming(_framing);
        if(!HudStyle.MotionEnabled&&_resident!=null)_resident.Rotation=new(0,IsThumbnail?ThumbnailYaw:_yaw,0);
        Aim();
    }
    private void Aim(float delta=0)
    {
        var center = _fullBody&&!IsThumbnail ? new Vector3(0,_child?.50f:.80f,0) : _resident != null ? _resident.Head.GlobalPosition - new Vector3(0, Compact ? .055f : .19f, 0) : new Vector3(0, 1.4f, 0);
        if(_framing==Framing.Hands&&!IsThumbnail&&_resident!=null)center=(_resident.Hands[0].GlobalPosition+_resident.Hands[1].GlobalPosition)*.5f+new Vector3(0,-.03f,0);
        _viewCenter=!_centerReady||delta==0||!HudStyle.MotionEnabled?center:_viewCenter.Lerp(center,1-MathF.Exp(-delta*9));_centerReady=true;
        float shownDistance=IsThumbnail?FaceDistance*.9f:_viewDistance;
        float shownPitch=IsThumbnail?0:_pitch;
        _camera.Position = _viewCenter + new Vector3(0, .035f+MathF.Sin(shownPitch)*shownDistance, -MathF.Cos(shownPitch)*shownDistance);
        _camera.LookAt(_viewCenter);
    }
    public override void _Process(double delta)
    {
        delta=EvaluationClock.Delta(delta);
        AdvanceOrbit((float)delta);
        // Mirrors the selected real action; this isolated model never advances the simulation.
        if (IsVisibleInTree() && _resident != null)
        {
            _resident.PresentAction((float)delta, _action, _actionPhase, _worldPaused, (float)(_identity % 11));
            float shownYaw=IsThumbnail?ThumbnailYaw:_yaw;
            _resident.Rotation = new Vector3(0, HudStyle.MotionEnabled?Mathf.LerpAngle(_resident.Rotation.Y, shownYaw, 1 - MathF.Exp(-(float)delta * 12)):shownYaw, 0);
            _viewDistance=HudStyle.MotionEnabled?Mathf.Lerp(_viewDistance,_distance,1-MathF.Exp(-(float)delta*10)):_distance;Aim((float)delta);
        }
    }
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton button)
        {
            if(button.ButtonIndex==MouseButton.Left&&button.Pressed&&button.DoubleClick){ResetView();AcceptEvent();return;}
            if (button.ButtonIndex == MouseButton.Left) { _drag = button.Pressed;if(button.Pressed)SetOrbiting(false); }
            if (button.Pressed && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            { ZoomBy((button.ButtonIndex == MouseButton.WheelUp ? -1 : 1)*(button.ShiftPressed?.03f:.10f)); }
            AcceptEvent();
        }
        if (input is InputEventMouseMotion motion && _drag && _resident != null)
        { _yaw += motion.Relative.X * .012f;_pitch=Math.Clamp(_pitch+motion.Relative.Y*.008f,-.45f,.60f); UpdateStudioReadout();AcceptEvent(); }
    }
    public override void _Input(InputEvent input)
    { if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } || input is InputEventMouseMotion { ButtonMask: 0 }) { _drag = false; } }
    public override void _Notification(int what)
    { if (what == NotificationApplicationFocusOut) { _drag = false;SetOrbiting(false); } }
    public void ValidatePresentation()
    {
        if (_resident == null) { throw new InvalidOperationException("Selected resident has no portrait"); }
        int lighting=Lighting;float yaw=_yaw,distance=_distance;var frame=_framing;var model=_resident;var key=_keyLight.Rotation;
        SetLighting(1);
        if(_keyLight.Rotation==key||_fillLight.LightEnergy>=.3f||_yaw!=yaw||_distance!=distance||_framing!=frame||model!=_resident)throw new InvalidOperationException("Studio lighting does not change real lights or resets the inspected model/view");
        SetLighting(lighting);
        bool motion=HudStyle.MotionEnabled;
        try
        {
            HudStyle.MotionEnabled=true;Hint(1);
            if(IsThumbnail)
            {
                if(_gestureHint.Modulate.A!=0||_hintTransition?.IsRunning()==true||_controlPlate.Visible||FocusMode!=FocusModeEnum.None||MouseFilter!=MouseFilterEnum.Ignore)
                    throw new InvalidOperationException("Notebook thumbnail exposes hidden studio controls or hint motion");
            }
            else _hintTransition!.CustomStep(.08);
            HudStyle.MotionEnabled=false;SettleMotion();
            if(_hintTransition?.IsRunning()==true||_gestureHint.Modulate.A!=0||_viewDistance!=_distance)
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
        bool expanded=Expanded;SetExpanded(true);
        _GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        _GuiInput(new InputEventMouseMotion { Relative = new Vector2(20, 0) });
        float turned = _yaw;
        _Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        _GuiInput(new InputEventMouseMotion { Relative = new Vector2(20, 0) });
        if (_yaw != turned) { throw new InvalidOperationException("Portrait drag continued after global release"); }
        float beforeZoom=_distance;
        _GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true });
        if (_distance >= beforeZoom) { throw new InvalidOperationException("Portrait zoom failed"); }
        float normalZoom=beforeZoom-_distance;_distance=beforeZoom;
        _GuiInput(new InputEventMouseButton {ButtonIndex=MouseButton.WheelUp,Pressed=true,ShiftPressed=true});
        if(beforeZoom-_distance<=0||beforeZoom-_distance>=normalZoom)throw new InvalidOperationException("Shift wheel does not refine the portrait zoom step");
        _GuiInput(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=true});
        _GuiInput(new InputEventMouseMotion {Relative=new(0,30)});
        if(_pitch<=0)throw new InvalidOperationException("Portrait pitch failed");
        SetFraming(Framing.Hands);if(_fullBody||_distance>=.85f)throw new InvalidOperationException("Hand framing failed");
        if(_keyLight.ShadowEnabled)throw new InvalidOperationException("Detail studio left coarse self-shadows enabled");
        SetFraming(Framing.Body);if(!_keyLight.ShadowEnabled)throw new InvalidOperationException("Body studio lost its contact shadow");SetFraming(Framing.Hands);
        float handDistance=_distance;SetAngle(MathF.PI);
        if(_distance!=handDistance||_framing!=Framing.Hands||_drag||_yaw!=MathF.PI||!_angleButtons[MathF.PI].ButtonPressed)throw new InvalidOperationException("Angle preset reset the inspected detail, lost its state or continued a gesture");
        _GuiInput(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=true});
        _GuiInput(new InputEventMouseMotion {Relative=new(10,0)});
        if(_angleButtons.Values.Any(button=>button.ButtonPressed))throw new InvalidOperationException("Custom orbit retains a misleading preset selection");
        _Input(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=false});
        ZoomBy(-10);if(!_zoomIn.Disabled||_zoomOut.Disabled)throw new InvalidOperationException("Close zoom bound has no disabled control feedback");
        ZoomBy(10);if(!_zoomOut.Disabled||_zoomIn.Disabled)throw new InvalidOperationException("Far zoom bound has no disabled control feedback");
        _GuiInput(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=true,DoubleClick=true});
        if(_pitch!=0||_drag)throw new InvalidOperationException("Portrait reset did not release orbit");
        SetFraming(Framing.Face);_viewDistance=FaceDistance;
        _distance = FaceDistance; _yaw = -.28f; _resident.Rotation = new Vector3(0, _yaw, 0); Aim();
        SetExpanded(expanded);ValidateThumbnailIdentity();
    }
    private void ValidateThumbnailIdentity()
    {
        if(!Compact||!NotebookThumbnail||_resident==null)return;
        bool expanded=Expanded;var framing=_framing;
        float yaw=_yaw,pitch=_pitch,distance=_distance,viewDistance=_viewDistance;
        try
        {
            foreach(var mode in new[]{Framing.Hands,Framing.Body})
            {
                SetExpanded(true);SetFraming(mode);_yaw=2.3f;_pitch=.31f;_distance*=.85f;SettleMotion();
                float inspectionDistance=_distance;var model=_resident;
                SetExpanded(false);SettleMotion();
                var head=_resident.Head.GlobalPosition-new Vector3(0,.055f,0);
                var camera=head+new Vector3(0,.035f,-FaceDistance*.9f);
                if(_camera.Position.DistanceTo(camera)>.00001f||_camera.Fov!=26||MathF.Abs(_resident.Rotation.Y-ThumbnailYaw)>.00001f
                    ||_portraitGround.Visible||_keyLight.ShadowEnabled||_resident!=model)
                    throw new InvalidOperationException("Inspection detail/back view replaces the notebook's identifying face thumbnail");
                SetExpanded(true);SettleMotion();
                if(_framing!=mode||_yaw!=2.3f||_pitch!=.31f||_distance!=inspectionDistance||_camera.Fov!=34
                    ||MathF.Abs(_resident.Rotation.Y-2.3f)>.00001f||_portraitGround.Visible!=(mode==Framing.Body)||_resident!=model)
                    throw new InvalidOperationException("Identity thumbnail resets the saved inspection framing, orbit or zoom");
            }
        }
        finally
        {
            SetFraming(framing);_yaw=yaw;_pitch=pitch;_distance=distance;_viewDistance=viewDistance;
            SetExpanded(expanded);Aim();
        }
        GD.Print("PORTRAIT_IDENTITY_PASS: face-only thumbnail after hand/body/back inspection; reused model and preserved framing/orbit/zoom");
    }
}
