using System;
using Godot;
using SandBoxSim.Core;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;

public partial class MainGame
{
    private ColorRect _portraitOverlay=null!;
    private PanelContainer _portraitWindow=null!,_portraitHome=null!;
    private VBoxContainer _portraitWindowBody=null!;
    private Label _portraitWindowTitle=null!,_portraitWindowActivity=null!;
    private Control? _portraitReturnFocus;
    private readonly Label[] _portraitFactLabels=new Label[4];
    private readonly ProgressBar[] _portraitFactBars=new ProgressBar[4];
    private Label _portraitFactAction=null!,_portraitFactInventory=null!;
    private static Label PortraitPaperLabel(string text,int size=13,bool muted=false)
    {var label=HudStyle.Label(text,size);label.AddThemeColorOverride("font_color",muted?HudStyle.PaperMuted:HudStyle.PaperInk);return label;}
    public override void _Input(InputEvent input)
    {
        if(_portraitOverlay?.Visible!=true||input is not InputEventKey {Pressed:true} key)return;
        var focused=GetViewport().GuiGetFocusOwner();
        if(focused==null||!_portraitWindow.IsAncestorOf(focused))_residentPortrait.GrabFocus();
        if(key.Keycode is not (Key.Tab or Key.Left or Key.Right or Key.Up or Key.Down))return;
        var controls=new System.Collections.Generic.List<Control>();
        void Visit(Node node)
        {
            if(node is Control control&&control.IsVisibleInTree()&&control.FocusMode==FocusModeEnum.All)controls.Add(control);
            foreach(Node child in node.GetChildren())Visit(child);
        }
        Visit(_portraitWindow);
        if(controls.Count>0)
        {
            int current=controls.IndexOf(GetViewport().GuiGetFocusOwner());
            bool backwards=key.Keycode is Key.Left or Key.Up||key.Keycode==Key.Tab&&key.ShiftPressed;
            int next=current<0?(backwards?controls.Count-1:0):(current+(backwards?-1:1)+controls.Count)%controls.Count;
            controls[next].GrabFocus();
        }
        GetViewport().SetInputAsHandled();
    }
    private void BuildPortraitWindow(Control overlay)
    {
        _portraitOverlay=new ColorRect {Color=new Color(.015f,.025f,.020f,.48f),Visible=false,MouseFilter=MouseFilterEnum.Stop};
        _portraitOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);overlay.AddChild(_portraitOverlay);
        _portraitOverlay.GuiInput+=input=>{if(input is InputEventMouseButton {ButtonIndex:MouseButton.Left,Pressed:true})ShowPortraitWindow(false);};
        _portraitWindow=new PanelContainer {MouseFilter=MouseFilterEnum.Stop};
        var paper=HudStyle.Box(HudStyle.Paper,5,20);paper.BorderColor=HudStyle.PaperLine;paper.ShadowSize=18;
        _portraitWindow.AddThemeStyleboxOverride("panel",paper);_portraitOverlay.AddChild(_portraitWindow);
        var layout=new VBoxContainer();layout.AddThemeConstantOverride("separation",12);_portraitWindow.AddChild(layout);
        var header=new HBoxContainer();layout.AddChild(header);
        var identity=new VBoxContainer {SizeFlagsHorizontal=SizeFlags.ExpandFill};identity.AddThemeConstantOverride("separation",3);header.AddChild(identity);
        identity.AddChild(PortraitPaperLabel("居民档案 / 人物观察",11,true));_portraitWindowTitle=PortraitPaperLabel("",26);identity.AddChild(_portraitWindowTitle);
        var close=new Godot.Button {Text="返回手记",CustomMinimumSize=new(96,36)};HudStyle.Button(close);header.AddChild(close);close.Pressed+=()=>ShowPortraitWindow(false);
        close.SetMeta("notebook",true);close.AddThemeColorOverride("font_color",HudStyle.PaperInk);close.AddThemeColorOverride("font_hover_color",HudStyle.PaperAccent);close.AddThemeColorOverride("font_pressed_color",HudStyle.PaperAccent);
        close.AddThemeStyleboxOverride("hover",HudStyle.Box(HudStyle.PaperWash,4,9,false));close.AddThemeStyleboxOverride("pressed",HudStyle.Box(HudStyle.PaperWash,4,9,false));
        _portraitWindowActivity=PortraitPaperLabel("",12,true);layout.AddChild(_portraitWindowActivity);
        var content=new HBoxContainer {SizeFlagsVertical=SizeFlags.ExpandFill};content.AddThemeConstantOverride("separation",20);layout.AddChild(content);
        _portraitWindowBody=new VBoxContainer {SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill};content.AddChild(_portraitWindowBody);
        var factsPanel=new PanelContainer {CustomMinimumSize=new(224,0)};factsPanel.AddThemeStyleboxOverride("panel",HudStyle.Box(HudStyle.PaperWash,3,16,false));content.AddChild(factsPanel);
        var facts=new VBoxContainer();facts.AddThemeConstantOverride("separation",12);factsPanel.AddChild(facts);
        facts.AddChild(PortraitPaperLabel("当下",11,true));_portraitFactAction=PortraitPaperLabel("",15);_portraitFactAction.AutowrapMode=TextServer.AutowrapMode.WordSmart;facts.AddChild(_portraitFactAction);
        facts.AddChild(new HSeparator());facts.AddChild(PortraitPaperLabel("身体与需求",11,true));
        for(int i=0;i<4;i++)
        {
            var metric=new VBoxContainer();metric.AddThemeConstantOverride("separation",5);facts.AddChild(metric);
            _portraitFactLabels[i]=PortraitPaperLabel("");metric.AddChild(_portraitFactLabels[i]);
            var bar=new ProgressBar {MaxValue=1,ShowPercentage=false,CustomMinimumSize=new(0,4),MouseFilter=MouseFilterEnum.Ignore};
            bar.AddThemeStyleboxOverride("background",HudStyle.Box(HudStyle.PaperLine,1,0,false));bar.AddThemeStyleboxOverride("fill",HudStyle.Box(HudStyle.PaperMuted,1,0,false));metric.AddChild(bar);_portraitFactBars[i]=bar;
        }
        facts.AddChild(new HSeparator());facts.AddChild(PortraitPaperLabel("随身物资",11,true));
        _portraitFactInventory=PortraitPaperLabel("",13);facts.AddChild(_portraitFactInventory);
        _residentPortrait.ExpandRequested+=()=>ShowPortraitWindow(!_portraitOverlay.Visible);
        FitPortraitWindow(Size);
    }
    private void FitPortraitWindow(Vector2 size)
    {
        if(_portraitWindow==null)return;
        var dimensions=new Vector2(MathF.Min(1120,size.X-96),MathF.Min(800,size.Y-120));
        HudStyle.Float(_portraitWindow,new(.5f,.5f),-dimensions*.5f,dimensions);
    }
    private void ShowPortraitWindow(bool visible)
    {
        if(_portraitOverlay==null||visible==_portraitOverlay.Visible)return;
        if(visible)
        {
            if(SelectedSlot<0)return;
            _portraitReturnFocus=GetViewport().GuiGetFocusOwner();
            if(_map is WorldView3D world)world.CancelPointerGesture();
            RefreshResidentVitals();
            _residentPortrait.Reparent(_portraitWindowBody,false);
            _residentPortrait.SetExpanded(true);_portraitOverlay.Visible=true;
            _residentPortrait.GrabFocus();
            RefreshPortraitFacts();
            FitPortraitWindow(Size);FadeIn(_portraitWindow);
        }
        else
        {
            _residentPortrait.Reparent(_portraitHome,false);_residentPortrait.SetExpanded(false);_portraitOverlay.Visible=false;
            if(_portraitReturnFocus!=null&&GodotObject.IsInstanceValid(_portraitReturnFocus)&&_portraitReturnFocus.IsVisibleInTree()&&_portraitReturnFocus.FocusMode!=FocusModeEnum.None)
                _portraitReturnFocus.GrabFocus();
            _portraitReturnFocus=null;
        }
    }
    private void RefreshPortraitFacts()
    {
        int slot=SelectedSlot;if(slot<0||_portraitFactAction==null)return;
        _portraitWindowTitle.Text=_residentTitle.Text;
        _portraitWindowActivity.Text=_residentSubtitle.Text+"  ·  "+_residentAction.Text;
        var agents=Sim.Agents;_portraitFactAction.Text=_residentAction.Text+"\n"+_residentActionPhase.Text;
        float[] values={agents.HealthOf(slot),agents.HungerOf(slot),agents.ThirstOf(slot),1-agents.FatigueOf(slot)};
        string[] names={"生命","饥饿","干渴","精力"};
        for(int i=0;i<4;i++)
        {
            _portraitFactLabels[i].Text=names[i]+"    "+values[i].ToString("P0");_portraitFactBars[i].Value=values[i];
            bool danger=i is 1 or 2?values[i]>.7f:values[i]<.35f;
            ((StyleBoxFlat)_portraitFactBars[i].GetThemeStylebox("fill")).BgColor=danger?new Color("#a5533d"):HudStyle.PaperMuted;
        }
        _portraitFactInventory.Text=$"食物    {agents.InventoryOf(slot,ResourceKind.Food):0.0}\n木材    {agents.InventoryOf(slot,ResourceKind.Wood):0.0}\n石料    {agents.InventoryOf(slot,ResourceKind.Stone):0.0}\n铁矿    {agents.InventoryOf(slot,ResourceKind.Iron):0.0}";
    }
    private void ValidatePortraitWindow()
    {
        if(SelectedSlot<0)return;
        string before=StateHash.ComputeDigest(Sim);
        var state=System.Text.Json.JsonSerializer.Serialize(_residentPortrait.EvaluationState());
        ShowJournal(true);_drawer.CurrentTab=1;_journalButton.GrabFocus();ShowPortraitWindow(true);
        if(!_portraitOverlay.Visible||!_residentPortrait.Expanded||_residentPortrait.GetParent()!=_portraitWindowBody)
            throw new InvalidOperationException("Large portrait did not reuse the selected model viewport");
        if(state!=System.Text.Json.JsonSerializer.Serialize(_residentPortrait.EvaluationState()))
            throw new InvalidOperationException("Expanding a portrait reset its framing or orbit");
        if(GetViewport().GuiGetFocusOwner()!=_residentPortrait)throw new InvalidOperationException("Portrait inherited background keyboard focus");
        for(int i=0;i<16;i++)
        {
            _Input(new InputEventKey {Keycode=i<8?Key.Tab:i%2==0?Key.Left:Key.Down,Pressed=true,Echo=i%2==0,ShiftPressed=i>5});
            var focused=GetViewport().GuiGetFocusOwner();
            if(focused==null||!_portraitWindow.IsAncestorOf(focused))throw new InvalidOperationException("Portrait keyboard focus escaped to the world");
        }
        _UnhandledKeyInput(new InputEventKey {Keycode=Key.Escape,Pressed=true});
        if(_portraitOverlay.Visible||!_journalPanel.Visible||_residentPortrait.GetParent()!=_portraitHome||_residentPortrait.Expanded)
            throw new InvalidOperationException("Closing portrait did not restore the notebook");
        if(GetViewport().GuiGetFocusOwner()!=_journalButton)throw new InvalidOperationException("Portrait did not restore keyboard focus");
        if(before!=StateHash.ComputeDigest(Sim))throw new InvalidOperationException("Portrait expansion changed simulation state");
        GD.Print("PORTRAIT_WINDOW_PASS: shared model/view, preserved framing, initial/repeated/directional focus isolation and restore, Escape priority, notebook return and observation purity");
    }
}
