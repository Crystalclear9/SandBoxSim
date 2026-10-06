using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

/// <summary>Isolated studio viewport. Portrait interactions never reach the world camera.</summary>
internal partial class ResidentPortrait : SubViewportContainer
{
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
    private float _yaw = -.28f, _distance = .85f;
    public override void _Ready()
    {
        Stretch = true; CustomMinimumSize = Compact ? new Vector2(0, 144) : new Vector2(270, 196);
        TooltipText = "拖动旋转人物 · 滚轮查看细节";
        MouseFilter = MouseFilterEnum.Stop;
        _viewport = new SubViewport { Size = Compact ? new Vector2I(624, 320) : new Vector2I(540, 392), OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa4X,
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible, HandleInputLocally = false };
        AddChild(_viewport);
        _stage = new Node3D(); _viewport.AddChild(_stage);
        var environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color("#454b40"), AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color("#becbbb"), AmbientLightEnergy = .38f, TonemapMode = Godot.Environment.ToneMapper.Aces };
        _stage.AddChild(new WorldEnvironment { Environment = environment });
        _stage.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-35, -35, 0), LightColor = new Color("#f0f3ed"), LightEnergy = .78f });
        _stage.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-15, 145, 0), LightColor = new Color("#c9ccc0"), LightEnergy = .36f });
        _portraitGround=new MeshInstance3D { Mesh = new PlaneMesh { Size = new(20,20) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new("#454b40"), Roughness = 1 },Visible=false };_stage.AddChild(_portraitGround);
        _camera = new Camera3D { Current = true, Fov = 34 }; _stage.AddChild(_camera); Aim();
        var framing = new Godot.Button { Text = "全身", Flat = true, CustomMinimumSize = new(48,28) }; HudStyle.Button(framing);
        framing.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight); framing.OffsetLeft=-60; framing.OffsetRight=-8; framing.OffsetTop=8; framing.OffsetBottom=36; AddChild(framing);
        framing.Pressed += () => { _fullBody=!_fullBody;_portraitGround.Visible=_fullBody;framing.Text=_fullBody?"面部":"全身";_distance=_fullBody?3.5f:.85f;Aim(); };
        var caption = HudStyle.Label(Compact ? "拖动旋转" : "拖动旋转  /  滚轮查看细节", 10);
        caption.AddThemeColorOverride("font_color", new Color("#b2aa97")); caption.MouseFilter = MouseFilterEnum.Ignore;
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
    private void Aim()
    {
        var center = _fullBody ? new Vector3(0,_child?.62f:1.0f,0) : _resident != null ? _resident.Head.GlobalPosition - new Vector3(0, Compact ? .055f : .19f, 0) : new Vector3(0, 1.4f, 0);
        _camera.Position = center + new Vector3(0, .035f, -_distance);
        _camera.LookAt(center);
    }
    public override void _Process(double delta)
    {
        // Mirrors the selected real action; this isolated model never advances the simulation.
        if (IsVisibleInTree() && _resident != null)
        {
            _resident.PresentAction((float)delta, _action, _actionPhase, _worldPaused, (float)(_identity % 11));
            _resident.Rotation = new Vector3(0, Mathf.LerpAngle(_resident.Rotation.Y, _yaw, 1 - MathF.Exp(-(float)delta * 12)), 0);
        }
    }
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Left) { _drag = button.Pressed; }
            if (button.Pressed && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            { _distance = Math.Clamp(_distance + (button.ButtonIndex == MouseButton.WheelUp ? -.2f : .2f), .50f, 3.8f); Aim(); }
            AcceptEvent();
        }
        if (input is InputEventMouseMotion motion && _drag && _resident != null)
        { _yaw += motion.Relative.X * .012f; AcceptEvent(); }
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
        _GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true });
        if (_distance >= .85f) { throw new InvalidOperationException("Portrait zoom failed"); }
        _distance = .85f; _yaw = -.28f; _resident.Rotation = new Vector3(0, _yaw, 0); Aim();
    }
}
