using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core;

namespace SandBoxSim.Client;

public partial class MainGame
{
    private PanelContainer _cameraOptions=null!;
    private Button _cameraOptionsToggle=null!;
    private readonly Dictionary<string,Button> _cameraPresets=new();
    private TextureRect _contextGlyph=null!;
    private bool _worldPauseKeyHeld;

    private void BuildWorldControls(Control overlay)
    {
        _timePanel=Surface(overlay,new(0,1),new(20,-88),new(300,72),10);
        _timePanel.Name="WorldTimeControls";
        _timePanel.AddThemeStyleboxOverride("panel",HudStyle.Frame(10));
        var timeBox=new VBoxContainer();timeBox.AddThemeConstantOverride("separation",5);_timePanel.AddChild(timeBox);
        var timeHeading=new HBoxContainer();timeBox.AddChild(timeHeading);
        _dayLabel=HudStyle.Label("第 1 天",13);_dayLabel.SizeFlagsHorizontal=SizeFlags.ExpandFill;timeHeading.AddChild(_dayLabel);
        _playStateLabel=HudStyle.Label("",11,true);_playStateLabel.SizeFlagsVertical=SizeFlags.ShrinkCenter;timeHeading.AddChild(_playStateLabel);
        var timeRow=new HBoxContainer();timeRow.AddThemeConstantOverride("separation",2);timeBox.AddChild(timeRow);
        foreach(int speed in new[]{0,1,4,8,16,32})
        {
            int chosen=speed;
            var button=ActionButton(timeRow,speed==0?"暂停":speed+"×",()=>SetSpeed(chosen==0?(_speed==0?_resumeSpeed:0):chosen));
            button.Name=speed==0?"WorldPauseResume":"WorldSpeed"+speed;
            button.ToggleMode=true;button.CustomMinimumSize=new(speed==0?52:38,36);
            button.SizeFlagsHorizontal=SizeFlags.ExpandFill;button.AddThemeFontSizeOverride("font_size",13);
            _speedButtons[speed]=button;
        }
        SetSpeed(_speed);

        _cameraPanel=Surface(overlay,new(1,1),new(-296,-88),new(280,72),10);
        _cameraPanel.Name="WorldCameraControls";
        _cameraPanel.AddThemeStyleboxOverride("panel",HudStyle.Frame(10));
        var cameraBox=new VBoxContainer();cameraBox.AddThemeConstantOverride("separation",5);_cameraPanel.AddChild(cameraBox);
        var cameraHeading=new HBoxContainer();cameraBox.AddChild(cameraHeading);
        _cameraReadout=HudStyle.Label("观察视角",11,true);_cameraReadout.SizeFlagsHorizontal=SizeFlags.ExpandFill;cameraHeading.AddChild(_cameraReadout);
        cameraHeading.AddChild(new HudCompass {View=(WorldView3D)_map});
        var cameraRow=new HBoxContainer();cameraRow.AddThemeConstantOverride("separation",6);cameraBox.AddChild(cameraRow);
        _cameraOptionsToggle=ActionButton(cameraRow,"视角与操作  ▴",()=>ShowCameraOptions(!_cameraOptions.Visible));
        _cameraOptionsToggle.Name="WorldCameraOptionsToggle";_cameraOptionsToggle.ToggleMode=true;
        _cameraOptionsToggle.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        _cameraOptionsToggle.TooltipText="展开镜头预设与鼠标操作说明 · Esc 收起";
        _followCameraButton=ActionButton(cameraRow,"跟随",()=>
        {
            var world=(WorldView3D)_map;
            if(world.IsFollowing(SelectedSlot))world.StopFollowing();else if(SelectedSlot>=0)_map.Follow(SelectedSlot);
            RefreshPresentationControls();
        });
        _followCameraButton.Name="WorldFollowSelection";_followCameraButton.ToggleMode=true;
        _followCameraButton.CustomMinimumSize=new(72,36);

        // This transient palette is last in the overlay's draw/input order. It
        // never resizes the world or pushes a notebook's contents off screen.
        _cameraOptions=new PanelContainer {Name="WorldCameraOptions",Visible=false};
        _cameraOptions.AddThemeStyleboxOverride("panel",HudStyle.Frame(14));overlay.AddChild(_cameraOptions);
        var options=new VBoxContainer();options.AddThemeConstantOverride("separation",10);_cameraOptions.AddChild(options);
        var title=new HBoxContainer();options.AddChild(title);
        var heading=HudStyle.Heading("镜头与操作",16);heading.SizeFlagsHorizontal=SizeFlags.ExpandFill;title.AddChild(heading);
        var close=ActionButton(title,"×",()=>ShowCameraOptions(false,true));HudStyle.QuietButton(close);
        close.TooltipText="收起镜头面板 · Esc";close.CustomMinimumSize=new(32,28);
        var presets=new GridContainer {Columns=2};presets.AddThemeConstantOverride("h_separation",6);presets.AddThemeConstantOverride("v_separation",6);options.AddChild(presets);
        foreach(string view in new[]{"全景","斜视","俯视","近景"})
        {
            string selected=view;
            var button=ActionButton(presets,view,()=>
            {
                if(selected=="全景")_map.Center();else ((WorldView3D)_map).SetPerspective(selected);
                RefreshPresentationControls();
            });
            button.Name="CameraPreset"+_cameraPresets.Count;button.ToggleMode=true;
            button.SizeFlagsHorizontal=SizeFlags.ExpandFill;button.CustomMinimumSize=new(110,36);
            button.TooltipText=view switch {"全景"=>"回到整个世界 · F", "俯视"=>"从上方观察；保留当前距离", "近景"=>"贴近当前观察点", _=>"恢复斜视与中景距离"};
            _cameraPresets[view]=button;
        }
        options.AddChild(new HSeparator());
        foreach(var (key,hint) in new[]{("左键拖动","平移地图"),("右键拖动","旋转与俯仰"),("滚轮","缩放到指针位置")})
        {
            var row=new HBoxContainer();row.AddThemeConstantOverride("separation",10);options.AddChild(row);
            var keycap=HudStyle.Keycap(key);keycap.CustomMinimumSize=new(86,25);row.AddChild(keycap);
            var label=HudStyle.Label(hint,12,true);label.SizeFlagsHorizontal=SizeFlags.ExpandFill;label.VerticalAlignment=VerticalAlignment.Center;row.AddChild(label);
        }
        var note=HudStyle.Label("创造时：Alt + 左键拖动平移",11,true);options.AddChild(note);
    }

