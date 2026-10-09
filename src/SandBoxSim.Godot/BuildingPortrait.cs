using Godot;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;

/// <summary>Real architecture in an isolated display world; no simulation or random state is consumed.</summary>
internal partial class BuildingPortrait : SubViewportContainer
{
    private BuildingKind _kind;
    private Node3D? _stage;
    public BuildingKind Kind { get => _kind; set { if (_kind == value) return; _kind = value; if (_stage != null) Rebuild(); } }
    public bool Highlighted { get; set; }
    private SubViewport _viewport = null!;
    private Node3D _model = null!;
    private float _angle = -.48f;
    private bool _wasVisible;
    public override void _Ready()
    {
        Stretch = true; MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(0, Mathf.Max(CustomMinimumSize.Y, 92));
        _viewport = new SubViewport { Size = new Vector2I(288, 184), OwnWorld3D = true,
            TransparentBg = true, HandleInputLocally = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Once };
        AddChild(_viewport);
        var stage = new Node3D(); _stage = stage; _viewport.AddChild(stage);
        stage.AddChild(new WorldEnvironment { Environment = new Godot.Environment {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0, 0, 0, 0),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color("#c2c9bd"), AmbientLightEnergy = .38f,
            TonemapMode = Godot.Environment.ToneMapper.Aces } });
        stage.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-45, -35, 0),
            LightColor = new Color("#f3eee5"), LightEnergy = .95f });
        stage.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-25, 145, 0),
            LightColor = new Color("#a8b5c3"), LightEnergy = .5f });
        Rebuild();
        var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 4.05f,
            Current = true, Position = new Vector3(0, 3.4f, 6.2f) };
        stage.AddChild(camera); camera.LookAt(new Vector3(0, 1.2f, 0));
    }
    private void Rebuild()
    {
        if (_model != null) { _stage!.RemoveChild(_model); _model.QueueFree(); }
        _model = new NatureModels().Building(Kind, true, 1); _stage!.AddChild(_model);
        _model.Rotation = new Vector3(0, _angle, 0);
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }
    public override void _Process(double delta)
    {
        bool visible = IsVisibleInTree();
        if (visible && !_wasVisible) _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        _wasVisible = visible;
        if (!visible) return;
        float previous = _angle;
        _angle = Mathf.LerpAngle(_angle, Highlighted ? .15f : -.48f, 1 - Mathf.Exp(-(float)delta * 3));
        _model.Rotation = new Vector3(0, _angle, 0);
        if (Mathf.Abs(_angle - previous) > .00001f) _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }
}
