using System;
using Godot;
using SandBoxSim.Core;

namespace SandBoxSim.Client;

public partial class MainGame
{
    private ColorRect _portraitOverlay=null!;
    private PanelContainer _portraitWindow=null!,_portraitHome=null!;
    private VBoxContainer _portraitWindowBody=null!;
    private Label _portraitWindowTitle=null!,_portraitWindowActivity=null!;
    public override void _Input(InputEvent input)
    {
        if(_portraitOverlay?.Visible!=true||input is not InputEventKey {Keycode:Key.Tab,Pressed:true,Echo:false} key)return;
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
            int next=current<0?(key.ShiftPressed?controls.Count-1:0):(current+(key.ShiftPressed?-1:1)+controls.Count)%controls.Count;
            controls[next].GrabFocus();
        }
        GetViewport().SetInputAsHandled();
    }
    private void BuildPortraitWindow(Control overlay)
    {
        _portraitOverlay=new ColorRect {Color=new Color(.015f,.025f,.020f,.68f),Visible=false,MouseFilter=MouseFilterEnum.Stop};
        _portraitOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);overlay.AddChild(_portraitOverlay);
        _portraitOverlay.GuiInput+=input=>{if(input is InputEventMouseButton {ButtonIndex:MouseButton.Left,Pressed:true})ShowPortraitWindow(false);};
        _portraitWindow=new PanelContainer {MouseFilter=MouseFilterEnum.Stop};
        _portraitWindow.AddThemeStyleboxOverride("panel",HudStyle.Frame(20));_portraitOverlay.AddChild(_portraitWindow);
        _portraitWindowBody=new VBoxContainer();_portraitWindowBody.AddThemeConstantOverride("separation",12);_portraitWindow.AddChild(_portraitWindowBody);
        var header=new HBoxContainer();_portraitWindowBody.AddChild(header);
        var identity=new VBoxContainer {SizeFlagsHorizontal=SizeFlags.ExpandFill};identity.AddThemeConstantOverride("separation",3);header.AddChild(identity);
        identity.AddChild(HudStyle.Label("人物观察",11,true));_portraitWindowTitle=HudStyle.Heading("",24);identity.AddChild(_portraitWindowTitle);
        var close=new Godot.Button {Text="返回手记",CustomMinimumSize=new(96,36)};HudStyle.Button(close);header.AddChild(close);close.Pressed+=()=>ShowPortraitWindow(false);
        _portraitWindowActivity=HudStyle.Label("",12,true);_portraitWindowBody.AddChild(_portraitWindowActivity);
        _residentPortrait.ExpandRequested+=()=>ShowPortraitWindow(!_portraitOverlay.Visible);
        FitPortraitWindow(Size);
    }
    private void FitPortraitWindow(Vector2 size)
    {
        if(_portraitWindow==null)return;
        var dimensions=new Vector2(MathF.Min(920,size.X-96),MathF.Min(800,size.Y-120));
        HudStyle.Float(_portraitWindow,new(.5f,.5f),-dimensions*.5f,dimensions);
    }
    private void ShowPortraitWindow(bool visible)
    {
        if(_portraitOverlay==null||visible==_portraitOverlay.Visible)return;
        if(visible)
        {
            if(SelectedSlot<0)return;
            if(_map is WorldView3D world)world.CancelPointerGesture();
            RefreshResidentVitals();
            _residentPortrait.Reparent(_portraitWindowBody,false);
            _residentPortrait.SetExpanded(true);_portraitOverlay.Visible=true;
            _portraitWindowTitle.Text=_residentTitle.Text;
            _portraitWindowActivity.Text=_residentSubtitle.Text+"  ·  "+_residentAction.Text;
            FitPortraitWindow(Size);FadeIn(_portraitWindow);
        }
        else
        {
            _residentPortrait.Reparent(_portraitHome,false);_residentPortrait.SetExpanded(false);_portraitOverlay.Visible=false;
        }
    }
    private void ValidatePortraitWindow()
    {
        if(SelectedSlot<0)return;
        string before=StateHash.ComputeDigest(Sim);
        var state=System.Text.Json.JsonSerializer.Serialize(_residentPortrait.EvaluationState());
        ShowJournal(true);_drawer.CurrentTab=1;ShowPortraitWindow(true);
        if(!_portraitOverlay.Visible||!_residentPortrait.Expanded||_residentPortrait.GetParent()!=_portraitWindowBody)
            throw new InvalidOperationException("Large portrait did not reuse the selected model viewport");
        if(state!=System.Text.Json.JsonSerializer.Serialize(_residentPortrait.EvaluationState()))
            throw new InvalidOperationException("Expanding a portrait reset its framing or orbit");
        for(int i=0;i<9;i++)
        {
            _Input(new InputEventKey {Keycode=Key.Tab,Pressed=true,ShiftPressed=i>5});
            var focused=GetViewport().GuiGetFocusOwner();
            if(focused==null||!_portraitWindow.IsAncestorOf(focused))throw new InvalidOperationException("Portrait keyboard focus escaped to the world");
        }
        _UnhandledKeyInput(new InputEventKey {Keycode=Key.Escape,Pressed=true});
        if(_portraitOverlay.Visible||!_journalPanel.Visible||_residentPortrait.GetParent()!=_portraitHome||_residentPortrait.Expanded)
            throw new InvalidOperationException("Closing portrait did not restore the notebook");
        if(before!=StateHash.ComputeDigest(Sim))throw new InvalidOperationException("Portrait expansion changed simulation state");
        GD.Print("PORTRAIT_WINDOW_PASS: shared model/view, preserved framing, Escape priority, notebook return and observation purity");
    }
}
