using Godot;

namespace SandBoxSim.Client;

/// <summary>Original generated art. Atlas regions retain the source alpha; simulation never reads visual data.</summary>
internal sealed class WorldArt
{
    public Texture2D? Terrain { get; private set; }
    public Texture2D? Objects { get; private set; }
    public void Load()
    {
        if (ResourceLoader.Exists("res://assets/textures/natural-terrain.png")) { Terrain = GD.Load<Texture2D>("res://assets/textures/natural-terrain.png"); }
        if (ResourceLoader.Exists("res://assets/textures/natural-objects.png")) { Objects = GD.Load<Texture2D>("res://assets/textures/natural-objects.png"); }
        GD.Print("WORLD_ART terrain=" + (Terrain?.GetSize().ToString() ?? "missing") + " objects=" + (Objects?.GetSize().ToString() ?? "missing"));
    }
    public static Rect2 Region(Texture2D texture, int index)
    {
        Vector2 cell = texture.GetSize() / 4;
        return new Rect2(new Vector2(index % 4, index / 4) * cell, cell);
    }
    public void DrawTerrain(CanvasItem canvas, Rect2 target, int index, Color tint)
    {
        if (Terrain != null) { canvas.DrawTextureRectRegion(Terrain, target, Region(Terrain, index), tint); }
    }
    public void DrawObject(CanvasItem canvas, Vector2 center, float size, int index, Color? tint = null)
    {
        if (Objects != null) { canvas.DrawTextureRectRegion(Objects, new Rect2(center - Vector2.One * size / 2, Vector2.One * size), Region(Objects, index), tint ?? Colors.White); }
    }
}
