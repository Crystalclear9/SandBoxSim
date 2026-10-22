using System;
using System.Collections.Generic;
using Godot;

namespace SandBoxSim.Client;

public partial class WorldView3D
{
    private readonly PoseSchedule _poseSchedule=new();
    /// <summary>Elapsed render time belongs to the actual actor identity, not its slot or current stride.</summary>
    private sealed class PoseSchedule
    {
        private readonly Dictionary<long,float> _elapsed=new();
        public int Count=>_elapsed.Count;
        public void Accumulate(long identity,float delta,bool active)
        {
            if(!active){Forget(identity);return;}
            if(!float.IsFinite(delta)||delta<=0)return;
            _elapsed.TryGetValue(identity,out float pending);
            _elapsed[identity]=MathF.Min(1,pending+delta);
        }
        public float Consume(long identity)
        {
            if(!_elapsed.Remove(identity,out float elapsed))return 0;
            return elapsed;
        }
        public void Forget(long identity)=>_elapsed.Remove(identity);
        public void Clear()=>_elapsed.Clear();
    }
    private static void ValidatePoseScheduling()
    {
        var schedule=new PoseSchedule();const long actor=(3L<<32)|7;
        float actual=0,consumed=0;
        float[] jitter={.01f,.01f,.01f,.01f,.01f,.01f,.01f,.08f};
        foreach(float delta in jitter){schedule.Accumulate(actor,delta,true);actual+=delta;}
        consumed+=schedule.Consume(actor);
        if(MathF.Abs(consumed-.15f)>.00001f||MathF.Abs(actual-consumed)>.00001f)
            throw new InvalidOperationException("Distant pose time follows the last frame instead of actual elapsed intervals");
        // Change distance bands before a pending update; neither invent nor lose time.
        for(int frame=0;frame<32;frame++)
        {
            float delta=frame%7==0?.07f:.009f;actual+=delta;schedule.Accumulate(actor,delta,true);
            int stride=frame<11?8:frame<19?1:6;
            if((frame+7)%stride==0)consumed+=schedule.Consume(actor);
        }
        consumed+=schedule.Consume(actor);
        if(MathF.Abs(actual-consumed)>.00001f)throw new InvalidOperationException("Stride change loses or duplicates pending animation time");
        schedule.Accumulate(actor,.08f,true);schedule.Accumulate(actor,.1f,false);
        if(schedule.Consume(actor)!=0)throw new InvalidOperationException("Paused or hidden resident retains pose catch-up debt");
        schedule.Accumulate(actor,.04f,true);schedule.Forget(actor);
        const long successor=(4L<<32)|7;
        if(schedule.Consume(successor)!=0||schedule.Count!=0)throw new InvalidOperationException("Reused slot inherits another resident's animation debt");
        schedule.Accumulate(successor,.02f,true);schedule.Clear();
        if(schedule.Consume(successor)!=0||schedule.Count!=0)throw new InvalidOperationException("World replacement retains pose scheduling state");
        schedule.Accumulate(actor,10,true);
        if(schedule.Consume(actor)!=1)throw new InvalidOperationException("Long render stalls exceed the bounded pose catch-up budget");
        GD.Print("POSE_SCHEDULER_PASS: actual variable frame intervals, distance stride changes, pause/visibility reset, identity reuse and bounded catch-up");
    }
}