    private void ShowCameraOptions(bool visible,bool restoreFocus=false)
    {
        if(_cameraOptions==null)return;
        if(visible)
        {
            if(_immersive||_portraitOverlay?.Visible==true)return;
            ((WorldView3D)_map).CancelPointerGesture();
            _hudOverlay.MoveChild(_cameraOptions,_hudOverlay.GetChildCount()-1);
        }
        _cameraOptions.Visible=visible;_cameraOptionsToggle.SetPressedNoSignal(visible);
        _cameraOptionsToggle.Text=visible?"视角与操作  ▾":"视角与操作  ▴";
        if(visible){FitCameraOptions(Size);FadeIn(_cameraOptions);_cameraPresets["全景"].GrabFocus();}
        else if(restoreFocus&&_cameraOptionsToggle.IsVisibleInTree())_cameraOptionsToggle.GrabFocus();
    }

    private void FitCameraOptions(Vector2 size)
    {
        if(_cameraOptions==null)return;
        var dimensions=_cameraOptions.GetCombinedMinimumSize().Max(new Vector2(280,302));
        HudStyle.Float(_cameraOptions,new(1,1),new(-16-dimensions.X,_cameraPanel.OffsetTop-8-dimensions.Y),dimensions);
    }

    private void HandleCameraOptionsInput(InputEvent input)
    {
        if(_cameraOptions?.Visible!=true)return;
        if(input is InputEventMouseButton {Pressed:true} button&&
            !_cameraOptions.GetGlobalRect().HasPoint(button.Position)&&!_cameraOptionsToggle.GetGlobalRect().HasPoint(button.Position))
            ShowCameraOptions(false);
        // Dismissal does not consume the press. The world, notebook or tool
        // underneath receives that same intentional click through normal GUI input.
    }

