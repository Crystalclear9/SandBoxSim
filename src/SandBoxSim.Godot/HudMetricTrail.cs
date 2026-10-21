using System;
using System.Collections.Generic;
using Godot;

namespace SandBoxSim.Client;

/// <summary>A bounded history of actual counter changes, not a decorative waveform.</summary>
internal partial class HudMetricTrail : Control
{
    private readonly Queue<double> _values=new();
    private float _pulse;
    public HudMetricTrail()
    {
        CustomMinimumSize=new(30,7);MouseFilter=MouseFilterEnum.Ignore;
        SizeFlagsHorizontal=SizeFlags.ExpandFill;SetProcess(false);Resized+=QueueRedraw;
    }
    internal void Sample(string text)
    {
        if(!double.TryParse(text,System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out double value))return;
        if(_values.Count>0&&_values.ToArray()[^1]==value)return;
        _values.Enqueue(value);while(_values.Count>32)_values.Dequeue();
        _pulse=HudStyle.MotionEnabled?1:0;SetProcess(_pulse>0);QueueRedraw();
    }
    internal void ResetHistory(){_values.Clear();_pulse=0;SetProcess(false);QueueRedraw();}
    public override void _Process(double delta)
    {
        _pulse=HudStyle.MotionEnabled?Math.Max(0,_pulse-(float)delta*2):0;
        QueueRedraw();if(_pulse==0)SetProcess(false);
    }
    public override void _Draw()
    {
        var values=_values.ToArray();if(values.Length==0||Size.X<=1)return;
        double low=values[0],high=values[0];foreach(double value in values){low=Math.Min(low,value);high=Math.Max(high,value);}
        float Y(double value)=>(float)(Size.Y-1-(value-low)/Math.Max(1,high-low)*(Size.Y-2));
        DrawLine(new(0,Size.Y-1),new(Size.X,Size.Y-1),new Color(HudStyle.Border,.5f),1);
        if(values.Length<2)return;
        var points=new Vector2[values.Length];for(int i=0;i<points.Length;i++)points[i]=new(i*Size.X/(points.Length-1),Y(values[i]));
        DrawPolyline(points,new Color(HudStyle.Accent,.6f),1,true);
        DrawCircle(points[^1],1+_pulse,new Color(HudStyle.Accent,.7f));
    }
}
