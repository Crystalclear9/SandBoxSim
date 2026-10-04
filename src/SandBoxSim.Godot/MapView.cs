using System;
using Godot;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Systems;

namespace SandBoxSim.Client;

public partial class MapView : Control
{
    public MainGame Game { get; set; } = null!;
    public int Overlay { get; set; }
    private float _scale = 7;
    private Vector2 _offset = new(30, 30);
    private bool _dragging, _painting;
    private Vector2I _lastPaint = new(-1, -1);
    private int[] _density = Array.Empty<int>();
    private readonly WorldArt _art = new();
    private Vector2 _mouse;
    private int _follow = -1, _followGeneration;
    private readonly System.Collections.Generic.List<(Vector2I Tile, double Born, Color Color, string Text)> _effects = new();
    private ImageTexture? _mini;
    private long _miniTick = -10000;
    public override void _Ready() { _art.Load(); MouseFilter = MouseFilterEnum.Stop; Resized += Center; Center(); }
    public virtual void Focus(int x, int y, float zoom = 15)
    {
        _scale = zoom; _offset = Size / 2 - new Vector2(x + .5f, y + .5f) * _scale; QueueRedraw();
    }
    public virtual void Follow(int slot)
    {
        _follow = slot; _followGeneration = Game.Sim.Agents.GenerationOf(slot);
    }
    public virtual void Effect(int x, int y, Color color, string text = "")
    {
        if (_effects.Count >= 30) { _effects.RemoveAt(0); }
        _effects.Add((new Vector2I(x, y), Time.GetTicksMsec() / 1000.0, color, text)); QueueRedraw();
    }
    public virtual void Center()
    {
        _follow = -1;
        _scale = MathF.Min(Size.X / Game.Sim.World.Width, Size.Y / Game.Sim.World.Height) * 0.92f;
        _offset = (Size - new Vector2(Game.Sim.World.Width, Game.Sim.World.Height) * _scale) / 2;
        QueueRedraw();
    }
    private Vector2 Point(int x, int y) => _offset + new Vector2(x + 0.5f, y + 0.5f) * _scale;
    private Vector2I Tile(Vector2 position) => new((int)MathF.Floor((position.X - _offset.X) / _scale),
        (int)MathF.Floor((position.Y - _offset.Y) / _scale));
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton button)
        {
            _mouse = button.Position;
            if (button.ButtonIndex == MouseButton.Right && button.Pressed) { Game.SelectTool(PlayerTool.Inspect); _painting = false; }
            if (button.ButtonIndex == MouseButton.Middle) { _dragging = button.Pressed; }
            if (button.ButtonIndex == MouseButton.Left)
            {
                _painting = button.Pressed && Game.Tool != PlayerTool.Inspect;
                _lastPaint = new(-1, -1);
                if (button.Pressed)
                {
                    var miniBounds = new Rect2(Size.X - 160, 18, 142, 142);
                    if (miniBounds.HasPoint(button.Position))
                    { Vector2 local = (button.Position - miniBounds.Position) / miniBounds.Size; Focus((int)(local.X * 100), (int)(local.Y * 100), MathF.Max(_scale, 12)); _painting = false; }
                    else { Paint(button.Position); }
                }
            }
            if (button.Pressed && (button.ButtonIndex == MouseButton.WheelUp || button.ButtonIndex == MouseButton.WheelDown))
            {
                float next = Math.Clamp(_scale * (button.ButtonIndex == MouseButton.WheelUp ? 1.15f : 1 / 1.15f), 2, 40);
                _offset = button.Position - (button.Position - _offset) * (next / _scale); _scale = next; QueueRedraw();
            }
        }
        if (input is InputEventMouseMotion motion)
        {
            _mouse = motion.Position;
            if (_dragging) { _follow = -1; _offset += motion.Relative; QueueRedraw(); }
            if (_painting) { Paint(motion.Position); }
        }
        AcceptEvent();
    }
    private void Paint(Vector2 position)
    {
        var tile = Tile(position); if (tile == _lastPaint) { return; }
        _lastPaint = tile; Game.ClickTile(tile.X, tile.Y); QueueRedraw();
    }
    private static Color TerrainColor(TerrainKind kind) => kind switch
    {
        TerrainKind.Grass => new Color("#566b42"), TerrainKind.Forest => new Color("#253f36"),
        TerrainKind.Water => new Color("#315f79"), TerrainKind.Mountain => new Color("#707574"),
        TerrainKind.Sand => new Color("#b49c6c"), TerrainKind.Farmland => new Color("#97865a"),
        TerrainKind.Road => new Color("#897f6c"), TerrainKind.Snow => new Color("#b9cbd0"),
        TerrainKind.Swamp => new Color("#486566"), TerrainKind.Desert => new Color("#c49454"),
        _ => new Color("#a84e36")
    };
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("#111d22"));
        var sim = Game.Sim;
        if (_follow >= 0 && sim.Agents.IsSlotAlive(_follow) && sim.Agents.GenerationOf(_follow) == _followGeneration)
        { _offset = Size / 2 - new Vector2(sim.Agents.XOf(_follow) + .5f, sim.Agents.YOf(_follow) + .5f) * _scale; }
        if (Overlay == 6)
        {
            if (_density.Length != sim.World.TileCount) { _density = new int[sim.World.TileCount]; }
            Array.Clear(_density);
            foreach (int slot in sim.Agents.AliveSlots())
                for (int dy = -3; dy <= 3; dy++)
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        int x = sim.Agents.XOf(slot) + dx, y = sim.Agents.YOf(slot) + dy;
                        if (sim.World.IsInBounds(x, y)) { _density[y * sim.World.Width + x]++; }
                    }
        }
        for (int y = 0; y < sim.World.Height; y++)
            for (int x = 0; x < sim.World.Width; x++)
            {
                Vector2 p = _offset + new Vector2(x, y) * _scale;
                if (p.X > Size.X || p.Y > Size.Y || p.X + _scale < 0 || p.Y + _scale < 0) { continue; }
                var tile = sim.World.TileAt(x, y); Color color = TerrainColor(tile.Terrain);
                if (Overlay == 1)
                {
                    int id = sim.Society.TerritoryAt(x, y);
                    if (id > 0) { color = color.Lerp(Color.FromHsv((id * 0.618034f) % 1, 0.6f, 0.75f), 0.6f); }
                }
                if (Overlay == 2) { color = new Color(0.15f, Math.Clamp(tile.Resource.Amount / 80, 0.15f, 0.9f), 0.25f); }
                if (Overlay == 3) { color = new Color(0.1f, tile.Moisture * 0.6f, 0.3f + tile.Moisture * 0.65f); }
                if (Overlay == 4) { color = new Color(0.2f, 0.15f + tile.Fertility * 0.7f, 0.15f); }
                if (Overlay == 5) { color = new Color(0.12f, 0.15f + (tile.Resource.Kind == ResourceKind.Food ? Math.Clamp(tile.Resource.Amount / 80, 0, 0.8f) : 0), 0.1f); }
                if (Overlay == 6) { color = new Color(0.12f + Math.Clamp(_density[y * sim.World.Width + x] / 20f, 0, 0.8f), 0.2f, 0.3f); }
                if (tile.Fire == FireState.Burning) { color = new Color("#e57737"); }
                if (tile.Fire == FireState.Burnt) { color = new Color("#30302d"); }
                var rect = new Rect2(p, new Vector2(_scale + 0.5f, _scale + 0.5f));
                DrawRect(rect, color);
                int terrainIndex = tile.Fire == FireState.Burnt ? 14 : (int)tile.Terrain;
                float shade = .88f + tile.Height * .15f + (float)Math.Sin(x * 13.1 + y * 7.7) * .025f;
                Color tint = Overlay is >= 1 and <= 6 ? color.Lightened(.3f) : Colors.White * shade;
                tint.A = 1;
                _art.DrawTerrain(this, rect, terrainIndex, tint);
                if (tile.Terrain != TerrainKind.Water && tile.Fire != FireState.Burnt)
                    for (int edge = 0; edge < 2; edge++)
                    {
                        int nx = x + (edge == 0 ? 1 : 0), ny = y + (edge == 1 ? 1 : 0);
                        if (sim.World.IsInBounds(nx, ny) && sim.World.TerrainAt(nx, ny) == TerrainKind.Water)
                        { Vector2 a = p + (edge == 0 ? new Vector2(_scale, 0) : new Vector2(0, _scale)); DrawLine(a, a + (edge == 0 ? new Vector2(0, _scale) : new Vector2(_scale, 0)), new Color("#b4ac87"), MathF.Max(1, _scale * .12f)); }
                    }
            }
        if (Overlay == 0 || Overlay >= 7)
            for (int y = 0; y < sim.World.Height; y++)
                for (int x = 0; x < sim.World.Width; x++)
                {
                    var p = Point(x, y); if (p.X < -40 || p.X > Size.X + 40 || p.Y < -40 || p.Y > Size.Y + 40) { continue; }
                    var tile = sim.World.TileAt(x, y);
                    uint variation = unchecked((uint)(x * 73856093 ^ y * 19349663));
                    if (tile.Terrain == TerrainKind.Forest && tile.Vegetation > .15f && tile.Fire != FireState.Burnt && variation % 3 != 0)
                        { _art.DrawObject(this, p + new Vector2((variation % 5 - 2f) * .1f, -.35f) * _scale, _scale * 2.0f, (int)(variation % 2)); }
                    else if (tile.Terrain == TerrainKind.Mountain && variation % 3 == 0) { _art.DrawObject(this, p, _scale * 1.8f, tile.Resource.Kind == ResourceKind.Iron ? 3 : 2); }
                    else if (tile.Resource.Kind == ResourceKind.Food && tile.Resource.Amount > 5 && variation % 11 == 0) { _art.DrawObject(this, p, _scale * 1.2f, 14); }
                    if (tile.Fire == FireState.Burning) { _art.DrawObject(this, p, _scale * 2, 15); }
                }
        for (int i = 0; i < sim.Buildings.Capacity; i++)
            if (sim.Buildings.IsAlive(i))
            {
                var center = Point(sim.Buildings.XOf(i), sim.Buildings.YOf(i));
                Color color = sim.Buildings.KindOf(i) switch { BuildingKind.House => new Color("#e5d6b0"),
                    BuildingKind.Storage => new Color("#d7ad68"), BuildingKind.Farm => new Color("#b4bd76"), _ => new Color("#a7aab5") };
                DrawRect(new Rect2(center - Vector2.One * _scale * 0.4f, Vector2.One * _scale * 0.8f), color,
                    sim.Buildings.StateOf(i) == BuildingState.Complete, 1);
                int sprite = sim.Buildings.KindOf(i) switch { BuildingKind.House => 4, BuildingKind.Storage => 5, BuildingKind.Farm => 6, _ => 7 };
                _art.DrawObject(this, center - new Vector2(0, _scale * .35f), _scale * 2.5f, sprite,
                    sim.Buildings.StateOf(i) == BuildingState.Complete ? Colors.White : new Color(1, 1, 1, .5f));
            }
        for (int i = 0; i < sim.Wildlife.Capacity; i++)
            if (sim.Wildlife.IsAlive(i)) { _art.DrawObject(this, Point(sim.Wildlife.XOf(i), sim.Wildlife.YOf(i)), MathF.Max(9, _scale * 1.5f), 12); }
        foreach (int slot in sim.Agents.AliveSlots())
        {
            var p = Point(sim.Agents.XOf(slot), sim.Agents.YOf(slot));
            int sprite = sim.Agents.JobOf(slot) switch { SandBoxSim.Core.Agents.JobType.Farmer => 10, SandBoxSim.Core.Agents.JobType.Soldier => 11, _ => 8 + slot % 2 };
            float bob = sim.Agents.PhaseOf(slot) == SandBoxSim.Core.Agents.ActionPhase.Moving ? (float)Math.Sin(Time.GetTicksMsec() * .012 + slot) * _scale * .06f : 0;
            float humanSize = sim.Agents.LifeStageOf(slot) == SandBoxSim.Core.Agents.LifeStage.Child ? .85f : 1.2f;
            _art.DrawObject(this, p + new Vector2(0, bob - _scale * .25f), MathF.Max(10, _scale * humanSize), sprite,
                sim.Diseases.OfSlot(slot)?.Active == true ? new Color(.8f, .75f, 1) : Colors.White);
            if (Overlay == 7) { DrawCircle(p, MathF.Max(2.5f, _scale * 0.3f), Color.FromHsv(((int)sim.Agents.ActionOf(slot) * 0.618034f) % 1, 0.7f, 1)); }
            if ((Overlay == 8 || slot == Game.SelectedSlot) && sim.Agents.HasTarget(slot))
            {
                var target = sim.Agents.TargetOf(slot);
                DrawLine(p, Point(target.X, target.Y), new Color(1, 0.85f, 0.4f, 0.7f));
                DrawArc(Point(target.X, target.Y), MathF.Max(3, _scale * 0.4f), 0, MathF.Tau, 12, Colors.Gold);
            }
            if (slot == Game.SelectedSlot) { DrawArc(p, _scale * .6f, 0, MathF.Tau, 24, new Color("#f1d393"), 2); }
            if (_scale >= 20 && sim.Agents.HealthOf(slot) < .7f) { DrawLine(p + new Vector2(-6, -_scale), p + new Vector2(-6 + 12 * sim.Agents.HealthOf(slot), -_scale), new Color("#a9c77f"), 2); }
        }
        foreach (var caravan in sim.Civilizations.Caravans)
            if (!caravan.Delivered) { DrawRect(new Rect2(Point(caravan.X, caravan.Y) - Vector2.One * 3, Vector2.One * 6), new Color("#ffc85c")); }
        foreach (var wolf in sim.Predators.Wolves)
            _art.DrawObject(this, Point(wolf.X, wolf.Y), MathF.Max(10, _scale * 1.5f), 13);
        for (int i = 0; i < sim.Settlements.EntityCount; i++)
        {
            var settlement = sim.Settlements.At(i); if (settlement.Dissolved) { continue; }
            Vector2 center = Point(settlement.CenterX, settlement.CenterY);
            DrawArc(center, _scale * 4, 0, MathF.Tau, 32, new Color(1, 0.88f, 0.65f, 0.45f), 1);
            DrawString(Game.Theme.DefaultFont, center + new Vector2(8, -18), sim.Society.SettlementName(settlement.Id), fontSize: 15, modulate: new Color("#f3e6c9"));
        }
        double now = Time.GetTicksMsec() / 1000.0;
        _effects.RemoveAll(e => now - e.Born > 2.5);
        foreach (var e in _effects)
        {
            float age = (float)(now - e.Born); Color c = e.Color; c.A = MathF.Max(0, 1 - age / 2.5f);
            Vector2 p = Point(e.Tile.X, e.Tile.Y);
            DrawArc(p, _scale * (1 + age * 2), 0, MathF.Tau, 40, c, 2);
            if (e.Text.Length > 0) { DrawString(Game.Theme.DefaultFont, p + new Vector2(12, -18 - age * 18), e.Text, fontSize: 16, modulate: c); }
        }
        if (Game.Tool != PlayerTool.Inspect && GetRect().HasPoint(_mouse))
        {
            var tile = Tile(_mouse);
            DrawArc(Point(tile.X, tile.Y), MathF.Max(_scale * .6f, Game.Radius * _scale), 0, MathF.Tau, 48, new Color(1, .86f, .6f, .8f), 1.5f);
        }
        DrawMiniMap();
    }
    private void DrawMiniMap()
    {
        var sim = Game.Sim;
        if (_mini == null || sim.Clock - _miniTick > 10 || sim.Clock < _miniTick)
        {
            var image = Image.CreateEmpty(100, 100, false, Image.Format.Rgba8);
            for (int y = 0; y < 100; y++) for (int x = 0; x < 100; x++) { image.SetPixel(x, y, TerrainColor(sim.World.TerrainAt(x, y))); }
            foreach (int slot in sim.Agents.AliveSlots()) { image.SetPixel(sim.Agents.XOf(slot), sim.Agents.YOf(slot), new Color("#f1d393")); }
            if (_mini == null) { _mini = ImageTexture.CreateFromImage(image); } else { _mini.Update(image); }
            _miniTick = sim.Clock;
        }
        var rect = new Rect2(Size.X - 160, 18, 142, 142);
        DrawRect(rect.Grow(4), new Color(.06f, .09f, .07f, .9f)); DrawTextureRect(_mini, rect, false);
        Vector2 viewPos = -_offset / _scale / 100 * rect.Size + rect.Position;
        var view = new Rect2(viewPos, Size / _scale / 100 * rect.Size).Intersection(rect);
        DrawRect(view, new Color("#e4d4a3"), false, 1);
        DrawString(Game.Theme.DefaultFont, new Vector2(rect.Position.X, 181), "点击概览 · 滚轮缩放", fontSize: 12, modulate: new Color("#c7cbb9"));
    }
}
