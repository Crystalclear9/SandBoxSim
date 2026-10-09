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
    private long _terrainSignature, _buildingSignature, _natureSignature, _appearanceSignature;
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
        var ground=_terrainRoot.GetChild<MeshInstance3D>(0).Mesh.SurfaceGetArrays(0);
        foreach(var normal in ground[(int)Mesh.ArrayType.Normal].AsVector3Array())
            if(!normal.IsFinite() || MathF.Abs(normal.Length()-1)>.005f)throw new InvalidOperationException("Invalid smooth terrain normal");
        foreach(var point in ground[(int)Mesh.ArrayType.Vertex].AsVector3Array())
            if(!point.IsFinite())throw new InvalidOperationException("Invalid subdivided terrain vertex");
        Vector3 target = _target; float distance = _distance, yaw = _yaw, pitch = _pitch;
        foreach (var view in new[] { "斜视", "俯视", "近景" })
        {
            SetPerspective(view);
            var planes=_camera.GetFrustum().ToArray();
            if(!ActorInView(_target,planes)||ActorInView(_camera.Position+_camera.Basis.Z*10,planes))
                throw new InvalidOperationException("Actor camera visibility failed in "+view);
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
            AmbientLightEnergy = .34f, TonemapMode = Godot.Environment.ToneMapper.Aces, TonemapExposure = .88f,
            FogEnabled = true, FogDensity = .0017f, FogLightColor = new Color("#c2d1bd") };
        _scene.AddChild(new WorldEnvironment { Environment = _weatherEnvironment });
        _sunlight = new DirectionalLight3D { RotationDegrees = new Vector3(-48, -35, 0), LightColor = new Color("#f4eee0"),
            LightEnergy = .9f, ShadowEnabled = true, DirectionalShadowMode=DirectionalLight3D.ShadowMode.Parallel2Splits, DirectionalShadowMaxDistance = 160 };
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
            _poll = 0; CheckTerrain(true); SyncEntities(); SyncGroundStocks(); UpdateObservation();
        }
        DrainVisualUpdates();
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
        _sunlight.DirectionalShadowMaxDistance=Math.Clamp(_distance*1.8f,45,160);
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
            hit = origin + ray * ((PositionAt(x, y).Y - origin.Y) / ray.Y);
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
        return tile.Terrain == TerrainKind.Water ? -.25f : (tile.Height+.006f*tile.FootTraffic - .25f) * 4;
    }
    private float VertexHeight(int x, int y)
    { return (HeightAt(x, y) + HeightAt(x - 1, y) + HeightAt(x, y - 1) + HeightAt(x - 1, y - 1)) * .25f; }
    private float[,]? _groundHeights;
    private ImageTexture? _groundTintTexture;
    private Image? _groundMap;private ImageTexture? _groundMapTexture;private byte[] _groundMapBytes=Array.Empty<byte>();private long _wearSignature;
    private void UpdateGroundWear()
    {
        if(_groundMap==null || _groundMapTexture==null)return;
        for(int i=0;i<Game.Sim.World.Tiles.Length;i++)
            _groundMapBytes[i*4+1]=(byte)Math.Clamp((int)(Game.Sim.World.Tiles[i].FootTraffic*255),0,255);
        _groundMap.SetData(Game.Sim.World.Width,Game.Sim.World.Height,false,Image.Format.Rgba8,_groundMapBytes);
        _groundMapTexture.Update(_groundMap);
    }
    private Image GroundTintImage()
    {
        var world=Game.Sim.World;var image=Image.CreateEmpty(world.Width,world.Height,false,Image.Format.Rgb8);
        for(int y=0;y<world.Height;y++)for(int x=0;x<world.Width;x++)image.SetPixel(x,y,TerrainTint(x,y,world.TileAt(x,y)));
        return image;
    }
    private void UpdateGroundAppearance()
    {
        using var profile=RenderProfile.Measure("GroundAppearance");
        if(_groundMap==null||_groundMapTexture==null||_groundTintTexture==null)return;
        for(int i=0;i<Game.Sim.World.Tiles.Length;i++)
        {
            var tile=Game.Sim.World.Tiles[i];int texture=tile.Fire==FireState.Burnt?14:(int)tile.Terrain;
            if(tile.Terrain==TerrainKind.Water)texture=13;
            _groundMapBytes[i*4]=(byte)Math.Clamp((int)MathF.Round(texture*255f/15),0,255);
        }
        _groundMap.SetData(Game.Sim.World.Width,Game.Sim.World.Height,false,Image.Format.Rgba8,_groundMapBytes);
        _groundMapTexture.Update(_groundMap);
        using var tint=GroundTintImage();_groundTintTexture.Update(tint);
    }
    private const int GroundDetail=3;
    private Vector3 PositionAt(int x,int y)
    {
        x=Math.Clamp(x,0,Game.Sim.World.Width-1);y=Math.Clamp(y,0,Game.Sim.World.Height-1);
        if(_groundHeights==null)return new(x*2+1,VertexHeight(x,y),y*2+1);
        int gx=x*GroundDetail+1,gy=y*GroundDetail+1;
        return new(x*2+1,(_groundHeights[gx+1,gy]+_groundHeights[gx,gy+1])*.5f-Game.Sim.World.TileAt(x,y).FootTraffic*.024f,y*2+1);
    }
    private static void Clear(Node node) { foreach (Node child in node.GetChildren()) { node.RemoveChild(child); child.QueueFree(); } }
    private void RebuildWorld()
    {
        _pendingTerrain=_pendingNature=_pendingBuildings=false;
        _world = Game.Sim; _follow = -1; Clear(_terrainRoot); Clear(_props); Clear(_actors);
        _observerPaths = new SandBoxSim.Core.Pathing.AStarPathfinder(Game.Sim.World);
        _observedRoute = new Int2[Game.Sim.World.Tiles.Length]; _density = new int[Game.Sim.World.Tiles.Length];
        _routes.Mesh = null; _natureSignature = 0;
        foreach (var e in _effects) { e.Node.QueueFree(); } _effects.Clear(); _people.Clear(); _deer.Clear(); _wolves.Clear(); _personModels.Clear();
        ResetLivingDetails(); ResetBuildingVisuals(); BuildTerrain(); BuildNature(); BuildBuildings(); SyncEntities(); SyncHazards();
    }
    private void CheckTerrain(bool deferred=false)
    {
        using var profile=RenderProfile.Measure("CheckTerrain");
        long signature = 17, nature = 17,wear=17,appearance=17;
        int tileIndex=0;
        foreach (var tile in Game.Sim.World.Tiles)
        {
            int tx=tileIndex%Game.Sim.World.Width,ty=tileIndex++/Game.Sim.World.Width;
            uint decoration=unchecked((uint)(tx*73856093^ty*19349663));
            signature = unchecked(signature * 31 + (int)tile.Terrain * 3 + (int)MathF.Round((tile.Height+.006f*tile.FootTraffic)*1000));
            appearance=unchecked(appearance*31+(int)tile.Fire);
            wear=unchecked(wear*31+(int)(tile.FootTraffic*255));
            int flags=(decoration%5==0&&tile.Terrain==TerrainKind.Forest&&tile.Vegetation>.12f&&tile.Fire!=FireState.Burnt?1:0)
                +(decoration%17==0&&tile.Resource.Kind==ResourceKind.Food&&tile.Resource.Amount>5?2:0)
                +(decoration%9==0&&tile.Terrain==TerrainKind.Mountain&&tile.Resource.Kind==ResourceKind.Iron?4:0)
                +(HasMeadow(tile,decoration)?8:0);
            nature = unchecked(nature * 31 + flags);
            if (Overlay is 2 or 5) { appearance = unchecked(appearance * 31 + (int)tile.Resource.Kind * 1000 + (int)tile.Resource.Amount); }
            if (Overlay == 3) { appearance = unchecked(appearance * 31 + (int)(tile.Moisture * 100)); }
            if (Overlay == 4) { appearance = unchecked(appearance * 31 + (int)(tile.Fertility * 100)); }
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
                appearance = unchecked(appearance * 31 + settlement.Id * 101 + settlement.CenterX * 503 + settlement.CenterY * 997 + (settlement.Dissolved ? 1 : 0));
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
            foreach (int value in _density) { appearance = unchecked(appearance * 31 + value); }
        }
        if(signature!=_terrainSignature)
        {
            _terrainSignature=signature;_natureSignature=nature;_buildingSignature=0;
            if(deferred){_pendingTerrain=true;_pendingNature=true;}
            else{Clear(_terrainRoot);BuildTerrain();Clear(_props);BuildNature();}
        }
        else if(_natureSignature!=nature)
        {
            _natureSignature=nature;
            if(deferred)_pendingNature=true;else{Clear(_props);BuildNature();}
        }
        if(_appearanceSignature!=appearance||_lastOverlay!=Overlay)
        {_appearanceSignature=appearance;_lastOverlay=Overlay;UpdateGroundAppearance();}
        if(_wearSignature!=wear){_wearSignature=wear;UpdateGroundWear();}
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
        if(buildings!=_buildingSignature){_buildingSignature=buildings;if(deferred)_pendingBuildings=true;else BuildBuildings();}
    }
    internal object RenderQualityState()=>new {shadowMode=_sunlight.DirectionalShadowMode.ToString(),shadowDistance=_sunlight.DirectionalShadowMaxDistance,msaa=_viewport.Msaa3D.ToString()};
    internal object VisualUpdateState()=>new {terrainPending=_pendingTerrain,naturePending=_pendingNature,buildingsPending=_pendingBuildings,cachedBuildings=_buildingVisuals.Count,livedHomes=_buildingVisuals.Values.Count(v=>v.Kind==BuildingKind.House&&v.Life!=null&&v.Life.Visible&&v.LifeKey>=3),growingFields=_buildingVisuals.Values.Count(v=>v.Kind==BuildingKind.Farm&&v.Life!=null&&v.Life.Visible),groundStockModels=_stockVisuals.Values.Count(v=>v.Node.Visible)};
    private bool _pendingTerrain,_pendingNature,_pendingBuildings;
    private void DrainVisualUpdates()
    {
        // One reconstruction category per frame. Entity state is re-read when the work executes.
        if(_pendingTerrain){_pendingTerrain=false;Clear(_terrainRoot);BuildTerrain();}
        else if(_pendingNature){_pendingNature=false;Clear(_props);BuildNature();}
        else if(_pendingBuildings)_pendingBuildings=BuildBuildings(1);
    }
    public void ValidateObservationLayers()
    {
        int original = Overlay;
        CheckTerrain();var geometry=_terrainRoot.GetChild<MeshInstance3D>(0).Mesh;
        try { for (int i = 0; i <= 8; i++) {
            Overlay = i; CheckTerrain(); SyncEntities(); UpdateObservation();
            if(_terrainRoot.GetChild<MeshInstance3D>(0).Mesh!=geometry)throw new InvalidOperationException("Observation layer rebuilt physical terrain");
        } }
        finally { Overlay = original; CheckTerrain(); UpdateObservation(); }
    }
    public void ValidateGroundWear()
    {
        CheckTerrain();var geometry=_terrainRoot.GetChild<MeshInstance3D>(0).Mesh;
        var world=Game.Sim.World;int index=world.Tiles.Length/2;var original=world.Tiles[index];
        string digest=StateHash.ComputeDigest(Game.Sim);
        try
        {
            world.Tiles[index].Height-=.006f*(.18f-original.FootTraffic);world.Tiles[index].FootTraffic=.18f;CheckTerrain();
            if(_terrainRoot.GetChild<MeshInstance3D>(0).Mesh!=geometry)throw new InvalidOperationException("Footfall rebuilt terrain geometry");
            if(MathF.Abs(_groundMap!.GetPixel(index%world.Width,index/world.Width).G-.18f)>1f/255)
                throw new InvalidOperationException("Footfall texture is stale below old coarse threshold");
        }
        finally {world.Tiles[index]=original;CheckTerrain();}
        if(StateHash.ComputeDigest(Game.Sim)!=digest)throw new InvalidOperationException("Ground presentation validation changed simulation");
        GD.Print("GROUND_PRESENTATION_PASS: texture-only overlays, byte-precision wear and unchanged simulation");
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
        using var profile=RenderProfile.Measure("Observation");
        var sim = Game.Sim;
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
        int height=Game.Sim.World.Height,nx=width*GroundDetail,ny=height*GroundDetail;
        var coarse=new float[width+1,height+1];
        for(int y=0;y<=height;y++)for(int x=0;x<=width;x++)coarse[x,y]=VertexHeight(x,y);
        _groundHeights=new float[nx+1,ny+1];
        float Cubic(float a,float b,float c,float d,float t)=>.5f*(2*b+(c-a)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t);
        float Sample(int x,int y)=>coarse[Math.Clamp(x,0,width),Math.Clamp(y,0,height)];
        var waterWeights=new float[width,height];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            float sum=0;for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                if(Game.Sim.World.IsInBounds(x+dx,y+dy)&&Game.Sim.World.TileAt(x+dx,y+dy).Terrain==TerrainKind.Water)sum+=(dx==0?4:1)*(dy==0?4:1);
            waterWeights[x,y]=sum/36f;
        }
        float Wet(int x,int y)=>waterWeights[Math.Clamp(x,0,width-1),Math.Clamp(y,0,height-1)];
        float Blend(float t)=>Math.Clamp(t,0,1);
        float WaterMask(float x,float y)
        {
            x-=.5f;y-=.5f;int ix=(int)MathF.Floor(x),iy=(int)MathF.Floor(y);float u=Blend(x-ix),v=Blend(y-iy);
            float Row(int dy)=>Cubic(Wet(ix-1,iy+dy),Wet(ix,iy+dy),Wet(ix+1,iy+dy),Wet(ix+2,iy+dy),u);
            return Math.Clamp(Cubic(Row(-1),Row(0),Row(1),Row(2),v),0,1);
        }
        for(int y=0;y<=ny;y++)for(int x=0;x<=nx;x++)
        {
            float fx=x/(float)GroundDetail,fy=y/(float)GroundDetail;int ix=(int)fx,iy=(int)fy;float u=fx-ix,v=fy-iy;
            float Row(int offset)=>Cubic(Sample(ix-1,iy+offset),Sample(ix,iy+offset),Sample(ix+1,iy+offset),Sample(ix+2,iy+offset),u);
            float h=Cubic(Row(-1),Row(0),Row(1),Row(2),v);
            float wet=WaterMask(fx,fy);h=Mathf.Lerp(h,MathF.Min(h,-.25f),Math.Clamp(wet*1.7f,0,1));
            // Small stable relief; water and paved surfaces keep an even bed.
            var tile=Game.Sim.World.TileAt(Math.Min(ix,width-1),Math.Min(iy,height-1));
            float relief=tile.Terrain is TerrainKind.Water or TerrainKind.Road?0:1;
            _groundHeights[x,y]=h+relief*(1-wet)*(1-tile.FootTraffic*.65f)*NaturalRelief(fx,fy,Game.Sim.World.Seed);
        }
        for(int y=0;y<=ny;y++)for(int x=0;x<=nx;x++)
        {
            int l=Math.Max(0,x-1),r=Math.Min(nx,x+1),b=Math.Max(0,y-1),t=Math.Min(ny,y+1);
            float dx=(_groundHeights[r,y]-_groundHeights[l,y])/((r-l)*2f/GroundDetail);
            float dz=(_groundHeights[x,t]-_groundHeights[x,b])/((t-b)*2f/GroundDetail);
            vertices.Add(new(x*2f/GroundDetail,_groundHeights[x,y],y*2f/GroundDetail));normals.Add(new Vector3(-dx,1,-dz).Normalized());
            uvs.Add(new(x/(float)nx,y/(float)ny));
            int tx=Math.Min(x/GroundDetail,width-1),ty=Math.Min(y/GroundDetail,height-1);
            colors.Add(TerrainTint(tx,ty,Game.Sim.World.TileAt(tx,ty)));
        }
        for(int y=0;y<ny;y++)for(int x=0;x<nx;x++)
        {
            int i=y*(nx+1)+x;indices.AddRange(new[]{i,i+1,i+nx+1,i+1,i+nx+2,i+nx+1});
        }
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            var tile=Game.Sim.World.TileAt(x,y);int texture=tile.Fire==FireState.Burnt?14:(int)tile.Terrain;
            if(tile.Terrain==TerrainKind.Water)texture=13;
            materialMap.SetPixel(x,y,new Color(texture/15f,tile.FootTraffic,Wet(x,y)));
            bool shore=false;for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                if(Game.Sim.World.IsInBounds(x+dx,y+dy) && Game.Sim.World.TileAt(x+dx,y+dy).Terrain==TerrainKind.Water)shore=true;
            if(!shore)continue;
            int w=waterV.Count;waterV.AddRange(new[]{new Vector3(x*2,.02f,y*2),new(x*2+2,.02f,y*2),new(x*2,.02f,y*2+2),new(x*2+2,.02f,y*2+2)});
            waterI.AddRange(new[]{w,w+1,w+2,w+1,w+3,w+2});
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
uniform sampler2D tint_map : filter_linear, repeat_disable;
uniform sampler2D forest_diffuse:source_color,filter_linear_mipmap_anisotropic,repeat_enable;
uniform sampler2D forest_normal:hint_normal,filter_linear_mipmap_anisotropic,repeat_enable;
uniform sampler2D forest_roughness:filter_linear_mipmap,repeat_enable;
uniform vec2 map_size;
uniform float forest_index;
varying vec3 world_position;
varying float terrain_slope;
void vertex(){terrain_slope=1.0-clamp(NORMAL.y,0.0,1.0); VERTEX.y-=texture(material_map,clamp(VERTEX.xz*.5/map_size,vec2(0.0),vec2(1.0))).g*.024; world_position = (MODEL_MATRIX * vec4(VERTEX,1.0)).xyz; }
float soil_noise(vec2 p){vec2 i=floor(p),f=fract(p);f=f*f*(3.0-2.0*f);vec2 q=vec2(127.1,311.7);return mix(mix(fract(sin(dot(i,q))*43758.5453),fract(sin(dot(i+vec2(1,0),q))*43758.5453),f.x),mix(fract(sin(dot(i+vec2(0,1),q))*43758.5453),fract(sin(dot(i+vec2(1,1),q))*43758.5453),f.x),f.y);}
vec3 surface_at(vec2 tile, vec2 pattern){
    float index = floor(texture(material_map,(clamp(tile,vec2(0.0),map_size-vec2(1.0))+vec2(.5))/map_size).r*15.0+.5);
    vec2 cell = vec2(mod(index,4.0),floor(index/4.0));
    vec2 uv = (cell+mix(vec2(.03),vec2(.97),pattern))/4.0;
    return mix(texture(atlas,uv).rgb,texture(atlas,(cell+mix(vec2(.03),vec2(.97),abs(fract(pattern*1.73+vec2(.23,.41))*2.0-1.0)))/4.0,2.0).rgb,.28);
}
float forest_at(vec2 tile){return 1.0-step(.5,abs(texture(material_map,(clamp(tile,vec2(0),map_size-vec2(1))+vec2(.5))/map_size).r*15.0-forest_index));}
float traffic(vec2 tile){return texture(material_map,(clamp(tile,vec2(0.0),map_size-vec2(1.0))+vec2(.5))/map_size).g;}
float trail(vec2 grid){
    vec2 tile=floor(grid+vec2(.5)),offset=grid-tile;
    float center=traffic(tile);if(center<=.015)return 0.0;
    float strength=smoothstep(.015,.45,center);
    float path=(1.0-smoothstep(.10,.35,length(offset)))*strength;
    for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++){
        if(x==0&&y==0)continue;
        vec2 direction=vec2(float(x),float(y));
        float joint=smoothstep(.015,.45,min(center,traffic(tile+direction)));
        float t=clamp(dot(offset,direction)/dot(direction,direction),0.0,.5);
        float shoulder=1.0-smoothstep(.10,.29,length(offset-direction*t));
        path=max(path,shoulder*joint);
    }
    return path;
}
void fragment(){
    vec2 grid=world_position.xz*.5-vec2(.5);
    vec2 origin=floor(grid), blend=fract(grid);
    vec2 rotated=mat2(vec2(.72,.69),vec2(-.69,.72))*world_position.xz*.34;
    vec2 pattern=abs(fract(rotated)*2.0-1.0);
    vec3 a=mix(surface_at(origin,pattern),surface_at(origin+vec2(1.0,0.0),pattern),blend.x);
    vec3 b=mix(surface_at(origin+vec2(0.0,1.0),pattern),surface_at(origin+vec2(1.0),pattern),blend.x);
    vec3 c=mix(a,b,blend.y); c=vec3(.5)+(c-vec3(.5))*.42; float lum=dot(c,vec3(.2126,.7152,.0722));
    float broad=(soil_noise(world_position.xz*.067)-.5)*.16;
    float grit=soil_noise(world_position.xz*5.7)-.5;
    float footprint=max(length(dFdx(world_position)),length(dFdy(world_position)));grit/=1.0+footprint*footprint*32.0;
    float damp=texture(material_map,(clamp(grid,vec2(0.0),map_size-vec2(1.0))+vec2(.5))/map_size).b;
    float slope=terrain_slope;
    float wear=texture(material_map,(clamp(grid,vec2(0.0),map_size-vec2(1.0))+vec2(.5))/map_size).g;
    vec3 earth=vec3(.205,.169,.125);
    vec3 meadow=mix(vec3(lum),c,.32)*texture(tint_map,world_position.xz*.5/map_size).rgb*vec3(.82,.94,.86)*(.94+broad+grit*.10);
    float woodland=mix(mix(forest_at(origin),forest_at(origin+vec2(1,0)),blend.x),mix(forest_at(origin+vec2(0,1)),forest_at(origin+vec2(1)),blend.x),blend.y);
    vec3 litter=texture(forest_diffuse,world_position.xz*.22).rgb;
    meadow=mix(meadow,mix(litter,meadow,.25),clamp(woodland*.60+damp*.22+slope*.55,0.0,.75));
    ALBEDO=mix(meadow,earth,clamp(trail(grid)*.82+wear*.12,0.0,.94)); ROUGHNESS=mix(.95,texture(forest_roughness,world_position.xz*.22).r,.32); SPECULAR=.18;
    vec3 scanned_normal=texture(forest_normal,world_position.xz*.22).rgb*2.0-1.0;
    vec3 scanned_world=normalize(vec3(scanned_normal.x*.25,max(.3,scanned_normal.z),scanned_normal.y*.25));
    NORMAL=normalize(mix(NORMAL,mat3(VIEW_MATRIX)*scanned_world,.24*(1.0-slope)));
    vec3 dx=dFdx(VERTEX),dy=dFdy(VERTEX),tx=cross(dy,NORMAL),ty=cross(NORMAL,dx);float determinant=dot(dx,tx);
    float height=grit*.0035;if(abs(determinant)>.0000000001)NORMAL=normalize(abs(determinant)*NORMAL-sign(determinant)*(dFdx(height)*tx+dFdy(height)*ty));
}" };
        var material = new ShaderMaterial { Shader = groundShader };
        string forestRoot="res://assets/models/environment/forest_ground_04/textures/";
        foreach(var map in new[]{("forest_diffuse","forest_ground_04_diff_1k.jpg"),("forest_normal","forest_ground_04_nor_gl_1k.jpg"),("forest_roughness","forest_ground_04_rough_1k.jpg")})
        {if(!ResourceLoader.Exists(forestRoot+map.Item2))throw new InvalidOperationException("Missing CC0 forest material");material.SetShaderParameter(map.Item1,GD.Load<Texture2D>(forestRoot+map.Item2));}
        _groundMap=materialMap;_groundMapBytes=materialMap.GetData();_groundMapTexture=ImageTexture.CreateFromImage(materialMap);
        material.SetShaderParameter("material_map", _groundMapTexture);
        _groundTintTexture=ImageTexture.CreateFromImage(GroundTintImage());material.SetShaderParameter("tint_map",_groundTintTexture);
        material.SetShaderParameter("map_size", new Vector2(width, Game.Sim.World.Height));
        material.SetShaderParameter("forest_index",(float)TerrainKind.Forest);
        if (_terrainAtlas != null) { material.SetShaderParameter("atlas", _terrainAtlas); }
        _terrainRoot.AddChild(new MeshInstance3D { Mesh = terrain, MaterialOverride = material });
        if (waterV.Count > 0)
        {
            var wa = new Godot.Collections.Array(); wa.Resize((int)Mesh.ArrayType.Max); wa[(int)Mesh.ArrayType.Vertex] = waterV.ToArray(); wa[(int)Mesh.ArrayType.Index] = waterI.ToArray();
            var wm = new ArrayMesh(); wm.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, wa);
            var shader = new Shader { Code = @"shader_type spatial; render_mode cull_disabled; uniform sampler2D atlas : source_color, filter_linear_mipmap; uniform sampler2D material_map : filter_nearest, repeat_disable; uniform vec2 map_size; varying vec3 p; float wet(vec2 tile){return texture(material_map,(clamp(tile,vec2(0.0),map_size-vec2(1.0))+vec2(.5))/map_size).b;} float cubic(float a,float b,float c,float d,float t){return .5*(2.0*b+(c-a)*t+(2.0*a-5.0*b+4.0*c-d)*t*t+(-a+3.0*b-3.0*c+d)*t*t*t);} float row(vec2 base,float y,float u){return cubic(wet(base+vec2(-1.0,y)),wet(base+vec2(0.0,y)),wet(base+vec2(1.0,y)),wet(base+vec2(2.0,y)),u);} void vertex(){ p = VERTEX; VERTEX.y += sin(VERTEX.x*.73+VERTEX.z*.41+TIME*.7)*.008 + cos(VERTEX.z*1.17-VERTEX.x*.29+TIME*.9)*.005; NORMAL=vec3(0.0,1.0,0.0); } void fragment(){vec2 grid=p.xz*.5-vec2(.5),base=floor(grid),f=fract(grid);float bank=clamp(cubic(row(base,-1.0,f.x),row(base,0.0,f.x),row(base,1.0,f.x),row(base,2.0,f.x),f.y),0.0,1.0);if(bank<.48)discard; float ripple = sin(p.x*2.4+TIME)*cos(p.z*1.8-TIME*.5); vec2 uv=(vec2(2.0,0.0)+clamp(fract(p.xz*.18+vec2(TIME*.003,0.0)),vec2(.02),vec2(.98)))/4.0; float fresnel=pow(1.0-clamp(dot(normalize(NORMAL),normalize(VIEW)),0.0,1.0),3.0); float fine=sin(p.x*9.0+TIME*1.2)*cos(p.z*7.0-TIME*.8); ALBEDO = mix(vec3(.075,.145,.145),vec3(.24,.32,.35),fresnel*.70) + vec3(ripple*.008+fine*.003); ALBEDO=mix(ALBEDO,vec3(.19,.24,.18),(1.0-smoothstep(.49,.80,bank))*.22); METALLIC=0.0; SPECULAR=.50; ROUGHNESS=.23;
float w1=cos(p.x*1.73+p.z*1.19+TIME*.7),w2=cos(p.x*3.11-p.z*2.47-TIME*.93),w3=cos(p.x*5.37+p.z*4.19+TIME*1.13);
vec3 surface_normal=normalize(vec3(w1*.010+w2*.006+w3*.003,1.0,w1*.007-w2*.005+w3*.002));
NORMAL=normalize(mat3(VIEW_MATRIX)*surface_normal); }" };
            var waterMaterial = new ShaderMaterial { Shader = shader };waterMaterial.SetShaderParameter("material_map",_groundMapTexture);waterMaterial.SetShaderParameter("map_size",new Vector2(width,height)); if (_terrainAtlas != null) { waterMaterial.SetShaderParameter("atlas", _terrainAtlas); }
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
    private static bool HasMeadow(Tile tile,uint hash)=>hash%5==1 && tile.BuildingId==0 && tile.Fire!=FireState.Burnt
        && tile.Vegetation>.2f && tile.Terrain is TerrainKind.Grass or TerrainKind.Forest;
    private void BuildNature()
    {
        using var profile=RenderProfile.Measure("BuildNature");
        var leaves = new List<Transform3D>(); var pines = new List<Transform3D>();
        var grass = new List<Transform3D>(); var ferns = new List<Transform3D>(); var rocks = new List<Transform3D>(); var ores = new List<Transform3D>(); var bushes = new List<Transform3D>();
        for (int y = 0; y < Game.Sim.World.Height; y++) for (int x = 0; x < Game.Sim.World.Width; x++)
        {
            var tile = Game.Sim.World.TileAt(x, y); uint h = unchecked((uint)(x * 73856093 ^ y * 19349663));
            Vector3 p = PositionAt(x, y); float size = .8f + h % 7 * .07f;
            if(tile.Terrain==TerrainKind.Forest&&tile.Vegetation>.35f&&tile.Moisture>.2f&&tile.Fire!=FireState.Burnt&&h%13==0)ferns.Add(NatureModels.Transform(p,new Vector3(size,size,size),new Vector3(0,h%19,0)));
            if(HasMeadow(tile,h))grass.Add(NatureModels.Transform(p,new Vector3(size,.8f+(h%11)*.065f,size),new Vector3(0,h%31,0)));
            if (tile.Terrain == TerrainKind.Forest && tile.Vegetation > .12f && tile.Fire != FireState.Burnt && h % 5 == 0)
            {
                var tree=NatureModels.Transform(p,Vector3.One*size,new Vector3(0,h%17,0));
                if(h%3==0)pines.Add(tree);else leaves.Add(tree);
            }
            if (tile.Terrain == TerrainKind.Mountain && h % 9 == 0)
            { var t = NatureModels.Transform(p + Vector3.Up * .4f, new Vector3(1.6f, 1.2f, 1.7f) * size, new Vector3(.2f, h % 7, .4f)); if (tile.Resource.Kind == ResourceKind.Iron) { ores.Add(t); } else { rocks.Add(t); } }
            if (tile.Resource.Kind == ResourceKind.Food && tile.Resource.Amount > 5 && h % 17 == 0)
                { bushes.Add(NatureModels.Transform(p + Vector3.Up * .28f, new Vector3(.9f, .7f, .9f) * size)); }
        }
        if(_groundMapTexture!=null)_models.BindEnvironmentGround(_groundMapTexture,new Vector2(Game.Sim.World.Width,Game.Sim.World.Height));
        _models.Batch(_props,"grass",5,grass,null,34,true);
        _models.Batch(_props,"fern",4,ferns,null,38,true);
        foreach (var place in Game.Wild.Places)
        {
            if (!Game.Sim.World.IsInBounds(place.X, place.Y) || !WildPlaces.IsLiving(Game.Sim, place)) continue;
            var model = _models.WildPlace(place.Kind, WildPlaces.Phase(Game.Sim.Clock / Game.Sim.Config.Clock.TicksPerDay), Game.Sim.GroundStocks.FindAt(place.X, place.Y) >= 0);
            model.Position = PositionAt(place.X, place.Y); _props.AddChild(model);
        }
        _models.Batch(_props,"broad-tree",4,leaves,preserveMaterials:true);
        _models.Batch(_props,"conifer-tree",5,pines,preserveMaterials:true); _models.Batch(_props, "rock", 6, rocks); _models.Batch(_props, "rock", 7, ores); _models.Batch(_props,"foliage",4,bushes,preserveMaterials:true);
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
            if (!_deer.ContainsKey(id)) { var animal = _models.Animal(false); ((AnimalRig)animal).SetIdentity(i); _actors.AddChild(animal); animal.Position = PositionAt(sim.Wildlife.XOf(i), sim.Wildlife.YOf(i)); _deer[id] = animal; }
        }
        foreach (long id in _deer.Keys.Where(k => !live.Contains(k)).ToArray()) { _deer[id].QueueFree(); _deer.Remove(id); }
        var wolves = new HashSet<int>();
        foreach (var wolf in sim.Predators.Wolves)
        {
            wolves.Add(wolf.Id); if (!_wolves.ContainsKey(wolf.Id)) { var model = _models.Animal(true); ((AnimalRig)model).SetIdentity(wolf.Id); _actors.AddChild(model); model.Position = PositionAt(wolf.X, wolf.Y); _wolves[wolf.Id] = model; }
        }
        foreach (int id in _wolves.Keys.Where(k => !wolves.Contains(k)).ToArray()) { _wolves[id].QueueFree(); _wolves.Remove(id); }
    }
    private int _poseFrame;
    private bool ActorInView(Vector3 center,Plane[] frustum)
    {
        foreach(var plane in frustum)
        {
            float outward=plane.DistanceTo(center)*(plane.DistanceTo(_target)>0?-1:1);
            if(outward>2.5f)return false;
        }
        return true;
    }
    private void AnimateActors(float delta)
    {
        using var profile=RenderProfile.Measure("AnimateActors");
        var sim = Game.Sim; _poseFrame++;
        // Copy six planes once; enumerating the native array per resident multiplies interop calls.
        var frustum=_camera.GetFrustum().ToArray();
        var cameraPosition=_camera.Position;
        foreach (var pair in _people)
        {
            int slot = (int)(pair.Key & uint.MaxValue); if (!sim.Agents.IsSlotAlive(slot) || sim.Society.Identity(slot) != pair.Key) { continue; }
            var node = pair.Value; Vector3 target = PositionAt(sim.Agents.XOf(slot), sim.Agents.YOf(slot));
            var current=node.Position;Vector3 direction = target-current;
            bool moving = sim.Agents.PhaseOf(slot) == ActionPhase.Moving;
            var rendered=current.Lerp(target,MathF.Min(1,delta*10));
            if(direction.LengthSquared()>.000001f)node.Position=rendered;
            bool visible=ActorInView(rendered+Vector3.Up*.9f,frustum);
            if(node.Visible!=visible)node.Visible=visible;
            if(!visible)continue;
            if (direction.LengthSquared() > .02f) { node.Rotation = new Vector3(0, MathF.Atan2(direction.X, direction.Z) + MathF.PI, 0); }
            if(sim.Agents.PhaseOf(slot)==ActionPhase.Executing)
            {
                var destination=sim.Agents.TargetOf(slot);var facing=sim.World.IsInBounds(destination)?PositionAt(destination.X,destination.Y)-target:Vector3.Zero;
                if(facing.LengthSquared()>.02f) node.Rotation=new(0,MathF.Atan2(facing.X,facing.Z)+MathF.PI,0);
            }
            float distanceSquared=cameraPosition.DistanceSquaredTo(rendered);
            // Keep position interpolation every frame; distant joint poses need fewer updates.
            int stride=distanceSquared>45*45?8:distanceSquared>30*30?6:distanceSquared>12*12?2:1;
            var rig=(ResidentRig)node;
            rig.SetHeadDetail(distanceSquared<18*18);rig.SetHandDetail(distanceSquared<18*18);
            if((_poseFrame+slot)%stride!=0)continue;
            var cargoKind=ResourceKind.Food;float cargoAmount=0;
            foreach(var resource in ResidentRig.CargoKinds)
            {float amount=sim.Agents.InventoryOf(slot,resource);if(amount>cargoAmount){cargoAmount=amount;cargoKind=resource;}}
            rig.ShowCargo(cargoAmount>.01f,cargoKind);
            rig.PresentAction(delta*stride,sim.Agents.ActionOf(slot),sim.Agents.PhaseOf(slot),Game.VisualPaused,slot);
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
