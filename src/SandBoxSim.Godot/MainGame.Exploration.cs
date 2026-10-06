using Godot;

namespace SandBoxSim.Client;

public partial class MainGame
{
    private static string TerrainLabel(SandBoxSim.Core.Environment.TerrainKind kind) => kind switch
    {
        SandBoxSim.Core.Environment.TerrainKind.Grass => "草地", SandBoxSim.Core.Environment.TerrainKind.Forest => "林地",
        SandBoxSim.Core.Environment.TerrainKind.Water => "水域", SandBoxSim.Core.Environment.TerrainKind.Mountain => "山地",
        SandBoxSim.Core.Environment.TerrainKind.Sand => "沙地", SandBoxSim.Core.Environment.TerrainKind.Farmland => "耕地",
        SandBoxSim.Core.Environment.TerrainKind.Road => "道路", SandBoxSim.Core.Environment.TerrainKind.Snow => "雪地",
        SandBoxSim.Core.Environment.TerrainKind.Swamp => "湿地", SandBoxSim.Core.Environment.TerrainKind.Desert => "旱地",
        _ => "熔岩"
    };
    private static string ResourceLabel(SandBoxSim.Core.Environment.ResourceKind kind) => kind switch
    {
        SandBoxSim.Core.Environment.ResourceKind.Food => "食物", SandBoxSim.Core.Environment.ResourceKind.Wood => "木材",
        SandBoxSim.Core.Environment.ResourceKind.Stone => "石料", SandBoxSim.Core.Environment.ResourceKind.Iron => "铁矿", _ => "无"
    };
    public void RememberPlace(string name)
    {
        if(string.IsNullOrWhiteSpace(name))name=Wild.At(SelectedX,SelectedY)?.Name ?? $"土地 {SelectedX},{SelectedY}";
        _status.Text=Wild.Remember(SelectedX,SelectedY,name) ? "已记下这片地方" : "名称最多 24 字，手记可保存 32 个地点";
    }
    public void LookAtPlace(int x,int y)
    {
        SelectTool(SandBoxSim.Core.Systems.PlayerTool.Inspect);
        _selectedX=x; _selectedY=y; FocusLocation(x,y); _discovery.Refresh();
    }
}
