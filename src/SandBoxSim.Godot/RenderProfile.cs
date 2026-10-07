using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
namespace SandBoxSim.Client;
internal static class RenderProfile
{
    private sealed class Sample {public int Calls;public double TotalMs,MaxMs;}
    private static readonly Dictionary<string,Sample> Samples=new();
    public static void Reset()=>Samples.Clear();
    public static object Report()=>Samples.OrderBy(pair=>pair.Key).ToDictionary(pair=>pair.Key,pair=>(object)new {calls=pair.Value.Calls,totalMs=pair.Value.TotalMs,maxMs=pair.Value.MaxMs});
    public static Scope Measure(string name)=>new(name);
    internal readonly struct Scope:IDisposable
    {
        private readonly string _name;private readonly long _start;
        public Scope(string name){_name=name;_start=EvaluationClock.Enabled?Stopwatch.GetTimestamp():0;}
        public void Dispose()
        {
            if(_start==0)return;
            double ms=(Stopwatch.GetTimestamp()-_start)*1000.0/Stopwatch.Frequency;
            if(!Samples.TryGetValue(_name,out var sample)){sample=new();Samples[_name]=sample;}
            sample.Calls++;sample.TotalMs+=ms;sample.MaxMs=Math.Max(sample.MaxMs,ms);
        }
    }
}
