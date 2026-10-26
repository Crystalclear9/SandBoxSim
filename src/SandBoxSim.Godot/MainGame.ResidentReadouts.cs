using System;
using Godot;

namespace SandBoxSim.Client;

public partial class MainGame
{
    private readonly Label[] _vitalStates=new Label[4],_portraitFactStates=new Label[4];
    // Keep simulation values intact: health/energy and hunger/thirst have opposite
    // directions. The labels and severity make that distinction visible, not hover-only.
    private readonly record struct VitalReadout(string State,Color Tint,string Hint);
    private static VitalReadout DescribeVital(int metric,float value)
    {
        value=Math.Clamp(float.IsFinite(value)?value:0,0,1);
        bool need=metric is 1 or 2;
        bool severe=need?value>=.9f:value<=.15f;
        bool danger=need?value>.7f:value<.35f;
        bool warning=need?value>.4f:value<.6f;
        string state=metric switch
        {
            0=>severe?"濒危":danger?"虚弱":warning?"受损":"良好",
            1=>severe?"严重饥饿":danger?"饥饿":warning?"有些饥饿":"饱足",
            2=>severe?"严重缺水":danger?"口渴":warning?"有些口渴":"水分充足",
            _=>severe?"极度疲惫":danger?"疲惫":warning?"需休息":"充沛"
        };
        Color tint=danger||severe?new("#dfa38b"):warning?HudStyle.Accent:metric==0?new("#a4b69a"):metric==2?new("#9fbac0"):HudStyle.Accent;
        string name=new[]{"生命","饥饿","干渴","精力"}[metric];
        string meaning=metric switch{1=>"饥饿越高，越需要食物；此项不是饱食度。",2=>"干渴越高，越需要饮水；此项不是水分储量。",3=>"精力越高，越有活力；疲劳积累时会下降。",_=>"生命越高，身体状态越好。"};
        return new(state,tint,name+" "+value.ToString("P0")+" · "+state+"\n"+meaning);
    }
    private void ValidateResidentReadouts()
    {
        foreach(int metric in new[]{0,1,2,3})
        {
            var low=DescribeVital(metric,.1f);var high=DescribeVital(metric,.95f);
            if(low.State==high.State||low.Tint==high.Tint)throw new InvalidOperationException("Resident need severity is not distinguishable without reading a percentage");
            if(!(metric is 1 or 2?high.State.StartsWith("严重",StringComparison.Ordinal):low.State is "濒危" or "极度疲惫"))
                throw new InvalidOperationException("Resident need direction is reversed");
        }
        if(SelectedSlot<0)return;
        var agents=Sim.Agents;int slot=SelectedSlot;
        float[] values={agents.HealthOf(slot),agents.HungerOf(slot),agents.ThirstOf(slot),1-agents.FatigueOf(slot)};
        for(int i=0;i<4;i++)
        {
            if(_vitalValues[i].Text!=values[i].ToString("P0")||_vitalStates[i].Text!=DescribeVital(i,values[i]).State
                ||_portraitFactLabels[i].Text!=values[i].ToString("P0")||_portraitFactStates[i].Text!=_vitalStates[i].Text)
                throw new InvalidOperationException("Notebook and portrait do not describe the same actual resident values");
        }
        GD.Print("RESIDENT_READOUT_PASS: actual values, opposite need directions and shared text severity in notebook/portrait");
    }
}
