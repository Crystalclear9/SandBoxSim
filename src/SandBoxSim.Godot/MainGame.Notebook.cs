using Godot;

namespace SandBoxSim.Client;
public partial class MainGame
{
    private void StyleNotebook()
    {
        Color paper=new("#d7cebc"),ink=new("#343a30"),muted=new("#565f4f"),accent=new("#755b3d");
        _journalPanel.Material=null;
        var surface=HudStyle.Box(paper,3,18);surface.BorderColor=new("#9e9f8c");
        _journalPanel.AddThemeStyleboxOverride("panel",surface);_journalPanel.GetChild<HudBevel>(0).Visible=false;
        void Apply(Node node)
        {
            if(node is ResidentPortrait)return;
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
                button.AddThemeStyleboxOverride("hover",HudStyle.Box(new Color("#c5c2ae"),3,9,false));
                button.AddThemeStyleboxOverride("pressed",HudStyle.Box(new Color("#babba3"),3,9,false));
            }
            if(node is VScrollBar scroll)
            {
                scroll.CustomMinimumSize=new(5,0);scroll.AddThemeStyleboxOverride("scroll",HudStyle.Box(new Color(0,0,0,.06f),2,0,false));
                foreach(string state in new[]{"grabber","grabber_highlight","grabber_pressed"})scroll.AddThemeStyleboxOverride(state,HudStyle.Box(new Color("#a5a08a"),2,0,false));
            }
            if(node is LineEdit field)
            {
                field.AddThemeColorOverride("font_color",ink);field.AddThemeColorOverride("font_placeholder_color",muted);field.AddThemeColorOverride("caret_color",accent);
                var box=HudStyle.Box(new Color("#e3dccb"),3,9);box.BorderColor=new("#acb09b");box.ShadowSize=0;field.AddThemeStyleboxOverride("normal",box);
                var focus=(StyleBoxFlat)box.Duplicate();focus.BorderColor=accent;field.AddThemeStyleboxOverride("focus",focus);
            }
            foreach(Node child in node.GetChildren())Apply(child);
        }
        Apply(_journalPanel);
        _drawer.AddThemeColorOverride("font_selected_color",accent);_drawer.AddThemeColorOverride("font_unselected_color",muted);
        var selected=HudStyle.Box(new Color(0,0,0,0),0,8,false);selected.BorderWidthBottom=2;selected.BorderColor=accent;
        _drawer.AddThemeStyleboxOverride("tab_selected",selected);
        _drawer.AddThemeStyleboxOverride("tab_hovered",HudStyle.Box(new Color("#c9c5b1"),2,8,false));
    }
}
