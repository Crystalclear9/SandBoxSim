using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;

public partial class WorldView3D
{
    // A contact is (world x, world z, age, strength). No event nodes or physics
    // bodies are allocated, and this presentation never writes simulator state.
    private readonly Vector4[] _waterContacts=new Vector4[8];
    private int _waterContactCount;
    private ShaderMaterial? _interactiveWater;
    private readonly Dictionary<int,Vector3?> _shoreContacts=new();
    private const float WaterContactLifetime=2.6f;
    private readonly List<Transform3D> _bankGravel=new();

    private void AddBankGravel(int x,int y,uint hash)
    {
        // Patchy deposits follow the measured shoreline, not every water tile's
        // square perimeter. Small stones grade into the dry bank behind them.
        var stand=PositionAt(x,y);
        if(hash%3!=0||!TryShoreContact(stand,out var contact))return;
        var inland=new Vector3(stand.X-contact.X,0,stand.Z-contact.Z).Normalized();
        var along=new Vector3(-inland.Z,0,inland.X);
        for(int i=0;i<9;i++)
        {
            uint sample=MeadowSample(hash,i);
            float lateral=(((sample>>8)&255)/255f-.5f)*1.4f;
            float setback=(((sample>>16)&255)/255f)*.65f-.10f;
            var point=contact+along*lateral+inland*setback;
            var world=Game.Sim.World;
            int px=(int)MathF.Floor(point.X*.5f),py=(int)MathF.Floor(point.Z*.5f);
            if(!world.IsInBounds(px,py))continue;
            var tile=world.TileAt(px,py);float height=GroundHeight(point.X,point.Z);
            if(tile.BuildingId!=0||tile.Fire==FireState.Burnt||tile.Terrain is TerrainKind.Road or TerrainKind.Lava
                ||height<-.04f||height>.27f||GroundNormal(point).Y<.85f)continue;
            float scale=.16f+((sample>>24)&255)/255f*.22f;
            scale*=.75f+Math.Clamp(setback,0,.65f)*.6f;
            _bankGravel.Add(GroundedStone("pebble",point,new(scale,scale*.58f,scale*1.35f),new(.06f,sample%31,.08f),.035f));
        }
    }

    private bool TryShoreContact(Vector3 stand,out Vector3 contact)
    {
        contact=default;var world=Game.Sim.World;
        int x=(int)MathF.Floor(stand.X*.5f),y=(int)MathF.Floor(stand.Z*.5f);
        if(!world.IsInBounds(x,y)||stand.Y>.65f||!DryActorTarget(stand)||!ActorFootSupport(stand))return false;
        int key=y*world.Width+x;
        if(!_shoreContacts.TryGetValue(key,out var cached))
        {
            Vector3 center=PositionAt(x,y);float best=1.65f*1.65f;cached=null;
            for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
            {
                int wx=x+dx,wy=y+dy;
                if(!world.IsInBounds(wx,wy)||world.TerrainAt(wx,wy)!=TerrainKind.Water)continue;
                var wet=PositionAt(wx,wy);
                if(GroundHeight(wet.X,wet.Z)>=.015f)continue;
                var dry=center;
                // Resolve the geometric bank, not the logical tile boundary.
                for(int i=0;i<13;i++)
                {
                    var mid=(dry+wet)*.5f;
                    if(GroundHeight(mid.X,mid.Z)>.012f)dry=mid;else wet=mid;
                }
                float distance=new Vector2(wet.X-center.X,wet.Z-center.Z).LengthSquared();
                if(distance<best){best=distance;cached=new(wet.X,.02f,wet.Z);}
            }
            _shoreContacts[key]=cached;
        }
        if(cached is not { } point||GroundHeight(point.X,point.Z)>.015f
            ||new Vector2(point.X-stand.X,point.Z-stand.Z).LengthSquared()>1.8f*1.8f)return false;
        contact=point;return true;
    }
    private static bool SettledAt(Vector3 current,Vector3 target)
        =>new Vector2(target.X-current.X,target.Z-current.Z).LengthSquared()<.035f*.035f;

