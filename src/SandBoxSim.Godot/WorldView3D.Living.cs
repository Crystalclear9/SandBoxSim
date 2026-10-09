using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SandBoxSim.Core.Environment;
namespace SandBoxSim.Client;
public partial class WorldView3D
{
    private readonly Dictionary<(int Slot,ResourceKind Kind),(Node3D Node,int Tier)> _stockVisuals=new();
    private Node3D? _stockRoot;
    private ulong _lifeFrame=ulong.MaxValue;
    private readonly HashSet<(int Slot,ResourceKind Kind)> _liveStocks=new();
    private readonly List<(int Slot,Vector3 Position,float Distance)> _stockOrder=new();
    public void FocusHome(int slot)
    {
        if(!_buildingVisuals.TryGetValue(slot,out var visual))return;
        _follow=-1;_target=visual.Root.Position+Vector3.Up*.7f;
        _yaw=visual.Root.Rotation.Y+.38f;_pitch=.56f;_distance=10.5f;UpdateCamera();
    }
    private void ResetLivingDetails()
    {
        if(_stockRoot!=null){_stockRoot.GetParent()?.RemoveChild(_stockRoot);_stockRoot.QueueFree();}
        _stockRoot=new Node3D {Name="ActualGroundStocks"};_scene.AddChild(_stockRoot);_stockVisuals.Clear();
    }
    private void UpdateHouseLife(BuildingVisual visual,int slot)
    {
        if(!visual.Complete||visual.Kind!=BuildingKind.House)return;
        int residents=Math.Clamp(Game.Sim.Buildings.OccupiedBedsOf(slot),0,4);
        float condition=Game.Sim.Buildings.DecayOf(slot);int wear=condition>.75f?0:condition>.35f?1:2;
        int key=residents*3+wear;
        if(visual.Detailed&&visual.LifeKey!=key)
        {
            ulong frame=Engine.GetProcessFrames();if(_lifeFrame==frame)return;_lifeFrame=frame;
            using var profile=RenderProfile.Measure("HouseLife");
            if(visual.Life!=null){visual.Root.RemoveChild(visual.Life);visual.Life.QueueFree();}
            visual.Life=_models.HouseLife(visual.Identity,residents,wear);visual.Root.AddChild(visual.Life);visual.LifeKey=key;
        }
        if(visual.Life==null)return;
        visual.Life.Visible=visual.Detailed&&key>0;
        visual.Life.GetNode<Node3D>("Drying/Clothes").Visible=Game.Sim.World.Weather.Kind is WeatherKind.Clear or WeatherKind.Cloudy or WeatherKind.Drought;
    }
    private void UpdateFarmLife(BuildingVisual visual,int slot)
    {
        if(!visual.Complete||visual.Kind!=BuildingKind.Farm)return;
        int phase=Game.Sim.Config.Buildings.LivingAgricultureEnabled?SandBoxSim.Core.Systems.LivingAgriculture.Phase(Game.Sim.Clock,Game.Sim.World.Calendar.TicksPerDay):1;
        var p=Game.Sim.Buildings.PositionOf(slot);var crop=SandBoxSim.Core.Systems.LivingAgriculture.CropAt(Game.Sim.World.Seed,p.X,p.Y);
        int key=100+(int)crop*4+phase+(visual.Detailed?0:12);bool near=_camera.Position.DistanceSquaredTo(visual.Root.Position)<55*55;
        if(near&&visual.LifeKey!=key&&_lifeFrame!=Engine.GetProcessFrames())
        {
            _lifeFrame=Engine.GetProcessFrames();if(visual.Life!=null){visual.Root.RemoveChild(visual.Life);visual.Life.QueueFree();}
            visual.Life=_models.CropField(crop,phase,visual.Detailed);visual.Root.AddChild(visual.Life);visual.LifeKey=key;
        }
        if(visual.Life!=null)visual.Life.Visible=near;
    }
    private void SyncGroundStocks()
    {
        if(_stockRoot==null)return;
        using var profile=RenderProfile.Measure("GroundStocks");
        var store=Game.Sim.GroundStocks;var live=_liveStocks;live.Clear();_stockOrder.Clear();int creations=0;
        double creationMs=0;
        foreach(int slot in store.AliveIndices())
        {
            var p=store.PositionOf(slot);var position=PositionAt(p.X,p.Y);
            _stockOrder.Add((slot,position,_camera.Position.DistanceSquaredTo(position)));
        }
        _stockOrder.Sort((a,b)=>a.Distance.CompareTo(b.Distance));
        foreach(var entry in _stockOrder)
        {
            int slot=entry.Slot;var position=entry.Position;
            foreach(var kind in ResidentRig.CargoKinds)
            {
                float amount=store.AmountOf(slot,kind);if(amount<=.01f)continue;
                var key=(slot,kind);live.Add(key);int tier=amount<8?0:amount<40?1:2;
                bool near=entry.Distance<55*55;
                if(near&&(!_stockVisuals.TryGetValue(key,out var existing)||existing.Tier!=tier)&&creations<4&&creationMs<2)
                {
                    long started=System.Diagnostics.Stopwatch.GetTimestamp();
                    creations++;
                    if(existing.Node!=null){_stockRoot.RemoveChild(existing.Node);existing.Node.QueueFree();}
                    var node=_models.GroundPile(kind,tier);_stockRoot.AddChild(node);_stockVisuals[key]=(node,tier);
                    creationMs+=System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                }
                if(_stockVisuals.TryGetValue(key,out var visual))
                {
                    visual.Node.Position=position+new Vector3(((int)kind%2-.5f)*.65f,0,((int)kind/2%2-.5f)*.65f);
                    visual.Node.Visible=near;
                }
            }
        }
        foreach(var key in _stockVisuals.Keys.Where(key=>!live.Contains(key)).ToArray())
        {var node=_stockVisuals[key].Node;_stockRoot.RemoveChild(node);node.QueueFree();_stockVisuals.Remove(key);}
    }
    public void ValidateLivingDetails()
    {
        var sim=Game.Sim;string snapshot=sim.SaveToText();var cameraPosition=_camera.Position;
        var target=_target;float yaw=_yaw,pitch=_pitch,distance=_distance;
        try
        {
        int slot=-1;
        for(int i=0;i<sim.Buildings.LiveCount;i++){int candidate=sim.Buildings.LiveAt(i);if(sim.Buildings.KindOf(candidate)==BuildingKind.House&&sim.Buildings.StateOf(candidate)==BuildingState.Complete){slot=candidate;break;}}
        if(slot<0)
        {
            for(int gy=30;gy<36;gy++)for(int gx=30;gx<36;gx++)sim.World.SetTerrain(gx,gy,TerrainKind.Grass);
            slot=sim.Buildings.Place(sim.World,BuildingKind.House,32,32,10);
            if(slot<0)throw new InvalidOperationException("Home fixture placement failed");sim.Buildings.MarkComplete(slot,sim.Clock);
        }
        sim.Buildings.TryOccupyBedOf(slot);
        int resident=sim.Agents.AliveSlots().FirstOrDefault(-1);if(resident>=0)sim.Agents.SetDwelling(resident,slot);
        BuildBuildings();var visual=_buildingVisuals[slot];var original=sim.Buildings.DecayOf(slot);
        try
        {
            visual.Detailed=true;_lifeFrame=ulong.MaxValue;UpdateHouseLife(visual,slot);var before=visual.Life;
            string digest=sim.StateDigestString();_lifeFrame=ulong.MaxValue;UpdateHouseLife(visual,slot);
            if(before!=visual.Life||digest!=sim.StateDigestString())throw new InvalidOperationException("Home observation regenerated or mutated simulation");
            sim.World.Weather.ForceKind(WeatherKind.Clear,1);UpdateHouseLife(visual,slot);
            var rack=visual.Life!.GetNode<Node3D>("Drying");var clothes=rack.GetNode<Node3D>("Clothes");
            if(!rack.Visible||!clothes.Visible)throw new InvalidOperationException("Sunny home hides clothes");
            sim.World.Weather.ForceKind(WeatherKind.Rain,1);UpdateHouseLife(visual,slot);
            if(!rack.Visible||clothes.Visible||visual.Life!=before)throw new InvalidOperationException("Rain removes rack or rebuilds home");
            sim.Buildings.SetDecay(slot,.2f);_lifeFrame=ulong.MaxValue;UpdateHouseLife(visual,slot);
            if(visual.Life==before||visual.LifeKey%3!=2)throw new InvalidOperationException("Home condition not reflected");
            visual.Detailed=false;UpdateHouseLife(visual,slot);if(visual.Life!.Visible)throw new InvalidOperationException("Distant home still draws daily details");
        }
        finally{sim.Buildings.SetDecay(slot,original);_lifeFrame=ulong.MaxValue;UpdateHouseLife(visual,slot);}
        Game.ValidateHomeNavigation(slot);
        var stocks=sim.GroundStocks;int x=40,y=40;
        foreach(var kind in ResidentRig.CargoKinds)stocks.Withdraw(x,y,kind,float.MaxValue);
        stocks.Deposit(x,y,ResourceKind.Wood,5,sim.Config.GroundStocks);int pile=stocks.FindAt(x,y);
        _camera.Position=PositionAt(x,y)+Vector3.Up*8;
        void Poll(){for(int i=0;i<=stocks.LiveCount*4;i++)SyncGroundStocks();}
        string state=sim.StateDigestString();Poll();if(state!=sim.StateDigestString())throw new InvalidOperationException("Pile observation changed inventory");
        var key=(pile,ResourceKind.Wood);var originalPile=_stockVisuals[key];Poll();
        if(_stockVisuals[key].Node!=originalPile.Node)throw new InvalidOperationException("Unchanged stock rebuilt geometry");
        stocks.Deposit(x,y,ResourceKind.Wood,40,sim.Config.GroundStocks);Poll();
        if(_stockVisuals[key].Tier!=2||_stockVisuals[key].Node==originalPile.Node)throw new InvalidOperationException("Stock quantity band not reflected");
        stocks.Withdraw(x,y,ResourceKind.Wood,float.MaxValue);Poll();
        if(_stockVisuals.ContainsKey(key))throw new InvalidOperationException("Depleted stock left geometry");
        stocks.Deposit(x+1,y,ResourceKind.Food,5,sim.Config.GroundStocks);Poll();
        int reused=stocks.FindAt(x+1,y);
        var replacement=_stockVisuals[(reused,ResourceKind.Food)].Node;
        var expected=PositionAt(x+1,y)+new Vector3(.325f,0,-.325f);
        if(replacement.Position.DistanceTo(expected)>.001f)throw new InvalidOperationException("Reused stock has stale position");
        }
        finally
        {
            var loaded=sim.LoadFromText(snapshot);if(!loaded.Success)throw new InvalidOperationException(loaded.Error);
            _target=target;_yaw=yaw;_pitch=pitch;_distance=distance;_camera.Position=cameraPosition;RebuildWorld();
        }
        GD.Print("LIVING_DETAILS_PASS: home selection/resident links, weather clothes/rack, condition/occupancy, cached details, distance policy, stock quantities/depletion/reuse and observation purity");
    }
}
