using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SandBoxSim.Core;
using SandBoxSim.Core.Systems;

namespace SandBoxSim.Client;

public partial class MainGame
{
    private readonly List<PlayerTool> _recentTools=new();
    private readonly Button[] _recentToolButtons=new Button[4];
    private readonly PlayerTool[] _recentToolChoices=new PlayerTool[4];
    private Label _recentToolHint=null!;
    private HBoxContainer _recentToolsRow=null!;
    private MenuButton _recentToolsMenu=null!;
    private static readonly string[] ToolCategories={"生命","生态","地貌","资源","灾害","祝福"};
    private void BuildRecentTools(VBoxContainer parent,HBoxContainer header)
    {
        _recentToolsMenu=new MenuButton {Name="RecentToolsMenu",Text="最近 ▾",CustomMinimumSize=new(76,32),Disabled=true};
        HudStyle.QuietButton(_recentToolsMenu,12);header.AddChild(_recentToolsMenu);header.MoveChild(_recentToolsMenu,header.GetChildCount()-2);
        _recentToolsMenu.GetPopup().AboutToPopup+=PopulateRecentMenu;
        _recentToolsMenu.GetPopup().IdPressed+=id=>ActivateRecentChoice((PlayerTool)id);
        var row=_recentToolsRow=new HBoxContainer {Name="RecentTools",CustomMinimumSize=new(0,32)};row.AddThemeConstantOverride("separation",5);parent.AddChild(row);
        var label=HudStyle.Label("最近",11,true);label.VerticalAlignment=VerticalAlignment.Center;row.AddChild(label);
        _recentToolHint=HudStyle.Label("最近选用的工具会留在这里",11,true);_recentToolHint.VerticalAlignment=VerticalAlignment.Center;row.AddChild(_recentToolHint);
        for(int i=0;i<_recentToolButtons.Length;i++)
        {
            int index=i;
            var button=new Button {Name="RecentTool"+i,ToggleMode=true,Visible=false,CustomMinimumSize=new(0,32),
                SizeFlagsHorizontal=SizeFlags.ExpandFill,TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis,
                ExpandIcon=true,ClipText=true};
            HudStyle.QuietButton(button,12);button.AddThemeConstantOverride("icon_max_width",14);button.AddThemeConstantOverride("h_separation",4);
            button.Pressed+=()=>ActivateRecentTool(index);row.AddChild(button);_recentToolButtons[i]=button;
        }
    }
    private static string ShortToolName(PlayerTool tool)=>ToolNames[(int)tool].Replace("创造", "").Replace("添加", "").Replace("生长", "").Replace("播撒", "").Replace("栽植", "").Replace("恢复", "").Replace("地形", "").Replace("画笔", "");
    private void RememberTool(PlayerTool tool)
    {
        if(tool!=PlayerTool.Inspect)
        {
            _recentTools.Remove(tool);_recentTools.Insert(0,tool);
            if(_recentTools.Count>4)_recentTools.RemoveAt(4);
        }
        if(_recentToolHint==null)return;
        // Keep existing chip hit areas stable while the palette is open.
        // Reusing a chip updates recency, but cannot turn a second click on
        // that same position into an unintended different tool.
        bool newChoice=tool!=PlayerTool.Inspect&&!_recentToolChoices.Take(_recentToolButtons.Count(b=>b?.Visible==true)).Contains(tool);
        RefreshRecentTools(newChoice||!_dockPanel.Visible);
    }
    private void RefreshRecentTools(bool reorder=false)
    {
        if(_recentToolHint==null)return;
        _recentToolsMenu.Disabled=_recentTools.Count==0;_recentToolsMenu.TooltipText=_recentTools.Count==0?"选用工具后可在这里快速切换":"最近选用的 "+_recentTools.Count+" 种工具";
        _recentToolHint.Visible=_recentTools.Count==0;
        for(int i=0;i<_recentToolButtons.Length;i++)
        {
            var button=_recentToolButtons[i];button.Visible=i<_recentTools.Count;if(!button.Visible)continue;
            if(reorder)_recentToolChoices[i]=_recentTools[i];
            var choice=_recentToolChoices[i];button.Text=ShortToolName(choice);button.Icon=ToolGlyphs.For(choice);
            button.TooltipText=ToolNames[(int)choice]+" · 切换到该工具及所属分类";button.AccessibilityName="最近工具："+ToolNames[(int)choice];
            button.SetPressedNoSignal(choice==Tool);
        }
    }
    private void ActivateRecentTool(int index)
    {
        if(index<0||index>=_recentToolButtons.Length||!_recentToolButtons[index].Visible)return;
        ActivateRecentChoice(_recentToolChoices[index]);
    }
    private void ActivateRecentChoice(PlayerTool chosen)
    {
        if(!_recentTools.Contains(chosen))return;
        string category=CategoryTools(_category).Contains(chosen)?_category:ToolCategories.First(category=>CategoryTools(category).Contains(chosen));
        SetCategory(category);_toolSearch.ReleaseFocus();SelectTool(chosen);
    }
    private void PopulateRecentMenu()
    {
        var popup=_recentToolsMenu.GetPopup();popup.Clear();
        foreach(var tool in _recentTools)
        {
            popup.AddIconCheckItem(ToolGlyphs.For(tool),ToolNames[(int)tool],(int)tool);
            popup.SetItemChecked(popup.ItemCount-1,tool==Tool);popup.SetItemIconMaxWidth(popup.ItemCount-1,18);
        }
    }
    private void FitRecentTools(Vector2 size)
    {
        if(_recentToolsRow==null)return;
        _recentToolsRow.Visible=size.Y>=900;_recentToolsMenu.Visible=!_recentToolsRow.Visible;
    }
    private void ValidateRecentTools()
    {
        string digest=StateHash.ComputeDigest(Sim);var saved=_recentTools.ToArray();var choices=(PlayerTool[])_recentToolChoices.Clone();
        var selected=Tool;string category=_category,query=_toolSearch.Text;bool visible=_dockPanel.Visible;
        try
        {
            _recentTools.Clear();RefreshRecentTools(true);ShowTools(true);
            if(!_recentToolHint.Visible||_recentToolButtons.Any(button=>button.Visible))throw new InvalidOperationException("Empty recent-tool strip retains stale actions");
            SetCategory("生态");SelectTool(PlayerTool.BerrySeeds);SelectTool(PlayerTool.MeadowSeeds);
            var positions=(PlayerTool[])_recentToolChoices.Clone();SelectTool(PlayerTool.BerrySeeds);
            if(!positions.SequenceEqual(_recentToolChoices)||_recentTools[0]!=PlayerTool.BerrySeeds)throw new InvalidOperationException("Recent reuse changes click targets or loses recency");
            _recentToolButtons[0].EmitSignal(BaseButton.SignalName.Pressed);
            if(Tool!=PlayerTool.MeadowSeeds||_category!="生态")throw new InvalidOperationException("Recent chip does not activate its actual tool");
            SelectTool(PlayerTool.River);SelectTool(PlayerTool.Wood);SelectTool(PlayerTool.Wolf);
            if(_recentTools.Count!=4||_recentTools.Distinct().Count()!=4||_recentTools.Contains(PlayerTool.Inspect))throw new InvalidOperationException("Recent tools exceed their unique bounded session history");
            ShowTools(false);ShowTools(true);
            if(!_recentToolChoices.SequenceEqual(_recentTools))throw new InvalidOperationException("Reopening tool palette does not restore recency order");
            PopulateRecentMenu();if(_recentToolsMenu.GetPopup().ItemCount!=4)throw new InvalidOperationException("Compact recent menu loses its bounded choices");
            int river=Array.IndexOf(_recentToolChoices,PlayerTool.River);_toolSearch.Text="没有匹配";PopulateTools();ActivateRecentTool(river);
            if(Tool!=PlayerTool.River||_category!="地貌"||_toolSearch.Text.Length!=0)throw new InvalidOperationException("Recent switch retains a stale category/search");
            if(digest!=StateHash.ComputeDigest(Sim))throw new InvalidOperationException("Recent tools change the world without a brush stroke");
        }
        finally
        {
            ShowTools(visible);SetCategory(category);SelectTool(selected);_toolSearch.Text=query;PopulateTools();
            _recentTools.Clear();_recentTools.AddRange(saved);Array.Copy(choices,_recentToolChoices,choices.Length);RefreshRecentTools(true);
        }
        GD.Print("RECENT_TOOLS_PASS: bounded unique history, stable chip hit areas, real activation, category/search restore and unchanged simulation");
    }
}