    private void BeginWaterContacts(float delta)
        =>AdvanceWaterContacts(delta,Game.VisualPaused);
    private void AdvanceWaterContacts(float delta,bool paused)
    {
        if(paused)return;
        int live=0;
        for(int i=0;i<_waterContactCount;i++)
        {
            var entry=_waterContacts[i];entry.Z+=MathF.Max(0,delta);
            if(entry.Z<WaterContactLifetime)_waterContacts[live++]=entry;
        }
        _waterContactCount=live;Array.Clear(_waterContacts,live,8-live);
    }
    private void OfferWaterContact(Vector3 point,float strength)
    {
        if(Game.VisualPaused||!point.IsFinite()||strength<=0||point.DistanceSquaredTo(_camera.Position)>34*34
            ||GroundHeight(point.X,point.Z)>.015f)return;
        for(int i=0;i<_waterContactCount;i++)
        {
            var e=_waterContacts[i];float dx=e.X-point.X,dz=e.Y-point.Z;
            if(dx*dx+dz*dz<.25f*.25f&&e.Z<.85f)return;
        }
        if(_waterContactCount==8)
        {
            int oldest=0;
            for(int i=1;i<8;i++)if(_waterContacts[i].Z>_waterContacts[oldest].Z)oldest=i;
            _waterContacts[oldest]=new(point.X,point.Z,0,Math.Clamp(strength,0,1));
        }
        else _waterContacts[_waterContactCount++]=new(point.X,point.Z,0,Math.Clamp(strength,0,1));
    }
    private void EndWaterContacts()
    {
        if(_interactiveWater==null)return;
        _interactiveWater.SetShaderParameter("contact_rings",_waterContacts);
        _interactiveWater.SetShaderParameter("contact_ring_count",_waterContactCount);
    }
    private void ValidateShoreInteractions()
    {
        string digest=StateHash.ComputeDigest(Game.Sim);int banks=0;
        var world=Game.Sim.World;
        for(int y=0;y<world.Height;y++)for(int x=0;x<world.Width;x++)
        {
            var stand=PositionAt(x,y);
            if(!TryShoreContact(stand,out var contact))continue;
            if(!ActorFootSupport(stand)||GroundHeight(contact.X,contact.Z)>.015f
                ||contact.Y!=.02f||stand.DistanceTo(contact)>1.81f)
                throw new InvalidOperationException("Drinking contact does not meet the actual supported bank");
            banks++;
        }
        if(banks==0)throw new InvalidOperationException("No actual shoreline contacts checked");
        var saved=(Vector4[])_waterContacts.Clone();int count=_waterContactCount;var camera=_camera.Position;
        try
        {
            _waterContactCount=1;_waterContacts[0]=new(1,2,WaterContactLifetime-.01f,.5f);
            AdvanceWaterContacts(.02f,true);
            if(_waterContactCount!=1||_waterContacts[0].Z!=WaterContactLifetime-.01f)
                throw new InvalidOperationException("Paused water contacts advance");
            AdvanceWaterContacts(.02f,false);
            if(_waterContactCount!=0)throw new InvalidOperationException("Departed drinking rings never expire");
            if(!Game.VisualPaused)
                foreach(var candidate in _shoreContacts.Values)
                {
                    if(candidate is not { } point)continue;
                    _camera.Position=point+Vector3.Up*2;
                    OfferWaterContact(point,.5f);OfferWaterContact(point,.5f);
                    OfferWaterContact(new(float.NaN,0,0),.5f);OfferWaterContact(point,-1);
                    if(_waterContactCount!=1)throw new InvalidOperationException("Water contact cooldown or input filtering fails");
                    for(int i=0;i<12;i++){AdvanceWaterContacts(.86f,false);OfferWaterContact(point,.5f);}
                    if(_waterContactCount>8||_waterContactCount==0)throw new InvalidOperationException("Water contact budget fails");
                    break;
                }
            EndWaterContacts();
            if(_interactiveWater is { } material)
            {
                var uploaded=material.GetShaderParameter("contact_rings").AsVector4Array();
                if(uploaded.Length!=8||material.GetShaderParameter("contact_ring_count").AsInt32()!=_waterContactCount)
                    throw new InvalidOperationException("Water contact shader budget is not bound");
                for(int i=0;i<8;i++)if(uploaded[i]!=_waterContacts[i])throw new InvalidOperationException("Water contact shader coordinates differ");
            }
            if(!SettledAt(new(1,3,2),new(1,0,2))||SettledAt(Vector3.Zero,Vector3.Right*.04f))
                throw new InvalidOperationException("Drinking arrival uses height instead of horizontal distance");
        }
        finally{Array.Copy(saved,_waterContacts,8);_waterContactCount=count;_camera.Position=camera;EndWaterContacts();}
        if(StateHash.ComputeDigest(Game.Sim)!=digest)throw new InvalidOperationException("Shore contact changes simulation");
        GD.Print($"SHORE_INTERACTION_PASS: {banks} actual supported banks, {_bankGravel.Count} grounded gravel instances, reach, water-side anchors, ring expiry/pause/upload and horizontal arrival");
    }
}
