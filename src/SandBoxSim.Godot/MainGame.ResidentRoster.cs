using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SandBoxSim.Core;
using SandBoxSim.Core.Systems;

namespace SandBoxSim.Client;

public partial class MainGame
{
    private PanelContainer _portraitRoster=null!;
    private Button _portraitRosterToggle=null!,_portraitRosterClose=null!;
    private LineEdit _portraitRosterSearch=null!;
    private ScrollContainer _portraitRosterScroll=null!;
    private VBoxContainer _portraitRosterRows=null!;
    private Label _portraitRosterCount=null!,_portraitRosterEmpty=null!;
    private HBoxContainer _portraitRosterPages=null!;
    private Button _portraitRosterPagePrevious=null!,_portraitRosterPageNext=null!;
    private Label _portraitRosterPageLabel=null!;
    private const int ResidentRosterPageSize=64;
    private int _portraitRosterPage;
    private long[] _portraitRosterMatches=Array.Empty<long>();
    private ulong _portraitRosterNextRefresh;
    private long _portraitRosterHighlight,_portraitRosterPinned;
    private readonly Dictionary<long,RosterRow> _portraitRosterEntries=new();
    private string _portraitRosterSignature="";
    private Simulation? _portraitRosterWorld;
    private ulong _portraitRosterLayoutRequest;
    private sealed record RosterRow(Button Button,Label Name,Label Detail,Label Badge);

    private void BuildPortraitRoster(HBoxContainer header)
    {
        _portraitRosterToggle=PortraitPaperButton("居民名录  ▾",()=>ShowPortraitRoster(!_portraitRoster.Visible));
        _portraitRosterToggle.Name="PortraitRosterToggle";_portraitRosterToggle.ToggleMode=true;
        _portraitRosterToggle.TooltipText="查找姓名、职业或人生阶段 · Ctrl / ⌘ F";header.AddChild(_portraitRosterToggle);
        _portraitRoster=new PanelContainer {Name="PortraitResidentRoster",Visible=false,MouseFilter=MouseFilterEnum.Stop};
        var plate=(StyleBoxFlat)HudStyle.Frame(14);plate.BgColor=HudStyle.Ink;plate.ShadowSize=20;
        _portraitRoster.AddThemeStyleboxOverride("panel",plate);_portraitOverlay.AddChild(_portraitRoster);
        var body=new VBoxContainer();body.AddThemeConstantOverride("separation",10);_portraitRoster.AddChild(body);
        var heading=new HBoxContainer();body.AddChild(heading);
        var title=HudStyle.Heading("居民名录",18);title.SizeFlagsHorizontal=SizeFlags.ExpandFill;heading.AddChild(title);
        _portraitRosterCount=HudStyle.Label("",11,true);_portraitRosterCount.Name="PortraitRosterCount";_portraitRosterCount.VerticalAlignment=VerticalAlignment.Center;heading.AddChild(_portraitRosterCount);
        _portraitRosterClose=PortraitPaperButton("×",()=>ShowPortraitRoster(false,true));HudStyle.QuietButton(_portraitRosterClose);
        _portraitRosterClose.CustomMinimumSize=new(32,32);_portraitRosterClose.TooltipText="收起名录 · Esc";heading.AddChild(_portraitRosterClose);
        _portraitRosterSearch=new LineEdit {Name="PortraitResidentSearch",PlaceholderText="搜索姓名、职业或阶段",ClearButtonEnabled=true,CustomMinimumSize=new(0,40)};
        _portraitRosterSearch.AddThemeFontSizeOverride("font_size",13);body.AddChild(_portraitRosterSearch);
        _portraitRosterSearch.TextChanged+=_=>{_portraitRosterPage=0;RefreshPortraitRoster(true);};
        _portraitRosterSearch.TextSubmitted+=_=>
        {
            long identity=_portraitRosterMatches.Contains(_selectedPersonId)&&_portraitRosterSearch.Text.Trim().Length==0
                ?_selectedPersonId:_portraitRosterMatches.FirstOrDefault();
            ChooseRosterResident(identity,_portraitRosterWorld);
        };
        _portraitRosterScroll=new ScrollContainer {Name="PortraitRosterScroll",HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,FollowFocus=true,SizeFlagsVertical=SizeFlags.ExpandFill};body.AddChild(_portraitRosterScroll);
        _portraitRosterRows=new VBoxContainer {SizeFlagsHorizontal=SizeFlags.ExpandFill};_portraitRosterRows.AddThemeConstantOverride("separation",3);_portraitRosterScroll.AddChild(_portraitRosterRows);
        _portraitRosterEmpty=HudStyle.Label("没有匹配的居民\n试试姓名、职业或人生阶段。",13,true);
        _portraitRosterEmpty.AutowrapMode=TextServer.AutowrapMode.WordSmart;_portraitRosterEmpty.Visible=false;body.AddChild(_portraitRosterEmpty);
        _portraitRosterPages=new HBoxContainer {Visible=false};_portraitRosterPages.AddThemeConstantOverride("separation",6);body.AddChild(_portraitRosterPages);
        _portraitRosterPagePrevious=PortraitPaperButton("上一页",()=>ChangeRosterPage(-1));_portraitRosterPagePrevious.Name="PortraitRosterPreviousPage";HudStyle.QuietButton(_portraitRosterPagePrevious,12);_portraitRosterPages.AddChild(_portraitRosterPagePrevious);
        _portraitRosterPageLabel=HudStyle.Label("",12,true);_portraitRosterPageLabel.Name="PortraitRosterPage";_portraitRosterPageLabel.SizeFlagsHorizontal=SizeFlags.ExpandFill;_portraitRosterPageLabel.HorizontalAlignment=HorizontalAlignment.Center;_portraitRosterPageLabel.VerticalAlignment=VerticalAlignment.Center;_portraitRosterPages.AddChild(_portraitRosterPageLabel);
        _portraitRosterPageNext=PortraitPaperButton("下一页",()=>ChangeRosterPage(1));_portraitRosterPageNext.Name="PortraitRosterNextPage";HudStyle.QuietButton(_portraitRosterPageNext,12);_portraitRosterPages.AddChild(_portraitRosterPageNext);
        var hint=HudStyle.Label("↑ ↓ 浏览  ·  Enter 选择  ·  Esc 返回",11,true);hint.MouseFilter=MouseFilterEnum.Ignore;body.AddChild(hint);
    }

