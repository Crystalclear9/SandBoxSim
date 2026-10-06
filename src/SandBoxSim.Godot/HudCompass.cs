using Godot;

namespace SandBoxSim.Client;

/// <summary>World north projected into the orbit camera's horizontal screen basis.</summary>
internal partial class HudCompass : Control
{
    public WorldView3D View { get; set; } = null!;
    private float _yaw = float.NaN;
    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(22, 18);
        MouseFilter = MouseFilterEnum.Ignore;
        Resized += QueueRedraw;
    }
    public override void _Process(double delta)
    {
        if (_yaw == View.CameraYaw) { return; }
        _yaw = View.CameraYaw; QueueRedraw();
    }
    public override void _Draw()
    {
        if (float.IsNaN(_yaw)) { return; }
        var center = Size / 2;
        DrawArc(center, 7, 0, Mathf.Tau, 32, new Color(HudStyle.Accent, .4f), 1, true);
        var north = new Vector2(Mathf.Sin(_yaw), -Mathf.Cos(_yaw));
        var side = new Vector2(-north.Y, north.X);
        DrawColoredPolygon(new[] { center + north * 7, center + side * 2, center - north * 3, center - side * 2 }, HudStyle.Accent);
        DrawLine(center, center - north * 6, HudStyle.Muted, 1, true);
    }
}
