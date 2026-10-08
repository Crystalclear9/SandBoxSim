using System;
using Godot;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;
public partial class WorldView3D
{
    /// <summary>Read-only visual response to real weather and the simulated day. No events or simulation RNG.</summary>
    private void UpdateWeatherAtmosphere(float delta)
    {
        float dayPhase = (Game.Sim.Clock % Game.Sim.Config.Clock.TicksPerDay) / (float)Game.Sim.Config.Clock.TicksPerDay;
        float daylight = .5f + .5f * MathF.Sin(dayPhase * MathF.Tau);
        WeatherKind kind = Game.Sim.World.Weather.Kind;
        bool wet = kind is WeatherKind.Rain or WeatherKind.Storm or WeatherKind.Snow;
        Color light = wet ? new Color("#cad7d5") : kind == WeatherKind.Drought ? new Color("#efd4a2") : new Color("#f3eee1");
        Color fog = wet ? new Color("#9daeb0") : new Color("#c5ceca");
        float speed = Math.Clamp(delta * 1.5f, 0, 1);
        _sunlight.LightColor = _sunlight.LightColor.Lerp(light, speed);
        _sunlight.LightEnergy = Mathf.Lerp(_sunlight.LightEnergy, (wet ? .55f : .85f) * (.6f + daylight * .4f), speed);
        _sunlight.RotationDegrees = new Vector3(Mathf.Lerp(_sunlight.RotationDegrees.X, -25 - daylight * 30, speed), -35, 0);
        _weatherEnvironment.FogLightColor = _weatherEnvironment.FogLightColor.Lerp(fog, speed);
        _weatherEnvironment.FogDensity = Mathf.Lerp(_weatherEnvironment.FogDensity, wet ? .0035f : .0015f, speed);
        _weatherEnvironment.AmbientLightEnergy = Mathf.Lerp(_weatherEnvironment.AmbientLightEnergy, .25f + daylight * .10f, speed);
    }
}
