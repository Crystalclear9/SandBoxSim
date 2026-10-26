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
        var lens=Buttons(Group("镜头"));_zoomControls=new HBoxContainer();_zoomControls.AddThemeConstantOverride("separation",3);lens.AddChild(_zoomControls);
        _zoomOut=StudioButton(_zoomControls,"−","拉远模型",()=>ZoomBy(.10f),32);
        _zoomIn=StudioButton(_zoomControls,"+","拉近模型 · 滚轮缩放，Shift + 滚轮精调",()=>ZoomBy(-.10f),32);
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
        _zoomOut.Disabled=_distance>=4.4999f;_zoomIn.Disabled=_distance<=.5001f;
        foreach(var pair in _angleButtons)pair.Value.SetPressedNoSignal(MathF.Abs(Mathf.Wrap(_yaw-pair.Key,-MathF.PI,MathF.PI))<.025f&&MathF.Abs(_pitch)<.025f);
    }
}
