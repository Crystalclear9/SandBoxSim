using System;
using System.Collections.Generic;
using System.Linq;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Systems;

public enum WildPlaceKind { Spring, Berries, OldGrove, Ruins, Ore }

public sealed class WildPlace
{
    public WildPlaceKind Kind;
    public int X, Y;
    public string Name = "";
}
public sealed class PlaceMark { public int X, Y; public string Name = ""; }

/// <summary>Opt-in living geography for graphical worlds. No goals, discovery counters or rewards.</summary>
public sealed class WildPlaces
{
    private readonly List<WildPlace> _places = new();
    private readonly List<PlaceMark> _marks = new();
    public IReadOnlyList<WildPlace> Places => _places;
    public IReadOnlyList<PlaceMark> Marks => _marks;
    public bool Remember(int x, int y, string name)
    {
        name=name.Trim(); if(x<0 || y<0 || x>4096 || y>4096 || name.Length==0 || name.Length>24)return false;
        var mark=_marks.FirstOrDefault(m=>m.X==x && m.Y==y);
        if(mark!=null) { mark.Name=name; return true; }
        if(_marks.Count>=32)return false;
        _marks.Add(new PlaceMark { X=x,Y=y,Name=name });return true;
    }
    public void Forget(int x,int y) => _marks.RemoveAll(m=>m.X==x && m.Y==y);
    public long LastDay { get; private set; }
    public static int Phase(long day) => (int)(Math.Max(0, day) / 12 % 4);
    public static string PhaseName(long day) => new[] { "萌芽期", "丰盛期", "落叶期", "休眠期" }[Phase(day)];
    private static uint Mix(uint n) { unchecked { n ^= n >> 16; n *= 0x7feb352d; n ^= n >> 15; n *= 0x846ca68b; return n ^ (n >> 16); } }
    public static WildPlaces Create(Simulation sim)
    {
        var result = new WildPlaces { LastDay = sim.Clock / sim.Config.Clock.TicksPerDay };
        if (sim.World.Width < 24 || sim.World.Height < 24) return result;
        int count = Math.Min(15, sim.World.Width * sim.World.Height / 500);
        for (int index = 0; index < count; index++)
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                uint hash = Mix(unchecked((uint)sim.World.Seed + (uint)index * 701u + (uint)attempt * 7919u));
                int x = 6 + (int)(hash % (uint)(sim.World.Width - 12));
                int y = 6 + (int)(Mix(hash) % (uint)(sim.World.Height - 12));
                if (result._places.Any(p => (p.X-x)*(p.X-x)+(p.Y-y)*(p.Y-y) < 196)) continue;
                if (sim.Agents.AliveSlots().Any(s => (sim.Agents.XOf(s)-x)*(sim.Agents.XOf(s)-x)+(sim.Agents.YOf(s)-y)*(sim.Agents.YOf(s)-y) < 144)) continue;
                var tile = sim.World.TileAt(x, y);
                if (!tile.Walkable || tile.BuildingId > 0 || tile.Fire != FireState.None) continue;
                var kind = (WildPlaceKind)(index % 5);
                string[] names = { "苔石泉", "野果洼地", "古木林", "旧聚落遗迹", "赤土岩脉" };
                var p = new WildPlace { Kind = kind, X = x, Y = y, Name = names[(int)kind] };
                result._places.Add(p); Initialize(sim, p); break;
            }
        }
        sim.World.RefreshSpatialIndex();
        return result;
    }
    private static void Initialize(Simulation sim, WildPlace p)
    {
        for (int dy = -3; dy <= 3; dy++) for (int dx = -3; dx <= 3; dx++)
        {
            if (dx*dx+dy*dy > 9) continue;
            int x=p.X+dx, y=p.Y+dy; var t=sim.World.TileAt(x,y);
            if (t.BuildingId > 0 || t.Terrain == TerrainKind.Water) continue;
            TerrainKind kind=p.Kind == WildPlaceKind.OldGrove ? TerrainKind.Forest : p.Kind == WildPlaceKind.Ore ? TerrainKind.Mountain : TerrainKind.Grass;
            sim.World.SetTerrain(x,y,kind); sim.World.ApplyDefaultResource(x,y);
            sim.World.Tiles[y*sim.World.Width+x].Height = p.Kind == WildPlaceKind.Ore ? .62f+(dx*dx+dy*dy)*.012f : .43f;
            sim.World.SetMoisture(x,y,p.Kind == WildPlaceKind.Ore ? .22f : .68f);
            sim.World.SetFertility(x,y,p.Kind == WildPlaceKind.Berries ? .82f : .6f);
            sim.World.SetVegetation(x,y,p.Kind == WildPlaceKind.OldGrove ? 1 : .58f);
            if (p.Kind == WildPlaceKind.Ore) sim.World.SetResource(x,y,new ResourceNode { Kind=(dx+dy)%3 == 0 ? ResourceKind.Iron : ResourceKind.Stone, Amount=58, Capacity=58, RegenerationRate=0 });
        }
        if (p.Kind == WildPlaceKind.Spring)
        {
            sim.World.SetTerrain(p.X,p.Y,TerrainKind.Water); sim.World.ClearResource(p.X,p.Y);
            sim.World.Tiles[p.Y*sim.World.Width+p.X].Height=.18f;
        }
        if (p.Kind == WildPlaceKind.Ruins)
        {
            sim.GroundStocks.Deposit(p.X,p.Y,ResourceKind.Stone,42,sim.Config.GroundStocks);
            sim.GroundStocks.Deposit(p.X,p.Y,ResourceKind.Wood,24,sim.Config.GroundStocks);
        }
    }
    public static bool IsLiving(Simulation sim, WildPlace p)
    {
        if (!sim.World.IsInBounds(p.X,p.Y)) return false;
        var t=sim.World.TileAt(p.X,p.Y);
        if (t.BuildingId > 0 || t.Fire != FireState.None) return false;
        return p.Kind switch { WildPlaceKind.Spring => t.Terrain == TerrainKind.Water, WildPlaceKind.OldGrove => t.Terrain == TerrainKind.Forest,
            WildPlaceKind.Ore => t.Terrain == TerrainKind.Mountain, _ => t.Terrain == TerrainKind.Grass };
    }
    public void Advance(Simulation sim)
    {
        long today=sim.Clock/sim.Config.Clock.TicksPerDay;
        while (LastDay < today)
        {
            LastDay++;
            foreach (var p in _places)
            {
                if (!IsLiving(sim,p) || p.Kind is WildPlaceKind.Ruins or WildPlaceKind.Ore) continue;
                for (int dy=-3;dy<=3;dy++) for(int dx=-3;dx<=3;dx++)
                {
                    if(dx*dx+dy*dy>9)continue;
                    int x=p.X+dx,y=p.Y+dy;if(!sim.World.IsInBounds(x,y))continue;
                    var t=sim.World.TileAt(x,y);
                    if(t.BuildingId>0 || t.Fire!=FireState.None || t.Terrain is TerrainKind.Water or TerrainKind.Mountain or TerrainKind.Road or TerrainKind.Lava)continue;
                    if(p.Kind==WildPlaceKind.Spring)
                        sim.World.SetMoisture(x,y,Math.Clamp(t.Moisture+.055f,0,1));
                    if(p.Kind==WildPlaceKind.OldGrove && t.Terrain==TerrainKind.Forest && t.Vegetation>.3f)
                        sim.World.SetMoisture(x,y,Math.Clamp(t.Moisture+.012f,0,1));
                    if(p.Kind==WildPlaceKind.Berries && t.Terrain==TerrainKind.Grass && t.Moisture>=.3f && t.Fertility>=.3f && t.Vegetation>=.2f)
                    {
                        float fruit=Phase(LastDay) switch { 0=>1.5f,1=>5f,2=>2.5f,_=>0 };
                        if(fruit>0)sim.ResourceSystem.ReplenishLivingNode(x,y,ResourceKind.Food,fruit);
                    }
                }
            }
        }
    }
    public WildPlace? At(int x,int y) => _places.Where(p=>(p.X-x)*(p.X-x)+(p.Y-y)*(p.Y-y)<=25).OrderBy(p=>(p.X-x)*(p.X-x)+(p.Y-y)*(p.Y-y)).FirstOrDefault();
    public string Describe(Simulation sim,WildPlace p)
    {
        string description=p.Kind switch {
            WildPlaceKind.Spring=>"岩缝渗水滋养附近土壤。泉眼被填平后，涵养作用停止。",
            WildPlaceKind.Berries=>"野果随生长周期补充；休眠期停止额外结果。干旱、贫瘠或火灾会影响它。",
            WildPlaceKind.OldGrove=>"老树下的湿润林地储存木材，也会燃烧。伐去林木后，涵养作用消失。",
            WildPlaceKind.Ruins=>"残墙间留有有限的石料和旧木。居民能到达才可利用；取走后不会重新出现。",
            _=>"露头周边分布铁矿和石料。采集与矿场需要可达的居民；矿脉并不直接变成库存。" };
        int pile=sim.GroundStocks.FindAt(p.X,p.Y);
        string stock=pile>=0 ? $"\n遗留物资：石 {sim.GroundStocks.AmountOf(pile,ResourceKind.Stone):0} · 木 {sim.GroundStocks.AmountOf(pile,ResourceKind.Wood):0}" : p.Kind==WildPlaceKind.Ruins ? "\n遗留物资已被取走。" : "";
        return p.Name+"\n"+description+stock+(IsLiving(sim,p)?"":"\n这里已被改造或受损，自然作用暂停。");
    }
    public JsonValue Encode()
    {
        var array=JsonValue.Array();foreach(var p in _places)array.Add(JsonValue.Object().Set("kind",JsonValue.From((int)p.Kind)).Set("x",JsonValue.From(p.X)).Set("y",JsonValue.From(p.Y)).Set("name",JsonValue.From(p.Name)));
        var marks=JsonValue.Array();foreach(var m in _marks)marks.Add(JsonValue.Object().Set("x",JsonValue.From(m.X)).Set("y",JsonValue.From(m.Y)).Set("name",JsonValue.From(m.Name)));
        return JsonValue.Object().Set("lastDay",JsonValue.From(LastDay)).Set("places",array).Set("marks",marks);
    }
    public static WildPlaces Decode(JsonValue root)
    {
        var result=new WildPlaces { LastDay=Math.Max(0,root.GetLong("lastDay")) };
        foreach(var v in root.Get("places").Items.Take(15))
        {
            int kind=v.GetInt("kind",-1);if(kind<0 || kind>4)continue;
            result._places.Add(new WildPlace { Kind=(WildPlaceKind)kind,X=v.GetInt("x"),Y=v.GetInt("y"),Name=v.GetString("name", "荒野") });
        }
        foreach(var v in root.Get("marks").Items.Take(32))result.Remember(v.GetInt("x",-1),v.GetInt("y",-1),v.GetString("name"));
        return result;
    }
}
