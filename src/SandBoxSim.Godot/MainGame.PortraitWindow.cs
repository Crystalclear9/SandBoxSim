using System;
using System.Linq;
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
    private Label _portraitFactAction=null!;
    private readonly Label[] _portraitInventoryValues=new Label[4];
    private Godot.Button _portraitPin=null!;
    private Godot.Button _portraitPrevious=null!,_portraitNext=null!;
    private Label _portraitIndex=null!;
    private readonly Button[] _portraitLighting=new Button[2];
    private static readonly ResourceKind[] PortraitResources={ResourceKind.Food,ResourceKind.Wood,ResourceKind.Stone,ResourceKind.Iron};
    private static Label PortraitPaperLabel(string text,int size=13,bool muted=false)
    {var label=HudStyle.Label(text,size);label.AddThemeColorOverride("font_color",muted?HudStyle.Muted:HudStyle.Text);return label;}
    private static Godot.Button PortraitPaperButton(string text,Action action,bool primary=false)
    {
        var button=new Godot.Button {Text=text,CustomMinimumSize=new(0,40)};HudStyle.Button(button,primary);
        foreach(string state in new[]{"font_color","font_focus_color","font_hover_color","font_pressed_color"})button.AddThemeColorOverride(state,primary?HudStyle.Ink:state=="font_color"?HudStyle.Text:HudStyle.Accent);
        var normal=HudStyle.Box(primary?HudStyle.Accent:HudStyle.Wash,6,10,false);
        button.AddThemeStyleboxOverride("normal",normal);
        foreach(string state in new[]{"hover","pressed","hover_pressed"})button.AddThemeStyleboxOverride(state,HudStyle.Box(primary?HudStyle.Accent.Lightened(.10f):HudStyle.Wash.Lightened(.10f),6,10,false));
        var focus=HudStyle.Box(new Color(0,0,0,0),6,9);focus.BorderColor=HudStyle.Accent;focus.ShadowSize=0;button.AddThemeStyleboxOverride("focus",focus);
        button.Pressed+=action;return button;
    }
    public override void _Input(InputEvent input)
    {
        if(input is InputEventKey {Pressed:true,Echo:false,Keycode:Key.K} search && (search.CtrlPressed||search.MetaPressed) && _portraitOverlay?.Visible!=true)
        {
            ShowTools(true);_toolSearch.GrabFocus();_toolSearch.SelectAll();GetViewport().SetInputAsHandled();return;
        }
        if(_portraitOverlay?.Visible!=true||input is not InputEventKey {Pressed:true} key)return;
        var focused=GetViewport().GuiGetFocusOwner();
        if(focused==null||!_portraitWindow.IsAncestorOf(focused))_residentPortrait.GrabFocus();
        if(key.Keycode is not (Key.Tab or Key.Left or Key.Right or Key.Up or Key.Down))return;
        var controls=new System.Collections.Generic.List<Control>();
        void Visit(Node node)
        {
            if(node is Control control&&control.IsVisibleInTree()&&control.FocusMode==FocusModeEnum.All&&control is not BaseButton {Disabled:true})controls.Add(control);
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
        _portraitOverlay=new ColorRect {Color=new Color(.008f,.012f,.020f,.64f),Visible=false,MouseFilter=MouseFilterEnum.Stop};
        _portraitOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);overlay.AddChild(_portraitOverlay);
        _portraitOverlay.GuiInput+=input=>{if(input is InputEventMouseButton {ButtonIndex:MouseButton.Left,Pressed:true})ShowPortraitWindow(false);};
        _portraitWindow=new PanelContainer {MouseFilter=MouseFilterEnum.Stop};
        var paper=HudStyle.Box(HudStyle.Surface,10,20);paper.BorderColor=HudStyle.Border;paper.ShadowSize=28;
        _portraitWindow.AddThemeStyleboxOverride("panel",paper);_portraitOverlay.AddChild(_portraitWindow);
        var layout=new VBoxContainer();layout.AddThemeConstantOverride("separation",12);_portraitWindow.AddChild(layout);
        var header=new HBoxContainer();layout.AddChild(header);
        var title=PortraitPaperLabel("居民档案",18);title.SizeFlagsHorizontal=SizeFlags.ExpandFill;header.AddChild(title);
        var lighting=new HBoxContainer();lighting.AddThemeConstantOverride("separation",6);header.AddChild(lighting);
        for(int i=0;i<2;i++)
        {
            int mode=i;var button=PortraitPaperButton(i==0?"柔光":"侧光",()=>{_residentPortrait.SetLighting(mode);RefreshPortraitFacts();});
            button.ToggleMode=true;button.CustomMinimumSize=new(56,32);
            var chosen=HudStyle.Box(HudStyle.Wash.Lightened(.08f),4,10,false);chosen.BorderWidthBottom=2;chosen.BorderColor=HudStyle.Accent;
            button.AddThemeStyleboxOverride("pressed",chosen);button.AddThemeStyleboxOverride("hover_pressed",chosen);
            button.TooltipText=i==0?"均匀观察肤色和衣料，不改变世界光照":"从侧面检查面部、手部与衣褶起伏";
            lighting.AddChild(button);_portraitLighting[i]=button;
        }
        header.AddChild(new VSeparator {MouseFilter=MouseFilterEnum.Ignore});
        _portraitPrevious=PortraitPaperButton("‹ 上一位",()=>BrowsePortraitResident(-1));header.AddChild(_portraitPrevious);
        _portraitIndex=PortraitPaperLabel("",12,true);_portraitIndex.CustomMinimumSize=new(64,0);_portraitIndex.HorizontalAlignment=HorizontalAlignment.Center;header.AddChild(_portraitIndex);
        _portraitNext=PortraitPaperButton("下一位 ›",()=>BrowsePortraitResident(1));header.AddChild(_portraitNext);
        var close=PortraitPaperButton("返回手记  ×",()=>ShowPortraitWindow(false));header.AddChild(close);
        var content=new HBoxContainer {SizeFlagsVertical=SizeFlags.ExpandFill};content.AddThemeConstantOverride("separation",24);layout.AddChild(content);
        _portraitWindowBody=new VBoxContainer {SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill};content.AddChild(_portraitWindowBody);
        var factsPanel=new PanelContainer {CustomMinimumSize=new(284,0)};
        var factsStyle=HudStyle.Box(new Color(0,0,0,0),0,8,false);factsStyle.BorderWidthLeft=1;factsStyle.BorderColor=HudStyle.Border;factsStyle.ContentMarginLeft=22;
        factsPanel.AddThemeStyleboxOverride("panel",factsStyle);content.AddChild(factsPanel);
        var sideLayout=new VBoxContainer();sideLayout.AddThemeConstantOverride("separation",12);factsPanel.AddChild(sideLayout);
        var scroll=new ScrollContainer {HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,FollowFocus=true,SizeFlagsVertical=SizeFlags.ExpandFill};sideLayout.AddChild(scroll);
        var facts=new VBoxContainer {SizeFlagsHorizontal=SizeFlags.ExpandFill};facts.AddThemeConstantOverride("separation",12);scroll.AddChild(facts);
        facts.AddChild(PortraitPaperLabel("正在观察的居民",11,true));
        _portraitWindowTitle=PortraitPaperLabel("",30);_portraitWindowTitle.AutowrapMode=TextServer.AutowrapMode.WordSmart;facts.AddChild(_portraitWindowTitle);
        _portraitWindowActivity=PortraitPaperLabel("",12,true);_portraitWindowActivity.AutowrapMode=TextServer.AutowrapMode.WordSmart;facts.AddChild(_portraitWindowActivity);
        facts.AddChild(new HSeparator());
        facts.AddChild(PortraitPaperLabel("当前行为",11,true));_portraitFactAction=PortraitPaperLabel("",15);_portraitFactAction.AutowrapMode=TextServer.AutowrapMode.WordSmart;facts.AddChild(_portraitFactAction);
        facts.AddChild(new HSeparator());facts.AddChild(PortraitPaperLabel("身体与需求",11,true));
        var metrics=new GridContainer {Columns=2};metrics.AddThemeConstantOverride("h_separation",8);metrics.AddThemeConstantOverride("v_separation",8);facts.AddChild(metrics);
        string[] metricNames={"生命","饥饿","干渴","精力"};
        for(int i=0;i<4;i++)
        {
            var tile=new PanelContainer {SizeFlagsHorizontal=SizeFlags.ExpandFill};tile.AddThemeStyleboxOverride("panel",HudStyle.Box(HudStyle.Wash,6,10,false));metrics.AddChild(tile);
            var metric=new VBoxContainer();metric.AddThemeConstantOverride("separation",4);tile.AddChild(metric);metric.AddChild(PortraitPaperLabel(metricNames[i],11,true));
            _portraitFactLabels[i]=PortraitPaperLabel("",22);_portraitFactLabels[i].AddThemeFontOverride("font",HudStyle.NumericFont);metric.AddChild(_portraitFactLabels[i]);
            var bar=new ProgressBar {MaxValue=1,ShowPercentage=false,CustomMinimumSize=new(0,4),MouseFilter=MouseFilterEnum.Ignore};
            bar.AddThemeStyleboxOverride("background",HudStyle.Box(HudStyle.Wash,2,0,false));bar.AddThemeStyleboxOverride("fill",HudStyle.Box(HudStyle.Accent,2,0,false));metric.AddChild(bar);_portraitFactBars[i]=bar;
        }
        facts.AddChild(new HSeparator());facts.AddChild(PortraitPaperLabel("随身物资",11,true));
        var inventory=new GridContainer {Columns=2};inventory.AddThemeConstantOverride("h_separation",18);inventory.AddThemeConstantOverride("v_separation",12);facts.AddChild(inventory);
        string[] resources={"食物","木材","石料","铁矿"};
        for(int i=0;i<4;i++)
        {
            var cell=new VBoxContainer {SizeFlagsHorizontal=SizeFlags.ExpandFill};cell.AddThemeConstantOverride("separation",1);inventory.AddChild(cell);
            cell.AddChild(PortraitPaperLabel(resources[i],11,true));var amount=PortraitPaperLabel("",22);amount.AddThemeFontOverride("font",HudStyle.NumericFont);cell.AddChild(amount);_portraitInventoryValues[i]=amount;
        }
        sideLayout.AddChild(PortraitPaperButton("在世界中跟随  →",()=>{int slot=SelectedSlot;if(slot<0)return;ShowPortraitWindow(false);_map.Follow(slot);},true));
        _portraitPin=PortraitPaperButton("关注此居民",()=>{TogglePin();RefreshPortraitFacts();});sideLayout.AddChild(_portraitPin);
        var guide=PortraitPaperLabel("拖动旋转 · 滚轮缩放\n双击复位 · Esc 返回",11,true);sideLayout.AddChild(guide);
        foreach(Node child in facts.GetChildren())if(child is HSeparator line)line.AddThemeStyleboxOverride("separator",HudStyle.Box(HudStyle.Border,0,0,false));
        _residentPortrait.ExpandRequested+=()=>ShowPortraitWindow(!_portraitOverlay.Visible);
        FitPortraitWindow(Size);
    }
    private void FitPortraitWindow(Vector2 size)
    {
        if(_portraitWindow==null)return;
        var dimensions=new Vector2(MathF.Min(1200,size.X-72),MathF.Min(800,size.Y-96));
        HudStyle.Float(_portraitWindow,new(.5f,.5f),-dimensions*.5f,dimensions);
    }
    private void BrowsePortraitResident(int direction)
    {
        var slots=Sim.Agents.AliveSlots().OrderBy(slot=>Sim.Society.Identity(slot)).ToArray();
        if(slots.Length<2)return;
        int index=Array.IndexOf(slots,SelectedSlot);
        InspectActor(slots[(index+direction+slots.Length)%slots.Length]);
    }
    private void ShowPortraitWindow(bool visible)
    {
        if(_portraitOverlay==null||visible==_portraitOverlay.Visible)return;
        if(visible)
        {
            if(SelectedSlot<0)return;SetImmersive(false);
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
        for(int i=0;i<2;i++)_portraitLighting[i].SetPressedNoSignal(_residentPortrait.Lighting==i);
        int slot=SelectedSlot;if(slot<0||_portraitFactAction==null)return;
        _portraitWindowTitle.Text=_residentTitle.Text;
        _portraitWindowActivity.Text=_residentSubtitle.Text;
        var agents=Sim.Agents;_portraitFactAction.Text=_residentAction.Text+"\n"+_residentActionPhase.Text;
        float[] values={agents.HealthOf(slot),agents.HungerOf(slot),agents.ThirstOf(slot),1-agents.FatigueOf(slot)};
        int count=0,index=0;
        foreach(int resident in agents.AliveSlots()){count++;if(Sim.Society.Identity(resident)<_selectedPersonId)index++;}
        _portraitIndex.Text=$"{index+1} / {count}";_portraitPrevious.Disabled=_portraitNext.Disabled=count<2;
        for(int i=0;i<4;i++)
        {
            _portraitFactLabels[i].Text=values[i].ToString("P0");_portraitFactBars[i].Value=values[i];
            bool danger=i is 1 or 2?values[i]>.7f:values[i]<.35f;
            ((StyleBoxFlat)_portraitFactBars[i].GetThemeStylebox("fill")).BgColor=danger?new Color("#d88c79"):i==0?new Color("#aabf9d"):i==2?new Color("#9cbfcf"):HudStyle.Accent;
        }
        for(int i=0;i<4;i++)_portraitInventoryValues[i].Text=agents.InventoryOf(slot,PortraitResources[i]).ToString("0.0");
        _portraitPin.Text=PinnedPerson!=0&&PinnedPerson==_selectedPersonId?"已关注 · 取消关注":"关注此居民";
    }
    private void ValidatePortraitWindow()
    {
        if(SelectedSlot<0)return;
        string before=StateHash.ComputeDigest(Sim);
        var state=System.Text.Json.JsonSerializer.Serialize(_residentPortrait.EvaluationState());
        ShowJournal(true);_drawer.CurrentTab=1;_journalButton.GrabFocus();RefreshPresentationControls();
        if(!_contextFocus.Visible||!_contextDetails.Visible||!_toolBadge.Text.Contains(Sim.Agents.NameOrOverride(SelectedSlot)))throw new InvalidOperationException("Selection card does not reflect the actual resident");
        _contextFocus.EmitSignal(Godot.Button.SignalName.Pressed);
        if(_map is WorldView3D world&&(world.CameraDistance<10||world.CameraDistance>12))throw new InvalidOperationException("Selection focus does not approach the resident");
        _contextDetails.EmitSignal(Godot.Button.SignalName.Pressed);
        if(!_portraitOverlay.Visible||!_residentPortrait.Expanded||_residentPortrait.GetParent()!=_portraitWindowBody)
            throw new InvalidOperationException("Large portrait did not reuse the selected model viewport");
        if(state!=System.Text.Json.JsonSerializer.Serialize(_residentPortrait.EvaluationState()))
            throw new InvalidOperationException("Expanding a portrait reset its framing or orbit");
        _portraitLighting[1].EmitSignal(BaseButton.SignalName.Pressed);
        if(_residentPortrait.Lighting!=1||!_portraitLighting[1].ButtonPressed||_portraitLighting[0].ButtonPressed)throw new InvalidOperationException("Studio lighting toggle has no visible selected state");
        _portraitLighting[0].EmitSignal(BaseButton.SignalName.Pressed);
        if(GetViewport().GuiGetFocusOwner()!=_residentPortrait)throw new InvalidOperationException("Portrait inherited background keyboard focus");
        if(Sim.Agents.LiveCount>1)
        {
            long original=_selectedPersonId;
            _portraitNext.EmitSignal(Godot.Button.SignalName.Pressed);
            if(_selectedPersonId==original||!_portraitOverlay.Visible)throw new InvalidOperationException("Resident browsing did not change the real selection inside the modal");
            _portraitPrevious.EmitSignal(Godot.Button.SignalName.Pressed);
            if(_selectedPersonId!=original||state!=System.Text.Json.JsonSerializer.Serialize(_residentPortrait.EvaluationState()))throw new InvalidOperationException("Resident browsing did not restore selection or preserve framing");
        }
        bool previousDisabled=_portraitPrevious.Disabled,nextDisabled=_portraitNext.Disabled;
        try
        {
            _portraitPrevious.Disabled=_portraitNext.Disabled=true;_portraitPin.GrabFocus();
            _Input(new InputEventKey {Keycode=Key.Tab,Pressed=true});
            if(GetViewport().GuiGetFocusOwner()==_portraitPin||GetViewport().GuiGetFocusOwner()==_portraitPrevious||GetViewport().GuiGetFocusOwner()==_portraitNext)
                throw new InvalidOperationException("Disabled resident navigation traps keyboard focus");
        }
        finally{_portraitPrevious.Disabled=previousDisabled;_portraitNext.Disabled=nextDisabled;}
        long priorPin=PinnedPerson;
        try
        {
            PinnedPerson=0;_portraitPin.EmitSignal(Godot.Button.SignalName.Pressed);
            if(PinnedPerson!=_selectedPersonId)throw new InvalidOperationException("Portrait failed to pin the real selected resident");
            _portraitPin.EmitSignal(Godot.Button.SignalName.Pressed);
            if(PinnedPerson!=0)throw new InvalidOperationException("Portrait failed to unpin the resident");
        }
        finally{PinnedPerson=priorPin;RefreshOperations();_discovery.Refresh();RefreshPortraitFacts();}
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
        GD.Print("PORTRAIT_WINDOW_PASS: shared model/view, preserved framing, pin/unpin, initial/repeated/directional focus isolation and restore, Escape priority, notebook return and observation purity");
    }
}