    private void FitPortraitRoster(Vector2 size)
    {
        if(_portraitRoster==null)return;
        var window=new Vector2(_portraitWindow.OffsetRight-_portraitWindow.OffsetLeft,_portraitWindow.OffsetBottom-_portraitWindow.OffsetTop);
        var origin=size*.5f+new Vector2(_portraitWindow.OffsetLeft,_portraitWindow.OffsetTop);
        HudStyle.Float(_portraitRoster,Vector2.Zero,origin+new Vector2(20,76),new(320,MathF.Min(600,window.Y-100)));
    }

    private void ShowPortraitRoster(bool visible,bool returnFocus=false)
    {
        if(_portraitRoster==null)return;
        if(visible&&_portraitOverlay?.Visible!=true)return;
        _portraitRosterLayoutRequest++;_portraitRoster.Visible=visible;
        _portraitRosterToggle.SetPressedNoSignal(visible);
        _portraitRosterToggle.Text=visible?"居民名录  ▴":"居民名录  ▾";
        if(visible)
        {
            _portraitRosterSearch.Text="";_portraitRosterPage=-1;RefreshPortraitRoster(true);FitPortraitRoster(Size);
            FadeIn(_portraitRoster);_portraitRosterSearch.GrabFocus();
            if(_portraitRosterEntries.TryGetValue(_selectedPersonId,out var current))RevealRosterRow(current.Button);
        }
        else if(returnFocus&&_portraitRosterToggle.IsVisibleInTree())_portraitRosterToggle.GrabFocus();
    }

