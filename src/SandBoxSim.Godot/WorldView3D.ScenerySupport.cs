using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;

public partial class WorldView3D
{
    private readonly List<(string Shape,Transform3D Placement,float Embed)> _groundedStones=new();
    private readonly Dictionary<int,(ResourceKind Kind,Vector3 Point)> _sceneResourceAnchors=new();
    private Transform3D GroundedStone(string shape,Vector3 point,Vector3 scale,Vector3 rotation,float embed)
    {
        var basis=Basis.FromEuler(rotation)*Basis.FromScale(scale);
        float y=float.PositiveInfinity;
        foreach(var sample in _models.StoneBase(shape))
        {
            var offset=basis*sample;
            y=MathF.Min(y,GroundHeight(point.X+offset.X,point.Z+offset.Z)-offset.Y);
        }
        var placement=new Transform3D(basis,new(point.X,y-embed,point.Z));
        _groundedStones.Add((shape,placement,embed));return placement;
    }
    private void RegisterSceneResource(int x,int y,ResourceKind kind,Vector3 point)
    {
        if(kind is not (ResourceKind.Wood or ResourceKind.Stone or ResourceKind.Iron or ResourceKind.Food))return;
        var world=Game.Sim.World;
        if(world.TileAt(x,y).Resource.Kind==kind)_sceneResourceAnchors[y*world.Width+x]=(kind,point);
    }
    private Vector3 SceneResourceFocus(int x,int y,ActionKind action)
    {
        var world=Game.Sim.World;int index=y*world.Width+x;
        var expected=action switch {ActionKind.GatherWood=>ResourceKind.Wood,ActionKind.GatherStone=>ResourceKind.Stone,
            ActionKind.GatherIron=>ResourceKind.Iron,ActionKind.GatherFood=>ResourceKind.Food,_=>ResourceKind.None};
        if(_sceneResourceAnchors.TryGetValue(index,out var anchor)&&expected==anchor.Kind
            &&world.Tiles[index].Resource.Kind==expected&&world.Tiles[index].Resource.Amount>0)return anchor.Point;
        return PositionAt(x,y);
    }
    private void ValidateScenerySupport()
    {
        string digest=StateHash.ComputeDigest(Game.Sim);int contacts=0;
        foreach(var item in _groundedStones)
        {
            float closest=float.NegativeInfinity;
            foreach(var sample in _models.StoneBase(item.Shape))
            {
                var p=item.Placement*sample;float gap=p.Y-GroundHeight(p.X,p.Z);
                if(!p.IsFinite()||gap>-item.Embed+.0001f)throw new InvalidOperationException("Placed stone base floats above terrain");
                closest=MathF.Max(closest,gap);contacts++;
            }
            if(MathF.Abs(closest+item.Embed)>.0002f)throw new InvalidOperationException("Stone placement loses its measured surface contact");
        }
        int resources=0;
        foreach(var item in _sceneResourceAnchors)
        {
            int x=item.Key%Game.Sim.World.Width,y=item.Key/Game.Sim.World.Width;
            var action=item.Value.Kind switch {ResourceKind.Wood=>ActionKind.GatherWood,ResourceKind.Food=>ActionKind.GatherFood,
                ResourceKind.Iron=>ActionKind.GatherIron,_=>ActionKind.GatherStone};
            if(Game.Sim.World.Tiles[item.Key].Resource.Amount<=0)continue;
            if(SceneResourceFocus(x,y,action)!=item.Value.Point||SceneResourceFocus(x,y,ActionKind.Drink)!=PositionAt(x,y))
                throw new InvalidOperationException("Scene focus loses its resource type or redirects unrelated actions");
            var original=Game.Sim.World.Tiles[item.Key];
            try
            {
                Game.Sim.World.Tiles[item.Key].Resource.Amount=0;
                if(SceneResourceFocus(x,y,action)!=PositionAt(x,y))throw new InvalidOperationException("Depleted scenery retains an active resource focus");
            }
            finally{Game.Sim.World.Tiles[item.Key]=original;}
            resources++;
        }
        if(contacts==0||resources==0||StateHash.ComputeDigest(Game.Sim)!=digest)
            throw new InvalidOperationException("Scenery checks have no actual contacts or change simulation");
        var vertical=_models.Resident(17,false,JobType.None);var neutral=_models.Resident(17,false,JobType.None);
        try
        {
            vertical.Attend(Vector3.Up);neutral.Attend(Vector3.Zero);
            for(int i=0;i<60;i++)
            {
                vertical.PoseAction(1f/60,ActionKind.None,ActionPhase.Idle,false,0);
                neutral.PoseAction(1f/60,ActionKind.None,ActionPhase.Idle,false,0);
            }
            if(vertical.Head.Transform!=neutral.Head.Transform)throw new InvalidOperationException("Pure vertical scenery focus invents a horizontal head turn");
        }
        finally{vertical.Free();neutral.Free();}
        GD.Print($"SCENERY_SUPPORT_PASS: {contacts} actual base probes, terrain embed, {resources} typed resource anchors, depletion/fallback and unchanged simulation");
    }
}
