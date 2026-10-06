using System;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;
public partial class MainGame
{
    private void AdvanceWorld(int ticks)
    {
        while(ticks>0)
        {
            int boundary=Sim.Config.Clock.TicksPerDay-(int)(Sim.Clock%Sim.Config.Clock.TicksPerDay);
            int step=Math.Min(ticks,boundary); Sim.Tick(step); ticks-=step; Wild.Advance(Sim);
        }
    }
    private void ChangeWeather(WeatherKind kind) => Sim.InterveneForceWeather(kind,24);
}