    private string RosterSearchText(int slot)=>Sim.Agents.NameOrOverride(slot)+" "+JobName(Sim.Agents.JobOf(slot))+" "+LifeStageName(Sim.Agents.LifeStageOf(slot));
    private void RefreshPortraitRoster(bool force=false)
    {
        if(_portraitRoster?.Visible!=true)return;
        ulong now=Time.GetTicksMsec();
        if(!force&&ReferenceEquals(_portraitRosterWorld,Sim)&&now<_portraitRosterNextRefresh
            &&_portraitRosterHighlight==_selectedPersonId&&_portraitRosterPinned==PinnedPerson)return;
        _portraitRosterNextRefresh=now+1000;_portraitRosterHighlight=_selectedPersonId;_portraitRosterPinned=PinnedPerson;
        var slots=Sim.Agents.AliveSlots().OrderBy(slot=>Sim.Society.Identity(slot)).ToArray();
        string query=_portraitRosterSearch.Text;
        string signature=query+"\n"+string.Join("\n",slots.Select(slot=>Sim.Society.Identity(slot)+":"+RosterSearchText(slot)));
        if(force||!ReferenceEquals(_portraitRosterWorld,Sim)||signature!=_portraitRosterSignature)
        {
            long focused=_portraitRosterEntries.FirstOrDefault(pair=>pair.Value.Button.HasFocus()).Key;
            _portraitRosterLayoutRequest++;_portraitRosterWorld=Sim;_portraitRosterSignature=signature;
            foreach(Node child in _portraitRosterRows.GetChildren()){_portraitRosterRows.RemoveChild(child);child.QueueFree();}
            _portraitRosterEntries.Clear();
            string[] terms=query.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
            var matches=slots.Where(slot=>terms.All(term=>RosterSearchText(slot).Contains(term,StringComparison.OrdinalIgnoreCase))).ToArray();
            _portraitRosterMatches=matches.Select(slot=>Sim.Society.Identity(slot)).ToArray();
            if(_portraitRosterPage<0)_portraitRosterPage=Math.Max(0,Array.IndexOf(_portraitRosterMatches,_selectedPersonId))/ResidentRosterPageSize;
            int pages=Math.Max(1,(matches.Length+ResidentRosterPageSize-1)/ResidentRosterPageSize);
            _portraitRosterPage=Math.Clamp(_portraitRosterPage,0,pages-1);
            _portraitRosterPages.Visible=pages>1;
            _portraitRosterPageLabel.Text=$"{_portraitRosterPage+1} / {pages}";
            _portraitRosterPagePrevious.Disabled=_portraitRosterPage==0;_portraitRosterPageNext.Disabled=_portraitRosterPage>=pages-1;
            var boundWorld=Sim;
            foreach(int slot in matches.Skip(_portraitRosterPage*ResidentRosterPageSize).Take(ResidentRosterPageSize))
            {
                long identity=Sim.Society.Identity(slot);
                var button=new Button {Name="RosterResident"+identity,CustomMinimumSize=new(0,60),SizeFlagsHorizontal=SizeFlags.ExpandFill,ToggleMode=true,ClipContents=true};
                button.SetMeta("resident_identity",identity);button.SetMeta("tool_row",true);HudStyle.Button(button);button.CustomMinimumSize=new(0,60);
                var chosen=HudStyle.Box(HudStyle.Wash,5,0,false);chosen.BorderWidthLeft=2;chosen.BorderColor=HudStyle.Accent;
                button.AddThemeStyleboxOverride("pressed",chosen);button.AddThemeStyleboxOverride("hover_pressed",chosen);
                var padding=new MarginContainer {MouseFilter=MouseFilterEnum.Ignore};padding.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                foreach(string margin in new[]{"margin_left","margin_right"})padding.AddThemeConstantOverride(margin,10);
                foreach(string margin in new[]{"margin_top","margin_bottom"})padding.AddThemeConstantOverride(margin,7);
                button.AddChild(padding);
                var lines=new VBoxContainer {MouseFilter=MouseFilterEnum.Ignore};lines.AddThemeConstantOverride("separation",2);padding.AddChild(lines);
                var top=new HBoxContainer {MouseFilter=MouseFilterEnum.Ignore};top.AddThemeConstantOverride("separation",8);lines.AddChild(top);
                var name=HudStyle.Heading("",15);name.SizeFlagsHorizontal=SizeFlags.ExpandFill;name.ClipText=true;name.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;top.AddChild(name);
                var badge=HudStyle.Label("",11,true);badge.VerticalAlignment=VerticalAlignment.Center;badge.AddThemeColorOverride("font_color",HudStyle.Accent);top.AddChild(badge);
                var detail=HudStyle.Label("",12,true);detail.ClipText=true;detail.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;lines.AddChild(detail);
                foreach(var label in new[]{name,badge,detail})label.MouseFilter=MouseFilterEnum.Ignore;
                _portraitRosterEntries[identity]=new(button,name,detail,badge);_portraitRosterRows.AddChild(button);
                button.Pressed+=()=>ChooseRosterResident(identity,boundWorld);button.FocusEntered+=()=>RevealRosterRow(button);
            }
            _portraitRosterScroll.ScrollVertical=0;
            if(focused!=0)
            {
                if(_portraitRosterEntries.TryGetValue(focused,out var restored))restored.Button.GrabFocus();else _portraitRosterSearch.GrabFocus();
            }
        }
        foreach(int slot in slots)
        {
            long identity=Sim.Society.Identity(slot);if(!_portraitRosterEntries.TryGetValue(identity,out var row))continue;
            row.Name.Text=Sim.Agents.NameOrOverride(slot);
            row.Detail.Text=LifeStageName(Sim.Agents.LifeStageOf(slot))+" · "+JobName(Sim.Agents.JobOf(slot))+" · "+Sim.Agents.AgeDaysOf(slot)+" 天";
            row.Badge.Text=identity==_selectedPersonId?(identity==PinnedPerson?"当前 · 已关注":"当前"):identity==PinnedPerson?"已关注":"";
            row.Button.SetPressedNoSignal(identity==_selectedPersonId);
            row.Button.TooltipText=row.Name.Text+"\n"+row.Detail.Text+"\n"+ActionRegistry.DisplayNameOf(Sim.Agents.ActionOf(slot))+" · "+Sim.Agents.PositionOf(slot);
            row.Button.AccessibilityName=row.Name.Text+"，"+row.Detail.Text;
        }
        _portraitRosterCount.Text=$"{_portraitRosterMatches.Length} / {slots.Length}";
        _portraitRosterEmpty.Visible=_portraitRosterMatches.Length==0;
    }

