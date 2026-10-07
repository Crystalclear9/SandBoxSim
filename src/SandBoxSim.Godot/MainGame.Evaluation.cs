using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using SandBoxSim.Core;
using SandBoxSim.Core.Systems;

namespace SandBoxSim.Client;

internal static class EvaluationClock
{
    public static bool Enabled;
    public static double Delta(double real)=>Enabled?1.0/60:real;
}
internal sealed class EvaluationCommand
{
    public int Frame {get;set;}
    public string Command {get;set;}="";
    public string Value {get;set;}="";
}
internal sealed class EvaluationSpec
{
    public int SchemaVersion {get;set;}=1;
    public string Id {get;set;}="sample";
    public int Seed {get;set;}=839102;
    public int Agents {get;set;}=40;
    public int PreviewDays {get;set;}
    public string Panel {get;set;}="field";
    public int WarmupFrames {get;set;}=60;
    public int MeasuredFrames {get;set;}=120;
    public int TicksPerFrame {get;set;}
    public int[] CaptureFrames {get;set;}=Array.Empty<int>();
    public EvaluationCommand[] Commands {get;set;}=Array.Empty<EvaluationCommand>();
    public double MinimumFps {get;set;}
    public string OutputDirectory {get;set;}="runs/evaluation";
    public static readonly JsonSerializerOptions Json=new() {PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    public void Validate()
    {
        if(SchemaVersion!=1||(Id==null||!System.Text.RegularExpressions.Regex.IsMatch(Id,"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$"))||Agents<0||Agents>2000||PreviewDays<0||PreviewDays>30||WarmupFrames<3||WarmupFrames>3600||MeasuredFrames<2||MeasuredFrames>36000||TicksPerFrame<0||TicksPerFrame>20||!double.IsFinite(MinimumFps)||MinimumFps<0||MinimumFps>10000||string.IsNullOrWhiteSpace(OutputDirectory))throw new ArgumentException("Invalid evaluation limits");
        if(!new[]{"field","person","person-hands","architecture","characters","faces","equipment","actions","hands","naturemodels"}.Contains(Panel))throw new ArgumentException("Unknown evaluation panel");
        if(CaptureFrames==null||Commands==null||Commands.Length>4096||CaptureFrames.Length>32||CaptureFrames.Distinct().Count()!=CaptureFrames.Length||CaptureFrames.Any(f=>f<3||f>=WarmupFrames+MeasuredFrames))throw new ArgumentException("Invalid capture schedule");
        if((Panel is "person" or "person-hands")&&Agents==0)throw new ArgumentException("Resident panel requires a population");
        foreach(var e in Commands)
        {
            if(e==null||e.Value==null||e.Frame<3||e.Frame>=WarmupFrames+MeasuredFrames||!new[]{"journal.open","settings.open","escape","portrait.face","portrait.hands","portrait.body","portrait.drag","portrait.zoom","portrait.reset","world.view"}.Contains(e.Command))throw new ArgumentException("Unknown evaluation command");
            if(e.Command=="world.view"&&!new[]{"near","top","oblique"}.Contains(e.Value))throw new ArgumentException("Unknown camera preset");
            if(e.Command.StartsWith("portrait.")&&Panel is not ("person" or "person-hands"))throw new ArgumentException("Portrait command requires a resident panel");
            if((e.Command=="portrait.drag"||e.Command=="portrait.zoom")&&(!float.TryParse(e.Value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float v)||!float.IsFinite(v)||Math.Abs(v)>120))throw new ArgumentException("Invalid portrait input");
        }
    }
}
public partial class MainGame
{
    private EvaluationSpec? _evaluation;
    private int _evaluationFrame;
    private long _evaluationLast,_evaluationStart;
    private readonly List<double> _evaluationTimes=new();
    private readonly List<object> _evaluationCaptures=new(),_evaluationCommands=new(),_evaluationQuestions=new(),_evaluationAnswers=new();
    private bool _evaluationPending,_evaluationFinishing;
    private double _evaluationDrawCalls,_evaluationPrimitives;
    private void LoadEvaluation(string path)
    {
        _evaluation=JsonSerializer.Deserialize<EvaluationSpec>(File.ReadAllText(path),EvaluationSpec.Json)??throw new ArgumentException("Missing evaluation configuration");
        _evaluation.Validate();_initialPopulation=_evaluation.Agents;_previewDays=_evaluation.PreviewDays;_previewPanel=_evaluation.Panel;
        _evaluation.OutputDirectory=Path.GetFullPath(_evaluation.OutputDirectory);
        if(Directory.Exists(_evaluation.OutputDirectory)&&Directory.EnumerateFileSystemEntries(_evaluation.OutputDirectory).Any())throw new ArgumentException("Evaluation output must be empty; previous artifacts are preserved");
        Directory.CreateDirectory(_evaluation.OutputDirectory);EvaluationClock.Enabled=true;HudStyle.MotionEnabled=true;
        File.WriteAllText(Path.Combine(_evaluation.OutputDirectory,"request.json"),JsonSerializer.Serialize(_evaluation,EvaluationSpec.Json));
    }
    private object EvaluationState()=>new {tick=Sim.Clock,digest=StateHash.ComputeDigest(Sim),population=Sim.Agents.LiveCount,buildings=Sim.Buildings.TotalCompleted,journalVisible=_journalPanel.Visible,settingsVisible=_settingsPanel.Visible,portrait=_residentPortrait.EvaluationState()};
    private void ApplyEvaluationCommand(EvaluationCommand e)
    {
        var before=EvaluationState();string digest=StateHash.ComputeDigest(Sim);
        switch(e.Command)
        {
            case "journal.open":ShowJournal(true);break;
            case "settings.open":ShowSettings(true);break;
            case "escape":_UnhandledKeyInput(new InputEventKey {Keycode=Key.Escape,Pressed=true});break;
            case "portrait.face":_residentPortrait.EvaluationFraming("face");break;
            case "portrait.hands":_residentPortrait.EvaluationFraming("hands");break;
            case "portrait.body":_residentPortrait.EvaluationFraming("body");break;
            case "portrait.reset":_residentPortrait.EvaluationReset();break;
            case "portrait.drag":
                _residentPortrait._GuiInput(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=true});
                _residentPortrait._GuiInput(new InputEventMouseMotion {Relative=new(20,float.Parse(e.Value,System.Globalization.CultureInfo.InvariantCulture))});
                _residentPortrait._Input(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=false});break;
            case "portrait.zoom":_residentPortrait._GuiInput(new InputEventMouseButton {ButtonIndex=float.Parse(e.Value,System.Globalization.CultureInfo.InvariantCulture)>0?MouseButton.WheelUp:MouseButton.WheelDown,Pressed=true});break;
            case "world.view":((WorldView3D)_map).SetPerspective(e.Value=="top"?"俯视":e.Value=="near"?"近景":"斜视");break;
        }
        _evaluationCommands.Add(new {frame=_evaluationFrame,command=e.Command,value=e.Value,before,state=EvaluationState(),simulationUnchanged=digest==StateHash.ComputeDigest(Sim)});
    }
    private void StepEvaluation()
    {
        if(_evaluation==null||_evaluationFinishing||_evaluationPending)return;
        int frame=_evaluationFrame;long now=Stopwatch.GetTimestamp();
        if(frame==_evaluation.WarmupFrames){RenderProfile.Reset();_evaluationStart=now;_evaluationLast=now;}
        else if(frame>_evaluation.WarmupFrames)
        {
            _evaluationTimes.Add((now-_evaluationLast)*1000.0/Stopwatch.Frequency);_evaluationLast=now;
            _evaluationDrawCalls+=Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
            _evaluationPrimitives+=Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
        }
        if(frame>=_evaluation.WarmupFrames+_evaluation.MeasuredFrames){FinishEvaluation(now);return;}
        AdvanceWorld(_evaluation.TicksPerFrame);
        foreach(var e in _evaluation.Commands.Where(e=>e.Frame==frame))ApplyEvaluationCommand(e);
        _evaluationFrame++;
        if(_evaluation.CaptureFrames.Contains(frame))CaptureEvaluation(frame);
    }
    private async void CaptureEvaluation(int frame)
    {
        _evaluationPending=true;
        try
        {
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            var image=GetViewport().GetTexture().GetImage();string name=$"frame-{frame:D5}.png",path=Path.Combine(_evaluation!.OutputDirectory,name);
            if(image==null||image.IsEmpty()||image.SavePng(path)!=Error.Ok)throw new IOException("Evaluation capture failed; use a graphical renderer");
            _evaluationCaptures.Add(new {frame,image=name,sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),state=EvaluationState()});
            foreach(var gallery in GetChildren().OfType<ModelGallery>())
            foreach(var region in gallery.EvaluationRegions())
            {
                var logical=GetViewport().GetVisibleRect().Size;var scale=new Vector2(image.GetWidth()/logical.X,image.GetHeight()/logical.Y);
                var rect=new Rect2(region.Bounds.Position*scale,region.Bounds.Size*scale);var clipped=rect.Intersection(new Rect2(Vector2.Zero,new Vector2(image.GetWidth(),image.GetHeight())));
                if(clipped.Size.X<8||clipped.Size.Y<8)throw new InvalidOperationException("Gallery region outside viewport");
                string id=$"f{frame:D5}-r{region.Index}",cropName=id+".png";
                var crop=image.GetRegion(new Rect2I((int)clipped.Position.X,(int)clipped.Position.Y,(int)clipped.Size.X,(int)clipped.Size.Y));
                if(crop.SavePng(Path.Combine(_evaluation.OutputDirectory,cropName))!=Error.Ok)throw new IOException("Crop write failed");
                _evaluationQuestions.Add(new {id,image=cropName,sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(_evaluation.OutputDirectory,cropName)))).ToLowerInvariant(),prompt="Which object is shown?",choices=new[]{"house","storage","resident","deer","wolf","tree","farm","mine","ruins","grove"}});
                _evaluationAnswers.Add(new {id,label=region.Label,frame,region=new[]{clipped.Position.X,clipped.Position.Y,clipped.Size.X,clipped.Size.Y}});
            }
        }
        catch(Exception ex){GD.PrintErr(ex.Message);GetTree().Quit(2);}
        finally{_evaluationPending=false;}
    }
    private void FinishEvaluation(long now)
    {
        _evaluationFinishing=true;double seconds=(now-_evaluationStart)/(double)Stopwatch.Frequency,fps=_evaluationTimes.Count/seconds;
        var ordered=_evaluationTimes.OrderBy(v=>v).ToArray();double Percentile(double p)=>ordered[Math.Clamp((int)Math.Ceiling(p*ordered.Length)-1,0,ordered.Length-1)];
        bool pass=fps>=_evaluation!.MinimumFps;
        var result=new {schemaVersion=1,id=_evaluation.Id,status=pass?"completed":"performance_threshold_failed",timing="monotonic-wall-clock; render submission intervals include capture IO",engine=Engine.GetVersionInfo()["string"].AsString(),renderer=RenderingServer.GetCurrentRenderingMethod(),vsync=DisplayServer.WindowGetVsyncMode().ToString(),os=OS.GetName(),processor=OS.GetProcessorName(),videoAdapter=RenderingServer.GetVideoAdapterName(),resolution=new[]{DisplayServer.WindowGetSize().X,DisplayServer.WindowGetSize().Y},logicalResolution=new[]{GetViewport().GetVisibleRect().Size.X,GetViewport().GetVisibleRect().Size.Y},fixedVisualDelta=1.0/60,seed=_evaluation.Seed,warmupFrames=_evaluation.WarmupFrames,measuredFrames=_evaluationTimes.Count,ticksPerFrame=_evaluation.TicksPerFrame,wallSeconds=seconds,averageFps=fps,frameMs=new {p50=Percentile(.5),p95=Percentile(.95),p99=Percentile(.99),max=ordered[^1]},averageDrawCalls=_evaluationDrawCalls/ordered.Length,averagePrimitives=_evaluationPrimitives/ordered.Length,managedMemoryBytes=GC.GetTotalMemory(false),cpuScopes=RenderProfile.Report(),state=EvaluationState(),captures=_evaluationCaptures,commands=_evaluationCommands};
        foreach(var (file,data) in new[]{("result.json",(object)result),("questions.json",_evaluationQuestions),("answer-key.json",_evaluationAnswers)})
            File.WriteAllText(Path.Combine(_evaluation.OutputDirectory,file),JsonSerializer.Serialize(data,EvaluationSpec.Json));
        GD.Print("EVALUATION_COMPLETE "+_evaluation.OutputDirectory);GetTree().Quit(pass?0:1);
    }
    private static void ValidateEvaluationContract()
    {
        var valid=new EvaluationSpec {CaptureFrames=new[]{3,60},Commands=new[]{new EvaluationCommand {Frame=4,Command="world.view",Value="top"}}};valid.Validate();
        foreach(var bad in new[]{new EvaluationSpec {TicksPerFrame=-1},new EvaluationSpec {CaptureFrames=new[]{3,3}},new EvaluationSpec {Commands=new[]{new EvaluationCommand {Frame=4,Command="execute"}}}})
        {bool rejected=false;try{bad.Validate();}catch(ArgumentException){rejected=true;}if(!rejected)throw new Exception("Invalid evaluation request accepted");}
        bool unknownRejected=false;try{JsonSerializer.Deserialize<EvaluationSpec>("{\"bogus\":1}",EvaluationSpec.Json);}catch(JsonException){unknownRejected=true;}
        if(!unknownRejected)throw new Exception("Unknown evaluation property accepted");GD.Print("EVALUATION_CONTRACT_PASS");
    }
}
