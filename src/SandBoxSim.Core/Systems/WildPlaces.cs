using System;
using System.Collections.Generic;
using System.Linq;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Systems;

public enum WildPlaceKind { Spring, Berries, OldGrove, Ruins, Ore, Meadow, Wetland, FallenWood }

public sealed class WildPlace
{
    public WildPlaceKind Kind;
    public int X, Y;
    public string Name = "";
    public float Growth = 1;
    public long EstablishedDay = -8; // Existing natural sites start mature.
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
                var kind = (WildPlaceKind)(index % 8);
                string[] names = { "苔石泉", "野果洼地", "古木林", "旧聚落遗迹", "赤土岩脉", "风花草甸", "苇泽", "倒木林隙" };
                var p = new WildPlace { Kind = kind, X = x, Y = y, Name = names[(int)kind] };
                result._places.Add(p); Initialize(sim, p); break;
            }
        }
        sim.World.RefreshSpatialIndex();
        return result;
    }
    public bool Cultivate(Simulation sim,WildPlaceKind kind,int x,int y)
    {
        if(kind is not (WildPlaceKind.Meadow or WildPlaceKind.Berries or WildPlaceKind.Wetland or WildPlaceKind.FallenWood))return false;
        if(!sim.World.IsInBounds(x,y))return false;
        var center=sim.World.TileAt(x,y);if(!center.Walkable||center.BuildingId>0||center.Terrain==TerrainKind.Road||center.Fire!=FireState.None)return false;
        var existing=_places.FirstOrDefault(p=>(p.X-x)*(p.X-x)+(p.Y-y)*(p.Y-y)<=4);
        if(existing!=null&&existing.Kind==kind&&IsLiving(sim,existing))return false;
        if(existing!=null)_places.Remove(existing);
        var place=new WildPlace {Kind=kind,X=x,Y=y,Growth=kind==WildPlaceKind.FallenWood?1:0,EstablishedDay=sim.Clock/sim.Config.Clock.TicksPerDay,
            Name=kind switch {WildPlaceKind.Meadow=>"新生花甸",WildPlaceKind.Berries=>"新栽野果地",WildPlaceKind.Wetland=>"恢复苇泽",_=>"腐木育土"}};
        _places.Add(place);
        for(int dy=-3;dy<=3;dy++)for(int dx=-3;dx<=3;dx++)
        {
            int px=x+dx,py=y+dy;if(dx*dx+dy*dy>9||!sim.World.IsInBounds(px,py))continue;
            var tile=sim.World.TileAt(px,py);if(!tile.Walkable||tile.BuildingId>0||tile.Terrain==TerrainKind.Road||tile.Fire!=FireState.None)continue;
            sim.World.SetTerrain(px,py,kind==WildPlaceKind.Wetland?TerrainKind.Swamp:TerrainKind.Grass);
            if(tile.Resource.Kind==ResourceKind.Wood)sim.World.ClearResource(px,py);
            sim.World.SetVegetation(px,py,Math.Max(tile.Vegetation,.18f));
            if(kind==WildPlaceKind.Wetland)sim.World.SetMoisture(px,py,Math.Max(tile.Moisture,.72f));
        }
        if(kind==WildPlaceKind.FallenWood)sim.GroundStocks.Deposit(x,y,ResourceKind.Wood,24,sim.Config.GroundStocks);
        sim.World.RefreshSpatialIndex();sim.Events.Record(sim.Clock,SandBoxSim.Core.History.WorldEventType.TerrainChanged,"栽植生态："+place.Name,SandBoxSim.Core.History.EventImportance.Normal,new Int2(x,y),cause:"自由栽植");return true;
    }
    public static float Maturity(Simulation sim,WildPlace place)=>Math.Clamp(place.Growth,0,1);
    private static void Initialize(Simulation sim, WildPlace p)
    {
        for (int dy = -3; dy <= 3; dy++) for (int dx = -3; dx <= 3; dx++)
        {
            if (dx*dx+dy*dy > 9) continue;
            int x=p.X+dx, y=p.Y+dy; if(!sim.World.IsInBounds(x,y))continue;var t=sim.World.TileAt(x,y);
            if (t.BuildingId > 0 || t.Terrain == TerrainKind.Water) continue;
            TerrainKind kind=p.Kind == WildPlaceKind.OldGrove ? TerrainKind.Forest : p.Kind == WildPlaceKind.Ore ? TerrainKind.Mountain : p.Kind == WildPlaceKind.Wetland ? TerrainKind.Swamp : TerrainKind.Grass;
            sim.World.SetTerrain(x,y,kind); sim.World.ApplyDefaultResource(x,y);
            sim.World.Tiles[y*sim.World.Width+x].Height = p.Kind == WildPlaceKind.Ore ? .62f+(dx*dx+dy*dy)*.012f : .43f;
            sim.World.SetMoisture(x,y,p.Kind == WildPlaceKind.Ore ? .22f : .68f);
            sim.World.SetFertility(x,y,p.Kind is WildPlaceKind.Berries or WildPlaceKind.Meadow ? .82f : .6f);
            sim.World.SetVegetation(x,y,p.Kind == WildPlaceKind.OldGrove ? 1 : .58f);
            if (p.Kind == WildPlaceKind.Ore) sim.World.SetResource(x,y,new ResourceNode { Kind=(dx+dy)%3 == 0 ? ResourceKind.Iron : ResourceKind.Stone, Amount=58, Capacity=58, RegenerationRate=0 });
        }
        if (p.Kind == WildPlaceKind.Spring)
        {
            sim.World.SetTerrain(p.X,p.Y,TerrainKind.Water); sim.World.ClearResource(p.X,p.Y);
            sim.World.Tiles[p.Y*sim.World.Width+p.X].Height=.18f;
        }
        if (p.Kind == WildPlaceKind.FallenWood)
            sim.GroundStocks.Deposit(p.X,p.Y,ResourceKind.Wood,48,sim.Config.GroundStocks);
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
            WildPlaceKind.Ore => t.Terrain == TerrainKind.Mountain, WildPlaceKind.Wetland => t.Terrain == TerrainKind.Swamp, _ => t.Terrain == TerrainKind.Grass };
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
                if(p.EstablishedDay>=0&&p.Kind!=WildPlaceKind.FallenWood)
                {
                    var root=sim.World.TileAt(p.X,p.Y);
                    float growing=Phase(LastDay)==3?0:Math.Clamp(root.Moisture*root.Fertility*2*(1-root.FootTraffic),0,1)/8;
                    p.Growth=Math.Clamp(p.Growth+(root.Moisture<.12f?-.02f:growing),0,1);
                }
                float maturity=p.Growth;
                int reach=1+(int)(maturity*2);
                bool pollinated=p.Kind==WildPlaceKind.Berries&&_places.Any(other=>other.Kind==WildPlaceKind.Meadow&&other.Growth>=.5f&&IsLiving(sim,other)&&sim.World.TileAt(other.X,other.Y).Moisture>=.25f&&(other.X-p.X)*(other.X-p.X)+(other.Y-p.Y)*(other.Y-p.Y)<=100);
                for (int dy=-reach;dy<=reach;dy++) for(int dx=-reach;dx<=reach;dx++)
                {
                    if(dx*dx+dy*dy>reach*reach)continue;
                    int x=p.X+dx,y=p.Y+dy;if(!sim.World.IsInBounds(x,y))continue;
                    var t=sim.World.TileAt(x,y);
                    if(t.BuildingId>0 || t.Fire!=FireState.None || t.Terrain is TerrainKind.Water or TerrainKind.Mountain or TerrainKind.Road or TerrainKind.Lava)continue;
                    if(p.Kind is WildPlaceKind.Spring or WildPlaceKind.Wetland)
                        sim.World.SetMoisture(x,y,Math.Clamp(t.Moisture+(p.Kind==WildPlaceKind.Spring ? .055f : .025f),0,1));
                    if(p.Kind==WildPlaceKind.OldGrove && t.Terrain==TerrainKind.Forest && t.Vegetation>.3f)
                        sim.World.SetMoisture(x,y,Math.Clamp(t.Moisture+.012f,0,1));
                    if(p.Kind is WildPlaceKind.Berries or WildPlaceKind.Meadow && t.Terrain==TerrainKind.Grass && t.Moisture>=.3f && t.Fertility>=.3f && t.Vegetation>=.2f)
                    {
                        float fruit=Phase(LastDay) switch { 0=>1.5f,1=>5f,2=>2.5f,_=>0 };
                        if(p.Kind==WildPlaceKind.Meadow)fruit*=.45f;
                        fruit*=maturity*(pollinated?1.25f:1);
                        float regrowth=.025f*t.Moisture*t.Fertility*(1-t.FootTraffic);
                        sim.World.SetVegetation(x,y,Math.Clamp(t.Vegetation+regrowth,0,1));
                        if(fruit>0)sim.ResourceSystem.ReplenishLivingNode(x,y,ResourceKind.Food,fruit);
                    }
                    if(p.Kind==WildPlaceKind.FallenWood)
                    {
                        int log=sim.GroundStocks.FindAt(p.X,p.Y);
                        if(log>=0 && sim.GroundStocks.AmountOf(log,ResourceKind.Wood)>0)
                            sim.World.SetFertility(x,y,Math.Clamp(t.Fertility+.004f,0,1));
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
            WildPlaceKind.Berries=>"野果随生长周期补充；附近活跃花甸使结果增加四分之一。休眠期停果，干旱、贫瘠、踩踏或火灾会影响它。",
            WildPlaceKind.OldGrove=>"老树下的湿润林地储存木材，也会燃烧。伐去林木后，涵养作用消失。",
            WildPlaceKind.Ruins=>"残墙间留有有限的石料和旧木。居民能到达才可利用；取走后不会重新出现。",
            WildPlaceKind.Meadow=>"草甸在温润时开花与结籽，提供随生长周期变化的地表食物。动物、采集与天气共同改变这里。",
            WildPlaceKind.Wetland=>"低洼苇泽保持周边土壤湿润。填平或烧毁后，涵养作用停止；湿土仍不同于可饮用的水域。",
            WildPlaceKind.FallenWood=>"倒木留下有限木料；尚未被取走的朽木缓慢滋养土壤。这里没有刷新奖励。",
            _=>"露头周边分布铁矿和石料。采集与矿场需要可达的居民；矿脉并不直接变成库存。" };
        int pile=sim.GroundStocks.FindAt(p.X,p.Y);
        string stock=pile>=0 ? $"\n遗留物资：石 {sim.GroundStocks.AmountOf(pile,ResourceKind.Stone):0} · 木 {sim.GroundStocks.AmountOf(pile,ResourceKind.Wood):0}" : p.Kind==WildPlaceKind.Ruins ? "\n遗留物资已被取走。" : "";
        return p.Name+"\n"+description+(p.EstablishedDay>=0?$"\n植被成熟度 {Maturity(sim,p):0%} · 随水土与踩踏变化":"")+stock+(IsLiving(sim,p)?"":"\n这里已被改造或受损，自然作用暂停。");
    }
    public JsonValue Encode()
    {
        var array=JsonValue.Array();foreach(var p in _places)array.Add(JsonValue.Object().Set("kind",JsonValue.From((int)p.Kind)).Set("x",JsonValue.From(p.X)).Set("y",JsonValue.From(p.Y)).Set("name",JsonValue.From(p.Name)).Set("establishedDay",JsonValue.From(p.EstablishedDay)).Set("growth",JsonValue.From(p.Growth)));
        var marks=JsonValue.Array();foreach(var m in _marks)marks.Add(JsonValue.Object().Set("x",JsonValue.From(m.X)).Set("y",JsonValue.From(m.Y)).Set("name",JsonValue.From(m.Name)));
        return JsonValue.Object().Set("lastDay",JsonValue.From(LastDay)).Set("places",array).Set("marks",marks);
    }
    public static WildPlaces Decode(JsonValue root)
    {
        var result=new WildPlaces { LastDay=Math.Max(0,root.GetLong("lastDay")) };
        foreach(var v in root.Get("places").Items.Take(65536))
        {
            int kind=v.GetInt("kind",-1);if(kind<0 || kind>7)continue;
            result._places.Add(new WildPlace { Kind=(WildPlaceKind)kind,X=v.GetInt("x"),Y=v.GetInt("y"),Name=v.GetString("name", "荒野"),EstablishedDay=Math.Max(-8,v.GetLong("establishedDay",-8)),Growth=Math.Clamp(v.GetFloat("growth",1),0,1) });
        }
        foreach(var v in root.Get("marks").Items.Take(32))result.Remember(v.GetInt("x",-1),v.GetInt("y",-1),v.GetString("name"));
        return result;
    }
}