    private void ChangeRosterPage(int delta)
    {
        _portraitRosterPage+=delta;RefreshPortraitRoster(true);_portraitRosterSearch.GrabFocus();
    }

    private bool ChooseRosterResident(long identity,Simulation? expectedWorld)
    {
        // Never capture a slot in a long-lived button: another person may have
        // inherited it by the time the user activates a search result.
        if(!ReferenceEquals(expectedWorld,Sim)){RefreshPortraitRoster(true);return false;}
        int slot=Sim.Agents.AliveSlots().FirstOrDefault(slot=>Sim.Society.Identity(slot)==identity,-1);
        if(slot<0){RefreshPortraitRoster(true);return false;}
        InspectActor(slot);ShowPortraitRoster(false);_residentPortrait.GrabFocus();return true;
    }

    private async void RevealRosterRow(Button button)
    {
        ulong request=++_portraitRosterLayoutRequest;
        for(int frame=0;frame<2;frame++)
        {
            if(!IsInsideTree()||request!=_portraitRosterLayoutRequest)return;
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        }
        if(IsInsideTree()&&request==_portraitRosterLayoutRequest&&_portraitRoster.IsVisibleInTree()
            &&GodotObject.IsInstanceValid(button)&&button.IsVisibleInTree())_portraitRosterScroll.EnsureControlVisible(button);
    }

    private bool HandlePortraitRosterInput(InputEvent input)
    {
        if(_portraitOverlay?.Visible!=true)return false;
        if(input is InputEventKey {Pressed:true,Echo:false,Keycode:Key.F} search&&(search.CtrlPressed||search.MetaPressed))
        {if(!_portraitRoster.Visible)ShowPortraitRoster(true);_portraitRosterSearch.GrabFocus();_portraitRosterSearch.SelectAll();GetViewport().SetInputAsHandled();return true;}
        if(_portraitRoster?.Visible!=true)return false;
        if(input is InputEventMouseButton {Pressed:true} pointer&&!_portraitRoster.GetGlobalRect().HasPoint(pointer.Position)&&!_portraitRosterToggle.GetGlobalRect().HasPoint(pointer.Position))
        {ShowPortraitRoster(false);return false;}
        if(input is not InputEventKey key||!key.Pressed)return false;
        var focused=GetViewport().GuiGetFocusOwner();
        if(focused==null||!_portraitRoster.IsAncestorOf(focused))_portraitRosterSearch.GrabFocus();
        if(key.Keycode==Key.Escape){ShowPortraitRoster(false,true);GetViewport().SetInputAsHandled();return true;}
        var rows=_portraitRosterEntries.Values.Select(row=>row.Button).ToArray();
        if(key.Keycode==Key.Tab)
        {
            var controls=new List<Control>{_portraitRosterSearch};controls.AddRange(rows);
            if(_portraitRosterPages.Visible){if(!_portraitRosterPagePrevious.Disabled)controls.Add(_portraitRosterPagePrevious);if(!_portraitRosterPageNext.Disabled)controls.Add(_portraitRosterPageNext);}
            controls.Add(_portraitRosterClose);
            int index=controls.IndexOf(GetViewport().GuiGetFocusOwner());int delta=key.ShiftPressed?-1:1;
            controls[(index+delta+controls.Count)%controls.Count].GrabFocus();GetViewport().SetInputAsHandled();return true;
        }
        int current=Array.FindIndex(rows,button=>button==GetViewport().GuiGetFocusOwner());
        if(key.Keycode is Key.Up or Key.Down or Key.Pageup or Key.Pagedown||(current>=0&&key.Keycode is Key.Home or Key.End))
        {
            int step=Math.Max(1,(int)(_portraitRosterScroll.Size.Y/63)-1);
            int next=key.Keycode switch {Key.Home=>0,Key.End=>rows.Length-1,Key.Pageup=>current-step,Key.Pagedown=>current+step,Key.Up=>current<0?rows.Length-1:current-1,_=>current+1};
            if(next<0)_portraitRosterSearch.GrabFocus();else if(rows.Length>0)rows[Math.Clamp(next,0,rows.Length-1)].GrabFocus();
            GetViewport().SetInputAsHandled();return true;
        }
        // Text/caret input goes to LineEdit, Enter/Space to the focused button.
        // Return before the portrait's generic arrow traversal can steal it.
        if(current>=0&&key.Keycode is Key.Left or Key.Right)GetViewport().SetInputAsHandled();
        return true;
    }

