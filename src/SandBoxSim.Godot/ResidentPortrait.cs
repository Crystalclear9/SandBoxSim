using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

/// <summary>Isolated studio viewport. Portrait interactions never reach the world camera.</summary>
internal partial class ResidentPortrait : SubViewportContainer
{
    private readonly NatureModels _models = new();
    private SubViewport _viewport = null!;
    private Node3D _stage = null!;
    private Camera3D _camera = null!;
    private ResidentRig? _resident;
    private long _identity = -1;
    private JobType _job;
    private bool _child, _drag;
    private float _yaw = -.28f, _distance = 1.7f;
    public override void _Ready()
    {
        Stretch = true; CustomMinimumSize = new Vector2(270, 196);
        TooltipText = "拖动旋转人物 · 滚轮查看细节";
        MouseFilter = MouseFilterEnum.Stop;
        _viewport = new SubViewport { Size = new Vector2I(540, 392), OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible, HandleInputLocally = false };
        AddChild(_viewport);
        _stage = new Node3D(); _viewport.AddChild(_stage);
        var environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color("#252c29"), AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color("#becbbb"), AmbientLightEnergy = .35f };
        _stage.AddChild(new WorldEnvironment { Environment = environment });
        _stage.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-35, -35, 0), LightColor = new Color("#fff1dc"), LightEnergy = 1.5f });
        _stage.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-15, 145, 0), LightColor = new Color("#c8d8cd"), LightEnergy = 1.2f });
        _camera = new Camera3D { Current = true, Fov = 34 }; _stage.AddChild(_camera); Aim();
        var caption = HudStyle.Label("拖动旋转  /  滚轮查看细节", 10);
        caption.AddThemeColorOverride("font_color", new Color("#cabfa8")); caption.MouseFilter = MouseFilterEnum.Ignore;
        caption.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide); caption.OffsetTop = -22; caption.OffsetLeft = 10; AddChild(caption);
    }
    public void ShowResident(long identity, int slot, bool child, JobType job)
    {
        if (identity == _identity && child == _child && job == _job) { return; }
        _resident?.QueueFree(); _identity = identity; _child = child; _job = job;
        _resident = _models.Resident(slot, child, job); _stage.AddChild(_resident);
        _resident.Rotation = new Vector3(0, _yaw, 0); Aim();
    }
    private void Aim()
    {
        var center = new Vector3(0, _child ? .74f : 1.12f, 0);
        _camera.Position = center + new Vector3(0, .13f, -_distance);
        _camera.LookAt(center);
    }
    public override void _Process(double delta)
    {
        // Idle breathing is a portrait presentation, not a simulated action.
        if (IsVisibleInTree() && _resident != null)
        {
            _resident.Pose((float)delta, false, false, false, false, (float)(_identity % 11));
            _resident.Rotation = new Vector3(0, Mathf.LerpAngle(_resident.Rotation.Y, _yaw, 1 - MathF.Exp(-(float)delta * 12)), 0);
        }
    }
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Left) { _drag = button.Pressed; }
            if (button.Pressed && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            { _distance = Math.Clamp(_distance + (button.ButtonIndex == MouseButton.WheelUp ? -.2f : .2f), 1.1f, 3.2f); Aim(); }
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
        if (_distance >= 1.7f) { throw new InvalidOperationException("Portrait zoom failed"); }
        _distance = 1.7f; _yaw = -.28f; _resident.Rotation = new Vector3(0, _yaw, 0); Aim();
    }
}
