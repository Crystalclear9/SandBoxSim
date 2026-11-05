using System;
using Godot;

namespace SandBoxSim.Client;

// Presentation-only, with at most one waypoint beyond the current leg.
// Its destinations come from observed simulation tiles.
internal struct ResidentTravel
{
    internal const double TilesPerSecond=.52;
    internal const string Version="paced-resident-route-v1";
    private bool _ready,_queued;
    private Vector2I _latestTile;
    private Vector3 _goal,_next;
    private float _cruise,_speed;

    internal Vector3 Goal=>_goal;
    internal bool Queued=>_queued;
    internal float Speed=>_speed;
    internal static Vector2 Planar(Vector3 p)=>new(p.X,p.Z);
    internal float Remaining(Vector3 current)=>Planar(_goal-current).Length()+(_queued?Planar(_next-_goal).Length():0);

    internal void Observe(Vector3 current,Vector2I tile,Vector3 destination)
    {
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
        const float braking=4.5f;
        if(remaining<.004f)return _speed=Mathf.MoveToward(_speed,0,dt*braking);
        var forward=new Vector2(-MathF.Sin(yaw),-MathF.Cos(yaw));
        float alignment=forward.Dot(direction/remaining);
        float throughSpeed=0;
        if(_queued)
        {
            var next=Planar(_next-_goal).Normalized();
            float corner=direction.Dot(next)/remaining;
            throughSpeed=Mathf.Lerp(.24f,_cruise,Mathf.SmoothStep(.1f,.97f,corner));
        }
        float ceiling=MathF.Sqrt(throughSpeed*throughSpeed+2*braking*remaining);
        float steer=.16f+.84f*Mathf.SmoothStep(-.23f,.85f,alignment);
        float wanted=MathF.Min(ceiling,_queued?MathF.Max(_cruise,1.46f):_cruise)*steer;
        _speed=Mathf.MoveToward(_speed,wanted,dt*(wanted>_speed?3.7f:braking));
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
