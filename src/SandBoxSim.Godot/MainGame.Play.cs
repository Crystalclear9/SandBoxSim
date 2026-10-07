using System;
using System.Diagnostics;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;
public partial class MainGame
{
    private void AdvanceWorld(int ticks)
    {
        using var profile=RenderProfile.Measure("SimulationTicks");
        while(ticks>0)
        {
            int boundary=Sim.Config.Clock.TicksPerDay-(int)(Sim.Clock%Sim.Config.Clock.TicksPerDay);
            int step=Math.Min(ticks,boundary); Sim.Tick(step); ticks-=step; Wild.Advance(Sim);
        }
    }
    private void AdvanceInteractive(double delta)
    {
        // Keep a bounded debt and yield between ticks; never change the core tick order.
        double rate=Sim.Config.Clock.TicksPerSecondAt1x*_speed;
        _pending=Math.Min(_pending+Math.Min(delta,.1)*rate,Math.Max(1,rate*.25));
        int limit=Math.Min((int)_pending,Math.Max(1,Sim.Config.Clock.MaxCatchUpTicksPerFrame));
        long started=Stopwatch.GetTimestamp();
        for(int i=0;i<limit;i++)
        {
            AdvanceWorld(1);_pending--;
            if((Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency>=4)break;
        }
    }
    private void ValidateInteractiveBudget()
    {
        int savedSpeed=_speed;double savedPending=_pending;long before=Sim.Clock;
        try
        {
            _speed=32;_pending=10000;AdvanceInteractive(10);
            long advanced=Sim.Clock-before;
            if(advanced<=0||advanced>Sim.Config.Clock.MaxCatchUpTicksPerFrame||_pending>Sim.Config.Clock.TicksPerSecondAt1x*_speed*.25)
                throw new Exception("Interactive tick budget or bounded catch-up failed");
        }
        finally{_speed=savedSpeed;_pending=savedPending;}
    }
    private void ChangeWeather(WeatherKind kind) => Sim.InterveneForceWeather(kind,24);
}
