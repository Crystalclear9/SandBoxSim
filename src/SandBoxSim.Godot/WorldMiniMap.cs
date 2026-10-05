using System;
using Godot;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;

public partial class WorldMiniMap : Control
{
    public MainGame Game { get; set; } = null!;
    private ImageTexture? _terrain;
    private SandBoxSim.Core.Simulation? _world;
    private ulong _lastRefresh;
    private Rect2 MapBounds
    {
        get
        {
            float scale = Math.Min(145f / Game.Sim.World.Width, Math.Max(1, Math.Max(Size.Y, CustomMinimumSize.Y) - 12) / Game.Sim.World.Height);
            return new Rect2(new Vector2(6, 6), new Vector2(Game.Sim.World.Width * scale, Game.Sim.World.Height * scale));
        }
    }
    public override void _Draw()
    {
        if (Size.X < 10 || Size.Y < 20) { return; }
        var sim = Game.Sim; ulong now = Time.GetTicksMsec();
        if (_terrain == null || !ReferenceEquals(_world, sim) || now - _lastRefresh > 1000)
        {
            _world = sim; _lastRefresh = now;
            var image = Image.CreateEmpty(sim.World.Width, sim.World.Height, false, Image.Format.Rgb8);
            for (int y = 0; y < sim.World.Height; y++) for (int x = 0; x < sim.World.Width; x++)
            {
                var tile = sim.World.TileAt(x, y);
                Color color = tile.Terrain switch { TerrainKind.Water => new Color("#467f80"), TerrainKind.Forest => new Color("#3d624b"),
                    TerrainKind.Mountain => new Color("#888a7b"), TerrainKind.Road => new Color("#baa98a"), TerrainKind.Desert or TerrainKind.Sand => new Color("#a99563"),
                    TerrainKind.Farmland => new Color("#77844a"), _ => new Color("#6e8157") };
                if (tile.Fire == FireState.Burnt) { color = new Color("#403f36"); }
                image.SetPixel(x, y, color);
            }
            _terrain = ImageTexture.CreateFromImage(image);
        }
        var rect = MapBounds; DrawStyleBox(HudStyle.Box(HudStyle.Wash, 2, 4, false), new Rect2(Vector2.Zero, Size));
        DrawTextureRect(_terrain, rect, false); DrawRect(rect, HudStyle.Border, false, 1);
        Vector2 Position(int x, int y) => rect.Position + new Vector2((x + .5f) / sim.World.Width * rect.Size.X, (y + .5f) / sim.World.Height * rect.Size.Y);
        foreach (int slot in sim.Agents.AliveSlots()) { DrawCircle(Position(sim.Agents.XOf(slot), sim.Agents.YOf(slot)), 1.5f, HudStyle.Surface); }
        foreach (var p in Game.Projects.Items) if (p.Active || p.Managed) { DrawCircle(Position(p.X, p.Y), p.Radius / (float)sim.World.Width * rect.Size.X, new Color("#9dcec0"), false, 1.5f); }
        for (int i = 0; i < sim.World.Tiles.Length; i++) if (sim.World.Tiles[i].Fire == FireState.Burning) { DrawCircle(Position(i % sim.World.Width, i / sim.World.Width), 2, new Color("#ee9367")); }
        var person = sim.Society.Find(Game.PinnedPerson);
        if (person?.Alive == true) { DrawCircle(Position(sim.Agents.XOf(person.Slot), sim.Agents.YOf(person.Slot)), 4, HudStyle.Accent, false, 1.5f); }
        var font = GetThemeDefaultFont(); float left = rect.End.X + 15;
        void Text(string value, float y, Color color, int size = 12) => DrawString(font, new Vector2(left, y), value, HorizontalAlignment.Left, -1, size, color);
        Text("N ↑   区域地图", 24, HudStyle.Muted, 11);
        Text("居民  " + sim.Agents.LiveCount, 54, HudStyle.Text, 15);
        Text("聚落  " + sim.Settlements.ActiveCount, 78, HudStyle.Muted);
        Text("工程  " + Game.Projects.ActiveCount + " / 4", 102, HudStyle.Accent);
        Text(Game.Trial.Running ? "额度  " + (int)Game.Trial.Influence : "自由沙盒", 129, HudStyle.Accent);
    }
    public Int2? TileAt(Vector2 point)
    {
        var rect = MapBounds;
        if (!rect.HasPoint(point)) { return null; }
        return new Int2(Math.Clamp((int)((point.X - rect.Position.X) / rect.Size.X * Game.Sim.World.Width), 0, Game.Sim.World.Width - 1),
            Math.Clamp((int)((point.Y - rect.Position.Y) / rect.Size.Y * Game.Sim.World.Height), 0, Game.Sim.World.Height - 1));
    }
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton button && button.Pressed && button.ButtonIndex == MouseButton.Left)
        {
            var point = TileAt(button.Position); if (point.HasValue) { Game.FocusLocation(point.Value.X, point.Value.Y); AcceptEvent(); }
        }
    }
    public void ValidateMapping()
    {
        var rect = MapBounds; var center = TileAt(rect.GetCenter());
        if (!center.HasValue || center.Value.X != Game.Sim.World.Width / 2 || center.Value.Y != Game.Sim.World.Height / 2 || TileAt(new Vector2(-10, -10)).HasValue)
            { throw new InvalidOperationException("Minimap does not map world coordinates correctly"); }
    }
}
