using Godot;

namespace SandBoxSim.Client;
public partial class MainGame
{
    private void StyleNotebook()
    {
        Color paper=new("#222d28"),ink=new("#e1dfd2"),muted=new("#a7b3a5"),accent=new("#bda984");
        _journalPanel.Material=null;
        var surface=HudStyle.Box(paper,3,18);surface.BorderColor=new("#506052");surface.BorderWidthLeft=1;surface.ShadowColor=new Color(0,0,0,.22f);surface.ShadowSize=12;
        _journalPanel.AddThemeStyleboxOverride("panel",surface);_journalPanel.GetChild<HudBevel>(0).Visible=false;
        void Apply(Node node)
        {
            if(node is ResidentPortrait || node.HasMeta("journal_heading"))return;
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
                foreach(string state in new[]{"font_color","font_focus_color","font_hover_color","font_pressed_color"})button.AddThemeColorOverride(state,state=="font_color"?ink:accent);
                button.AddThemeStyleboxOverride("hover",HudStyle.Box(new Color("#39493d"),3,9,false));
                button.AddThemeStyleboxOverride("pressed",HudStyle.Box(new Color("#415440"),3,9,false));
            }
            if(node is VScrollBar scroll)
            {
                scroll.CustomMinimumSize=new(5,0);scroll.AddThemeStyleboxOverride("scroll",HudStyle.Box(new Color(0,0,0,.06f),2,0,false));
                foreach(string state in new[]{"grabber","grabber_highlight","grabber_pressed"})scroll.AddThemeStyleboxOverride(state,HudStyle.Box(new Color("#75866e"),2,0,false));
            }
            if(node is LineEdit field)
            {
                field.AddThemeColorOverride("font_color",ink);field.AddThemeColorOverride("font_placeholder_color",muted);field.AddThemeColorOverride("caret_color",accent);
                var box=HudStyle.Box(new Color("#303e34"),3,9);box.BorderColor=new("#53634f");box.ShadowSize=0;field.AddThemeStyleboxOverride("normal",box);
                var focus=(StyleBoxFlat)box.Duplicate();focus.BorderColor=accent;field.AddThemeStyleboxOverride("focus",focus);
            }
            foreach(Node child in node.GetChildren())Apply(child);
        }
        Apply(_journalPanel);
        var tabbar=_drawer.GetTabBar();tabbar.AddThemeFontSizeOverride("font_size",13);tabbar.AddThemeConstantOverride("icon_max_width",16);tabbar.AddThemeConstantOverride("h_separation",6);
        var separator=HudStyle.Box(new Color("#c2baa6"),0,0,false);separator.ContentMarginTop=1;separator.ContentMarginBottom=1;
        void Separators(Node node){if(node is HSeparator line)line.AddThemeStyleboxOverride("separator",separator);foreach(Node child in node.GetChildren())Separators(child);}
        Separators(_journalPanel);
        _drawer.AddThemeColorOverride("font_selected_color",accent);_drawer.AddThemeColorOverride("font_unselected_color",muted);
        var selected=HudStyle.Box(new Color("#394d3d"),2,6,false);selected.BorderWidthBottom=2;selected.BorderColor=accent;
        _drawer.AddThemeStyleboxOverride("tab_selected",selected);
        _drawer.AddThemeStyleboxOverride("tab_unselected",HudStyle.Box(new Color(0,0,0,0),2,6,false));
        _drawer.AddThemeStyleboxOverride("tab_hovered",HudStyle.Box(new Color("#354638"),2,8,false));
    }
}
