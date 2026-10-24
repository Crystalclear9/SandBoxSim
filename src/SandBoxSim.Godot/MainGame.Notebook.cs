using Godot;

namespace SandBoxSim.Client;
public partial class MainGame
{
    private void StyleNotebook()
    {
        Color paper=HudStyle.Paper,ink=HudStyle.PaperInk,muted=HudStyle.PaperMuted,accent=HudStyle.PaperAccent;
        _journalPanel.Material=null;
        var surface=HudStyle.Box(paper,12,18);surface.BorderColor=HudStyle.PaperLine;surface.BorderWidthLeft=1;surface.ShadowColor=new Color(0,0,0,.24f);surface.ShadowSize=16;
        _journalPanel.AddThemeStyleboxOverride("panel",surface);_journalPanel.GetChild<HudBevel>(0).Visible=true;
        void Apply(Node node)
        {
            if(node is ResidentPortrait or WorldMiniMap or StatisticsView)return;
            if(node is PanelContainer panel&&panel!=_journalPanel&&panel.GetThemeStylebox("panel") is StyleBoxFlat existing)
            {
                var box=(StyleBoxFlat)existing.Duplicate();
                if(box.BgColor.A>0)box.BgColor=HudStyle.PaperWash;
                box.BorderColor=HudStyle.PaperLine;box.ShadowSize=0;panel.AddThemeStyleboxOverride("panel",box);
            }
            if(node is ProgressBar bar)bar.AddThemeStyleboxOverride("background",HudStyle.Box(HudStyle.PaperLine,1,0,false));
            if(node is Label label)
            {
                bool soft=label.GetThemeColor("font_color")==HudStyle.Muted;
                label.AddThemeColorOverride("font_color",soft?muted:ink);
            }
            if(node is RichTextLabel text)text.AddThemeColorOverride("default_color",ink);
            if(node is Godot.Button button)
            {
                button.SetMeta("notebook",true);
                var focus=HudStyle.Box(new Color(0,0,0,0),3,9);focus.BorderColor=accent;focus.ShadowSize=0;button.AddThemeStyleboxOverride("focus",focus);
                button.AddThemeColorOverride("font_disabled_color",muted);
                foreach(string state in new[]{"font_color","font_focus_color","font_hover_color","font_pressed_color","icon_normal_color","icon_hover_color","icon_pressed_color"})button.AddThemeColorOverride(state,state is "font_color" or "icon_normal_color"?ink:accent);
                button.AddThemeStyleboxOverride("hover",HudStyle.Box(HudStyle.PaperWash,3,9,false));
                button.AddThemeStyleboxOverride("pressed",HudStyle.Box(HudStyle.PaperWash.Darkened(.06f),3,9,false));
                button.AddThemeStyleboxOverride("hover_pressed",HudStyle.Box(HudStyle.PaperWash.Darkened(.03f),3,9,false));
            }
            if(node is VScrollBar scroll)
            {
                scroll.CustomMinimumSize=new(5,0);scroll.AddThemeStyleboxOverride("scroll",HudStyle.Box(new Color(0,0,0,.06f),2,0,false));
                foreach(string state in new[]{"grabber","grabber_highlight","grabber_pressed"})scroll.AddThemeStyleboxOverride(state,HudStyle.Box(HudStyle.PaperMuted,2,0,false));
            }
            if(node is LineEdit field)
            {
                field.AddThemeColorOverride("font_color",ink);field.AddThemeColorOverride("font_placeholder_color",muted);field.AddThemeColorOverride("caret_color",accent);
                var box=HudStyle.Box(HudStyle.PaperWash,3,9);box.BorderColor=HudStyle.PaperLine;box.ShadowSize=0;field.AddThemeStyleboxOverride("normal",box);
                var focus=(StyleBoxFlat)box.Duplicate();focus.BorderColor=accent;field.AddThemeStyleboxOverride("focus",focus);
            }
            foreach(Node child in node.GetChildren())Apply(child);
        }
        Apply(_journalPanel);
        var tabbar=_drawer.GetTabBar();tabbar.AddThemeFontSizeOverride("font_size",14);tabbar.AddThemeConstantOverride("icon_max_width",16);tabbar.AddThemeConstantOverride("h_separation",7);
        var separator=HudStyle.Box(HudStyle.PaperLine,0,0,false);separator.ContentMarginTop=1;separator.ContentMarginBottom=1;
        void Separators(Node node){if(node is HSeparator line)line.AddThemeStyleboxOverride("separator",separator);foreach(Node child in node.GetChildren())Separators(child);}
        Separators(_journalPanel);
        _drawer.AddThemeColorOverride("font_selected_color",ink);_drawer.AddThemeColorOverride("font_unselected_color",muted);
        // Use the shared tab styles; equal padding keeps hover hit regions stable.
    }
}
