using System;
using Godot;

namespace SandBoxSim.Client;

// The observed-tile motor is also the fallback when route projection is unsafe.
internal struct ResidentTravel
{
    internal const double TilesPerSecond=.52;
    internal const string Version="projected-resident-route-v4";
    private const float PreparationTime=.16f;
    private const float Braking=4.5f;
    private bool _ready,_queued;
    private Vector2 _projectedVelocity;
    private Vector2I _latestTile;
    private Vector3 _goal,_next;
    private float _cruise,_speed,_preparation,_restTime;

    internal Vector3 Goal=>_goal;
    internal bool Queued=>_queued;
    internal bool Projecting{get;private set;}
    internal bool Redirecting{get;private set;}
    internal bool CatchingUpFromRedirect{get;private set;}
    internal float Preparation{get;private set;}
    internal static Vector2 Planar(Vector3 p)=>new(p.X,p.Z);
    internal float Remaining(Vector3 current)=>Planar(_goal-current).Length()+(_queued?Planar(_next-_goal).Length():0);

    internal bool PlanRedirect(Vector2 heading,float yaw,bool turning)
    {
        var forward=new Vector2(-MathF.Sin(yaw),-MathF.Cos(yaw));
        float alignment=forward.Dot(heading);
        Redirecting=Redirecting?turning||alignment<.93f:alignment<-.38f;
        CatchingUpFromRedirect|=Redirecting;
        return Redirecting;
    }

    internal Vector3 BrakeForRedirect(Vector3 current,float dt,bool hasRouteProjection)
    {
        Projecting=hasRouteProjection;
        _ready=_queued=false;
        Preparation=0;
        _projectedVelocity=_projectedVelocity.MoveToward(Vector2.Zero,Braking*dt);
        _speed=_projectedVelocity.Length();
        var step=_projectedVelocity*dt;
        return current+new Vector3(step.X,0,step.Y);
    }

    internal void StopProjectedMomentum()
    {
        _projectedVelocity=Vector2.Zero;
        _speed=0;
    }

    internal Vector3 ProjectedStep(Vector3 current,Vector3 target,float yaw,float limit,float dt)
    {
        Projecting=true;
        _ready=_queued=false;
        Preparation=0;
        var direction=Planar(target-current);
        float distance=direction.Length();
        if(distance<1.85f)CatchingUpFromRedirect=false;
        _restTime=distance<.025f?_restTime+dt:0;
        if(_restTime>=.20f)_preparation=0;
        if(distance>.012f&&_speed<.05f&&_preparation<PreparationTime)
        {
            _projectedVelocity=Vector2.Zero;
            _preparation=MathF.Min(PreparationTime,_preparation+dt);
            Preparation=_preparation/PreparationTime;
            return current;
        }
        var forward=new Vector2(-MathF.Sin(yaw),-MathF.Cos(yaw));
        var heading=direction.Normalized();
        float alignment=forward.Dot(heading);
        float steer=.13f+.87f*Mathf.SmoothStep(-.10f,.67f,alignment);
        float wanted=MathF.Min(distance*(1-MathF.Exp(-8*dt))/dt,limit*steer);
        float speed=Mathf.MoveToward(_speed,wanted,dt*(wanted>_speed?3.7f:Braking));
        float step=MathF.Min(distance,speed*dt);
        _speed=step/dt;
        _projectedVelocity=heading*_speed;
        return distance<.0001f?current:current.Lerp(target,step/distance);
    }

    internal void Observe(Vector3 current,Vector2I tile,Vector3 destination)
    {
        Projecting=false;
        Redirecting=CatchingUpFromRedirect=false;
        _projectedVelocity=Vector2.Zero;
        if(!_ready)
        {
            _ready=true;_latestTile=tile;
            SetGoal(current,destination);
            return;
        }
        AdvanceWaypoint(current);
        if(tile==_latestTile)
        {
            // The last tile may acquire a more precise local work position.
            if(_queued)_next=destination;
            else if(Planar(destination-_goal).LengthSquared()>.012f*.012f)SetGoal(current,destination);
            return;
        }
        var jump=tile-_latestTile;
        _latestTile=tile;
        if(Math.Abs(jump.X)>1||Math.Abs(jump.Y)>1||_queued)
        {
            _queued=false;_speed=0;
            SetGoal(current,destination);
        }
        else if(Planar(_goal-current).LengthSquared()>.015f*.015f)
        {
            _next=destination;_queued=true;
        }
        else SetGoal(current,destination);
    }

    private void SetGoal(Vector3 current,Vector3 goal)
    {
        _goal=goal;
        _cruise=Math.Clamp(.23f+(float)TilesPerSecond*Planar(goal-current).Length(),.57f,1.61f);
    }

    internal void AdvanceWaypoint(Vector3 current)
    {
        if(!_queued||Planar(_goal-current).LengthSquared()>.015f*.015f)return;
        _queued=false;
        SetGoal(current,_next);
    }

    internal float AdvanceSpeed(Vector3 current,float yaw,float dt)
    {
        var direction=Planar(_goal-current);
        float remaining=direction.Length();
        Preparation=0;
        _restTime=!_queued&&remaining<.025f?_restTime+dt:0;
        if(_restTime>=.20f)_preparation=0;
        if(remaining<.004f)return _speed=Mathf.MoveToward(_speed,0,dt*Braking);
        if(remaining>.12f&&_speed<.05f&&_preparation<PreparationTime)
        {
            _preparation=MathF.Min(PreparationTime,_preparation+dt);
            Preparation=_preparation/PreparationTime;
            return _speed=0;
        }
        if(remaining>.12f)_preparation=PreparationTime;
        var forward=new Vector2(-MathF.Sin(yaw),-MathF.Cos(yaw));
        float alignment=forward.Dot(direction/remaining);
        float throughSpeed=0;
        if(_queued)
        {
            var next=Planar(_next-_goal).Normalized();
            float corner=direction.Dot(next)/remaining;
            throughSpeed=Mathf.Lerp(.24f,_cruise,Mathf.SmoothStep(.1f,.97f,corner));
        }
        float ceiling=MathF.Sqrt(throughSpeed*throughSpeed+2*Braking*remaining);
        float steer=.16f+.84f*Mathf.SmoothStep(-.23f,.85f,alignment);
        float wanted=MathF.Min(ceiling,_queued?MathF.Max(_cruise,1.46f):_cruise)*steer;
        _speed=Mathf.MoveToward(_speed,wanted,dt*(wanted>_speed?3.7f:Braking));
        return _speed;
    }

    internal Vector2 Heading(Vector3 current)
    {
        var direction=Planar(_goal-current);
        float distance=direction.Length();
        if(!_queued||distance>.40f)return direction;
        var future=Planar(_next-_goal).Normalized();
        return (direction.Normalized().Lerp(future,.35f*(1-Mathf.SmoothStep(.04f,.40f,distance))))*distance;
    }
}
