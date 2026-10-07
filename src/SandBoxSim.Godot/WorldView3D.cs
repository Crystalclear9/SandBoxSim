using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Systems;

namespace SandBoxSim.Client;

/// <summary>Actual 3D scene with perspective orbit camera and read-only animated visual agents.</summary>
public partial class WorldView3D : MapView
{
    private SubViewport _viewport = null!;
    private SubViewportContainer _container = null!;
    private Node3D _scene = null!, _terrainRoot = null!, _props = null!, _actors = null!;
    private Camera3D _camera = null!;
    private NatureModels _models = null!;
    private Simulation? _world;
    private Vector3 _target = new(92, 1, 100);
    private float _distance = 44, _yaw = -.45f, _pitch = .85f;
    public float CameraYaw => _yaw;
    private bool _orbit, _pan, _painting, _leftHeld, _leftDragged;
    private Vector2 _leftStart;
    private Vector2 _mouse;
    private Vector2I _lastPaint = new(-1, -1);
    private int _follow = -1, _followGeneration;
    private long _terrainSignature, _buildingSignature, _natureSignature;
    private double _poll;
    private int _lastOverlay = -1;
    private readonly Dictionary<long, Node3D> _people = new();
    private readonly Dictionary<long, Node3D> _deer = new();
    private readonly Dictionary<int, Node3D> _wolves = new();
    private readonly Dictionary<long, (bool Child, JobType Job)> _personModels = new();
    private readonly List<(Node3D Node, double Born)> _effects = new();
    private MeshInstance3D _brush = null!, _selection = null!;
    private Godot.Environment _weatherEnvironment = null!;
    private DirectionalLight3D _sunlight = null!;
    private Label _cameraHint = null!;
    private Texture2D? _terrainAtlas;
    private MeshInstance3D _routes = null!;
    private SandBoxSim.Core.Pathing.AStarPathfinder _observerPaths = null!;
    private Int2[] _observedRoute = Array.Empty<Int2>();
    private int[] _density = Array.Empty<int>();
    public bool HasTerrain => _terrainRoot.GetChildCount() > 0;
    public bool HasPerspectiveCamera => _camera.Projection == Camera3D.ProjectionType.Perspective;
    public void ValidateViewControls()
    {
        if (!HasTerrain || !HasPerspectiveCamera || _people.Count != Game.Sim.Agents.LiveCount) { throw new InvalidOperationException("3D scene incomplete"); }
        Vector3 target = _target; float distance = _distance, yaw = _yaw, pitch = _pitch;
        foreach (var view in new[] { "斜视", "俯视", "近景" })
        {
            SetPerspective(view);
            Vector2 screen = _camera.UnprojectPosition(PositionAt(43, 50));
            Vector2I? picked = Pick(screen);
            if (!picked.HasValue || Math.Abs(picked.Value.X - 43) > 1 || Math.Abs(picked.Value.Y - 50) > 1)
                { throw new InvalidOperationException("Camera picking failed in " + view); }
        }
        _yaw += .8f; _distance = 8; UpdateCamera();
        if (_camera.Position.DistanceTo(_target) > 8.1f) { throw new InvalidOperationException("Camera zoom failed"); }
        Game.SelectTool(PlayerTool.Inspect);
        Vector3 beforeDrag = _target;
        _GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(400, 300) });
        _Input(new InputEventMouseMotion { Position = new Vector2(460, 320), Relative = new Vector2(60, 20) });
        _Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(-20, -20) });
        if (_target == beforeDrag || _leftHeld || _painting) { throw new InvalidOperationException("Left drag/release failed"); }
        float beforeYaw = _yaw, beforePitch = _pitch;
        _GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = new Vector2(400, 300) });
        _Input(new InputEventMouseMotion { Position = new Vector2(430, 310), Relative = new Vector2(30, 10) });
        _Input(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = new Vector2(-20, -20) });
        if (_yaw == beforeYaw || _pitch == beforePitch || _orbit) { throw new InvalidOperationException("Orbit/release failed"); }
        float beforeZoom = _distance;
        _GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = new Vector2(400, 300) });
        if (_distance >= beforeZoom) { throw new InvalidOperationException("Mouse wheel zoom failed"); }
        _target = target; _distance = distance; _yaw = yaw; _pitch = pitch; UpdateCamera();
    }
    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusOut) { _orbit = _pan = _painting = _leftHeld = _leftDragged = false; }
    }
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop; ClipContents = true;
        _container = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
        _container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(_container);
        _viewport = new SubViewport { OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Msaa3D = Viewport.Msaa.Msaa2X };
        _container.AddChild(_viewport);
        _scene = new Node3D(); _viewport.AddChild(_scene);
        _terrainRoot = new Node3D(); _scene.AddChild(_terrainRoot);
        _props = new Node3D(); _scene.AddChild(_props);
        _actors = new Node3D(); _scene.AddChild(_actors);
        _models = new NatureModels();
        BuildHazardVisuals();
        if (ResourceLoader.Exists("res://assets/textures/natural-terrain.png")) { _terrainAtlas = GD.Load<Texture2D>("res://assets/textures/natural-terrain.png"); }
        var sky = new ProceduralSkyMaterial { SkyTopColor = new Color("#779ca9"), SkyHorizonColor = new Color("#d4d4b8"), GroundHorizonColor = new Color("#b8c2a0"), GroundBottomColor = new Color("#465346") };
        _weatherEnvironment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky }, AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = .42f, TonemapMode = Godot.Environment.ToneMapper.Aces, TonemapExposure = .72f,
            FogEnabled = true, FogDensity = .0017f, FogLightColor = new Color("#c2d1bd") };
        _scene.AddChild(new WorldEnvironment { Environment = _weatherEnvironment });
        _sunlight = new DirectionalLight3D { RotationDegrees = new Vector3(-48, -35, 0), LightColor = new Color("#ffe8bc"),
            LightEnergy = .9f, ShadowEnabled = true, DirectionalShadowMaxDistance = 160 };
        _scene.AddChild(_sunlight);
        _camera = new Camera3D { Current = true, Near = .15f, Far = 650, Fov = 52 }; _scene.AddChild(_camera);
        _routes = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = HudStyle.Accent, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } }; _scene.AddChild(_routes);
        _brush = Ring(new Color("#e1cf8d"));
        _brush.Mesh = new TorusMesh { InnerRadius = .994f, OuterRadius = 1, Rings = 64, RingSegments = 6 };
        _scene.AddChild(_brush);
        _selection = Ring(new Color("#f7d787")); _scene.AddChild(_selection);
        _cameraHint = new Label { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _cameraHint.AddThemeColorOverride("font_color", new Color("#f6f0d6")); AddChild(_cameraHint);
        _cameraHint.AddThemeFontSizeOverride("font_size", 13); _cameraHint.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, .8f));
        _cameraHint.AddThemeConstantOverride("shadow_offset_x", 1); _cameraHint.AddThemeConstantOverride("shadow_offset_y", 1);
        RebuildWorld(); UpdateCamera();
    }
    public override void _Draw() { }
    public override void Center() { _target = new Vector3(Game.Sim.World.Width, 0, Game.Sim.World.Height); _distance = Math.Max(Game.Sim.World.Width, Game.Sim.World.Height) * 1.6f; _pitch = 1.1f; _follow = -1; UpdateCamera(); }
    public override void Focus(int x, int y, float zoom = 15)
    { _follow = -1; _target = PositionAt(x, y); _distance = Math.Clamp(650 / zoom, 7, 180); _pitch = .8f; UpdateCamera(); }
    public override void Follow(int slot) { _follow = slot; _followGeneration = Game.Sim.Agents.GenerationOf(slot); _distance = MathF.Min(_distance, 14); _pitch = .6f; }
    public void SetPerspective(string view)
    {
        _camera.Projection = Camera3D.ProjectionType.Perspective;
        if (view == "俯视") { _pitch = 1.48f; }
        else if (view == "近景") { _pitch = .48f; _distance = 11; }
        else { _pitch = .9f; _distance = 42; }
        UpdateCamera();
    }
    public override void Effect(int x, int y, Color color, string text = "")
    {
        var root = new Node3D { Position = PositionAt(x, y) + Vector3.Up * .2f }; _scene.AddChild(root);
        var ring = Ring(color); ring.Visible = true; ring.Scale = Vector3.One * 1.5f; root.AddChild(ring);
        ring.Mesh = new TorusMesh { InnerRadius = .99f, OuterRadius = 1, Rings = 48, RingSegments = 6 };
        ring.MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        if (text.Length > 0) { root.AddChild(new Label3D { Text = text, Position = new Vector3(0, 2.2f, 0), Font = Game.Theme.DefaultFont, FontSize = 22,
            PixelSize = .012f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Modulate = color, NoDepthTest = true }); }
        if (_effects.Count >= 6) { _effects[0].Node.QueueFree(); _effects.RemoveAt(0); }
        _effects.Add((root, Time.GetTicksMsec() / 1000.0));
    }
    private static MeshInstance3D Ring(Color color)
    {
        return new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = .94f, OuterRadius = 1, Rings = 40, RingSegments = 8 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
    }
    public override void _Process(double delta)
    {
        delta=EvaluationClock.Delta(delta);
        if (_camera == null) { return; }
        if (!ReferenceEquals(_world, Game.Sim)) { RebuildWorld(); }
        if (_follow >= 0 && Game.Sim.Agents.IsSlotAlive(_follow) && Game.Sim.Agents.GenerationOf(_follow) == _followGeneration)
            { _target = PositionAt(Game.Sim.Agents.XOf(_follow), Game.Sim.Agents.YOf(_follow)); }
        UpdateCamera();UpdateBuildingDetail();
        _poll += delta;
        if (_poll >= .3)
        {
            _poll = 0; CheckTerrain(); SyncEntities(); UpdateObservation();
        }
        AnimateActors((float)delta); UpdateBrush(); UpdateWeatherAtmosphere((float)delta);
        double now = Time.GetTicksMsec() / 1000.0;
        for (int i = _effects.Count - 1; i >= 0; i--)
        {
            var e = _effects[i]; float age = (float)(now - e.Born);
            if (age > 1.6f) { e.Node.QueueFree(); _effects.RemoveAt(i); }
            else
            {
                var ring = e.Node.GetChild<MeshInstance3D>(0); ring.Scale = Vector3.One * (1 + age * .55f);
                var material = (StandardMaterial3D)ring.MaterialOverride; float alpha = 1 - age / 1.6f;
                material.AlbedoColor = new Color(material.AlbedoColor, alpha);
                if (e.Node.GetChildCount() > 1)
                {
                    var label = e.Node.GetChild<Label3D>(1); label.Modulate = new Color(label.Modulate, alpha);
                    label.OutlineModulate = new Color(HudStyle.Ink, alpha * .6f);
                }
            }
        }
    }
    private void UpdateCamera()
    {
        if (_camera == null) { return; }
        _target.X = Math.Clamp(_target.X, 0, Game.Sim.World.Width * 2); _target.Z = Math.Clamp(_target.Z, 0, Game.Sim.World.Height * 2);
        var offset = new Vector3(MathF.Sin(_yaw) * MathF.Cos(_pitch), MathF.Sin(_pitch), MathF.Cos(_yaw) * MathF.Cos(_pitch)) * _distance;
        _camera.Position = _target + offset; _camera.LookAt(_target, Vector3.Up);
    }
    private void Pan(Vector2 screenDelta)
    {
        _follow = -1;
        Vector3 right = new(MathF.Cos(_yaw), 0, -MathF.Sin(_yaw)), forward = new(MathF.Sin(_yaw), 0, MathF.Cos(_yaw));
        _target += (right * -screenDelta.X + forward * -screenDelta.Y) * _distance * .002f;
    }
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton b)
        {
            _mouse = b.Position;
            if (b.ButtonIndex == MouseButton.Right) { _orbit = b.Pressed; }
            if (b.ButtonIndex == MouseButton.Middle) { _pan = b.Pressed; }
            if (b.Pressed && (b.ButtonIndex == MouseButton.WheelUp || b.ButtonIndex == MouseButton.WheelDown))
                { _distance = Math.Clamp(_distance * (b.ButtonIndex == MouseButton.WheelUp ? .85f : 1.18f), 4, 260); }
            if (b.ButtonIndex == MouseButton.Left)
            {
                if (b.Pressed)
                {
                    _leftHeld = true; _leftDragged = false; _leftStart = b.Position;
                    _painting = Game.Tool != PlayerTool.Inspect; _lastPaint = new Vector2I(-1, -1);
                    if (_painting) { Paint(b.Position); }
                }
            }
        }
        if (input is InputEventMouseMotion m)
        {
            _mouse = m.Position;
        }
        UpdateCamera(); AcceptEvent();
    }
    public override void _Input(InputEvent input)
    {
        // Release globally: crossing a HUD panel must never leave an orbit or brush latched.
        if (input is InputEventMouseButton b && !b.Pressed)
        {
            if (b.ButtonIndex == MouseButton.Right) { _orbit = false; }
            if (b.ButtonIndex == MouseButton.Middle) { _pan = false; }
            if (b.ButtonIndex == MouseButton.Left && _leftHeld)
            {
                Vector2 local = GetGlobalTransform().AffineInverse() * b.Position;
                if (!_painting && !_leftDragged && !_orbit && !_pan && new Rect2(Vector2.Zero, Size).HasPoint(local)
                    && GetViewport().GuiGetHoveredControl() == this) { Paint(local); }
                _leftHeld = _painting = _leftDragged = false;
            }
        }
        if (input is InputEventMouseMotion m && (_orbit || _pan || _leftHeld))
        {
            _mouse = GetGlobalTransform().AffineInverse() * m.Position;
            if (_orbit) { _follow = -1; if (_leftHeld) _leftDragged = true; _yaw -= m.Relative.X * .008f; _pitch = Math.Clamp(_pitch + m.Relative.Y * .005f, .18f, 1.50f); }
            if (_pan) { Pan(m.Relative); }
            if (_leftHeld && !_painting && !_orbit && !_pan)
            {
                if (!_leftDragged && _mouse.DistanceTo(_leftStart) >= 6) { _leftDragged = true; }
                if (_leftDragged) { Pan(m.Relative); }
            }
            if (_painting && !_orbit && !_pan && GetViewport().GuiGetHoveredControl() == this) { Paint(_mouse); }
            UpdateCamera();
        }
    }
    private Vector2I? Pick(Vector2 screen)
    {
        Vector3 origin = _camera.ProjectRayOrigin(screen), ray = _camera.ProjectRayNormal(screen);
        if (ray.Y >= -.01f) { return null; }
        // Intersect the visual height field without introducing physics writes into the simulation.
        Vector3 hit = origin + ray * ((.8f - origin.Y) / ray.Y);
        for (int i = 0; i < 4; i++)
        {
            int x = (int)MathF.Floor(hit.X / 2), y = (int)MathF.Floor(hit.Z / 2);
            if (!Game.Sim.World.IsInBounds(x, y)) { return null; }
            hit = origin + ray * ((HeightAt(x, y) - origin.Y) / ray.Y);
        }
        var tile = new Vector2I((int)MathF.Floor(hit.X / 2), (int)MathF.Floor(hit.Z / 2));
        return Game.Sim.World.IsInBounds(tile.X, tile.Y) ? tile : null;
    }
    private void Paint(Vector2 screen)
    {
        Vector2I? tile = Pick(screen); if (!tile.HasValue || tile.Value == _lastPaint) { return; }
        _lastPaint = tile.Value; Game.ClickTile(tile.Value.X, tile.Value.Y);
    }
    private void UpdateBrush()
    {
        var picked = Pick(_mouse); _brush.Visible = picked.HasValue && Game.Tool != PlayerTool.Inspect;
        if (picked.HasValue) { _brush.Position = PositionAt(picked.Value.X, picked.Value.Y) + Vector3.Up * .12f; float radius = MathF.Max(1, Game.Radius * 2); _brush.Scale = new Vector3(radius, .25f, radius); }
        _selection.Visible = Game.SelectedSlot >= 0;
        if (Game.SelectedSlot >= 0) { _selection.Position = PositionAt(Game.Sim.Agents.XOf(Game.SelectedSlot), Game.Sim.Agents.YOf(Game.SelectedSlot)) + Vector3.Up * .08f; _selection.Scale = new Vector3(.65f, .3f, .65f); }
    }
    private float HeightAt(int x, int y)
    {
        var tile = Game.Sim.World.TileAtClamped(x, y);
        return tile.Terrain == TerrainKind.Water ? -.25f : (tile.Height - .25f) * 4;
    }
    private float VertexHeight(int x, int y)
    { return (HeightAt(x, y) + HeightAt(x - 1, y) + HeightAt(x, y - 1) + HeightAt(x - 1, y - 1)) * .25f; }
    private Vector3 PositionAt(int x, int y) => new(x * 2 + 1,
        (VertexHeight(x, y) + VertexHeight(x + 1, y) + VertexHeight(x, y + 1) + VertexHeight(x + 1, y + 1)) / 4, y * 2 + 1);
    private static void Clear(Node node) { foreach (Node child in node.GetChildren()) { node.RemoveChild(child); child.QueueFree(); } }
    private void RebuildWorld()
    {
        _world = Game.Sim; _follow = -1; Clear(_terrainRoot); Clear(_props); Clear(_actors);
        _observerPaths = new SandBoxSim.Core.Pathing.AStarPathfinder(Game.Sim.World);
        _observedRoute = new Int2[Game.Sim.World.Tiles.Length]; _density = new int[Game.Sim.World.Tiles.Length];
        _routes.Mesh = null; _natureSignature = 0;
        foreach (var e in _effects) { e.Node.QueueFree(); } _effects.Clear(); _people.Clear(); _deer.Clear(); _wolves.Clear(); _personModels.Clear();
        ResetBuildingVisuals(); BuildTerrain(); BuildNature(); BuildBuildings(); SyncEntities(); SyncHazards();
    }
    private void CheckTerrain()
    {
        using var profile=RenderProfile.Measure("CheckTerrain");
        long signature = 17, nature = 17;
        foreach (var tile in Game.Sim.World.Tiles)
        {
            signature = unchecked(signature * 31 + (int)tile.Terrain * 3 + (int)tile.Fire + (int)(tile.Height * 10000));
            int flags = (tile.Vegetation > .12f ? 1 : 0) + (tile.Resource.Kind == ResourceKind.Food && tile.Resource.Amount > 5 ? 2 : 0)
                + (tile.Resource.Kind == ResourceKind.Iron ? 4 : 0);
            nature = unchecked(nature * 31 + flags);
            if (Overlay is 2 or 5) { signature = unchecked(signature * 31 + (int)tile.Resource.Kind * 1000 + (int)tile.Resource.Amount); }
            if (Overlay == 3) { signature = unchecked(signature * 31 + (int)(tile.Moisture * 100)); }
            if (Overlay == 4) { signature = unchecked(signature * 31 + (int)(tile.Fertility * 100)); }
        }
        nature = unchecked(nature * 31 + WildPlaces.Phase(Game.Sim.Clock / Game.Sim.Config.Clock.TicksPerDay));
        foreach (var place in Game.Wild.Places)
        {
            nature = unchecked(nature * 31 + (WildPlaces.IsLiving(Game.Sim, place) ? 1 : 0));
            int pile = Game.Sim.GroundStocks.FindAt(place.X, place.Y);
            nature = unchecked(nature * 31 + (pile >= 0 ? 1 : 0));
        }
        if (Overlay == 1)
            for (int i = 0; i < Game.Sim.Settlements.EntityCount; i++)
            {
                var settlement = Game.Sim.Settlements.At(i);
                signature = unchecked(signature * 31 + settlement.Id * 101 + settlement.CenterX * 503 + settlement.CenterY * 997 + (settlement.Dissolved ? 1 : 0));
            }
        if (Overlay == 6)
        {
            Array.Clear(_density);
            foreach (int slot in Game.Sim.Agents.AliveSlots())
            {
                int ax = Game.Sim.Agents.XOf(slot), ay = Game.Sim.Agents.YOf(slot);
                for (int y = Math.Max(0, ay - 3); y <= Math.Min(Game.Sim.World.Height - 1, ay + 3); y++)
                    for (int x = Math.Max(0, ax - 3); x <= Math.Min(Game.Sim.World.Width - 1, ax + 3); x++) { _density[y * Game.Sim.World.Width + x]++; }
            }
            foreach (int value in _density) { signature = unchecked(signature * 31 + value); }
        }
        if (signature != _terrainSignature || _lastOverlay != Overlay)
            { _terrainSignature = signature; _lastOverlay = Overlay; Clear(_terrainRoot); BuildTerrain(); Clear(_props); BuildNature(); _natureSignature = nature; _buildingSignature = 0; }
        else if (_natureSignature != nature) { _natureSignature = nature; Clear(_props); BuildNature(); }
        long buildings = 17 + Game.Sim.Settlements.ActiveCount;
        foreach (int i in Enumerable.Range(0, Game.Sim.Buildings.Capacity))
            if (Game.Sim.Buildings.IsAlive(i))
            {
                var store = Game.Sim.Buildings;
                buildings = unchecked(buildings * 31 + i * 7 + (int)store.StateOf(i) + (int)store.KindOf(i) * 17
                    + store.GenerationOf(i) * 101 + store.XOf(i) * 503 + store.YOf(i) * 997);
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    if (Math.Abs(dx) + Math.Abs(dy) == 1 && Game.Sim.World.IsInBounds(store.XOf(i) + dx, store.YOf(i) + dy))
                        buildings = unchecked(buildings * 31 + (int)Game.Sim.World.TerrainAt(store.XOf(i) + dx, store.YOf(i) + dy));
            }
        for(int i=0;i<Game.Sim.Settlements.EntityCount;i++)
        {
            var settlement=Game.Sim.Settlements.At(i);
            buildings=unchecked(buildings*31+settlement.Id*101+settlement.CenterX*503+settlement.CenterY*997+(settlement.Dissolved?1:0));
            foreach(char c in Game.Sim.Society.SettlementName(settlement.Id))buildings=unchecked(buildings*31+c);
        }
        if (buildings != _buildingSignature) { _buildingSignature = buildings; BuildBuildings(); }
    }
    public void ValidateObservationLayers()
    {
        int original = Overlay;
        try { for (int i = 0; i <= 8; i++) { Overlay = i; CheckTerrain(); SyncEntities(); UpdateObservation(); } }
        finally { Overlay = original; CheckTerrain(); UpdateObservation(); }
    }
    public void ValidateWorldDimensions()
    {
        RebuildWorld(); Center(); ValidateObservationLayers();
        var mesh = ((MeshInstance3D)_terrainRoot.GetChild(0)).Mesh;
        var bounds = mesh.GetAabb();
        if (Math.Abs(bounds.Size.X - Game.Sim.World.Width * 2) > .01f || Math.Abs(bounds.Size.Z - Game.Sim.World.Height * 2) > .01f)
            { throw new InvalidOperationException("Terrain geometry does not match loaded world dimensions"); }
        if (_target.X != Game.Sim.World.Width || _target.Z != Game.Sim.World.Height)
            { throw new InvalidOperationException("Camera did not center on loaded world"); }
    }
    private void UpdateObservation()
    {
        var sim = Game.Sim; _poseFrame++;
        foreach (var pair in _people)
        {
            var label = pair.Value.GetNodeOrNull<Label3D>("Activity");
            if (Overlay != 7) { if (label != null) { label.Visible = false; } continue; }
            if (label == null)
            {
                label = new Label3D { Name = "Activity", Font = Game.Theme.DefaultFont, FontSize = 24, PixelSize = .014f,
                    Position = Vector3.Up * 2.2f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, VisibilityRangeEnd = 55 };
                pair.Value.AddChild(label);
            }
            int slot = (int)(pair.Key & uint.MaxValue); label.Visible = true;
            var phase = sim.Agents.PhaseOf(slot);
            label.Text = phase == ActionPhase.Moving ? "前往 · " + ActionRegistry.DisplayNameOf(sim.Agents.ActionOf(slot))
                : phase == ActionPhase.Executing ? ActionRegistry.DisplayNameOf(sim.Agents.ActionOf(slot)) : "待命";
            label.Modulate = phase == ActionPhase.Moving ? new Color("#aed4d8") : HudStyle.Accent;
        }
        _cameraHint.Visible = Overlay > 0;
        _cameraHint.Text = Overlay switch
        {
            1 => "聚落领土 · 不同颜色对应不同聚落", 2 => "资源储量 · 绿色越亮，资源越多", 3 => "湿度 · 蓝色越明显，土地越湿润",
            4 => "肥力 · 绿色越亮，土地越肥沃", 5 => "食物分布 · 浅绿色标出食物资源", 6 => "人口密度 · 红色越明显，附近居民越密集",
            7 => "AI 状态 · 蓝色表示移动，金色表示当前行动", 8 => "行动路径 · 所选居民及镜头附近最多八条可达路径", _ => ""
        };
        _cameraHint.Position = new Vector2(24, Size.Y - 155);
        _routes.Visible = Overlay == 8;
        if (Overlay != 8) { return; }
        var vertices = new List<Vector3>();
        var candidates = sim.Agents.AliveSlots().Where(sim.Agents.HasTarget)
            .OrderBy(i => i == Game.SelectedSlot ? -1 : PositionAt(sim.Agents.XOf(i), sim.Agents.YOf(i)).DistanceSquaredTo(_target)).Take(8);
        foreach (int slot in candidates)
        {
            var goal = sim.Agents.TargetOf(slot);
            var result = _observerPaths.FindPath(sim.Agents.XOf(slot), sim.Agents.YOf(slot), goal.X, goal.Y, _observedRoute);
            if (!result.Success) { continue; }
            for (int i = 1; i < result.Length; i++)
            {
                vertices.Add(PositionAt(_observedRoute[i - 1].X, _observedRoute[i - 1].Y) + Vector3.Up * .2f);
                vertices.Add(PositionAt(_observedRoute[i].X, _observedRoute[i].Y) + Vector3.Up * .2f);
            }
        }
        if (vertices.Count == 0) { _routes.Mesh = null; return; }
        var mesh = new ImmediateMesh(); mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        foreach (Vector3 point in vertices) { mesh.SurfaceAddVertex(point); }
        mesh.SurfaceEnd(); _routes.Mesh = mesh;
    }
    private void BuildTerrain()
    {
        using var profile=RenderProfile.Measure("BuildTerrain");
        var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>(); var colors = new List<Color>(); var indices = new List<int>();
        var waterV = new List<Vector3>(); var waterI = new List<int>();
        int width = Game.Sim.World.Width;
        var materialMap = Image.CreateEmpty(width, Game.Sim.World.Height, false, Image.Format.Rgba8);
        for (int y = 0; y < Game.Sim.World.Height; y++) for (int x = 0; x < width; x++)
        {
            var tile = Game.Sim.World.TileAt(x, y); int index = vertices.Count;
            Vector3[] quad = { new(x * 2, VertexHeight(x, y), y * 2), new(x * 2 + 2, VertexHeight(x + 1, y), y * 2),
                new(x * 2, VertexHeight(x, y + 1), y * 2 + 2), new(x * 2 + 2, VertexHeight(x + 1, y + 1), y * 2 + 2) };
            Vector3 normal = (quad[2] - quad[0]).Cross(quad[1] - quad[0]).Normalized();
            int texture = tile.Fire == FireState.Burnt ? 14 : (int)tile.Terrain;
            if (tile.Terrain == TerrainKind.Water) { texture = 13; }
            materialMap.SetPixel(x, y, new Color(texture / 15f, 0, 0));
            Color tint = TerrainTint(x, y, tile);
            Vector2 cell = new(texture % 4, texture / 4);
            Vector2[] corners = { new(.015f, .015f), new(.985f, .015f), new(.015f, .985f), new(.985f, .985f) };
            for (int corner = 0; corner < 4; corner++) { vertices.Add(quad[corner]); normals.Add(normal); uvs.Add((cell + corners[corner]) / 4); colors.Add(tint); }
            indices.AddRange(new[] { index, index + 1, index + 2, index + 1, index + 3, index + 2 });
            if (tile.Terrain == TerrainKind.Water)
            {
                int w = waterV.Count; foreach (var point in quad) { waterV.Add(new Vector3(point.X, .02f, point.Z)); }
                waterI.AddRange(new[] { w, w + 1, w + 2, w + 1, w + 3, w + 2 });
            }
        }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray(); arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray(); arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colors.ToArray(); arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var terrain = new ArrayMesh(); terrain.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        var groundShader = new Shader { Code = @"
shader_type spatial;
render_mode cull_disabled;
uniform sampler2D atlas : source_color, filter_linear_mipmap;
uniform sampler2D material_map : filter_nearest, repeat_disable;
uniform vec2 map_size;
varying vec3 world_position;
void vertex(){ world_position = (MODEL_MATRIX * vec4(VERTEX,1.0)).xyz; }
vec3 surface_at(vec2 tile, vec2 pattern){
    float index = floor(texture(material_map,(clamp(tile,vec2(0.0),map_size-vec2(1.0))+vec2(.5))/map_size).r*15.0+.5);
    vec2 cell = vec2(mod(index,4.0),floor(index/4.0));
    vec2 uv = (cell+mix(vec2(.03),vec2(.97),pattern))/4.0;
    return mix(texture(atlas,uv,1.0).rgb,texture(atlas,uv,4.0).rgb,.55);
}
void fragment(){
    vec2 grid=world_position.xz*.5-vec2(.5);
    vec2 origin=floor(grid), blend=smoothstep(vec2(.1),vec2(.9),fract(grid));
    vec2 rotated=mat2(vec2(.72,.69),vec2(-.69,.72))*world_position.xz*.14;
    vec2 pattern=abs(fract(rotated)*2.0-1.0);
    vec3 a=mix(surface_at(origin,pattern),surface_at(origin+vec2(1.0,0.0),pattern),blend.x);
    vec3 b=mix(surface_at(origin+vec2(0.0,1.0),pattern),surface_at(origin+vec2(1.0),pattern),blend.x);
    vec3 c=mix(a,b,blend.y); float lum=dot(c,vec3(.2126,.7152,.0722));
    float broad=sin(world_position.x*.041+cos(world_position.z*.029))*cos(world_position.z*.053)*.055;
    ALBEDO=mix(vec3(lum),c,.42)*COLOR.rgb*vec3(.88,.92,.89)*(.88+broad); ROUGHNESS=1.0;
}" };
        var material = new ShaderMaterial { Shader = groundShader };
        material.SetShaderParameter("material_map", ImageTexture.CreateFromImage(materialMap));
        material.SetShaderParameter("map_size", new Vector2(width, Game.Sim.World.Height));
        if (_terrainAtlas != null) { material.SetShaderParameter("atlas", _terrainAtlas); }
        _terrainRoot.AddChild(new MeshInstance3D { Mesh = terrain, MaterialOverride = material });
        if (waterV.Count > 0)
        {
            var wa = new Godot.Collections.Array(); wa.Resize((int)Mesh.ArrayType.Max); wa[(int)Mesh.ArrayType.Vertex] = waterV.ToArray(); wa[(int)Mesh.ArrayType.Index] = waterI.ToArray();
            var wm = new ArrayMesh(); wm.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, wa);
            var shader = new Shader { Code = "shader_type spatial; render_mode cull_disabled; uniform sampler2D atlas : source_color, filter_linear_mipmap; varying vec3 p; void vertex(){ p = VERTEX; VERTEX.y += sin(VERTEX.x*1.6+TIME*.7)*.025 + cos(VERTEX.z*1.3+TIME*.9)*.02; NORMAL=vec3(0.0,1.0,0.0); } void fragment(){ float ripple = sin(p.x*2.4+TIME)*cos(p.z*1.8-TIME*.5); vec2 uv=(vec2(2.0,0.0)+clamp(fract(p.xz*.18+vec2(TIME*.003,0.0)),vec2(.02),vec2(.98)))/4.0; float fresnel=pow(1.0-clamp(dot(normalize(NORMAL),normalize(VIEW)),0.0,1.0),3.0); float fine=sin(p.x*9.0+TIME*1.2)*cos(p.z*7.0-TIME*.8); ALBEDO = mix(vec3(.075,.135,.125),vec3(.21,.28,.27),fresnel*.55) + vec3(ripple*.008+fine*.003); METALLIC=.16; ROUGHNESS=.24; NORMAL = normalize(NORMAL+vec3(ripple*.08,0.0,sin(p.z*3.0+TIME)*.06)); }" };
            var waterMaterial = new ShaderMaterial { Shader = shader }; if (_terrainAtlas != null) { waterMaterial.SetShaderParameter("atlas", _terrainAtlas); }
            _terrainRoot.AddChild(new MeshInstance3D { Mesh = wm, MaterialOverride = waterMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        // A physical diorama edge makes the terrain volume visible from low camera angles.
        _models.Part(_terrainRoot, "box", new Vector3(width, -3, Game.Sim.World.Height), new Vector3(width * 2, 5.0f, Game.Sim.World.Height * 2), 6);
    }
    private Color TerrainTint(int x, int y, Tile tile)
    {
        if (Overlay == 1) { int s = Game.Sim.Society.TerritoryAt(x, y); return s > 0 ? Color.FromHsv(s * .618034f % 1, .3f, 1) : Colors.White; }
        if (Overlay == 2) { return new Color(.4f, .5f + Math.Clamp(tile.Resource.Amount / 100, 0, .5f), .4f); }
        if (Overlay == 3) { return new Color(.5f, .65f, .4f + tile.Moisture * .6f); }
        if (Overlay == 4) { return new Color(.55f, .4f + tile.Fertility * .6f, .4f); }
        if (Overlay == 5) { return tile.Resource.Kind == ResourceKind.Food ? new Color(.85f, 1, .7f) : new Color(.5f, .55f, .5f); }
        if (Overlay == 6) { int count = _density[y * Game.Sim.World.Width + x]; return new Color(.5f + Math.Clamp(count / 20f, 0, .5f), .55f, .5f); }
        return new Color(.9f, .94f, .85f);
    }
    private void BuildNature()
    {
        using var profile=RenderProfile.Measure("BuildNature");
        var trunks = new List<Transform3D>(); var branches = new List<Transform3D>(); var leaves = new List<Transform3D>(); var pines = new List<Transform3D>();
        var rocks = new List<Transform3D>(); var ores = new List<Transform3D>(); var bushes = new List<Transform3D>();
        for (int y = 0; y < Game.Sim.World.Height; y++) for (int x = 0; x < Game.Sim.World.Width; x++)
        {
            var tile = Game.Sim.World.TileAt(x, y); uint h = unchecked((uint)(x * 73856093 ^ y * 19349663));
            Vector3 p = PositionAt(x, y); float size = .8f + h % 7 * .07f;
            if (tile.Terrain == TerrainKind.Forest && tile.Vegetation > .12f && tile.Fire != FireState.Burnt && h % 5 == 0)
            {
                trunks.Add(NatureModels.Transform(p + Vector3.Up * 1.7f * size, new Vector3(.42f, 3.4f, .42f) * size));
                if (h % 3 == 0)
                {
                    pines.Add(NatureModels.Transform(p + Vector3.Up * 3.2f * size, new Vector3(3.2f,4.4f,3.2f) * size, new Vector3(0,h%17,0)));
                }
                else
                    for (int branch = 0; branch < 5; branch++)
                    {
                        float angle = branch * 1.256f + h % 17, dx = MathF.Cos(angle), dz = MathF.Sin(angle);
                        branches.Add(NatureModels.Transform(p + new Vector3(dx * .65f, 2.6f, dz * .65f) * size, new Vector3(.14f, 1.7f, .14f) * size, new Vector3(dz * .6f, 0, -dx * .6f)));
                        leaves.Add(NatureModels.Transform(p + new Vector3(dx * 1.05f, 3.2f + (branch % 2) * .65f, dz * 1.05f) * size, new Vector3(2.6f, 2.0f, 2.4f) * size));
                    }
            }
            if (tile.Terrain == TerrainKind.Mountain && h % 9 == 0)
            { var t = NatureModels.Transform(p + Vector3.Up * .4f, new Vector3(1.6f, 1.2f, 1.7f) * size, new Vector3(.2f, h % 7, .4f)); if (tile.Resource.Kind == ResourceKind.Iron) { ores.Add(t); } else { rocks.Add(t); } }
            if (tile.Resource.Kind == ResourceKind.Food && tile.Resource.Amount > 5 && h % 17 == 0)
                { bushes.Add(NatureModels.Transform(p + Vector3.Up * .28f, new Vector3(.9f, .7f, .9f) * size)); }
        }
        foreach (var place in Game.Wild.Places)
        {
            if (!Game.Sim.World.IsInBounds(place.X, place.Y) || !WildPlaces.IsLiving(Game.Sim, place)) continue;
            var model = _models.WildPlace(place.Kind, WildPlaces.Phase(Game.Sim.Clock / Game.Sim.Config.Clock.TicksPerDay), Game.Sim.GroundStocks.FindAt(place.X, place.Y) >= 0);
            model.Position = PositionAt(place.X, place.Y); _props.AddChild(model);
        }
        _models.Batch(_props, "trunk", 0, trunks); _models.Batch(_props, "cylinder", 0, branches); _models.Batch(_props, "foliage", 4, leaves);
        _models.Batch(_props, "pine", 5, pines); _models.Batch(_props, "rock", 6, rocks); _models.Batch(_props, "rock", 7, ores); _models.Batch(_props, "foliage", 4, bushes);
    }
    private void SyncEntities()
    {
        using var profile=RenderProfile.Measure("SyncEntities");
        SyncHazards();
        var sim = Game.Sim; var live = new HashSet<long>();
        foreach (int slot in sim.Agents.AliveSlots())
        {
            long id = sim.Society.Identity(slot); live.Add(id);
            bool child = sim.Agents.LifeStageOf(slot) == LifeStage.Child; JobType job = sim.Agents.JobOf(slot);
            if (_people.TryGetValue(id, out Node3D? old) && _personModels[id] != (child, job)) { old.QueueFree(); _people.Remove(id); }
            if (!_people.ContainsKey(id))
            {
                var person = _models.Human(slot, child, job); _actors.AddChild(person); person.Position = PositionAt(sim.Agents.XOf(slot), sim.Agents.YOf(slot));
                _people[id] = person; _personModels[id] = (child, job);
            }
        }
        foreach (long id in _people.Keys.Where(k => !live.Contains(k)).ToArray()) { _people[id].QueueFree(); _people.Remove(id); _personModels.Remove(id); }
        live.Clear();
        foreach (int i in sim.Wildlife.AliveIndices())
        {
            long id = ((long)sim.Wildlife.GenerationOf(i) << 32) | (uint)i; live.Add(id);
            if (!_deer.ContainsKey(id)) { var animal = _models.Animal(false); _actors.AddChild(animal); animal.Position = PositionAt(sim.Wildlife.XOf(i), sim.Wildlife.YOf(i)); _deer[id] = animal; }
        }
        foreach (long id in _deer.Keys.Where(k => !live.Contains(k)).ToArray()) { _deer[id].QueueFree(); _deer.Remove(id); }
        var wolves = new HashSet<int>();
        foreach (var wolf in sim.Predators.Wolves)
        {
            wolves.Add(wolf.Id); if (!_wolves.ContainsKey(wolf.Id)) { var model = _models.Animal(true); _actors.AddChild(model); model.Position = PositionAt(wolf.X, wolf.Y); _wolves[wolf.Id] = model; }
        }
        foreach (int id in _wolves.Keys.Where(k => !wolves.Contains(k)).ToArray()) { _wolves[id].QueueFree(); _wolves.Remove(id); }
    }
    private int _poseFrame;
    private void AnimateActors(float delta)
    {
        using var profile=RenderProfile.Measure("AnimateActors");
        var sim = Game.Sim; _poseFrame++;
        foreach (var pair in _people)
        {
            int slot = (int)(pair.Key & uint.MaxValue); if (!sim.Agents.IsSlotAlive(slot) || sim.Society.Identity(slot) != pair.Key) { continue; }
            var node = pair.Value; Vector3 target = PositionAt(sim.Agents.XOf(slot), sim.Agents.YOf(slot));
            Vector3 direction = target - node.Position;
            bool moving = sim.Agents.PhaseOf(slot) == ActionPhase.Moving;
            node.Position = node.Position.Lerp(target, MathF.Min(1, delta * 10));
            if (direction.LengthSquared() > .02f) { node.Rotation = new Vector3(0, MathF.Atan2(direction.X, direction.Z) + MathF.PI, 0); }
            if(sim.Agents.PhaseOf(slot)==ActionPhase.Executing)
            {
                var destination=sim.Agents.TargetOf(slot);var facing=PositionAt(destination.X,destination.Y)-target;
                if(facing.LengthSquared()>.02f) node.Rotation=new(0,MathF.Atan2(facing.X,facing.Z)+MathF.PI,0);
            }
            var cargoKind=ResourceKind.Food;float cargoAmount=0;
            foreach(var resource in ResidentRig.CargoKinds)
            {float amount=sim.Agents.InventoryOf(slot,resource);if(amount>cargoAmount){cargoAmount=amount;cargoKind=resource;}}
            ((ResidentRig)node).ShowCargo(cargoAmount>.01f,cargoKind);
            ((ResidentRig)node).SetHeadDetail(_camera.Position.DistanceTo(node.Position)<18);
            ((ResidentRig)node).SetHandDetail(_camera.Position.DistanceTo(node.Position)<18);
            float distance=_camera.Position.DistanceTo(node.Position);int stride=distance>30?3:distance>12?2:1;
            if((_poseFrame+slot)%stride==0)
                ((ResidentRig)node).PresentAction(delta*stride,sim.Agents.ActionOf(slot),sim.Agents.PhaseOf(slot),Game.VisualPaused,slot);
        }
        foreach (var pair in _deer)
        { int slot = (int)(pair.Key & uint.MaxValue); if (sim.Wildlife.IsAlive(slot)) { MoveAnimal(pair.Value, PositionAt(sim.Wildlife.XOf(slot), sim.Wildlife.YOf(slot)), delta); } }
        foreach (var wolf in sim.Predators.Wolves) if (_wolves.TryGetValue(wolf.Id, out Node3D? node)) { MoveAnimal(node, PositionAt(wolf.X, wolf.Y), delta); }
    }
    private void MoveAnimal(Node3D node, Vector3 target, float delta)
    {
        Vector3 direction = target - node.Position;
        ((AnimalRig)node).Pose(delta, direction.LengthSquared() > .02f, Game.VisualPaused);
        node.Position = node.Position.Lerp(target, MathF.Min(1, delta * 8));
        if (direction.LengthSquared() > .02f) { node.Rotation = new Vector3(0, MathF.Atan2(direction.X, direction.Z) + MathF.PI, 0); }
    }
}
