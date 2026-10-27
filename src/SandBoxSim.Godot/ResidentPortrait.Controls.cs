using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentPortrait
{
    private readonly List<Label> _studioHeadings=new();
    private readonly Dictionary<float,Button> _angleButtons=new();
    private VBoxContainer _angleGroup=null!;
    private HBoxContainer _zoomControls=null!;
    private Label _distanceReadout=null!;
    private Button _zoomIn=null!,_zoomOut=null!;
    private HSlider _distanceSlider=null!;
    private Button _orbitButton=null!;
    private bool _orbiting;

    private void BuildStudioControls()
    {
        _controlPlate=new PanelContainer {Name="PortraitCameraControls"};
        _controlPlate.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        _controlPlate.OffsetLeft=10;_controlPlate.OffsetRight=-10;_controlPlate.OffsetBottom=-10;
        _controlPlate.AddThemeStyleboxOverride("panel",HudStyle.Box(new Color(HudStyle.Surface,.98f),6,8));AddChild(_controlPlate);
        var groups=new HBoxContainer();groups.AddThemeConstantOverride("separation",14);_controlPlate.AddChild(groups);
        VBoxContainer Group(string title)
        {
            var group=new VBoxContainer {SizeFlagsHorizontal=SizeFlags.ExpandFill};group.AddThemeConstantOverride("separation",2);groups.AddChild(group);
            var heading=HudStyle.Label(title,11,true);heading.MouseFilter=MouseFilterEnum.Ignore;group.AddChild(heading);_studioHeadings.Add(heading);
            return group;
        }
        HBoxContainer Buttons(VBoxContainer group)
        {var row=new HBoxContainer();row.AddThemeConstantOverride("separation",3);group.AddChild(row);return row;}
        Button StudioButton(HBoxContainer row,string title,string hint,Action action,int width=42)
        {
            var button=new Button {Text=title,CustomMinimumSize=new(width,36)};HudStyle.Button(button);
            button.AddThemeFontSizeOverride("font_size",12);button.TooltipText=hint;row.AddChild(button);button.Pressed+=action;return button;
        }
        var range=Buttons(Group("观察范围"));
        foreach(var (mode,title) in new[]{(Framing.Face,"面部"),(Framing.Hands,"手部"),(Framing.Body,"全身")})
        {
            var button=StudioButton(range,title,"聚焦"+title+" · 保留当前旋转角度",()=>SetFraming(mode));
            button.ToggleMode=true;_frameButtons[mode]=button;
        }
        _frameButtons[Framing.Face].SetPressedNoSignal(true);
        _angleGroup=Group("观察角度");var angles=Buttons(_angleGroup);
        foreach(var (title,angle) in new[]{("正面",0f),("三刻",-.65f),("背面",MathF.PI)})
        {
            var button=StudioButton(angles,title,"切换观察角度，保留聚焦和缩放",()=>SetAngle(angle),46);
            button.ToggleMode=true;_angleButtons[angle]=button;
        }
        _orbitButton=StudioButton(angles,"环视","缓慢环视当前居民；再次点击、拖动或选择角度即可停止",()=>SetOrbiting(!_orbiting),46);
        _orbitButton.Name="PortraitOrbit";_orbitButton.ToggleMode=true;
        var lens=Buttons(Group("镜头距离 · 近 → 远"));_zoomControls=new HBoxContainer();_zoomControls.AddThemeConstantOverride("separation",3);lens.AddChild(_zoomControls);
        _zoomOut=StudioButton(_zoomControls,"−","拉远模型",()=>ZoomBy(.10f),32);
        _zoomIn=StudioButton(_zoomControls,"+","拉近模型 · 滚轮缩放，Shift + 滚轮精调",()=>ZoomBy(-.10f),32);
        _distanceSlider=new HSlider {Name="PortraitDistanceSlider",MinValue=0,MaxValue=1,Step=.005,Value=DistancePosition(_distance),
            CustomMinimumSize=new(88,36),SizeFlagsVertical=SizeFlags.ShrinkCenter,FocusMode=FocusModeEnum.All,
            TooltipText="镜头距离 · 左侧靠近，右侧拉远；方向键微调，Home / End 到达边界",AccessibilityName="人物观察镜头距离"};
        _zoomControls.AddChild(_distanceSlider);
        _distanceSlider.ValueChanged+=position=>SetDistance(.5f*MathF.Pow(9,(float)position));
        _distanceReadout=HudStyle.Label("",12,true);_distanceReadout.Name="PortraitCameraDistance";
        _distanceReadout.AddThemeFontOverride("font",HudStyle.NumericFont);_distanceReadout.CustomMinimumSize=new(64,0);
        _distanceReadout.VerticalAlignment=VerticalAlignment.Center;_distanceReadout.HorizontalAlignment=HorizontalAlignment.Center;
        _distanceReadout.MouseFilter=MouseFilterEnum.Ignore;_distanceReadout.TooltipText="当前镜头目标距离（米）";_zoomControls.AddChild(_distanceReadout);
        StudioButton(lens,"复位","恢复当前构图 · 也可双击画面",ResetView);
        _expandButton=StudioButton(lens,"放大","打开独立人物观察窗",()=>ExpandRequested?.Invoke());
    }
    private void SetStudioControlsExpanded(bool expanded)
    {
        foreach(var heading in _studioHeadings)heading.Visible=expanded;
        _angleGroup.Visible=expanded;_zoomControls.Visible=expanded;
        _controlPlate.OffsetTop=expanded?-86:-60;
        UpdateStudioReadout();
    }
    private void UpdateStudioReadout()
    {
        if(_distanceReadout==null)return;
        _distanceReadout.Text=_distance.ToString("0.00",CultureInfo.InvariantCulture)+" m";
        _distanceSlider.SetValueNoSignal(DistancePosition(_distance));
        _orbitButton.Disabled=!HudStyle.MotionEnabled;
        _orbitButton.TooltipText=HudStyle.MotionEnabled?"缓慢环视当前居民；再次点击、拖动或选择角度即可停止":"启用柔和界面动效后可使用环视";
        _zoomOut.Disabled=_distance>=4.4999f;_zoomIn.Disabled=_distance<=.5001f;
        UpdateAngleReadout();
    }
    private void UpdateAngleReadout()
    {foreach(var pair in _angleButtons)pair.Value.SetPressedNoSignal(!_orbiting&&MathF.Abs(Mathf.Wrap(_yaw-pair.Key,-MathF.PI,MathF.PI))<.025f&&MathF.Abs(_pitch)<.025f);}
    private static float DistancePosition(float distance)=>MathF.Log(Math.Clamp(distance,.5f,4.5f)/.5f)/MathF.Log(9);
    private void SetDistance(float distance)
    {
        if(!float.IsFinite(distance))return;
        _distance=Math.Clamp(distance,.5f,4.5f);UpdateStudioReadout();Aim();
    }
    private void SetOrbiting(bool enabled)
    {
        _orbiting=enabled&&Expanded&&HudStyle.MotionEnabled&&IsVisibleInTree();
        if(_orbitButton==null)return;
        _orbitButton.SetPressedNoSignal(_orbiting);_orbitButton.Text=_orbiting?"停止":"环视";_orbitButton.Disabled=!HudStyle.MotionEnabled;
        UpdateAngleReadout();
    }
    private void AdvanceOrbit(float delta)
    {
        if(!_orbiting)return;
        if(!Expanded||!IsVisibleInTree()||!HudStyle.MotionEnabled){SetOrbiting(false);return;}
        // View-only, opt-in motion. It does not advance the paused simulation
        // or integrate a large angle after a stall/focus interruption.
        _yaw=Mathf.Wrap(_yaw+Math.Clamp(delta,0,.05f)*.20f,-MathF.PI,MathF.PI);
    }
    internal void ValidateStudioControls()
    {
        bool expanded=Expanded,motion=HudStyle.MotionEnabled;float yaw=_yaw,pitch=_pitch,distance=_distance;
        var framing=_framing;
        try
        {
            HudStyle.MotionEnabled=true;SetExpanded(true);
            _distanceSlider.Value=.5;
            if(MathF.Abs(_distance-1.5f)>.0001f)throw new InvalidOperationException("Log distance slider has no matching camera distance");
            ZoomBy(-.10f);
            if(Math.Abs(_distanceSlider.Value-DistancePosition(_distance))>.0051)throw new InvalidOperationException("Wheel/buttons leave the distance slider stale");
            _distanceSlider.GrabFocus();float before=_distance;
            foreach(bool pressed in new[]{true,false})GetViewport().PushInput(new InputEventKey {Keycode=Key.Right,PhysicalKeycode=Key.Right,Pressed=pressed},true);
            if(_distance<=before||!_distanceSlider.HasFocus())throw new InvalidOperationException("Portrait modal steals slider keyboard input");
            SetOrbiting(true);float start=_yaw;AdvanceOrbit(.02f);
            if(!_orbiting||_yaw==start||!_orbitButton.ButtonPressed)throw new InvalidOperationException("Opt-in portrait orbit does not advance its actual view");
            SetAngle(0);if(_orbiting||_orbitButton.ButtonPressed)throw new InvalidOperationException("Angle selection leaves orbit running");
            SetOrbiting(true);_GuiInput(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=true});
            if(_orbiting)throw new InvalidOperationException("Manual portrait drag competes with orbit");
            _Input(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=false});
            SetOrbiting(true);_Notification((int)NotificationApplicationFocusOut);
            if(_orbiting)throw new InvalidOperationException("Unfocused portrait keeps orbiting");
            SetOrbiting(true);SetExpanded(false);SetExpanded(true);
            if(_orbiting)throw new InvalidOperationException("Reopening portrait starts unsolicited orbit");
            SetOrbiting(true);HudStyle.MotionEnabled=false;AdvanceOrbit(.02f);
            if(_orbiting||!_orbitButton.Disabled)throw new InvalidOperationException("Reduced motion retains portrait orbit");
        }
        finally
        {
            HudStyle.MotionEnabled=motion;SetOrbiting(false);SetFraming(framing);_yaw=yaw;_pitch=pitch;_distance=distance;
            SetExpanded(expanded);UpdateStudioReadout();SettleMotion();
        }
        GD.Print("PORTRAIT_CONTINUOUS_PASS: linked logarithmic distance, native keyboard slider, opt-in orbit, drag/preset/close/focus/reduced-motion stops");
    }
}
