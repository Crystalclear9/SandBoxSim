using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;

public partial class WorldView3D
{
    private MultiMeshInstance3D _flames = null!, _smoke = null!, _illness = null!;
    private readonly Dictionary<int, Node3D> _projectMarks = new();
    private MeshInstance3D _blueprintRing = null!;
    private void BuildHazardVisuals()
    {
        _blueprintRing = Ring(HudStyle.Accent);
        _blueprintRing.Mesh = new TorusMesh { InnerRadius = .998f, OuterRadius = 1, Rings = 80, RingSegments = 6 };
        _blueprintRing.Scale = new Vector3(20, .5f, 20); _scene.AddChild(_blueprintRing);
        var flameShader = new Shader { Code = @"
shader_type spatial;
render_mode unshaded,cull_disabled,depth_draw_never;
varying float phase;
float hash(vec2 p){ return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453); }
float noise(vec2 p){
    vec2 i=floor(p),f=fract(p); f=f*f*(3.0-2.0*f);
    return mix(mix(hash(i),hash(i+vec2(1.0,0.0)),f.x),mix(hash(i+vec2(0.0,1.0)),hash(i+vec2(1.0)),f.x),f.y);
}
void vertex(){
    phase=MODEL_MATRIX[3].x*.37+MODEL_MATRIX[3].z*.21;
    MODELVIEW_MATRIX=VIEW_MATRIX*mat4(INV_VIEW_MATRIX[0],INV_VIEW_MATRIX[1],INV_VIEW_MATRIX[2],MODEL_MATRIX[3]);
}
void fragment(){
    float h=1.0-UV.y;
    float n=noise(vec2(UV.x*7.0+phase,h*8.0-TIME*3.7));
    float wave=sin(h*10.0-TIME*6.0+phase)*.055*h;
    float width=.40*pow(max(.001,1.0-h),.7);
    float edge=1.0-smoothstep(width-.10,width,abs(UV.x-.5+wave));
    float density=edge*(1.0-smoothstep(.65,1.0,h+n*.22))*(.65+n*.35);
    vec3 c=mix(vec3(1.0,.85,.32),vec3(1.0,.15,.015),smoothstep(.05,.85,h));
    c=mix(c,vec3(1.0,.98,.75),edge*(1.0-h)*.55);
    ALBEDO=c; EMISSION=c*.35; ALPHA=density;
}" };
        _flames = BatchVisual(new QuadMesh { Size = new Vector2(2.2f, 3.2f) },
            new ShaderMaterial { Shader = flameShader });
        _smoke = BatchVisual(new SphereMesh { Radius = .8f, Height = 1.6f, RadialSegments = 8, Rings = 4 },
            new StandardMaterial3D { AlbedoColor = new Color(.19f, .19f, .17f, .4f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded });
        _illness = BatchVisual(new SphereMesh { Radius = .18f, Height = .36f, RadialSegments = 8, Rings = 4 },
            new StandardMaterial3D { AlbedoColor = new Color("#c89edb"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded });
    }
    private MultiMeshInstance3D BatchVisual(Mesh mesh, Material material)
    {
        var visual = new MultiMeshInstance3D { Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh },
            MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _scene.AddChild(visual); return visual;
    }
    private static void SetInstances(MultiMeshInstance3D visual, List<Transform3D> transforms)
    {
        visual.Multimesh.InstanceCount = transforms.Count;
        for (int i = 0; i < transforms.Count; i++) { visual.Multimesh.SetInstanceTransform(i, transforms[i]); }
    }
    private void SyncHazards()
    {
        var flames = new List<Transform3D>(); var smoke = new List<Transform3D>(); var illness = new List<Transform3D>();
        var sim = Game.Sim;
        _blueprintRing.Visible = Game.Blueprint.Kind >= 0;
        if (_blueprintRing.Visible) _blueprintRing.Position = PositionAt(Game.Blueprint.X, Game.Blueprint.Y) + Vector3.Up * .12f;
        for (int i = 0; i < sim.World.Tiles.Length; i++)
        {
            if (sim.World.Tiles[i].Fire != FireState.Burning) { continue; }
            Vector3 p = PositionAt(i % sim.World.Width, i / sim.World.Width);
            flames.Add(new Transform3D(Basis.Identity, p + Vector3.Up * 1.2f));
            if (smoke.Count < 256) { smoke.Add(new Transform3D(Basis.Identity.Scaled(new Vector3(1, 1.6f, 1)), p + Vector3.Up * 4)); }
        }
        foreach (int slot in sim.Agents.AliveSlots())
            if (sim.Diseases.OfSlot(slot)?.Active == true) { illness.Add(new Transform3D(Basis.Identity, PositionAt(sim.Agents.XOf(slot), sim.Agents.YOf(slot)) + Vector3.Up * 2.3f)); }
        SetInstances(_flames, flames); SetInstances(_smoke, smoke); SetInstances(_illness, illness);
        var active = new HashSet<int>();
        foreach (var p in Game.Projects.Items)
        {
            if (!p.Active && !p.Managed) { continue; } active.Add(p.Id);
            if (!_projectMarks.TryGetValue(p.Id, out var root))
            {
                root = new Node3D(); _scene.AddChild(root); _projectMarks[p.Id] = root;
                var ring = Ring(new Color("#9dcec0")); ring.Visible = true; root.AddChild(ring);
                ring.Mesh = new TorusMesh { InnerRadius = .996f, OuterRadius = 1, Rings = 64, RingSegments = 6 };
                root.AddChild(new Label3D { Name = "Title", Font = Game.Theme.DefaultFont, FontSize = 22, PixelSize = .014f,
                    Position = Vector3.Up * 2.3f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Modulate = new Color("#b5e2cd") });
            }
            root.Position = PositionAt(p.X, p.Y) + Vector3.Up * .1f;
            root.GetChild<Node3D>(0).Scale = new Vector3(p.Radius * 2, .7f, p.Radius * 2);
            root.GetNode<Label3D>("Title").Text = Game.Projects.Recipe(p.Kind).Name + (p.Active ? $"  {p.Stage}/{p.Duration}" : " · " + SandBoxSim.Core.Systems.LandProjects.Policies[p.Policy]);
        }
        foreach (int id in new List<int>(_projectMarks.Keys)) if (!active.Contains(id)) { _projectMarks[id].QueueFree(); _projectMarks.Remove(id); }
    }
    public void ValidateHazardVisuals()
    {
        SyncHazards();
        int burning = 0, sick = 0;
        foreach (var tile in Game.Sim.World.Tiles) { if (tile.Fire == FireState.Burning) { burning++; } }
        foreach (int slot in Game.Sim.Agents.AliveSlots()) { if (Game.Sim.Diseases.OfSlot(slot)?.Active == true) { sick++; } }
        if (_flames.Multimesh.InstanceCount != burning || _illness.Multimesh.InstanceCount != sick || _projectMarks.Count != Game.Projects.ActiveCount + Game.Projects.ManagedCount)
            { throw new System.InvalidOperationException("Hazard visuals do not match physical world state"); }
    }
}
