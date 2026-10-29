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
    // Gallery previews use independent studios. Do not build or draw a second world underneath them.
    internal bool WorldPresentationEnabled { get; init; } = true;
    private SubViewport _viewport = null!;
    private SubViewportContainer _container = null!;
    private Node3D _scene = null!, _terrainRoot = null!, _props = null!, _actors = null!;
    private Camera3D _camera = null!;
    private NatureModels _models = null!;
    private Simulation? _world;
    private Vector3 _target = new(92, 1, 100);
    private float _distance = 44, _yaw = -.45f, _pitch = .85f;
    public float CameraYaw => _yaw;
    internal float CameraDistance=>_distance;
    internal string CameraReadout=>$"俯角 {Mathf.RadToDeg(_pitch):0}° · 距离 {_distance:0} m";
    internal bool IsFollowing(int slot)=>slot>=0&&_follow==slot&&Game.Sim.Agents.IsSlotAlive(slot)&&Game.Sim.Agents.GenerationOf(slot)==_followGeneration;
    internal void StopFollowing()=>_follow=-1;
    private bool _orbit, _pan, _painting, _leftHeld, _leftDragged;
    private Vector2 _leftStart;
    private bool _doubleClick;
    private PlayerTool _strokeTool;
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
    private MeshInstance3D _routes = null!;
    private SandBoxSim.Core.Pathing.AStarPathfinder _observerPaths = null!;
    private Int2[] _observedRoute = Array.Empty<Int2>();
    private int[] _density = Array.Empty<int>();
    public bool HasTerrain => _terrainRoot.GetChildCount() > 0;
    public bool HasPerspectiveCamera => _camera.Projection == Camera3D.ProjectionType.Perspective;
    internal static void ValidateGalleryIsolation(MainGame game)
    {
        var view = new WorldView3D { Game = game, WorldPresentationEnabled = false };
        game.AddChild(view);
        try
        {
            view._Process(1.0 / 60);
            if (view.HasTerrain || view._props.GetChildCount() != 0 || view._actors.GetChildCount() != 0 ||
                view._viewport.RenderTargetUpdateMode != SubViewport.UpdateMode.Disabled || view._container.Visible ||
                view._weatherEnvironment.Sky!=null || view._sunlight.ShadowEnabled)
                throw new InvalidOperationException("Model gallery constructed or rendered a background world");
        }
        finally { game.RemoveChild(view); view.Free(); }
    }
    public void ValidateViewControls()
    {
        if (!HasTerrain || !HasPerspectiveCamera || _people.Count != Game.Sim.Agents.LiveCount) { throw new InvalidOperationException("3D scene incomplete"); }
        var ground=_terrainRoot.GetChild<MeshInstance3D>(0).Mesh.SurfaceGetArrays(0);
        foreach(var normal in ground[(int)Mesh.ArrayType.Normal].AsVector3Array())
            if(!normal.IsFinite() || MathF.Abs(normal.Length()-1)>.005f)throw new InvalidOperationException("Invalid smooth terrain normal");
        foreach(var point in ground[(int)Mesh.ArrayType.Vertex].AsVector3Array())
            if(!point.IsFinite())throw new InvalidOperationException("Invalid subdivided terrain vertex");
        Vector3 target = _target; float distance = _distance, yaw = _yaw, pitch = _pitch;
        ValidateGroundedFlowers();
        int following=_follow,generation=_followGeneration;
        try
        {
            int observed=Game.Sim.Agents.AliveSlots().First();
            Follow(observed);if(!IsFollowing(observed))throw new InvalidOperationException("Camera follow indicator misses live resident");
            StopFollowing();if(IsFollowing(observed))throw new InvalidOperationException("Stopping camera follow leaves indicator active");
            if(_target!=target)throw new InvalidOperationException("Stopping camera follow jumps the target");
        }
        finally {_follow=following;_followGeneration=generation;_distance=distance;_pitch=pitch;}
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
        _Input(new InputEventMouseMotion { Position = new Vector2(460, 320), Relative = new Vector2(60, 20), ButtonMask = MouseButtonMask.Left });
        _Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(-20, -20) });
        if (_target == beforeDrag || _leftHeld || _painting) { throw new InvalidOperationException("Left drag/release failed"); }
        float beforeYaw = _yaw, beforePitch = _pitch;
        _GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = new Vector2(400, 300) });
        _Input(new InputEventMouseMotion { Position = new Vector2(430, 310), Relative = new Vector2(30, 10), ButtonMask = MouseButtonMask.Right });
        _Input(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = new Vector2(-20, -20) });
        if (_yaw == beforeYaw || _pitch == beforePitch || _orbit) { throw new InvalidOperationException("Orbit/release failed"); }
        float beforeZoom = _distance;
        _GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = new Vector2(400, 300) });
        if (_distance >= beforeZoom) { throw new InvalidOperationException("Mouse wheel zoom failed"); }
        _target = target; _distance = distance; _yaw = yaw; _pitch = pitch; UpdateCamera();
    }
    public override void _Notification(int what)
    {
        if (what==NotificationWMWindowFocusOut || what==NotificationApplicationFocusOut || (what==NotificationVisibilityChanged&&!IsVisibleInTree())) { CancelPointerGesture(); }
    }
    public override void _Ready()
    {
        MouseFilter = WorldPresentationEnabled ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore; ClipContents = true;
        _container = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore, Visible = WorldPresentationEnabled };
        _container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(_container);
        _viewport = new SubViewport { OwnWorld3D = true, RenderTargetUpdateMode = WorldPresentationEnabled ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled, Msaa3D = Viewport.Msaa.Msaa2X };
        _container.AddChild(_viewport);
        _scene = new Node3D(); _viewport.AddChild(_scene);
        _terrainRoot = new Node3D(); _scene.AddChild(_terrainRoot);
        _props = new Node3D(); _scene.AddChild(_props);
        _actors = new Node3D(); _scene.AddChild(_actors);
        _models = new NatureModels();
        BuildHazardVisuals();
        // Hidden gallery backgrounds must not allocate sky radiance or shadow
        // resources. They can be destroyed before ever reaching a render frame.
        var sky = new ProceduralSkyMaterial { SkyTopColor = new Color("#779ca9"), SkyHorizonColor = new Color("#d4d4b8"), GroundHorizonColor = new Color("#b8c2a0"), GroundBottomColor = new Color("#465346") };
        _weatherEnvironment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = WorldPresentationEnabled ? new Sky { SkyMaterial = sky } : null, AmbientLightSource = WorldPresentationEnabled ? Godot.Environment.AmbientSource.Sky : Godot.Environment.AmbientSource.Color,
            AmbientLightEnergy = .34f, TonemapMode = Godot.Environment.ToneMapper.Aces, TonemapExposure = .88f,
            FogEnabled = true, FogDensity = .0017f, FogLightColor = new Color("#c2d1bd") };
        if(WorldPresentationEnabled)_scene.AddChild(new WorldEnvironment { Environment = _weatherEnvironment });
        _sunlight = new DirectionalLight3D { RotationDegrees = new Vector3(-48, -35, 0), LightColor = new Color("#f4eee0"),
            LightEnergy = .9f, ShadowEnabled = WorldPresentationEnabled, ShadowBias=2.4f, DirectionalShadowMode=DirectionalLight3D.ShadowMode.Parallel4Splits, DirectionalShadowMaxDistance = 160 };
        _scene.AddChild(_sunlight);
        _camera = new Camera3D { Current = true, Near = .15f, Far = 650, Fov = 52 }; _scene.AddChild(_camera);
        _routes = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = HudStyle.Accent, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } }; _scene.AddChild(_routes);
        _brush = Ring(HudStyle.Accent);
        _brush.Mesh = new TorusMesh { InnerRadius = .994f, OuterRadius = 1, Rings = 64, RingSegments = 6 };
        _scene.AddChild(_brush);
        _selection = Ring(HudStyle.Accent); _scene.AddChild(_selection);
        _cameraHint = new Label { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _cameraHint.AddThemeColorOverride("font_color", new Color("#f6f0d6")); AddChild(_cameraHint);
        _cameraHint.AddThemeFontSizeOverride("font_size", 13); _cameraHint.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, .8f));
        _cameraHint.AddThemeConstantOverride("shadow_offset_x", 1); _cameraHint.AddThemeConstantOverride("shadow_offset_y", 1);
        BuildHoverInfo();RebuildWorld(); UpdateCamera();
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
    internal bool MatchesCameraPreset(string view)=>view switch
    {
        "全景"=>MathF.Abs(_pitch-1.1f)<.015f&&MathF.Abs(_distance-Math.Max(Game.Sim.World.Width,Game.Sim.World.Height)*1.6f)<.2f&&new Vector2(_target.X-Game.Sim.World.Width,_target.Z-Game.Sim.World.Height).Length()<.1f,
        "俯视"=>MathF.Abs(_pitch-1.48f)<.015f,
        "近景"=>MathF.Abs(_pitch-.48f)<.015f&&MathF.Abs(_distance-11)<.2f,
        "斜视"=>MathF.Abs(_pitch-.9f)<.015f&&MathF.Abs(_distance-42)<.2f,
        _=>false
    };
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
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,CullMode=BaseMaterial3D.CullModeEnum.Disabled },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
    }
    public override void _Process(double delta)
    {
        delta=EvaluationClock.Delta(delta);
        if (_camera == null || !WorldPresentationEnabled) { return; }
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
        AnimateActors((float)delta); UpdateBrush();UpdateHoverInfo(delta); UpdateWeatherAtmosphere((float)delta);
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
        _sunlight.DirectionalShadowMaxDistance=Math.Clamp(_distance*1.8f,16,160);
        _sunlight.ShadowNormalBias=Mathf.Lerp(.2f,1f,Math.Clamp((_distance-16)/24,0,1));
        _camera.Position = _target + offset; _camera.LookAt(_target, Vector3.Up);
    }
    private Vector2I _brushTile=new(-1,-1);
    private int _brushRadius=-1;
    private long _brushGroundSignature;
    private void UpdateBrush()
    {
        bool active=Game.Tool!=PlayerTool.Inspect&&!_orbit&&!_pan&&GetViewport().GuiGetHoveredControl()==this;
        var picked=active?Pick(_mouse):null;_brush.Visible=picked.HasValue;
        if(picked.HasValue&&(_brushTile!=picked.Value||_brushRadius!=Game.Radius||_brushGroundSignature!=(_terrainSignature^_wearSignature)))
        {
            _brushTile=picked.Value;_brushRadius=Game.Radius;_brushGroundSignature=_terrainSignature^_wearSignature;
            _brush.Mesh=GroundRing(PositionAt(picked.Value.X,picked.Value.Y),Game.Radius*2+1);_brush.Position=Vector3.Zero;_brush.Scale=Vector3.One;
        }
        _selection.Visible=Game.SelectedSlot>=0;
        if(Game.SelectedSlot>=0)
        {
            long id=Game.Sim.Society.Identity(Game.SelectedSlot);var position=_people.TryGetValue(id,out var person)?person.Position:PositionAt(Game.Sim.Agents.XOf(Game.SelectedSlot),Game.Sim.Agents.YOf(Game.SelectedSlot));
            _selection.Position=position+Vector3.Up*.045f;_selection.Scale=new Vector3(.65f,.12f,.65f);
        }
    }
    private Mesh GroundRing(Vector3 center,float radius)
    {
        const int segments=96;var vertices=new Vector3[segments*2];var indices=new int[segments*6];
        for(int i=0;i<segments;i++)
        {
            float angle=i*MathF.Tau/segments;
            for(int edge=0;edge<2;edge++)
            {
                float r=radius+(edge==0?-.035f:.035f),x=center.X+MathF.Cos(angle)*r,z=center.Z+MathF.Sin(angle)*r;
                int tx=Math.Clamp((int)(x*.5f),0,Game.Sim.World.Width-1),ty=Math.Clamp((int)(z*.5f),0,Game.Sim.World.Height-1);
                float y=GroundHeight(x,z);if(Game.Sim.World.TerrainAt(tx,ty)==TerrainKind.Water)y=MathF.Max(y,.02f);
                vertices[i*2+edge]=new(x,y+.04f,z);
            }
            int a=i*2,b=(i+1)%segments*2,j=i*6;indices[j]=a;indices[j+1]=b;indices[j+2]=a+1;indices[j+3]=a+1;indices[j+4]=b;indices[j+5]=b+1;
        }
        var arrays=new Godot.Collections.Array();arrays.Resize((int)Mesh.ArrayType.Max);arrays[(int)Mesh.ArrayType.Vertex]=vertices;arrays[(int)Mesh.ArrayType.Index]=indices;
        var mesh=new ArrayMesh();mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);return mesh;
    }
    private float HeightAt(int x, int y)
    {
        var tile = Game.Sim.World.TileAtClamped(x, y);
        if(tile.Terrain==TerrainKind.Water)return -.25f;
        // Logical water is selected by a per-world quantile, not Height < .25.
        // A walkable tile must therefore have an actual dry supporting surface.
        float land=.12f+MathF.Max(0,(tile.Height+.006f*tile.FootTraffic-.25f)*12);
        if(tile.Terrain==TerrainKind.Mountain)return land;
        int distance=Game.Sim.World.DistanceToWater(Math.Clamp(x,0,Game.Sim.World.Width-1),Math.Clamp(y,0,Game.Sim.World.Height-1),4);
        float inland=Mathf.SmoothStep(1,4,distance);
        float lowBank=.16f+MathF.Min(.40f,land*.18f);
        return Mathf.Lerp(lowBank,land,inland);
    }
    private float VertexHeight(int x, int y)
    { return (HeightAt(x, y) + HeightAt(x - 1, y) + HeightAt(x, y - 1) + HeightAt(x - 1, y - 1)) * .25f; }
    private float[,]? _groundHeights;
    private ImageTexture? _groundTintTexture;
    private Image? _groundTintImage;
    private long _groundTintUploadRevision;
    private Image? _groundMap;private ImageTexture? _groundMapTexture;private byte[] _groundMapBytes=Array.Empty<byte>();private long _wearSignature;
    private void UpdateGroundWear()
    {
        if(_groundMap==null || _groundMapTexture==null)return;
        var changedTiles=new HashSet<int>();
        for(int i=0;i<Game.Sim.World.Tiles.Length;i++)
        {
            byte wear=(byte)Math.Clamp((int)(Game.Sim.World.Tiles[i].FootTraffic*255),0,255);
            if(_groundMapBytes[i*4+1]!=wear)changedTiles.Add(i);
            _groundMapBytes[i*4+1]=wear;
        }
        _groundMap.SetData(Game.Sim.World.Width,Game.Sim.World.Height,false,Image.Format.Rgba8,_groundMapBytes);
        _groundMapTexture.Update(_groundMap);InvalidateMeadowPlacements(changedTiles);SyncFlowerRoots();SyncMeadowRoots(changedTiles);
    }
    private Image GroundTintImage()
    {
        var world=Game.Sim.World;var image=Image.CreateEmpty(world.Width,world.Height,false,Image.Format.Rgba8);
        for(int y=0;y<world.Height;y++)for(int x=0;x<world.Width;x++)
        {
            var tile=world.TileAt(x,y);var tint=TerrainTint(x,y,tile);tint.A=Math.Clamp(tile.Vegetation,0,1);image.SetPixel(x,y,tint);
        }
        return image;
    }
    private void UploadGroundTint(bool create=false)
    {
        var image=GroundTintImage();
        if(create)_groundTintTexture=ImageTexture.CreateFromImage(image);else _groundTintTexture!.Update(image);
        _groundTintImage?.Dispose();_groundTintImage=image;_groundTintUploadRevision++;
    }
    private void UpdateGroundAppearance()
    {
        using var profile=RenderProfile.Measure("GroundAppearance");
        if(_groundMap==null||_groundMapTexture==null||_groundTintTexture==null)return;
        for(int i=0;i<Game.Sim.World.Tiles.Length;i++)
        {
            var tile=Game.Sim.World.Tiles[i];int texture=tile.Fire==FireState.Burnt?14:(int)tile.Terrain;
            if(tile.Terrain==TerrainKind.Water)texture=13;
            _groundMapBytes[i*4+3]=(byte)Math.Clamp((int)(tile.Moisture*255),0,255);
            _groundMapBytes[i*4]=(byte)Math.Clamp((int)MathF.Round(texture*255f/15),0,255);
        }
        _groundMap.SetData(Game.Sim.World.Width,Game.Sim.World.Height,false,Image.Format.Rgba8,_groundMapBytes);
        _groundMapTexture.Update(_groundMap);
        UploadGroundTint();
    }
    private const int GroundDetail=4;
    private Vector2I _groundGridSize;
    private Vector2[] _groundUvs=Array.Empty<Vector2>();
    private int[] _groundIndices=Array.Empty<int>();
    private Vector3 PositionAt(int x,int y)
    {
        x=Math.Clamp(x,0,Game.Sim.World.Width-1);y=Math.Clamp(y,0,Game.Sim.World.Height-1);
        if(_groundHeights==null)return new(x*2+1,VertexHeight(x,y),y*2+1);
        return new(x*2+1,GroundHeight(x*2+1,y*2+1),y*2+1);
    }
    private static void Clear(Node node) { foreach (Node child in node.GetChildren()) { node.RemoveChild(child); child.QueueFree(); } }
    private void RebuildWorld()
    {
        if (!WorldPresentationEnabled) { return; }
        CancelPointerGesture();
        _pickMeshes.Clear();_brushTile=new(-1,-1);_pendingTerrain=_pendingNature=_pendingBuildings=false;
        _world = Game.Sim; _follow = -1; Clear(_terrainRoot); Clear(_props); Clear(_actors);
        _flowerBatches.Clear();
        _observerPaths = new SandBoxSim.Core.Pathing.AStarPathfinder(Game.Sim.World);
        _observedRoute = new Int2[Game.Sim.World.Tiles.Length]; _density = new int[Game.Sim.World.Tiles.Length];
        _routes.Mesh = null; _natureSignature = 0;
        foreach (var e in _effects) { e.Node.QueueFree(); } _effects.Clear(); _people.Clear(); _deer.Clear(); _wolves.Clear(); _personModels.Clear();_poseSchedule.Clear();
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
            appearance=unchecked(appearance*31+(int)tile.Fire*17+(int)(tile.Moisture*16)+(int)(tile.Vegetation*16)*257);
            wear=unchecked(wear*31+(int)(tile.FootTraffic*255));
            int flags=(decoration%5==0&&tile.Terrain==TerrainKind.Forest&&tile.Vegetation>.12f&&tile.Fire!=FireState.Burnt?1:0)
                +(decoration%17==0&&tile.Resource.Kind==ResourceKind.Food&&tile.Resource.Amount>5?2:0)
                +(decoration%9==0&&tile.Terrain==TerrainKind.Mountain&&tile.Resource.Kind==ResourceKind.Iron?4:0)
                +(MeadowCandidateMask(tile,tx,ty,decoration)<<8);
            if(tile.BuildingId>0)flags|=128;
            if(decoration%9==0&&tile.Terrain==TerrainKind.Forest&&tile.Vegetation>.35f&&tile.Moisture>.2f&&tile.Fire!=FireState.Burnt)flags|=16;
            if(HasRiverPlants(tile,decoration))flags|=32;
            nature = unchecked(nature * 31 + flags);
            if (Overlay is 2 or 5) { appearance = unchecked(appearance * 31 + (int)tile.Resource.Kind * 1000 + (int)tile.Resource.Amount); }
            if (Overlay == 3) { appearance = unchecked(appearance * 31 + (int)(tile.Moisture * 100)); }
            if (Overlay == 4) { appearance = unchecked(appearance * 31 + (int)(tile.Fertility * 100)); }
        }
        nature = unchecked(nature * 31 + WildPlaces.Phase(Game.Sim.Clock / Game.Sim.Config.Clock.TicksPerDay));
        foreach (var place in Game.Wild.Places)
        {
            nature = unchecked(nature * 31 + (int)place.Kind*97+place.X*503+place.Y*997+(int)(WildPlaces.Maturity(Game.Sim,place)*8));
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
    internal object RenderQualityState(object[]? galleries=null,object? portrait=null)
    {
        var quality=new Dictionary<string,object> { ["worldPresentationEnabled"]=WorldPresentationEnabled,["shadowMode"]=_sunlight.DirectionalShadowMode.ToString(),
            ["shadowDistance"]=_sunlight.DirectionalShadowMaxDistance,["shadowBias"]=_sunlight.ShadowBias,
            ["shadowNormalBias"]=_sunlight.ShadowNormalBias,["msaa"]=_viewport.Msaa3D.ToString(),
            ["landscapeSurface"]="coverage-mosaic-v6",["habitatDetail"]="stratified-meadow-v10",["vegetationSurface"]="engine-faced-v2",
            ["architecture"]="inset-joinery-v13",["garmentMaterial"]="relaxed-yoke-linen-v14",["shoulderMotion"]="supported-reach-v1",["reachMotion"]="palm-trajectory-v2",["interface"]="resident-framing-memory-v28",["wildFlora"]="arched-bramble-v1",["fontProfile"]="noto-packaged-v1",["interfaceVisible"]=Game.InterfaceVisible,
            ["animalPresentation"]="source-paw-contours-v11",["residentSurface"]="linear-skin-v9",["workEffort"]="relative-load-v1",["toolMotion"]="velocity-continuity-v1",["bodySupport"]="planted-sagittal-v1",["facialTiming"]="phrase-blink-v1",["handSurface"]="anatomical-regions-v1",["handlingMotion"]="cargo-priority-v3",["toolGeometry"]="forged-heads-v2",["cargoGeometry"]="bound-timber-v2",["equipmentAttachment"]="surface-holster-v2",["socialMotion"]="phrase-coordinated-v3",["waterSurface"]="constrained-bank-v6",["wristContour"]="source-oval-v1",
            ["hairline"]="source-root-v1",["reedPanicle"]="connected-axis-v1",["bodyFrame"]="continuous-lower-body-v7",["faceContour"]="regional-adult-v2",["forearmProfile"]="tapered-volume-v1" };
        if(galleries is {Length:>0})quality["galleries"]=galleries;
        if(portrait!=null)quality["portrait"]=portrait;
        return quality;
    }
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
        foreach(float x in new[]{-.5f,0f,world.Width*2f,world.Width*2f+.5f})
        foreach(float z in new[]{-.5f,0f,world.Height*2f,world.Height*2f+.5f})
        {
            var plant=RootedPlant(new(x,0,z),Vector3.One,.7f);
            if(!plant.IsFinite()||MathF.Abs(plant.Origin.Y-GroundHeight(x,z)+.012f)>.0001f)
                throw new InvalidOperationException("Rooted plant fails at terrain boundary");
        }
        try
        {
            world.Tiles[index].Height-=.006f*(.18f-original.FootTraffic);world.Tiles[index].FootTraffic=.18f;CheckTerrain();
            if(_terrainRoot.GetChild<MeshInstance3D>(0).Mesh!=geometry)throw new InvalidOperationException("Footfall rebuilt terrain geometry");
            if(MathF.Abs(_groundMap!.GetPixel(index%world.Width,index/world.Width).G-.18f)>1f/255)
                throw new InvalidOperationException("Footfall texture is stale below old coarse threshold");
        }
        finally {world.Tiles[index]=original;CheckTerrain();}
        if(StateHash.ComputeDigest(Game.Sim)!=digest)throw new InvalidOperationException("Ground presentation validation changed simulation");
        ValidateMeadowPresentation();
        ValidateLogicalShoreline();
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
        ResetMeadowPlacements();
        using var profile=RenderProfile.Measure("BuildTerrain");
        int width=Game.Sim.World.Width,height=Game.Sim.World.Height,nx=width*GroundDetail,ny=height*GroundDetail;
        var vertices=new Vector3[(nx+1)*(ny+1)];var normals=new Vector3[vertices.Length];
        if(_groundGridSize!=new Vector2I(nx,ny))
        {
            _groundGridSize=new(nx,ny);_groundUvs=new Vector2[vertices.Length];_groundIndices=new int[nx*ny*6];
            for(int y=0;y<=ny;y++)for(int x=0;x<=nx;x++)_groundUvs[y*(nx+1)+x]=new(x/(float)nx,y/(float)ny);
            for(int y=0;y<ny;y++)for(int x=0;x<nx;x++)
            {
                int i=y*(nx+1)+x,j=(y*nx+x)*6;
                _groundIndices[j]=i;_groundIndices[j+1]=i+1;_groundIndices[j+2]=i+nx+1;
                _groundIndices[j+3]=i+1;_groundIndices[j+4]=i+nx+2;_groundIndices[j+5]=i+nx+1;
            }
        }
        var waterV=new List<Vector3>();var waterI=new List<int>();
        var materialMap=Image.CreateEmpty(width,height,false,Image.Format.Rgba8);
        var coarse=new float[width+1,height+1];
        for(int y=0;y<=height;y++)for(int x=0;x<=width;x++)coarse[x,y]=VertexHeight(x,y);
        _groundHeights=new float[nx+1,ny+1];
        var shoreMasks=BuildShoreNeighbourMasks();
        float Cubic(float a,float b,float c,float d,float t)=>.5f*(2*b+(c-a)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t);
        float CubicSlope(float a,float b,float c,float d,float t)=>.5f*((c-a)+2*(2*a-5*b+4*c-d)*t+3*(-a+3*b-3*c+d)*t*t);
        float Sample(int x,int y)=>coarse[Math.Clamp(x,0,width),Math.Clamp(y,0,height)];
        var waterWeights=new float[width,height];
        var foundation=new float[width+1,height+1];
        for(int y=0;y<=height;y++)for(int x=0;x<=width;x++)
        {
            float amount=0;for(int dy=-1;dy<=0;dy++)for(int dx=-1;dx<=0;dx++)
            {var ground=Game.Sim.World.TileAtClamped(x+dx,y+dy);if(ground.Terrain==TerrainKind.Road)amount+=.25f;}
            foundation[x,y]=amount;
        }
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
            float r0=Row(-1),r1=Row(0),r2=Row(1),r3=Row(2);
            float h=Cubic(r0,r1,r2,r3,v);
            float wet=WaterMask(fx,fy);
            // Small stable relief; water and paved surfaces keep an even bed.
            var tile=Game.Sim.World.TileAt(Math.Min(ix,width-1),Math.Min(iy,height-1));
            float relief=1-Mathf.Lerp(Mathf.Lerp(foundation[ix,iy],foundation[Math.Min(ix+1,width),iy],u),Mathf.Lerp(foundation[ix,Math.Min(iy+1,height)],foundation[Math.Min(ix+1,width),Math.Min(iy+1,height)],u),v);
            if(tile.Terrain==TerrainKind.Water)relief=0;
            float bank=Mathf.SmoothStep(.04f,.22f,wet)*(1-Mathf.SmoothStep(.42f,.60f,wet));
            float erosion=(TerrainNoise(fx*1.8f,fy*1.8f,Game.Sim.World.Seed)-.5f)*.14f*bank*relief;
            float RowSlope(int offset)=>CubicSlope(Sample(ix-1,iy+offset),Sample(ix,iy+offset),Sample(ix+1,iy+offset),Sample(ix+2,iy+offset),u);
            float slope=new Vector2(Cubic(RowSlope(-1),RowSlope(0),RowSlope(1),RowSlope(2),v),CubicSlope(r0,r1,r2,r3,v)).Length()*.5f;
            float exposed=Mathf.SmoothStep(.18f,.7f,slope);
            float rockRelief=(1-MathF.Abs(TerrainNoise(fx*1.1f+7,fy*1.1f-3,Game.Sim.World.Seed)*2-1)-.5f)*.16f*exposed;
            float sculpted=h+erosion+relief*(1-wet)*(1-tile.FootTraffic*.65f)*(NaturalRelief(fx,fy,Game.Sim.World.Seed)+rockRelief);
            _groundHeights[x,y]=ShoreSupportedHeight(fx,fy,sculpted,shoreMasks[ix,iy]);
        }
        for(int y=0;y<=ny;y++)for(int x=0;x<=nx;x++)
        {
            int l=Math.Max(0,x-1),r=Math.Min(nx,x+1),b=Math.Max(0,y-1),t=Math.Min(ny,y+1);
            float dx=(_groundHeights[r,y]-_groundHeights[l,y])/((r-l)*2f/GroundDetail);
            float dz=(_groundHeights[x,t]-_groundHeights[x,b])/((t-b)*2f/GroundDetail);
            int index=y*(nx+1)+x;
            vertices[index]=new(x*2f/GroundDetail,_groundHeights[x,y],y*2f/GroundDetail);normals[index]=new Vector3(-dx,1,-dz).Normalized();
        }
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            var tile=Game.Sim.World.TileAt(x,y);int texture=tile.Fire==FireState.Burnt?14:(int)tile.Terrain;
            if(tile.Terrain==TerrainKind.Water)texture=13;
            materialMap.SetPixel(x,y,new Color(texture/15f,tile.FootTraffic,Wet(x,y),tile.Moisture));
            bool shore=false;for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                if(Game.Sim.World.IsInBounds(x+dx,y+dy) && Game.Sim.World.TileAt(x+dx,y+dy).Terrain==TerrainKind.Water)shore=true;
            if(!shore)continue;
            int w=waterV.Count;waterV.AddRange(new[]{new Vector3(x*2,.02f,y*2),new(x*2+2,.02f,y*2),new(x*2,.02f,y*2+2),new(x*2+2,.02f,y*2+2)});
            waterI.AddRange(new[]{w,w+1,w+2,w+1,w+3,w+2});
        }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex]=vertices;arrays[(int)Mesh.ArrayType.Normal]=normals;arrays[(int)Mesh.ArrayType.TexUV]=_groundUvs;
        arrays[(int)Mesh.ArrayType.Index]=_groundIndices;
        var terrain = new ArrayMesh(); terrain.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        var material=CreateGroundMaterial();
        _groundMap=materialMap;_groundMapBytes=materialMap.GetData();_groundMapTexture=ImageTexture.CreateFromImage(materialMap);
        material.SetShaderParameter("material_map", _groundMapTexture);material.SetShaderParameter("hydrology_map",_groundMapTexture);
        UploadGroundTint(true);material.SetShaderParameter("tint_map",_groundTintTexture??throw new InvalidOperationException("Ground tint upload did not create its texture"));
        material.SetShaderParameter("map_size", new Vector2(width, Game.Sim.World.Height));
        _terrainRoot.AddChild(new MeshInstance3D { Mesh = terrain, MaterialOverride = material });
        if (waterV.Count > 0)
        {
            var wa = new Godot.Collections.Array(); wa.Resize((int)Mesh.ArrayType.Max); wa[(int)Mesh.ArrayType.Vertex] = waterV.ToArray(); wa[(int)Mesh.ArrayType.Index] = waterI.ToArray();
            var wm = new ArrayMesh(); wm.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, wa);
            var waterMaterial=CreateWaterMaterial(vertices,nx,ny,width,height);
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
    private static bool HasMeadow(Tile tile,uint hash)=>tile.BuildingId==0 && tile.Fire!=FireState.Burnt
        && tile.Vegetation>.2f && tile.Terrain is TerrainKind.Grass or TerrainKind.Forest;
    private static bool HasRiverPlants(Tile tile,uint hash)=>hash%3!=0&&tile.BuildingId==0&&tile.Fire!=FireState.Burnt
        &&tile.Moisture>.30f&&tile.Terrain is TerrainKind.Grass or TerrainKind.Forest or TerrainKind.Sand;
    private void BuildNature()
    {
        _flowerBatches.Clear();
        using var profile=RenderProfile.Measure("BuildNature");
        var leaves = new List<Transform3D>(); var pines = new List<Transform3D>();
        var grass = new List<Transform3D>(); var ferns = new List<Transform3D>(); var rocks = new List<Transform3D>(); var ores = new List<Transform3D>(); var bushes = new List<Transform3D>(); var pebbles=new List<Transform3D>();var reeds=new List<Transform3D>();
        var flowers=new[]{new List<Transform3D>(),new List<Transform3D>(),new List<Transform3D>()};
        int botanicalPhase=WildPlaces.Phase(Game.Sim.Clock/Game.Sim.Config.Clock.TicksPerDay);
        for (int y = 0; y < Game.Sim.World.Height; y++) for (int x = 0; x < Game.Sim.World.Width; x++)
        {
            var tile = Game.Sim.World.TileAt(x, y); uint h = unchecked((uint)(x * 73856093 ^ y * 19349663));
            Vector3 p = PositionAt(x, y); float size = .8f + h % 7 * .07f;
            if(tile.BuildingId==0&&tile.Fire!=FireState.Burnt&&tile.Terrain is not (TerrainKind.Water or TerrainKind.Lava or TerrainKind.Road)&&h%11==0)
            {
                bool shore=false;for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)if(Game.Sim.World.IsInBounds(x+dx,y+dy)&&Game.Sim.World.TerrainAt(x+dx,y+dy)==TerrainKind.Water)shore=true;
                if(shore||tile.Terrain is TerrainKind.Mountain or TerrainKind.Sand||h%5==0)
                for(int i=0;i<(shore?4:2);i++)
                {
                    float px=p.X+MathF.Sin(h%791+i*2.41f)*.68f,pz=p.Z+MathF.Cos(h%613+i*1.87f)*.68f;
                    float scale=.10f+((h>>(i*3))%7)*.024f;
                    pebbles.Add(NatureModels.Transform(new(px,GroundHeight(px,pz)+scale*.18f,pz),new(scale,scale*.65f,scale*1.3f),new(.1f,i*1.7f+h%13,.12f)));
                }
            }
            bool living=tile.BuildingId==0&&tile.Fire!=FireState.Burnt;
            bool riverbank=false;var waterDirection=Vector2.Zero;
            if(HasRiverPlants(tile,h))
                for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                    if(Game.Sim.World.IsInBounds(x+dx,y+dy)&&Game.Sim.World.TerrainAt(x+dx,y+dy)==TerrainKind.Water){riverbank=true;waterDirection+=new Vector2(dx,dy);}
            if(riverbank)
            {
                waterDirection=waterDirection.Normalized();
                for(int candidate=0;candidate<4;candidate++)
                {
                    float px=p.X+waterDirection.X*(.35f+candidate*.15f)+MathF.Sin(h%719)*.22f;
                    float pz=p.Z+waterDirection.Y*(.35f+candidate*.15f)+MathF.Cos(h%431)*.22f;
                    var point=new Vector3(px,0,pz);
                    if(!SupportsReeds(point))continue;
                    reeds.Add(RootedPlant(point,Vector3.One*(.72f+(h%7)*.06f),h%31));break;
                }
            }
            if(living&&tile.Terrain==TerrainKind.Forest&&tile.Vegetation>.35f&&tile.Moisture>.2f&&h%9==0)
                for(int fern=0;fern<2;fern++)
                {
                    float px=p.X+MathF.Sin(h%613+fern*2.7f)*.48f,pz=p.Z+MathF.Cos(h%791+fern*2.7f)*.48f;
                    float scale=size*(.65f+fern*.21f);
                    ferns.Add(RootedPlant(new(px,0,pz),Vector3.One*scale,h%19+fern));
                }
            int meadowMask=MeadowCandidateMask(tile,x,y,h);
            for(int tuft=0;tuft<2;tuft++)
            {
                if((meadowMask&(1<<tuft))==0)continue;
                uint sample=MeadowSample(h,tuft);
                float patch=TerrainNoise(x*.12f,y*.12f,Game.Sim.World.Seed);
                float px=p.X+((sample>>8)&255)/255f*1.60f-.80f;
                float pz=p.Z+((sample>>16)&255)/255f*1.60f-.80f;
                float variation=.62f+patch*.48f+((sample>>24)&255)/255f*.30f;
                var clumpScale=tuft==0?new Vector3(size*1.12f,variation*.65f,size*1.12f):new Vector3(size*.90f,variation*1.10f,size*.90f);
                if(TryMeadowPlacement(new(px,0,pz),clumpScale,(sample%1000)*.006283185f,out var placement))grass.Add(placement);
            }
            if (tile.BuildingId==0&&tile.Terrain == TerrainKind.Forest && tile.Vegetation > .12f && tile.Fire != FireState.Burnt && h % 5 == 0)
            {
                float px=p.X+((h>>7)%101/100f-.5f)*1.35f,pz=p.Z+((h>>17)%101/100f-.5f)*1.35f;
                var tree=NatureModels.Transform(new(px,GroundHeight(px,pz)-.045f,pz),Vector3.One*(.76f+(h%17)*.035f),new Vector3(0,h%17,0));
                if(h%3==0)pines.Add(tree);else leaves.Add(tree);
            }
            if (tile.BuildingId==0&&tile.Terrain == TerrainKind.Mountain && h % 9 == 0)
            { var t = NatureModels.Transform(p-Vector3.Up*.08f, new Vector3(1.6f, 1.2f, 1.7f) * size, new Vector3(0,h%7,0)); if (tile.Resource.Kind == ResourceKind.Iron) ores.Add(t); else rocks.Add(t); }
            if (tile.BuildingId==0&&tile.Resource.Kind == ResourceKind.Food && tile.Resource.Amount > 5 && h % 17 == 0)
                { bushes.Add(RootedPlant(p, new Vector3(.9f, .7f, .9f) * size,h%31)); }
        }
        if(_groundMapTexture!=null)_models.BindEnvironmentGround(_groundMapTexture,new Vector2(Game.Sim.World.Width,Game.Sim.World.Height));
        _models.Batch(_props,"pebble",6,pebbles,_models.RockSurface(false),28);
        if(_groundMapTexture!=null)_models.BindMeadowGround(_groundMapTexture,new Vector2(Game.Sim.World.Width,Game.Sim.World.Height));
        _meadowBatches.Clear();
        _meadowSeeds.Clear();
        _models.BatchMesh(_props,_models.MeadowTurf(),grass,range:34,castShadows:false,padding:.04f,label:"MeadowTurf",register:_meadowBatches);
        _models.Batch(_props,"fern",4,ferns,null,38,true);
        _models.Batch(_props,"reeds",4,reeds,null,42);
        foreach (var place in Game.Wild.Places)
        {
            if (!Game.Sim.World.IsInBounds(place.X, place.Y) || !WildPlaces.IsLiving(Game.Sim, place)) continue;
            if(place.Kind==WildPlaceKind.Meadow)
            {
                var center=PositionAt(place.X,place.Y);float maturity=.30f+.70f*WildPlaces.Maturity(Game.Sim,place);
                for(int i=0;i<34;i++)
                {
                    float a=i*2.39996f,r=(.45f+Mathf.Sqrt(i/34f)*2.8f)*maturity;
                    var point=center+new Vector3(Mathf.Cos(a)*r,0,Mathf.Sin(a)*r);
                    int x=(int)MathF.Floor(point.X*.5f),y=(int)MathF.Floor(point.Z*.5f);if(!Game.Sim.World.IsInBounds(x,y))continue;
                    var tile=Game.Sim.World.TileAt(x,y);
                    if(tile.BuildingId!=0||tile.Fire==FireState.Burnt||tile.Terrain is TerrainKind.Water or TerrainKind.Lava or TerrainKind.Road)continue;
                    flowers[i%3].Add(RootedPlant(point,Vector3.One*maturity,a));
                }
                continue;
            }
            var model = _models.WildPlace(place.Kind, WildPlaces.Phase(Game.Sim.Clock / Game.Sim.Config.Clock.TicksPerDay), Game.Sim.GroundStocks.FindAt(place.X, place.Y) >= 0);
            model.Position = PositionAt(place.X, place.Y); if(place.Kind is WildPlaceKind.Meadow or WildPlaceKind.Berries or WildPlaceKind.Wetland)model.Scale=Vector3.One*(.30f+.70f*WildPlaces.Maturity(Game.Sim,place)); _props.AddChild(model);
        }
        for(int variant=0;variant<3;variant++)if(flowers[variant].Count>0)
            _models.BatchMesh(_props,_models.WildflowerMesh(variant,botanicalPhase<2),flowers[variant],range:42,castShadows:false,padding:.04f,label:"MeadowFlowers",register:_flowerBatches);
        _models.Batch(_props,"broad-tree",4,leaves,preserveMaterials:true);
        _models.Batch(_props,"conifer-tree",5,pines,preserveMaterials:true); _models.Batch(_props, "boulder", 6, rocks,preserveMaterials:true); _models.Batch(_props, "boulder", 7, ores,_models.RockSurface(true)); _models.Batch(_props,"foliage",4,bushes,preserveMaterials:true);
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
            if (_people.TryGetValue(id, out Node3D? old) && _personModels[id] != (child, job)) { old.QueueFree(); _people.Remove(id);_poseSchedule.Forget(id); }
            if (!_people.ContainsKey(id))
            {
                var person = _models.Human(slot, child, job); _actors.AddChild(person); person.Position = PositionAt(sim.Agents.XOf(slot), sim.Agents.YOf(slot));
                _people[id] = person; _personModels[id] = (child, job);
            }
        }
        foreach (long id in _people.Keys.Where(k => !live.Contains(k)).ToArray()) { _people[id].QueueFree(); _people.Remove(id); _personModels.Remove(id);_poseSchedule.Forget(id); }
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
            int slot = (int)(pair.Key & uint.MaxValue); if (!sim.Agents.IsSlotAlive(slot) || sim.Society.Identity(slot) != pair.Key) { _poseSchedule.Forget(pair.Key);continue; }
            var node = pair.Value; Vector3 target = PositionAt(sim.Agents.XOf(slot), sim.Agents.YOf(slot));
            var current=node.Position;Vector3 direction = target-current;
            bool moving = sim.Agents.PhaseOf(slot) == ActionPhase.Moving;
            var rendered=GroundedActorPosition(current,target,delta,10,Game.VisualPaused);
            if(direction.LengthSquared()>.000001f)node.Position=rendered;
            bool visible=ActorInView(rendered+Vector3.Up*.9f,frustum);
            if(node.Visible!=visible)node.Visible=visible;
            _poseSchedule.Accumulate(pair.Key,delta,visible&&!Game.VisualPaused);
            if(!visible)continue;
            if (!Game.VisualPaused && direction.X*direction.X+direction.Z*direction.Z > .002f) { node.Rotation = new Vector3(0, Mathf.LerpAngle(node.Rotation.Y,MathF.Atan2(direction.X, direction.Z) + MathF.PI,1-MathF.Exp(-delta*12)), 0); }
            if(!Game.VisualPaused&&sim.Agents.HasTarget(slot)&&sim.Agents.PhaseOf(slot)==ActionPhase.Executing)
            {
                var destination=sim.Agents.TargetOf(slot);var facing=sim.World.IsInBounds(destination)?PositionAt(destination.X,destination.Y)-target:Vector3.Zero;
                if(facing.LengthSquared()>.02f) node.Rotation=new(0,Mathf.LerpAngle(node.Rotation.Y,MathF.Atan2(facing.X,facing.Z)+MathF.PI,1-MathF.Exp(-delta*7)),0);
            }
            float distanceSquared=cameraPosition.DistanceSquaredTo(rendered);
            // Keep position interpolation every frame; distant joint poses need fewer updates.
            int stride=distanceSquared>45*45?8:distanceSquared>30*30?6:distanceSquared>12*12?2:1;
            var rig=(ResidentRig)node;
            rig.UpdateDistanceDetail(distanceSquared);
            if((_poseFrame+slot)%stride!=0)continue;
            var cargoKind=ResourceKind.Food;float cargoAmount=0;
            foreach(var resource in ResidentRig.CargoKinds)
            {float amount=sim.Agents.InventoryOf(slot,resource);if(amount>cargoAmount){cargoAmount=amount;cargoKind=resource;}}
            rig.ShowCargo(cargoAmount>.01f,cargoKind);
            var interest=sim.Agents.TargetOf(slot);
            var attention=sim.Agents.HasTarget(slot)&&sim.World.IsInBounds(interest)?PositionAt(interest.X,interest.Y)-rendered:Vector3.Zero;
            rig.Attend(node.Basis.Inverse()*attention);
            rig.SetWorkCondition(sim.Agents.FatigueOf(slot),sim.Agents.HealthOf(slot));
            rig.PresentAction(_poseSchedule.Consume(pair.Key),sim.Agents.ActionOf(slot),sim.Agents.PhaseOf(slot),Game.VisualPaused,slot);
            if(distanceSquared<18*18&&!Game.VisualPaused)ConformFeet(rig);
        }
        foreach (var pair in _deer)
        { int slot = (int)(pair.Key & uint.MaxValue); if (sim.Wildlife.IsAlive(slot)) { MoveAnimal(pair.Value, PositionAt(sim.Wildlife.XOf(slot), sim.Wildlife.YOf(slot)), delta,sim.Wildlife.ActionOf(slot)==ActionKind.Eat); } }
        foreach (var wolf in sim.Predators.Wolves) if (_wolves.TryGetValue(wolf.Id, out Node3D? node)) { MoveAnimal(node, PositionAt(wolf.X, wolf.Y), delta); }
    }
    private void MoveAnimal(Node3D node, Vector3 target, float delta,bool grazing=false)
    {
        if(Game.VisualPaused)return;
        Vector3 direction = target - node.Position;
        var location=GroundedActorPosition(node.Position,target,delta,8,Game.VisualPaused);node.Position=location;
        if (direction.LengthSquared() > .02f) { node.Rotation = new Vector3(0, Mathf.LerpAngle(node.Rotation.Y,MathF.Atan2(direction.X, direction.Z) + MathF.PI,1-MathF.Exp(-delta*9)), 0); }
        if(_camera.Position.DistanceSquaredTo(node.Position)<24*24)
        {
            var space=node.Transform;var inverse=space.AffineInverse();
            ((AnimalRig)node).Pose(delta,direction.LengthSquared()>.02f,false,grazing,point=>{var world=space*point;world.Y=GroundHeight(world.X,world.Z);return (inverse*world).Y;});
        }
        else ((AnimalRig)node).Pose(delta,direction.LengthSquared()>.02f,false,grazing);
    }
}