    private void ValidatePortraitRoster()
    {
        int original=SelectedSlot;if(original<0)return;
        string digest=StateHash.ComputeDigest(Sim);long originalIdentity=_selectedPersonId;
        bool expanded=_portraitOverlay.Visible;
        try
        {
            ShowPortraitWindow(true);ShowPortraitRoster(true);
            if(!_portraitRosterSearch.HasFocus()||_portraitRosterMatches.Length!=Sim.Agents.LiveCount||_portraitRosterEntries.Count>ResidentRosterPageSize)throw new InvalidOperationException("Resident roster lacks living identities, bounded controls or initial search focus");
            _portraitRosterSearch.Text=Sim.Agents.NameOrOverride(original).ToUpperInvariant();RefreshPortraitRoster(true);
            if(!_portraitRosterEntries.ContainsKey(originalIdentity))throw new InvalidOperationException("Resident search is not case-insensitive");
            string condition=_portraitRosterSearch.Text;
            _Input(new InputEventKey {Keycode=Key.F,CtrlPressed=true,Pressed=true});
            if(_portraitRosterSearch.Text!=condition||!_portraitRosterSearch.HasSelection())throw new InvalidOperationException("Repeated resident search shortcut clears its current condition");
            _portraitRosterSearch.Text="___不存在的居民___";RefreshPortraitRoster(true);
            if(_portraitRosterEntries.Count!=0||!_portraitRosterEmpty.Visible)throw new InvalidOperationException("Empty resident query retains stale actions");
            void KeyInput(Key code)
            {foreach(bool pressed in new[]{true,false})GetViewport().PushInput(new InputEventKey {Keycode=code,PhysicalKeycode=code,Pressed=pressed},true);}
            KeyInput(Key.Escape);
            if(_portraitRoster.Visible||!_portraitOverlay.Visible||!_portraitRosterToggle.HasFocus())throw new InvalidOperationException("Roster Escape closes the portrait or loses return focus");
            ShowPortraitRoster(true);KeyInput(Key.Down);
            if(_selectedPersonId!=originalIdentity||!_portraitRosterEntries.Values.Any(row=>row.Button.HasFocus()))throw new InvalidOperationException("Roster navigation selects before confirmation or loses focus");
            KeyInput(Key.Enter);
            if(_portraitRoster.Visible||!_portraitOverlay.Visible||!_residentPortrait.HasFocus())throw new InvalidOperationException("Roster activation loses the model or its focus");
            long selected=_selectedPersonId;
            if(ChooseRosterResident(long.MaxValue,_portraitRosterWorld)||ChooseRosterResident(selected,null)||_selectedPersonId!=selected)throw new InvalidOperationException("Stale resident result selects a reused slot or another world");
            if(digest!=StateHash.ComputeDigest(Sim))throw new InvalidOperationException("Resident search changes the simulation");
        }
        finally {ShowPortraitRoster(false);InspectActor(original);if(!expanded)ShowPortraitWindow(false);}
        GD.Print("RESIDENT_ROSTER_PASS: living stable identities, search/empty state, real keyboard navigation/activation/Escape, stale rejection and observation purity");
    }
}