    private bool HandleWorldControlKey(InputEvent input)
    {
        if(input is not InputEventKey {Keycode:Key.Space} key)return false;
        var focused=GetViewport().GuiGetFocusOwner();
        bool worldControls=focused!=null&&(_cameraOptions.IsAncestorOf(focused)||_cameraPanel.IsAncestorOf(focused)||_timePanel.IsAncestorOf(focused));
        if(!key.Pressed&&_worldPauseKeyHeld)
        {_worldPauseKeyHeld=false;GetViewport().SetInputAsHandled();return true;}
        if(!worldControls||!key.Pressed||key.CtrlPressed||key.MetaPressed||key.AltPressed)return false;
        // Space pauses the world while using its time/camera tools. Enter still
        // activates the focused button; text fields and settings keep their
        // normal Space behaviour. Consume release too, so BaseButton never
        // receives half of a keyboard click.
        if(!key.Echo){SetSpeed(_speed==0?_resumeSpeed:0);_worldPauseKeyHeld=true;}
        GetViewport().SetInputAsHandled();return true;
    }

    private void RefreshCameraPresets()
    {
        var world=(WorldView3D)_map;
        foreach(var pair in _cameraPresets)pair.Value.SetPressedNoSignal(world.MatchesCameraPreset(pair.Key));
    }

    private void ValidateWorldControls()
    {
        string digest=StateHash.ComputeDigest(Sim);
        int speed=_speed,resume=_resumeSpeed;
        try
        {
            SetSpeed(8);_speedButtons[0].EmitSignal(BaseButton.SignalName.Pressed);
            if(_speed!=0||_speedButtons[0].Text!="继续")throw new InvalidOperationException("Pause action has no resume affordance");
            _speedButtons[0].EmitSignal(BaseButton.SignalName.Pressed);
            if(_speed!=8||_speedButtons[0].Text!="暂停")throw new InvalidOperationException("Resume lost the prior speed");
            ShowCameraOptions(true);
            if(!_cameraOptions.Visible||!_cameraOptionsToggle.ButtonPressed)throw new InvalidOperationException("Camera options have no expanded feedback");
            foreach(var pair in _cameraPresets)
            {
                pair.Value.EmitSignal(BaseButton.SignalName.Pressed);
                if(!pair.Value.ButtonPressed)throw new InvalidOperationException("Camera preset disagrees with actual view");
            }
            void Space()
            {
                foreach(bool pressed in new[]{true,false})
                    GetViewport().PushInput(new InputEventKey {Keycode=Key.Space,PhysicalKeycode=Key.Space,Pressed=pressed},true);
            }
            Space();
            if(_speed!=0||!_cameraPresets["近景"].ButtonPressed||!_cameraOptions.Visible)
                throw new InvalidOperationException("Focused camera button consumes Space or changes the view");
            Space();
            if(_speed!=8)throw new InvalidOperationException("Camera-focused Space cannot resume the world");
            _UnhandledKeyInput(new InputEventKey {Keycode=Key.Escape,Pressed=true});
            if(_cameraOptions.Visible||!_cameraOptionsToggle.HasFocus())throw new InvalidOperationException("Escape did not dismiss camera controls and return focus");
            Space();
            if(_speed!=0||_cameraOptions.Visible)throw new InvalidOperationException("Returned camera trigger consumes Space");
            ShowCameraOptions(true);ShowJournal(true);
            if(_cameraOptions.Visible)throw new InvalidOperationException("Camera palette remains over a newly opened notebook");
            if(digest!=StateHash.ComputeDigest(Sim))throw new InvalidOperationException("Camera controls changed simulation state");
        }
        finally {ShowCameraOptions(false);SetSpeed(speed);_resumeSpeed=resume;}
        GD.Print("WORLD_CONTROLS_PASS: pause/resume speed, real routed Space with camera focus, actual presets, palette dismissal/focus and read-only state");
    }
}
