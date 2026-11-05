using System;
using Godot;
using SandBoxSim.Core;

namespace SandBoxSim.Client;

public partial class WorldView3D
{
    private readonly Vector4[] _vegetationContacts=new Vector4[8];
    private readonly float[] _vegetationDistances=new float[8];
    private int _vegetationContactCount;
    private float _vegetationContactClock;
    private bool _sampleVegetationContacts;

    private void BeginVegetationContacts(float delta)
    {
        _sampleVegetationContacts=false;
        _vegetationContactClock-=delta;
        if(_vegetationContactClock>0)return;
        _vegetationContactClock=1f/60;_sampleVegetationContacts=true;_vegetationContactCount=0;
        Array.Clear(_vegetationContacts);Array.Fill(_vegetationDistances,float.PositiveInfinity);
    }
    private void OfferVegetationContact(Vector3 point,float radius)
    {
        if(!_sampleVegetationContacts||!point.IsFinite()||radius<=0)return;
        float distance=point.DistanceSquaredTo(_camera.Position);
        if(distance>34*34)return;
        int index=0;
        while(index<_vegetationContactCount&&distance>=_vegetationDistances[index])index++;
        if(index==8)return;
        for(int i=Math.Min(_vegetationContactCount,7);i>index;i--)
        {_vegetationContacts[i]=_vegetationContacts[i-1];_vegetationDistances[i]=_vegetationDistances[i-1];}
        _vegetationContacts[index]=new(point.X,point.Y,point.Z,radius);_vegetationDistances[index]=distance;
        _vegetationContactCount=Math.Min(8,_vegetationContactCount+1);
    }
    private void EndVegetationContacts()
    {
        if(_sampleVegetationContacts)
            _models.BindMeadowContacts(_vegetationContacts,_vegetationContactCount);
    }
    private void ValidateVegetationContacts()
    {
        var saved=(Vector4[])_vegetationContacts.Clone();var distances=(float[])_vegetationDistances.Clone();
        int count=_vegetationContactCount;float clock=_vegetationContactClock;bool sampling=_sampleVegetationContacts;
        string digest=StateHash.ComputeDigest(Game.Sim);
        try
        {
            _sampleVegetationContacts=true;_vegetationContactCount=0;Array.Fill(_vegetationDistances,float.PositiveInfinity);
            for(int i=11;i>=1;i--)OfferVegetationContact(_camera.Position+new Vector3(i,0,0),.55f);
            OfferVegetationContact(new(float.NaN,0,0),.55f);
            if(_vegetationContactCount!=8)throw new InvalidOperationException("Vegetation contact budget lost");
            for(int i=0;i<8;i++)
                if(MathF.Abs(_vegetationDistances[i]-(i+1)*(i+1))>.001f)
                    throw new InvalidOperationException("Vegetation contacts do not retain nearest actors");
            EndVegetationContacts();_models.ValidateMeadowContacts(_vegetationContacts,8);
            _vegetationContactCount=0;Array.Clear(_vegetationContacts);EndVegetationContacts();
            _models.ValidateMeadowContacts(_vegetationContacts,0);
            _sampleVegetationContacts=false;OfferVegetationContact(_camera.Position,.8f);
            if(_vegetationContactCount!=0)throw new InvalidOperationException("Unsampled/paused vegetation state advances");
        }
        finally
        {
            Array.Copy(saved,_vegetationContacts,8);Array.Copy(distances,_vegetationDistances,8);
            _vegetationContactCount=count;_vegetationContactClock=clock;_sampleVegetationContacts=sampling;
            _models.BindMeadowContacts(_vegetationContacts,count);
        }
        if(StateHash.ComputeDigest(Game.Sim)!=digest)throw new InvalidOperationException("Vegetation contacts change simulation");
        GD.Print("VEGETATION_CONTACT_PASS: nearest-eight budget, finite inputs, shader upload, removal, unsampled freeze and unchanged simulation");
    }
}
